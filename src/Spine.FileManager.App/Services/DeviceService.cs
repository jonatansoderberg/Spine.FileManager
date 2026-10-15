using System.Net.Sockets;
using Plugin.Maui.Spine.Common;
using Spine.FileManager.DeviceLink;

namespace Spine.FileManager.Services;

/// <summary>How a linked device can be reached now. Only the local network for now; the link ladder adds more ways.</summary>
public enum LinkState { Checking, LocalNetwork, Offline }

/// <summary>A device's reachability: how, at which address, and when it last answered.</summary>
public sealed record LinkStatus(LinkState State, string? Endpoint = null, DateTimeOffset? LastSeen = null);

/// <summary>
/// This installation among Other Devices (plan §19–21): its node identity, the devices it trusts and the node that
/// listens for them. The node starts when it is first needed, so a Mac that links nothing never opens a port.
/// </summary>
public sealed class DeviceService
{
    /// <summary>Where the node listens, when the port is free; a fixed port lets a device find it again after a restart.</summary>
    public const int Port = 47821;

    private const string KeyName = "node-key";
    private readonly SemaphoreSlim _starting = new(1, 1);
    private LinkNode? _node;

    public TrustStore Trust { get; } = new(Path.Combine(AppPaths.Data, "trust.json"));

    /// <summary>A file arrived: the sending device's name and the file's path.</summary>
    public event Action<string, string>? Received;

    /// <summary>A device paired through this one's invitation.</summary>
    public event Action<TrustedNode>? Paired;

    /// <summary>A file is arriving: the sending device's node id, the file's name, bytes so far and its size.</summary>
    public event Action<string, string, long, long>? Receiving;

    /// <summary>A device's <see cref="LinkStatus"/> changed: its node id.</summary>
    public event Action<string>? StatusChanged;

    private readonly Dictionary<string, LinkStatus> _status = [];
    private CancellationTokenSource? _watching;

    public LinkStatus StatusOf(string nodeId)
    {
        lock (_status)
            return _status.GetValueOrDefault(nodeId) ?? new LinkStatus(LinkState.Checking);
    }

    private void SetStatus(string nodeId, LinkStatus status)
    {
        lock (_status)
        {
            if (_status.GetValueOrDefault(nodeId) is { } old && old.State == status.State && old.Endpoint == status.Endpoint)
            {
                _status[nodeId] = status;
                return;
            }
            _status[nodeId] = status;
        }
        StatusChanged?.Invoke(nodeId);
    }

    /// <summary>A device answered: a transfer to or from it got through.</summary>
    public void Seen(string nodeId, string? endpoint = null) =>
        SetStatus(nodeId, new LinkStatus(LinkState.LocalNetwork, endpoint ?? StatusOf(nodeId).Endpoint, DateTimeOffset.Now));

    /// <summary>
    /// Checks every linked device now and then every <paramref name="every"/> while the app runs: a connection to its
    /// last address, opened and closed. A device that answered keeps its state until a check fails.
    /// </summary>
    public void WatchStatus(TimeSpan every)
    {
        if (_watching is not null || Trust.All.Count == 0)
            return;
        var cancel = _watching = new CancellationTokenSource();
        Trust.Changed += () => _ = CheckAllAsync(cancel.Token);
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(every);
            do
                await CheckAllAsync(cancel.Token);
            while (await timer.WaitForNextTickAsync(cancel.Token));
        });
    }

    public async Task CheckAllAsync(CancellationToken cancel = default)
    {
        var node = await GetNodeAsync();
        await Task.WhenAll(Trust.All.Select(async device =>
        {
            var endpoint = await node.ProbeAsync(device.NodeId, cancel);
            SetStatus(device.NodeId, endpoint is null
                ? new LinkStatus(LinkState.Offline, null, StatusOf(device.NodeId).LastSeen)
                : new LinkStatus(LinkState.LocalNetwork, endpoint, DateTimeOffset.Now));
        }));
    }

    /// <summary>Where received files go: Downloads on a computer, as with AirDrop; on a phone the app's documents, which the Files app shows.</summary>
    public static string Inbox => DeviceInfo.Idiom == DeviceIdiom.Desktop
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Received");

    /// <summary>The name other devices see: the computer's name on a Mac, which Mac Catalyst reports as "iPad".</summary>
    public static string DeviceName =>
