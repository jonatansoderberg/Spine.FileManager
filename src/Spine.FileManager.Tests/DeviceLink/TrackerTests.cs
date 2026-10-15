using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Spine.FileManager.DeviceLink;
using Spine.FileManager.Tracker;

namespace Spine.FileManager.Tests.DeviceLink;

public sealed class TrackerTests : IAsyncLifetime
{
    private WebApplication _tracker = null!;
    private Uri _url = null!;
    private readonly List<IAsyncDisposable> _clients = [];
    private readonly List<NodeIdentity> _identities = [];

    public async Task InitializeAsync()
    {
        _tracker = TrackerApp.Create(["--urls", "http://127.0.0.1:0"]);
        await _tracker.StartAsync();
        var address = _tracker.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _url = new Uri(address.Replace("http://", "ws://") + "/link");
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
            await client.DisposeAsync();
        foreach (var identity in _identities)
            identity.Dispose();
        await _tracker.StopAsync();
        await _tracker.DisposeAsync();
    }

    private async Task<(TrackerClient Client, NodeIdentity Identity)> Device(IReadOnlyList<string> endpoints, params NodeIdentity[] trusts)
    {
        var identity = NodeIdentity.Create();
        _identities.Add(identity);
        var client = new TrackerClient(_url, identity, () => new TrackerUpdate(endpoints, [.. trusts.Select(t => TrackerProtocol.HashOf(t.NodeId))]));
        _clients.Add(client);
        var connected = new TaskCompletionSource();
        client.ConnectionChanged += up => { if (up) connected.TrySetResult(); };
        client.Start();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        return (client, identity);
    }

    [Fact]
    public async Task A_device_finds_a_device_that_trusts_it_and_a_stranger_finds_nothing()
    {
        var (phone, phoneId) = await Device(["10.0.0.5:47821"]);
        var (computer, computerId) = await Device(["192.168.1.20:47821", "203.0.113.7:47821"], phoneId);
        var (stranger, _) = await Device([]);

        var found = await phone.LookupAsync(computerId.NodeId);
        var hidden = await stranger.LookupAsync(computerId.NodeId);

        Assert.Equal(["192.168.1.20:47821", "203.0.113.7:47821"], found!.Endpoints);
        Assert.True(found.Online);
        Assert.Empty(hidden!.Endpoints);
        Assert.False(hidden.Online);
        // The phone trusts nobody here, so the computer cannot find it either.
        Assert.Empty((await computer.LookupAsync(phoneId.NodeId))!.Endpoints);
    }

    [Fact]
    public async Task A_call_reaches_a_device_that_trusts_the_caller_and_no_other()
    {
        var (computer, computerId) = await Device(["192.168.1.20:47821"]);
        var (stranger, _) = await Device([]);
        var (phone, phoneId) = await Device(["10.0.0.5:47821"], computerId);
        var calls = new List<TrackerCalled>();
        var called = new TaskCompletionSource();
        phone.Called += c => { lock (calls) calls.Add(c); called.TrySetResult(); };

        await stranger.CallAsync(phoneId.NodeId, ["198.51.100.9:47821"]);
        await computer.CallAsync(phoneId.NodeId, ["203.0.113.7:47821"]);
        await called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        var call = Assert.Single(calls);
        Assert.Equal(computerId.NodeId, call.From);
        Assert.Equal(["203.0.113.7:47821"], call.Endpoints);
    }

    [Fact]
    public async Task Endpoints_a_device_publishes_later_are_what_the_others_find()
    {
        var endpoints = new List<string> { "192.168.1.20:47821" };
        var (phone, phoneId) = await Device([]);
        var identity = NodeIdentity.Create();
        _identities.Add(identity);
        var computer = new TrackerClient(_url, identity, () => new TrackerUpdate([.. endpoints], [TrackerProtocol.HashOf(phoneId.NodeId)]));
        _clients.Add(computer);
        var connected = new TaskCompletionSource();
        computer.ConnectionChanged += up => { if (up) connected.TrySetResult(); };
        computer.Start();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        endpoints.Add("203.0.113.7:47821");
        await computer.PublishAsync();
        await Task.Delay(200);

        Assert.Equal(["192.168.1.20:47821", "203.0.113.7:47821"], (await phone.LookupAsync(identity.NodeId))!.Endpoints);
    }
}
