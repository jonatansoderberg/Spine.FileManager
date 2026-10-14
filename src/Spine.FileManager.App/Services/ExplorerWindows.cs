using Spine.FileManager.Pages;

namespace Spine.FileManager.Services;

/// <summary>
/// Explorer windows beyond the first. Spine hosts one window (its host page is a singleton), so another window is a
/// plain MAUI page holding its own <see cref="ExplorerPage"/>, whose view model is transient (Spike 4).
/// </summary>
public static class ExplorerWindows
{
    public static bool IsSupported => DeviceInfo.Idiom == DeviceIdiom.Desktop;

    /// <summary>Opens a window showing <paramref name="tab"/>'s folder, history and view, or a copy of where <paramref name="from"/> is.</summary>
    public static void Open(ExplorerTab? tab = null, StorageLocation? from = null)
    {
        if (!IsSupported || Application.Current is not { } app)
            return;
        var explorer = new ExplorerPage();
        var vm = (ExplorerPageViewModel)explorer.BindingContext;
        vm.Start(tab, from);
        var window = new Window(new ContentPage { Content = explorer, Title = AppInfo.Name }) { Title = AppInfo.Name };
        window.Created += (_, _) => _ = vm.OnAppearingAsync(NavigationDirection.NavigateTo);
        window.Destroying += (_, _) =>
        {
            if (AppMenu.Explorer == vm)
                AppMenu.Explorer = null;
        };
        app.OpenWindow(window);
    }
}
