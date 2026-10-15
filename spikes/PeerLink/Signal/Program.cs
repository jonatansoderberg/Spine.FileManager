using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// The switchboard: each peer keeps a WebSocket open under its id; a message names the peer it is for and is passed on
// with the sender's id. It carries offers, answers and ICE candidates only, never the data.
var app = WebApplication.CreateBuilder(args).Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
var peers = new ConcurrentDictionary<string, WebSocket>();

app.MapGet("/", () => $"signal: {peers.Count} peers");
app.Map("/s/{id}", async (string id, HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
        return Results.BadRequest();
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    peers[id] = socket;
    app.Logger.LogInformation("{Id} joined", id);
    var buffer = new byte[64 * 1024];
    try
    {
        while (socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                    return Results.Empty;
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            var node = JsonNode.Parse(message.ToArray())!.AsObject();
            var to = node["to"]?.GetValue<string>();
            node.Remove("to");
            node["from"] = id;
            if (to is not null && peers.TryGetValue(to, out var target) && target.State == WebSocketState.Open)
            {
                app.Logger.LogInformation("{From} -> {To}: {Type}", id, to, node["type"]);
                lock (target)
                    target.SendAsync(Encoding.UTF8.GetBytes(node.ToJsonString()), WebSocketMessageType.Text, true, default).GetAwaiter().GetResult();
            }
            else
            {
                var error = new JsonObject { ["type"] = "error", ["error"] = $"{to} is not connected" };
                await socket.SendAsync(Encoding.UTF8.GetBytes(error.ToJsonString()), WebSocketMessageType.Text, true, default);
            }
        }
    }
    catch (Exception e) when (e is WebSocketException or OperationCanceledException)
    {
    }
    finally
    {
        peers.TryRemove(new KeyValuePair<string, WebSocket>(id, socket));
        app.Logger.LogInformation("{Id} left", id);
    }
    return Results.Empty;
});
app.Run();
