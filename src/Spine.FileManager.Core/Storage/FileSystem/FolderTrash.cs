namespace Spine.FileManager.Storage.FileSystem;

/// <summary>What is in the trash: its top-level items and their size, files in folders included.</summary>
public sealed record TrashContents(int Items, long Bytes);

/// <summary>A trash kept as a plain folder, as the Mac keeps ~/.Trash.</summary>
public static class FolderTrash
{
    private const string FinderInfo = ".DS_Store";

    /// <summary>The trash's items and size, or null when the folder cannot be read (on the Mac: no Full Disk Access).</summary>
    public static TrashContents? Measure(string folder, CancellationToken cancellationToken = default)
    {
        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(folder);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return null;
        }
        var items = 0;
        long bytes = 0;
        foreach (var entry in entries)
        {
            if (Path.GetFileName(entry) == FinderInfo)
                continue;
            items++;
            bytes += Size(new FileInfo(entry), cancellationToken);
        }
        return new TrashContents(items, bytes);
    }

    /// <summary>Deletes everything in the trash for good; returns the names it could not delete (locked, in use).</summary>
    public static IReadOnlyList<string> Empty(string folder, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var entries = Directory.GetFileSystemEntries(folder);
        var failed = new List<string>();
        for (var i = 0; i < entries.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];
            try
            {
                // A link is removed itself, never what it points to.
                var info = new FileInfo(entry);
                if (info.Attributes.HasFlag(FileAttributes.Directory) && info.LinkTarget is null)
                    Directory.Delete(entry, recursive: true);
                else
                    File.Delete(entry);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                if (Path.GetFileName(entry) != FinderInfo)
                    failed.Add(Path.GetFileName(entry));
            }
            progress?.Report((double)(i + 1) / entries.Length);
        }
        return failed;
    }

    // Links count as themselves: a link to a large folder takes no room in the trash.
    private static long Size(FileSystemInfo entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.LinkTarget is not null)
            return 0;
        if (!entry.Attributes.HasFlag(FileAttributes.Directory))
            return (entry as FileInfo)?.Length ?? new FileInfo(entry.FullName).Length;
        try
        {
            return new DirectoryInfo(entry.FullName).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true })
                .Sum(child => Size(child, cancellationToken));
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return 0;
        }
    }
}
