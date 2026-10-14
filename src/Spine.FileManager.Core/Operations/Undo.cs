using Spine.FileManager.Storage;

namespace Spine.FileManager.Operations;

/// <summary>What taking back one action does, as Finder does it.</summary>
public abstract record UndoStep;

/// <summary>Copies and new folders go to the Trash.</summary>
public sealed record TrashItems(IReadOnlyList<StorageLocation> Items) : UndoStep;

/// <summary>Moved items go back to the folders they came from, one move per folder.</summary>
public sealed record MoveBack(IReadOnlyList<MoveBackGroup> Groups) : UndoStep;

public sealed record MoveBackGroup(IReadOnlyList<StorageLocation> Items, StorageLocation Folder);

/// <summary>A renamed item gets its old name.</summary>
public sealed record RenameBack(StorageLocation Item, string Name) : UndoStep;

public static class UndoSteps
{
    /// <summary>The step that takes back a finished copy or move; null when it placed nothing.</summary>
    public static UndoStep? For(FileOperation operation, Func<StorageLocation, StorageLocation?> parentOf)
    {
        if (operation.State != OperationState.Completed || operation.Placed.Count == 0)
            return null;
        if (operation.Kind == OperationKind.Copy)
            return new TrashItems([.. operation.Placed.Select(p => p.To)]);
        var groups = operation.Placed
            .Select(p => (p.To, Folder: parentOf(p.From)))
            .Where(p => p.Folder is not null)
            .GroupBy(p => p.Folder!)
            .Select(g => new MoveBackGroup([.. g.Select(p => p.To)], g.Key))
            .ToList();
        return groups.Count > 0 ? new MoveBack(groups) : null;
    }
}

/// <summary>The actions that can be taken back, newest last; one history for the whole app, as in Finder.</summary>
public sealed class UndoHistory(int limit = 50)
{
    private readonly List<UndoStep> _steps = [];

    public event Action? Changed;

    public bool CanUndo
    {
        get { lock (_steps) return _steps.Count > 0; }
    }

    public void Push(UndoStep step)
    {
        lock (_steps)
        {
            _steps.Add(step);
            if (_steps.Count > limit)
                _steps.RemoveAt(0);
        }
        Changed?.Invoke();
    }

    public UndoStep? Pop()
    {
        UndoStep? step;
        lock (_steps)
        {
            if (_steps.Count == 0)
                return null;
            step = _steps[^1];
            _steps.RemoveAt(_steps.Count - 1);
        }
        Changed?.Invoke();
        return step;
    }
}
