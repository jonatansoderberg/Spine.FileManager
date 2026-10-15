namespace Spine.FileManager.Storage.FileSystem;

/// <summary>What the file-system provider needs from the OS beyond System.IO.</summary>
public interface IFileSystemPlatform
{
    /// <summary>The sidebar's places: known folders that exist, then volumes.</summary>
    IReadOnlyList<(string Path, string Name, StorageRootKind Kind)> GetRoots();

    /// <summary>False where FileSystemWatcher does not work (iOS).</summary>
    bool CanWatch { get; }

    /// <summary>Whether <see cref="MoveToTrashAsync"/> works for <paramref name="path"/>.</summary>
    bool CanTrash(string path);

    Task MoveToTrashAsync(string path, CancellationToken cancellationToken);

    /// <summary>The trash as a folder the explorer can show (the Mac's ~/.Trash), or null where it is not one.</summary>
    string? TrashFolder { get; }

    /// <summary>What is in the trash, or null when there is none or the app may not look.</summary>
    Task<TrashContents?> MeasureTrashAsync(CancellationToken cancellationToken);

    /// <summary>Deletes what is in the trash for good; returns the names it could not delete.</summary>
    Task<IReadOnlyList<string>> EmptyTrashAsync(IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>Shows the trash in the system's own file manager, for a trash that is not a folder.</summary>
    void ShowTrash();

    /// <summary>The name to show for a folder when it differs from its file name (localised system folders), or null.</summary>
    string? FolderDisplayName(string path, string name);
}

/// <summary>Known folders through <see cref="Environment.SpecialFolder"/> and fixed volumes through <see cref="DriveInfo"/>; no trash.</summary>
public class DefaultFileSystemPlatform : IFileSystemPlatform
{
    public virtual IReadOnlyList<(string Path, string Name, StorageRootKind Kind)> GetRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<(string, string, StorageRootKind)>();
        void Add(string path, StorageRootKind kind)
        {
            if (path.Length > 0 && Directory.Exists(path) && !roots.Any(r => r.Item1 == path))
                roots.Add((path, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : path, kind));
        }

        Add(home, StorageRootKind.Home);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), StorageRootKind.Desktop);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), StorageRootKind.Documents);
        Add(Path.Combine(home, "Downloads"), StorageRootKind.Downloads);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), StorageRootKind.Pictures);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), StorageRootKind.Music);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), StorageRootKind.Videos);
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Network))
            Add(drive.RootDirectory.FullName, drive.DriveType switch
            {
                DriveType.Removable => StorageRootKind.RemovableVolume,
                DriveType.Network => StorageRootKind.NetworkVolume,
                _ => StorageRootKind.Volume,
            });
        return roots;
    }

    public virtual bool CanWatch => !OperatingSystem.IsIOS() && !OperatingSystem.IsTvOS();

    public virtual bool CanTrash(string path) => false;

    public virtual Task MoveToTrashAsync(string path, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException("This platform has no trash.");

    public virtual string? FolderDisplayName(string path, string name) => null;

    public virtual string? TrashFolder => null;

    public virtual Task<TrashContents?> MeasureTrashAsync(CancellationToken cancellationToken) =>
        TrashFolder is { } folder ? Task.Run(() => FolderTrash.Measure(folder, cancellationToken), cancellationToken) : Task.FromResult<TrashContents?>(null);

    public virtual Task<IReadOnlyList<string>> EmptyTrashAsync(IProgress<double>? progress, CancellationToken cancellationToken) =>
        TrashFolder is { } folder
            ? Task.Run(() => FolderTrash.Empty(folder, progress, cancellationToken), cancellationToken)
            : throw new PlatformNotSupportedException("This platform has no trash.");

    public virtual void ShowTrash()
    {
    }
}
