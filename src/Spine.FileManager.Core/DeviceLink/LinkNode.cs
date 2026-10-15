using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Spine.FileManager.Storage;
using Spine.FileManager.Transfer;

namespace Spine.FileManager.DeviceLink;

/// <summary>Where a node listens, sent by each side once a pairing is complete.</summary>
public sealed record LinkEndpoints(IReadOnlyList<string> Endpoints);

/// <summary>What a transfer brings, sent before any file so the receiver can say yes or no: names at the top level, how many files and bytes.</summary>
public sealed record TransferRequest(IReadOnlyList<string> Names, int Files, long Bytes);

/// <summary>The receiver's answer to a <see cref="TransferRequest"/>.</summary>
public sealed record TransferAnswer(bool Accepted);

/// <summary>A transfer coming in, for the receiver to accept: who sends it and what it brings.</summary>
public sealed record IncomingTransfer(string NodeId, TransferRequest Request);

/// <summary>The receiver said no, or did not answer in time.</summary>
public sealed class TransferDeclinedException(bool timedOut) : IOException(timedOut ? "The other device did not answer." : "The other device declined.")
{
    public bool TimedOut { get; } = timedOut;
}

/// <summary>A third device's hand-over: another computer's invitation, with proof it scanned this one's code.</summary>
public sealed record Introduction(string SessionId, string Invitation, byte[] Mac);

