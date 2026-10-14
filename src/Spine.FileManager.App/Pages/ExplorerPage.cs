namespace Spine.FileManager.Pages;

[NavigableRegion(IsHeaderBarVisible = false)]
public partial class ExplorerPage
{
    public ExplorerPage()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            if (BindingContext is ExplorerPageViewModel vm)
            {
                vm.IsWide = Width >= 640;
                vm.GridSpan = Math.Max(2, (int)((Width - (vm.IsWide ? 210 : 0)) / 104));
            }
        };
        Loaded += (_, _) =>
        {
            if (BindingContext is not ExplorerPageViewModel vm)
                return;
            AppMenu.Install(this, vm);
            // A deep folder's breadcrumb is wider than the bar; keep its end, the current folder, in view.
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ExplorerPageViewModel.Segments))
                    Dispatcher.Dispatch(async () => await BreadcrumbScroll.ScrollToAsync(double.MaxValue, 0, false));
            };
        };
    }
}
