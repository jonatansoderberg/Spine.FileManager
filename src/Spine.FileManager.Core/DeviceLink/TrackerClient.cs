using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace Spine.FileManager.DeviceLink;

/// <summary>
/// This node's line to the tracker (peer link plan, step 2): it signs in with the node key, tells the tracker where it
/// listens and whom it trusts, looks other nodes up and asks them to connect. The line is kept open, and opened again
/// after a cut with a growing pause, so the tracker knows the node is there.
/// </summary>
public sealed class TrackerClient(Uri url, NodeIdentity identity, Func<TrackerUpdate> state) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<TrackerFound>> _lookups = new();
    private readonly SemaphoreSlim _sending = new(1, 1);
    private ClientWebSocket? _socket;
    private int _nextId;
    private Task? _running;

    /// <summary>Signed in and listening for answers.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>Why the last attempt to reach the tracker failed, with its address; null once signed in.</summary>
    public string? LastError { get; private set; }

    /// <summary>The line opened (true) or was cut (false).</summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>A trusted node asks this one to connect to it, as it cannot reach this one itself.</summary>
    public event Action<TrackerCalled>? Called;

    public void Start() => _running ??= Task.Run(() => RunAsync(_stop.Token));

    private async Task RunAsync(CancellationToken cancel)
    {
        var pause = TimeSpan.FromSeconds(1);
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                using (var connecting = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    connecting.CancelAfter(TimeSpan.FromSeconds(10));
                    await socket.ConnectAsync(url, connecting.Token);
                }
                _socket = socket;
                if (await ReceiveAsync(socket, cancel) is not TrackerChallenge challenge)
                    throw new WebSocketException("The tracker did not ask this node to sign in.");
                var now = state();
                await SendAsync(new TrackerHello(identity.PublicKey, identity.Sign(TrackerProtocol.Signed(challenge.Nonce)), now.Endpoints, now.Trusted));
                if (await ReceiveAsync(socket, cancel) is not TrackerWelcome)
                    throw new WebSocketException("The tracker turned the sign-in down.");
                LastError = null;
                SetConnected(true);
                pause = TimeSpan.FromSeconds(1);
                while (await ReceiveAsync(socket, cancel) is { } message)
                {
                    switch (message)
                    {
                        case TrackerFound found when _lookups.TryRemove(found.Id, out var waiting):
                            waiting.TrySetResult(found);
                            break;
                        case TrackerCalled called:
                            Called?.Invoke(called);
                            break;
                    }
                }
            }
            catch (Exception e) when (e is WebSocketException or IOException or JsonException or NotSupportedException or OperationCanceledException && !cancel.IsCancellationRequested)
            {
                // Kept with the address, so a tracker that cannot be reached says where and why instead of failing quietly.
                LastError = $"{url}: {e.GetType().Name}: {e.Message}";
                System.Diagnostics.Debug.WriteLine($"[Tracker] {LastError}");
            }
            finally
            {
                _socket = null;
                SetConnected(false);
            }
            if (cancel.IsCancellationRequested)
                return;
            try
            {
                await Task.Delay(pause, cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            pause = TimeSpan.FromSeconds(Math.Min(30, pause.TotalSeconds * 2));
        }
    }

    private void SetConnected(bool connected)
    {
        if (IsConnected == connected)
            return;
        IsConnected = connected;
        ConnectionChanged?.Invoke(connected);
    }

    /// <summary>Tells the tracker the node's current endpoints and trust list; nothing happens while the line is down.</summary>
    public Task PublishAsync() => IsConnected ? SendAsync(state() with { }) : Task.CompletedTask;

    /// <summary>Where the tracker last saw <paramref name="nodeId"/>; null when the line is down or the tracker does not answer in time.</summary>
    public async Task<TrackerFound?> LookupAsync(string nodeId, CancellationToken cancel = default)
    {
        if (!IsConnected)
            return null;
        var id = Interlocked.Increment(ref _nextId);
        var waiting = new TaskCompletionSource<TrackerFound>(TaskCreationOptions.RunContinuationsAsynchronously);
        _lookups[id] = waiting;
        try
        {
            await SendAsync(new TrackerLookup(id, nodeId));
            return await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3), cancel);
        }
        catch (Exception e) when (e is TimeoutException or WebSocketException or ObjectDisposedException)
        {
            return null;
        }
        finally
        {
            _lookups.TryRemove(id, out _);
        }
    }

    /// <summary>Asks <paramref name="nodeId"/>, through the tracker, to connect to this node at <paramref name="endpoints"/>.</summary>
    public Task CallAsync(string nodeId, IReadOnlyList<string> endpoints) =>
        IsConnected ? SendAsync(new TrackerCall(nodeId, endpoints)) : Task.CompletedTask;

    private async Task SendAsync(TrackerMessage message)
    {
        if (_socket is not { State: WebSocketState.Open } socket)
            return;
        await _sending.WaitAsync();
        try
        {
            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, _stop.Token);
        }
        finally
        {
            _sending.Release();
        }
    }

    private static async Task<TrackerMessage?> ReceiveAsync(ClientWebSocket socket, CancellationToken cancel)
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
        } while (!result.EndOfMessage);
        return JsonSerializer.Deserialize<TrackerMessage>(message.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_running is not null)
            await _running.ContinueWith(_ => { }, TaskScheduler.Default);
        _stop.Dispose();
    }
}
