using Plugin.Maui.Spine.Common;

namespace Spine.FileManager.Pages;

public partial class HomePageViewModel : ViewModelBase
{
    private readonly ISpineStrings _strings;

    public HomePageViewModel(ISpineStrings strings)
    {
        _strings = strings;
        _strings.Changed += (_, _) => Title = _strings["Home.Title"];
    }

    public string Version => $"Spine File Manager {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

    // Navigation sets Title from [NavigableRegion], which takes literal text, before the page appears.
    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        Title = _strings["Home.Title"];
        return Task.CompletedTask;
    }
}
