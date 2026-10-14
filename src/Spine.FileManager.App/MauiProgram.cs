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
        builder.Services.AddSingleton<StorageProviders>();
        builder.Services.AddSingleton(services => new Operations.OperationQueue(
            services.GetRequiredService<StorageProviders>(), new Operations.OperationJournal(Path.Combine(AppPaths.Data, "operations"))));
        builder.Services.AddSingleton(new Favorites.FavoritesStore(Path.Combine(AppPaths.Data, "favorites.json")));
        builder.Services.AddSingleton(new Workspaces.SessionStore(Path.Combine(AppPaths.Data, "session.json")));
        builder.Services.AddSingleton<SessionTracker>();
        builder.Services.AddSingleton<UndoService>();
        builder.Services.AddSingleton<DeviceService>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
