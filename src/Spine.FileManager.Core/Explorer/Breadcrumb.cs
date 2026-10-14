using Spine.FileManager.Storage;

namespace Spine.FileManager.Explorer;

/// <summary>The folders from a location's root down to the location itself, the segments of a breadcrumb bar.</summary>
public static class Breadcrumb
{
    public static IReadOnlyList<StorageLocation> Segments(IStorageProvider provider, StorageLocation location)
    {
        var segments = new List<StorageLocation> { location };
        while (provider.GetParent(segments[^1]) is { } parent)
            segments.Add(parent);
        segments.Reverse();
        return segments;
    }

    /// <summary>How many segments, from the root down, a bar showing <paramref name="shown"/> can keep for <paramref name="next"/>.</summary>
    public static int Shared(IReadOnlyList<StorageLocation> shown, IReadOnlyList<StorageLocation> next)
    {
        var kept = 0;
        while (kept < shown.Count && kept < next.Count && shown[kept].Equals(next[kept]))
            kept++;
        return kept;
    }

    /// <summary>A folder's subfolders for a segment's drop-down: folders only, hidden ones left out unless asked, in natural order.</summary>
    public static async Task<IReadOnlyList<StorageItem>> SubfoldersAsync(IStorageProvider provider, StorageLocation folder, bool includeHidden, int limit = 500, CancellationToken cancellationToken = default)
    {
        var folders = new List<StorageItem>();
        await foreach (var item in provider.EnumerateAsync(folder, cancellationToken))
            if (item.IsFolder && (includeHidden || !item.IsHidden))
            {
                folders.Add(item);
                if (folders.Count == limit)
                    break;
            }
        var names = NaturalComparer.Current;
        folders.Sort((a, b) => names.Compare(a.DisplayName, b.DisplayName));
        return folders;
    }
}
