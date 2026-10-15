using Spine.FileManager.DeviceLink;
using Spine.FileManager.Operations;
using Spine.FileManager.Storage;
using Spine.FileManager.Storage.FileSystem;
using Spine.FileManager.Tests.Storage;

namespace Spine.FileManager.Tests.Operations;

public sealed class SendOperationTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly StorageProviders _providers = new([new FileSystemStorageProvider(new DefaultFileSystemPlatform())]);

    public void Dispose() => _temp.Dispose();

    private OperationQueue Queue() => new(_providers, new OperationJournal(Path.Combine(_temp.Path, "journal")));

    private static readonly StorageLocation Phone = new(new StorageProviderId("device"), "phone-node", "iPhone");

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task A_send_runs_through_the_apps_sender_and_waits_while_paused()
    {
        var queue = Queue();
        var sent = 0;
        var gate = new SemaphoreSlim(0);
        queue.Sender = async (operation, progress, checkpoint, cancel) =>
        {
            for (var piece = 1; piece <= 4; piece++)
            {
                await checkpoint(cancel);
                Interlocked.Increment(ref sent);
                progress(piece * 25, 100);
                if (piece == 1)
                    await gate.WaitAsync(cancel);
            }
        };
        var send = queue.Enqueue(OperationKind.Send, [FileSystemStorageProvider.Location(_temp.File("a.txt"))], Phone);
        await Until(() => sent == 1);
        queue.Pause(send);
        gate.Release();
        await Task.Delay(300);
        Assert.Equal(1, sent);

        queue.Continue(send);
        await queue.WaitAsync(send).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(OperationState.Completed, send.State);
        Assert.Equal(4, sent);
        Assert.Equal(1.0, send.Progress.Fraction);
    }

    [Fact]
    public async Task A_declined_send_is_cancelled_with_its_reason_and_a_cut_one_fails()
    {
        var queue = Queue();
        queue.Sender = (operation, _, _, _) => operation.Sources.Count == 1
            ? throw new TransferDeclinedException(timedOut: false, "iPhone declined the files")
            : throw new LinkNode.UnreachableException("The other device could not be reached at any of its addresses.");
        var declined = queue.Enqueue(OperationKind.Send, [FileSystemStorageProvider.Location(_temp.File("a.txt"))], Phone);
        await queue.WaitAsync(declined).WaitAsync(TimeSpan.FromSeconds(10));
        var cut = queue.Enqueue(OperationKind.Send, [FileSystemStorageProvider.Location(_temp.File("b.txt")), FileSystemStorageProvider.Location(_temp.File("c.txt"))], Phone);
        await queue.WaitAsync(cut).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(OperationState.Cancelled, declined.State);
        Assert.Equal("iPhone declined the files", Assert.Single(declined.Errors).Message);
        Assert.Equal(OperationState.Failed, cut.State);
        Assert.Equal(OperationKind.Send, Assert.Single(Queue().Operations, o => o.Id == cut.Id).Kind);
    }
}
