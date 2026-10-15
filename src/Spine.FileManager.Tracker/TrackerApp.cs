using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using Spine.FileManager.DeviceLink;

namespace Spine.FileManager.Tracker;

/// <summary>
/// The tracker (peer link plan, step 2): linked devices register where they listen and find each other through it, as
/// with a BitTorrent tracker. It keeps node ids, endpoints, last seen and hashed trust lists in memory; no names and no
/// files. A device proves its node id by signing a challenge with its node key.
/// </summary>
public static class TrackerApp
{
    public static WebApplication Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<Registry>();
        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.MapGet("/", (Registry registry) => $"Spine File Manager tracker: {registry.Online} online");
        app.Map("/link", async (HttpContext context, Registry registry) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
                return Results.BadRequest();
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await registry.ServeAsync(new Line(socket), context.RequestAborted);
            return Results.Empty;
        });
        return app;
    }
}

/// <summary>One device's WebSocket: JSON messages in and out, one send at a time.</summary>
public sealed class Line(WebSocket socket)
{
    private readonly SemaphoreSlim _sending = new(1, 1);

    public async Task SendAsync(TrackerMessage message, CancellationToken cancel = default)
    {
        await _sending.WaitAsync(cancel);
        try
        {
            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, cancel);
        }
        finally
        {
            _sending.Release();
        }
    }

    public async Task<TrackerMessage?> ReceiveAsync(CancellationToken cancel)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancel);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            message.Write(buffer, 0, result.Count);
            // Endpoints and a trust list fit in a few kilobytes; anything bigger is not a device of ours.
            if (message.Length > 64 * 1024)
                return null;
        } while (!result.EndOfMessage);
        return JsonSerializer.Deserialize<TrackerMessage>(message.ToArray());
    }
}

public sealed class Registry
{
    private sealed class Node
    {
        public IReadOnlyList<string> Endpoints = [];
        public HashSet<string> Trusted = [];
        public DateTimeOffset LastSeen;
        public Line? Line;
    }

    private readonly ConcurrentDictionary<string, Node> _nodes = new();

    public int Online => _nodes.Values.Count(n => n.Line is not null);

    public async Task ServeAsync(Line line, CancellationToken cancel)
    {
        var nonce = RandomNumberGenerator.GetBytes(32);
        await line.SendAsync(new TrackerChallenge(nonce), cancel);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        if (await line.ReceiveAsync(timeout.Token) is not TrackerHello hello
            || !NodeIdentity.Verify(hello.PublicKey, TrackerProtocol.Signed(nonce), hello.Signature))
            return;
        var id = NodeIdentity.Fingerprint(hello.PublicKey);
        var node = _nodes.GetOrAdd(id, _ => new Node());
        lock (node)
        {
            node.Endpoints = hello.Endpoints;
            node.Trusted = [.. hello.Trusted];
            node.LastSeen = DateTimeOffset.UtcNow;
            node.Line = line;
        }
        await line.SendAsync(new TrackerWelcome(id), cancel);
        try
        {
            while (await line.ReceiveAsync(cancel) is { } message)
            {
                lock (node)
                    node.LastSeen = DateTimeOffset.UtcNow;
                switch (message)
                {
                    case TrackerUpdate update:
                        lock (node)
                        {
                            node.Endpoints = update.Endpoints;
                            node.Trusted = [.. update.Trusted];
                        }
                        break;
                    case TrackerLookup lookup:
                        await line.SendAsync(Find(id, lookup), cancel);
                        break;
                    case TrackerCall call when Trusts(call.Node, id, out var target) && target.Line is { } other:
                        await other.SendAsync(new TrackerCalled(id, call.Endpoints), cancel);
                        break;
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or JsonException or OperationCanceledException)
        {
        }
        finally
        {
            lock (node)
            {
                if (node.Line == line)
                    node.Line = null;
                node.LastSeen = DateTimeOffset.UtcNow;
            }
        }
    }

    // Only a node that trusts the asker answers; to anyone else every node looks unknown.
    private TrackerFound Find(string asker, TrackerLookup lookup)
    {
        if (!Trusts(lookup.Node, asker, out var node))
            return new TrackerFound(lookup.Id, lookup.Node, [], false, null);
        lock (node)
            return new TrackerFound(lookup.Id, lookup.Node, node.Endpoints, node.Line is not null, node.LastSeen);
    }

    private bool Trusts(string nodeId, string asker, out Node node) =>
        _nodes.TryGetValue(nodeId, out node!) && node.Trusted.Contains(TrackerProtocol.HashOf(asker));
}
