using Spine.FileManager.Explorer;
using Spine.FileManager.Storage.FileSystem;
using Spine.FileManager.Tests.Storage;

namespace Spine.FileManager.Tests.Explorer;

public sealed class FileSearchTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FileSystemStorageProvider _provider = new(new DefaultFileSystemPlatform());

    public void Dispose() => _temp.Dispose();

    private async Task<List<string>> Search(string query, bool includeHidden = false, int limit = 100, CancellationToken cancel = default)
    {
        var names = new List<string>();
        await foreach (var item in FileSearch.SearchAsync(_provider, FileSystemStorageProvider.Location(_temp.Path), query, includeHidden, limit, cancel))
            names.Add(Path.GetRelativePath(_temp.Path, item.Location.ItemId).Replace('\\', '/'));
        return names;
    }

    [Fact]
    public async Task Finds_names_in_every_subfolder_nearest_first()
    {
        _temp.File("deep/er/report-2026.pdf");
        _temp.File("Report.txt");
        _temp.File("deep/notes.txt");
        _temp.Folder("deep/reports");

        Assert.Equal(["Report.txt", "deep/reports", "deep/er/report-2026.pdf"], await Search("report"));
    }

    [Fact]
    public async Task Leaves_out_hidden_items_unless_asked_and_does_not_enter_packages()
    {
        _temp.File(".hidden-report.txt");
        _temp.File("Tool.app/Contents/report.txt");

        Assert.Empty(await Search("report"));
        Assert.Equal([".hidden-report.txt"], await Search("report", includeHidden: true));
    }

    [Fact]
    public async Task Stops_at_the_limit_and_when_cancelled()
    {
        for (var i = 0; i < 5; i++)
            _temp.File($"a{i}.txt");

        Assert.Equal(2, (await Search("a", limit: 2)).Count);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Search("a", cancel: new CancellationToken(canceled: true)));
        Assert.Empty(await Search("  "));
    }
}
