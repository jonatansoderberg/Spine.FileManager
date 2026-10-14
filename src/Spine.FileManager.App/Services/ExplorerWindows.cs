using Spine.FileManager.Pages;

namespace Spine.FileManager.Services;

/// <summary>
/// Explorer windows beyond the first. Spine hosts one window (its host page is a singleton), so another window is a
/// plain MAUI page holding its own <see cref="ExplorerPage"/>, whose view model is transient (Spike 4).
/// </summary>
public static class ExplorerWindows
{
    public static bool IsSupported => DeviceInfo.Idiom == DeviceIdiom.Desktop;

    /// <summary>Opens a window with <paramref name="tabs"/> (moved or restored, <paramref name="active"/> shown), or at <paramref name="from"/>.</summary>
    public static void Open(IReadOnlyList<ExplorerTab>? tabs = null, int active = 0, StorageLocation? from = null)
    {
        if (IsSupported && Application.Current is { } app)
            app.OpenWindow(Create(tabs, active, from));
    }

    public static Window Create(IReadOnlyList<ExplorerTab>? tabs = null, int active = 0, StorageLocation? from = null)
    {
        var explorer = new ExplorerPage();
        var vm = (ExplorerPageViewModel)explorer.BindingContext;
        vm.Start(tabs, active, from);
        var window = new Window(new ContentPage { Content = explorer, Title = AppInfo.Name }) { Title = AppInfo.Name };
#if MACCATALYST
        // As the main window (ExplorerPage): the explorer reaches into the title bar, which shows no title.
        window.Created += (_, _) =>
        {
            if (window.Handler?.PlatformView is UIKit.UIWindow { WindowScene.Titlebar: { } titlebar })
            {
                titlebar.TitleVisibility = UIKit.UITitlebarTitleVisibility.Hidden;
                titlebar.Toolbar = null;
            }
        };
#endif
        window.Created += (_, _) => _ = vm.OnAppearingAsync(NavigationDirection.NavigateTo);
        SettleLayout(window);
        window.Destroying += (_, _) =>
        {
            // A window the user closes is not part of the next session.
            IPlatformApplication.Current?.Services.GetService<SessionTracker>()?.Unregister(vm);
        };
        return window;
    }

    /// <summary>Closes a window for good: MAUI's CloseWindow only hides a scene on the Mac.</summary>
    public static void Close(Window window)
    {
#if MACCATALYST || IOS
        if (window.Handler?.PlatformView is UIKit.UIWindow { WindowScene: { } scene })
        {
            UIKit.UIApplication.SharedApplication.RequestSceneSessionDestruction(scene.Session, null, null);
            return;
        }
#endif
        Application.Current?.CloseWindow(window);
    }

    /// <summary>
    /// A window opened while the app runs is laid out before macOS gives it its size and drawn shifted up under its
    /// title bar until it is resized (Mac Catalyst); resizing it by a point and back once it is on screen draws it right.
    /// </summary>
    private static void SettleLayout(Window window)
    {
#if MACCATALYST
        window.Created += (_, _) => window.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), () =>
        {
            if (window.Handler?.PlatformView is not UIKit.UIWindow { WindowScene: { } scene })
                return;
            var frame = scene.EffectiveGeometry.SystemFrame;
            scene.RequestGeometryUpdate(new UIKit.UIWindowSceneGeometryPreferencesMac(new CoreGraphics.CGRect(frame.X, frame.Y, frame.Width, frame.Height + 1)), null);
            window.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () =>
                scene.RequestGeometryUpdate(new UIKit.UIWindowSceneGeometryPreferencesMac(frame), null));
        });
#endif
    }
}
