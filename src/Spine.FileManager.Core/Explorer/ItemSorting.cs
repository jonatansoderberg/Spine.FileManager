using Spine.FileManager.Storage;

namespace Spine.FileManager.Explorer;

public enum SortColumn { Name, Modified, Size, Kind }

/// <summary>A list's order: folders always first, then the column, ties broken by name A–Z whichever way the column runs.</summary>
public sealed record ItemSort(SortColumn Column = SortColumn.Name, bool Descending = false)
{
    public ItemSort Toggle(SortColumn column) => column == Column ? this with { Descending = !Descending } : new(column, column is SortColumn.Modified or SortColumn.Size);

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
            return order != 0 ? order : names.Compare(a.DisplayName, b.DisplayName);
        });
    }
}
