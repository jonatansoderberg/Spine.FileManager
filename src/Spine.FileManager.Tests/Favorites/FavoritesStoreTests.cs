using Spine.FileManager.Favorites;
using Spine.FileManager.Storage;
using Spine.FileManager.Tests.Storage;

namespace Spine.FileManager.Tests.Favorites;

public sealed class FavoritesStoreTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string File => Path.Combine(_temp.Path, "favorites.json");

    private static StorageLocation L(string id, string provider = "local") => new(new(provider), id, id, id);

    [Fact]
    public void Adds_once_removes_and_survives_a_restart()
    {
        var store = new FavoritesStore(File);
        var changes = 0;
        store.Changed += () => changes++;
        store.Add(L("/a"), "a");
        store.Add(L("/a"), "a again");
        store.Add(L("dcim", "saf"), "DCIM");
        store.Remove(L("/missing"));

        var reloaded = new FavoritesStore(File);
        Assert.Equal(["a", "DCIM"], reloaded.Items.Select(f => f.Name));
        Assert.Equal(new StorageProviderId("saf"), reloaded.Items[1].Location.Provider);
        Assert.True(reloaded.Contains(L("/a")));
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Reorders_and_keeps_favorites_left_out_of_the_order()
    {
        var store = new FavoritesStore(File);
        foreach (var name in new[] { "a", "b", "c" })
            store.Add(L("/" + name), name);
        store.Reorder([L("/c"), L("/a")]);
        Assert.Equal(["c", "a", "b"], new FavoritesStore(File).Items.Select(f => f.Name));
    }

    [Fact]
    public void A_damaged_file_gives_an_empty_list_and_is_kept_aside()
    {
        System.IO.File.WriteAllText(File, "{ not json");
        Assert.Empty(new FavoritesStore(File).Items);
        Assert.True(System.IO.File.Exists(File + ".broken"));
    }
}
