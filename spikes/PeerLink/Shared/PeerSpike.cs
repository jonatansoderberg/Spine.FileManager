using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using SIPSorcery.Net;

namespace PeerLinkSpike;

/// <summary>A peer's line to the switchboard: JSON messages to and from other peers by id.</summary>
public sealed class SignalClient(string url, string id) : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _send = new(1, 1);

    public string Id => id;

    public event Action<JsonObject>? Received;

    public async Task ConnectAsync(CancellationToken cancel = default)
    {
        await _socket.ConnectAsync(new Uri($"{url.TrimEnd('/')}/s/{id}"), cancel);
        _ = ReceiveLoop();
    }

    public async Task SendAsync(string to, string type, string payload)
    {
        var message = new JsonObject { ["to"] = to, ["type"] = type, ["payload"] = payload };
        await _send.WaitAsync();
        try
        {
            await _socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, default);
        }
        finally
        {
            _send.Release();
        }
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[64 * 1024];
        while (_socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(buffer, default);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            if (JsonNode.Parse(message.ToArray()) is JsonObject node)
                Received?.Invoke(node);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, default);
        _socket.Dispose();
    }
}

/// <summary>
/// The spike: two peers find each other through the switchboard and STUN, open a WebRTC data channel and measure it.
/// </summary>
public static class PeerSpike
{
    public static RTCConfiguration Configuration() => new()
    {
        iceServers =
        [
            new RTCIceServer { urls = "stun:stun.l.google.com:19302" },
            new RTCIceServer { urls = "stun:stun.cloudflare.com:3478" },
        ],
    };

    /// <summary>Waits for offers and answers each with a channel that echoes a byte count when the sender says "done".</summary>
    public static void Answer(SignalClient signal, Action<string> log)
    {
        var sessions = new Dictionary<string, RTCPeerConnection>();
        signal.Received += async message =>
        {
            var from = message["from"]?.GetValue<string>() ?? "";
            var type = message["type"]?.GetValue<string>();
            var payload = message["payload"]?.GetValue<string>() ?? "";
            try
            {
                switch (type)
                {
                    case "offer":
                        var pc = new RTCPeerConnection(Configuration());
                        sessions[from] = pc;
                        Watch(pc, log);
                        pc.onicecandidate += c => _ = signal.SendAsync(from, "candidate", c.toJSON());
                        pc.ondatachannel += channel =>
                        {
                            log($"channel '{channel.label}' from {from}");
                            long received = 0;
                            var clock = new Stopwatch();
                            channel.onmessage += (_, _, data) =>
                            {
                                if (data.Length == 4 && Encoding.ASCII.GetString(data) == "done")
                                {
                                    log($"received {received / 1048576.0:F1} MB in {clock.ElapsedMilliseconds} ms");
                                    channel.send(Encoding.ASCII.GetBytes($"got {received}"));
                                    received = 0;
                                    clock.Reset();
                                    return;
                                }
                                if (received == 0)
                                    clock.Start();
                                received += data.Length;
                            };
                        };
                        if (!RTCSessionDescriptionInit.TryParse(payload, out var offer))
                            throw new FormatException("bad offer");
                        var set = pc.setRemoteDescription(offer);
                        if (set != SetDescriptionResultEnum.OK)
                            throw new InvalidOperationException($"remote offer: {set}");
                        var answer = pc.createAnswer();
                        await pc.setLocalDescription(answer);
                        await signal.SendAsync(from, "answer", answer.toJSON());
                        break;
                    case "candidate" when sessions.TryGetValue(from, out var known):
                        if (RTCIceCandidateInit.TryParse(payload, out var candidate))
                            known.addIceCandidate(candidate);
                        break;
                    case "error":
                        log($"switchboard: {message["error"]}");
                        break;
                }
            }
            catch (Exception e)
            {
                log($"answer failed: {e.Message}");
            }
        };
    }

    /// <summary>Calls <paramref name="target"/>, opens a channel and sends <paramref name="megabytes"/> through it.</summary>
    public static async Task<string> CallAsync(SignalClient signal, string target, int megabytes, Action<string> log, CancellationToken cancel = default)
    {
        var pc = new RTCPeerConnection(Configuration());
        Watch(pc, log);
        var clock = Stopwatch.StartNew();
        signal.Received += message =>
        {
            if (message["from"]?.GetValue<string>() != target)
            {
                if (message["type"]?.GetValue<string>() == "error")
                    log($"switchboard: {message["error"]}");
                return;
            }
            var payload = message["payload"]?.GetValue<string>() ?? "";
            switch (message["type"]?.GetValue<string>())
            {
                case "answer" when RTCSessionDescriptionInit.TryParse(payload, out var answer):
                    log($"answer after {clock.ElapsedMilliseconds} ms: {pc.setRemoteDescription(answer)}");
                    break;
                case "candidate" when RTCIceCandidateInit.TryParse(payload, out var candidate):
                    pc.addIceCandidate(candidate);
                    break;
            }
        };
        pc.onicecandidate += c => _ = signal.SendAsync(target, "candidate", c.toJSON());
        var channel = await pc.createDataChannel("files");
        var open = new TaskCompletionSource();
        channel.onopen += () => open.TrySetResult();
        var reply = new TaskCompletionSource<string>();
        channel.onmessage += (_, _, data) => reply.TrySetResult(Encoding.ASCII.GetString(data));
        var offer = pc.createOffer();
        await pc.setLocalDescription(offer);
        await signal.SendAsync(target, "offer", offer.toJSON());
        await open.Task.WaitAsync(TimeSpan.FromSeconds(30), cancel);
        var connected = clock.ElapsedMilliseconds;
        log($"channel open after {connected} ms");

        var chunk = new byte[int.TryParse(Environment.GetEnvironmentVariable("CHUNK"), out var kb) ? kb * 1024 : 16 * 1024];
        Random.Shared.NextBytes(chunk);
        long total = (long)megabytes * 1048576, sent = 0;
        var send = Stopwatch.StartNew();
        while (sent < total)
        {
            // Back-pressure: the channel buffers everything it is given, so the sender waits while a megabyte is queued.
            while (channel.bufferedAmount > (ulong)(int.TryParse(Environment.GetEnvironmentVariable("QUEUE"), out var q) ? q : 1048576))
                await Task.Delay(2, cancel);
            channel.send(chunk);
            sent += chunk.Length;
        }
        channel.send(Encoding.ASCII.GetBytes("done"));
        var got = await reply.Task.WaitAsync(TimeSpan.FromMinutes(5), cancel);
        var seconds = send.Elapsed.TotalSeconds;
        var result = $"connected in {connected} ms; {megabytes} MB in {seconds:F1} s = {megabytes / seconds:F2} MB/s; {got}; route {Route(pc)}";
        log(result);
        pc.close();
        return result;
    }

    private static void Watch(RTCPeerConnection pc, Action<string> log)
    {
        pc.onconnectionstatechange += state => log($"connection {state}");
        pc.oniceconnectionstatechange += state => log($"ice {state}");
    }

    private static string Route(RTCPeerConnection pc)
    {
        try
        {
            var pair = pc.GetRtpChannel()?.NominatedEntry;
            return pair is null ? "unknown" : $"{pair.LocalCandidate?.type}:{pair.LocalCandidate?.address} -> {pair.RemoteCandidate?.type}:{pair.RemoteCandidate?.address}";
        }
        catch (Exception e)
        {
            return $"unknown ({e.GetType().Name})";
        }
    }
}