public sealed record IntroductionResult(bool Linked, string? Error);

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
    private const byte Introduce = (byte)'I';
    private const byte Update = (byte)'U';
    private const byte Callback = (byte)'R';

    private readonly ConcurrentDictionary<string, TaskCompletionSource<TcpClient>> _callbacks = new();

    /// <summary>How long the user has to answer a request to link before it counts as turned down.</summary>
    public static readonly TimeSpan ConfirmTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Asked once a device has proved it knows this node's pairing code, before it is trusted: true links it. Unset,
    /// every proven device is linked.
    /// </summary>
    public Func<PairedNode, Task<bool>>? Confirm { get; set; }

    /// <summary>
    /// Asked before files from a trusted node come in: true lets them. Unset, every transfer is let in. No answer within
    /// <see cref="ConfirmTimeout"/> is a no.
    /// </summary>
    public Func<IncomingTransfer, Task<bool>>? AcceptTransfer { get; set; }

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private TcpListener? _listener;
    private (Pairing.Host Host, PairingInvitation Invitation, TrustLifetime Lifetime)? _offer;

    public NodeIdentity Identity => identity;

    public TrustStore Trust => trust;

    public string Name => name;

    private IPAddress? _address;
    private int _port;

    /// <summary>
    /// Where this node listens: host:port for each of the machine's addresses as they are now. Read each time, since a
    /// laptop that changes networks gets new addresses while the app keeps running.
    /// </summary>
    public IReadOnlyList<string> Endpoints => _listener is null ? []
        : [.. _address is not null && !_address.Equals(IPAddress.Any) ? [$"{_address}:{_port}"] : LocalAddresses().Select(a => $"{a}:{_port}"),
            .. PublicEndpoint is { } mapped ? [mapped.ToString()] : Array.Empty<string>()];

    /// <summary>The port the router forwards here, when it does: the address a device outside the local network uses.</summary>
    public IPEndPoint? PublicEndpoint { get; set; }

    /// <summary>The port this node listens on.</summary>
    public int Port => _port;

    /// <summary>A trusted node reached this one and proved who it is: its node id. It is there, even when this node cannot reach it back.</summary>
    public event Action<string>? Contacted;

    /// <summary>
    /// Swaps current endpoints with a trusted node over the authenticated channel, so each keeps the other's latest
    /// addresses, its router's public one included, without pairing again. The address that answered, or null.
    /// </summary>
    public async Task<string?> ExchangeEndpointsAsync(string nodeId, CancellationToken cancel = default)
    {
        if (trust.Find(nodeId) is not { } node)
            return null;
        try
        {
            using var client = await ConnectAsync(node.Endpoints, cancel);
            var answered = client.Client.RemoteEndPoint?.ToString();
            var connection = client.GetStream();
            connection.WriteByte(Update);
            await using var channel = await SecureChannel.ConnectAsync(connection, identity, nodeId, cancel);
            await FileTransfer.WriteAsync(channel, new LinkEndpoints(Endpoints), cancel);
            var theirs = await FileTransfer.ReadAsync<LinkEndpoints>(channel, cancel);
            trust.SetEndpoints(nodeId, theirs.Endpoints);
            return answered;
        }
        catch (Exception e) when (e is IOException or SocketException or ChannelException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task HostUpdate(NetworkStream stream)
    {
        var (channel, peer) = await SecureChannel.AcceptAsync(stream, identity, id => trust.Find(id) is not null);
        await using (channel)
        {
            var theirs = await FileTransfer.ReadAsync<LinkEndpoints>(channel);
            await FileTransfer.WriteAsync(channel, new LinkEndpoints(Endpoints));
            trust.SetEndpoints(peer, theirs.Endpoints);
        }
        Contacted?.Invoke(peer);
    }

    /// <summary>A device paired through this node's invitation.</summary>
    public event Action<TrustedNode>? Paired;

    /// <summary>A file arrived from a trusted node: its node id and where the file now is.</summary>
    public event Action<string, string>? Received;

    /// <summary>A file is arriving from a trusted node: its node id, the file's name, bytes so far and its size.</summary>
    public event Action<string, string, long, long>? Receiving;

    /// <summary>
    /// Whether a trusted node answers at one of its addresses now: the address that answered, or null. Only a connection
    /// is opened and closed; the node takes it for nothing.
    /// </summary>
    public async Task<string?> ProbeAsync(string nodeId, CancellationToken cancel = default)
    {
        if (trust.Find(nodeId) is not { Endpoints.Count: > 0 } node)
            return null;
        try
        {
            using var client = await ConnectAsync(node.Endpoints, cancel);
            return client.Client.RemoteEndPoint?.ToString();
        }
        catch (Exception e) when (e is IOException or SocketException)
        {
            return null;
        }
    }

    /// <summary>Listens on <paramref name="port"/> (0: any free one), on every address or only on <paramref name="address"/>.</summary>
    public void Start(int port = 0, IPAddress? address = null)
    {
        _listener = new TcpListener(address ?? IPAddress.Any, port);
        _listener.Start();
        _address = address;
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
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
        // The computer's user answers before it sends where it listens; no answer is a no.
        var endpoints = await FileTransfer.ReadAsync<LinkEndpoints?>(stream, cancel)
            ?? throw new PairingException("The other device turned the link down.");
        trust.Trust(host, lifetime, _time.GetUtcNow());
        trust.SetEndpoints(host.NodeId, endpoints.Endpoints);
        return trust.Find(host.NodeId)!;
    }

    /// <summary>
    /// Sends files and folders to a trusted node, folders with everything in them; <paramref name="progress"/> gets bytes
    /// sent of the total. A node trusted for one transfer is forgotten after it.
    /// </summary>
    public async Task SendAsync(string nodeId, IReadOnlyList<string> paths, Action<long, long>? progress = null, CancellationToken cancel = default, TcpClient? over = null)
    {
        var node = trust.Find(nodeId) ?? throw new IOException("This device is not linked any more.");
        var files = paths.SelectMany(Expand).ToList();
        var total = files.Sum(f => new FileInfo(f.Path).Length);
        long done = 0;
        // Over a connection the other device opened (the return path), or one this node opens.
        using (var client = over ?? await ConnectAsync(node.Endpoints, cancel))
        {
            var connection = client.GetStream();
            if (over is null)
                connection.WriteByte(Transfer);
            await using var channel = await SecureChannel.ConnectAsync(connection, identity, nodeId, cancel);
            var names = paths.Select(p => Path.GetFileName(Path.TrimEndingDirectorySeparator(p))).ToList();
            await FileTransfer.WriteAsync(channel, new TransferRequest(names, files.Count, total), cancel);
            // The receiver's user may be asked first; a null answer is the receiver giving up waiting.
            var answer = await FileTransfer.ReadAsync<TransferAnswer?>(channel, cancel);
            if (answer is not { Accepted: true })
                throw new TransferDeclinedException(timedOut: answer is null);
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
                var handedOver = false;
                try
                {
                    try
                    {
                        var stream = client.GetStream();
                        switch (stream.ReadByte())
                        {
                            case Callback:
                                handedOver = await TakeCallback(client, stream);
                                break;
                            case Pair:
                                await HostPairing(stream);
                                break;
                            case Transfer:
                                await ReceiveFiles(stream);
                                break;
                            case Introduce:
                                await HostIntroduction(stream);
                                break;
                            case Update:
                                await HostUpdate(stream);
                                break;
                        }
                    }
                    catch (Exception e) when (e is IOException or PairingException or ChannelException or System.Text.Json.JsonException)
                    {
                        // The other device gets the reason from its own side; this one keeps listening.
                    }
                }
                finally
                {
                    if (!handedOver)
                        client.Dispose();
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
        if (!await Confirmed(joined))
        {
            await FileTransfer.WriteAsync<LinkEndpoints?>(stream, null);
            return;
        }
        await FileTransfer.WriteAsync(stream, new LinkEndpoints(Endpoints));
        trust.Trust(joined, offer.Lifetime, _time.GetUtcNow());
        trust.SetEndpoints(joined.NodeId, endpoints.Endpoints);
        Paired?.Invoke(trust.Find(joined.NodeId)!);
    }

    // True or false as the user answered, null when nobody answered in time.
    private async Task<bool?> Accepted(IncomingTransfer transfer)
    {
        if (AcceptTransfer is not { } ask)
            return true;
        try
        {
            return await ask(transfer).WaitAsync(ConfirmTimeout);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private async Task<bool> Confirmed(PairedNode joined)
    {
        if (Confirm is not { } ask)
            return true;
        try
        {
            return await ask(joined).WaitAsync(ConfirmTimeout);
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// For a device between two others (a phone that scanned both computers' codes): hands <paramref name="introduced"/>
    /// to the device that showed <paramref name="to"/>, which then joins it as if it had scanned it. The proof that the
    /// introducer scanned <paramref name="to"/> is an HMAC with its secret; the introducer is linked to neither.
    /// </summary>
    public static async Task IntroduceAsync(PairingInvitation to, PairingInvitation introduced, CancellationToken cancel = default)
    {
        var link = introduced.ToUri();
        using var client = await ConnectAsync(to.Endpoints, cancel);
        var stream = client.GetStream();
        stream.WriteByte(Introduce);
        await FileTransfer.WriteAsync(stream, new Introduction(to.SessionId, link, Pairing.IntroductionMac(to.Secret, link)), cancel);
        var result = await FileTransfer.ReadAsync<IntroductionResult>(stream, cancel);
        if (!result.Linked)
            throw new PairingException(result.Error ?? "The computers did not link.");
    }

    // The introduced code stands for this node's own: it is used up, and its lifetime is what the other computer gets.
    private async Task HostIntroduction(NetworkStream stream)
    {
        var introduction = await FileTransfer.ReadAsync<Introduction>(stream);
        if (_offer is not { } offer || offer.Invitation.SessionId != introduction.SessionId || offer.Invitation.IsExpired(_time.GetUtcNow())
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(introduction.Mac, Pairing.IntroductionMac(offer.Invitation.Secret, introduction.Invitation)))
        {
            await FileTransfer.WriteAsync(stream, new IntroductionResult(false, "This computer's code was not the one scanned, or it has expired."));
            return;
        }
        _offer = null;
        try
        {
            var host = await JoinAsync(PairingInvitation.Parse(introduction.Invitation), offer.Lifetime);
            await FileTransfer.WriteAsync(stream, new IntroductionResult(true, null));
            Paired?.Invoke(host);
        }
        catch (Exception e) when (e is PairingException or IOException or SocketException or FormatException)
        {
            await FileTransfer.WriteAsync(stream, new IntroductionResult(false, e.Message));
        }
    }

    /// <summary>
    /// Waits for <paramref name="nodeId"/> to connect back after a call through the tracker (the return path: a device
    /// this one cannot reach, which can reach this one). The connection it opens, for <see cref="SendAsync"/>.
    /// </summary>
    public async Task<TcpClient?> WaitForCallbackAsync(string nodeId, TimeSpan timeout, CancellationToken cancel = default)
    {
        var waiting = new TaskCompletionSource<TcpClient>(TaskCreationOptions.RunContinuationsAsynchronously);
        _callbacks[nodeId] = waiting;
        try
        {
            return await waiting.Task.WaitAsync(timeout, cancel);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            _callbacks.TryRemove(new KeyValuePair<string, TaskCompletionSource<TcpClient>>(nodeId, waiting));
        }
    }

    // The node says who it is in the clear; the TLS that follows proves it, as the sender pins that node id.
    private async Task<bool> TakeCallback(TcpClient client, NetworkStream stream)
    {
        var nodeId = await FileTransfer.ReadAsync<string>(stream);
        return _callbacks.TryGetValue(nodeId, out var waiting) && waiting.TrySetResult(client);
    }

    /// <summary>
    /// The other half of the return path: <paramref name="nodeId"/> asked through the tracker to be connected to, as
    /// it has files and cannot reach this node. This node connects to it and receives as if it had been reached.
    /// </summary>
    public async Task ConnectBackAsync(string nodeId, IReadOnlyList<string> endpoints, CancellationToken cancel = default)
    {
        if (trust.Find(nodeId) is not { } node)
            return;
        using var client = await ConnectAsync([.. endpoints, .. node.Endpoints], cancel);
        var stream = client.GetStream();
        stream.WriteByte(Callback);
        await FileTransfer.WriteAsync(stream, identity.NodeId, cancel);
        await ReceiveFiles(stream);
    }

    private async Task ReceiveFiles(NetworkStream stream)
    {
        var (channel, peer) = await SecureChannel.AcceptAsync(stream, identity, id => trust.Find(id) is not null);
        await using (channel)
        {
            var request = await FileTransfer.ReadAsync<TransferRequest>(channel);
            var accepted = await Accepted(new IncomingTransfer(peer, request));
            await FileTransfer.WriteAsync<TransferAnswer?>(channel, accepted is null ? null : new TransferAnswer(accepted.Value));
            if (accepted is not true)
                return;
            // Each top-level name gets a free name in the inbox once; its folder's files follow it there.
            var tops = new Dictionary<string, string>();
            FileOffer? current = null;
            while (await FileTransfer.ReceiveAsync(channel, offer => Target((current = offer).Name, tops),
                received => Receiving?.Invoke(peer, Path.GetFileName(current!.Name), received, current.Size)) is { } path)
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

    /// <summary>Thrown when a device answers at none of its addresses: it is off, asleep, or the app is not open there.</summary>
    public sealed class UnreachableException(string message) : IOException(message);

    // Every address at once, the first to answer wins: a phone's addresses include some that never answer (another
    // network's), and trying them one by one cost seconds each before the one that does.
    private static async Task<TcpClient> ConnectAsync(IReadOnlyList<string> endpoints, CancellationToken cancel)
    {
        var addresses = endpoints.Select(e => IPEndPoint.TryParse(e, out var a) ? a : null).OfType<IPEndPoint>().Distinct().ToList();
        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        race.CancelAfter(TimeSpan.FromSeconds(3));
        var attempts = addresses.Select(async address =>
        {
            var client = new TcpClient { NoDelay = true };
            try
            {
                await client.ConnectAsync(address, race.Token);
                return client;
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException)
            {
                client.Dispose();
                return null;
            }
        }).ToList();
        TcpClient? winner = null;
        while (attempts.Count > 0)
        {
            var done = await Task.WhenAny(attempts);
            attempts.Remove(done);
            if (await done is { } client)
            {
                winner = client;
                break;
            }
        }
        race.Cancel();
        // The ones still connecting give up now; any that connected meanwhile are closed.
        foreach (var late in attempts)
            _ = late.ContinueWith(t => t.Result?.Dispose(), TaskScheduler.Default);
        cancel.ThrowIfCancellationRequested();
        return winner ?? throw new UnreachableException("The other device could not be reached at any of its addresses.");
    }

    // The machine's own network: virtual bridges (the iOS Simulator's, virtual machines'), VPN tunnels, AirDrop's link
    // and a phone's mobile data (pdp_ip, and 192.0.0.x, the address iOS gives IPv4 over an IPv6-only operator) are left
    // out: nobody on the local network reaches them, and the invitation's QR code stays small.
    private static readonly string[] Virtual = ["bridge", "vmnet", "utun", "awdl", "llw", "anpi", "ap", "vEthernet", "docker", "pdp_ip", "ipsec"];

    private static IEnumerable<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && !Virtual.Any(prefix => n.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && a.GetAddressBytes() is not [192, 0, 0, _])
            .Distinct();

    public void Dispose() => _listener?.Stop();
}
