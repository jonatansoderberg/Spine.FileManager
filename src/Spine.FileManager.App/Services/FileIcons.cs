namespace Spine.FileManager.Services;

/// <summary>
/// The icon for a file by its kind, after the spec's concept images: a coloured tile per kind (documents blue,
/// spreadsheets green, presentations orange, PDF red, video purple…), a plain page for text and anything unknown.
/// </summary>
public static class FileIcons
{
    private static readonly Dictionary<string, string> Kinds = Build(new()
    {
        ["type_document"] = ["doc", "docx", "odt", "rtf", "pages"],
        ["type_spreadsheet"] = ["xls", "xlsx", "ods", "csv", "tsv", "numbers"],
        ["type_presentation"] = ["ppt", "pptx", "odp", "key"],
        ["type_pdf"] = ["pdf"],
        ["type_image"] = ["jpg", "jpeg", "png", "gif", "heic", "heif", "webp", "bmp", "tif", "tiff", "svg", "raw", "dng", "cr2", "nef", "psd", "fig", "sketch"],
        ["type_video"] = ["mp4", "mov", "m4v", "avi", "mkv", "webm", "wmv", "mpg", "mpeg"],
        ["type_audio"] = ["mp3", "wav", "aac", "flac", "m4a", "ogg", "aiff", "wma"],
        ["type_archive"] = ["zip", "rar", "7z", "tar", "gz", "tgz", "bz2", "xz", "dmg", "iso", "pkg"],
        ["type_code"] = ["cs", "js", "ts", "tsx", "jsx", "py", "java", "c", "cpp", "h", "hpp", "swift", "kt", "go", "rs", "rb", "php", "json", "xml", "html", "css", "scss", "xaml", "sh", "ps1", "yml", "yaml", "toml", "sql", "csproj", "sln", "slnx", "props", "targets"],
        ["type_app"] = ["app", "exe", "msi", "apk", "aab", "ipa", "appx", "msix"],
    });

    private static readonly Dictionary<string, ImageSource> Sources = [];

    public static ImageSource For(StorageItem item)
    {
        var name = item.IsFolder ? (item.Extension == "app" ? "type_app" : "folder")
            : Kinds.GetValueOrDefault(item.Extension.ToLowerInvariant(), "type_text");
        if (!Sources.TryGetValue(name, out var source))
            Sources[name] = source = $"{name}.png";
        return source;
    }

    private static Dictionary<string, string> Build(Dictionary<string, string[]> kinds) =>
        kinds.SelectMany(k => k.Value.Select(extension => (extension, k.Key))).ToDictionary(p => p.extension, p => p.Key);
}