#if MACCATALYST
        ObjCRuntime.Runtime.GetNSObject<Foundation.NSString>(ComputerName(IntPtr.Zero, IntPtr.Zero), owns: true)?.ToString() ?? DeviceInfo.Name;

    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/SystemConfiguration.framework/SystemConfiguration", EntryPoint = "SCDynamicStoreCopyComputerName")]
    private static extern IntPtr ComputerName(IntPtr store, IntPtr encoding);
#else
        DeviceInfo.Name;
#endif

    // Every link is the user's choice: a device that scanned this one's code, or a computer a phone introduced, waits
    // for a yes here before either side keeps it.
    private static async Task<bool> AskToLink(PairedNode joined)
    {
        var strings = IPlatformApplication.Current!.Services.GetRequiredService<ISpineStrings>();
        if (Application.Current?.Windows.FirstOrDefault()?.Page is not { } page)
            return false;
        return await page.DisplayAlertAsync(strings.Get("Link.Confirm.Title", joined.Name), strings.Get("Link.Confirm.Message", joined.Name),
            strings["Link.Confirm.Accept"], strings["Link.Confirm.Decline"]);
    }

    public async Task<LinkNode> GetNodeAsync()
    {
        if (_node is not null)
            return _node;
        await _starting.WaitAsync();
        try
        {
            if (_node is null)
            {
                var node = new LinkNode(await LoadIdentityAsync(), Trust, DeviceName, Inbox);
                try
                {
                    node.Start(Port);
                }
                catch (SocketException)
                {
                    node.Start();
                }
                node.Received += (from, path) =>
                {
                    Seen(from);
                    Received?.Invoke(Trust.Find(from)?.DisplayName ?? from, path);
                };
                node.Receiving += (from, name, received, size) => Receiving?.Invoke(from, name, received, size);
                node.Paired += trusted => Paired?.Invoke(trusted);
                node.Confirm = joined => MainThread.InvokeOnMainThreadAsync(() => AskToLink(joined));
                _node = node;
            }
            return _node;
        }
        finally
        {
            _starting.Release();
        }
    }

    /// <summary>Starts listening at launch when some device is already linked, so it can send files here.</summary>
    public void StartIfLinked()
    {
        if (Trust.All.Count == 0)
            return;
        _ = GetNodeAsync();
        WatchStatus(TimeSpan.FromSeconds(30));
    }

    public async Task SendAsync(string nodeId, IReadOnlyList<string> paths, Action<long, long> progress)
    {
        try
        {
            await (await GetNodeAsync()).SendAsync(nodeId, paths, progress);
            Seen(nodeId);
        }
        catch (Exception e) when (e is IOException or SocketException)
        {
            SetStatus(nodeId, new LinkStatus(LinkState.Offline, null, StatusOf(nodeId).LastSeen));
            throw;
        }
    }

    // The private key goes to the platform's secure storage (Keychain, Keystore, DPAPI). Where that is not available
    // to the app (an unsigned Mac build has no keychain access group), a file only the user can read keeps it.
    private static async Task<NodeIdentity> LoadIdentityAsync()
    {
        var file = Path.Combine(AppPaths.Data, "node.key");
        try
        {
            if (await SecureStorage.Default.GetAsync(KeyName) is { } stored)
                return NodeIdentity.Import(Convert.FromBase64String(stored));
            var created = NodeIdentity.Create();
            await SecureStorage.Default.SetAsync(KeyName, Convert.ToBase64String(created.ExportPrivateKey()));
            return created;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (File.Exists(file))
                return NodeIdentity.Import(Convert.FromBase64String(await File.ReadAllTextAsync(file)));
            var created = NodeIdentity.Create();
            Directory.CreateDirectory(AppPaths.Data);
            await File.WriteAllTextAsync(file, Convert.ToBase64String(created.ExportPrivateKey()));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return created;
        }
    }
}
