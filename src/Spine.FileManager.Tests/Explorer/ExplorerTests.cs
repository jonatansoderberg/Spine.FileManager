using System.Globalization;
using Spine.FileManager.Explorer;
using Spine.FileManager.Storage;

namespace Spine.FileManager.Tests.Explorer;

public sealed class ExplorerTests
{
    private static StorageLocation L(string path) => new(new("local"), path, path, path);

    private static StorageItem File(string name, long size = 0, int day = 1) =>
        new(L("/" + name), name, StorageItemKind.File) { Size = size, Modified = new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero) };

    private static StorageItem Folder(string name) => new(L("/" + name), name, StorageItemKind.Folder);

    [Fact]
    public void History_goes_back_and_forward_and_drops_forward_on_a_new_folder()
    {
        var history = new NavigationHistory();
        history.Navigate(L("/a"));
        history.Navigate(L("/b"));
        history.Navigate(L("/c"));
        Assert.Equal(L("/b"), history.GoBack());
        Assert.Equal(L("/a"), history.GoBack());
        Assert.False(history.CanGoBack);
        Assert.Equal(L("/b"), history.GoForward());
        history.Navigate(L("/d"));
        Assert.False(history.CanGoForward);
        Assert.Equal(L("/b"), history.GoBack());
    }

    [Fact]
    public void Navigating_to_the_current_folder_adds_nothing()
    {
        var history = new NavigationHistory();
        history.Navigate(L("/a"));
        history.Navigate(L("/a"));
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void Sorts_folders_first_then_by_the_column()
    {
        StorageItem[] items = [File("b.txt", 300, 3), Folder("Zeta"), File("a10.txt", 100, 1), File("a2.txt", 200, 2), Folder("alpha")];
        string Names(ItemSort sort) => string.Join(",", items.Order(sort.Comparer(new NaturalComparer(CultureInfo.InvariantCulture))).Select(i => i.Name));

        Assert.Equal("alpha,Zeta,a2.txt,a10.txt,b.txt", Names(new()));
        Assert.Equal("Zeta,alpha,b.txt,a10.txt,a2.txt", Names(new(SortColumn.Name, true)));
        Assert.Equal("alpha,Zeta,b.txt,a2.txt,a10.txt", Names(new(SortColumn.Size, true)));
        Assert.Equal("alpha,Zeta,a10.txt,a2.txt,b.txt", Names(new(SortColumn.Modified)));
    }

    [Theory]
    [InlineData(SortColumn.Name, false)]
    [InlineData(SortColumn.Name, true)]
    [InlineData(SortColumn.Kind, false)]
    [InlineData(SortColumn.Size, true)]
    public void Keyed_sort_orders_exactly_as_the_comparer(SortColumn column, bool descending)
    {
        var random = new Random(7);
        string[] stems = ["file", "File", "ärende", "apa", "Zebra", "item 2", "item 10", "a01", "a1", "x", "", "Å", "äpple", "v1.2.10", "v1.2.9"];
        string[] extensions = ["", ".txt", ".PDF", ".jpg", ".tar.gz"];
        var items = Enumerable.Range(0, 400).Select(i =>
        {
            var name = $"{stems[random.Next(stems.Length)]}{(random.Next(3) == 0 ? random.Next(200).ToString() : "")}{extensions[random.Next(extensions.Length)]}";
            if (name.Length == 0)
                name = "z";
            return random.Next(5) == 0 ? Folder(name) : File(name, random.Next(10), random.Next(1, 28));
        }).ToArray();
        var sort = new ItemSort(column, descending);
        var names = new NaturalComparer(CultureInfo.GetCultureInfo("sv-SE"));

        var expected = items.Order(sort.Comparer(names)).Select(i => i.Name).ToArray();
        var keyed = (StorageItem[])items.Clone();
        sort.Sort(keyed, names);

        Assert.Equal(expected, keyed.Select(i => i.Name));
    }

    [Fact]
    public void Clicking_a_column_again_reverses_it_and_a_new_column_starts_in_its_natural_order()
    {
        var sort = new ItemSort();
        sort = sort.Toggle(SortColumn.Name);
        Assert.True(sort.Descending);
        sort = sort.Toggle(SortColumn.Modified);
        Assert.Equal(new ItemSort(SortColumn.Modified, Descending: true), sort);
    }

    [Fact]
    public void Selection_follows_click_ctrl_click_and_shift_click()
    {
        var selection = new Selection();
        selection.Click(2);
        Assert.Equal([2], selection.Indices);
        selection.Click(5, toggle: true);
        Assert.Equal([2, 5], selection.Indices);
        selection.Click(2, toggle: true);
        Assert.Equal([5], selection.Indices);
        // The anchor is the last row clicked without Shift, even when that click deselected it.
        selection.Click(1, extend: true);
        Assert.Equal([1, 2], selection.Indices);
        selection.Click(5);
        selection.Click(1, extend: true);
        Assert.Equal([1, 2, 3, 4, 5], selection.Indices);
        selection.Click(3);
        Assert.Equal([3], selection.Indices);
    }

    [Fact]
    public void Arrow_keys_move_or_extend_the_selection_within_the_list()
    {
        var selection = new Selection();
        selection.Step(1, 4);
        Assert.Equal([0], selection.Indices);
        selection.Step(1, 4);
        selection.Step(1, 4, extend: true);
        Assert.Equal([1, 2], selection.Indices);
        selection.Step(10, 4);
        Assert.Equal([3], selection.Indices);
        selection.Step(-1, 4);
        Assert.Equal([2], selection.Indices);
    }

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(999, "999 bytes")]
    [InlineData(1_000, "1 KB")]
    [InlineData(1_234_567, "1.2 MB")]
    [InlineData(48_000_000, "48 MB")]
    [InlineData(999_960_000, "1 GB")]
    public void Formats_sizes(long bytes, string text) => Assert.Equal(text, ByteSize.Format(bytes, CultureInfo.InvariantCulture));

    [Fact]
    public void Formats_sizes_with_the_cultures_decimal_separator() =>
        Assert.Equal("1,2 MB", ByteSize.Format(1_234_567, CultureInfo.GetCultureInfo("sv-SE")));
}
