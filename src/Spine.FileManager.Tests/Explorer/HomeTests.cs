using Spine.FileManager.Explorer;
using Spine.FileManager.Storage.FileSystem;
using Spine.FileManager.Tests.Storage;

namespace Spine.FileManager.Tests.Explorer;

public sealed class HomeTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_start_disk_comes_first_with_its_size()
    {
        var disks = Disks.List();
        Assert.NotEmpty(disks);
        Assert.True(disks[0].Location.ItemId is "/" or @"C:\");
        Assert.All(disks, d => Assert.InRange(d.UsedFraction, 0, 1));
        Assert.DoesNotContain(disks, d => d.Location.ItemId.StartsWith("/System/Volumes", StringComparison.Ordinal));
    }

    [Fact]
    public void Recent_locations_keep_the_newest_first_once_each_and_survive_a_restart()
    {
        var path = Path.Combine(_temp.Path, "recent.json");
        var recent = new RecentLocations(path, limit: 2);
        var a = FileSystemStorageProvider.Location(_temp.Folder("a"));
        var b = FileSystemStorageProvider.Location(_temp.Folder("b"));
        var c = FileSystemStorageProvider.Location(_temp.Folder("c"));
        recent.Add(a, "a", DateTimeOffset.Now);
        recent.Add(b, "b", DateTimeOffset.Now);
        recent.Add(a, "a", DateTimeOffset.Now);
        recent.Add(c, "c", DateTimeOffset.Now);

        Assert.Equal(["c", "a"], recent.Items.Select(r => r.Name));
        Assert.Equal(["c", "a"], new RecentLocations(path).Items.Select(r => r.Name));
    }
}
