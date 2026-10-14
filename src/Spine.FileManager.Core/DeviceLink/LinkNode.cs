using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Spine.FileManager.Storage;
using Spine.FileManager.Transfer;

namespace Spine.FileManager.DeviceLink;

/// <summary>Where a node listens, sent by each side once a pairing is complete.</summary>
public sealed record LinkEndpoints(IReadOnlyList<string> Endpoints);

/// <summary>
/// This installation on the network (plan §19–23): it listens for devices that pair with it through its invitation and
/// for files from devices it trusts, joins another device's invitation, and sends files to a trusted device. A
/// connection's first byte says what it is for: a pairing (the exchange is authenticated by the invitation's secret)
/// or a transfer (mutual TLS on the node keys, the peer one this node trusts).
/// </summary>
public sealed class LinkNode(NodeIdentity identity, TrustStore trust, string name, string inbox, TimeProvider? time = null) : IDisposable
{
    private const byte Pair = (byte)'P';
    private const byte Transfer = (byte)'T';

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private TcpListener? _listener;
    private (Pairing.Host Host, PairingInvitation Invitation, TrustLifetime Lifetime)? _offer;

    public NodeIdentity Identity => identity;

    public TrustStore Trust => trust;

    public string Name => name;

    /// <summary>Where this node listens: host:port for each of the machine's addresses.</summary>
    public IReadOnlyList<string> Endpoints { get; private set; } = [];

    /// <summary>A device paired through this node's invitation.</summary>
    public event Action<TrustedNode>? Paired;

    /// <summary>A file arrived from a trusted node: its node id and where the file now is.</summary>
    public event Action<string, string>? Received;

    /// <summary>Listens on <paramref name="port"/> (0: any free one), on every address or only on <paramref name="address"/>.</summary>
    public void Start(int port = 0, IPAddress? address = null)
    {
        _listener = new TcpListener(address ?? IPAddress.Any, port);
        _listener.Start();
        var bound = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Endpoints = address is not null && !address.Equals(IPAddress.Any)
            ? [$"{address}:{bound}"]
            : [.. LocalAddresses().Select(a => $"{a}:{bound}")];
        _ = AcceptLoop(_listener);
    }

    /// <summary>A new invitation for a device to pair with this one; the device gets <paramref name="lifetime"/> here.</summary>
    public PairingInvitation Invite(TrustLifetime lifetime)
    {
        var invitation = PairingInvitation.Create(identity, name, Endpoints, _time.GetUtcNow());
        _offer = (new Pairing.Host(identity, invitation, name, _time), invitation, lifetime);
        return invitation;
    }

    public void CancelInvitation() => _offer = null;

    /// <summary>Pairs with the device that showed <paramref name="invitation"/>; it gets <paramref name="lifetime"/> here.</summary>
    public async Task<TrustedNode> JoinAsync(PairingInvitation invitation, TrustLifetime lifetime, CancellationToken cancel = default)
    {
        var joiner = new Pairing.Joiner(identity, invitation, name, _time);
        using var client = await ConnectAsync(invitation.Endpoints, cancel);
        var stream = client.GetStream();
        stream.WriteByte(Pair);
        await FileTransfer.WriteAsync(stream, joiner.Hello(), cancel);
        var welcome = await FileTransfer.ReadAsync<PairingWelcome?>(stream, cancel) ?? throw new PairingException("The computer turned the pairing down.");
        var (confirm, host) = joiner.Accept(welcome);
        await FileTransfer.WriteAsync(stream, confirm, cancel);
        await FileTransfer.WriteAsync(stream, new LinkEndpoints(Endpoints), cancel);
        var endpoints = await FileTransfer.ReadAsync<LinkEndpoints>(stream, cancel);
        trust.Trust(host, lifetime, _time.GetUtcNow());
        trust.SetEndpoints(host.NodeId, endpoints.Endpoints);
        return trust.Find(host.NodeId)!;
    }

    /// <summary>
    /// Sends files and folders to a trusted node, folders with everything in them; <paramref name="progress"/> gets bytes
    /// sent of the total. A node trusted for one transfer is forgotten after it.
    /// </summary>
    public async Task SendAsync(string nodeId, IReadOnlyList<string> paths, Action<long, long>? progress = null, CancellationToken cancel = default)
    {
        var node = trust.Find(nodeId) ?? throw new IOException("This device is not linked any more.");
        var files = paths.SelectMany(Expand).ToList();
        var total = files.Sum(f => new FileInfo(f.Path).Length);
        long done = 0;
        using (var client = await ConnectAsync(node.Endpoints, cancel))
        {
            var connection = client.GetStream();
            connection.WriteByte(Transfer);
            await using var channel = await SecureChannel.ConnectAsync(connection, identity, nodeId, cancel);
            foreach (var (path, relative) in files)
            {
                var offer = new FileOffer(Guid.NewGuid().ToString("N"), relative, new FileInfo(path).Length, await FileTransfer.HashAsync(path, cancel));
                var start = done;
                await FileTransfer.SendAsync(channel, path, offer, sent => progress?.Invoke(start + sent, total), cancel);
                done += offer.Size;
            }
            await FileTransfer.EndAsync(channel, cancel);
        }
        if (node.Lifetime == TrustLifetime.QuickTransfer)
            trust.Revoke(nodeId);
    }

