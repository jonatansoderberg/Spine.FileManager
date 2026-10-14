using System.Collections.ObjectModel;
using Plugin.Maui.Spine.Common;
using Spine.FileManager.Explorer;
using Spine.FileManager.Favorites;
using Spine.FileManager.Operations;
using MenuAction = Plugin.Maui.Spine.Core.MenuAction;
using MenuItems = Plugin.Maui.Spine.Core.MenuItems;
using MenuSection = Plugin.Maui.Spine.Core.MenuSection;
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

/// <summary>One tab of a pane: its folder, history, order, view and what was selected when it was left.</summary>
public sealed partial class ExplorerTab : ObservableObject
{
    public NavigationHistory History { get; } = new();
    public StorageLocation? Location { get; set; }
    public ItemSort Sort { get; set; } = new();
    public bool ShowHidden { get; set; }
    public bool IsIcons { get; set; }

    /// <summary>The folder's items as last read, shown at once when the tab is chosen again.</summary>
    public List<StorageItem>? Items { get; set; }

    public HashSet<StorageLocation> Selected { get; set; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial bool IsActive { get; set; }
}

public sealed partial class FavoriteRow(Favorite favorite) : ObservableObject
{
    private static readonly ImageSource FolderIcon = "folder.png";

    public Favorite Favorite { get; } = favorite;

    public string Name => Favorite.Name;

    public ImageSource Icon => FolderIcon;

    /// <summary>False while the folder is gone, offline or unreachable; the row stays, dimmed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Dimmed))]
    public partial bool IsAvailable { get; set; } = true;

    public double Dimmed => IsAvailable ? 1 : 0.4;

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
    private readonly TabList<ExplorerTab> _tabs = new(new ExplorerTab { IsActive = true });

    private NavigationHistory _history => _tabs.Active.History;
    private readonly Selection _selection = new();
    private List<StorageItem> _items = [];
    private CancellationTokenSource? _loading;
    private IDisposable? _watch;
    private CancellationTokenSource? _reload;

    private readonly INavigationService _navigation;
    private readonly OperationQueue _operations;
    private readonly FavoritesStore _favorites;

    public ExplorerPageViewModel(ISpineStrings strings, StorageProviders providers, INavigationService navigation, OperationQueue operations, FavoritesStore favorites)
    {
        _favorites = favorites;
        _favorites.Changed += () => MainThread.BeginInvokeOnMainThread(() => _ = LoadFavorites());
        BuildMenus(strings);
        strings.Changed += (_, _) => BuildMenus(strings);
        _strings = strings;
        _providers = providers;
        _navigation = navigation;
        _operations = operations;
        _operations.Changed += operation => MainThread.BeginInvokeOnMainThread(() => OnOperationChanged(operation));
        _operations.ResolveConflict = (_, conflict) => MainThread.InvokeOnMainThreadAsync(() => AskConflict(conflict));
        _strings.Changed += (_, _) => Rebuild(keepSelection: true);
    }

    public ObservableCollection<PlaceRow> Places { get; } = [];

    public ObservableCollection<FavoriteRow> Favorites { get; } = [];

    public ObservableCollection<ExplorerTab> Tabs { get; } = [];

    /// <summary>The tab bar shows from the second tab on, as in Finder.</summary>
    public bool HasTabs => Tabs.Count > 1;

    private void SyncTabs()
    {
        Tabs.Clear();
        foreach (var tab in _tabs.Tabs)
        {
            tab.IsActive = tab == _tabs.Active;
            Tabs.Add(tab);
        }
        OnPropertyChanged(nameof(HasTabs));
    }

    [RelayCommand]
    private Task NewTab() => OpenTab(Current, activate: true);

    private ExplorerTab? _startTab;
    private StorageLocation? _startLocation;

    /// <summary>For an explorer in a new window: start in <paramref name="tab"/> (moved from another window) or at <paramref name="location"/>.</summary>
    public void Start(ExplorerTab? tab, StorageLocation? location)
    {
        _startTab = tab;
        _startLocation = location;
    }

    [RelayCommand]
    private void NewWindow() => ExplorerWindows.Open(from: Current);

