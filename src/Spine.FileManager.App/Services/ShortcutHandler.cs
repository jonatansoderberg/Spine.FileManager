using Spine.FileManager.Pages;

namespace Spine.FileManager.Services;

/// <summary>
/// The app's shortcuts: the menu-bar menu on the Mac and the tray menu on Windows, the long-press menu on the app icon
/// on iPhone and Android, the jump list on Windows. They reach what is useful when no explorer window is in front:
/// a window, the files other devices sent, linking a device, the transfers that are running.
/// </summary>
public sealed class ShortcutHandler(INavigationService navigation) : IShortcutHandler
{
    // Declared before the app's strings are loaded, so the titles follow the system's language here.
    private static bool Swedish => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "sv";

    public static void Configure(IShortcutBuilder builder)
    {
        builder.Add("new-window", Swedish ? "Nytt fönster" : "New Window", true, "plus", null);
        builder.Add("received", Swedish ? "Hämtade filer" : "Downloads", true, "download", Swedish ? "Där mottagna filer hamnar" : "Where received files go");
        builder.Add("link-device", Swedish ? "Länka enhet…" : "Link Device…", true, "qrcode", null);
        builder.Add("operations", Swedish ? "Överföringar" : "Transfers", true, "upload", null);
    }

    public Task InvokeAsync(string shortcutId)
    {
        switch (shortcutId)
        {
            case "new-window":
                ExplorerWindows.Open();
                return Task.CompletedTask;
            case "received":
                ExplorerWindows.Open(from: Storage.FileSystem.FileSystemStorageProvider.Location(DeviceService.Inbox));
                return Task.CompletedTask;
            case "link-device":
                return navigation.ShowAsync<LinkDevicePage>();
            case "operations":
                return navigation.ShowAsync<OperationsPage>();
            default:
                return Task.CompletedTask;
        }
    }
}
