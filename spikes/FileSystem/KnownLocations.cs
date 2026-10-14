namespace FileSystemSpike;

public sealed record KnownLocation(string Name, string Path, bool Write);

/// <summary>The folders each platform might reach without asking the user to pick one.</summary>
public static class KnownLocations
{
    public static IEnumerable<KnownLocation> All()
    {
        yield return new("App data", FileSystem.AppDataDirectory, true);
        yield return new("App cache", FileSystem.CacheDirectory, true);
        yield return new("Temp", Path.GetTempPath(), true);

#if MACCATALYST
        // In the sandbox HOME is the container; the user's own folders sit under the real home.
        var home = $"/Users/{Environment.UserName}";
        yield return new("Container home (HOME)", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), true);
        yield return new("~/Documents", $"{home}/Documents", true);
        yield return new("~/Downloads", $"{home}/Downloads", true);
        yield return new("~/Desktop", $"{home}/Desktop", true);
        yield return new("~/Pictures", $"{home}/Pictures", true);
        yield return new("~ (home)", home, false);
        yield return new("iCloud Drive", $"{home}/Library/Mobile Documents/com~apple~CloudDocs", true);
        yield return new("File providers (~/Library/CloudStorage)", $"{home}/Library/CloudStorage", false);
        foreach (var provider in SafeDirectories($"{home}/Library/CloudStorage"))
            yield return new($"Provider {System.IO.Path.GetFileName(provider)}", provider, true);
        yield return new("/Applications", "/Applications", false);
        yield return new("/ (root)", "/", false);
        foreach (var volume in SafeDirectories("/Volumes"))
            yield return new($"Volume {System.IO.Path.GetFileName(volume)}", volume, volume != "/Volumes/Macintosh HD");
#elif IOS
        yield return new("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), true);
        yield return new("Library", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library"), true);
        yield return new("App bundle", Foundation.NSBundle.MainBundle.BundlePath, false);
        yield return new("/var/mobile/Media", "/var/mobile/Media", false);
        yield return new("/ (root)", "/", false);
#elif ANDROID
        var context = Platform.AppContext;
        yield return new("App external files", context.GetExternalFilesDir(null)!.AbsolutePath, true);
        var shared = Android.OS.Environment.ExternalStorageDirectory!.AbsolutePath;
        yield return new("Shared storage root", shared, true);
        foreach (var (name, type) in new[] { ("Download", Android.OS.Environment.DirectoryDownloads), ("DCIM", Android.OS.Environment.DirectoryDcim), ("Pictures", Android.OS.Environment.DirectoryPictures), ("Documents", Android.OS.Environment.DirectoryDocuments), ("Music", Android.OS.Environment.DirectoryMusic) })
            yield return new($"Shared {name}", Android.OS.Environment.GetExternalStoragePublicDirectory(type!)!.AbsolutePath, true);
        yield return new("Another app's data", "/storage/emulated/0/Android/data", false);
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && context.GetSystemService(Android.Content.Context.StorageService) is Android.OS.Storage.StorageManager storage)
            foreach (var volume in storage.StorageVolumes.Where(v => !v.IsPrimary))
                if (volume.Directory?.AbsolutePath is { } directory)
                    yield return new($"Volume {volume.GetDescription(context)} ({(volume.IsRemovable ? "removable" : "fixed")})", directory, true);
        yield return new("/storage", "/storage", false);
        yield return new("/ (root)", "/", false);
#elif WINDOWS
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return new("User profile", profile, false);
        yield return new("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), true);
        yield return new("Downloads", System.IO.Path.Combine(profile, "Downloads"), true);
        yield return new("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), true);
        yield return new("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), true);
        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } oneDrive)
                yield return new($"%{variable}%", oneDrive, true);
        // A network share to try: set SPIKE_UNC=\\server\share before starting the app.
        if (Environment.GetEnvironmentVariable("SPIKE_UNC") is { Length: > 0 } unc)
            yield return new("Network share (SPIKE_UNC)", unc, true);
        var system = System.IO.Path.GetPathRoot(Environment.SystemDirectory);
        foreach (var drive in DriveInfo.GetDrives())
            yield return new($"Drive {drive.Name} ({drive.DriveType})", drive.RootDirectory.FullName, drive.IsReady && drive.Name != system && drive.DriveType is not DriveType.CDRom);
#endif
    }

    static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch
        {
            return [];
        }
    }
}
