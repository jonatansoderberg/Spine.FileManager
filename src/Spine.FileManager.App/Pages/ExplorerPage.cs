using Spine.FileManager.Operations;
using Spine.FileManager.Storage.FileSystem;

namespace Spine.FileManager.Pages;

// On the Mac the explorer reaches into the title bar: its tabs sit in the title bar's row and the window shows no title.
#if MACCATALYST
[NavigableRegion(IsHeaderBarVisible = false, SafeAreaEdges = SafeAreaEdges.None)]
#else
[NavigableRegion(IsHeaderBarVisible = false)]
#endif
public partial class ExplorerPage
{
    private double _splitStart;

    public ExplorerPage()
    {
        InitializeComponent();
#if MACCATALYST
        // With the window's material the sidebar shows the desktop through it; the files keep a solid background.
        if (WindowMaterial.IsOn)
        {
            Sidebar.BackgroundColor = Colors.Transparent;
            Pane.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#1E1E1E"));
        }
#endif
        SizeChanged += (_, _) =>
        {
            if (BindingContext is ExplorerPageViewModel vm)
            {
                vm.IsWide = Width >= 640;
                var paneWidth = vm.IsSplit ? Root.ColumnDefinitions[3].Width.Value : 0;
                vm.GridSpan = Math.Max(2, (int)((Width - (vm.ShowsSidebar ? vm.SidebarWidth : 0) - paneWidth) / 104));
            }
        };
        Loaded += (_, _) =>
        {
            if (BindingContext is not ExplorerPageViewModel vm)
                return;
            vm.HostPage = () => Window?.Page;
            TabBar.SizeChanged += (_, _) => vm.TabBarWidth = TabBar.Width;
#if MACCATALYST
            // Spine measures the title bar for its page; a window of its own has it as the window's safe area. Neither
            // is known when the page loads, so it is read again once they are.
            void ReadTitleBar()
            {
                var top = vm.SafeAreaInsets.Top;
                if (top <= 0 && Window?.Handler?.PlatformView is UIKit.UIWindow window)
                    top = window.SafeAreaInsets.Top;
                if (top <= 0 || vm.IsSecondary)
                    return;
                vm.TitleBarHeight = top;
                // A window of its own lays the page out below the title bar; Spine's host moves its page up the same way.
                if (Parent is ContentPage)
                    Margin = new Thickness(0, -top, 0, 0);
            }
            ReadTitleBar();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.SafeAreaInsets))
                    ReadTitleBar();
                else if (e.PropertyName == nameof(vm.TitleBarHeight) && vm.Partner is { } partner)
                    partner.TitleBarHeight = vm.TitleBarHeight;
            };
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), ReadTitleBar);
#endif
            // Name, Date modified, Size and Kind need about 560 points; a narrower pane shows Name and Size.
            List.SizeChanged += (_, _) => vm.HasColumns = List.Width >= 560;
            vm.FilterRequested += () => FilterField.Focus();
            Keys.Pressed += vm.OnListKey;
            vm.RevealRequested += index =>
            {
                var list = vm.IsIcons ? Icons : List;
                list.ScrollTo(index, position: ScrollToPosition.MakeVisible, animate: false);
            };
            if (!vm.IsSecondary)
                AppMenu.Install(this, vm);
            // A deep folder's breadcrumb is wider than the bar; keep its end, the current folder, in view as the
            // segments arrive, their names fill in and the pane is resized.
            void KeepEnd(object? sender, EventArgs e) => Dispatcher.Dispatch(async () => await BreadcrumbScroll.ScrollToAsync(double.MaxValue, 0, false));
            BreadcrumbScroll.SizeChanged += KeepEnd;
            if (BreadcrumbScroll.Content is View crumbs)
                crumbs.SizeChanged += KeepEnd;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ExplorerPageViewModel.IsSplit))
                    ShowSecondPane(vm);
            };
        };
    }

    private void OnTabDragStarting(object? sender, DragStartingEventArgs e)
    {
        if (sender is not View { BindingContext: ExplorerTab tab } || BindingContext is not ExplorerPageViewModel vm)
        {
            e.Cancel = true;
            return;
        }
#if IOS || MACCATALYST
        // Nothing other apps can take: by default the tab would drop into Finder as a picture of itself.
        e.PlatformArgs?.SetItemProvider(new Foundation.NSItemProvider());
#endif
        TabDrag.Start(vm, tab);
    }

    private void OnTabPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is View { BindingContext: ExplorerTab tab })
            tab.IsHovered = true;
    }

    private void OnTabPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is View { BindingContext: ExplorerTab tab })
            tab.IsHovered = false;
    }

    private void OnTabDropCompleted(object? sender, DropCompletedEventArgs e)
    {
        if (TabDrag.End() is { Dropped: false, OverWindow: false } drag)
            drag.From.MoveTabToNewWindowCommand.Execute(drag.Tab);
    }

    private void OnTabDragOver(object? sender, DragEventArgs e)
    {
        if (TabDrag.Current is null)
        {
            OnFileDragOver(sender, e);
            return;
        }
        e.AcceptedOperation = TabDrag.Current is null ? DataPackageOperation.None : DataPackageOperation.Copy;
        if (TabDrag.Current is { } drag)
            drag.OverWindow = true;
    }

    private void OnTabDragLeave(object? sender, DragEventArgs e)
    {
        if (TabDrag.Current is null)
            OnFileDragLeave(sender, e);
        else
            OnWindowDragLeave(sender, e);
    }

    private async void OnTabDrop(object? sender, DropEventArgs e)
    {
        if (TabDrag.Current is null)
        {
            OnFileDrop(sender, e);
            return;
        }
        if (TabDrag.Current is not { } drag || BindingContext is not ExplorerPageViewModel vm)
            return;
        e.Handled = true;
        drag.Dropped = true;
        var index = vm.Tabs.Count;
        if (sender is View { BindingContext: ExplorerTab over } view)
        {
            index = vm.Tabs.IndexOf(over);
            if (e.GetPosition(view) is { } point && point.X > view.Width / 2)
                index++;
        }
        await vm.ReceiveTab(drag.From, drag.Tab, index);
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.None;
        if (TabDrag.Current is { } drag)
            drag.OverWindow = true;
    }

    private void OnWindowDragLeave(object? sender, DragEventArgs e)
    {
        if (TabDrag.Current is { } drag)
            drag.OverWindow = false;
    }

    // Files: dragged from a row; dropped on a folder row, the list (the folder shown), a tab, a place, a favorite or a
    // breadcrumb segment. From another app they come as file URLs, to another app they go as the files themselves.
    private void OnRowDragStarting(object? sender, DragStartingEventArgs e)
    {
        if (sender is not View { BindingContext: ItemRow row } || BindingContext is not ExplorerPageViewModel vm)
        {
            e.Cancel = true;
            return;
        }
        var items = vm.DragItems(row);
#if IOS || MACCATALYST
        if (items.Where(i => i.NativePath is not null).Select(i => new UIKit.UIDragItem(new Foundation.NSItemProvider(Foundation.NSUrl.CreateFileUrl(i.NativePath!, null)))).ToArray() is { Length: > 0 } files)
            e.PlatformArgs?.SetDragItems(files);
#endif
        FileDrag.Start(vm, items);
    }

    private void OnRowDropCompleted(object? sender, DropCompletedEventArgs e) => FileDrag.End();

    private StorageLocation? DropFolder(object? sender) => (sender as BindableObject)?.BindingContext switch
    {
        ItemRow { Item.IsFolder: true } row => row.Item.Location,
        ItemRow or ExplorerPageViewModel => (BindingContext as ExplorerPageViewModel)?.Current,
        ExplorerTab tab => tab.Location ?? tab.History.Current,
        PlaceRow place => place.Root.Location,
        FavoriteRow favorite => favorite.Favorite.Location,
        BreadcrumbSegment segment => segment.Location,
        _ => null,
    };

    // From this app the rule decides; from another app files are copied unless the move key is held, so a drop never
    // takes a file away from where another app keeps it by accident.
    private DropAction FileAction(object? sender, bool external)
    {
        if (TabDrag.Current is not null || BindingContext is not ExplorerPageViewModel vm || DropFolder(sender) is not { } target)
            return DropAction.None;
        if (FileDrag.Current is { } drag)
            return vm.DropActionFor(drag.Items, target);
        return !external ? DropAction.None : ModifierKeys.MoveKey ? DropAction.Move : DropAction.Copy;
    }

    private static void Highlight(object? sender, bool on)
    {
        if (sender is BindableObject { BindingContext: ItemRow { Item.IsFolder: true } row })
            row.IsDropTarget = on;
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        var external = false;
#if IOS || MACCATALYST
        external = e.PlatformArgs?.DropSession?.HasConformingItems(["public.file-url"]) == true;
#endif
        // A linked device takes files and folders from here or from another app, and sends them.
        if (sender is BindableObject { BindingContext: DeviceRow device })
        {
            var sends = TabDrag.Current is null && (external || FileDrag.Current?.Items.Any(i => i.NativePath is not null) == true);
            device.IsDropTarget = sends;
            e.AcceptedOperation = sends ? DataPackageOperation.Copy : DataPackageOperation.None;
            return;
        }
        var action = FileAction(sender, external);
        Highlight(sender, action != DropAction.None && sender is BindableObject { BindingContext: ItemRow });
        e.AcceptedOperation = action == DropAction.None ? DataPackageOperation.None : DataPackageOperation.Copy;
#if IOS || MACCATALYST
        if (action != DropAction.None)
            e.PlatformArgs?.SetDropProposal(new UIKit.UIDropProposal(action == DropAction.Move ? UIKit.UIDropOperation.Move : UIKit.UIDropOperation.Copy));
#endif
    }

    private void OnFileDragLeave(object? sender, DragEventArgs e)
    {
        Highlight(sender, false);
        if (sender is BindableObject { BindingContext: DeviceRow device })
            device.IsDropTarget = false;
    }

    private async void OnFileDrop(object? sender, DropEventArgs e)
    {
        Highlight(sender, false);
        if (sender is BindableObject { BindingContext: DeviceRow device } && BindingContext is ExplorerPageViewModel explorer)
        {
            device.IsDropTarget = false;
            e.Handled = true;
            var paths = FileDrag.Current is { } dragged ? [.. dragged.Items.Select(i => i.NativePath).OfType<string>()] : new List<string>();
#if IOS || MACCATALYST
            if (FileDrag.Current is null && e.PlatformArgs?.DropSession is { } dropped)
                paths = await DroppedPaths(dropped);
#endif
            await explorer.SendToDevice(device, paths);
            return;
        }
        if (TabDrag.Current is not null || BindingContext is not ExplorerPageViewModel vm || DropFolder(sender) is not { } target)
            return;
        e.Handled = true;
        if (FileDrag.Current is { } drag)
        {
            vm.DropFiles(drag.Items, target, vm.DropActionFor(drag.Items, target));
            return;
        }
#if IOS || MACCATALYST
        if (e.PlatformArgs?.DropSession is { } session)
        {
            var action = ModifierKeys.MoveKey ? DropAction.Move : DropAction.Copy;
            vm.DropFiles([.. (await DroppedPaths(session)).Select(FileSystemStorageProvider.Location)], target, action);
        }
#else
        await Task.CompletedTask;
#endif
    }

