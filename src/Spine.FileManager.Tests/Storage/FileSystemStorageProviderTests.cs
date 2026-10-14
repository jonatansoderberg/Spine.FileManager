using Spine.FileManager.Storage;
using Spine.FileManager.Storage.FileSystem;

namespace Spine.FileManager.Tests.Storage;

public sealed class FileSystemStorageProviderTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeTrash _platform = new();
    private readonly FileSystemStorageProvider _provider;

    public FileSystemStorageProviderTests() => _provider = new(_platform);

    public void Dispose() => _temp.Dispose();

    private StorageLocation Root => FileSystemStorageProvider.Location(_temp.Path);

    private async Task<List<StorageItem>> List(StorageLocation folder)
    {
        var items = new List<StorageItem>();
        await foreach (var item in _provider.EnumerateAsync(folder))
            items.Add(item);
        return items;
    }

    [Fact]
    public async Task Lists_files_and_folders_with_size_date_and_hidden_flag()
    {
        _temp.File("a.txt", "hello");
        _temp.File(".hidden");
        _temp.Folder("sub");

        var items = (await List(Root)).ToDictionary(i => i.Name);

        Assert.Equal(3, items.Count);
        Assert.Equal(StorageItemKind.File, items["a.txt"].Kind);
        Assert.Equal(5, items["a.txt"].Size);
        Assert.Equal("txt", items["a.txt"].Extension);
        Assert.NotNull(items["a.txt"].Modified);
        Assert.True(items[".hidden"].IsHidden);
        Assert.Equal(StorageItemKind.Folder, items["sub"].Kind);
        Assert.Null(items["sub"].Size);
        Assert.Equal(Path.Combine(_temp.Path, "sub"), items["sub"].Location.NativePath);
    }

    [Fact]
    public async Task Listing_a_missing_folder_is_not_found()
    {
        var missing = FileSystemStorageProvider.Location(Path.Combine(_temp.Path, "nope"));
        var error = await Assert.ThrowsAsync<StorageException>(() => List(missing));
        Assert.Equal(StorageErrorKind.NotFound, error.Kind);
    }

    [Fact]
    public async Task Listing_stops_when_cancelled()
    {
        for (var i = 0; i < 3000; i++)
            _temp.File($"f{i}");
        using var cancel = new CancellationTokenSource();
        var seen = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _provider.EnumerateAsync(Root, cancel.Token))
                if (++seen == 10)
                    cancel.Cancel();
        });
        Assert.Equal(10, seen);
    }

    [Fact]
    public async Task Creates_renames_and_refuses_an_existing_name()
    {
        var folder = await _provider.CreateFolderAsync(Root, "New");
        Assert.True(Directory.Exists(folder.Location.ItemId));

        var renamed = await _provider.RenameAsync(folder.Location, "Renamed");
        Assert.Equal("Renamed", renamed.Name);
        Assert.False(Directory.Exists(folder.Location.ItemId));

        _temp.File("taken.txt");
        var error = await Assert.ThrowsAsync<StorageException>(() => _provider.RenameAsync(renamed.Location, "taken.txt"));
        Assert.Equal(StorageErrorKind.AlreadyExists, error.Kind);
        error = await Assert.ThrowsAsync<StorageException>(() => _provider.CreateFolderAsync(Root, "Renamed"));
        Assert.Equal(StorageErrorKind.AlreadyExists, error.Kind);
    }

    [Fact]
    public async Task Rename_to_another_case_of_the_same_name_works()
    {
        var file = FileSystemStorageProvider.Location(_temp.File("readme.md"));
        var renamed = await _provider.RenameAsync(file, "README.md");
        Assert.Equal("README.md", renamed.Name);
        Assert.Contains("README.md", Directory.GetFiles(_temp.Path).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("name.")]
    [InlineData("CON")]
    public async Task Refuses_invalid_names(string name)
    {
        var error = await Assert.ThrowsAsync<StorageException>(() => _provider.CreateFolderAsync(Root, name));
        Assert.Equal(StorageErrorKind.InvalidName, error.Kind);
    }

    [Fact]
    public async Task Writes_and_reads_back_a_file_and_will_not_overwrite_unless_asked()
    {
        await using (var stream = await _provider.OpenWriteAsync(Root, "data.bin"))
            await stream.WriteAsync(new byte[] { 1, 2, 3 });

        var file = FileSystemStorageProvider.Location(Path.Combine(_temp.Path, "data.bin"));
        await using (var stream = await _provider.OpenReadAsync(file))
        {
            var buffer = new byte[3];
            await stream.ReadExactlyAsync(buffer);
            Assert.Equal(new byte[] { 1, 2, 3 }, buffer);
        }

        var error = await Assert.ThrowsAsync<StorageException>(() => _provider.OpenWriteAsync(Root, "data.bin"));
        Assert.Equal(StorageErrorKind.AlreadyExists, error.Kind);
        await using (await _provider.OpenWriteAsync(Root, "data.bin", overwrite: true)) { }
        Assert.Equal(0, new FileInfo(file.ItemId).Length);
    }

    [Fact]
    public async Task Copies_and_moves_files_and_folders()
    {
        _temp.File("src/one.txt", "1");
        _temp.File("src/deep/two.txt", "2");
        var target = FileSystemStorageProvider.Location(_temp.Folder("target"));
        var source = FileSystemStorageProvider.Location(Path.Combine(_temp.Path, "src"));

        var copy = await _provider.CopyAsync(source, target);
        Assert.Equal("2", File.ReadAllText(Path.Combine(copy.Location.ItemId, "deep", "two.txt")));
        Assert.True(Directory.Exists(source.ItemId));

        var moved = await _provider.MoveAsync(source, target, "moved");
        Assert.False(Directory.Exists(source.ItemId));
        Assert.True(File.Exists(Path.Combine(moved.Location.ItemId, "one.txt")));

        var error = await Assert.ThrowsAsync<StorageException>(() => _provider.CopyAsync(moved.Location, target, "moved"));
        Assert.Equal(StorageErrorKind.AlreadyExists, error.Kind);
    }

    [Fact]
    public async Task A_folder_cannot_move_into_itself()
    {
        var outer = FileSystemStorageProvider.Location(_temp.Folder("outer"));
        var inner = FileSystemStorageProvider.Location(_temp.Folder("outer/inner"));
        var error = await Assert.ThrowsAsync<StorageException>(() => _provider.MoveAsync(outer, inner));
        Assert.Equal(StorageErrorKind.NotSupported, error.Kind);
    }

    [Fact]
    public async Task Deletes_to_the_trash_or_permanently()
    {
        var first = FileSystemStorageProvider.Location(_temp.File("first.txt"));
        var second = FileSystemStorageProvider.Location(_temp.Folder("second"));

        await _provider.DeleteAsync(first, StorageDeleteMode.Trash);
        Assert.Equal([first.ItemId], _platform.Trashed);

        await _provider.DeleteAsync(second, StorageDeleteMode.Permanent);
        Assert.False(Directory.Exists(second.ItemId));
    }

    [Fact]
    public async Task Trash_is_a_capability_only_where_the_platform_has_one()
    {
        Assert.True((await _provider.GetCapabilitiesAsync(Root)).HasFlag(StorageCapabilities.Trash));

        var noTrash = new FileSystemStorageProvider(new DefaultFileSystemPlatform());
        Assert.False((await noTrash.GetCapabilitiesAsync(Root)).HasFlag(StorageCapabilities.Trash));
        var error = await Assert.ThrowsAsync<StorageException>(() => noTrash.DeleteAsync(FileSystemStorageProvider.Location(_temp.File("x")), StorageDeleteMode.Trash));
        Assert.Equal(StorageErrorKind.NotSupported, error.Kind);
    }

    [Fact]
    public async Task A_refused_write_drops_the_folders_modify_capabilities()
    {
        if (OperatingSystem.IsWindows())
            return;
        var locked = _temp.Folder("locked");
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var folder = FileSystemStorageProvider.Location(locked);
            var error = await Assert.ThrowsAsync<StorageException>(() => _provider.CreateFolderAsync(folder, "x"));
            Assert.Equal(StorageErrorKind.AccessDenied, error.Kind);
            var capabilities = await _provider.GetCapabilitiesAsync(folder);
            Assert.True(capabilities.HasFlag(StorageCapabilities.List));
            Assert.False(capabilities.HasFlag(StorageCapabilities.Write));
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task Reports_a_created_file_while_watching()
    {
        using var created = new SemaphoreSlim(0);
        StorageChange? change = null;
        using var watch = _provider.Watch(Root, c =>
        {
            if (c.Kind == StorageChangeKind.Created)
            {
                change = c;
                created.Release();
            }
        });
        _temp.File("new.txt");
        Assert.True(await created.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("new.txt", Path.GetFileName(change!.Location.ItemId));
    }

    [Fact]
    public void Parent_walks_up_to_the_root_and_then_stops()
    {
        var root = Path.GetPathRoot(_temp.Path)!;
        var location = FileSystemStorageProvider.Location(_temp.Path);
        var steps = 0;
        while (_provider.GetParent(location) is { } parent)
        {
            location = parent;
            steps++;
        }
        Assert.True(steps > 0);
        Assert.Equal(root, location.ItemId);
    }

    [Fact]
    public void Locations_are_equal_by_provider_and_id_only()
    {
        var a = new StorageLocation(new("local"), "/x", "/x");
        var b = new StorageLocation(new("local"), "/x", "Shown differently", "/x");
        var c = new StorageLocation(new("saf"), "/x", "/x");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public async Task Roots_come_from_the_platform()
    {
        var roots = await _provider.GetRootsAsync();
        Assert.Equal([new StorageRoot(FileSystemStorageProvider.Location(Path.GetTempPath()), "Temp", StorageRootKind.Home)], roots);
    }

    private sealed class FakeTrash : DefaultFileSystemPlatform
    {
        public List<string> Trashed { get; } = [];

        public override bool CanTrash(string path) => true;

        public override Task MoveToTrashAsync(string path, CancellationToken cancellationToken)
        {
            Trashed.Add(path);
            File.Delete(path);
            return Task.CompletedTask;
        }

        public override IReadOnlyList<(string Path, string Name, StorageRootKind Kind)> GetRoots() => [(Path.GetTempPath(), "Temp", StorageRootKind.Home)];
    }
}
