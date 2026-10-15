namespace Spine.FileManager.Pages;

[NavigableSheet(AllowedDetents = [SheetDetent.Medium, SheetDetent.Expanded], InitialDetent = SheetDetent.Medium)]
public partial class SettingsPage
{
    public SettingsPage()
    {
        InitializeComponent();
        SheetTheme.Follow(this);
    }
}
