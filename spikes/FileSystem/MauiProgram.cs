using Plugin.Maui.Spine.Extensions;

namespace FileSystemSpike;

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
                options.AppTitle = "FS Spike";
                options.Windows.InitialWidth = 1000;
                options.Windows.InitialHeight = 800;
            });
        return builder.Build();
    }
}
