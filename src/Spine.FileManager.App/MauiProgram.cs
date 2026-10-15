using Microsoft.Maui.LifecycleEvents;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Extensions;
using Spine.FileManager.Services;
using Spine.FileManager.Storage.FileSystem;

namespace Spine.FileManager;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSpine(options =>
            {
                options.AddAssembly(typeof(MauiProgram).Assembly);
                options.AppTitle = "Spine File Manager";
                options.Windows.InitialWidth = 1200;
                options.Windows.InitialHeight = 760;
                options.Windows.MinWidth = 480;
                options.Windows.MinHeight = 360;
                options.Windows.PersistWindowPosition = true;
                // Windows 11's Mica behind the window, following light and dark (the Mac's material: WindowMaterial).
                options.Windows.Backdrop = WindowBackdrop.Mica;
                // A tray icon with the shortcuts (ShortcutHandler). Closing the window keeps the app running in the
                // background, so other devices can still send files and transfers go on.
                options.Shortcuts.UseHandler<ShortcutHandler>();
                options.MacOS.ShowTrayIcon = true;
                options.MacOS.TrayIconSvg = "tray.svg";
                options.MacOS.TrayIconTooltip = "Spine File Manager";
                options.MacOS.CloseToBackground = true;
                options.Windows.ShowTrayIcon = true;
                options.Windows.TrayIconSvg = "tray.svg";
                options.Windows.TrayIconTooltip = "Spine File Manager";
                options.Windows.CloseToBackground = true;
            })
            .ConfigureLifecycleEvents(events =>
            {
#if IOS || MACCATALYST
                // A pairing link (spinefm://pair?…) opened on this device joins the device that showed it.
                events.AddiOS(ios => ios
                    .OpenUrl((_, url, _) => DeviceLinks.Open(url.AbsoluteString))
                    .SceneOpenUrl((_, contexts) => contexts.ToArray().Count(context => DeviceLinks.Open(context.Url.AbsoluteString)) > 0));
#endif
            });

#if IOS || MACCATALYST
        // MAUI 10's CollectionViewHandler2 lays out every row up front (compositional layout): 100 000 rows held the
        // window for 1.75 s and a jump to the end for 1.6 s; the flow-layout handler took 0.47 s and 0.08 s (Spike 2).
        builder.ConfigureMauiHandlers(h => h.AddHandler<Controls.FileListView, Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler>());
#endif
#if MACCATALYST
        builder.ConfigureMauiHandlers(h => h.AddHandler<Controls.KeySink, KeySinkHandler>());
#endif
        // A field in a capsule of its own (search) draws neither its border nor the system's focus ring, whose shape
        // differs from the capsule; the capsule shows the focus instead.
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("Bare", (handler, entry) =>
        {
            if (entry is not Entry { ClassId: "Bare" })
                return;
#if IOS || MACCATALYST
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
            handler.PlatformView.FocusEffect = null;
#elif WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = null;
#endif
        });
        builder.Services.AddSingleton<IFileSystemPlatform>(
#if MACCATALYST
            new MacFileSystemPlatform()
#elif WINDOWS
            new WindowsFileSystemPlatform()
#else
            new DefaultFileSystemPlatform()
#endif
        );
        builder.Services.AddSingleton<IStorageProvider, FileSystemStorageProvider>();
#if IOS || MACCATALYST
        builder.Services.AddSingleton<IStorageProvider, Services.PhotoLibraryProvider>();
#endif
        builder.Services.AddSingleton<StorageProviders>();
        builder.Services.AddSingleton(services => new Operations.OperationQueue(
            services.GetRequiredService<StorageProviders>(), new Operations.OperationJournal(Path.Combine(AppPaths.Data, "operations"))));
        builder.Services.AddSingleton(new Favorites.FavoritesStore(Path.Combine(AppPaths.Data, "favorites.json")));
        builder.Services.AddSingleton(new Workspaces.SessionStore(Path.Combine(AppPaths.Data, "session.json")));
        builder.Services.AddSingleton(new Explorer.RecentLocations(Path.Combine(AppPaths.Data, "recent.json")));
        builder.Services.AddSingleton<SessionTracker>();
        builder.Services.AddSingleton<UndoService>();
        builder.Services.AddSingleton<DeviceService>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Peer link spike: PEER_SPIKE="<signal url> <id> <target> <megabytes>" calls the target over ICE + KCP. On the
        // phone without a cable the test starts by itself (spike build only) and shows its result.
        if ((Environment.GetEnvironmentVariable("PEER_SPIKE") is { Length: > 0 } env ? env : "wss://x97np3r3-5290.euw.devtunnels.ms phone mac-k 20") is { } spike)
            _ = Task.Run(async () =>
            {
                var a = spike.Split(' ');
                void Log(string line) => Console.WriteLine($"[spike] {DateTime.Now:HH:mm:ss.fff} {line}");
                try
                {
                    var signal = new PeerLinkSpike.SignalClient(a[0], a[1]);
                    await signal.ConnectAsync();
                    Log("signal connected");
                    var link = await PeerLinkSpike.IceKcpSpike.ConnectAsync(signal, a[2], Log);
                    var result = await PeerLinkSpike.IceKcpSpike.SendAsync(link, int.Parse(a[3]), Log);
                    await signal.SendAsync(a[2], "result", result);
                    Show(result);
                }
                catch (Exception e)
                {
                    Log($"failed: {e}");
                    Show($"failed: {e.Message}");
                }

                static void Show(string text) => MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(500);
                    if (Application.Current?.Windows.FirstOrDefault()?.Page is { } page)
                        await page.DisplayAlertAsync("Peer link spike", text, "OK");
                });
            });
        return builder.Build();
    }
}
