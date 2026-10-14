using Spine.FileManager.Storage;

namespace Spine.FileManager.Operations;

public enum OperationKind { Copy, Move }

/// <summary>
/// Where an operation is. <see cref="Interrupted"/> is one the app found unfinished in its journal at start: it was
/// running when the app quit or crashed, and waits to be resumed.
/// </summary>
public enum OperationState { Queued, Preparing, Running, Paused, Completed, Failed, Cancelled, Interrupted }

/// <summary>What to do when an item of the same name is already in the target folder.</summary>
public enum ConflictChoice { Replace, Skip, KeepBoth, Stop }

public sealed record Conflict(StorageItem Source, StorageItem Existing, int Remaining);

public sealed record ConflictAnswer(ConflictChoice Choice, bool ForAll = false);

public sealed record OperationError(StorageLocation Item, string Message);

public sealed record Placed(StorageLocation From, StorageLocation To);

/// <summary>How far an operation has come. Byte totals cover files; folders count as items.</summary>
public sealed record OperationProgress(long BytesDone, long BytesTotal, int ItemsDone, int ItemsTotal, string? Current, double BytesPerSecond)
{
    public static readonly OperationProgress None = new(0, 0, 0, 0, null, 0);

    public double Fraction => BytesTotal > 0 ? (double)BytesDone / BytesTotal : ItemsTotal > 0 ? (double)ItemsDone / ItemsTotal : 0;
}

/// <summary>
/// One copy or move the explorer asked for. The explorer never copies files itself; it enqueues one of these and
/// watches it (docs/plan.md §10). The queue journals it, so it survives a crash and can be resumed.
/// </summary>
public sealed class FileOperation
{
    internal CancellationTokenSource Cancellation = new();
    internal readonly ManualResetEventSlim Running = new(true);

    internal FileOperation(Guid id, OperationKind kind, IReadOnlyList<StorageLocation> sources, StorageLocation target, DateTimeOffset created)
    {
        Id = id;
        Kind = kind;
        Sources = sources;
        Target = target;
        Created = created;
        Updated = created;
    }

    public Guid Id { get; }
    public OperationKind Kind { get; }
    public IReadOnlyList<StorageLocation> Sources { get; }
    public StorageLocation Target { get; }
    public DateTimeOffset Created { get; }
    public DateTimeOffset Updated { get; internal set; }
    public OperationState State { get; internal set; } = OperationState.Queued;
    public OperationProgress Progress { get; internal set; } = OperationProgress.None;

    /// <summary>How many times it has been started: 1 for a first run, more after a resume or retry.</summary>
    public int Attempts { get; internal set; }

    /// <summary>The items that failed and why; the operation goes on with the rest.</summary>
    public List<OperationError> Errors { get; } = [];

    /// <summary>The top-level items as they ended up in the target, for selecting them when the operation is done.</summary>
    public List<StorageLocation> Results { get; } = [];

    /// <summary>Each top-level item's source and where it ended up, for undoing the operation. Not journaled: undo is for this run.</summary>
    public List<Placed> Placed { get; } = [];

    /// <summary>Target items finished, file by file and folder by folder: a resume skips them.</summary>
    internal HashSet<string> Done { get; } = [];

    /// <summary>Target folders this operation created: a resume goes on inside them without asking.</summary>
    internal HashSet<string> CreatedFolders { get; } = [];

    /// <summary>The target file being written when the journal was last saved: a resume removes it and writes it again.</summary>
    internal StorageLocation? Writing { get; set; }

    public bool IsFinished => State is OperationState.Completed or OperationState.Failed or OperationState.Cancelled;

    public bool CanResume => State is OperationState.Interrupted or OperationState.Failed or OperationState.Cancelled;

    public void Cancel()
    {
        Cancellation.Cancel();
        Running.Set();
    }
}
