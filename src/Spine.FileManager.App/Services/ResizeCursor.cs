namespace Spine.FileManager.Services;

/// <summary>
/// The left-right resize pointer over a grip, as macOS shows over a split view's divider; MAUI has no cursors. On the
/// Mac it is AppKit's NSCursor, reached through the Objective-C runtime (Mac Catalyst has no binding for it).
/// </summary>
public static class ResizeCursor
{
#if MACCATALYST
    public static void Show() => Set("resizeLeftRightCursor");

    public static void Hide() => Set("arrowCursor");

    private static void Set(string cursor)
    {
        var handle = Send(ObjCRuntime.Class.GetHandle("NSCursor"), cursor);
        if (handle != IntPtr.Zero)
            Send(handle, "set");
    }

    private static IntPtr Send(IntPtr receiver, string selector) => objc_msgSend(receiver, ObjCRuntime.Selector.GetHandle(selector));

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
#else
    public static void Show() { }

    public static void Hide() { }
#endif
}
