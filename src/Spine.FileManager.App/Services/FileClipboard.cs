namespace Spine.FileManager.Services;

/// <summary>
/// Items copied or cut in the explorer. On the Mac they also go to the system pasteboard as file URLs, so they paste
/// in Finder, and files copied in Finder paste here.
/// </summary>
public static class FileClipboard
{
    private static IReadOnlyList<StorageLocation> _items = [];
#if MACCATALYST
    private static nint _changeCount = -1;
#endif

    public static bool IsCut { get; private set; }

    public static void Set(IReadOnlyList<StorageLocation> items, bool cut)
    {
        _items = items;
        IsCut = cut;
#if MACCATALYST
        var urls = items.Where(i => i.NativePath is not null).Select(i => Foundation.NSUrl.FromFilename(i.NativePath!)).ToArray();
        if (urls.Length > 0)
        {
            UIKit.UIPasteboard.General.Urls = urls;
            _changeCount = UIKit.UIPasteboard.General.ChangeCount;
        }
#endif
    }

    /// <summary>What a paste would bring: the app's own items, or files another app put on the pasteboard since.</summary>
    public static IReadOnlyList<StorageLocation> Items
    {
        get
        {
#if MACCATALYST
            var pasteboard = UIKit.UIPasteboard.General;
            if (pasteboard.ChangeCount != _changeCount && pasteboard.Urls is { Length: > 0 } urls && urls.All(u => u.IsFileUrl))
                return [.. urls.Select(u => Storage.FileSystem.FileSystemStorageProvider.Location(u.Path!))];
#endif
            return _items;
        }
    }

    public static bool HasItems => Items.Count > 0;

    /// <summary>After a cut has been pasted, the items are gone from where they were; the clipboard empties.</summary>
    public static void Clear()
    {
        _items = [];
        IsCut = false;
    }
}
