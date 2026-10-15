using System.Runtime.CompilerServices;
using Spine.FileManager.Storage;

namespace Spine.FileManager.Explorer;

/// <summary>
/// Searches a folder and everything below it by name, as the filter matches names (<see cref="NameFilter"/>). The
/// tree is walked breadth first, so the nearest matches come first; a folder that cannot be read is passed by, links
/// and packages (an app, a photo library) are not entered, and the search stops at <c>limit</c> matches.
/// </summary>
public static class FileSearch
{
    private static readonly HashSet<string> Packages = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "photoslibrary", "bundle", "framework", "plugin", "kext", "xcodeproj", "xcworkspace", "musiclibrary", "tvlibrary",
    };

    public static async IAsyncEnumerable<StorageItem> SearchAsync(IStorageProvider provider, StorageLocation root, string query, bool includeHidden,
        int limit = 10_000, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            yield break;
        var folders = new Queue<StorageLocation>([root]);
        var found = 0;
        while (folders.TryDequeue(out var folder))
        {
            var children = new List<StorageItem>();
            try
            {
                await foreach (var item in provider.EnumerateAsync(folder, cancellationToken))
                    children.Add(item);
            }
            catch (StorageException)
            {
                continue;
            }
            foreach (var item in children)
            {
                if (item.IsHidden && !includeHidden)
                    continue;
                if (NameFilter.Matches(item.DisplayName, query))
                {
                    yield return item;
                    if (++found >= limit)
                        yield break;
                }
                if (item.IsFolder && !item.IsLink && !Packages.Contains(Path.GetExtension(item.Name).TrimStart('.')))
                    folders.Enqueue(item.Location);
            }
        }
    }
}
