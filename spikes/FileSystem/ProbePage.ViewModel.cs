using System.Text;

namespace FileSystemSpike;

/// <summary>
/// Probes every known and granted location at launch and writes the report to probe-report.md in the app's data folder
/// (and to stdout on the Mac, whose container other processes cannot read). Launch arguments: SPIKE_PICK=1 opens the
/// picker after the probe, SPIKE_PHOTOS=1 adds the photo library; on Android as intent extras (am start -e pick 1).
/// </summary>
public partial class ProbePageViewModel : ViewModelBase
{
    readonly List<LocationReport> _picked = [];
    LocationReport? _photos;
    bool _started;

    [ObservableProperty]
    public partial string Text { get; set; } = "Probing…";

    public bool IsAndroid => DeviceInfo.Platform == DevicePlatform.Android;

    public bool HasPhotos => DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.MacCatalyst;

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (_started)
            return;
        _started = true;
        await Probe();
        if (Argument("photos"))
            await Photos();
        if (Argument("pick"))
            await Pick();
    }

    [RelayCommand]
    async Task Probe()
    {
        Text = "Probing…";
        var reports = await Task.Run(() => KnownLocations.All().Select(l => PathProbe.Run(l.Name, l.Path, l.Write)).ToList());
        reports.AddRange(await FolderAccess.ProbeGrantedAsync());
        _known = reports;
        Publish();
    }

    List<LocationReport> _known = [];

    [RelayCommand]
    async Task Pick()
    {
        _picked.AddRange(await FolderAccess.PickAndProbeAsync());
        Publish();
    }

    [RelayCommand]
    async Task Photos()
    {
#if IOS || MACCATALYST
        _photos = await PhotosProbe.RunAsync();
        Publish();
#else
        await Task.CompletedTask;
#endif
    }

    [RelayCommand]
    void AllFiles()
    {
#if ANDROID
        FolderAccess.RequestAllFilesAccess();
#endif
    }

    [RelayCommand]
    void Forget() => FolderAccess.Forget();

    void Publish()
    {
        List<LocationReport> all = [.. _known, .. _picked];
        if (_photos is not null)
            all.Add(_photos);
        Text = ReportWriter.Render("File-system probe", all);
        File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "probe-report.md"), Text);
#if MACCATALYST
        using var stdout = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(1, false), FileAccess.Write);
        stdout.Write(Encoding.UTF8.GetBytes($"\n===REPORT===\n{Text}\n===END===\n"));
#else
        Console.WriteLine(Text);
#endif
    }

    static bool Argument(string name)
    {
#if ANDROID
        return Platform.CurrentActivity?.Intent?.GetStringExtra(name) == "1";
#else
        return Environment.GetEnvironmentVariable($"SPIKE_{name.ToUpperInvariant()}") == "1";
#endif
    }
}
