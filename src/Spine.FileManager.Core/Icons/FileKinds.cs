namespace Spine.FileManager.Icons;

/// <summary>What kind of file an extension is, for its icon: the spec's concept images give each kind its own.</summary>
public static class FileKinds
{
    public const string Document = "document", Spreadsheet = "spreadsheet", Presentation = "presentation", Pdf = "pdf",
        Image = "image", Video = "video", Audio = "audio", Archive = "archive", Code = "code", App = "app", Text = "text";

    private static readonly Dictionary<string, string> ByExtension = new Dictionary<string, string[]>
    {
        [Document] = ["doc", "docx", "odt", "rtf", "pages"],
        [Spreadsheet] = ["xls", "xlsx", "ods", "csv", "tsv", "numbers"],
        [Presentation] = ["ppt", "pptx", "odp", "key"],
        [Pdf] = ["pdf"],
        [Image] = ["jpg", "jpeg", "png", "gif", "heic", "heif", "webp", "bmp", "tif", "tiff", "svg", "raw", "dng", "cr2", "nef", "psd", "fig", "sketch"],
        [Video] = ["mp4", "mov", "m4v", "avi", "mkv", "webm", "wmv", "mpg", "mpeg"],
        [Audio] = ["mp3", "wav", "aac", "flac", "m4a", "ogg", "aiff", "wma"],
        [Archive] = ["zip", "rar", "7z", "tar", "gz", "tgz", "bz2", "xz", "dmg", "iso", "pkg"],
        [Code] = ["cs", "js", "ts", "tsx", "jsx", "py", "java", "c", "cpp", "h", "hpp", "swift", "kt", "go", "rs", "rb", "php", "json", "xml", "html", "css", "scss", "xaml", "sh", "ps1", "yml", "yaml", "toml", "sql", "csproj", "sln", "slnx", "props", "targets"],
        [App] = ["app", "exe", "msi", "apk", "aab", "ipa", "appx", "msix"],
    }.SelectMany(k => k.Value.Select(extension => (extension, k.Key))).ToDictionary(p => p.extension, p => p.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The kind for an extension (without the dot); text for anything not listed.</summary>
    public static string Of(string extension) => ByExtension.GetValueOrDefault(extension, Text);
}
