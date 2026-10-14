namespace FileSystemSpike;

/// <summary>What about the process decides access: sandbox, permissions, identity.</summary>
public static class ProcessContext
{
    public static IEnumerable<string> Lines()
    {
        yield return $"user: {Environment.UserName}, HOME: {Environment.GetEnvironmentVariable("HOME") ?? "(none)"}";
#if MACCATALYST
        yield return $"App Sandbox: {(Environment.GetEnvironmentVariable("APP_SANDBOX_CONTAINER_ID") is { } id ? $"on ({id})" : "off")}";
#elif ANDROID
        yield return $"API level {(int)Android.OS.Build.VERSION.SdkInt}, target {Platform.AppContext.ApplicationInfo!.TargetSdkVersion}";
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            yield return $"All files access (MANAGE_EXTERNAL_STORAGE): {(Android.OS.Environment.IsExternalStorageManager ? "granted" : "not granted")}";
#elif WINDOWS
        yield return $"elevated: {new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)}, packaged: false";
#endif
    }
}
