using System.Collections.ObjectModel;
using Plugin.Maui.Spine.Common;

namespace Spine.FileManager.Pages;

public sealed record IconStyleChoice(string Style, string Title);

/// <summary>
/// Settings, from the sidebar's foot (as in the concept image) or File › Settings: the appearance (the system's, light
/// or dark, kept by Spine's theme service), the Mac's window material, the file icons and where received files go.
/// </summary>
public partial class SettingsPageViewModel(ISpineStrings strings, IThemeService theme, INavigationService navigation) : ViewModelBase
{
    private static readonly AppTheme[] Appearances = [AppTheme.Unspecified, AppTheme.Light, AppTheme.Dark];

    [ObservableProperty]
    public partial bool ConfirmTrash { get; set; } = Preferences.Get(ExplorerPageViewModel.ConfirmTrashKey, true);

    partial void OnConfirmTrashChanged(bool value) => Preferences.Set(ExplorerPageViewModel.ConfirmTrashKey, value);

    /// <summary>Small, medium or large rows, icons and text in the lists and the sidebar.</summary>
    /// <summary>Places hidden from the sidebar, each with Show to bring it back.</summary>
    public ObservableCollection<HiddenPlace> HiddenPlaces { get; } = new(Services.HiddenPlaces.Items);

    public bool HasHiddenPlaces => HiddenPlaces.Count > 0;

    [RelayCommand]
    private void ShowPlace(HiddenPlace place)
    {
        Services.HiddenPlaces.Show(place);
        HiddenPlaces.Remove(place);
        OnPropertyChanged(nameof(HasHiddenPlaces));
    }

    [ObservableProperty]
    public partial int ItemSizeIndex { get; set; } = (int)ItemSizes.Current;

    partial void OnItemSizeIndexChanged(int value) => ItemSizes.Use((ItemSize)Math.Clamp(value, 0, 2));

    [ObservableProperty]
    public partial int AppearanceIndex { get; set; } = Math.Max(0, Array.IndexOf(Appearances, theme.Current));

    partial void OnAppearanceIndexChanged(int value) => theme.Current = Appearances[Math.Clamp(value, 0, 2)];

    public bool HasWindowMaterial => DeviceInfo.Platform == DevicePlatform.MacCatalyst;

    /// <summary>The Mac's translucent window; it applies to the windows opened after the change.</summary>
    [ObservableProperty]
    public partial bool WindowMaterial { get; set; } = Preferences.Get("window.material", true);

    partial void OnWindowMaterialChanged(bool value) => Preferences.Set("window.material", value);

    public IReadOnlyList<IconStyleChoice> IconStyles { get; } =
    [
        new(FileIcons.SpineStyle, strings["Menu.Icons.Spine"]),
        .. FileIcons.HasSystemIcons ? [new IconStyleChoice(FileIcons.SystemStyle, strings["Menu.Icons.System"])] : Array.Empty<IconStyleChoice>(),
        .. FileIcons.Themes.Select(t => new IconStyleChoice(Path.GetFileName(t.Folder), t.Name)),
    ];

    [ObservableProperty]
    public partial IconStyleChoice? IconStyle { get; set; }

    partial void OnIconStyleChanged(IconStyleChoice? value)
    {
        if (value is not null && value.Style != FileIcons.Style)
            FileIcons.Use(value.Style);
    }

    public string ReceivedFolder => DeviceService.Inbox;

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        Title = strings["Settings.Title"];
        IconStyle = IconStyles.FirstOrDefault(s => s.Style == FileIcons.Style) ?? IconStyles[0];
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenThemes()
    {
        await navigation.CloseAsync();
        AppMenu.Explorer?.OpenLocationCommand.Execute(Storage.FileSystem.FileSystemStorageProvider.Location(FileIcons.PrepareThemesFolder()));
    }

    [RelayCommand]
    private async Task ShowReceived()
    {
        Directory.CreateDirectory(DeviceService.Inbox);
        await navigation.CloseAsync();
        AppMenu.Explorer?.OpenLocationCommand.Execute(Storage.FileSystem.FileSystemStorageProvider.Location(DeviceService.Inbox));
    }
}
