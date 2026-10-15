using System.Collections.ObjectModel;
using Plugin.Maui.Spine.Common;
using Spine.FileManager.Controls;
using Spine.FileManager.Explorer;
using Spine.FileManager.Favorites;
using Spine.FileManager.Operations;
using Spine.FileManager.Storage.FileSystem;
using Spine.FileManager.Workspaces;
using MenuAction = Plugin.Maui.Spine.Core.MenuAction;
using MenuItems = Plugin.Maui.Spine.Core.MenuItems;
using MenuSection = Plugin.Maui.Spine.Core.MenuSection;
using Spine.FileManager.Services;

namespace Spine.FileManager.Pages;

public sealed partial class PlaceRow(StorageRoot root) : ObservableObject
{
    public StorageRoot Root { get; } = root;

    public string Name => Root.Name;

    /// <summary>A glyph from Spine's set, tinted like the sidebar's other entries.</summary>
    public string Glyph => Root.Kind switch
    {
        StorageRootKind.Home => "House.svg",
        StorageRootKind.Desktop => "Tv.svg",
        StorageRootKind.Documents => "Text.svg",
        StorageRootKind.Downloads => "Download.svg",
        StorageRootKind.Pictures => "Image.svg",
        StorageRootKind.Music => "Speaker.svg",
        StorageRootKind.Videos => "Play.svg",
        StorageRootKind.Cloud => "Cloud.svg",
        StorageRootKind.NetworkVolume => "Globe.svg",
        StorageRootKind.Volume or StorageRootKind.RemovableVolume => "Stack.svg",
        _ when Root.Name == "Applications" => "Grid.svg",
        _ => "Layers.svg",
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
    [NotifyPropertyChangedFor(nameof(ShowsClose))]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsClose))]
    public partial bool IsHovered { get; set; }

    /// <summary>Showing Home rather than a folder; <see cref="Location"/> is then the folder Back returns to.</summary>
    [ObservableProperty]
    public partial bool IsHome { get; set; }

    /// <summary>As in Edge: the active tab and the one under the pointer show their ×.</summary>
    public bool ShowsClose => IsActive || IsHovered;
}

/// <summary>A linked device in the sidebar's Other Devices; files dropped on it are sent there.</summary>
public sealed partial class DeviceRow(DeviceLink.TrustedNode node, string lifetime) : ObservableObject
{
    public DeviceLink.TrustedNode Node { get; } = node;

    public string Name => Node.DisplayName;

    public string Lifetime { get; } = lifetime;

    public string Glyph => Node.SourceName.Contains("Phone", StringComparison.OrdinalIgnoreCase) || Node.SourceName.Contains("Android", StringComparison.OrdinalIgnoreCase)
        ? "Phone.svg" : "Tv.svg";

    [ObservableProperty]
    public partial bool IsDropTarget { get; set; }
}

public sealed partial class FavoriteRow(Favorite favorite) : ObservableObject
{
    public Favorite Favorite { get; } = favorite;

    public string Name => Favorite.Name;

    public string Glyph => "Star.svg";

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

    /// <summary>Whether <see cref="Children"/> lists hidden folders.</summary>
    public bool ChildrenWithHidden { get; set; }

    /// <summary>Folded into the bar's « because the breadcrumb does not fit.</summary>
    [ObservableProperty]
    public partial bool IsCollapsed { get; set; }

    /// <summary>The segment's width as last laid out, kept while it is folded away, for deciding what fits.</summary>
    public double Width { get; set; }
}

/// <summary>A disk on Home's overview: how full it is, worded for its card.</summary>
public sealed class DiskRow(Disk disk, string kind, string free, string used)
{
    public Disk Disk { get; } = disk;
    public string Name => Disk.Name;
    public string Kind { get; } = kind;
    public string Icon => Disk.Kind == DiskKind.Network ? "cloud.png" : "drive.png";
    public double Fraction => Disk.UsedFraction;

    /// <summary>Nine tenths full or more: the bar turns red, as in Finder and Explorer.</summary>
    public bool IsNearlyFull => Disk.UsedFraction >= 0.9;

    public string Free { get; } = free;
    public string Used { get; } = used;
}

/// <summary>A folder opened lately, on Home.</summary>
public sealed class RecentRow(RecentLocation recent, string when)
{
    public StorageLocation Location => recent.Location;
    public string Name => recent.Name;
    public string Path => recent.Location.DisplayPath;
    public string When { get; } = when;
}

/// <summary>How a list's rows are worded: the culture and the kind names, shared by every row of one listing.</summary>
public sealed class RowFormat(ISpineStrings strings)
{
    public System.Globalization.CultureInfo Culture { get; } = System.Globalization.CultureInfo.CurrentCulture;
    public string Folder { get; } = strings["Explorer.Kind.Folder"];
    public string Document { get; } = strings["Explorer.Kind.Document"];
    public string Application { get; } = strings["Explorer.Kind.Application"];
    public string FileKind { get; } = strings["Explorer.Kind.File"];
}

/// <summary>
/// One row of the list. The texts are made when a cell asks for them, so a folder of 100 000 items costs 100 000 small
/// objects rather than 300 000 formatted strings before the first row shows.
/// </summary>
public sealed partial class ItemRow(StorageItem item, RowFormat format) : ObservableObject
{
    public StorageItem Item { get; } = item;
    public string Name => Item.DisplayName;
    public string Kind => Item.IsBrowsable ? format.Folder
        : Item.IsPackage && Item.Extension.Equals("app", StringComparison.OrdinalIgnoreCase) ? format.Application
        : Item.Extension.Length > 0 ? string.Format(format.Culture, format.FileKind, Item.Extension.ToUpperInvariant())
        : format.Document;
    public string Modified => Item.Modified?.ToLocalTime().ToString("g", format.Culture) ?? "";
    public string Size => Item.Size is { } size ? ByteSize.Format(size, format.Culture) : "";
    public ImageSource Icon => FileIcons.For(Item);
    public double Dimmed => Item.IsHidden ? 0.5 : 1;

    /// <summary>In a search of the subfolders, the folder the item is in, from the folder searched.</summary>
    public string? Where { get; init; }

    public string WhereSuffix => Where is null ? "" : "   " + Where;

    /// <summary>A folder row a dragged item is over.</summary>
    [ObservableProperty]
    public partial bool IsDropTarget { get; set; }

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

    public ExplorerPageViewModel(ISpineStrings strings, StorageProviders providers, INavigationService navigation, OperationQueue operations, FavoritesStore favorites, DeviceService devices)
    {
        _devices = devices;
        _devices.Trust.Changed += () => MainThread.BeginInvokeOnMainThread(LoadDevices);
        _devices.Received += (from, path) => MainThread.BeginInvokeOnMainThread(() => DeviceActivity = _strings.Get("Devices.Received", Path.GetFileName(path), from));
        _favorites = favorites;
        // Created with the first explorer, so the first finished operation is already recorded.
        _ = Undo;
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
        FileIcons.Changed += () => MainThread.BeginInvokeOnMainThread(() => Rebuild(keepSelection: true));
        FileIcons.Refreshed += () => MainThread.BeginInvokeOnMainThread(() => Rebuild(keepSelection: true));
        HiddenPlaces.Changed += () => MainThread.BeginInvokeOnMainThread(ShowPlaces);
        // A list measures its first row once: a new size needs its rows again, the sidebar's lists new heights.
        ItemSizes.Changed += () =>
        {
            Rebuild(keepSelection: true);
            PlacesHeight = Places.Count * SidebarRowHeight;
            CloudHeight = CloudPlaces.Count * SidebarRowHeight;
            FavoritesHeight = Favorites.Count * SidebarRowHeight;
            DevicesHeight = Devices.Count * SidebarRowHeight;
        };
    }

