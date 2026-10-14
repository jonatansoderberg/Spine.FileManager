using Spine.FileManager.Storage;

namespace Spine.FileManager.Explorer;

public enum SortColumn { Name, Modified, Size, Kind }

/// <summary>A list's order: folders always first, then the column, ties broken by name A–Z whichever way the column runs.</summary>
public sealed record ItemSort(SortColumn Column = SortColumn.Name, bool Descending = false)
{
    public ItemSort Toggle(SortColumn column) => column == Column ? this with { Descending = !Descending } : new(column, column is SortColumn.Modified or SortColumn.Size);

    /// <summary>Sorts <paramref name="items"/> in place, with each name's key computed once.</summary>
    public void Sort(StorageItem[] items, NaturalComparer? names = null)
    {
        var comparer = names ?? NaturalComparer.Current;
        // The keys are the costly part (the culture's sort key per run), so they are made in parallel, and for every
        // column: a size or date sort breaks its many ties by name.
        var keys = new NaturalKey[items.Length];
        var kinds = Column == SortColumn.Kind ? new NaturalKey[items.Length] : null;
        Parallel.For(0, items.Length, i =>
        {
            keys[i] = comparer.GetKey(items[i].DisplayName);
            if (kinds is not null)
                kinds[i] = comparer.GetKey(items[i].Extension);
        });
        NaturalKey Name(int i) => keys[i];
        var order = new int[items.Length];
        for (var i = 0; i < order.Length; i++)
            order[i] = i;
        Array.Sort(order, (i, j) =>
        {
            StorageItem a = items[i], b = items[j];
            if (a.IsFolder != b.IsFolder)
                return a.IsFolder ? -1 : 1;
            var result = Column switch
            {
                SortColumn.Modified => Nullable.Compare(a.Modified, b.Modified),
                SortColumn.Size => Nullable.Compare(a.Size, b.Size),
                SortColumn.Kind => kinds![i].CompareTo(kinds[j]),
                _ => Name(i).CompareTo(Name(j)),
            };
            if (Descending)
                result = -result;
            if (result == 0)
                result = Name(i).CompareTo(Name(j));
            return result != 0 ? result : string.CompareOrdinal(a.Name, b.Name);
        });
        var sorted = Array.ConvertAll(order, i => items[i]);
        sorted.CopyTo(items, 0);
    }

    public IComparer<StorageItem> Comparer(IComparer<string>? names = null)
    {
        names ??= NaturalComparer.Current;
        return Comparer<StorageItem>.Create((a, b) =>
        {
            if (a.IsFolder != b.IsFolder)
                return a.IsFolder ? -1 : 1;
            var order = Column switch
            {
                SortColumn.Modified => Nullable.Compare(a.Modified, b.Modified),
                SortColumn.Size => Nullable.Compare(a.Size, b.Size),
                SortColumn.Kind => names.Compare(a.Extension, b.Extension),
                _ => names.Compare(a.DisplayName, b.DisplayName),
            };
            if (Descending)
                order = -order;
            if (order == 0)
                order = names.Compare(a.DisplayName, b.DisplayName);
            // "File" and "file" can live side by side on a case-sensitive volume; keep their order fixed.
            return order != 0 ? order : string.CompareOrdinal(a.Name, b.Name);
        });
    }
}
