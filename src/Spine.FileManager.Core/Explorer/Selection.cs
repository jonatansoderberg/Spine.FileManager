namespace Spine.FileManager.Explorer;

/// <summary>
/// Which rows of a list are selected, with the desktop rules: a click selects one row, Ctrl/Cmd-click adds or removes
/// one, Shift-click selects from the anchor to the row.
/// </summary>
public sealed class Selection
{
    private readonly SortedSet<int> _selected = [];

    public IReadOnlyCollection<int> Indices => _selected;

    public int Count => _selected.Count;

    /// <summary>The row Shift-click extends from: the last row clicked without Shift.</summary>
    public int? Anchor { get; private set; }

    public bool Contains(int index) => _selected.Contains(index);

    public void Click(int index, bool toggle = false, bool extend = false)
    {
        if (extend && Anchor is { } anchor)
        {
            if (!toggle)
                _selected.Clear();
            for (var i = Math.Min(anchor, index); i <= Math.Max(anchor, index); i++)
                _selected.Add(i);
            return;
        }
        if (toggle)
        {
            if (!_selected.Remove(index))
                _selected.Add(index);
        }
        else
        {
            _selected.Clear();
            _selected.Add(index);
        }
        Anchor = index;
    }

    /// <summary>
    /// The rows a selection rectangle covers, from <paramref name="first"/> to <paramref name="last"/> in either order,
    /// on top of <paramref name="kept"/>: what was selected when the rectangle began with Cmd/Ctrl held, else nothing.
    /// </summary>
    public void Box(int first, int last, IReadOnlyCollection<int> kept)
    {
        _selected.Clear();
        _selected.UnionWith(kept);
        for (var i = Math.Max(0, Math.Min(first, last)); i <= Math.Max(first, last); i++)
            _selected.Add(i);
        Anchor = first >= 0 ? first : null;
    }

    public void SelectAll(int count)
    {
        _selected.Clear();
        for (var i = 0; i < count; i++)
            _selected.Add(i);
    }

    public void Clear()
    {
        _selected.Clear();
        Anchor = null;
    }

    /// <summary>Moves the selection one row up or down from the anchor, as the arrow keys do; Shift extends it.</summary>
    public void Step(int delta, int count, bool extend = false)
    {
        if (count == 0)
            return;
        var from = extend && _selected.Count > 0 ? (delta > 0 ? _selected.Max : _selected.Min) : Anchor ?? (delta > 0 ? -1 : count);
        var to = Math.Clamp(from + delta, 0, count - 1);
        if (extend)
            _selected.Add(to);
        else
            Click(to);
    }
}