    /// <summary>This Device: known folders and disks the user has not hidden.</summary>
    public ObservableCollection<PlaceRow> Places { get; } = [];

    /// <summary>The Cloud section: iCloud Drive and the providers in ~/Library/CloudStorage (OneDrive, Google Drive …).</summary>
    public ObservableCollection<PlaceRow> CloudPlaces { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloud))]
    public partial double CloudHeight { get; private set; }

    public bool HasCloud => CloudHeight > 0;

    private readonly List<PlaceRow> _allPlaces = [];

    [ObservableProperty]
    public partial MenuItems PlaceMenu { get; private set; } = [];

    private void ShowPlaces()
    {
        Places.Clear();
        CloudPlaces.Clear();
        foreach (var place in _allPlaces.Where(p => !HiddenPlaces.Contains(p.Root.Location)))
        {
            place.IsCurrent = !IsHome && place.Root.Location.Equals(Current);
            (place.Root.Kind == StorageRootKind.Cloud ? CloudPlaces : Places).Add(place);
        }
        PlacesHeight = Places.Count * SidebarRowHeight;
        CloudHeight = CloudPlaces.Count * SidebarRowHeight;
    }

    [RelayCommand]
    private void HidePlace(PlaceRow place) => HiddenPlaces.Hide(place.Root.Location, place.Name);

    private readonly DeviceService _devices;

    public ObservableCollection<DeviceRow> Devices { get; } = [];

    // A list a point short per row scrolled itself.
    private static double SidebarRowHeight => ItemSizes.SidebarRowHeight;

    [ObservableProperty]
    public partial double DevicesHeight { get; private set; }

    [ObservableProperty]
    public partial double PlacesHeight { get; private set; }

    /// <summary>The last device transfer, in the status bar: sending, sent, received or why it failed.</summary>
    [ObservableProperty]
    public partial string DeviceActivity { get; private set; } = "";

    [ObservableProperty]
    public partial MenuItems DeviceMenu { get; private set; } = [];

    private void LoadDevices()
    {
        Devices.Clear();
        foreach (var node in _devices.Trust.All.OrderBy(n => n.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            Devices.Add(new DeviceRow(node, _strings[$"Devices.Lifetime.{node.Lifetime}"]));
        DevicesHeight = Devices.Count * SidebarRowHeight;
        OnPropertyChanged(nameof(HasNoDevices));
    }

    [RelayCommand]
    private Task LinkDevice() => _navigation.ShowAsync<LinkDevicePage>();

    [RelayCommand]
    private Task ShowSettings() => _navigation.ShowAsync<SettingsPage>();

    [RelayCommand]
    private async Task RenameDevice(DeviceRow row)
    {
        if (await Prompt("Devices.Rename.Title", "Devices.Rename.Message", row.Name) is { } name)
            _devices.Trust.Rename(row.Node.NodeId, name);
    }

    [RelayCommand]
    private async Task ForgetDevice(DeviceRow row)
    {
        if (await Confirm(_strings.Get("Devices.Forget.Title", row.Name), _strings["Devices.Forget.Message"], _strings["Devices.Forget"]))
            _devices.Trust.Revoke(row.Node.NodeId);
    }

    /// <summary>Sends files and folders to a linked device; the status bar shows how far it has come.</summary>
    public async Task SendToDevice(DeviceRow row, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;
        var name = row.Name;
        try
        {
            DeviceActivity = _strings.Get("Devices.Sending", name, 0);
            await _devices.SendAsync(row.Node.NodeId, paths, (sent, total) =>
                MainThread.BeginInvokeOnMainThread(() => DeviceActivity = _strings.Get("Devices.Sending", name, total > 0 ? sent * 100 / total : 100)));
            DeviceActivity = _strings.Get("Devices.Sent", name);
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or Transfer.ChannelException)
        {
            DeviceActivity = _strings.Get("Devices.SendFailed", name, e.Message);
        }
    }

    public ObservableCollection<FavoriteRow> Favorites { get; } = [];

    public ObservableCollection<ExplorerTab> Tabs { get; } = [];

    /// <summary>A tab dropped on this pane's bar before the tab at <paramref name="index"/>, from this pane or another.</summary>
    public async Task ReceiveTab(ExplorerPageViewModel from, ExplorerTab tab, int index)
    {
        if (from == this)
        {
            _tabs.Drop(tab, index);
            SyncTabs();
            return;
        }
        if (!await from.Release(tab, emptyCloses: true))
            return;
        Remember();
        _tabs.Drop(tab, index);
        SyncTabs();
        await Show(tab);
    }

    // The tab leaves this pane for another window or a window of its own. A pane's last tab can only leave for
    // another pane (emptyCloses): the second pane then closes, and a window with no second pane closes with it. The
    // main window stays, as Spine's sheets show only there.
    private async Task<bool> Release(ExplorerTab tab, bool emptyCloses = false)
    {
        if (_tabs.Tabs.Count < 2)
        {
            if (!emptyCloses || (Partner is not null && !IsSecondary)
                || (!IsSecondary && HostPage()?.Window == Application.Current?.Windows.FirstOrDefault()))
                return false;
            // After the drop has finished with the views of this pane.
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (IsSecondary)
                    Partner!.IsSplit = false;
                else if (HostPage()?.Window is { } window)
                {
                    IPlatformApplication.Current?.Services.GetService<SessionTracker>()?.Unregister(this);
                    ExplorerWindows.Close(window);
                }
            });
            return true;
        }
        if (tab == _tabs.Active)
            Remember();
        await Close(tab, reopenable: false);
        return true;
    }

    private void SyncTabs()
    {
        IPlatformApplication.Current?.Services.GetService<SessionTracker>()?.Changed();
        Tabs.Clear();
        foreach (var tab in _tabs.Tabs)
        {
            tab.IsActive = tab == _tabs.Active;
            Tabs.Add(tab);
        }
        OnPropertyChanged(nameof(TabWidth));
    }

    /// <summary>A new tab opens on Home, as a browser's opens on its start page.</summary>
    [RelayCommand]
    private void NewTab()
    {
        Remember();
        var tab = new ExplorerTab { Sort = Sort, ShowHidden = ShowHidden, IsIcons = IsIcons, Location = Current };
        _tabs.Add(tab, activate: true);
        SyncTabs();
        EnterHome(tab);
    }

    private IReadOnlyList<ExplorerTab>? _startTabs;
    private int _startActive;
    private StorageLocation? _startLocation;

    /// <summary>For an explorer in a new window: start with <paramref name="tabs"/> (moved or restored) or at <paramref name="location"/>.</summary>
    public void Start(IReadOnlyList<ExplorerTab>? tabs, int active, StorageLocation? location)
    {
        _startTabs = tabs;
        _startActive = active;
        _startLocation = location;
    }

    /// <summary>This window's tabs as the session saves them.</summary>
    // A tab opened in the background or restored and never shown has its folder only in its history.
    public WindowState Snapshot()
    {
        var fallback = Places.FirstOrDefault()?.Root.Location;
        StorageLocation? Folder(ExplorerTab t) => t.Location ?? (t.History.Current is { } c && c != HomeLocation ? c : null) ?? (t.IsHome ? fallback : null);
        var tabs = _tabs.Tabs.Where(t => Folder(t) is not null).ToList();
        return new([.. tabs.Select(t => new TabState(Folder(t)!, t.Sort, t.ShowHidden, t.IsIcons, t.IsHome))],
            Math.Max(0, tabs.IndexOf(_tabs.Active)));
    }

    private static string TabTitle(StorageLocation location) =>
        Path.GetFileName(location.DisplayPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : location.DisplayPath;

    private static ExplorerTab Tab(TabState state)
    {
        var tab = new ExplorerTab { Sort = state.Sort, ShowHidden = state.ShowHidden, IsIcons = state.IsIcons, Title = TabTitle(state.Location), IsHome = state.IsHome };
        if (state.IsHome)
        {
            tab.Location = state.Location;
            tab.History.Navigate(HomeLocation);
        }
        else
            tab.History.Navigate(state.Location);
        return tab;
    }

    [RelayCommand]
    private void NewWindow() => ExplorerWindows.Open(from: Current);

    /// <summary>The active tab leaves this window for a window of its own, with its folder, history and view.</summary>
    [RelayCommand]
    private async Task MoveTabToNewWindow(ExplorerTab? tab)
    {
        var moving = tab ?? _tabs.Active;
        if (!await Release(moving))
            return;
        moving.IsActive = true;
        ExplorerWindows.Open([moving]);
    }

    [RelayCommand]
    private async Task OpenInNewTab()
    {
        foreach (var folder in SelectedItems.Where(i => i.IsBrowsable).ToList())
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
    private Task RowShowPackageContents(ItemRow row) => row.Item.IsPackage ? Navigate(row.Item.Location) : Task.CompletedTask;

    [RelayCommand]
    private Task RowOpenInNewTab(ItemRow row) => row.Item.IsBrowsable ? OpenTab(row.Item.Location, activate: true) : Task.CompletedTask;

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
    private Task CloseTab(ExplorerTab? tab) => Close(tab ?? _tabs.Active, reopenable: true);

    private async Task Close(ExplorerTab closing, bool reopenable)
    {
        var wasActive = closing == _tabs.Active;
        if (wasActive)
            Remember();
        if (!_tabs.Close(closing))
            return;
        if (reopenable)
            _closed.Push(closing);
        SyncTabs();
        if (wasActive)
            await Show(_tabs.Active);
    }

    // Closed tabs with their history, for Reopen Closed Tab; a tab moved to another pane or window is not closed.
    private readonly Stack<ExplorerTab> _closed = [];

    [RelayCommand]
    private async Task ReopenClosedTab()
    {
        if (!_closed.TryPop(out var tab))
            return;
        Remember();
        tab.IsHovered = false;
        _tabs.Add(tab, activate: true);
        SyncTabs();
        await Show(tab);
    }

    /// <summary>Ctrl+1 to Ctrl+8 choose that tab, Ctrl+9 the last, as in a browser.</summary>
    [RelayCommand]
    private Task GoToTab(string number)
    {
        var index = int.Parse(number);
        var tabs = _tabs.Tabs;
        return index == 9 ? SelectTab(tabs[^1]) : index <= tabs.Count ? SelectTab(tabs[index - 1]) : Task.CompletedTask;
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
        if (tab.IsHome)
            EnterHome(tab);
        else if (tab.History.Current is { } location)
            await Load(location, cached: tab.Items, keep: tab.Selected);
    }

    partial void OnSortChanged(ItemSort value) { _tabs.Active.Sort = value; SessionChanged(); }

    partial void OnShowHiddenChanged(bool value) { _tabs.Active.ShowHidden = value; SessionChanged(); }

    partial void OnIsIconsChanged(bool value) { _tabs.Active.IsIcons = value; SessionChanged(); }

    private static void SessionChanged() => IPlatformApplication.Current?.Services.GetService<SessionTracker>()?.Changed();

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

    /// <summary>The right-click menu of the list's empty space: the folder shown, not a row.</summary>
    [ObservableProperty]
    public partial MenuItems BackgroundMenu { get; private set; } = [];

    [RelayCommand]
    private void BackgroundFavorite()
    {
        _selection.Clear();
        ApplySelection();
        AddFavorite();
    }

    [RelayCommand]
    private void BackgroundTerminal()
    {
        _selection.Clear();
        ApplySelection();
        OpenInTerminal(null);
    }

    [RelayCommand]
    private void BackgroundPaste()
    {
        Focus();
        Paste();
    }

    private void BuildMenus(ISpineStrings strings)
    {
        RowMenu =
        [
            new MenuSection
            {
                new MenuAction(strings["Menu.Open"], null, RowOpenCommand),
                new MenuAction(strings["Menu.OpenInNewTab"], null, RowOpenInNewTabCommand),
                new MenuAction(strings["Menu.ShowPackageContents"], null, RowShowPackageContentsCommand),
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
            new MenuAction(strings["Menu.Delete"], "Delete.svg", RowDeleteCommand) { IsDestructive = true },
        ];
        BackgroundMenu =
        [
            new MenuSection
            {
                new MenuAction(strings["Menu.NewFolder"], null, NewFolderCommand),
                new MenuAction(strings["Menu.Paste"], null, BackgroundPasteCommand),
            },
            new MenuSection
            {
                new MenuAction(strings["Explorer.AsList"], "List.svg", ShowAsCommand) { CommandParameter = "List" },
                new MenuAction(strings["Explorer.AsIcons"], "Grid.svg", ShowAsCommand) { CommandParameter = "Icons" },
                new MenuAction(strings["Menu.ShowHidden"], null, ToggleHiddenCommand),
                new MenuAction(strings["Menu.Refresh"], "Refresh.svg", RefreshCommand),
            },
            new MenuSection
            {
                new MenuAction(strings["Menu.AddFavorite"], "Star.svg", BackgroundFavoriteCommand),
                new MenuAction(strings["Menu.OpenInTerminal"], null, BackgroundTerminalCommand) { IsVisible = TerminalLauncher.IsSupported },
            },
        ];
        TabMenu =
        [
            new MenuAction(strings["Menu.MoveTabToNewWindow"], null, MoveTabToNewWindowCommand) { IsVisible = ExplorerWindows.IsSupported },
            new MenuAction(strings["Menu.CloseTab"], null, CloseTabCommand),
        ];
        DeviceMenu =
        [
            new MenuAction(strings["Devices.Rename"], "Edit.svg", RenameDeviceCommand),
            new MenuAction(strings["Devices.Forget"], "Delete.svg", ForgetDeviceCommand) { IsDestructive = true },
        ];
        PlaceMenu =
        [
            new MenuAction(strings["Menu.Open"], null, OpenPlaceCommand),
            new MenuAction(strings["Places.Hide"], "EyeOff.svg", HidePlaceCommand),
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
        FavoritesHeight = Favorites.Count * SidebarRowHeight;
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
        {
            if (Target == this)
                await Navigate(row.Favorite.Location);
            else
                await Target.OpenLocationCommand.ExecuteAsync(row.Favorite.Location);
        }
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

    public ObservableCollection<BreadcrumbSegment> Segments { get; } = [];

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

    public bool CanGoUp => !IsHome && Current is not null && _providers[Current].GetParent(Current) is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSidebar))]
    public partial bool IsWide { get; set; } = true;

    /// <summary>Whether the list is wide enough for the Date modified and Kind columns; in Dual Pane each pane decides.</summary>
    [ObservableProperty]
    public partial bool HasColumns { get; set; } = true;

    /// <summary>The second pane of Dual Pane: no sidebar, no strips, not part of the saved session.</summary>
    public bool IsSecondary
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(IsPaneOfTwo));
        }
    }

    /// <summary>The other pane of Dual Pane, while it is open.</summary>
    public ExplorerPageViewModel? Partner { get; set; }

    public bool ShowsSidebar => IsWide && !IsSecondary;

    /// <summary>
    /// The window's title bar on the Mac, which the explorer reaches into without a title: the tab bar is as tall as it,
    /// and the sidebar starts below the traffic lights. Elsewhere 0, and the tab bar keeps its own height.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabBarHeight), nameof(TabBarPadding), nameof(TabHeight), nameof(SidebarPadding))]
    public partial double TitleBarHeight { get; set; }

    public double TabBarHeight => Math.Max(32, TitleBarHeight);

    // The tabs stand on the toolbar below them, the active one joined to it.
    public Thickness TabBarPadding => new(TitleBarHeight > 0 && !ShowsSidebar && !IsSecondary ? 80 : 8, TabBarHeight - TabHeight, 8, 0);

    public double TabHeight => TabBarHeight - 6;

    /// <summary>The tab bar's width, from the page: tabs share it, each at most 180 and at least 70 wide.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabWidth))]
    public partial double TabBarWidth { get; set; }

    // Room is left for the + after the tabs.
    public double TabWidth => Tabs.Count == 0 || TabBarWidth <= 0 ? 200
        : Math.Clamp((TabBarWidth - TabBarPadding.Left - TabBarPadding.Right - NewTabWidth) / Tabs.Count - 2, 70, 200);

    private const double NewTabWidth = 32;

    public Thickness SidebarPadding => new(0, TitleBarHeight, 0, 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarksFocus), nameof(IsPaneOfTwo))]
    public partial bool IsSplit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarksFocus))]
    public partial bool IsFocusedPane { get; private set; } = true;

    public bool MarksFocus => IsFocusedPane && IsPaneOfTwo;

    public bool IsPaneOfTwo => IsSplit || IsSecondary;

    /// <summary>This pane takes the menus and the sidebar; the other pane lets go.</summary>
    public void Focus()
    {
        IsFocusedPane = true;
        if (Partner is { } partner)
            partner.IsFocusedPane = false;
        AppMenu.Explorer = this;
    }

    /// <summary>The pane of this window the menus and the sidebar act on.</summary>
    public ExplorerPageViewModel FocusedPane => Partner is { IsFocusedPane: true } partner ? partner : this;

    // The sidebar belongs to the first pane but opens its places in the pane in focus.
    private ExplorerPageViewModel Target => FocusedPane;

    [RelayCommand]
    private void ToggleSplit()
    {
        var primary = IsSecondary ? Partner! : this;
        primary.IsSplit = !primary.IsSplit;
    }

    [RelayCommand]
    private void CopyToOtherPane() => ToOtherPane(OperationKind.Copy);

    [RelayCommand]
    private void MoveToOtherPane() => ToOtherPane(OperationKind.Move);

    /// <summary>The items a drag from <paramref name="row"/> takes: the selection when the row is in it, else the row alone.</summary>
    public IReadOnlyList<StorageLocation> DragItems(ItemRow row)
    {
        if (!row.IsSelected)
            Pick(row);
        return [.. SelectedItems.Select(i => i.Location)];
    }

    public DropAction DropActionFor(IReadOnlyList<StorageLocation> items, StorageLocation target) =>
        DropRule.Choose(items, target, l => _providers[l].GetParent(l), Volumes.Of, ModifierKeys.CopyKey, ModifierKeys.MoveKey);

    /// <summary>Copies or moves dropped items into <paramref name="target"/>; when that is the folder shown, they are selected after.</summary>
    public void DropFiles(IReadOnlyList<StorageLocation> items, StorageLocation target, DropAction action)
    {
        if (action == DropAction.None || items.Count == 0)
            return;
        var operation = _operations.Enqueue(action == DropAction.Move ? OperationKind.Move : OperationKind.Copy, items, target);
        if (target.Equals(Current))
            _pasteSelects = operation;
    }

    private void ToOtherPane(OperationKind kind)
    {
        if (Partner?.Current is { } target && SelectedItems.Select(i => i.Location).ToList() is { Count: > 0 } items)
            _operations.Enqueue(kind, items, target);
    }

    public bool ShowsOperation => HasOperation && !IsSecondary;

    public bool ShowsInterrupted => HasInterrupted && !IsSecondary;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    [ObservableProperty]
    public partial string Status { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameChevron), nameof(ModifiedChevron), nameof(SizeChevron), nameof(KindChevron))]
    public partial ItemSort Sort { get; private set; } = new();

    [ObservableProperty]
    public partial bool ShowHidden { get; private set; }

    /// <summary>The text the list is filtered by, cleared when the folder changes.</summary>
    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    partial void OnFilterChanged(string value)
    {
        OnPropertyChanged(nameof(ShowsSearchScope));
        if (SearchSubfolders && !string.IsNullOrWhiteSpace(value))
            Search();
        else
        {
            StopSearch();
            Rebuild(keepSelection: true);
        }
    }

    public bool ShowsSearchScope => !string.IsNullOrWhiteSpace(Filter);

    /// <summary>Whether the filter searches the subfolders too: 0 the folder shown, 1 it and everything below it.</summary>
    [ObservableProperty]
    public partial int SearchScope { get; set; }

    public bool SearchSubfolders => SearchScope == 1;

    partial void OnSearchScopeChanged(int value) => OnFilterChanged(Filter);

    [ObservableProperty]
    public partial bool IsSearching { get; private set; }

    partial void OnIsSearchingChanged(bool value) => UpdateStatus();

    private CancellationTokenSource? _search;
    private List<StorageItem>? _found;

    // The subfolders are searched in the background a moment after the typing stops; what is found shows in batches,
    // and a new text, another scope or another folder stops the search.
    private void Search()
    {
        _search?.Cancel();
        if (Current is not { } root)
            return;
        var cancel = _search = new CancellationTokenSource();
        var query = Filter;
        var includeHidden = ShowHidden;
        var provider = _providers[root];
        var found = _found = [];
        IsSearching = true;
        Show([], null);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cancel.Token);
                var shown = DateTime.UtcNow;
                await foreach (var item in FileSearch.SearchAsync(provider, root, query, includeHidden, cancellationToken: cancel.Token))
                {
                    lock (found)
                        found.Add(item);
                    if (DateTime.UtcNow - shown > TimeSpan.FromMilliseconds(300))
                    {
                        shown = DateTime.UtcNow;
                        MainThread.BeginInvokeOnMainThread(() => ShowFound(cancel));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (cancel != _search)
                    return;
                IsSearching = false;
                ShowFound(cancel);
            });
        });
    }

    private void ShowFound(CancellationTokenSource search)
    {
        if (search == _search && !search.IsCancellationRequested)
            Rebuild(keepSelection: true);
    }

    private void StopSearch()
    {
        _search?.Cancel();
        _search = null;
        _found = null;
        IsSearching = false;
    }

    [RelayCommand]
    private void StopSearching()
    {
        _search?.Cancel();
        IsSearching = false;
    }

    // ---- Preview ----

    [ObservableProperty]
    public partial bool ShowsPreview { get; private set; }

    [ObservableProperty] public partial ImageSource? PreviewImage { get; private set; }
    [ObservableProperty] public partial string? PreviewTitle { get; private set; }
    [ObservableProperty] public partial string? PreviewDetails { get; private set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPreviewText))] public partial string? PreviewText { get; private set; }

    public bool HasPreviewText => PreviewText is not null;

    private CancellationTokenSource? _preview;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "markdown", "json", "xml", "csv", "log", "yml", "yaml", "toml", "ini", "cfg", "conf", "sh", "zsh", "ps1", "bat",
        "cs", "csproj", "slnx", "sln", "props", "targets", "xaml", "js", "ts", "tsx", "jsx", "css", "html", "htm", "py", "rb", "go", "rs",
        "java", "kt", "swift", "c", "h", "cpp", "hpp", "sql", "plist", "gitignore", "editorconfig",
    };

    [RelayCommand]
    private void TogglePreview()
    {
        ShowsPreview = !ShowsPreview;
        UpdatePreview();
    }

    [RelayCommand]
    private void QuickLookSelection()
    {
        var paths = (SelectedItems.Any() ? SelectedItems : Rows.Select(r => r.Item)).Where(i => i.Location.NativePath is not null)
            .Select(i => i.Location.NativePath!).ToList();
        if (QuickLookPreview.IsSupported)
            QuickLookPreview.Show(paths, 0, WindowPage);
        else if (!ShowsPreview)
            TogglePreview();
    }

    // The pane follows the selection: one item shows its picture, text and facts; several, or none, show nothing.
    private void UpdatePreview()
    {
        _preview?.Cancel();
        if (!ShowsPreview || SelectedItems.ToList() is not [var item])
        {
            PreviewImage = null;
            PreviewTitle = null;
            PreviewDetails = null;
            PreviewText = null;
            return;
        }
        var cancel = _preview = new CancellationTokenSource();
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        PreviewTitle = item.DisplayName;
        var facts = new List<string> { new ItemRow(item, new RowFormat(_strings)).Kind };
        if (item.Size is { } size)
            facts.Add(ByteSize.Format(size, culture));
        if (item.Modified is { } modified)
            facts.Add(_strings.Get("Preview.Modified", modified.ToLocalTime().ToString("g", culture)));
        if (item.Created is { } created)
            facts.Add(_strings.Get("Preview.Created", created.ToLocalTime().ToString("g", culture)));
        PreviewDetails = string.Join("\n", facts);
        PreviewImage = item.IsPackage ? FileIcons.For(item) : item.IsFolder ? "folder.png" : "file.png";
        PreviewText = null;
        // The picture and the text come separately: a slow thumbnail must not hold the text back.
        _ = Task.Run(async () =>
        {
            await Task.Delay(120, cancel.Token);
            if (item.Location.NativePath is { } path && await Thumbnails.GetAsync(path, 512) is { } picture && !cancel.IsCancellationRequested)
                MainThread.BeginInvokeOnMainThread(() => { if (!cancel.IsCancellationRequested) PreviewImage = picture; });
        }, cancel.Token);
        if (!item.IsFolder && (TextExtensions.Contains(item.Extension) || item.Extension.Length == 0))
            _ = Task.Run(async () =>
            {
                if (await ReadText(item, cancel.Token) is { } text)
                    MainThread.BeginInvokeOnMainThread(() => { if (!cancel.IsCancellationRequested) PreviewText = text; });
            }, cancel.Token);
    }

    // The first 32 KB as text, unless it holds a NUL byte (then it is not text).
    private async Task<string?> ReadText(StorageItem item, CancellationToken cancel)
    {
        try
        {
            await using var stream = await _providers[item.Location].OpenReadAsync(item.Location, cancel);
            var buffer = new byte[32 * 1024];
            var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancel);
            if (Array.IndexOf(buffer, (byte)0, 0, read) >= 0)
                return null;
            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            return read == buffer.Length ? text + "\n…" : text;
        }
        catch (Exception e) when (e is StorageException or OperationCanceledException)
        {
            return null;
        }
    }

    // ---- Keys from the list (KeySink) ----

    /// <summary>Asks the page to scroll the row at this index into view.</summary>
    public event Action<int>? RevealRequested;

    private string _typed = "";
    private DateTime _typedAt;

    public void OnListKey(ListKey key, bool shift, string? text)
    {
        switch (key)
        {
            case ListKey.Up or ListKey.Down:
                _selection.Step(key == ListKey.Down ? 1 : -1, Rows.Count, extend: shift);
                ApplySelection();
                Reveal();
                break;
            case ListKey.Return:
                // Finder renames on Return; opening is ⌘↓ or ⌘O.
                _ = Rename();
                break;
            case ListKey.Space:
                QuickLookSelection();
                break;
            case ListKey.Text when text is not null:
                // Typing a name's beginning jumps to it; a pause of a second starts a new name.
                _typed = DateTime.Now - _typedAt < TimeSpan.FromSeconds(1) ? _typed + text : text;
                _typedAt = DateTime.Now;
                var culture = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
                for (var i = 0; i < Rows.Count; i++)
                    if (culture.IsPrefix(Rows[i].Name, _typed, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace))
                    {
                        _selection.Click(i);
                        ApplySelection();
                        Reveal();
                        break;
                    }
                break;
        }
    }

    private void Reveal()
    {
        if (_selection.Anchor is { } index)
            RevealRequested?.Invoke(index);
    }

    /// <summary>Raised by ⌘F / Ctrl+F; the page puts the cursor in the filter field.</summary>
    public event Action? FilterRequested;

    [RelayCommand]
    private void Find()
    {
        IsSearchOpen = true;
        FilterRequested?.Invoke();
    }

    [ObservableProperty]
    public partial bool IsSearchOpen { get; set; }

    /// <summary>The segments folded into « , nearest first as in Windows; null when everything fits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCollapsed))]
    public partial Plugin.Maui.Spine.Core.MenuItems? CollapsedMenu { get; private set; }

    public bool HasCollapsed => CollapsedMenu is not null;

    /// <summary>Folds the first <paramref name="count"/> segments, those nearest the root, into « .</summary>
    public void Collapse(int count)
    {
        for (var i = 0; i < Segments.Count; i++)
            Segments[i].IsCollapsed = i < count;
        if (count == 0)
        {
            CollapsedMenu = null;
            return;
        }
        var menu = new Plugin.Maui.Spine.Core.MenuItems();
        foreach (var segment in Segments.Take(count).Reverse())
            menu.Add(new Plugin.Maui.Spine.Core.MenuAction(segment.Name, "folder.svg", OpenLocationCommand) { CommandParameter = segment.Location });
        CollapsedMenu = menu;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsList), nameof(ViewIndex), nameof(ShowsList), nameof(ShowsIconGrid))]
    public partial bool IsIcons { get; private set; }

    public bool IsList => !IsIcons;

    /// <summary>The view as the toolbar's segmented control picks it: 0 list, 1 icons.</summary>
    public int ViewIndex
    {
        get => IsIcons ? 1 : 0;
        set => IsIcons = value == 1;
    }

    /// <summary>Columns of the icon grid, from the list's width.</summary>
    [ObservableProperty]
    public partial int GridSpan { get; set; } = 6;

    // The column the list is sorted by shows a chevron: up ascending, down descending.
    public string? NameChevron => Chevron(SortColumn.Name);
    public string? ModifiedChevron => Chevron(SortColumn.Modified);
    public string? SizeChevron => Chevron(SortColumn.Size);
    public string? KindChevron => Chevron(SortColumn.Kind);

    private string? Chevron(SortColumn column) =>
        Sort.Column != column ? null : Sort.Descending ? "ChevronDown.svg" : "ChevronUp.svg";

    /// <summary>The widths of the parts a user resizes, kept between runs; Name takes what the other columns leave.</summary>
    [ObservableProperty]
    public partial double SidebarWidth { get; set; } = Preferences.Get(nameof(SidebarWidth), 210.0);

    [ObservableProperty]
    public partial double PreviewWidth { get; set; } = Preferences.Get(nameof(PreviewWidth), 300.0);

    [ObservableProperty]
    public partial double ModifiedWidth { get; set; } = Preferences.Get(nameof(ModifiedWidth), 160.0);

    [ObservableProperty]
    public partial double SizeWidth { get; set; } = Preferences.Get(nameof(SizeWidth), 90.0);

    [ObservableProperty]
    public partial double KindWidth { get; set; } = Preferences.Get(nameof(KindWidth), 130.0);

    /// <summary>Resizes one of the parts by the width's name, within its limits; <paramref name="keep"/> saves it for the next run.</summary>
    public void Resize(string part, double width, bool keep)
    {
        var (min, max) = part switch
        {
            nameof(SidebarWidth) => (150.0, 400.0),
            nameof(PreviewWidth) => (200.0, 600.0),
            _ => (50.0, 400.0),
        };
        width = Math.Clamp(width, min, max);
        switch (part)
        {
            case nameof(SidebarWidth): SidebarWidth = width; break;
            case nameof(PreviewWidth): PreviewWidth = width; break;
            case nameof(ModifiedWidth): ModifiedWidth = width; break;
            case nameof(SizeWidth): SizeWidth = width; break;
            case nameof(KindWidth): KindWidth = width; break;
            default: return;
        }
        if (keep)
            Preferences.Set(part, width);
    }

    public double Width(string part) => part switch
    {
        nameof(SidebarWidth) => SidebarWidth,
        nameof(PreviewWidth) => PreviewWidth,
        nameof(ModifiedWidth) => ModifiedWidth,
        nameof(SizeWidth) => SizeWidth,
        nameof(KindWidth) => KindWidth,
        _ => 0,
    };

    public IEnumerable<StorageItem> SelectedItems => _selection.Indices.Where(i => i < Rows.Count).Select(i => Rows[i].Item);


    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (_allPlaces.Count > 0)
            return;
        var session = IPlatformApplication.Current!.Services.GetRequiredService<SessionTracker>();
        if (_startTabs is null && !IsSecondary && await session.RestoreAsync() is { Count: > 0 } windows)
        {
            // The first explorer brings back the last session. The other windows' tabs come back as tabs here: a window
            // opened while the app was still starting laid its content out shifted under the title bar (Mac Catalyst).
            _startTabs = [.. windows.SelectMany(w => w.Tabs).Select(Tab)];
            _startActive = windows[0].Active;
        }
        if (_startTabs is { Count: > 0 } startTabs)
        {
            // Moved or restored tabs replace the window's first, empty tab.
            var initial = _tabs.Tabs[0];
            // In their order: Add would put each one right after the first.
            foreach (var tab in startTabs)
                _tabs.Drop(tab, _tabs.Tabs.Count);
            _tabs.Close(initial);
            _tabs.Activate(startTabs[Math.Clamp(_startActive, 0, startTabs.Count - 1)]);
            _startTabs = null;
        }
        // Restored Home tabs were named before the strings were at hand.
        foreach (var tab in _tabs.Tabs.Where(t => t.IsHome))
            tab.Title = _strings["Home.Title"];
        SyncTabs();
        if (!IsSecondary)
            session.Register(this);
        UpdateInterrupted();
        await LoadFavorites();
        foreach (var provider in _providers.All)
            foreach (var root in await provider.GetRootsAsync())
                _allPlaces.Add(new(root));
        // This Device ends with the disks, as in the concept image, unless the provider already lists one.
        foreach (var disk in await Task.Run(ListDisks))
            if (!_allPlaces.Any(p => p.Root.Location.Equals(disk.Location)))
                _allPlaces.Add(new(new StorageRoot(disk.Location, disk.Name, disk.Kind switch
                {
                    DiskKind.Network => StorageRootKind.NetworkVolume,
                    DiskKind.External => StorageRootKind.RemovableVolume,
                    _ => StorageRootKind.Volume,
                })));
        ShowPlaces();
        LoadDevices();
        if (!IsSecondary)
            _devices.StartIfLinked();
        if (_tabs.Active.IsHome || _tabs.Active.History.Current is not null)
            await Show(_tabs.Active);
        else if (_startLocation is { } start)
            await Navigate(start);
        else
            EnterHome(_tabs.Active);
        // Presented from the root page's first appearance the sheet never showed; after the first folder it does.
        if (FirstRunPageViewModel.IsNeeded && !IsSecondary)
            MainThread.BeginInvokeOnMainThread(async () => await _navigation.ShowAsync<FirstRunPage>());
        else if (!IsSecondary)
            await AskTrashAccessOnce();
    }

    [RelayCommand]
    private Task OpenPlace(PlaceRow place) => Target == this ? Navigate(place.Root.Location) : Target.OpenLocationCommand.ExecuteAsync(place.Root.Location);

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
        // A click in a list makes its pane and window the one the menus and ⌘C/⌘V act on.
        if (AppMenu.Explorer != this)
            Focus();
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
        // Ctrl-double-click (⌘ on the Mac) opens a folder in a new tab, as in Windows' Explorer. Only a click: the menu's
        // Open has no row, and its shortcut holds the key.
        // An app or other package opens like a file; Show Package Contents browses into it.
        if (item.IsBrowsable && row is not null && ModifierKeys.Command)
            await OpenTab(item.Location, activate: true);
        else if (item.IsBrowsable)
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
            Undo.Record(new TrashItems([created.Location]));
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
            Undo.Record(new RenameBack(renamed.Location, item.Name));
            await Load(folder);
            SelectWhere(i => i.Location.Equals(renamed.Location));
        });
    }

    private static UndoService Undo => IPlatformApplication.Current!.Services.GetRequiredService<UndoService>();

    public static bool CanUndo => Undo.CanUndo;

    /// <summary>Takes back the last copy, move, rename or new folder, in whichever window it happened.</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task UndoLast() => Try(Undo.UndoAsync);

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
    [NotifyPropertyChangedFor(nameof(HasInterrupted), nameof(ShowsInterrupted))]
    public partial string? InterruptedText { get; private set; }

    public bool HasInterrupted => InterruptedText is not null && !HasOperation;

    private void UpdateInterrupted()
    {
        var count = _operations.Operations.Count(o => o.State == OperationState.Interrupted);
        InterruptedText = count == 0 ? null : _strings.Get("Operations.Interrupted", count);
    }

    public bool CanPaste => FileClipboard.HasItems;

    public bool HasSelection => _selection.Count > 0;

    public IReadOnlyCollection<int> SelectedIndices => [.. _selection.Indices];

    /// <summary>Selects the items a selection rectangle covers, in any layout, beside <paramref name="kept"/>.</summary>
    public void SelectItems(IEnumerable<int> items, IReadOnlyCollection<int> kept)
    {
        if (AppMenu.Explorer != this)
            Focus();
        _selection.Box(items.Where(i => i < Rows.Count).OrderBy(i => i), kept);
        ApplySelection();
    }

    /// <summary>Selects the rows a selection rectangle covers, <paramref name="first"/> to <paramref name="last"/>, beside <paramref name="kept"/>.</summary>
    public void SelectRows(int first, int last, IReadOnlyCollection<int> kept)
    {
        if (AppMenu.Explorer != this)
            Focus();
        last = Math.Min(last, Rows.Count - 1);
        if (first > last)
            first = last = -1;
        _selection.Box(first, last, kept);
        ApplySelection();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOperation), nameof(ShowsOperation), nameof(ShowsInterrupted))]
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

    public const string ConfirmTrashKey = "delete.confirmTrash";

    private async Task Remove(bool permanently)
    {
        var items = SelectedItems.ToList();
        if (Current is not { } folder || items.Count == 0)
            return;
        var provider = _providers[folder];
        var trash = !permanently && (await provider.GetCapabilitiesAsync(folder)).HasFlag(StorageCapabilities.Trash);
        var what = items.Count == 1 ? $"“{items[0].DisplayName}”" : _strings.Get("Explorer.Items", items.Count);
        if (!trash)
        {
            var reason = permanently ? "" : _strings["Explorer.Delete.NoTrash"] + " ";
            if (!await Confirm(_strings["Explorer.Delete.Title"], reason + _strings.Get("Explorer.Delete.Message", what), _strings["Explorer.Delete.Confirm"]))
                return;
        }
        // As in Explorer, moving to the Trash asks too unless turned off in Settings; it can be undone, so it says so.
        else if (Preferences.Get(ConfirmTrashKey, true)
            && !await Confirm(_strings["Explorer.Trash.Title"], _strings.Get("Explorer.Trash.Message", what), _strings["Explorer.Trash.Confirm"]))
            return;
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

    /// <summary>Home's place in a tab's history, so Back and Forward pass through it as in a browser.</summary>
    private static readonly StorageLocation HomeLocation = new(new StorageProviderId("home"), "", "Home");

    private static RecentLocations? Recent => IPlatformApplication.Current?.Services.GetService<RecentLocations>();

    /// <summary>The active tab shows Home: the disks, linked devices and recent folders instead of a folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsFiles), nameof(ShowsList), nameof(ShowsIconGrid), nameof(CanGoUp))]
    public partial bool IsHome { get; private set; }

    public bool ShowsFiles => !IsHome;

    public bool ShowsList => IsList && !IsHome;

    public bool ShowsIconGrid => IsIcons && !IsHome;

    public ObservableCollection<DiskRow> Disks { get; } = [];

    public ObservableCollection<RecentRow> RecentRows { get; } = [];

    public bool HasNoRecent => RecentRows.Count == 0;

    public bool HasNoDevices => Devices.Count == 0;

    public string HomeDevice => DeviceService.DeviceName;

    [RelayCommand]
    private void ShowHome()
    {
        if (Target != this)
        {
            Target.ShowHomeCommand.Execute(null);
            return;
        }
        if (IsHome)
            return;
        Remember();
        EnterHome(_tabs.Active);
    }

    private void EnterHome(ExplorerTab tab)
    {
        _loading?.Cancel();
        tab.History.Navigate(HomeLocation);
        tab.IsHome = true;
        tab.Location ??= Current;
        tab.Title = _strings["Home.Title"];
        IsHome = true;
        StopSearch();
        Filter = "";
        CanGoBack = tab.History.CanGoBack;
        CanGoForward = tab.History.CanGoForward;
        foreach (var place in _allPlaces)
            place.IsCurrent = false;
        foreach (var favorite in Favorites)
            favorite.IsCurrent = false;
        SessionChanged();
        _ = RefreshHome();
    }

    private async Task RefreshHome()
    {
        var disks = await Task.Run(ListDisks);
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        Disks.Clear();
        foreach (var disk in disks)
            Disks.Add(new DiskRow(disk, _strings[$"Home.Disk.{disk.Kind}"],
                _strings.Get("Home.Free", ByteSize.Format(disk.Free, culture), ByteSize.Format(disk.Total, culture)),
                _strings.Get("Home.Used", $"{Math.Round(disk.UsedFraction * 100)}")));
        RecentRows.Clear();
        foreach (var recent in Recent?.Items ?? [])
            RecentRows.Add(new RecentRow(recent, When(recent.At)));
        OnPropertyChanged(nameof(HasNoRecent));
        OnPropertyChanged(nameof(HasNoDevices));
        await RefreshTrash();
    }

    private string When(DateTimeOffset at)
    {
        var ago = DateTimeOffset.Now - at;
        if (ago.TotalMinutes < 1)
            return _strings["Home.JustNow"];
        if (ago.TotalHours < 1)
            return _strings.Get("Home.MinutesAgo", (int)ago.TotalMinutes);
        if (at.Date == DateTime.Today)
            return _strings.Get("Home.HoursAgo", (int)ago.TotalHours);
        if (at.Date == DateTime.Today.AddDays(-1))
            return _strings["Home.Yesterday"];
        return at.ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture);
    }

    // As Finder: the start disk named "Macintosh HD" rather than "/", free space with what macOS can purge counted, and
    // the volumes it hides (Recovery) left out.
    private static IReadOnlyList<Disk> ListDisks()
    {
#if MACCATALYST
        var visible = Foundation.NSFileManager.DefaultManager.GetMountedVolumes(Array.Empty<Foundation.NSString>(), Foundation.NSVolumeEnumerationOptions.SkipHiddenVolumes)?
            .Select(url => url.Path?.TrimEnd('/') ?? "").ToHashSet();
        return Explorer.Disks.List(
            path => Foundation.NSFileManager.DefaultManager.DisplayName(path) is { Length: > 0 } name && name != "/" ? name : null,
            path => Foundation.NSUrl.FromFilename(path).TryGetResource(Foundation.NSUrl.VolumeAvailableCapacityForImportantUsageKey, out var value)
                && value is Foundation.NSNumber number && number.Int64Value > 0 ? number.Int64Value : null,
            path => visible?.Contains(path.TrimEnd('/')) != false);
#else
        return Explorer.Disks.List();
#endif
    }

    [RelayCommand]
    private Task OpenDisk(DiskRow disk) => Navigate(disk.Disk.Location);

    private static IFileSystemPlatform Platform => IPlatformApplication.Current!.Services.GetRequiredService<IFileSystemPlatform>();

    public string TrashName => _strings[OperatingSystem.IsWindows() ? "Trash.Title.Windows" : "Trash.Title"];

    /// <summary>How many items and how much the trash holds, that it is empty, or that the app may not look.</summary>
    [ObservableProperty]
    public partial string TrashSummary { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEmptyTrash))]
    public partial bool TrashNeedsAccess { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEmptyTrash))]
    public partial TrashContents? Trash { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEmptyTrash))]
    public partial bool IsEmptyingTrash { get; private set; }

    [ObservableProperty]
    public partial double TrashProgress { get; private set; }

    public bool CanEmptyTrash => Trash is { Items: > 0 } && !IsEmptyingTrash;

    private async Task RefreshTrash()
    {
        Trash = await Platform.MeasureTrashAsync(CancellationToken.None);
        TrashNeedsAccess = Trash is null && Platform.TrashFolder is not null;
        TrashSummary = Trash switch
        {
            null => TrashNeedsAccess ? _strings["Trash.NeedsAccess"] : "",
            { Items: 0 } => _strings["Trash.IsEmpty"],
            var t => _strings.Get(Plural("Trash.Summary", t.Items), t.Items, ByteSize.Format(t.Bytes, System.Globalization.CultureInfo.CurrentCulture)),
        };
    }

    /// <summary>The Mac's Trash in the explorer, once the app may read it; elsewhere the system's own Recycle Bin.</summary>
    // Spine picks a plural form only when the count is the one argument; with more, the key is chosen here.
    private static string Plural(string key, int count) => count == 1 ? $"{key}.one" : $"{key}.other";

    [RelayCommand]
    private async Task OpenTrash()
    {
        if (Platform.TrashFolder is not { } folder)
        {
            Platform.ShowTrash();
            return;
        }
        if (await Platform.MeasureTrashAsync(CancellationToken.None) is null)
        {
            await AskTrashAccess();
            return;
        }
        await Target.OpenLocationCommand.ExecuteAsync(Storage.FileSystem.FileSystemStorageProvider.Location(folder));
    }

    [RelayCommand]
    private async Task EmptyTrash()
    {
        await RefreshTrash();
        if (Trash is not { Items: > 0 } trash)
            return;
        var size = ByteSize.Format(trash.Bytes, System.Globalization.CultureInfo.CurrentCulture);
        if (!await Confirm(_strings["Trash.Confirm.Title"], _strings.Get(Plural("Trash.Confirm.Message", trash.Items), trash.Items, size), _strings["Trash.Confirm.Accept"]))
            return;
        IsEmptyingTrash = true;
        TrashProgress = 0;
        TrashSummary = _strings["Trash.Emptying"];
        try
        {
            var failed = await Platform.EmptyTrashAsync(new Progress<double>(p => TrashProgress = p), CancellationToken.None);
            if (failed.Count > 0)
                Error = _strings.Get("Trash.Failed", string.Join(", ", failed.Take(5)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
        }
        finally
        {
            IsEmptyingTrash = false;
            await RefreshTrash();
        }
    }

    [RelayCommand]
    private Task OpenFullDiskAccess() =>
        Launcher.Default.OpenAsync("x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles");

    private async Task AskTrashAccess()
    {
        if (await Confirm(_strings["Trash.Access.Title"], _strings["Trash.Access.Message"], _strings["Trash.OpenSettings"]))
            await OpenFullDiskAccess();
    }

    // Asked once, at start, when the Mac's Trash cannot be read (the first-run sheet offers it to new users).
    private async Task AskTrashAccessOnce()
    {
        const string asked = "trash.accessAsked";
        if (Platform.TrashFolder is null || Preferences.Get(asked, false) || await Platform.MeasureTrashAsync(CancellationToken.None) is not null)
            return;
        Preferences.Set(asked, true);
        await AskTrashAccess();
    }

    private async Task Navigate(StorageLocation location)
    {
        _history.Navigate(location);
        await Load(location);
    }

    private async Task Load(StorageLocation location, bool keepSelection = false, List<StorageItem>? cached = null, HashSet<StorageLocation>? keep = null)
    {
        if (location == HomeLocation)
        {
            EnterHome(_tabs.Active);
            return;
        }
        _loading?.Cancel();
        var cancel = _loading = new CancellationTokenSource();
        var provider = _providers[location];
        keep ??= keepSelection ? SelectedItems.Select(i => i.Location).ToHashSet() : null;
        // A tab switch always rebuilds the bar and the watcher, even onto the same folder.
        var changedFolder = !location.Equals(Current) || cached is not null || keep is { Count: > 0 } && !keepSelection;
        var tab = _tabs.Active;
        var leftHome = tab.IsHome;
        tab.IsHome = false;
        IsHome = false;
        tab.Location = location;
        SessionChanged();
        if (string.IsNullOrEmpty(tab.Title) || !location.Equals(Current) || leftHome)
            tab.Title = TabTitle(location);
        if (changedFolder || leftHome)
            Recent?.Add(location, _allPlaces.FirstOrDefault(p => p.Root.Location.Equals(location))?.Name ?? TabTitle(location), DateTimeOffset.Now);

        Current = location;
        if (changedFolder)
            _ = BuildBreadcrumb(provider, location);
        CanGoBack = _history.CanGoBack;
        CanGoForward = _history.CanGoForward;
        foreach (var place in _allPlaces)
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
            // A filter belongs to the folder it was typed in, as in Finder; so does a search.
            StopSearch();
            Filter = "";
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
            if (_found is null)
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

    // Only the segments that differ change: the bar keeps those the new location shares with it, from the root down,
    // names and drop-downs already read. New segments show at once with their file names; their display names and
    // subfolders follow, as do the drop-downs not read yet or read with the other hidden-files setting.
    private async Task BuildBreadcrumb(IStorageProvider provider, StorageLocation location)
    {
        var locations = Breadcrumb.Segments(provider, location);
        var kept = Breadcrumb.Shared([.. Segments.Select(s => s.Location)], locations);
        while (Segments.Count > kept)
            Segments.RemoveAt(Segments.Count - 1);
        foreach (var added in locations.Skip(kept))
            Segments.Add(new(added, Path.GetFileName(added.DisplayPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : added.DisplayPath));
        var tab = _tabs.Active;
        var last = Segments[^1];
        if (last.Children is not null)
            tab.Title = last.Name;
        var showHidden = ShowHidden;
        foreach (var segment in Segments.Where(s => s.Children is null || s.ChildrenWithHidden != showHidden).ToList())
        {
            if (!location.Equals(Current))
                return;
            try
            {
                segment.Name = (await provider.GetItemAsync(segment.Location)).DisplayName;
                if (segment == last && tab.Location?.Equals(location) == true)
                    tab.Title = segment.Name;
                var menu = new Plugin.Maui.Spine.Core.MenuItems();
                foreach (var folder in await Breadcrumb.SubfoldersAsync(provider, segment.Location, showHidden))
                    menu.Add(new Plugin.Maui.Spine.Core.MenuAction(folder.DisplayName, "folder.svg", OpenLocationCommand) { CommandParameter = folder.Location });
                segment.ChildrenWithHidden = showHidden;
                segment.Children = menu;
            }
            catch (StorageException)
            {
            }
        }
    }

    // Started off the UI thread: on the Mac creating the watcher opens the folder, and for a protected folder (Desktop,
    // Documents) that open waits for the TCC prompt — on the UI thread it froze every window until it was answered.
    private void Watch(IStorageProvider provider, StorageLocation folder)
    {
        _watch?.Dispose();
        _watch = null;
        _ = Task.Run(() =>
        {
            try
            {
                var watch = provider.Watch(folder, _ => ScheduleReload());
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (folder.Equals(Current) && _watch is null)
                        _watch = watch;
                    else
                        watch.Dispose();
                });
            }
            catch (Exception e) when (e is StorageException or IOException or ArgumentException)
            {
            }
        });
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
        var filter = Filter;
        var visible = items.Where(i => (ShowHidden || !i.IsHidden) && NameFilter.Matches(i.DisplayName, filter)).ToArray();
        Sort.Sort(visible);
        return Array.ConvertAll(visible, item => new ItemRow(item, format));
    }

    private void Rebuild(bool keepSelection = false) =>
        Show(_found is { } found ? BuildFoundRows(found) : BuildRows(_items), keepSelection ? SelectedItems.Select(i => i.Location).ToHashSet() : null);

    // Search results, sorted as the list is, each with the folder it is in.
    private ItemRow[] BuildFoundRows(List<StorageItem> found)
    {
        StorageItem[] items;
        lock (found)
            items = [.. found];
        Sort.Sort(items);
        var format = new RowFormat(_strings);
        var root = Current?.DisplayPath ?? "";
        return Array.ConvertAll(items, item => new ItemRow(item, format)
        {
            Where = Path.GetRelativePath(root, Path.GetDirectoryName(item.Location.DisplayPath) ?? root) is var where && where != "." ? where : null,
        });
    }

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
        OnPropertyChanged(nameof(HasSelection));
        UpdateStatus();
        UpdatePreview();
    }

    private void UpdateStatus()
    {
        var count = _found is not null
            ? _strings.Get(IsSearching ? "Search.Searching" : "Search.Found", Rows.Count)
            : string.IsNullOrWhiteSpace(Filter)
            ? _strings.Get("Explorer.Items", Rows.Count)
            : _strings.Get("Explorer.Matches", Rows.Count) + " " + _strings.Get("Explorer.OfItems", _items.Count(i => ShowHidden || !i.IsHidden));
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
