#if WINDOWS
namespace FileSystemSpike;

public static partial class FolderAccess
{
    public static partial async Task<IReadOnlyList<LocationReport>> PickAndProbeAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        var window = (Microsoft.UI.Xaml.Window)Application.Current!.Windows[0].Handler!.PlatformView!;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return [];

        var report = await Task.Run(() => PathProbe.Run($"Picked {folder.Name}", folder.Path, write: true, via: "System.IO, picked"));
        report.Run("future access list", () =>
        {
            // Needs package identity; the app runs unpackaged, so the path itself is what is kept.
            Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Add(folder);
            return "added";
        });
        Save(Load().Append(folder.Path).Distinct());
        return [report];
    }

    public static partial Task<IReadOnlyList<LocationReport>> ProbeGrantedAsync() => Task.Run<IReadOnlyList<LocationReport>>(() =>
        [.. Load().Select(path => PathProbe.Run($"{Path.GetFileName(path)} after restart", path, write: true, via: "System.IO, kept path"))]);
}
#endif
