namespace Spine.FileManager.Services;

/// <summary>Which volume a location is on, for a drop's copy-or-move: the longest mount point its path starts with.</summary>
public static class Volumes
{
    private static (DateTime Read, string[] Roots) _mounts = (DateTime.MinValue, []);

    public static string? Of(StorageLocation location)
    {
        if (location.NativePath is not { } path)
            return location.Provider.Value;
        // Read again at most every few seconds: a drag asks for every item on every move of the pointer.
        if (DateTime.UtcNow - _mounts.Read > TimeSpan.FromSeconds(5))
            _mounts = (DateTime.UtcNow, [.. DriveInfo.GetDrives().Select(d => d.RootDirectory.FullName)]);
        return _mounts.Roots.Where(root => path.StartsWith(root, StringComparison.Ordinal)
            && (path.Length == root.Length || root.EndsWith(Path.DirectorySeparatorChar) || path[root.Length] == Path.DirectorySeparatorChar)).MaxBy(root => root.Length);
    }
}
