using System.Text.Json;
using Spine.FileManager.Explorer;
using Spine.FileManager.Storage;

namespace Spine.FileManager.Workspaces;

/// <summary>One tab as it is saved: where it is and how it shows (its history is not kept).</summary>
public sealed record TabState(StorageLocation Location, ItemSort Sort, bool ShowHidden, bool IsIcons);

public sealed record WindowState(IReadOnlyList<TabState> Tabs, int Active);

/// <summary>
/// The open windows and their tabs, saved as they change and restored at the next start: the "last session" of
/// plan §18. Named workspaces are saved sessions of the same shape.
/// </summary>
public sealed class SessionStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public IReadOnlyList<WindowState> Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<WindowState>>(File.ReadAllText(path), Json) ?? [] : [];
        }
        catch (JsonException)
        {
            File.Move(path, path + ".broken", overwrite: true);
            return [];
        }
    }

    public void Save(IReadOnlyList<WindowState> windows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(windows, Json));
        File.Move(temporary, path, overwrite: true);
    }
}
