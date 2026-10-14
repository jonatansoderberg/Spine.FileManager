using System.Collections.ObjectModel;
using Plugin.Maui.Spine.Common;
using Spine.FileManager.Explorer;
using Spine.FileManager.Services;

namespace Spine.FileManager.Pages;

public sealed partial class PlaceRow(StorageRoot root) : ObservableObject
{
    public StorageRoot Root { get; } = root;

    public string Name => Root.Name;

    public ImageSource Icon => Root.Kind switch
    {
        StorageRootKind.Cloud => "cloud.png",
        StorageRootKind.Volume or StorageRootKind.RemovableVolume or StorageRootKind.NetworkVolume => "drive.png",
        _ => "folder.png",
    };

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

public sealed partial class BreadcrumbSegment(StorageLocation location, string name) : ObservableObject
{
    public StorageLocation Location { get; } = location;

    [ObservableProperty]
    public partial string Name { get; set; } = name;

    /// <summary>
    /// The folder's subfolders for the drop-down behind the segment's chevron, set once when read: Spine rebuilds a native
    /// menu on every change to its items, and filling one row by row froze the window for 27 s at 500 subfolders.
    /// </summary>
    [ObservableProperty]
    public partial Plugin.Maui.Spine.Core.MenuItems? Children { get; set; }
}

/// <summary>How a list's rows are worded: the culture and the kind names, shared by every row of one listing.</summary>
public sealed class RowFormat(ISpineStrings strings)
{
    public System.Globalization.CultureInfo Culture { get; } = System.Globalization.CultureInfo.CurrentCulture;
    public string Folder { get; } = strings["Explorer.Kind.Folder"];
    public string Document { get; } = strings["Explorer.Kind.Document"];
    public string FileKind { get; } = strings["Explorer.Kind.File"];
}

/// <summary>
/// One row of the list. The texts are made when a cell asks for them, so a folder of 100 000 items costs 100 000 small
/// objects rather than 300 000 formatted strings before the first row shows.
/// </summary>
public sealed partial class ItemRow(StorageItem item, RowFormat format) : ObservableObject
{
    private static readonly ImageSource FolderIcon = "folder.png";
    private static readonly ImageSource FileIcon = "file.png";

    public StorageItem Item { get; } = item;
    public string Name => Item.DisplayName;
    public string Kind => Item.IsFolder ? format.Folder
        : Item.Extension.Length > 0 ? string.Format(format.Culture, format.FileKind, Item.Extension.ToUpperInvariant())
        : format.Document;
    public string Modified => Item.Modified?.ToLocalTime().ToString("g", format.Culture) ?? "";
    public string Size => Item.Size is { } size ? ByteSize.Format(size, format.Culture) : "";
    public ImageSource Icon => Item.IsFolder ? FolderIcon : FileIcon;
    public double Dimmed => Item.IsHidden ? 0.5 : 1;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// One pane of the explorer: the sidebar's places, a folder's items as a details list, the history, the selection and
/// the commands the toolbar, the menu bar and the keyboard run.
/// </summary>
public partial class ExplorerPageViewModel : ViewModelBase
{
    private readonly ISpineStrings _strings;
    private readonly StorageProviders _providers;
    private readonly NavigationHistory _history = new();
    private readonly Selection _selection = new();
    private List<StorageItem> _items = [];
    private CancellationTokenSource? _loading;
    private IDisposable? _watch;
    private CancellationTokenSource? _reload;

    private readonly INavigationService _navigation;

    public ExplorerPageViewModel(ISpineStrings strings, StorageProviders providers, INavigationService navigation)
    {
        _strings = strings;
        _providers = providers;
        _navigation = navigation;
        _strings.Changed += (_, _) => Rebuild(keepSelection: true);
    }

    public ObservableCollection<PlaceRow> Places { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<BreadcrumbSegment> Segments { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ItemRow> Rows { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PathText), nameof(CanGoUp))]
    public partial StorageLocation? Current { get; private set; }

    public string PathText => Current?.DisplayPath ?? "";

    [ObservableProperty]
    public partial bool CanGoBack { get; private set; }

    [ObservableProperty]
    public partial bool CanGoForward { get; private set; }

    public bool CanGoUp => Current is not null && _providers[Current].GetParent(Current) is not null;

