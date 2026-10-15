using Spine.FileManager.Icons;

namespace Spine.FileManager.Services;

/// <summary>
/// The icons in the file lists, in the style the user picks (View › Icons):
/// <list type="bullet">
/// <item>Spine's own, after the spec's concept images: a coloured tile per kind of file;</item>
/// <item>the system's, as Finder shows them (Mac, iPhone, iPad; elsewhere Spine's);</item>
/// <item>an icon theme the user adds: a folder with a <c>theme.json</c> in the app's IconThemes folder.</item>
/// </list>
/// Icons are looked up once per extension, so a folder of 100 000 files costs a handful of lookups.
/// </summary>
public static class FileIcons
{
    public const string SpineStyle = "spine", SystemStyle = "system";
    private const string StyleKey = "icons.style";

    private static readonly Dictionary<string, ImageSource> Cache = [];
    private static IconTheme? _theme;

    /// <summary>The style changed: lists build their rows again.</summary>
    public static event Action? Changed;

    public static string ThemesFolder => Path.Combine(AppPaths.Data, "IconThemes");

    public static bool HasSystemIcons => DeviceInfo.Platform != DevicePlatform.Android;

    /// <summary><see cref="SpineStyle"/>, <see cref="SystemStyle"/> or the folder name of an icon theme.</summary>
    public static string Style { get; private set; } = Load();

    public static IReadOnlyList<IconTheme> Themes => IconTheme.All(ThemesFolder);

    public static void Use(string style)
    {
        Preferences.Set(StyleKey, style);
        Style = Load();
        Changed?.Invoke();
    }

    private static string Load()
    {
        lock (Cache)
            Cache.Clear();
        var style = Preferences.Get(StyleKey, SpineStyle);
        _theme = style is SpineStyle or SystemStyle ? null : IconTheme.Load(Path.Combine(ThemesFolder, style));
        return style == SystemStyle && !HasSystemIcons || style is not (SpineStyle or SystemStyle) && _theme is null ? SpineStyle : style;
    }

    // Bytes rather than an ImageSource: MAUI cancels a source's load when the same source starts loading again, so
    // one source shared by the cells that ask at once showed in none of them.
    private static readonly Dictionary<string, byte[]> Photos = [];

    private static ImageSource Picture(byte[] bytes) => ImageSource.FromStream(() => new MemoryStream(bytes));

    // A stream source of its own for each view that shows it, for the same reason (the system's icon of a kind is
    // shared by every row of that kind); file and resource sources are safe to share.
    private static ImageSource Fresh(ImageSource source) => source is StreamImageSource stream ? new StreamImageSource { Stream = stream.Stream } : source;
    private static readonly Dictionary<string, List<Action>> Waiting = [];

    /// <summary>
    /// The item's icon now; for a photo in the photo library, the kind's icon until its thumbnail arrives, when
    /// <paramref name="ready"/> is called (on the main thread) to ask again.
    /// </summary>
    private static readonly HashSet<string> PicturedKinds = [FileKinds.Image, FileKinds.Video, FileKinds.Pdf];

    /// <summary>
    /// The item's icon now; for a picture, a video or a PDF (and every photo in the photo library) the kind's icon until
    /// its thumbnail arrives, when <paramref name="ready"/> is called (on the main thread) to ask again.
    /// </summary>
    public static ImageSource For(StorageItem item, Action? ready)
    {
#if IOS || MACCATALYST
        if (item.Location.Provider == PhotoLibraryProvider.ProviderId && item.Location.ItemId.Length > 0)
            return Thumbnail($"photo:{item.Location.ItemId}", () => PhotoLibraryProvider.ThumbnailBytesAsync(item.Location, 64), item, ready)
                ?? Spine(false, item.Extension.ToLowerInvariant());
#endif
        // As Finder's icon previews: the file's own picture, made again when the file changes.
        if (!item.IsFolder && item.Location.NativePath is { } path && PicturedKinds.Contains(FileKinds.Of(item.Extension.ToLowerInvariant())))
            return Thumbnail($"file:{path}:{item.Modified?.UtcTicks}", () => Thumbnails.GetBytesAsync(path, 64), item, ready) ?? For(item);
        return For(item);
    }

