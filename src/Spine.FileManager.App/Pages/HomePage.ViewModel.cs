using System.Collections.ObjectModel;
using Plugin.Maui.Spine.Common;

namespace Spine.FileManager.Pages;

public sealed record PlaceRow(StorageRoot Root, string Kind)
{
    public string Name => Root.Name;
    public string Path => Root.Location.DisplayPath;
}

public partial class HomePageViewModel : ViewModelBase
{
    private readonly ISpineStrings _strings;
    private readonly StorageProviders _providers;

    public HomePageViewModel(ISpineStrings strings, StorageProviders providers)
    {
        _strings = strings;
        _providers = providers;
        _strings.Changed += (_, _) => Title = _strings["Home.Title"];
    }

    public ObservableCollection<PlaceRow> Places { get; } = [];

    public string Version => $"Spine File Manager {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

    // Navigation sets Title from [NavigableRegion], which takes literal text, before the page appears.
    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        Title = _strings["Home.Title"];
        if (Places.Count > 0)
            return;
        foreach (var provider in _providers.All)
            foreach (var root in await provider.GetRootsAsync())
                Places.Add(new(root, _strings[$"Place.Kind.{root.Kind}"]));
    }
}
