using Plugin.Maui.Spine.Common;

namespace Spine.FileManager.Pages;

/// <summary>
/// The Mac's first launch: macOS asks once per protected folder, and the call that touches the folder waits for the
/// answer. Touching them here, on purpose, gathers the prompts into one moment instead of scattering them through the
/// first browsing (docs/plan.md, Decisions).
/// </summary>
public partial class FirstRunPageViewModel(ISpineStrings strings, INavigationService navigation, StorageProviders providers) : ViewModelBase
{
    public const string DoneKey = "firstRun.done";

    public static bool IsNeeded => DeviceInfo.Platform == DevicePlatform.MacCatalyst && !Preferences.Get(DoneKey, false);

    [ObservableProperty]
    public partial bool IsWorking { get; private set; }

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        Title = strings["FirstRun.Title"];
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Continue()
    {
        IsWorking = true;
        var protectedKinds = new[] { StorageRootKind.Desktop, StorageRootKind.Documents, StorageRootKind.Downloads, StorageRootKind.Cloud };
        foreach (var provider in providers.All)
            foreach (var root in await provider.GetRootsAsync())
                if (protectedKinds.Contains(root.Kind))
                    await Touch(provider, root.Location);
        Preferences.Set(DoneKey, true);
        IsWorking = false;
        await navigation.CloseAsync();
    }

    [RelayCommand]
    private Task OpenFullDiskAccess() =>
        Launcher.Default.OpenAsync("x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles");

    // Closing the sheet without continuing also counts: macOS will still ask, folder by folder, when one is opened.
    public override Task OnDismissedAsync()
    {
        Preferences.Set(DoneKey, true);
        return Task.CompletedTask;
    }

    private static async Task Touch(IStorageProvider provider, StorageLocation folder)
    {
        try
        {
            await using var items = provider.EnumerateAsync(folder).GetAsyncEnumerator();
            await items.MoveNextAsync();
        }
        catch (StorageException)
        {
        }
    }
}
