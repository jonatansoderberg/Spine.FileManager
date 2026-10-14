using Spine.FileManager.Storage;

namespace Spine.FileManager.Operations;

public enum DropAction { None, Copy, Move }

/// <summary>
/// What dropping items on a folder does, by Finder's and Explorer's rule: on the same volume they move, onto another
/// they are copied; the copy key (Option / Ctrl) or the move key (Command / Shift) decides instead. A folder never goes
/// into itself or below itself, and moving items to the folder they are already in does nothing.
/// </summary>
public static class DropRule
{
    public static DropAction Choose(IReadOnlyList<StorageLocation> items, StorageLocation target, Func<StorageLocation, StorageLocation?> parentOf,
        Func<StorageLocation, string?> volumeOf, bool copyKey = false, bool moveKey = false)
    {
        if (items.Count == 0)
            return DropAction.None;
        for (StorageLocation? folder = target; folder is not null; folder = parentOf(folder))
            if (items.Contains(folder))
                return DropAction.None;
        var action = copyKey ? DropAction.Copy
            : moveKey ? DropAction.Move
            : volumeOf(target) is { } volume && items.All(i => i.Provider == target.Provider && volumeOf(i) == volume) ? DropAction.Move
            : DropAction.Copy;
        return action == DropAction.Move && items.All(i => target.Equals(parentOf(i))) ? DropAction.None : action;
    }
}
