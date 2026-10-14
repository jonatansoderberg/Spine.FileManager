namespace Spine.FileManager.Explorer;

/// <summary>
/// A pane's tabs and which one is active, with a browser's rules: a new tab opens right after the active one, and
/// closing the active tab activates the one to its right, or to its left when it was the last.
/// </summary>
public sealed class TabList<T> where T : class
{
    private readonly List<T> _tabs = [];

    public TabList(T first)
    {
        _tabs.Add(first);
        Active = first;
    }

    public IReadOnlyList<T> Tabs => _tabs;

    public T Active { get; private set; }

    public int ActiveIndex => _tabs.IndexOf(Active);

    public event Action? Changed;

    public void Add(T tab, bool activate = true)
    {
        _tabs.Insert(ActiveIndex + 1, tab);
        if (activate)
            Active = tab;
        Changed?.Invoke();
    }

    /// <summary>Closes <paramref name="tab"/>; the last tab of a pane stays open.</summary>
    public bool Close(T tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0 || _tabs.Count == 1)
            return false;
        _tabs.RemoveAt(index);
        if (tab == Active)
            Active = _tabs[Math.Min(index, _tabs.Count - 1)];
        Changed?.Invoke();
        return true;
    }

    public void Activate(T tab)
    {
        if (!_tabs.Contains(tab) || tab == Active)
            return;
        Active = tab;
        Changed?.Invoke();
    }

    /// <summary>The next (+1) or previous (-1) tab, wrapping around.</summary>
    public void Step(int direction)
    {
        if (_tabs.Count > 1)
            Activate(_tabs[(ActiveIndex + direction + _tabs.Count) % _tabs.Count]);
    }

    /// <summary>
    /// A tab dropped on the bar before the tab at <paramref name="index"/> (or at the end), counted as the bar shows
    /// it before the drop: one of this list's own moves there, one from elsewhere joins and becomes active.
    /// </summary>
    public void Drop(T tab, int index)
    {
        if (_tabs.IndexOf(tab) is var from and >= 0)
        {
            Move(tab, from < index ? index - 1 : index);
            return;
        }
        _tabs.Insert(Math.Clamp(index, 0, _tabs.Count), tab);
        Active = tab;
        Changed?.Invoke();
    }

    public void Move(T tab, int toIndex)
    {
        if (!_tabs.Remove(tab))
            return;
        _tabs.Insert(Math.Clamp(toIndex, 0, _tabs.Count), tab);
        Changed?.Invoke();
    }
}
