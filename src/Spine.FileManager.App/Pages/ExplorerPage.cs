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

    // The icon grid's margin and the gap between its columns (GridItemsLayout.HorizontalItemSpacing in the view).
    private const double IconsMargin = 8, GridSpacing = 4;

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
        void Fit()
        {
            if (BindingContext is ExplorerPageViewModel vm)
            {
                vm.IsWide = Width >= 640;
                var paneWidth = vm.IsSplit ? Root.ColumnDefinitions[3].Width.Value : 0;
                if (Icons.Width <= 0)
                {
                    vm.GridSpan = Math.Max(1, (int)((Width - (vm.ShowsSidebar ? vm.SidebarWidth : 0) - paneWidth) / ItemSizes.GridCellWidth));
                    return;
                }
                // A wrapping grid, as Finder's: a CollectionView grid shares its width among its columns, so the cells
                // moved apart as the pane grew. It is made exactly as wide as its whole columns (the room left goes to
                // its right margin), so the icons stay where they are and only wrap when a column comes or goes.
                var pitch = ItemSizes.GridCellWidth;
                var room = Icons.Width + Icons.Margin.Right - IconsMargin;
                var span = Math.Max(1, (int)((room + GridSpacing) / pitch));
                vm.GridSpan = span;
                var spare = Math.Max(0, room - (span * pitch - GridSpacing));
                if (Math.Abs(Icons.Margin.Right - IconsMargin - spare) > 0.5)
                    Icons.Margin = new Thickness(IconsMargin, IconsMargin, IconsMargin + spare, 0);
            }
        }
        SizeChanged += (_, _) => Fit();
        Icons.SizeChanged += (_, _) => Fit();
        ItemSizes.Changed += () =>
        {
            Fit();
            // The lists keep the size they measured their first row at; switching the strategy measures again.
            foreach (var list in new[] { List, Icons })
            {
                list.ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems;
                Dispatcher.Dispatch(() => list.ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem);
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
            List.Scrolled += OnListScrolled;
            Icons.Scrolled += OnListScrolled;
            // Focused once the field has folded out.
            vm.FilterRequested += () => Dispatcher.Dispatch(() => FilterField.Focus());
            Keys.Pressed += vm.OnListKey;
            vm.RevealRequested += index =>
            {
                var list = vm.IsIcons ? Icons : List;
                list.ScrollTo(index, position: ScrollToPosition.MakeVisible, animate: false);
            };
            if (!vm.IsSecondary)
                AppMenu.Install(this, vm);
            CrumbArea.SizeChanged += (_, _) => FitCrumbs();
            vm.Segments.CollectionChanged += (_, _) => Dispatcher.Dispatch(FitCrumbs);
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

    private void OnCrumbSizeChanged(object? sender, EventArgs e)
    {
        if (sender is View { BindingContext: BreadcrumbSegment segment, Width: > 0 } view && view.IsVisible)
        {
            segment.Width = view.Width;
            FitCrumbs();
        }
    }

    private string? _collapsed;

    // As Windows' address bar: the current folder and as many of its parents as fit show, the rest fold from the root
    // end into « . Widths are those last laid out, so a folded segment still counts with its own width.
    private void FitCrumbs()
    {
        if (BindingContext is not ExplorerPageViewModel vm || CrumbArea.Width <= 0 || vm.Segments.Count == 0)
            return;
        var segments = vm.Segments.ToList();
        if (segments.Any(s => s.Width <= 0))
            return;
        var room = CrumbArea.Width + (CrumbOverflow.IsVisible ? CrumbOverflow.Width : 0);
        var count = 0;
        if (segments.Sum(s => s.Width) > room)
        {
            var budget = room - 30;
            var used = 0d;
            var kept = 0;
            for (var i = segments.Count - 1; i >= 0 && (kept == 0 || used + segments[i].Width <= budget); i--, kept++)
                used += segments[i].Width;
            count = segments.Count - kept;
        }
        var key = $"{count}|{string.Join('/', segments.Take(count).Select(s => s.Name))}";
        if (key == _collapsed)
            return;
        _collapsed = key;
        vm.Collapse(count);
    }

    private void OnFilterFocused(object? sender, FocusEventArgs e) => SearchCapsule.StrokeThickness = 2;

    private void OnFilterUnfocused(object? sender, FocusEventArgs e)
    {
        SearchCapsule.StrokeThickness = 0;
        if (BindingContext is ExplorerPageViewModel { IsSearching: false } vm && string.IsNullOrEmpty(vm.Filter))
            vm.IsSearchOpen = false;
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
        // Each file drags as its icon; several stack, with their count, as Finder's do. The file pressed shows the
        // icon its row shows (the system's or a theme's), the others Spine's picture for their kind.
        var shown = ((sender as Layout) ?? ((View)sender).Parent as Layout)?.OfType<Image>().FirstOrDefault()?.Handler?.PlatformView as UIKit.UIImageView;
        var byLocation = vm.Rows.ToDictionary(r => r.Item.Location, r => r.Item);
        if (items.Where(i => i.NativePath is not null).Select(location =>
            {
                var drag = new UIKit.UIDragItem(new Foundation.NSItemProvider(Foundation.NSUrl.CreateFileUrl(location.NativePath!, null)));
                var icon = location.Equals(row.Item.Location) ? shown?.Image
                    : byLocation.TryGetValue(location, out var item) ? UIKit.UIImage.FromBundle(FileIcons.BundleName(item)) : null;
                if (icon is not null)
                    drag.PreviewProvider = () => new UIKit.UIDragPreview(new UIKit.UIImageView(icon)
                    {
                        Frame = new CoreGraphics.CGRect(0, 0, 56, 56),
                        ContentMode = UIKit.UIViewContentMode.ScaleAspectFit,
                    });
                return drag;
            }).ToArray() is { Length: > 0 } files)
            e.PlatformArgs?.SetDragItems(files);
#endif
        FileDrag.Start(vm, items);
    }

    private void OnRowDropCompleted(object? sender, DropCompletedEventArgs e) => FileDrag.End();

    private StorageLocation? DropFolder(object? sender) => (sender as BindableObject)?.BindingContext switch
    {
        ItemRow { Item.IsBrowsable: true } row => row.Item.Location,
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
        if (sender is BindableObject { BindingContext: ItemRow { Item.IsBrowsable: true } row })
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
    private Point? _pressed;
    private Point? _itemPressed;
    private double _listOffset;
    private bool _boxing;
    private Point _boxStart;
    private Point _boxPress;
    private Point _boxTotal;
    private IReadOnlyCollection<int> _boxKept = [];
    private IDispatcherTimer? _autoScroll;

    // The list shown: the details list or the icon grid.
    private Controls.FileListView Shown => (BindingContext as ExplorerPageViewModel)?.IsIcons == true ? Icons : List;

    private void OnListPressed(object? sender, PointerEventArgs e)
    {
        Keys.Activate();
        _pressed = e.GetPosition(Shown);
    }

    // A press on what drags files: a row's name or icon, a whole cell of the icon grid.
    private void OnItemPressed(object? sender, PointerEventArgs e) => _itemPressed = e.GetPosition(Shown);

    // As in Finder and Explorer, a drag from a file moves it and a drag from anywhere else in the list draws a
    // selection rectangle. It keeps its start in the list's content, so it stretches as the list scrolls: by the
    // wheel, or on its own while the pointer is near the top or bottom edge.
    private void OnListPanned(object? sender, PanUpdatedEventArgs e)
    {
        if (BindingContext is not ExplorerPageViewModel vm || sender != Shown)
            return;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                if (_pressed is not { } start || _itemPressed is { } item && start.Distance(item) < 4)
                    return;
                _boxing = true;
                _boxPress = start;
                _boxStart = new Point(start.X, start.Y + _listOffset);
                _boxTotal = Point.Zero;
                _boxKept = ModifierKeys.Command ? vm.SelectedIndices : [];
                Marquee.IsVisible = true;
                _autoScroll ??= CreateAutoScroll();
                _autoScroll.Start();
                UpdateBox();
                break;
            case GestureStatus.Running when _boxing:
                _boxTotal = new Point(e.TotalX, e.TotalY);
                UpdateBox();
                break;
            case GestureStatus.Completed or GestureStatus.Canceled:
                _boxing = false;
                _itemPressed = null;
                _autoScroll?.Stop();
                Marquee.IsVisible = false;
                break;
        }
    }

    private void OnListScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (sender != Shown)
            return;
        _listOffset = e.VerticalOffset;
        UpdateBox();
    }

    // The pointer in the list's frame now: where the press was, moved by the drag.
    private Point BoxPointer => new(_boxPress.X + _boxTotal.X, _boxPress.Y + _boxTotal.Y);

    private void UpdateBox()
    {
        if (!_boxing || BindingContext is not ExplorerPageViewModel vm)
            return;
        var list = Shown;
        var pointer = BoxPointer;
        var left = Math.Max(0, Math.Min(_boxStart.X, pointer.X));
        var right = Math.Min(list.Width, Math.Max(_boxStart.X, pointer.X));
        var top = Math.Min(_boxStart.Y, pointer.Y + _listOffset);
        var bottom = Math.Max(_boxStart.Y, pointer.Y + _listOffset);
        // Drawn over the list's visible part only; the rectangle sits in the rows both lists share.
        var shownTop = Math.Clamp(top - _listOffset, 0, list.Height);
        var shownBottom = Math.Clamp(bottom - _listOffset, 0, list.Height);
        Marquee.TranslationX = list.Frame.X - Marquee.Frame.X + left;
        Marquee.TranslationY = list.Frame.Y - Marquee.Frame.Y + shownTop;
        Marquee.WidthRequest = Math.Max(0, right - left);
        Marquee.HeightRequest = Math.Max(0, shownBottom - shownTop);
        if (ItemsIn(list, new Rect(left, top, right - left, bottom - top)) is { } items)
            vm.SelectItems(items, _boxKept);
        else
            vm.SelectRows((int)Math.Floor(Math.Max(0, top) / ItemSizes.RowHeight), (int)Math.Floor(bottom / ItemSizes.RowHeight), _boxKept);
    }

    // The cells the list's own layout puts inside the rectangle (in content coordinates), in the grid as in the list;
    // null where the layout cannot be asked, and the rows are counted from the row height instead.
    private static IReadOnlyList<int>? ItemsIn(Controls.FileListView list, Rect content)
    {
#if IOS || MACCATALYST
        if (FindScrollView(list.Handler?.PlatformView as UIKit.UIView) is UIKit.UICollectionView view
            && view.CollectionViewLayout.LayoutAttributesForElementsInRect(new CoreGraphics.CGRect(content.X, content.Y - view.AdjustedContentInset.Top, content.Width, Math.Max(1, content.Height))) is { } cells)
            return [.. cells.Where(c => c.RepresentedElementCategory == UIKit.UICollectionElementCategory.Cell).Select(c => (int)c.IndexPath.Item)];
#endif
        return null;
    }

    private IDispatcherTimer CreateAutoScroll()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) =>
        {
            const double edge = 24;
            var y = BoxPointer.Y;
            var height = Shown.Height;
            var step = y < edge ? -(edge - y) : y > height - edge ? y - (height - edge) : 0;
            if (step != 0)
                ScrollListBy(Math.Clamp(step / 2, -24, 24));
        };
        return timer;
    }

    private void ScrollListBy(double dy)
    {
#if IOS || MACCATALYST
        if (FindScrollView(Shown.Handler?.PlatformView as UIKit.UIView) is { } scroll)
        {
            var max = Math.Max(0, scroll.ContentSize.Height + scroll.AdjustedContentInset.Bottom - scroll.Bounds.Height);
            var y = Math.Clamp(scroll.ContentOffset.Y + dy, 0, max);
            scroll.SetContentOffset(new CoreGraphics.CGPoint(scroll.ContentOffset.X, y), false);
        }
#endif
    }

