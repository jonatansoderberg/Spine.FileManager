using Spine.FileManager.Operations;

namespace Spine.FileManager.Services;

/// <summary>
/// Edit › Undo for the explorer: every finished copy or move is recorded, as are renames and new folders, and the last
/// one is taken back. Moves back go through the queue like any other move; they are not recorded themselves.
/// </summary>
public sealed class UndoService
{
    private readonly OperationQueue _operations;
    private readonly StorageProviders _providers;
    private readonly UndoHistory _history = new();
    private readonly HashSet<Guid> _recorded = [];

    public UndoService(OperationQueue operations, StorageProviders providers)
    {
        _operations = operations;
        _providers = providers;
        operations.Changed += operation =>
        {
            if (operation.State != OperationState.Completed)
                return;
            lock (_recorded)
                if (!_recorded.Add(operation.Id))
                    return;
            if (UndoSteps.For(operation, l => _providers[l].GetParent(l)) is { } step)
                _history.Push(step);
        };
    }

    public bool CanUndo => _history.CanUndo;

    public void Record(UndoStep step) => _history.Push(step);

    public async Task UndoAsync()
    {
        switch (_history.Pop())
        {
            case TrashItems trash:
                foreach (var item in trash.Items)
                    await _providers[item].DeleteAsync(item, StorageDeleteMode.Trash);
                break;
            case MoveBack back:
                foreach (var group in back.Groups)
                    lock (_recorded)
                        _recorded.Add(_operations.Enqueue(OperationKind.Move, group.Items, group.Folder).Id);
                break;
            case RenameBack rename:
                await _providers[rename.Item].RenameAsync(rename.Item, rename.Name);
                break;
        }
    }
}
