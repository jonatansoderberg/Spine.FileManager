#if IOS || MACCATALYST
using Foundation;
using UIKit;
using UniformTypeIdentifiers;

namespace FileSystemSpike;

public static partial class FolderAccess
{
    // Which bookmark survives a restart is what the spike is asking, so both kinds are kept.
    static readonly (string Label, NSUrlBookmarkCreationOptions Create, NSUrlBookmarkResolutionOptions Resolve)[] Bookmarks =
#if MACCATALYST
        [("security-scoped", NSUrlBookmarkCreationOptions.WithSecurityScope, NSUrlBookmarkResolutionOptions.WithSecurityScope), ("plain", 0, 0)];
#else
        [("plain", 0, 0), ("minimal", NSUrlBookmarkCreationOptions.MinimalBookmark, 0)];
#endif

    public static partial async Task<IReadOnlyList<LocationReport>> PickAndProbeAsync()
    {
        var url = await PickFolderAsync();
        if (url is null)
            return [];

        var access = url.StartAccessingSecurityScopedResource();
        try
        {
            var report = new LocationReport($"Picked {url.LastPathComponent}", url.Path!, "System.IO, picked");
            report.Notes.Add($"StartAccessingSecurityScopedResource: {access}");
            PathProbe.Probe(report, url.Path!, write: true);

            var granted = Load();
            foreach (var (label, create, _) in Bookmarks)
                report.Run($"bookmark {label}", () =>
                {
                    var data = url.CreateBookmarkData(create, null, null, out var error) ?? throw new IOException(error?.LocalizedDescription);
                    granted.Add($"{label}\t{url.LastPathComponent}\t{data.GetBase64EncodedString(0)}");
                    return $"{data.Length} bytes";
                });
            Save(granted);
            return [report];
        }
        finally
        {
            if (access)
                url.StopAccessingSecurityScopedResource();
        }
    }

    public static partial Task<IReadOnlyList<LocationReport>> ProbeGrantedAsync()
    {
        var reports = new List<LocationReport>();
        foreach (var entry in Load())
        {
            var parts = entry.Split('\t');
            var (label, name) = (parts[0], parts[1]);
            var resolve = Bookmarks.FirstOrDefault(b => b.Label == label).Resolve;
            var report = new LocationReport($"{name} after restart ({label} bookmark)", "bookmark", "System.IO, bookmark");
            NSUrl? url = null;
            report.Run("resolve", () =>
            {
                url = NSUrl.FromBookmarkData(new NSData(parts[2], NSDataBase64DecodingOptions.None), resolve, null, out var stale, out var error)
                    ?? throw new IOException(error?.LocalizedDescription ?? "no url");
                return $"{url.Path}{(stale ? ", stale" : "")}";
            });
            if (url is not null)
            {
                var access = url.StartAccessingSecurityScopedResource();
                report.Notes.Add($"StartAccessingSecurityScopedResource: {access}");
                try
                {
                    PathProbe.Probe(report, url.Path!, write: true);
                }
                finally
                {
                    if (access)
                        url.StopAccessingSecurityScopedResource();
                }
            }
            reports.Add(report);
        }
        return Task.FromResult<IReadOnlyList<LocationReport>>(reports);
    }

    static Task<NSUrl?> PickFolderAsync()
    {
        var done = new TaskCompletionSource<NSUrl?>();
        var picker = new UIDocumentPickerViewController([UTTypes.Folder], false) { AllowsMultipleSelection = false };
        picker.DidPickDocumentAtUrls += (_, e) => done.TrySetResult(e.Urls.FirstOrDefault());
        picker.WasCancelled += (_, _) => done.TrySetResult(null);
        Platform.GetCurrentUIViewController()!.PresentViewController(picker, true, null);
        return done.Task;
    }
}
#endif
