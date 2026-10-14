using Spine.FileManager.Storage;

namespace Spine.FileManager.Explorer;

/// <summary>Back and forward through the folders a pane has shown, like a browser's history.</summary>
public sealed class NavigationHistory
{
    private readonly Stack<StorageLocation> _back = new();
    private readonly Stack<StorageLocation> _forward = new();

    public StorageLocation? Current { get; private set; }

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    /// <summary>Goes to <paramref name="location"/>; the forward history is dropped, as in a browser.</summary>
    public void Navigate(StorageLocation location)
    {
        if (location.Equals(Current))
            return;
        if (Current is not null)
            _back.Push(Current);
        _forward.Clear();
        Current = location;
    }

    public StorageLocation? GoBack()
    {
        if (!CanGoBack)
            return null;
        _forward.Push(Current!);
        return Current = _back.Pop();
    }

    public StorageLocation? GoForward()
    {
        if (!CanGoForward)
            return null;
        _back.Push(Current!);
        return Current = _forward.Pop();
    }

    /// <summary>Leaves the current folder for one that replaces it in the history, after it was renamed or removed.</summary>
    public void Replace(StorageLocation location) => Current = location;
}