    // Asked for by the rows a list shows, so only the visible items load; the oldest go when there are too many.
    private static ImageSource? Thumbnail(string key, Func<Task<byte[]?>> load, StorageItem item, Action? ready)
    {
        lock (Photos)
        {
            if (Photos.TryGetValue(key, out var cached))
                return Picture(cached);
            if (Waiting.TryGetValue(key, out var waiting))
            {
                if (ready is not null)
                    waiting.Add(ready);
                return null;
            }
            Waiting[key] = ready is null ? [] : [ready];
        }
        _ = Task.Run(async () =>
        {
            byte[]? picture = null;
            try
            {
                picture = await load();
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[FileIcons] No thumbnail for {item.Location.DisplayPath}: {e}");
            }
            List<Action> callbacks;
            lock (Photos)
            {
                callbacks = Waiting.Remove(key, out var w) ? w : [];
                if (picture is null)
                    return;
                if (Photos.Count > 3000)
                    Photos.Clear();
                Photos[key] = picture;
            }
            MainThread.BeginInvokeOnMainThread(() => callbacks.ForEach(c => c()));
        });
        return null;
    }

    public static ImageSource For(StorageItem item)
    {
        var extension = item.IsBrowsable ? "" : item.Extension.ToLowerInvariant();
        // A package (an app) has its own icon: it is looked up by itself rather than by its extension, and in every
        // style, since no theme can draw another app's icon.
        var key = item.IsPackage ? item.Location.ItemId : item.IsFolder ? "/folder" : extension;
        lock (Cache)
            if (Cache.TryGetValue(key, out var cached))
                return Fresh(cached);
        if (item.IsPackage)
            return SystemIcon(item, key) ?? Spine(false, extension);
        if (Style == SystemStyle)
            return SystemIcon(item, key) ?? Spine(item.IsFolder, extension);
        var source = (_theme?.IconFor(item.IsFolder, extension) is { } picture ? ImageSource.FromFile(picture) : null) ?? Spine(item.IsFolder, extension);
        lock (Cache)
            Cache[key] = source;
        return source;
    }

    /// <summary>Spine's picture for the item as a file in the app bundle, for native code (a drag's preview).</summary>
    public static string BundleName(StorageItem item) =>
        item.IsBrowsable ? "folder.png" : $"type_{FileKinds.Of(item.Extension.ToLowerInvariant())}.png";

    private static ImageSource Spine(bool isFolder, string extension) =>
        isFolder ? "folder.png" : $"type_{FileKinds.Of(extension)}.png";

    private static readonly HashSet<string> Loading = [];
    private static CancellationTokenSource? _refresh;

    /// <summary>System icons arrived: lists build their rows again to show them.</summary>
    public static event Action? Refreshed;

    // The system's icon is read once per kind of file, in the background; until it arrives the list shows Spine's.
    private static ImageSource? SystemIcon(StorageItem item, string key)
    {
        if (item.Location.NativePath is not { } path)
            return null;
        lock (Cache)
            if (!Loading.Add(key))
                return null;
        var style = Style;
        _ = Task.Run(async () =>
        {
            var icon = await Thumbnails.GetIconAsync(path, 64);
            lock (Cache)
            {
                Loading.Remove(key);
                if (icon is null || Style != style)
                    return;
                Cache[key] = icon;
            }
            // Many kinds arrive at once when a folder opens; the lists build their rows once for them.
            _refresh?.Cancel();
            var refresh = _refresh = new CancellationTokenSource();
            await Task.Delay(150);
            if (!refresh.IsCancellationRequested)
                Refreshed?.Invoke();
        });
        return null;
    }

    /// <summary>The themes folder, made with an example theme and its description the first time.</summary>
    public static string PrepareThemesFolder()
    {
        var example = Path.Combine(ThemesFolder, "Example");
        if (!Directory.Exists(example))
        {
            Directory.CreateDirectory(example);
            File.WriteAllText(Path.Combine(example, IconTheme.FileName), """
                {
                  // Put the pictures (PNG) next to this file and name them below. An extension wins over its kind,
                  // a kind over "file". Anything left out keeps Spine's icon. Pick the theme in View › Icons.
                  "name": "Example",
                  "folder": null,
                  "file": null,
                  "kinds": {
                    "document": null, "spreadsheet": null, "presentation": null, "pdf": null, "image": null,
                    "video": null, "audio": null, "archive": null, "code": null, "app": null, "text": null
                  },
                  "extensions": {
                    "fig": null
                  }
                }
                """);
        }
        return ThemesFolder;
    }
}
