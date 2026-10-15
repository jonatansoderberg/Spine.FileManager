using System.Text.Json;
using Spine.FileManager.Storage;

namespace Spine.FileManager.Services;

/// <summary>A place hidden from the sidebar, with the name it had, for Settings' list.</summary>
public sealed record HiddenPlace(string Key, string Name);

/// <summary>
/// Places the user has hidden from the sidebar (right-click › Hide from Sidebar), such as a cloud folder a disabled
/// provider left behind; kept in Preferences and shown again from Settings.
/// </summary>
public static class HiddenPlaces
{
    private const string PreferenceKey = "sidebar.hidden";

    private static List<HiddenPlace>? _items;

    public static event Action? Changed;

    public static IReadOnlyList<HiddenPlace> Items => _items ??= Load();

    public static string KeyOf(StorageLocation location) => $"{location.Provider.Value}|{location.ItemId}";

    public static bool Contains(StorageLocation location) => Items.Any(h => h.Key == KeyOf(location));

    public static void Hide(StorageLocation location, string name)
    {
        if (Contains(location))
            return;
        Save([.. Items, new HiddenPlace(KeyOf(location), name)]);
    }

    public static void Show(HiddenPlace place) => Save([.. Items.Where(h => h.Key != place.Key)]);

    private static void Save(List<HiddenPlace> items)
    {
        _items = items;
        Preferences.Set(PreferenceKey, JsonSerializer.Serialize(items));
        Changed?.Invoke();
    }

    private static List<HiddenPlace> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<List<HiddenPlace>>(Preferences.Get(PreferenceKey, "[]")) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
