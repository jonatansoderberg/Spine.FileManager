using System.Runtime.InteropServices;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Spine.FileManager;

/// <summary>
/// The Mac's window material: the desktop shows through the window, blurred, as through Finder's sidebar. Mac Catalyst
/// has no API for it, so an AppKit NSVisualEffectView goes behind the window's UIKit content and the window stops being
/// opaque; the views that should show it are transparent (the sidebar), the rest keep their own background. The
/// NSWindow is found the way Spine finds it: as the object of NSWindowDidBecomeKeyNotification.
/// </summary>
public static class WindowMaterial
{
    // NSVisualEffectMaterial.Sidebar, NSVisualEffectBlendingMode.BehindWindow, NSVisualEffectState.Active,
    // NSViewWidthSizable | NSViewHeightSizable, NSWindowBelow.
    private const nint Sidebar = 7, BehindWindow = 0, Active = 1, Below = -1;
    private const nuint FillsSuperview = 2 | 16;

    private static readonly HashSet<IntPtr> Applied = [];

    public static bool IsOn => Preferences.Get("window.material", true);

    public static void Start()
    {
        if (!IsOn)
            return;
        NSNotificationCenter.DefaultCenter.AddObserver(new NSString("NSWindowDidBecomeKeyNotification"), notification =>
        {
            if (notification.Object is not { } window || !Applied.Add(window.Handle))
                return;
            Apply(window.Handle);
        });
    }

    private static void Apply(IntPtr window)
    {
        try
        {
            var content = Send(window, "contentView");
            var frame = Send(content, "superview");
            if (content == IntPtr.Zero || frame == IntPtr.Zero)
                return;
            var effect = Send(Send(Class.GetHandle("NSVisualEffectView"), "alloc"), "initWithFrame:", Bounds(frame, Selector.GetHandle("bounds")));
            SendNint(effect, "setMaterial:", Sidebar);
            SendNint(effect, "setBlendingMode:", BehindWindow);
            SendNint(effect, "setState:", Active);
            SendNuint(effect, "setAutoresizingMask:", FillsSuperview);
            AddBelow(frame, Selector.GetHandle("addSubview:positioned:relativeTo:"), effect, Below, content);
            SendBool(window, "setOpaque:", false);
            SendPtr(window, "setBackgroundColor:", Send(Class.GetHandle("NSColor"), "clearColor"));
            // UIKit's window and the views MAUI puts under the page are opaque by default.
            foreach (var scene in UIApplication.SharedApplication.ConnectedScenes.ToArray<UIScene>().OfType<UIWindowScene>())
                foreach (var uiWindow in scene.Windows)
                    for (UIView? view = uiWindow; view is not null && view is not Microsoft.Maui.Platform.LayoutView; view = view.Subviews.FirstOrDefault())
                        view.BackgroundColor = UIColor.Clear;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowMaterial] {e.GetType().Name}: {e.Message}");
        }
    }

    // The material follows the window's appearance, which is the system's unless the app chose light or dark itself.
    private static IntPtr Send(IntPtr receiver, string selector) => IntPtr_objc_msgSend(receiver, Selector.GetHandle(selector));
    private static IntPtr Send(IntPtr receiver, string selector, CGRect rect) => IntPtr_objc_msgSend_CGRect(receiver, Selector.GetHandle(selector), rect);
    private static void SendNint(IntPtr receiver, string selector, nint value) => Void_objc_msgSend_nint(receiver, Selector.GetHandle(selector), value);
    private static void SendNuint(IntPtr receiver, string selector, nuint value) => Void_objc_msgSend_nuint(receiver, Selector.GetHandle(selector), value);
    private static void SendBool(IntPtr receiver, string selector, bool value) => Void_objc_msgSend_bool(receiver, Selector.GetHandle(selector), value);
    private static void SendPtr(IntPtr receiver, string selector, IntPtr value) => Void_objc_msgSend_IntPtr(receiver, Selector.GetHandle(selector), value);

    private const string ObjC = "/usr/lib/libobjc.dylib";

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend_CGRect(IntPtr receiver, IntPtr selector, CGRect rect);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern CGRect Bounds(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void Void_objc_msgSend_nint(IntPtr receiver, IntPtr selector, nint value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void Void_objc_msgSend_nuint(IntPtr receiver, IntPtr selector, nuint value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void Void_objc_msgSend_bool(IntPtr receiver, IntPtr selector, bool value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void Void_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void AddBelow(IntPtr receiver, IntPtr selector, IntPtr view, nint place, IntPtr relativeTo);
}