    /// <summary>The active tab leaves this window for a window of its own, with its folder, history and view.</summary>
    [RelayCommand]
    private async Task MoveTabToNewWindow(ExplorerTab? tab)
    {
        var moving = tab ?? _tabs.Active;
        if (_tabs.Tabs.Count < 2)
            return;
        if (moving == _tabs.Active)
            Remember();
        await CloseTab(moving);
        moving.IsActive = true;
        ExplorerWindows.Open(moving);
    }

    [RelayCommand]
    private async Task OpenInNewTab()
    {
        foreach (var folder in SelectedItems.Where(i => i.IsFolder).ToList())
            await OpenTab(folder.Location, activate: true);
    }

    public bool CanOpenTerminal => TerminalLauncher.IsSupported;

    /// <summary>Opens the selected folder, or the folder shown when a file or nothing is selected, in a terminal.</summary>
    [RelayCommand]
    private void OpenInTerminal(Terminal? terminal)
    {
        var folder = SelectedItems.ToList() is [{ IsFolder: true } one] ? one.Location : Current;
        if (folder?.NativePath is not { } path || !TerminalLauncher.Open(path, terminal))
            Error = _strings["Explorer.Error.Terminal"];
    }

    [RelayCommand]
    private void RowOpenInTerminal(ItemRow row)
    {
        Pick(row);
        OpenInTerminal(null);
    }

    [RelayCommand]
    private Task RowOpenInNewTab(ItemRow row) => row.Item.IsFolder ? OpenTab(row.Item.Location, activate: true) : Task.CompletedTask;

    private async Task OpenTab(StorageLocation? location, bool activate)
    {
        if (location is null)
            return;
        Remember();
        var tab = new ExplorerTab { Sort = Sort, ShowHidden = ShowHidden, IsIcons = IsIcons, Title = Path.GetFileName(location.DisplayPath.TrimEnd(Path.DirectorySeparatorChar)) };
        tab.History.Navigate(location);
        _tabs.Add(tab, activate);
        SyncTabs();
        if (activate)
            await Show(tab);
    }

    [RelayCommand]
    private async Task CloseTab(ExplorerTab? tab)
    {
        var closing = tab ?? _tabs.Active;
        var wasActive = closing == _tabs.Active;
        if (!_tabs.Close(closing))
            return;
        SyncTabs();
        if (wasActive)
            await Show(_tabs.Active);
    }

    [RelayCommand]
    private async Task SelectTab(ExplorerTab tab)
    {
        if (tab == _tabs.Active)
            return;
        Remember();
        _tabs.Activate(tab);
        SyncTabs();
        await Show(tab);
    }

    [RelayCommand]
    private async Task StepTab(string direction)
    {
        if (_tabs.Tabs.Count < 2)
            return;
        Remember();
        _tabs.Step(int.Parse(direction));
        SyncTabs();
        await Show(_tabs.Active);
    }

    // What the tab being left had selected, so it comes back as it was.
    private void Remember() => _tabs.Active.Selected = SelectedItems.Select(i => i.Location).ToHashSet();

    private async Task Show(ExplorerTab tab)
    {
        Sort = tab.Sort;
        ShowHidden = tab.ShowHidden;
        IsIcons = tab.IsIcons;
        if (tab.History.Current is { } location)
            await Load(location, cached: tab.Items, keep: tab.Selected);
    }

    partial void OnSortChanged(ItemSort value) => _tabs.Active.Sort = value;

    partial void OnShowHiddenChanged(bool value) => _tabs.Active.ShowHidden = value;

