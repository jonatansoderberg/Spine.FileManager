using Spine.FileManager.Storage;

namespace Spine.FileManager.Operations;

public enum OperationKind { Copy, Move }

public enum OperationState { Queued, Preparing, Running, Completed, Failed, Cancelled }

/// <summary>What to do when an item of the same name is already in the target folder.</summary>
public enum ConflictChoice { Replace, Skip, KeepBoth, Stop }

public sealed record Conflict(StorageItem Source, StorageItem Existing, int Remaining);

public sealed record ConflictAnswer(ConflictChoice Choice, bool ForAll = false);

/// <summary>How far an operation has come. Byte totals cover files; folders count as items.</summary>
public sealed record OperationProgress(long BytesDone, long BytesTotal, int ItemsDone, int ItemsTotal, string? Current, double BytesPerSecond)
{
    public static readonly OperationProgress None = new(0, 0, 0, 0, null, 0);

    public double Fraction => BytesTotal > 0 ? (double)BytesDone / BytesTotal : ItemsTotal > 0 ? (double)ItemsDone / ItemsTotal : 0;
}

/// <summary>
/// One copy or move the explorer asked for. The explorer never copies files itself; it enqueues one of these and
/// watches it (docs/plan.md §10). Persisting it across restarts is milestone B's journal.
/// </summary>
public sealed class FileOperation
{
    internal readonly CancellationTokenSource Cancellation = new();

    internal FileOperation(OperationKind kind, IReadOnlyList<StorageLocation> sources, StorageLocation target)
    {
        Kind = kind;
        Sources = sources;
        Target = target;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public OperationKind Kind { get; }
    public IReadOnlyList<StorageLocation> Sources { get; }
    public StorageLocation Target { get; }
    public OperationState State { get; internal set; } = OperationState.Queued;
    public OperationProgress Progress { get; internal set; } = OperationProgress.None;

    /// <summary>The items that failed and why; the operation goes on with the rest.</summary>
    public List<(StorageLocation Item, string Error)> Errors { get; } = [];

    /// <summary>The top-level items as they ended up in the target, for selecting them when the operation is done.</summary>
    public List<StorageLocation> Results { get; } = [];

    public bool IsFinished => State is OperationState.Completed or OperationState.Failed or OperationState.Cancelled;

    public void Cancel() => Cancellation.Cancel();
}
