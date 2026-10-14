using Spine.FileManager.Operations;
using Spine.FileManager.Storage;
using Spine.FileManager.Storage.FileSystem;
using Spine.FileManager.Tests.Storage;

namespace Spine.FileManager.Tests.Operations;

public sealed class UndoTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FileSystemStorageProvider _provider = new(new DefaultFileSystemPlatform());
    private readonly OperationQueue _queue;

    public UndoTests() => _queue = new(new StorageProviders([_provider]));

    public void Dispose() => _temp.Dispose();

    private static StorageLocation L(string path) => FileSystemStorageProvider.Location(path);

    private async Task<FileOperation> Run(OperationKind kind, StorageLocation target, params StorageLocation[] sources)
    {
        var operation = _queue.Enqueue(kind, sources, target);
        await _queue.WaitAsync(operation).WaitAsync(TimeSpan.FromSeconds(30));
        return operation;
    }

    [Fact]
    public async Task Undoing_a_copy_trashes_the_copies()
    {
        var file = L(_temp.File("a.txt", "x"));
        var operation = await Run(OperationKind.Copy, L(_temp.Folder("dst")), file);

        var step = Assert.IsType<TrashItems>(UndoSteps.For(operation, _provider.GetParent));
        Assert.Equal([L(Path.Combine(_temp.Path, "dst", "a.txt"))], step.Items);
    }

    [Fact]
    public async Task Undoing_a_move_takes_each_item_back_to_its_own_folder()
    {
        var a = L(_temp.File("one/a.txt", "a"));
        var b = L(_temp.File("two/b.txt", "b"));
        var target = L(_temp.Folder("dst"));
        var operation = await Run(OperationKind.Move, target, a, b);

        var step = Assert.IsType<MoveBack>(UndoSteps.For(operation, _provider.GetParent));
        Assert.Equal(2, step.Groups.Count);
        foreach (var group in step.Groups)
            await Run(OperationKind.Move, group.Folder, [.. group.Items]);

        Assert.Equal("a", File.ReadAllText(Path.Combine(_temp.Path, "one", "a.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(_temp.Path, "two", "b.txt")));
        Assert.Empty(Directory.GetFileSystemEntries(target.ItemId));
    }

    [Fact]
    public async Task An_operation_that_placed_nothing_has_nothing_to_undo()
    {
        var file = L(_temp.File("a.txt"));
        var operation = await Run(OperationKind.Move, L(_temp.Path), file);
        Assert.Null(UndoSteps.For(operation, _provider.GetParent));
    }

    [Fact]
    public void The_history_keeps_the_newest_steps()
    {
        var history = new UndoHistory(limit: 2);
        history.Push(new RenameBack(L("/a"), "1"));
        history.Push(new RenameBack(L("/a"), "2"));
        history.Push(new RenameBack(L("/a"), "3"));
        Assert.Equal("3", Assert.IsType<RenameBack>(history.Pop()).Name);
        Assert.Equal("2", Assert.IsType<RenameBack>(history.Pop()).Name);
        Assert.Null(history.Pop());
        Assert.False(history.CanUndo);
    }
}
