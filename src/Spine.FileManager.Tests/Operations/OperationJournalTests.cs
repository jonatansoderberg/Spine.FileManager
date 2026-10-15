using Spine.FileManager.Operations;
using Spine.FileManager.Storage;
using Spine.FileManager.Storage.FileSystem;
using Spine.FileManager.Tests.Storage;

namespace Spine.FileManager.Tests.Operations;

public sealed class OperationJournalTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly StorageProviders _providers = new([new FileSystemStorageProvider(new DefaultFileSystemPlatform())]);

    public void Dispose() => _temp.Dispose();

    private string JournalFolder => Path.Combine(_temp.Path, "journal");

    private OperationQueue Queue(int reports = 10) => new(_providers, new OperationJournal(JournalFolder)) { ReportsPerSecond = reports };

    private static StorageLocation L(string path) => FileSystemStorageProvider.Location(path);

    private string Big(string name, long size)
    {
        var path = Path.Combine(_temp.Path, "src", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        stream.SetLength(size);
        return path;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task A_finished_operation_is_in_the_history_after_a_restart()
    {
        var queue = Queue();
        var operation = queue.Enqueue(OperationKind.Copy, [L(_temp.File("src/a.txt"))], L(_temp.Folder("dst")));
        await queue.WaitAsync(operation);

        var restored = Queue().Operations.Single();
        Assert.Equal(operation.Id, restored.Id);
        Assert.Equal(OperationState.Completed, restored.State);
        Assert.Equal(operation.Results, restored.Results);
    }

    [Fact]
    public async Task An_operation_cut_off_midway_resumes_without_doing_its_finished_files_again()
    {
        var a = _temp.File("src/a.txt", "a");
        var b = _temp.File("src/b.txt", "b");
        var big = Big("big.bin", OperationQueue.NativeCopyLimit * 8);
        var target = L(_temp.Folder("dst"));

        // The first run is paused inside the big file and then abandoned, as if the app had crashed there.
        var crashed = Queue(reports: 1000);
        var paused = 0;
        crashed.Changed += op =>
        {
            if (op.State == OperationState.Running && op.Progress.BytesDone > 4 && Interlocked.Exchange(ref paused, 1) == 0)
                crashed.Pause(op);
        };
        var first = crashed.Enqueue(OperationKind.Copy, [L(a), L(b), L(big)], target);
        await Until(() => first.State == OperationState.Paused);
        Assert.Contains(Directory.GetFiles(target.ItemId), f => f.EndsWith(".spine-part"));

        var queue = Queue();
        var interrupted = queue.Operations.Single();
        Assert.Equal(OperationState.Interrupted, interrupted.State);
        queue.ResolveConflict = (_, conflict) => throw new InvalidOperationException($"asked about {conflict.Source.Name}");
        queue.Resume(interrupted);
        await queue.WaitAsync(interrupted).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(OperationState.Completed, interrupted.State);
        Assert.Equal(["a.txt", "b.txt", "big.bin"], Directory.GetFiles(target.ItemId).Select(Path.GetFileName).Order());
        Assert.Equal(new FileInfo(big).Length, new FileInfo(Path.Combine(target.ItemId, "big.bin")).Length);
        Assert.Equal(2, interrupted.Attempts);
        first.Cancel();
    }

    [Fact]
    public async Task Queued_operations_run_in_the_order_the_user_puts_them()
    {
        var queue = Queue(reports: 1000);
        var target = L(_temp.Folder("dst"));
        var blocker = queue.Enqueue(OperationKind.Copy, [L(Big("big.bin", OperationQueue.NativeCopyLimit * 8))], target);
        var paused = 0;
        queue.Changed += op =>
        {
            if (op == blocker && op.State == OperationState.Running && op.Progress.BytesDone > 0 && Interlocked.Exchange(ref paused, 1) == 0)
                queue.Pause(op);
        };
        await Until(() => blocker.State == OperationState.Paused);

        var finished = new List<string>();
        queue.Changed += op =>
        {
            if (op.State == OperationState.Completed && op != blocker)
                lock (finished)
                    if (!finished.Contains(Path.GetFileName(op.Sources[0].ItemId)))
                        finished.Add(Path.GetFileName(op.Sources[0].ItemId));
        };
        var b = queue.Enqueue(OperationKind.Copy, [L(_temp.File("src/b.txt"))], target);
        var c = queue.Enqueue(OperationKind.Copy, [L(_temp.File("src/c.txt"))], target);
        queue.Move(c, -1);
        queue.Continue(blocker);
        await queue.WaitAsync(b).WaitAsync(TimeSpan.FromSeconds(30));
        await queue.WaitAsync(c).WaitAsync(TimeSpan.FromSeconds(30));
        // An operation counts as finished before the queue reports it, from its own thread: wait for both reports.
        await Until(() => { lock (finished) return finished.Count == 2; });

        lock (finished)
            Assert.Equal(["c.txt", "b.txt"], finished);
    }

    [Fact]
    public async Task A_removed_operation_leaves_the_journal()
    {
        var queue = Queue();
        var operation = queue.Enqueue(OperationKind.Copy, [L(_temp.File("src/a.txt"))], L(_temp.Folder("dst")));
        await queue.WaitAsync(operation);
        queue.Remove(operation);
        Assert.Empty(Queue().Operations);
    }
}
