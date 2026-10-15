using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Text;
using KcpSharp;
using SIPSorcery.Net;

namespace PeerLinkSpike;

/// <summary>
/// The second spike: SIPSorcery's ICE agent only finds the way through the routers (host, STUN or peer-reflexive
/// candidates), and KCP, a reliable protocol over UDP, carries the data on that path instead of WebRTC's SCTP.
/// </summary>
public static class IceKcpSpike
{
    private sealed class IceTransport(RtpIceChannel ice, Func<IPEndPoint?> remote) : IKcpTransport
    {
        public ValueTask SendPacketAsync(Memory<byte> packet, CancellationToken cancellationToken)
        {
            if (remote() is { } endpoint)
                ice.Send(RTPChannelSocketsEnum.RTP, endpoint, packet.ToArray());
            return default;
        }
    }

    public sealed record Link(RtpIceChannel Ice, KcpConversation Kcp, string Route, long ConnectMs);

    private static KcpConversationOptions Options() => new()
    {
        Mtu = 1200,
        SendWindow = 1024,
        ReceiveWindow = 1024,
        RemoteReceiveWindow = 1024,
        UpdateInterval = 10,
        NoDelay = true,
        FastResend = 2,
        StreamMode = true,
        SendQueueSize = 4096,
        ReceiveQueueSize = 4096,
    };

    /// <summary>Both sides run this: one as the controller (the caller), the other answering the first peer that calls.</summary>
    public static Task<Link> ConnectAsync(SignalClient signal, string? target, Action<string> log, CancellationToken cancel = default)
    {
        var done = new TaskCompletionSource<Link>();
        var clock = Stopwatch.StartNew();
        RtpIceChannel? ice = null;
        string? peer = target;
        var gate = new object();

        RtpIceChannel Start(string with)
        {
            var channel = new RtpIceChannel(null, RTCIceComponent.rtp, PeerSpike.Configuration().iceServers, RTCIceTransportPolicy.all, true, 0, null);
            // KCP opens each packet with its conversation id, low byte first: 0x9C falls in the RTP range, where
            // SIPSorcery passes packets on; 0x00–0x03 would be taken for STUN and dropped.
            // The setter is internal; RTCPeerConnection sets it from the SDP role.
            typeof(RtpIceChannel).GetProperty(nameof(RtpIceChannel.IsController))!.SetValue(channel, target is not null);
            channel.OnIceCandidate += c => _ = signal.SendAsync(with, "ice-cand", c.toJSON());
            channel.OnIceConnectionStateChange += state =>
            {
                log($"ice {state} after {clock.ElapsedMilliseconds} ms");
                if (state != RTCIceConnectionState.connected || channel.NominatedEntry is not { } pair)
                    return;
                var remote = pair.RemoteCandidate.DestinationEndPoint;
                var kcp = new KcpConversation(new IceTransport(channel, () => remote), 0x9C, Options());
                channel.OnRTPDataReceived += (port, from, data) => kcp.InputPakcetAsync(data, default);
                done.TrySetResult(new Link(channel, kcp, $"{pair.LocalCandidate.type}:{pair.LocalCandidate.address} -> {pair.RemoteCandidate.type}:{remote}", clock.ElapsedMilliseconds));
            };
            channel.Start();
            channel.StartGathering();
            _ = signal.SendAsync(with, "ice-creds", $"{channel.LocalIceUser} {channel.LocalIcePassword}");
            return channel;
        }

        signal.Received += message =>
        {
            var from = message["from"]?.GetValue<string>();
            var type = message["type"]?.GetValue<string>();
            var payload = message["payload"]?.GetValue<string>() ?? "";
            if (type == "error")
            {
                log($"switchboard: {message["error"]}");
                return;
            }
            if (from is null || type is not ("ice-creds" or "ice-cand"))
                return;
            lock (gate)
            {
                peer ??= from;
                if (from != peer)
                    return;
                ice ??= Start(from);
            }
            if (type == "ice-creds")
            {
                var parts = payload.Split(' ');
                ice.SetRemoteCredentials(parts[0], parts[1]);
            }
            else if (RTCIceCandidateInit.TryParse(payload, out var init))
                ice.AddRemoteCandidate(new RTCIceCandidate(init));
        };
        if (target is not null)
            lock (gate)
                ice = Start(target);
        return done.Task.WaitAsync(TimeSpan.FromSeconds(30), cancel);
    }

    public static async Task<string> SendAsync(Link link, int megabytes, Action<string> log, CancellationToken cancel = default)
    {
        long total = (long)megabytes * 1048576;
        var header = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(header, total);
        await link.Kcp.SendAsync(header, cancel);
        var chunk = new byte[64 * 1024];
        Random.Shared.NextBytes(chunk);
        var clock = Stopwatch.StartNew();
        for (long sent = 0; sent < total; sent += chunk.Length)
            await link.Kcp.SendAsync(chunk, cancel);
        var reply = new byte[64];
        var result = await link.Kcp.ReceiveAsync(reply, cancel);
        var seconds = clock.Elapsed.TotalSeconds;
        var line = $"connected in {link.ConnectMs} ms; {megabytes} MB in {seconds:F1} s = {megabytes / seconds:F2} MB/s; {Encoding.ASCII.GetString(reply, 0, result.BytesReceived)}; route {link.Route}";
        log(line);
        return line;
    }

    public static async Task ReceiveAsync(Link link, Action<string> log, CancellationToken cancel = default)
    {
        var buffer = new byte[256 * 1024];
        var header = new byte[8];
        var have = 0;
        while (have < 8)
            have += (await link.Kcp.ReceiveAsync(header.AsMemory(have), cancel)).BytesReceived;
        var total = BinaryPrimitives.ReadInt64BigEndian(header);
        long received = 0;
        var clock = Stopwatch.StartNew();
        while (received < total)
            received += (await link.Kcp.ReceiveAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, total - received)), cancel)).BytesReceived;
        log($"received {received / 1048576.0:F1} MB in {clock.ElapsedMilliseconds} ms over {link.Route}");
        await link.Kcp.SendAsync(Encoding.ASCII.GetBytes($"got {received}"), cancel);
        await link.Kcp.FlushAsync(cancel);
    }
}