#if IOS || MACCATALYST
    private static async Task<List<string>> DroppedPaths(UIKit.IUIDropSession session)
    {
        var paths = new List<string>();
        foreach (var item in session.Items)
        {
            if (!item.ItemProvider.HasItemConformingTo("public.file-url"))
                continue;
            var loaded = new TaskCompletionSource<string?>();
            item.ItemProvider.LoadItem("public.file-url", null, (data, _) => loaded.TrySetResult(data switch
            {
                Foundation.NSUrl url => url.Path,
                Foundation.NSData bytes => Foundation.NSUrl.FromString(Foundation.NSString.FromData(bytes, Foundation.NSStringEncoding.UTF8)?.ToString() ?? "")?.Path,
                _ => null,
            }));
            if (await loaded.Task is { } path)
                paths.Add(path);
        }
        return paths;
    }
#endif

    // The second pane is an explorer of its own; this one keeps the sidebar and both answer to the menus by focus.
    private void ShowSecondPane(ExplorerPageViewModel vm)
    {
        if (vm.IsSplit && SecondPane.Content is null)
        {
            var second = new ExplorerPage();
            var partner = (ExplorerPageViewModel)second.BindingContext;
            partner.IsSecondary = true;
            partner.Partner = vm;
            vm.Partner = partner;
            partner.Start(null, 0, vm.Current);
            partner.TitleBarHeight = vm.TitleBarHeight;
            Root.ColumnDefinitions[3].Width = new GridLength(Math.Max(320, (Width - (vm.ShowsSidebar ? vm.SidebarWidth : 0)) / 2));
            SecondPane.Content = second;
            _ = partner.OnAppearingAsync(NavigationDirection.NavigateTo);
            partner.Focus();
        }
        else if (!vm.IsSplit && SecondPane.Content is not null)
        {
            SecondPane.Content = null;
            Root.ColumnDefinitions[3].Width = GridLength.Auto;
            vm.Partner = null;
            vm.Focus();
        }
    }

    private void OnPanePressed(object? sender, PointerEventArgs e)
    {
        if (BindingContext is ExplorerPageViewModel vm)
            vm.Focus();
    }

    // A click in the list gives it the keyboard (arrows, Return, Space, typing); a click in a text field takes it back.
    private void OnListPressed(object? sender, PointerEventArgs e) => Keys.Activate();

    private double _gripStart;

    // A grip resizes the part its ClassId names: the sidebar, the preview (its left edge, so it grows to the left) or
    // a list column. The width is kept when the drag ends.
    private void OnGripPanned(object? sender, PanUpdatedEventArgs e)
    {
        if (sender is not Element { ClassId: { } part } || BindingContext is not ExplorerPageViewModel vm)
            return;
        var direction = part == nameof(ExplorerPageViewModel.PreviewWidth) ? -1 : 1;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _gripStart = vm.Width(part);
                break;
            case GestureStatus.Running:
                vm.Resize(part, _gripStart + direction * e.TotalX, keep: false);
                break;
            case GestureStatus.Completed:
                vm.Resize(part, vm.Width(part), keep: true);
                break;
        }
    }

    private void OnSplitterPanned(object? sender, PanUpdatedEventArgs e)
    {
        var column = Root.ColumnDefinitions[3];
        if (e.StatusType == GestureStatus.Started)
            _splitStart = column.Width.Value;
        else if (e.StatusType == GestureStatus.Running)
            column.Width = new GridLength(Math.Clamp(_splitStart - e.TotalX, 240, Math.Max(240, Width - 450)));
    }
}
