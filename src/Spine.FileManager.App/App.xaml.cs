namespace Spine.FileManager;

public partial class App
{
    public App() => InitializeComponent();

    // Spine hosts one window. A window beyond it that the system asks for itself — a scene macOS restores from the
    // last run — would get Spine's host page a second time, which MAUI refuses at launch. It gets an empty window that
    // ends its scene at once; the app's own session restore brings the tabs back (SessionTracker).
    protected override Window CreateWindow(IActivationState? activationState)
    {
        if (Windows.Count == 0)
            return base.CreateWindow(activationState);
        var window = new Window(new ContentPage());
        window.Created += (_, _) => Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), () =>
        {
#if MACCATALYST || IOS
            if (window.Handler?.PlatformView is UIKit.UIWindow { WindowScene: { } scene })
                UIKit.UIApplication.SharedApplication.RequestSceneSessionDestruction(scene.Session, null, null);
            else
#endif
                CloseWindow(window);
        });
        return window;
    }
}
