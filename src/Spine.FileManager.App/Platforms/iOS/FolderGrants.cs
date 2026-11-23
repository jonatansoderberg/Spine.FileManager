using System.Text.Json;
using Foundation;
using Spine.FileManager.Storage;
using UIKit;
using UniformTypeIdentifiers;

namespace Spine.FileManager;

/// <summary>
/// Folders the user gave the app in iOS's folder picker (On My iPhone, iCloud Drive, another provider, a USB drive, a
/// server): each kept as a security-scoped bookmark, resolved and opened again at every start, and shown as a place.
/// Inside one, System.IO works on its real path while the access is held (file-system spike, iOS).
/// </summary>
public static class FolderGrants
{
    private sealed record Grant(string Bookmark);

    private static readonly string StorePath = Path.Combine(AppPaths.Data, "folder-grants.json");
    private static readonly List<(NSUrl Url, string Bookmark)> _open = [];

    /// <summary>A folder was added or removed: the places are built again.</summary>
    public static event Action? Changed;

    /// <summary>The granted folders as places: their path, the name the Files app gives them, and what kind of place.</summary>
    public static IReadOnlyList<(string Path, string Name, StorageRootKind Kind)> Roots
    {
        get
        {
            lock (_open)
                return [.. _open.Select(g => (g.Url.Path ?? "", NameOf(g.Url), KindOf(g.Url))).Where(r => r.Item1.Length > 0)];
        }
    }

    public static bool Contains(string path)
    {
        lock (_open)
            return _open.Any(g => g.Url.Path == path);
    }

    /// <summary>Opens every kept folder again; a stale bookmark is made anew, one that no longer resolves is dropped.</summary>
    public static void Load()
    {
        List<Grant> grants;
        try
        {
            grants = File.Exists(StorePath) ? JsonSerializer.Deserialize<List<Grant>>(File.ReadAllText(StorePath)) ?? [] : [];
        }
        catch (JsonException)
        {
            grants = [];
        }
        lock (_open)
        {
            foreach (var grant in grants)
            {
                var url = NSUrl.FromBookmarkData(NSData.FromArray(Convert.FromBase64String(grant.Bookmark)), NSUrlBookmarkResolutionOptions.WithoutUI, null, out var stale, out var error);
                if (url is null || error is not null || !url.StartAccessingSecurityScopedResource())
                {
                    System.Diagnostics.Debug.WriteLine($"[Grants] a kept folder could not be opened: {error?.LocalizedDescription}");
                    continue;
                }
                _open.Add((url, stale ? BookmarkOf(url) ?? grant.Bookmark : grant.Bookmark));
            }
        }
        Save();
    }

    /// <summary>Asks for a folder in iOS's picker and keeps it; false when the user cancelled.</summary>
    public static async Task<bool> PickAsync()
    {
        var picked = new TaskCompletionSource<NSUrl?>();
        var picker = new UIDocumentPickerViewController([UTTypes.Folder], asCopy: false) { AllowsMultipleSelection = false };
        picker.DidPickDocumentAtUrls += (_, e) => picked.TrySetResult(e.Urls.FirstOrDefault());
        picker.WasCancelled += (_, _) => picked.TrySetResult(null);
        if (Platform.GetCurrentUIViewController() is not { } presenter)
            return false;
        presenter.PresentViewController(picker, true, null);
        if (await picked.Task is not { } url || !url.StartAccessingSecurityScopedResource())
            return false;
        if (BookmarkOf(url) is not { } bookmark)
        {
            url.StopAccessingSecurityScopedResource();
            return false;
        }
        lock (_open)
        {
            if (_open.Any(g => g.Url.Path == url.Path))
                return true;
            _open.Add((url, bookmark));
        }
        Save();
        Changed?.Invoke();
        return true;
    }

    public static void Remove(string path)
    {
        lock (_open)
        {
            foreach (var grant in _open.Where(g => g.Url.Path == path).ToList())
            {
                grant.Url.StopAccessingSecurityScopedResource();
                _open.Remove(grant);
            }
        }
        Save();
        Changed?.Invoke();
    }

    private static string? BookmarkOf(NSUrl url) =>
        url.CreateBookmarkData(NSUrlBookmarkCreationOptions.MinimalBookmark, null, null, out var error) is { } data && error is null
            ? Convert.ToBase64String(data.ToArray()) : null;

    // As the Files app names it: "On My iPhone", "iCloud Drive", a provider's or a folder's own name.
    private static string NameOf(NSUrl url) =>
        url.TryGetResource(NSUrl.LocalizedNameKey, out var name) && name?.ToString() is { Length: > 0 } text ? text : url.LastPathComponent ?? "";

    private static StorageRootKind KindOf(NSUrl url) => url.Path switch
    {
        { } path when path.Contains("/Mobile Documents/") || path.Contains("/CloudStorage/") => StorageRootKind.Cloud,
        { } path when path.Contains("/File Provider Storage") => StorageRootKind.Volume,
        _ => StorageRootKind.Other,
    };

    private static void Save()
    {
        lock (_open)
        {
            Directory.CreateDirectory(AppPaths.Data);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(_open.Select(g => new Grant(g.Bookmark)).ToList()));
        }
    }
}