    partial void OnIsIconsChanged(bool value) => _tabs.Active.IsIcons = value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoFavorites))]
    public partial double FavoritesHeight { get; private set; }

    public bool HasNoFavorites => FavoritesHeight == 0;

    /// <summary>The right-click (long-press) menu of a row in the list; the row is its parameter.</summary>
    [ObservableProperty]
    public partial MenuItems RowMenu { get; private set; } = [];

    [ObservableProperty]
    public partial MenuItems FavoriteMenu { get; private set; } = [];

    [ObservableProperty]
    public partial MenuItems TabMenu { get; private set; } = [];

    private void BuildMenus(ISpineStrings strings)
    {
        RowMenu =
        [
            new MenuSection
            {
                new MenuAction(strings["Menu.Open"], null, RowOpenCommand),
                new MenuAction(strings["Menu.OpenInNewTab"], null, RowOpenInNewTabCommand),
                new MenuAction(strings["Menu.OpenInTerminal"], null, RowOpenInTerminalCommand) { IsVisible = TerminalLauncher.IsSupported },
                new MenuAction(strings["Menu.AddFavorite"], "Star.svg", RowFavoriteCommand),
            },
            new MenuSection
            {
                new MenuAction(strings["Menu.Rename"], "Edit.svg", RowRenameCommand),
                new MenuAction(strings["Menu.Copy"], "Copy.svg", RowCopyCommand),
                new MenuAction(strings["Menu.Cut"], null, RowCutCommand),
                new MenuAction(strings["Menu.Duplicate"], null, RowDuplicateCommand),
            },
            new MenuAction(strings["Menu.Delete"], "Trashcan.svg", RowDeleteCommand) { IsDestructive = true },
        ];
        TabMenu =
        [
            new MenuAction(strings["Menu.MoveTabToNewWindow"], null, MoveTabToNewWindowCommand) { IsVisible = ExplorerWindows.IsSupported },
            new MenuAction(strings["Menu.CloseTab"], null, CloseTabCommand),
        ];
        FavoriteMenu =
        [
            new MenuAction(strings["Menu.Open"], null, OpenFavoriteCommand),
            new MenuAction(strings["Menu.RemoveFavorite"], "Unpin.svg", RemoveFavoriteCommand) { IsDestructive = true },
        ];
    }

    private async Task LoadFavorites()
    {
        Favorites.Clear();
        foreach (var favorite in _favorites.Items)
            Favorites.Add(new FavoriteRow(favorite) { IsCurrent = favorite.Location.Equals(Current) });
        FavoritesHeight = Favorites.Count * 30;
        foreach (var row in Favorites.ToList())
            row.IsAvailable = await IsReachable(row.Favorite.Location);
    }

    private async Task<bool> IsReachable(StorageLocation location)
    {
        try
        {
            // A share or a cloud folder that hangs counts as unavailable rather than holding the sidebar.
            var item = await _providers[location].GetItemAsync(location).WaitAsync(TimeSpan.FromSeconds(3));
            return item.IsFolder;
        }
        catch (Exception e) when (e is StorageException or TimeoutException)
        {
            return false;
        }
    }

    [RelayCommand]
    private async Task OpenFavorite(FavoriteRow row)
    {
        if (await IsReachable(row.Favorite.Location))
            await Navigate(row.Favorite.Location);
        else
        {
            row.IsAvailable = false;
            Error = _strings.Get("Explorer.Error.Unavailable", row.Favorite.Location.DisplayPath);
        }
    }

    [RelayCommand]
    private void RemoveFavorite(FavoriteRow row) => _favorites.Remove(row.Favorite.Location);

    [RelayCommand]
    private void SaveFavoriteOrder() => _favorites.Reorder(Favorites.Select(f => f.Favorite.Location));

    [RelayCommand]
    private void AddFavorite()
    {
        var items = SelectedItems.ToList();
        var folders = items.Count > 0 ? items.Where(i => i.IsFolder).ToList() : Current is { } here ? [new StorageItem(here, Path.GetFileName(here.DisplayPath.TrimEnd(Path.DirectorySeparatorChar)), StorageItemKind.Folder) { DisplayName = Segments.LastOrDefault()?.Name ?? here.DisplayPath }] : [];
        if (folders.Count == 0)
        {
            Error = _strings["Explorer.Error.FavoriteFolder"];
            return;
        }
        foreach (var folder in folders)
            _favorites.Add(folder.Location, folder.DisplayName);
    }

    // A right-click on a row that is not selected selects it alone first, as Finder and Explorer do.
    private void Pick(ItemRow row)
    {
        if (!row.IsSelected)
            Tap(row);
    }

    [RelayCommand]
    private Task RowOpen(ItemRow row) { Pick(row); return Activate(row); }

    [RelayCommand]
    private void RowFavorite(ItemRow row) { Pick(row); AddFavorite(); }

    [RelayCommand]
    private Task RowRename(ItemRow row) { Pick(row); return Rename(); }

    [RelayCommand]
    private void RowCopy(ItemRow row) { Pick(row); Copy(); }

    [RelayCommand]
    private void RowCut(ItemRow row) { Pick(row); Cut(); }

    [RelayCommand]
    private void RowDuplicate(ItemRow row) { Pick(row); Duplicate(); }

    [RelayCommand]
    private Task RowDelete(ItemRow row) { Pick(row); return Delete(); }

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
        if (_startTab is { } moved)
        {
            // A tab moved here from another window becomes this window's only tab, as it was.
            _tabs.Add(moved);
            _tabs.Close(_tabs.Tabs[0]);
            _startTab = null;
        }
        SyncTabs();
        UpdateInterrupted();
        await LoadFavorites();
        foreach (var provider in _providers.All)
            foreach (var root in await provider.GetRootsAsync())
                Places.Add(new(root));
        if (_tabs.Active.History.Current is { } kept)
            await Show(_tabs.Active);
        else if ((_startLocation ?? Places.FirstOrDefault()?.Root.Location) is { } start)
            await Navigate(start);
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
    private void Copy() => PutOnClipboard(cut: false);

    [RelayCommand]
    private void Cut() => PutOnClipboard(cut: true);

    private void PutOnClipboard(bool cut)
    {
        var items = SelectedItems.Select(i => i.Location).ToList();
        if (items.Count > 0)
            FileClipboard.Set(items, cut);
    }

    [RelayCommand]
    private void Paste()
    {
        if (Current is not { } folder || FileClipboard.Items is not { Count: > 0 } items)
            return;
        _pasteSelects = _operations.Enqueue(FileClipboard.IsCut ? OperationKind.Move : OperationKind.Copy, items, folder);
        if (FileClipboard.IsCut)
            FileClipboard.Clear();
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (Current is { } folder && SelectedItems.Select(i => i.Location).ToList() is { Count: > 0 } items)
            _pasteSelects = _operations.Enqueue(OperationKind.Copy, items, folder);
    }

    [RelayCommand]
    private void CancelOperation() => _activeOperation?.Cancel();

    [RelayCommand]
    private Task ShowOperations() => _navigation.ShowAsync<OperationsPage>();

    /// <summary>Operations the app found unfinished in its journal, waiting to be resumed from the Operation Center.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInterrupted))]
    public partial string? InterruptedText { get; private set; }

    public bool HasInterrupted => InterruptedText is not null && !HasOperation;

    private void UpdateInterrupted()
    {
        var count = _operations.Operations.Count(o => o.State == OperationState.Interrupted);
        InterruptedText = count == 0 ? null : _strings.Get("Operations.Interrupted", count);
    }

    public bool CanPaste => FileClipboard.HasItems;

    public bool HasSelection => _selection.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOperation))]
    public partial string? OperationText { get; private set; }

    [ObservableProperty]
    public partial double OperationProgress { get; private set; }

    public bool HasOperation => OperationText is not null;

    private FileOperation? _activeOperation;
    private FileOperation? _pasteSelects;

    private void OnOperationChanged(FileOperation operation)
    {
        UpdateInterrupted();
        OnPropertyChanged(nameof(HasInterrupted));
        if (!operation.IsFinished)
        {
            _activeOperation = operation;
            var progress = operation.Progress;
            var verb = _strings[operation.Kind == OperationKind.Move ? "Operation.Moving" : "Operation.Copying"];
            var amount = progress.BytesTotal > 0
                ? $"{ByteSize.Format(progress.BytesDone)} / {ByteSize.Format(progress.BytesTotal)}"
                : _strings.Get("Explorer.Items", progress.ItemsTotal);
            var speed = progress.BytesPerSecond > 1_000_000 ? $" · {ByteSize.Format((long)progress.BytesPerSecond)}/s" : "";
            OperationText = operation.State == OperationState.Preparing ? _strings["Operation.Preparing"] : $"{verb} {progress.Current} — {amount}{speed}";
            OperationProgress = progress.Fraction;
            return;
        }

        if (operation == _activeOperation)
        {
            _activeOperation = null;
            OperationText = null;
        }
        if (operation.Errors.Count > 0)
            Error = $"{_strings.Get("Operation.Errors", operation.Errors.Count)} {operation.Errors[0].Message}";
        if (operation == _pasteSelects && Current is { } folder && operation.Target.Equals(folder))
        {
            var results = operation.Results.ToHashSet();
            _ = Load(folder).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() => SelectWhere(i => results.Contains(i.Location))));
        }
    }

    private async Task<ConflictAnswer> AskConflict(Conflict conflict)
    {
        if (WindowPage is not { } page)
            return new(ConflictChoice.Skip);
        string replace = _strings["Conflict.Replace"], keepBoth = _strings["Conflict.KeepBoth"], skip = _strings["Conflict.Skip"];
        string replaceAll = _strings["Conflict.ReplaceAll"], skipAll = _strings["Conflict.SkipAll"], keepAll = _strings["Conflict.KeepBothAll"];
        string[] buttons = conflict.Remaining > 0 ? [replace, keepBoth, skip, replaceAll, keepAll, skipAll] : [replace, keepBoth, skip];
        var title = _strings.Get(conflict.Existing.IsFolder && conflict.Source.IsFolder ? "Conflict.FolderTitle" : "Conflict.Title", conflict.Source.Name);
        var pick = await page.DisplayActionSheetAsync(title, _strings["Conflict.Stop"], null, buttons);
        return pick switch
        {
            _ when pick == replace => new(ConflictChoice.Replace),
            _ when pick == keepBoth => new(ConflictChoice.KeepBoth),
            _ when pick == skip => new(ConflictChoice.Skip),
            _ when pick == replaceAll => new(ConflictChoice.Replace, ForAll: true),
            _ when pick == keepAll => new(ConflictChoice.KeepBoth, ForAll: true),
            _ when pick == skipAll => new(ConflictChoice.Skip, ForAll: true),
            _ => new(ConflictChoice.Stop),
        };
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

    private async Task Load(StorageLocation location, bool keepSelection = false, List<StorageItem>? cached = null, HashSet<StorageLocation>? keep = null)
    {
        _loading?.Cancel();
        var cancel = _loading = new CancellationTokenSource();
        var provider = _providers[location];
        keep ??= keepSelection ? SelectedItems.Select(i => i.Location).ToHashSet() : null;
        // A tab switch always rebuilds the bar and the watcher, even onto the same folder.
        var changedFolder = !location.Equals(Current) || cached is not null || keep is { Count: > 0 } && !keepSelection;
        var tab = _tabs.Active;
        tab.Location = location;
        if (string.IsNullOrEmpty(tab.Title) || !location.Equals(Current))
            tab.Title = Path.GetFileName(location.DisplayPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : location.DisplayPath;

        Current = location;
        if (changedFolder)
            _ = BuildBreadcrumb(provider, location);
        CanGoBack = _history.CanGoBack;
        CanGoForward = _history.CanGoForward;
        foreach (var place in Places)
            place.IsCurrent = place.Root.Location.Equals(location);
        foreach (var favorite in Favorites)
            favorite.IsCurrent = favorite.Favorite.Location.Equals(location);
        Error = null;
        IsLoading = true;
        if (changedFolder)
        {
            _selection.Clear();
            _items = cached ?? [];
            Rows = [];
            Watch(provider, location);
            if (cached is not null)
                Show(BuildRows(cached), keep);
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
            if (tab != _tabs.Active)
                return;
            _items = items;
            tab.Items = items;
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
        var tab = _tabs.Active;
        foreach (var segment in segments)
        {
            if (!location.Equals(Current))
                return;
            try
            {
                segment.Name = (await provider.GetItemAsync(segment.Location)).DisplayName;
                if (segment == segments[^1] && tab.Location?.Equals(location) == true)
                    tab.Title = segment.Name;
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

    /// <summary>The page of the window this explorer is in, for its prompts; set by the page when it loads.</summary>
    public Func<Page?> HostPage { get; set; } = () => Application.Current?.Windows.FirstOrDefault()?.Page;

    private Page? WindowPage => HostPage();

    private async Task<string?> Prompt(string title, string message, string initial) =>
        WindowPage is { } page ? await page.DisplayPromptAsync(_strings[title], _strings[message], _strings["Explorer.OK"], _strings["Explorer.Cancel"], initialValue: initial) : null;

    private async Task<bool> Confirm(string title, string message, string accept) =>
        WindowPage is { } page && await page.DisplayAlertAsync(title, message, accept, _strings["Explorer.Cancel"]);
}
