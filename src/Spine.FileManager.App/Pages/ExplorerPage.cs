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
                vm.IsWide = Width >= 640;
        };
        Loaded += (_, _) =>
        {
            if (BindingContext is ExplorerPageViewModel vm)
                AppMenu.Install(this, vm);
        };
    }
}
