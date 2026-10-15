using System.Text.Json;
using Spine.FileManager.Storage;

namespace Spine.FileManager.Explorer;

public sealed record RecentLocation(StorageLocation Location, string Name, DateTimeOffset At);

/// <summary>The folders opened lately, newest first, for Home; kept between runs.</summary>
public sealed class RecentLocations(string path, int limit = 12)
{
    private List<RecentLocation>? _items;

    public event Action? Changed;

    public IReadOnlyList<RecentLocation> Items => _items ??= Load();

    public void Add(StorageLocation location, string name, DateTimeOffset at)
    {
        var items = _items ??= Load();
        items.RemoveAll(r => r.Location.Equals(location));
        items.Insert(0, new RecentLocation(location, name, at));
        if (items.Count > limit)
            items.RemoveRange(limit, items.Count - limit);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(items));
        }
        catch (IOException)
        {
        }
        Changed?.Invoke();
    }

    private List<RecentLocation> Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<RecentLocation>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return [];
        }
    }
}
