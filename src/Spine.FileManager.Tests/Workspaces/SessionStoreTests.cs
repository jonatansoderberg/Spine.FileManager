using Spine.FileManager.Explorer;
using Spine.FileManager.Storage;
using Spine.FileManager.Tests.Storage;
using Spine.FileManager.Workspaces;

namespace Spine.FileManager.Tests.Workspaces;

public sealed class SessionStoreTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static StorageLocation L(string id) => new(new("local"), id, id, id);

    [Fact]
    public void Windows_and_their_tabs_come_back_as_saved()
    {
        var path = Path.Combine(_temp.Path, "session.json");
        WindowState[] windows =
        [
            new([new(L("/a"), new ItemSort(SortColumn.Size, true), true, false), new(L("/b"), new ItemSort(), false, true)], 1),
            new([new(L("/c"), new ItemSort(SortColumn.Kind), false, false)], 0),
        ];
        new SessionStore(path).Save(windows);

        var restored = new SessionStore(path).Load();

        Assert.Equal(2, restored.Count);
        Assert.Equal(1, restored[0].Active);
        Assert.Equal(windows[0].Tabs, restored[0].Tabs);
        Assert.Equal(new ItemSort(SortColumn.Kind), restored[1].Tabs[0].Sort);
    }

    [Fact]
    public void No_file_or_a_damaged_one_gives_no_windows()
    {
        var path = Path.Combine(_temp.Path, "session.json");
        Assert.Empty(new SessionStore(path).Load());
        File.WriteAllText(path, "[{");
        Assert.Empty(new SessionStore(path).Load());
        Assert.True(File.Exists(path + ".broken"));
    }
}