    // A file goes as its name; a folder as every file in it, named by its path from the folder's parent.
    private static IEnumerable<(string Path, string Relative)> Expand(string path)
    {
        if (File.Exists(path))
            return [(path, Path.GetFileName(path))];
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))!;
        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Select(f => (f, Path.GetRelativePath(parent, f).Replace(Path.DirectorySeparatorChar, '/')));
    }

    private async Task AcceptLoop(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync();
            }
            catch (Exception e) when (e is ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = Task.Run(async () =>
            {
                using (client)
                {
                    try
                    {
                        var stream = client.GetStream();
                        switch (stream.ReadByte())
                        {
                            case Pair:
                                await HostPairing(stream);
                                break;
                            case Transfer:
                                await ReceiveFiles(stream);
                                break;
                        }
                    }
                    catch (Exception e) when (e is IOException or PairingException or ChannelException or System.Text.Json.JsonException)
                    {
                        // The other device gets the reason from its own side; this one keeps listening.
                    }
                }
            });
        }
    }

    private async Task HostPairing(NetworkStream stream)
    {
        if (_offer is not { } offer)
        {
            await FileTransfer.WriteAsync<PairingWelcome?>(stream, null);
            return;
        }
        var hello = await FileTransfer.ReadAsync<PairingHello>(stream);
        await FileTransfer.WriteAsync<PairingWelcome?>(stream, offer.Host.Answer(hello));
        var joined = offer.Host.Complete(await FileTransfer.ReadAsync<PairingConfirm>(stream));
        _offer = null;
        var endpoints = await FileTransfer.ReadAsync<LinkEndpoints>(stream);
        await FileTransfer.WriteAsync(stream, new LinkEndpoints(Endpoints));
        trust.Trust(joined, offer.Lifetime, _time.GetUtcNow());
        trust.SetEndpoints(joined.NodeId, endpoints.Endpoints);
        Paired?.Invoke(trust.Find(joined.NodeId)!);
    }

    private async Task ReceiveFiles(NetworkStream stream)
    {
        var (channel, peer) = await SecureChannel.AcceptAsync(stream, identity, id => trust.Find(id) is not null);
        await using (channel)
        {
            // Each top-level name gets a free name in the inbox once; its folder's files follow it there.
            var tops = new Dictionary<string, string>();
            while (await FileTransfer.ReceiveAsync(channel, offer => Target(offer.Name, tops)) is { } path)
                Received?.Invoke(peer, path);
        }
        if (trust.Find(peer) is { Lifetime: TrustLifetime.QuickTransfer })
            trust.Revoke(peer);
    }

    private string Target(string relative, Dictionary<string, string> tops)
    {
        var parts = relative.Split('/');
        if (parts.Any(p => !StorageNames.IsValid(p) || p is "." or ".."))
            throw new IOException($"The other device sent a name that cannot be used here: {relative}");
        if (!tops.TryGetValue(parts[0], out var top))
        {
            Directory.CreateDirectory(inbox);
            tops[parts[0]] = top = StorageNames.Unique(parts[0], n => File.Exists(Path.Combine(inbox, n)) || Directory.Exists(Path.Combine(inbox, n)));
        }
        return Path.Combine([inbox, top, .. parts[1..]]);
    }

    private static async Task<TcpClient> ConnectAsync(IReadOnlyList<string> endpoints, CancellationToken cancel)
    {
        foreach (var endpoint in endpoints)
        {
            if (!IPEndPoint.TryParse(endpoint, out var address))
                continue;
            var client = new TcpClient { NoDelay = true };
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                await client.ConnectAsync(address, timeout.Token);
                return client;
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException && !cancel.IsCancellationRequested)
            {
                client.Dispose();
            }
        }
        throw new IOException("The other device could not be reached at any of its addresses.");
    }

    // The machine's own network: virtual bridges (the iOS Simulator's, virtual machines'), VPN tunnels and AirDrop's
    // link are left out, so the invitation's QR code stays small.
    private static readonly string[] Virtual = ["bridge", "vmnet", "utun", "awdl", "llw", "anpi", "ap", "vEthernet", "docker"];

    private static IEnumerable<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && !Virtual.Any(prefix => n.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));

    public void Dispose() => _listener?.Stop();
}
