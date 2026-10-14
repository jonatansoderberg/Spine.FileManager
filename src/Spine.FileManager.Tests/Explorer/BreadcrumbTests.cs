using Spine.FileManager.Explorer;
using Spine.FileManager.Storage.FileSystem;
using Spine.FileManager.Tests.Storage;

namespace Spine.FileManager.Tests.Explorer;

public sealed class BreadcrumbTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FileSystemStorageProvider _provider = new(new DefaultFileSystemPlatform());

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Segments_run_from_the_root_to_the_folder()
    {
        var deep = FileSystemStorageProvider.Location(_temp.Folder("a/b"));
        var segments = Breadcrumb.Segments(_provider, deep);
        Assert.Equal(Path.GetPathRoot(_temp.Path), segments[0].ItemId);
        Assert.Equal(deep, segments[^1]);
        Assert.Equal(Path.Combine(_temp.Path, "a"), segments[^2].ItemId);
    }

    [Fact]
    public async Task Drop_down_lists_subfolders_only_in_natural_order()
    {
        _temp.Folder("item10");
        _temp.Folder("item2");
        _temp.Folder(".hidden");
        _temp.File("file.txt");
        var root = FileSystemStorageProvider.Location(_temp.Path);

        var names = (await Breadcrumb.SubfoldersAsync(_provider, root, includeHidden: false)).Select(f => f.Name);
        Assert.Equal(["item2", "item10"], names);

        var withHidden = await Breadcrumb.SubfoldersAsync(_provider, root, includeHidden: true);
        Assert.Equal(3, withHidden.Count);
    }
}
