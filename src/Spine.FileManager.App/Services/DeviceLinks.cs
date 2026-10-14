using Spine.FileManager.DeviceLink;

namespace Spine.FileManager.Services;

/// <summary>A pairing link opened from outside the app, by the system or another app: Link Device opens and joins it.</summary>
public sealed record JoinRequest(string Link);

public static class DeviceLinks
{
    public static bool Open(string? url)
    {
        if (url is null || !url.StartsWith($"{PairingInvitation.Scheme}://", StringComparison.OrdinalIgnoreCase))
            return false;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (IPlatformApplication.Current?.Services.GetService<INavigationService>() is { } navigation)
                await navigation.ShowAsync<Pages.LinkDevicePage, JoinRequest>(new JoinRequest(url));
        });
        return true;
    }
}
