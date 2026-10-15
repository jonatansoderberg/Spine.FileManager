using System.Text.Json;
using Spine.FileManager.Storage;

namespace Spine.FileManager.Services;

/// <summary>
/// The view the user picked for a folder, list or icons, kept in Preferences as Finder keeps it per folder. A folder
/// with no choice shows in the view the tab already has. The most recent choices are kept.
/// </summary>
public static class FolderViews
{
    private const string PreferenceKey = "folder.views";
    private const int Kept = 1000;

    private static Dictionary<string, bool>? _icons;

    private static Dictionary<string, bool> Icons => _icons ??= Load();

    private static string KeyOf(StorageLocation location) => $"{location.Provider.Value}|{location.ItemId}";

    /// <summary>True for icons, false for the list, null when the folder has no choice of its own.</summary>
    public static bool? Get(StorageLocation location) => Icons.TryGetValue(KeyOf(location), out var icons) ? icons : null;

    public static void Set(StorageLocation location, bool icons)
    {
        var key = KeyOf(location);
        // Re-added at the end, so the oldest choices are the ones dropped.
        Icons.Remove(key);
        Icons[key] = icons;
        foreach (var old in Icons.Keys.Take(Math.Max(0, Icons.Count - Kept)).ToList())
            Icons.Remove(old);
        Preferences.Set(PreferenceKey, JsonSerializer.Serialize(Icons));
    }

    private static Dictionary<string, bool> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(Preferences.Get(PreferenceKey, "{}")) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
