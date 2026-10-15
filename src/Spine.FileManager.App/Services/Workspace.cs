using System.Runtime.InteropServices;

namespace Spine.FileManager.Services;

/// <summary>An app that can open a file, for the preview's Open With.</summary>
public sealed record OpenWithApp(string Name, string Path, bool IsDefault);

/// <summary>
/// What the system's file manager does with a file beyond opening it: the apps that can open it, opening it with one
/// of them, and showing it in Finder or Explorer. On the Mac through AppKit's NSWorkspace, reached through the
/// Objective-C runtime (Mac Catalyst has no binding for it).
/// </summary>
public static class Workspace
{
    public static bool CanOpenWith => OperatingSystem.IsMacCatalyst();

    /// <summary>The apps that can open <paramref name="path"/>, the default first; empty where the system cannot say.</summary>
    public static IReadOnlyList<OpenWithApp> AppsFor(string path)
    {
#if MACCATALYST
        var workspace = Send(Class("NSWorkspace"), "sharedWorkspace");
        var url = Foundation.NSUrl.FromFilename(path);
        var preferred = ObjCRuntime.Runtime.GetNSObject<Foundation.NSUrl>(Send(workspace, "URLForApplicationToOpenURL:", url.Handle))?.Path;
        var apps = ObjCRuntime.Runtime.GetNSObject<Foundation.NSArray>(Send(workspace, "URLsForApplicationsToOpenURL:", url.Handle));
        if (apps is null)
            return [];
        return [.. Enumerable.Range(0, (int)apps.Count)
            .Select(i => apps.GetItem<Foundation.NSUrl>((nuint)i).Path)
            .OfType<string>()
            .Distinct()
            .Select(app => new OpenWithApp(Foundation.NSFileManager.DefaultManager.DisplayName(app), app, app == preferred))
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
#else
        return [];
#endif
    }

    public static void OpenWith(string path, string app)
    {
#if MACCATALYST
        var workspace = Send(Class("NSWorkspace"), "sharedWorkspace");
        var files = Foundation.NSArray.FromNSObjects(Foundation.NSUrl.FromFilename(path));
        var configuration = Send(Class("NSWorkspaceOpenConfiguration"), "configuration");
        SendOpen(workspace, ObjCRuntime.Selector.GetHandle("openURLs:withApplicationAtURL:configuration:completionHandler:"),
            files.Handle, Foundation.NSUrl.FromFilename(app).Handle, configuration, IntPtr.Zero);
#endif
    }

    /// <summary>Shows the file selected in Finder or Explorer.</summary>
    public static void Reveal(string path)
    {
#if MACCATALYST
        var workspace = Send(Class("NSWorkspace"), "sharedWorkspace");
        Send(workspace, "activateFileViewerSelectingURLs:", Foundation.NSArray.FromNSObjects(Foundation.NSUrl.FromFilename(path)).Handle);
#elif WINDOWS
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
#endif
    }

#if MACCATALYST
    private static IntPtr Class(string name) => ObjCRuntime.Class.GetHandle(name);

    private static IntPtr Send(IntPtr receiver, string selector) => Msg(receiver, ObjCRuntime.Selector.GetHandle(selector));

    private static IntPtr Send(IntPtr receiver, string selector, IntPtr argument) => Msg(receiver, ObjCRuntime.Selector.GetHandle(selector), argument);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr Msg(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr Msg(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendOpen(IntPtr receiver, IntPtr selector, IntPtr urls, IntPtr app, IntPtr configuration, IntPtr completion);
#endif
}
