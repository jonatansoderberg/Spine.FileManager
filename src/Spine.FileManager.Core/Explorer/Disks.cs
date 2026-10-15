using Spine.FileManager.Storage;
using Spine.FileManager.Storage.FileSystem;

namespace Spine.FileManager.Explorer;

public enum DiskKind { Internal, External, Network }

/// <summary>A disk on this device and how full it is, for Home's overview.</summary>
public sealed record Disk(StorageLocation Location, string Name, DiskKind Kind, long Total, long Free)
{
    public long Used => Total - Free;

    public double UsedFraction => Total > 0 ? (double)Used / Total : 0;
}

/// <summary>
/// The disks a person thinks of as theirs: the start disk and what is mounted beside it (external drives, network
/// shares, disk images); the system's own volumes (/System/Volumes, /private, devfs, autofs) are left out.
/// </summary>
public static class Disks
{
    /// <param name="displayName">The platform's name for a disk's root ("Macintosh HD"), or null for the default.</param>
    /// <param name="available">The platform's free space for a root, when it counts what the system can free (purgeable space on APFS).</param>
    /// <param name="isVisible">Whether the platform shows the volume to people; hidden ones (Recovery) are left out.</param>
    public static IReadOnlyList<Disk> List(Func<string, string?>? displayName = null, Func<string, long?>? available = null, Func<string, bool>? isVisible = null)
    {
        var disks = new List<Disk>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.TotalSize <= 0 || !IsUsers(drive))
                    continue;
                var root = drive.RootDirectory.FullName;
                if (isVisible?.Invoke(root) == false)
                    continue;
                var name = displayName?.Invoke(root) ?? (OperatingSystem.IsWindows()
                    ? $"{(string.IsNullOrEmpty(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel)} ({root.TrimEnd('\\')})"
                    : root == "/" ? "/" : Path.GetFileName(root.TrimEnd('/')));
                disks.Add(new Disk(FileSystemStorageProvider.Location(root), name, Kind(drive), drive.TotalSize, available?.Invoke(root) ?? drive.AvailableFreeSpace));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        // The start disk first, then by name.
        return [.. disks.OrderBy(d => d.Location.ItemId is "/" or @"C:\" ? 0 : 1).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static bool IsUsers(DriveInfo drive)
    {
        if (drive.DriveType is DriveType.Ram or DriveType.NoRootDirectory or DriveType.Unknown)
            return false;
        if (OperatingSystem.IsWindows())
            return true;
        var root = drive.RootDirectory.FullName.TrimEnd('/');
        return root.Length == 0 || root.StartsWith("/Volumes/", StringComparison.Ordinal) || root.StartsWith("/media/", StringComparison.Ordinal) || root.StartsWith("/mnt/", StringComparison.Ordinal);
    }

    private static DiskKind Kind(DriveInfo drive) => drive.DriveType switch
    {
        DriveType.Network => DiskKind.Network,
        DriveType.Removable or DriveType.CDRom => DiskKind.External,
        _ => drive.RootDirectory.FullName.StartsWith("/Volumes/", StringComparison.Ordinal) ? DiskKind.External : DiskKind.Internal,
    };
}
