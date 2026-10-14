using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spine.FileManager.Icons;

/// <summary>
/// A set of file icons the user adds: a folder holding a <c>theme.json</c> and its pictures. The file names an icon per
/// extension, per kind (<see cref="FileKinds"/>), for folders and for any other file; an extension wins over its kind,
/// a kind over the general file icon. Whatever the theme leaves out keeps the app's own icon.
/// </summary>
public sealed class IconTheme
{
    public const string FileName = "theme.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly string _folder;
    private readonly Document _document;

    private IconTheme(string folder, Document document)
    {
        _folder = folder;
        _document = document;
    }

    /// <summary>Its name: the file's <c>name</c>, or the folder's.</summary>
    public string Name => _document.Name ?? Path.GetFileName(_folder);

    public string Folder => _folder;

    /// <summary>Reads a theme from its folder; null when it has no theme file or the file cannot be read.</summary>
    public static IconTheme? Load(string folder)
    {
        try
        {
            var path = Path.Combine(folder, FileName);
            return File.Exists(path) && JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Json) is { } document
                ? new IconTheme(folder, document)
                : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The themes in <paramref name="root"/>, one per folder, by name.</summary>
    public static IReadOnlyList<IconTheme> All(string root) =>
        Directory.Exists(root)
            ? [.. Directory.EnumerateDirectories(root).Select(Load).OfType<IconTheme>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)]
            : [];

    /// <summary>The picture for a folder or for a file with <paramref name="extension"/>, when the theme has one and it exists.</summary>
    public string? IconFor(bool isFolder, string extension)
    {
        var name = isFolder
            ? _document.Folder
            : Find(_document.Extensions, extension) ?? Find(_document.Kinds, FileKinds.Of(extension)) ?? _document.File;
        if (name is null)
            return null;
        var path = Path.GetFullPath(Path.Combine(_folder, name));
        // A theme's pictures stay inside its own folder.
        return path.StartsWith(Path.GetFullPath(_folder) + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(path) ? path : null;
    }

    private static string? Find(Dictionary<string, string>? map, string key) =>
        map?.FirstOrDefault(p => string.Equals(p.Key.TrimStart('.'), key, StringComparison.OrdinalIgnoreCase)).Value;

    private sealed record Document(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("folder")] string? Folder,
        [property: JsonPropertyName("file")] string? File,
        [property: JsonPropertyName("kinds")] Dictionary<string, string>? Kinds,
        [property: JsonPropertyName("extensions")] Dictionary<string, string>? Extensions);
}
