using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Scanner;
using Spine.FileManager.DeviceLink;

namespace Spine.FileManager.Pages;

/// <summary>
/// Link Device (plan §19–21): this device shows a code for another to scan, or joins the code another device shows.
/// The code is a one-time invitation that changes every two minutes; how long the other device is trusted here is
/// chosen before it joins.
/// </summary>
public partial class LinkDevicePageViewModel(ISpineStrings strings, INavigationService navigation, DeviceService devices) : ViewModelBase, IReceivesNavigationParameter<JoinRequest>
{
    private string? _pendingJoin;
    private static readonly TrustLifetime[] Lifetimes = [TrustLifetime.QuickTransfer, TrustLifetime.Session, TrustLifetime.Remember];
    private LinkNode? _node;
    private IDispatcherTimer? _renew;

    [ObservableProperty]
    public partial string Link { get; private set; } = "";

    [ObservableProperty]
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
        Renew();
        _renew = Application.Current!.Dispatcher.CreateTimer();
        _renew.Interval = PairingInvitation.Lifetime - TimeSpan.FromSeconds(10);
        _renew.Tick += (_, _) => Renew();
        _renew.Start();
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
        if (_node is null)
            _pendingJoin = parameter.Link;
        else
            await JoinWith(parameter.Link);
    }

    partial void OnLifetimeIndexChanged(int value) => Renew();

    private void Renew()
    {
        if (_node is null)
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
