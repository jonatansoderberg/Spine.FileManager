using Spine.FileManager.Operations;
using Spine.FileManager.Storage;
using Spine.FileManager.Storage.FileSystem;
using Spine.FileManager.Tests.Storage;

namespace Spine.FileManager.Tests.Operations;

public sealed class OperationQueueTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly OperationQueue _queue;

    public OperationQueueTests() => _queue = new(new StorageProviders([new FileSystemStorageProvider(new DefaultFileSystemPlatform())]));

    public void Dispose() => _temp.Dispose();

    private static StorageLocation L(string path) => FileSystemStorageProvider.Location(path);

    private async Task<FileOperation> Run(OperationKind kind, StorageLocation target, params string[] sources)
    {
        var operation = _queue.Enqueue(kind, sources.Select(L), target);
        await _queue.WaitAsync(operation);
        return operation;
    }

    [Fact]
    public async Task Copies_files_and_folders_with_their_contents()
    {
        var file = _temp.File("src/a.txt", "hello");
        _temp.File("src/sub/b.txt", "deep");
        var target = L(_temp.Folder("dst"));

        var operation = await Run(OperationKind.Copy, target, file, Path.Combine(_temp.Path, "src"));

        Assert.Equal(OperationState.Completed, operation.State);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(_temp.Path, "dst", "a.txt")));
        Assert.Equal("deep", File.ReadAllText(Path.Combine(_temp.Path, "dst", "src", "sub", "b.txt")));
        Assert.True(File.Exists(file));
        Assert.Equal(operation.Progress.ItemsTotal, operation.Progress.ItemsDone);
        Assert.Equal(operation.Progress.BytesTotal, operation.Progress.BytesDone);
        Assert.Equal(2, operation.Results.Count);
    }

    [Fact]
    public async Task Moves_and_removes_the_source()
    {
        var file = _temp.File("a.txt", "x");
        var folder = _temp.Folder("dir");
        _temp.File("dir/in.txt");
        var target = L(_temp.Folder("dst"));

        var operation = await Run(OperationKind.Move, target, file, folder);

        Assert.Equal(OperationState.Completed, operation.State);
        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(Path.Combine(_temp.Path, "dst", "dir", "in.txt")));
    }

    [Fact]
    public async Task A_copy_into_its_own_folder_becomes_a_numbered_copy()
    {
        var file = _temp.File("report.pdf", "1");
        var operation = await Run(OperationKind.Copy, L(_temp.Path), file);
        Assert.Equal("report (2).pdf", Path.GetFileName(operation.Results.Single().ItemId));
    }

    [Theory]
    [InlineData(ConflictChoice.Replace, "new", 1)]
    [InlineData(ConflictChoice.Skip, "old", 1)]
    [InlineData(ConflictChoice.KeepBoth, "old", 2)]
    public async Task A_taken_name_is_resolved_as_answered(ConflictChoice choice, string content, int files)
    {
        var source = _temp.File("src/note.txt", "new");
        _temp.File("dst/note.txt", "old");
        var asked = 0;
        _queue.ResolveConflict = (_, conflict) =>
        {
            asked++;
            Assert.Equal("note.txt", conflict.Existing.Name);
            return Task.FromResult(new ConflictAnswer(choice));
        };

        await Run(OperationKind.Copy, L(Path.Combine(_temp.Path, "dst")), source);

        Assert.Equal(1, asked);
        Assert.Equal(content, File.ReadAllText(Path.Combine(_temp.Path, "dst", "note.txt")));
        Assert.Equal(files, Directory.GetFiles(Path.Combine(_temp.Path, "dst")).Length);
    }

    [Fact]
    public async Task A_folder_onto_a_folder_merges_and_asks_per_file()
    {
        _temp.File("src/Photos/a.jpg", "new a");
        _temp.File("src/Photos/b.jpg", "b");
        _temp.File("dst/Photos/a.jpg", "old a");
        _temp.File("dst/Photos/c.jpg", "c");
        var questions = new List<string>();
        _queue.ResolveConflict = (_, conflict) =>
        {
            questions.Add(conflict.Source.Name);
            return Task.FromResult(new ConflictAnswer(ConflictChoice.Replace));
        };

        await Run(OperationKind.Copy, L(Path.Combine(_temp.Path, "dst")), Path.Combine(_temp.Path, "src", "Photos"));

        Assert.Equal(["Photos", "a.jpg"], questions);
        var photos = Path.Combine(_temp.Path, "dst", "Photos");
        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], Directory.GetFiles(photos).Select(Path.GetFileName).Order());
        Assert.Equal("new a", File.ReadAllText(Path.Combine(photos, "a.jpg")));
    }

    [Fact]
    public async Task An_answer_for_all_is_not_asked_again()
    {
        for (var i = 0; i < 3; i++)
        {
            _temp.File($"src/f{i}.txt", "new");
            _temp.File($"dst/f{i}.txt", "old");
        }
        var asked = 0;
        _queue.ResolveConflict = (_, _) =>
        {
            asked++;
            return Task.FromResult(new ConflictAnswer(ConflictChoice.Skip, ForAll: true));
        };
        var sources = Enumerable.Range(0, 3).Select(i => Path.Combine(_temp.Path, "src", $"f{i}.txt")).ToArray();

        await Run(OperationKind.Copy, L(Path.Combine(_temp.Path, "dst")), sources);

        Assert.Equal(1, asked);
        Assert.All(Directory.GetFiles(Path.Combine(_temp.Path, "dst")), f => Assert.Equal("old", File.ReadAllText(f)));
    }

    [Fact]
    public async Task Stop_cancels_the_operation()
    {
        var source = _temp.File("src/x.txt", "new");
        _temp.File("dst/x.txt", "old");
        _queue.ResolveConflict = (_, _) => Task.FromResult(new ConflictAnswer(ConflictChoice.Stop));
        var operation = await Run(OperationKind.Copy, L(Path.Combine(_temp.Path, "dst")), source);
        Assert.Equal(OperationState.Cancelled, operation.State);
    }

    [Fact]
    public async Task A_folder_cannot_go_into_itself()
    {
        var folder = _temp.Folder("outer");
        var inner = _temp.Folder("outer/inner");
        var operation = await Run(OperationKind.Copy, L(inner), folder);
        Assert.Equal(OperationState.Failed, operation.State);
        Assert.False(Directory.Exists(Path.Combine(inner, "outer")));
    }

    [Fact]
    public async Task A_large_file_is_streamed_and_can_be_cancelled_midway()
    {
        var big = Path.Combine(_temp.Path, "big.bin");
        await using (var stream = File.Create(big))
            stream.SetLength(OperationQueue.NativeCopyLimit * 4);
        var target = L(_temp.Folder("dst"));
        var queue = new OperationQueue(new StorageProviders([new FileSystemStorageProvider(new DefaultFileSystemPlatform())])) { ReportsPerSecond = 1000 };
        queue.Changed += op =>
        {
            if (op.Progress.BytesDone > 0)
                op.Cancel();
        };
        var operation = queue.Enqueue(OperationKind.Copy, [L(big)], target);

        await queue.WaitAsync(operation);

        Assert.Equal(OperationState.Cancelled, operation.State);
    }

    [Fact]
    public async Task Operations_run_one_at_a_time_in_order()
    {
        var finished = new List<string>();
        _queue.Changed += op =>
        {
            if (op.State == OperationState.Completed)
                lock (finished)
                    finished.Add(Path.GetFileName(op.Sources[0].ItemId));
        };
        var target = L(_temp.Folder("dst"));
        var operations = Enumerable.Range(0, 5).Select(i => _queue.Enqueue(OperationKind.Copy, [L(_temp.File($"f{i}.txt"))], target)).ToList();
        foreach (var operation in operations)
            await _queue.WaitAsync(operation);
        Assert.Equal(5, finished.Count);
    }
}
