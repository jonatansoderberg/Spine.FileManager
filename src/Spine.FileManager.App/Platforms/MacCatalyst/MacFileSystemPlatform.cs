using System.Runtime.InteropServices;
using Foundation;
using Spine.FileManager.Storage;
using Spine.FileManager.Storage.FileSystem;

namespace Spine.FileManager.Storage;

/// <summary>Finder's places, mounted volumes and the Trash on the Mac.</summary>
public sealed class MacFileSystemPlatform : DefaultFileSystemPlatform
{
    public override IReadOnlyList<(string Path, string Name, StorageRootKind Kind)> GetRoots()
    {
        var files = NSFileManager.DefaultManager;
        var home = UserHome();
        var roots = new List<(string, string, StorageRootKind)>();
        void Add(string path, StorageRootKind kind, string? name = null)
        {
            if (Directory.Exists(path) && !roots.Any(r => r.Item1 == path))
                roots.Add((path, name ?? files.DisplayName(path), kind));
        }

        Add(home, StorageRootKind.Home);
        Add($"{home}/Desktop", StorageRootKind.Desktop);
        Add($"{home}/Documents", StorageRootKind.Documents);
        Add($"{home}/Downloads", StorageRootKind.Downloads);
        Add($"{home}/Pictures", StorageRootKind.Pictures);
        Add($"{home}/Music", StorageRootKind.Music);
        Add($"{home}/Movies", StorageRootKind.Videos);
        Add("/Applications", StorageRootKind.Other);
        Add($"{home}/Library/Mobile Documents/com~apple~CloudDocs", StorageRootKind.Cloud, "iCloud Drive");
        foreach (var provider in Folders($"{home}/Library/CloudStorage"))
            Add(provider, StorageRootKind.Cloud);

        NSString[] keys = [NSUrl.VolumeLocalizedNameKey, NSUrl.VolumeIsRemovableKey, NSUrl.VolumeIsLocalKey, NSUrl.VolumeIsRootFileSystemKey];
        foreach (var volume in files.GetMountedVolumes(keys, NSVolumeEnumerationOptions.SkipHiddenVolumes) ?? [])
        {
            if (volume.Path is not { } path || Flag(volume, NSUrl.VolumeIsRootFileSystemKey) == true)
                continue;
            var kind = Flag(volume, NSUrl.VolumeIsLocalKey) == false ? StorageRootKind.NetworkVolume
                : Flag(volume, NSUrl.VolumeIsRemovableKey) == true ? StorageRootKind.RemovableVolume
                : StorageRootKind.Volume;
            volume.TryGetResource(NSUrl.VolumeLocalizedNameKey, out var name);
            Add(path, kind, name?.ToString());
        }
        return roots;
    }

    public override bool CanWatch => true;

    // Network volumes have no Trash; a delete there is permanent and the app asks first.
    public override bool CanTrash(string path) => Flag(NSUrl.FromFilename(path), NSUrl.VolumeIsLocalKey) != false;

    public override Task MoveToTrashAsync(string path, CancellationToken cancellationToken)
    {
        if (!NSFileManager.DefaultManager.TrashItem(NSUrl.FromFilename(path), out _, out var error))
            throw error.Code == (int)NSCocoaError.FileWriteNoPermission
                ? new UnauthorizedAccessException(error.LocalizedDescription)
                : new IOException(error.LocalizedDescription);
        return Task.CompletedTask;
    }

    // Only folders: for files DisplayName hides the extension when Finder is set to, which a file manager must not.
    public override string? FolderDisplayName(string path, string name) =>
        name.EndsWith(".localized", StringComparison.Ordinal) || name is "Desktop" or "Documents" or "Downloads" or "Movies" or "Music" or "Pictures" or "Public" or "Applications" or "Library"
            ? NSFileManager.DefaultManager.DisplayName(path)
            : null;

    static bool? Flag(NSUrl url, NSString key) => url.TryGetResource(key, out var value) && value is NSNumber number ? number.BoolValue : null;

    static IEnumerable<string> Folders(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // HOME is the container in a sandboxed build; the password database always has the user's real home.
    static string UserHome()
    {
        var entry = getpwuid(getuid());
        // struct passwd on 64-bit Darwin: pw_dir follows name, passwd, uid, gid, change, class and gecos.
        return entry != 0 && Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(entry, 48)) is { Length: > 0 } home
            ? home
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    [DllImport("libc")]
    static extern uint getuid();

    [DllImport("libc")]
    static extern nint getpwuid(uint uid);
}
