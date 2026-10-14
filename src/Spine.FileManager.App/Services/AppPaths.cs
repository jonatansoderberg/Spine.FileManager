namespace Spine.FileManager.Services;

/// <summary>Where the app keeps its own files.</summary>
public static class AppPaths
{
    /// <summary>
    /// The app's data folder. Without the App Sandbox MAUI's <see cref="FileSystem.AppDataDirectory"/> is the user's
    /// whole ~/Library on the Mac, so the Mac build uses ~/Library/Application Support/&lt;bundle id&gt; as a Mac app should.
    /// </summary>
    public static string Data { get; } = Resolve();

    private static string Resolve()
    {
#if MACCATALYST
        var support = Foundation.NSFileManager.DefaultManager.GetUrls(Foundation.NSSearchPathDirectory.ApplicationSupportDirectory, Foundation.NSSearchPathDomain.User)[0].Path!;
        var path = Path.Combine(support, AppInfo.PackageName);
#else
        var path = FileSystem.AppDataDirectory;
#endif
        Directory.CreateDirectory(path);
        return path;
    }
}
