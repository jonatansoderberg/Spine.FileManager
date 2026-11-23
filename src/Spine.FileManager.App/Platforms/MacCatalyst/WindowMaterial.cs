using System.Runtime.InteropServices;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Spine.FileManager;

/// <summary>
/// The Mac's window material, the user's Mica or Acrylic (<see cref="WindowBackdrops"/>): the desktop shows through the
/// window, as through Finder's sidebar. Mac Catalyst
/// has no API for it, so an AppKit NSVisualEffectView goes behind the window's UIKit content and the window stops being
/// opaque; the views that should show it are transparent (the sidebar), the rest keep their own background. The
/// NSWindow is found the way Spine finds it: as the object of NSWindowDidBecomeKeyNotification.
/// </summary>
public static class WindowMaterial
{
    // NSVisualEffectMaterial.WindowBackground (Mica: tinted by the desktop picture and nearly opaque, as Finder's sidebar
    // is) and .Sidebar (Acrylic: the desktop clearly through it; .UnderWindowBackground draws the same on macOS 27),
    // NSVisualEffectBlendingMode.BehindWindow, NSVisualEffectState.Active, NSViewWidthSizable | NSViewHeightSizable,
    // NSWindowBelow.
    private const nint WindowBackground = 12, Sidebar = 7, BehindWindow = 0, Active = 1, Below = -1;
    private const nuint FillsSuperview = 2 | 16;

    // Each scene window's material view, installed once and then changed or hidden as the choice changes.
    private static readonly Dictionary<IntPtr, IntPtr> Effects = [];

    // With glass, each window's AppKit NSGlassEffectView behind the sidebar (macOS 26), above the material.
    private static readonly Dictionary<IntPtr, IntPtr> Glasses = [];
    private const nint Above = 1;

    public static void Start()
    {
        NSNotificationCenter.DefaultCenter.AddObserver(new NSString("NSWindowDidBecomeKeyNotification"), notification =>
        {
            if (notification.Object is { } window)
                Apply(window.Handle);
        });
        WindowBackdrops.Changed += () =>
        {
            var app = Send(Class.GetHandle("NSApplication"), "sharedApplication");
            if (ObjCRuntime.Runtime.GetNSObject<NSArray>(Send(app, "windows")) is not { } windows)
                return;
            for (nuint i = 0; i < windows.Count; i++)
                Apply(windows.ValueAt(i));
        };
    }

    private static void Apply(IntPtr window)
    {
        try
        {
            var content = Send(window, "contentView");
            var frame = Send(content, "superview");
            // Only a scene's window: menus, the status item and sheets' own windows have AppKit windows too.
            if (content == IntPtr.Zero || frame == IntPtr.Zero || ObjCRuntime.Runtime.GetNSObject(content)?.Class.Name != "UINSSceneContainerView")
                return;
            if (!Effects.TryGetValue(window, out var effect))
            {
                if (!WindowBackdrops.IsOn)
                    return;
                effect = Send(Send(Class.GetHandle("NSVisualEffectView"), "alloc"), "initWithFrame:", Bounds(frame, Selector.GetHandle("bounds")));
                SendNint(effect, "setBlendingMode:", BehindWindow);
                SendNint(effect, "setState:", Active);
                SendNuint(effect, "setAutoresizingMask:", FillsSuperview);
                AddBelow(frame, Selector.GetHandle("addSubview:positioned:relativeTo:"), effect, Below, content);
                SendBool(window, "setOpaque:", false);
                SendPtr(window, "setBackgroundColor:", Send(Class.GetHandle("NSColor"), "clearColor"));
                // UIKit's window and the views MAUI puts under the page are opaque by default; clear, but not to the hit test.
                foreach (var scene in UIApplication.SharedApplication.ConnectedScenes.ToArray<UIScene>().OfType<UIWindowScene>())
                    foreach (var uiWindow in scene.Windows)
                        for (UIView? view = uiWindow; view is not null && view is not Microsoft.Maui.Platform.LayoutView; view = view.Subviews.FirstOrDefault())
                            view.BackgroundColor = Microsoft.Maui.Platform.ColorExtensions.ToPlatform(WindowBackdrops.HitTestableClear);
                Effects[window] = effect;
            }
            SendNint(effect, "setMaterial:", WindowBackdrops.Current is Backdrop.Acrylic ? Sidebar : WindowBackground);
            SendBool(effect, "setHidden:", !WindowBackdrops.IsOn);
            if (Glasses.TryGetValue(window, out var glass))
                SendBool(glass, "setHidden:", !ShowsGlass);
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowMaterial] {e.GetType().Name}: {e.Message}");
        }
    }

    private static bool ShowsGlass => WindowBackdrops.IsOn && WindowBackdrops.Current is Backdrop.Glass;

    /// <summary>
    /// Puts the window's glass at <paramref name="rect"/>, in <paramref name="uiWindow"/>'s points (empty: no glass). UIKit
    /// knows no NSWindow, so the window is the one whose scene has the UIKit window's shape; with the iPad idiom UIKit's
    /// points are scaled, so the scale comes from the two widths.
    /// </summary>
    public static void SetGlassFrame(UIWindow uiWindow, CGRect rect)
    {
        if (uiWindow.Bounds.Width <= 0 || Class.GetHandle("NSGlassEffectView") == IntPtr.Zero)
            return;
        foreach (var (window, effect) in Effects)
        {
            var frame = Send(Send(window, "contentView"), "superview");
            var bounds = Bounds(frame, Selector.GetHandle("bounds"));
            var scale = bounds.Width / uiWindow.Bounds.Width;
            if (Math.Abs(bounds.Height - uiWindow.Bounds.Height * scale) > 2)
                continue;
            var appKit = new CGRect(rect.X * scale, 0, rect.Width * scale, rect.Height * scale);
            appKit.Y = Bool_objc_msgSend(frame, Selector.GetHandle("isFlipped")) ? rect.Y * scale : bounds.Height - (rect.Y + rect.Height) * scale;
            if (!Glasses.TryGetValue(window, out var glass))
            {
                if (!ShowsGlass || rect.IsEmpty)
                    continue;
                glass = Send(Send(Class.GetHandle("NSGlassEffectView"), "alloc"), "initWithFrame:", appKit);
                AddBelow(frame, Selector.GetHandle("addSubview:positioned:relativeTo:"), glass, Above, effect);
                Glasses[window] = glass;
            }
            Void_objc_msgSend_CGRect(glass, Selector.GetHandle("setFrame:"), appKit);
            Void_objc_msgSend_nfloat(glass, Selector.GetHandle("setCornerRadius:"), (nfloat)(12 * scale));
            SendBool(glass, "setHidden:", !ShowsGlass || rect.IsEmpty);
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
    private static extern void Void_objc_msgSend_CGRect(IntPtr receiver, IntPtr selector, CGRect rect);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void Void_objc_msgSend_nfloat(IntPtr receiver, IntPtr selector, nfloat value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Bool_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void AddBelow(IntPtr receiver, IntPtr selector, IntPtr view, nint place, IntPtr relativeTo);
}
