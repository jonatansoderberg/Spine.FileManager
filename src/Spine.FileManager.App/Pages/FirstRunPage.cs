namespace Spine.FileManager.Pages;

[NavigableSheet(AllowedDetents = [SheetDetent.Medium], InitialDetent = SheetDetent.Medium)]
public partial class FirstRunPage
{
    public FirstRunPage()
    {
        InitializeComponent();
        SheetTheme.Follow(this);
    }
}
