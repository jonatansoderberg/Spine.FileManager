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
        if (!IsSupported || Application.Current is not { } app)
            return;
        var explorer = new ExplorerPage();
        var vm = (ExplorerPageViewModel)explorer.BindingContext;
        vm.Start(tabs, active, from);
        var window = new Window(new ContentPage { Content = explorer, Title = AppInfo.Name }) { Title = AppInfo.Name };
        window.Created += (_, _) => _ = vm.OnAppearingAsync(NavigationDirection.NavigateTo);
        window.Destroying += (_, _) =>
        {
            if (AppMenu.Explorer == vm)
                AppMenu.Explorer = null;
            // A window the user closes is not part of the next session.
            IPlatformApplication.Current?.Services.GetService<SessionTracker>()?.Unregister(vm);
        };
        app.OpenWindow(window);
    }
}