    [ObservableProperty]
    public partial bool IsWide { get; set; } = true;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    [ObservableProperty]
    public partial string Status { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameHeader), nameof(ModifiedHeader), nameof(SizeHeader), nameof(KindHeader))]
    public partial ItemSort Sort { get; private set; } = new();

    [ObservableProperty]
    public partial bool ShowHidden { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsList))]
    public partial bool IsIcons { get; private set; }

    public bool IsList => !IsIcons;

    /// <summary>Columns of the icon grid, from the list's width.</summary>
    [ObservableProperty]
    public partial int GridSpan { get; set; } = 6;

    public string NameHeader => Header("Explorer.Column.Name", SortColumn.Name);
    public string ModifiedHeader => Header("Explorer.Column.Modified", SortColumn.Modified);
    public string SizeHeader => Header("Explorer.Column.Size", SortColumn.Size);
    public string KindHeader => Header("Explorer.Column.Kind", SortColumn.Kind);

    public IEnumerable<StorageItem> SelectedItems => _selection.Indices.Where(i => i < Rows.Count).Select(i => Rows[i].Item);

    private string Header(string key, SortColumn column) =>
        Sort.Column == column ? $"{_strings[key]} {(Sort.Descending ? "▼" : "▲")}" : _strings[key];

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (Places.Count > 0)
            return;
        foreach (var provider in _providers.All)
            foreach (var root in await provider.GetRootsAsync())
                Places.Add(new(root));
        if (Places.FirstOrDefault() is { } home)
            await Navigate(home.Root.Location);
        // Presented from the root page's first appearance the sheet never showed; after the first folder it does.
        if (FirstRunPageViewModel.IsNeeded)
            MainThread.BeginInvokeOnMainThread(async () => await _navigation.ShowAsync<FirstRunPage>());
    }

    [RelayCommand]
    private Task OpenPlace(PlaceRow place) => Navigate(place.Root.Location);

    [RelayCommand]
    private Task OpenLocation(StorageLocation location) => location.Equals(Current) ? Task.CompletedTask : Navigate(location);

    [RelayCommand]
    private async Task Back()
    {
        if (_history.GoBack() is { } location)
            await Load(location);
    }

    [RelayCommand]
    private async Task Forward()
    {
        if (_history.GoForward() is { } location)
            await Load(location);
    }

    [RelayCommand]
    private async Task Up()
    {
        if (Current is not null && _providers[Current].GetParent(Current) is { } parent)
        {
            var from = Current;
            await Navigate(parent);
            SelectWhere(item => item.Location.Equals(from));
        }
    }

    [RelayCommand]
    private Task Refresh() => Current is null ? Task.CompletedTask : Load(Current, keepSelection: true);

    [RelayCommand]
    private void Tap(ItemRow row)
    {
        var index = IndexOf(row);
        if (index < 0)
            return;
        _selection.Click(index, toggle: ModifierKeys.Command, extend: ModifierKeys.Shift);
        ApplySelection();
    }

    [RelayCommand]
    private async Task Activate(ItemRow? row)
    {
        var item = row?.Item ?? (_selection.Count == 1 ? SelectedItems.First() : null);
        if (item is null)
            return;
        if (item.IsFolder)
            await Navigate(item.Location);
        else if (!await FileLauncher.OpenAsync(item))
            Error = _strings.Get("Explorer.Error.CannotOpen", item.Name);
    }

    [RelayCommand]
    private void SelectAll()
    {
        _selection.SelectAll(Rows.Count);
        ApplySelection();
    }

    /// <summary>"1" or "-1" moves the selection, "+1" or "+-1" extends it.</summary>
    [RelayCommand]
    private void Step(string delta)
    {
        var extend = delta.StartsWith('+');
        _selection.Step(int.Parse(delta.TrimStart('+')), Rows.Count, extend);
        ApplySelection();
    }

    [RelayCommand]
    private void ToggleHidden()
    {
        ShowHidden = !ShowHidden;
        Rebuild(keepSelection: true);
        if (Current is { } location)
            _ = BuildBreadcrumb(_providers[location], location);
    }

    [RelayCommand]
    private void ShowAs(string view) => IsIcons = view == "Icons";

    [RelayCommand]
    private void SortBy(string column)
    {
        Sort = Sort.Toggle(Enum.Parse<SortColumn>(column));
        Rebuild(keepSelection: true);
    }

    [RelayCommand]
    private async Task NewFolder()
    {
        if (Current is not { } folder)
            return;
        var suggested = StorageNames.Unique(_strings["Explorer.NewFolder.Default"], name => _items.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)));
        var name = await Prompt("Explorer.NewFolder.Title", "Explorer.NewFolder.Message", suggested);
        if (string.IsNullOrWhiteSpace(name))
            return;
        await Try(async () =>
        {
            var created = await _providers[folder].CreateFolderAsync(folder, name.Trim());
            await Load(folder);
            SelectWhere(item => item.Location.Equals(created.Location));
        });
    }

    [RelayCommand]
    private async Task Rename()
    {
        if (Current is not { } folder || SelectedItems.ToList() is not [var item])
            return;
        var name = await Prompt("Explorer.Rename.Title", "Explorer.Rename.Message", item.Name);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name)
            return;
        await Try(async () =>
        {
            var renamed = await _providers[item.Location].RenameAsync(item.Location, name.Trim());
            await Load(folder);
            SelectWhere(i => i.Location.Equals(renamed.Location));
        });
    }

    [RelayCommand]
    private Task Delete() => Remove(permanently: false);

    [RelayCommand]
    private Task DeletePermanently() => Remove(permanently: true);

    private async Task Remove(bool permanently)
    {
        var items = SelectedItems.ToList();
        if (Current is not { } folder || items.Count == 0)
            return;
        var provider = _providers[folder];
        var trash = !permanently && (await provider.GetCapabilitiesAsync(folder)).HasFlag(StorageCapabilities.Trash);
        if (!trash)
        {
            var what = items.Count == 1 ? $"“{items[0].Name}”" : _strings.Get("Explorer.Items", items.Count);
            var reason = permanently ? "" : _strings["Explorer.Delete.NoTrash"] + " ";
            if (!await Confirm(_strings["Explorer.Delete.Title"], reason + _strings.Get("Explorer.Delete.Message", what), _strings["Explorer.Delete.Confirm"]))
                return;
        }
        var first = _selection.Indices.FirstOrDefault();
        await Try(async () =>
        {
            foreach (var item in items)
                await provider.DeleteAsync(item.Location, trash ? StorageDeleteMode.Trash : StorageDeleteMode.Permanent);
        });
        await Load(folder);
        if (Rows.Count > 0)
        {
            _selection.Click(Math.Min(first, Rows.Count - 1));
            ApplySelection();
        }
    }

    private async Task Navigate(StorageLocation location)
    {
        _history.Navigate(location);
        await Load(location);
    }

    private async Task Load(StorageLocation location, bool keepSelection = false)
    {
        _loading?.Cancel();
        var cancel = _loading = new CancellationTokenSource();
        var provider = _providers[location];
        var keep = keepSelection ? SelectedItems.Select(i => i.Location).ToHashSet() : null;
        var changedFolder = !location.Equals(Current);

        Current = location;
        if (changedFolder)
            _ = BuildBreadcrumb(provider, location);
        CanGoBack = _history.CanGoBack;
        CanGoForward = _history.CanGoForward;
        foreach (var place in Places)
            place.IsCurrent = place.Root.Location.Equals(location);
        Error = null;
        IsLoading = true;
        if (changedFolder)
        {
            _selection.Clear();
            _items = [];
            Rows = [];
            Watch(provider, location);
        }

        try
        {
            // Read and sorted off the UI thread; only the finished rows cross over.
            var (items, rows) = await Task.Run(async () =>
            {
                var items = new List<StorageItem>();
                await foreach (var item in provider.EnumerateAsync(location, cancel.Token))
                    items.Add(item);
                return (items, BuildRows(items));
            }, cancel.Token);
            _items = items;
            Show(rows, keep);
        }
        catch (OperationCanceledException)
        {
        }
        catch (StorageException e)
        {
            _items = [];
            Rebuild();
            Error = Describe(e);
        }
        finally
        {
            if (cancel == _loading)
                IsLoading = false;
        }
    }

    // The segments show at once with their file names; display names and each segment's subfolders follow.
    private async Task BuildBreadcrumb(IStorageProvider provider, StorageLocation location)
    {
        var segments = Breadcrumb.Segments(provider, location)
            .Select(l => new BreadcrumbSegment(l, Path.GetFileName(l.DisplayPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : l.DisplayPath))
            .ToList();
        Segments = segments;
        foreach (var segment in segments)
        {
            if (!location.Equals(Current))
                return;
            try
            {
                segment.Name = (await provider.GetItemAsync(segment.Location)).DisplayName;
                var menu = new Plugin.Maui.Spine.Core.MenuItems();
                foreach (var folder in await Breadcrumb.SubfoldersAsync(provider, segment.Location, ShowHidden))
                    menu.Add(new Plugin.Maui.Spine.Core.MenuAction(folder.DisplayName, "folder.svg", OpenLocationCommand) { CommandParameter = folder.Location });
                segment.Children = menu;
            }
            catch (StorageException)
            {
            }
        }
    }

    private void Watch(IStorageProvider provider, StorageLocation folder)
    {
        _watch?.Dispose();
        _watch = null;
        try
        {
            _watch = provider.Watch(folder, _ => ScheduleReload());
        }
        catch (Exception e) when (e is StorageException or IOException or ArgumentException)
        {
        }
    }

    // A copy of a hundred files raises a hundred events; one reload after they settle is enough.
    private void ScheduleReload()
    {
        _reload?.Cancel();
        var cancel = _reload = new CancellationTokenSource();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Task.Delay(300, cancel.Token);
                if (Current is not null)
                    await Load(Current, keepSelection: true);
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private ItemRow[] BuildRows(List<StorageItem> items)
    {
        var format = new RowFormat(_strings);
        var visible = ShowHidden ? [.. items] : items.Where(i => !i.IsHidden).ToArray();
        Sort.Sort(visible);
        return Array.ConvertAll(visible, item => new ItemRow(item, format));
    }

    private void Rebuild(bool keepSelection = false) =>
        Show(BuildRows(_items), keepSelection ? SelectedItems.Select(i => i.Location).ToHashSet() : null);

    private void Show(ItemRow[] rows, HashSet<StorageLocation>? keep)
    {
        Rows = rows;
        _selection.Clear();
        if (keep is { Count: > 0 })
            SelectWhere(item => keep.Contains(item.Location));
        else
            UpdateStatus();
    }

    private void SelectWhere(Func<StorageItem, bool> predicate)
    {
        _selection.Clear();
        var first = true;
        for (var i = 0; i < Rows.Count; i++)
            if (predicate(Rows[i].Item))
            {
                _selection.Click(i, toggle: !first);
                first = false;
            }
        ApplySelection();
    }

    private void ApplySelection()
    {
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].IsSelected = _selection.Contains(i);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var count = _strings.Get("Explorer.Items", Rows.Count);
        Status = _selection.Count == 0 ? count : $"{count} · {_strings.Get("Explorer.Selected", _selection.Count)}";
    }

    private int IndexOf(ItemRow row)
    {
        for (var i = 0; i < Rows.Count; i++)
            if (ReferenceEquals(Rows[i], row))
                return i;
        return -1;
    }

    private string Describe(StorageException e) => e.Kind switch
    {
        StorageErrorKind.AccessDenied => _strings.Get("Explorer.Error.AccessDenied", e.Location?.DisplayPath ?? ""),
        StorageErrorKind.NotFound => _strings.Get("Explorer.Error.NotFound", e.Location?.DisplayPath ?? ""),
        StorageErrorKind.AlreadyExists => _strings.Get("Explorer.Error.AlreadyExists", e.Location is { } l ? Path.GetFileName(l.DisplayPath) : ""),
        StorageErrorKind.InvalidName => _strings["Explorer.Error.InvalidName"],
        StorageErrorKind.Unavailable => _strings.Get("Explorer.Error.Unavailable", e.Location?.DisplayPath ?? ""),
        _ => e.Message,
    };

    private async Task Try(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (StorageException e)
        {
            Error = Describe(e);
        }
    }

    private static Page? WindowPage => Application.Current?.Windows.FirstOrDefault()?.Page;

    private async Task<string?> Prompt(string title, string message, string initial) =>
        WindowPage is { } page ? await page.DisplayPromptAsync(_strings[title], _strings[message], _strings["Explorer.OK"], _strings["Explorer.Cancel"], initialValue: initial) : null;

    private async Task<bool> Confirm(string title, string message, string accept) =>
        WindowPage is { } page && await page.DisplayAlertAsync(title, message, accept, _strings["Explorer.Cancel"]);
}
