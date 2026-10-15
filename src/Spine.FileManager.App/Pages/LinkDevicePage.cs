namespace Spine.FileManager.Pages;

[NavigableSheet(AllowedDetents = [SheetDetent.Expanded], InitialDetent = SheetDetent.Expanded)]
public partial class LinkDevicePage : INavigableWithParameter<JoinRequest>
{
    public LinkDevicePage()
    {
        InitializeComponent();
        SheetTheme.Follow(this);
    }
}