#if IOS || MACCATALYST
    private static UIKit.UIScrollView? FindScrollView(UIKit.UIView? view) => view switch
    {
        null => null,
        UIKit.UIScrollView scroll => scroll,
        _ => view.Subviews.Select(FindScrollView).FirstOrDefault(s => s is not null),
    };
#endif

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
                var width = _gripStart + direction * e.TotalX;
                QueueResize(() => vm.Resize(part, width, keep: false));
                break;
            case GestureStatus.Completed:
                FlushResize();
                vm.Resize(part, vm.Width(part), keep: true);
                break;
        }
    }

    private static readonly Color GripAccent = Color.FromArgb("#2F6FEB");

    // Over a grip the pointer turns into the resize arrows and the grip's line into the accent, so the edge reads as
    // something to drag.
    private void OnGripEntered(object? sender, PointerEventArgs e)
    {
        ResizeCursor.Show();
        if (GripLine(sender) is { } line)
        {
            line.Color = GripAccent;
            line.Opacity = 1;
        }
    }

    private void OnGripExited(object? sender, PointerEventArgs e)
    {
        ResizeCursor.Hide();
        if (GripLine(sender) is not { } line)
            return;
        line.SetAppThemeColor(BoxView.ColorProperty, Color.FromArgb("#D5D7DB"), Color.FromArgb("#3A3C40"));
        line.Opacity = line.StyleId == "HoverOnly" ? 0 : 1;
    }

    private static BoxView? GripLine(object? sender) => sender switch
    {
        BoxView box => box,
        Grid grid => grid.Children.OfType<BoxView>().FirstOrDefault(),
        _ => null,
    };

    private void OnSplitterPanned(object? sender, PanUpdatedEventArgs e)
    {
        var column = Root.ColumnDefinitions[3];
        if (e.StatusType == GestureStatus.Started)
            _splitStart = column.Width.Value;
        else if (e.StatusType == GestureStatus.Running)
        {
            var width = Math.Clamp(_splitStart - e.TotalX, 240, Math.Max(240, Width - 450));
            QueueResize(() =>
            {
                column.Width = new GridLength(width);
                // A changed column width alone did not lay the grid out again.
                ((IView)Root).InvalidateMeasure();
            });
        }
    }

    private Action? _resize;
    private bool _resizeQueued;

    // A drag reports far more often than the window can lay out both panes' lists again; each relayout waits for the
    // next frame and takes only the latest width, so the edge follows the pointer instead of trailing it in steps.
    private void QueueResize(Action resize)
    {
        _resize = resize;
        if (_resizeQueued)
            return;
        _resizeQueued = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), FlushResize);
    }

    private void FlushResize()
    {
        _resizeQueued = false;
        _resize?.Invoke();
        _resize = null;
    }
}
