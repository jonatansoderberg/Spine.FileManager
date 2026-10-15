using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Spine.FileManager.Storage.FileSystem;

/// <summary>
/// Storage reached through paths and System.IO: the local disks on Windows and the Mac, an app's own folders on iOS
/// and Android, and shared storage on Android with All files access. Item ids are full paths.
/// </summary>
public sealed class FileSystemStorageProvider(IFileSystemPlatform platform) : IStorageProvider
{
    public static readonly StorageProviderId ProviderId = new("local");

    private const StorageCapabilities Full =
        StorageCapabilities.List | StorageCapabilities.Read | StorageCapabilities.Write | StorageCapabilities.CreateFolder |
        StorageCapabilities.Rename | StorageCapabilities.Delete | StorageCapabilities.NativeCopy | StorageCapabilities.NativeMove |
        StorageCapabilities.RandomAccess | StorageCapabilities.NativePath;

    // Folders where a write was refused; their capabilities drop the modifying flags until the app restarts.
    private readonly ConcurrentDictionary<string, byte> _readOnly = new(PathComparer);

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public StorageProviderId Id => ProviderId;

    public static StorageLocation Location(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length > 1 && full.EndsWith(Path.DirectorySeparatorChar) && Path.GetPathRoot(full) != full)
            full = full.TrimEnd(Path.DirectorySeparatorChar);
        return new(ProviderId, full, full, full);
    }

    public Task<IReadOnlyList<StorageRoot>> GetRootsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<StorageRoot>>(() => [.. platform.GetRoots().Select(r => new StorageRoot(Location(r.Path), r.Name, r.Kind))], cancellationToken);

    public ValueTask<StorageCapabilities> GetCapabilitiesAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        var capabilities = Full;
        if (platform.CanWatch)
            capabilities |= StorageCapabilities.Watch;
        if (platform.CanTrash(location.ItemId))
            capabilities |= StorageCapabilities.Trash;
        if (_readOnly.ContainsKey(location.ItemId))
            capabilities &= ~StorageCapabilities.Modify;
        return ValueTask.FromResult(capabilities);
    }

    public Task<StorageItem> GetItemAsync(StorageLocation location, CancellationToken cancellationToken = default) => Run(location, () =>
    {
        var path = location.ItemId;
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException(null, path);
        return Named(Item(info));
    }, cancellationToken);

    public StorageLocation? GetParent(StorageLocation location) =>
        Path.GetDirectoryName(location.ItemId) is { Length: > 0 } parent ? Location(parent) : null;

    public async IAsyncEnumerable<StorageItem> EnumerateAsync(StorageLocation folder, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The listing runs on a pool thread so a slow disk, share or TCC prompt never holds the caller.
        var channel = Channel.CreateBounded<StorageItem>(new BoundedChannelOptions(1024) { SingleReader = true, SingleWriter = true });
        var producer = Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(folder.ItemId))
                    throw File.Exists(folder.ItemId)
                        ? new StorageException(StorageErrorKind.NotSupported, folder, $"{folder.DisplayPath} is a file, not a folder.")
                        : new DirectoryNotFoundException(folder.ItemId);
                var entries = new FileSystemEnumerable<StorageItem>(folder.ItemId, (ref FileSystemEntry entry) => Named(Item(ref entry)),
                    new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, ReturnSpecialDirectories = false });
                foreach (var item in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    while (!channel.Writer.TryWrite(item))
                        if (!channel.Writer.WaitToWriteAsync(cancellationToken).AsTask().GetAwaiter().GetResult())
                            return;
                }
                channel.Writer.Complete();
            }
            catch (Exception e)
            {
                channel.Writer.Complete(Translate(e, folder));
            }
        }, cancellationToken);

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
        await producer;
    }

    public Task<Stream> OpenReadAsync(StorageLocation file, CancellationToken cancellationToken = default) => Run(file, () =>
        (Stream)new FileStream(file.ItemId, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true), cancellationToken);

    public Task<Stream> OpenWriteAsync(StorageLocation folder, string name, long? length = null, bool overwrite = false, CancellationToken cancellationToken = default) =>
        Run(folder, () =>
        {
            var path = Child(folder, name);
            var stream = new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            if (length is > 0)
                stream.SetLength(length.Value);
            return (Stream)stream;
        }, cancellationToken, writes: true);

    public Task<StorageItem> CreateFolderAsync(StorageLocation folder, string name, CancellationToken cancellationToken = default) => Run(folder, () =>
    {
        var path = Child(folder, name);
        if (Path.Exists(path))
            throw new StorageException(StorageErrorKind.AlreadyExists, Location(path), $"{name} already exists.");
        return Item(Directory.CreateDirectory(path));
    }, cancellationToken, writes: true);

    public Task<StorageItem> RenameAsync(StorageLocation item, string newName, CancellationToken cancellationToken = default) => Run(item, () =>
    {
        var source = item.ItemId;
        var target = Child(Location(Path.GetDirectoryName(source)!), newName);
        // A change of case only is a rename on case-insensitive volumes too; every other existing name is a conflict.
        if (Path.Exists(target) && !string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new StorageException(StorageErrorKind.AlreadyExists, Location(target), $"{newName} already exists.");
        if (Directory.Exists(source))
            Directory.Move(source, target);
        else
            File.Move(source, target);
        return Item(Directory.Exists(target) ? new DirectoryInfo(target) : new FileInfo(target));
    }, cancellationToken, writes: true);

    public Task DeleteAsync(StorageLocation item, StorageDeleteMode mode, CancellationToken cancellationToken = default) => Run(item, async () =>
    {
        var path = item.ItemId;
        if (!Path.Exists(path))
            throw new FileNotFoundException(null, path);
        if (mode == StorageDeleteMode.Trash)
        {
            if (!platform.CanTrash(path))
                throw new StorageException(StorageErrorKind.NotSupported, item, $"{item.DisplayPath} cannot be moved to the trash.");
            await platform.MoveToTrashAsync(path, cancellationToken);
        }
        else if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else
            File.Delete(path);
        return true;
    }, cancellationToken, writes: true);

    public Task<StorageItem> CopyAsync(StorageLocation item, StorageLocation targetFolder, string? newName = null, CancellationToken cancellationToken = default) =>
        Run(targetFolder, () =>
        {
            var target = Child(targetFolder, newName ?? Path.GetFileName(item.ItemId));
            if (Path.Exists(target))
                throw new StorageException(StorageErrorKind.AlreadyExists, Location(target), $"{Path.GetFileName(target)} already exists.");
            if (Directory.Exists(item.ItemId))
            {
                CopyFolder(item.ItemId, target, cancellationToken);
                return Item(new DirectoryInfo(target));
            }
            File.Copy(item.ItemId, target);
            return Item(new FileInfo(target));
        }, cancellationToken, writes: true);

    public Task<StorageItem> MoveAsync(StorageLocation item, StorageLocation targetFolder, string? newName = null, CancellationToken cancellationToken = default) =>
        Run(targetFolder, () =>
        {
            var target = Child(targetFolder, newName ?? Path.GetFileName(item.ItemId));
            if (Path.Exists(target))
                throw new StorageException(StorageErrorKind.AlreadyExists, Location(target), $"{Path.GetFileName(target)} already exists.");
            if (IsInside(target, item.ItemId))
                throw new StorageException(StorageErrorKind.NotSupported, item, "A folder cannot be moved into itself.");
            if (Directory.Exists(item.ItemId))
            {
                Directory.Move(item.ItemId, target);
                return Item(new DirectoryInfo(target));
            }
            File.Move(item.ItemId, target);
            return Item(new FileInfo(target));
        }, cancellationToken, writes: true);

    public IDisposable Watch(StorageLocation folder, Action<StorageChange> changed)
    {
        if (!platform.CanWatch)
            throw new StorageException(StorageErrorKind.NotSupported, folder, "Watching is not supported here.");
        var watcher = new FileSystemWatcher(folder.ItemId)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
        };
        watcher.Created += (_, e) => changed(new(StorageChangeKind.Created, Location(e.FullPath)));
        watcher.Deleted += (_, e) => changed(new(StorageChangeKind.Deleted, Location(e.FullPath)));
        watcher.Changed += (_, e) => changed(new(StorageChangeKind.Changed, Location(e.FullPath)));
        watcher.Renamed += (_, e) => changed(new(StorageChangeKind.Renamed, Location(e.FullPath), Location(e.OldFullPath)));
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private static string Child(StorageLocation folder, string name)
    {
        if (StorageNames.Problem(name) is { } problem)
            throw new StorageException(StorageErrorKind.InvalidName, folder, $"'{name}' is not a valid name ({problem}).");
        return Path.Combine(folder.ItemId, name);
    }

    private static bool IsInside(string path, string folder) =>
        path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void CopyFolder(string source, string target, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        foreach (var folder in Directory.EnumerateDirectories(source))
            CopyFolder(folder, Path.Combine(target, Path.GetFileName(folder)), cancellationToken);
    }

    private StorageItem Named(StorageItem item)
    {
        if (!item.IsFolder)
            return item;
        if (platform.IsPackage(item.Location.ItemId))
            item = item with { IsPackage = true };
        return platform.FolderDisplayName(item.Location.ItemId, item.Name) is { } shown && shown != item.Name ? item with { DisplayName = shown } : item;
    }

    private static StorageItem Item(ref FileSystemEntry entry)
    {
        var path = entry.ToFullPath();
        var name = entry.FileName.ToString();
        return new StorageItem(new(ProviderId, path, path, path), name, entry.IsDirectory ? StorageItemKind.Folder : StorageItemKind.File)
        {
            Size = entry.IsDirectory ? null : entry.Length,
            Modified = entry.LastWriteTimeUtc,
            Created = entry.CreationTimeUtc,
            IsHidden = entry.IsHidden || name.StartsWith('.'),
            IsReadOnly = (entry.Attributes & FileAttributes.ReadOnly) != 0,
            IsLink = (entry.Attributes & FileAttributes.ReparsePoint) != 0,
        };
    }

    private static StorageItem Item(FileSystemInfo info)
    {
        var location = Location(info.FullName);
        return new StorageItem(location, info.Name, info is DirectoryInfo ? StorageItemKind.Folder : StorageItemKind.File)
        {
            Size = info is FileInfo file ? file.Length : null,
            Modified = info.LastWriteTimeUtc,
            Created = info.CreationTimeUtc,
            IsHidden = (info.Attributes & FileAttributes.Hidden) != 0 || info.Name.StartsWith('.'),
            IsReadOnly = (info.Attributes & FileAttributes.ReadOnly) != 0,
            IsLink = info.LinkTarget is not null,
        };
    }

    private Task<T> Run<T>(StorageLocation location, Func<T> action, CancellationToken cancellationToken, bool writes = false) =>
        Run(location, () => Task.FromResult(action()), cancellationToken, writes);

    private async Task<T> Run<T>(StorageLocation location, Func<Task<T>> action, CancellationToken cancellationToken, bool writes = false)
    {
        try
        {
            return await Task.Run(action, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var translated = Translate(e, location);
            if (writes && translated is StorageException { Kind: StorageErrorKind.AccessDenied })
                _readOnly.TryAdd(Directory.Exists(location.ItemId) ? location.ItemId : Path.GetDirectoryName(location.ItemId) ?? location.ItemId, 0);
            throw translated;
        }
    }

    // Windows reports Win32 errors as 0x8007xxxx, Unix the errno itself; the same number means different things.
    private static bool IsError(IOException e, int[] windows, int[] unix) =>
        (OperatingSystem.IsWindows() ? windows : unix).Contains(e.HResult & 0xFFFF);

    private static Exception Translate(Exception e, StorageLocation location) => e switch
    {
        StorageException or OperationCanceledException => e,
        FileNotFoundException or DirectoryNotFoundException => new StorageException(StorageErrorKind.NotFound, location, $"{location.DisplayPath} was not found.", e),
        UnauthorizedAccessException => new StorageException(StorageErrorKind.AccessDenied, location, $"No access to {location.DisplayPath}.", e),
        DriveNotFoundException => new StorageException(StorageErrorKind.Unavailable, location, $"{location.DisplayPath} is not available.", e),
        PathTooLongException => new StorageException(StorageErrorKind.InvalidName, location, e.Message, e),
        PlatformNotSupportedException or NotSupportedException => new StorageException(StorageErrorKind.NotSupported, location, e.Message, e),
        IOException io when IsError(io, windows: [80, 183], unix: [17]) => new StorageException(StorageErrorKind.AlreadyExists, location, e.Message, e),
        IOException io when IsError(io, windows: [21, 53, 121], unix: [60, 110, 112]) => new StorageException(StorageErrorKind.Unavailable, location, e.Message, e),
        _ => new StorageException(StorageErrorKind.Other, location, e.Message, e),
    };
}
