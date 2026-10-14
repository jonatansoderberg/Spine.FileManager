using System.Text.Json;
using Spine.FileManager.Storage;

namespace Spine.FileManager.Favorites;

/// <summary>A place the user keeps in the sidebar: any provider's folder, under the name it had when added.</summary>
public sealed record Favorite(StorageLocation Location, string Name);

/// <summary>
/// The one list of favorites (docs/plan.md §15: not Favorites and Pinned and Quick access), kept as JSON in a file.
/// A favorite stays when its folder is gone or offline; the sidebar shows it as unavailable.
/// </summary>
public sealed class FavoritesStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private List<Favorite>? _items;

    public event Action? Changed;

    public IReadOnlyList<Favorite> Items => _items ??= Load();

    public bool Contains(StorageLocation location) => Items.Any(f => f.Location.Equals(location));

    public void Add(StorageLocation location, string name)
    {
        if (Contains(location))
            return;
        _items = [.. Items, new Favorite(location, name)];
        Save();
    }

    public void Remove(StorageLocation location)
    {
        _items = [.. Items.Where(f => !f.Location.Equals(location))];
        Save();
    }

    /// <summary>Puts the favorites in this order, for the sidebar's drag to reorder.</summary>
    public void Reorder(IEnumerable<StorageLocation> order)
    {
        var byLocation = Items.ToDictionary(f => f.Location);
        _items = [.. order.Where(byLocation.ContainsKey).Select(l => byLocation[l]), .. Items.Where(f => !order.Contains(f.Location))];
        Save();
    }

    private List<Favorite> Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<Favorite>>(File.ReadAllText(path), Json) ?? [] : [];
        }
        catch (JsonException)
        {
            // A damaged file must not take the sidebar down; it is kept aside rather than overwritten.
            File.Move(path, path + ".broken", overwrite: true);
            return [];
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_items, Json));
        File.Move(temporary, path, overwrite: true);
        Changed?.Invoke();
    }
}
