#if ANDROID
using Android.Content;
using AUri = Android.Net.Uri;

namespace FileSystemSpike;

public static partial class FolderAccess
{
    public const int PickRequestCode = 4711;

    internal static TaskCompletionSource<AUri?>? Pending;

    const ActivityFlags ReadWrite = ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission;

    public static partial async Task<IReadOnlyList<LocationReport>> PickAndProbeAsync()
    {
        var activity = Platform.CurrentActivity!;
        Pending = new();
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ReadWrite | ActivityFlags.GrantPersistableUriPermission);
        activity.StartActivityForResult(intent, PickRequestCode);
        var tree = await Pending.Task;
        if (tree is null)
            return [];

        var resolver = activity.ContentResolver!;
        var report = await Task.Run(() => SafProbe.Run(resolver, tree, $"Picked tree {tree.LastPathSegment}"));
        report.Run("persist permission", () =>
        {
            resolver.TakePersistableUriPermission(tree, ReadWrite);
            return $"{resolver.PersistedUriPermissions.Count} persisted grants";
        });
        Save(Load().Append(tree.ToString()!).Distinct());
        return [report];
    }

    public static partial Task<IReadOnlyList<LocationReport>> ProbeGrantedAsync() => Task.Run<IReadOnlyList<LocationReport>>(() =>
    {
        var resolver = Platform.AppContext.ContentResolver!;
        var persisted = resolver.PersistedUriPermissions.Select(p => p.Uri!.ToString()).ToHashSet();
        return [.. Load().Select(entry =>
        {
            var tree = AUri.Parse(entry)!;
            var report = SafProbe.Run(resolver, tree, $"Tree {tree.LastPathSegment} after restart");
            report.Notes.Insert(0, $"Persisted grant still listed: {persisted.Contains(entry)}");
            return report;
        })];
    });

    /// <summary>Opens the settings page where the user turns on All files access for the app.</summary>
    public static void RequestAllFilesAccess()
    {
        var intent = new Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission, AUri.Parse($"package:{AppInfo.PackageName}"));
        Platform.CurrentActivity!.StartActivity(intent);
    }
}
#endif
