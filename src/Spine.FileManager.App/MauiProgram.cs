using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Extensions;
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
        builder.Services.AddSingleton<StorageProviders>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
