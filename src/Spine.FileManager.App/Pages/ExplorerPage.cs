namespace Spine.FileManager.Pages;

[NavigableRegion(IsHeaderBarVisible = false)]
public partial class ExplorerPage
{
    private double _splitStart;

    public ExplorerPage()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            if (BindingContext is ExplorerPageViewModel vm)
            {
                vm.IsWide = Width >= 640;
                var paneWidth = vm.IsSplit ? Root.ColumnDefinitions[3].Width.Value : 0;
                vm.GridSpan = Math.Max(2, (int)((Width - (vm.ShowsSidebar ? 210 : 0) - paneWidth) / 104));
            }
        };
        Loaded += (_, _) =>
        {
            if (BindingContext is not ExplorerPageViewModel vm)
                return;
            vm.HostPage = () => Window?.Page;
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
            Root.ColumnDefinitions[3].Width = new GridLength(Math.Max(320, (Width - (vm.ShowsSidebar ? 210 : 0)) / 2));
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

    private void OnSplitterPanned(object? sender, PanUpdatedEventArgs e)
    {
        var column = Root.ColumnDefinitions[3];
        if (e.StatusType == GestureStatus.Started)
            _splitStart = column.Width.Value;
        else if (e.StatusType == GestureStatus.Running)
            column.Width = new GridLength(Math.Clamp(_splitStart - e.TotalX, 240, Math.Max(240, Width - 450)));
    }
}
