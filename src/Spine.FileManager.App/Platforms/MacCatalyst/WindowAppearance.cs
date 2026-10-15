using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Spine.FileManager;

/// <summary>
/// Every window of the app, sheets included, in the app's light or dark rather than the system's. On the Mac a sheet is
/// a window of its own: given no appearance it drew in the system's (its glass close button dark in the app's light
/// theme), and Spine sets a sheet's UIKit style once, when it opens. On each change of theme, and whenever a window
/// becomes key (a sheet opening), the windows are taken from NSApplication's list, so no stale window is messaged.
/// </summary>
public static class WindowAppearance
{
    private static bool _hooked;

    public static void Start()
    {
        NSNotificationCenter.DefaultCenter.AddObserver(new NSString("NSWindowDidBecomeKeyNotification"), notification =>
        {
            // Spine's Changed rather than Application.RequestedThemeChanged, a weak event whose handler held by
            // nothing else is collected.
            if (!_hooked && IPlatformApplication.Current?.Services.GetService<Plugin.Maui.Spine.Core.IThemeService>() is { } theme)
            {
                _hooked = true;
                theme.Changed += (_, _) => FollowAll();
            }
            if (notification.Object is { } window)
            {
                Follow(Send(Class.GetHandle("NSApplication"), "sharedApplication"));
                Follow(window.Handle);
            }
        });
        // A sheet need not become key (the app in the background): its window is followed when it begins.
        NSNotificationCenter.DefaultCenter.AddObserver(new NSString("NSWindowWillBeginSheetNotification"), _ =>
            NSRunLoop.Main.BeginInvokeOnMainThread(FollowAll));
    }

    private static void FollowAll()
    {
        // The app itself too, for what AppKit draws outside the windows (menus, alerts).
        Follow(Send(Class.GetHandle("NSApplication"), "sharedApplication"));
        if (ObjCRuntime.Runtime.GetNSObject<NSArray>(Send(Send(Class.GetHandle("NSApplication"), "sharedApplication"), "windows")) is { } windows)
            for (nuint i = 0; i < windows.Count; i++)
                Follow(windows.ValueAt(i));

        // The sheets Spine presented keep the style they opened with; their card chrome lives in the container view.
        var style = Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark ? UIUserInterfaceStyle.Dark : UIUserInterfaceStyle.Light;
        foreach (var window in UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>().SelectMany(s => s.Windows))
            for (var presented = window.RootViewController?.PresentedViewController; presented is not null; presented = presented.PresentedViewController)
            {
                if (presented.OverrideUserInterfaceStyle == UIUserInterfaceStyle.Unspecified)
                    continue;
                presented.OverrideUserInterfaceStyle = style;
                if (presented.PresentationController?.ContainerView is { } container)
                    container.OverrideUserInterfaceStyle = style;
            }
    }

    private static void Follow(IntPtr window)
    {
        var name = Microsoft.Maui.Controls.Application.Current?.UserAppTheme switch
        {
            AppTheme.Light => "NSAppearanceNameAqua",
            AppTheme.Dark => "NSAppearanceNameDarkAqua",
            _ => null,
        };
        var appearance = name is null ? IntPtr.Zero : SendPtrResult(Class.GetHandle("NSAppearance"), Selector.GetHandle("appearanceNamed:"), new NSString(name).Handle);
        SendPtr(window, Selector.GetHandle("setAppearance:"), appearance);
    }

    private static IntPtr Send(IntPtr receiver, string selector) => Msg(receiver, Selector.GetHandle(selector));

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr Msg(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPtrResult(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendPtr(IntPtr receiver, IntPtr selector, IntPtr argument);
}
