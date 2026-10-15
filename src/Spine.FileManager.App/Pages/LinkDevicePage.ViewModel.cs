using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Scanner;
using Spine.FileManager.DeviceLink;

namespace Spine.FileManager.Pages;

/// <summary>
/// Link Device (plan §19–21): first a choice, then this device shows a code for another to scan, joins the code another
/// device shows (scanned on a phone, entered on a computer, which can try its camera), or, on a phone, links two
/// computers. The code is a one-time invitation that changes every two minutes; how long the other device is trusted
/// here is chosen before it joins.
/// </summary>
public partial class LinkDevicePageViewModel(ISpineStrings strings, INavigationService navigation, DeviceService devices) : ViewModelBase, IReceivesNavigationParameter<JoinRequest>
{
    private string? _pendingJoin;
    private static readonly TrustLifetime[] Lifetimes = [TrustLifetime.QuickTransfer, TrustLifetime.Session, TrustLifetime.Remember];
    private LinkNode? _node;
    private IDispatcherTimer? _renew;

    private enum LinkMode { Choose, Show, Join, Bridge }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosing), nameof(IsShowing), nameof(IsJoining), nameof(IsBridging))]
    private partial LinkMode Mode { get; set; }

    public bool IsChoosing => Mode == LinkMode.Choose;
    public bool IsShowing => Mode == LinkMode.Show;
    public bool IsJoining => Mode == LinkMode.Join;
    public bool IsBridging => Mode == LinkMode.Bridge;

    public string JoinTitle => strings[CanScan ? "Link.Choice.Scan" : "Link.Choice.Enter"];
    public string JoinHint => strings[CanScan ? "Link.Choice.Scan.Hint" : "Link.Choice.Enter.Hint"];
    public string ScanText => strings[CanScan ? "Link.Scan" : "Link.TryScan"];

    /// <summary>Whether this device has a camera the scanner can use: a Mac with one can try it, Windows cannot.</summary>
    public bool HasCamera => BarcodeScannerView.IsSupported;

    public bool HasStatus => Status.Length > 0;

    [ObservableProperty]
    public partial string Link { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; private set; } = "";

    [ObservableProperty]
    public partial int LifetimeIndex { get; set; } = 2;

    [ObservableProperty]
    public partial string JoinLink { get; set; } = "";

    // Low error correction: fewer, larger modules, which a phone reads from a screen more easily.
    public Plugin.Maui.Spine.Barcodes.QrCodeOptions QrOptions { get; } = new() { ErrorCorrection = Plugin.Maui.Spine.Barcodes.QrErrorCorrection.Low };

    public bool CanScan => DeviceInfo.Idiom == DeviceIdiom.Phone || DeviceInfo.Idiom == DeviceIdiom.Tablet;

    private TrustLifetime Lifetime => Lifetimes[Math.Clamp(LifetimeIndex, 0, 2)];

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        Title = strings["Link.Title"];
        if (_node is not null)
            return;
        _node = await devices.GetNodeAsync();
        devices.Paired += OnPaired;
        _renew = Application.Current!.Dispatcher.CreateTimer();
        _renew.Interval = PairingInvitation.Lifetime - TimeSpan.FromSeconds(10);
        _renew.Tick += (_, _) => Renew();
        if (_pendingJoin is { } link)
        {
            _pendingJoin = null;
            await JoinWith(link);
        }
    }

    // Opened from a pairing link: it joins as soon as this device's node is ready.
    public async Task OnNavigationParameterAsync(JoinRequest parameter)
    {
        JoinLink = parameter.Link;
        Mode = LinkMode.Join;
        if (_node is null)
            _pendingJoin = parameter.Link;
        else
            await JoinWith(parameter.Link);
    }

    partial void OnLifetimeIndexChanged(int value) => Renew();

    private void Renew()
    {
        if (_node is null || Mode != LinkMode.Show)
            return;
        Link = _node.Invite(Lifetime).ToUri();
        Status = strings["Link.Waiting"];
    }

    private void OnPaired(TrustedNode node) => MainThread.BeginInvokeOnMainThread(async () =>
    {
        Status = strings.Get("Link.Linked", node.DisplayName);
        await Task.Delay(1200);
        await navigation.CloseAsync();
    });

    [RelayCommand]
    private void Choose()
    {
        // Back to the choice: a code that was shown is no longer good.
        _renew?.Stop();
        _node?.CancelInvitation();
        Status = "";
        Mode = LinkMode.Choose;
    }

    [RelayCommand]
    private void ShowCode()
    {
        Mode = LinkMode.Show;
        Renew();
        _renew?.Start();
    }

    // A phone scans at once; a computer starts with the link entered, and can try its camera from there.
    [RelayCommand]
    private async Task JoinOther()
    {
        Status = "";
        Mode = LinkMode.Join;
        if (CanScan)
            await Scan();
    }

    [RelayCommand]
    private Task CopyLink() => Clipboard.Default.SetTextAsync(Link);

    [RelayCommand]
    private async Task Join() => await JoinWith(JoinLink.Trim());

    [RelayCommand]
    private async Task Scan()
    {
        var scan = await navigation.NavigateToWithResultAsync<BarcodeScannerPage, BarcodeScanOptions, BarcodeScanResult>(
            new BarcodeScanOptions { Formats = Plugin.Maui.Spine.Barcodes.BarcodeFormat.QrCode });
        if (scan is { IsSuccess: true, Value: { } code })
            await JoinWith(code.Value);
    }

    /// <summary>
    /// The phone between two computers: it scans one computer's code, then the other's, and hands the first to the
    /// second, which links with it. The phone itself is linked to neither.
    /// </summary>
    [RelayCommand]
    private async Task LinkTwoComputers()
    {
        Status = "";
        Mode = LinkMode.Bridge;
        if (await ScanInvitation() is not { } first)
            return;
        Status = strings.Get("Link.ScanSecond", first.Name);
        await Task.Delay(800);
        if (await ScanInvitation() is not { } second)
            return;
        if (second.NodeId == first.NodeId)
        {
            Status = strings["Link.SameComputer"];
            return;
        }
        try
        {
            Status = strings.Get("Link.Introducing", first.Name, second.Name);
            await LinkNode.IntroduceAsync(to: second, introduced: first);
            Status = strings.Get("Link.Introduced", first.Name, second.Name);
        }
        catch (Exception e) when (e is PairingException or IOException or System.Net.Sockets.SocketException)
        {
            Status = strings.Get("Link.Failed", e.Message);
        }
    }

    private async Task<PairingInvitation?> ScanInvitation()
    {
        var scan = await navigation.NavigateToWithResultAsync<BarcodeScannerPage, BarcodeScanOptions, BarcodeScanResult>(
            new BarcodeScanOptions { Formats = Plugin.Maui.Spine.Barcodes.BarcodeFormat.QrCode });
        if (scan is not { IsSuccess: true, Value: { } code })
            return null;
        try
        {
            return PairingInvitation.Parse(code.Value);
        }
        catch (FormatException e)
        {
            Status = strings.Get("Link.Failed", e.Message);
            return null;
        }
    }

    private async Task JoinWith(string text)
    {
        if (_node is null || text.Length == 0)
            return;
        try
        {
            var invitation = PairingInvitation.Parse(text);
            Status = strings["Link.Joining"];
            var host = await _node.JoinAsync(invitation, Lifetime);
            Status = strings.Get("Link.Linked", host.DisplayName);
            await Task.Delay(1200);
            await navigation.CloseAsync();
        }
        catch (Exception e) when (e is FormatException or PairingException or IOException or System.Net.Sockets.SocketException)
        {
            Status = strings.Get("Link.Failed", e.Message);
        }
    }

    public override Task OnDismissedAsync()
    {
        _renew?.Stop();
        devices.Paired -= OnPaired;
        _node?.CancelInvitation();
        return Task.CompletedTask;
    }
}
