namespace Spine.FileManager.Services;

/// <summary>Opens a file in the app the system has for it.</summary>
public static class FileLauncher
{
    public static async Task<bool> OpenAsync(StorageItem file)
    {
        if (file.Location.NativePath is not { } path)
            return false;
#if MACCATALYST
        // Launcher's OpenFileRequest presents a preview sheet on Mac Catalyst; a file URL opens the default app, as Finder does.
        var done = new TaskCompletionSource<bool>();
        UIKit.UIApplication.SharedApplication.OpenUrl(Foundation.NSUrl.FromFilename(path), new UIKit.UIApplicationOpenUrlOptions(), done.SetResult);
        return await done.Task;
#else
        return await Launcher.Default.OpenAsync(new OpenFileRequest { File = new ReadOnlyFile(path) });
#endif
    }
}
