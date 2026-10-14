namespace Spine.FileManager.Services;

/// <summary>The system's full preview of files, with paging between them: Quick Look on Apple platforms.</summary>
public static class QuickLookPreview
{
    public static bool IsSupported => OperatingSystem.IsMacCatalyst() || OperatingSystem.IsIOS();

    public static void Show(IReadOnlyList<string> paths, int index, Page? from)
    {
#if MACCATALYST || IOS
        if (paths.Count == 0 || from?.Handler?.MauiContext is null)
            return;
        var controller = new global::QuickLook.QLPreviewController { DataSource = new Items(paths), CurrentPreviewItemIndex = Math.Clamp(index, 0, paths.Count - 1) };
        var presenter = Platform.GetCurrentUIViewController();
        presenter?.PresentViewController(controller, true, null);
#endif
    }

#if MACCATALYST || IOS
    private sealed class Items(IReadOnlyList<string> paths) : global::QuickLook.QLPreviewControllerDataSource
    {
        public override nint PreviewItemCount(global::QuickLook.QLPreviewController controller) => paths.Count;

        public override global::QuickLook.IQLPreviewItem GetPreviewItem(global::QuickLook.QLPreviewController controller, nint index) =>
            new Item(Foundation.NSUrl.FromFilename(paths[(int)index]));
    }

    private sealed class Item(Foundation.NSUrl url) : Foundation.NSObject, global::QuickLook.IQLPreviewItem
    {
        public Foundation.NSUrl PreviewItemUrl => url;

        public string PreviewItemTitle => url.LastPathComponent ?? "";
    }
#endif
}
