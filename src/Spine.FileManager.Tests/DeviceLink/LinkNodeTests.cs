using System.Net;
using Spine.FileManager.DeviceLink;
using Spine.FileManager.Tests.Storage;

namespace Spine.FileManager.Tests.DeviceLink;

public sealed class LinkNodeTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly List<IDisposable> _owned = [];

    public void Dispose()
    {
        foreach (var owned in _owned)
            owned.Dispose();
        _temp.Dispose();
    }

    private LinkNode Node(string name)
    {
        var identity = NodeIdentity.Create();
        var node = new LinkNode(identity, new TrustStore(Path.Combine(_temp.Path, name, "trust.json")), name, Path.Combine(_temp.Path, name, "inbox"));
        node.Start(0, IPAddress.Loopback);
        _owned.Add(node);
        _owned.Add(identity);
        return node;
    }

    private static async Task<TrustedNode> Link(LinkNode computer, LinkNode phone, TrustLifetime computerKeeps = TrustLifetime.Remember, TrustLifetime phoneKeeps = TrustLifetime.Remember)
    {
        var paired = new TaskCompletionSource<TrustedNode>();
        computer.Paired += paired.SetResult;
        await phone.JoinAsync(computer.Invite(computerKeeps), phoneKeeps).WaitAsync(TimeSpan.FromSeconds(10));
        return await paired.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Two_devices_pair_over_the_network_and_learn_where_the_other_listens()
    {
        var computer = Node("computer");
        var phone = Node("phone");

        var seenByComputer = await Link(computer, phone, phoneKeeps: TrustLifetime.Session);

        Assert.Equal(phone.Identity.NodeId, seenByComputer.NodeId);
        Assert.Equal("phone", seenByComputer.SourceName);
        Assert.Equal(phone.Endpoints, seenByComputer.Endpoints);
        var seenByPhone = phone.Trust.Find(computer.Identity.NodeId)!;
        Assert.Equal(TrustLifetime.Session, seenByPhone.Lifetime);
        Assert.Equal(computer.Endpoints, seenByPhone.Endpoints);
    }

    [Fact]
    public async Task A_computer_that_turns_the_link_down_is_trusted_by_neither_side()
    {
        var computer = Node("computer");
        var phone = Node("phone");
        PairedNode? asked = null;
        computer.Confirm = joined =>
        {
            asked = joined;
            return Task.FromResult(false);
        };

        await Assert.ThrowsAsync<PairingException>(() => phone.JoinAsync(computer.Invite(TrustLifetime.Remember), TrustLifetime.Remember).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal("phone", asked?.Name);
        Assert.Empty(computer.Trust.All);
        Assert.Empty(phone.Trust.All);
    }

    [Fact]
    public async Task A_phone_that_scanned_both_codes_links_two_computers_without_linking_itself()
    {
        var first = Node("first");
        var second = Node("second");
        var askedFirst = new TaskCompletionSource<string>();
        first.Confirm = joined =>
        {
            askedFirst.TrySetResult(joined.Name);
            return Task.FromResult(true);
        };

        await LinkNode.IntroduceAsync(to: second.Invite(TrustLifetime.Remember), introduced: first.Invite(TrustLifetime.Session)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("second", await askedFirst.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(TrustLifetime.Session, first.Trust.Find(second.Identity.NodeId)?.Lifetime);
        Assert.Equal(TrustLifetime.Remember, second.Trust.Find(first.Identity.NodeId)?.Lifetime);
        Assert.Equal(first.Endpoints, second.Trust.Find(first.Identity.NodeId)?.Endpoints);
    }

    [Fact]
    public async Task An_introduction_without_the_receiving_computers_code_is_refused()
    {
        var first = Node("first");
        var second = Node("second");
        var guessed = second.Invite(TrustLifetime.Remember) with { Secret = new byte[PairingInvitation.SecretLength] };

        await Assert.ThrowsAsync<PairingException>(() => LinkNode.IntroduceAsync(guessed, first.Invite(TrustLifetime.Remember)).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Empty(first.Trust.All);
        Assert.Empty(second.Trust.All);
    }

    [Fact]
    public async Task Files_and_folders_arrive_in_the_inbox_and_a_second_copy_gets_a_free_name()
    {
        var computer = Node("computer");
        var phone = Node("phone");
        await Link(computer, phone);
        var file = _temp.File("send/photo.jpg", "jpeg");
        var folder = _temp.Folder("send/Trip");
        _temp.File("send/Trip/day1/a.txt", "a");
        var received = new List<string>();
        computer.Received += (_, path) => { lock (received) received.Add(path); };

        await phone.SendAsync(computer.Identity.NodeId, [file, folder]);
        await phone.SendAsync(computer.Identity.NodeId, [file]);

        var inbox = Path.Combine(_temp.Path, "computer", "inbox");
        Assert.Equal("jpeg", File.ReadAllText(Path.Combine(inbox, "photo.jpg")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(inbox, "Trip", "day1", "a.txt")));
        Assert.Equal("jpeg", File.ReadAllText(Path.Combine(inbox, "photo (2).jpg")));
        Assert.Equal(3, received.Count);
    }

    [Fact]
    public async Task A_linked_device_answers_a_probe_until_it_stops_and_reports_receiving_progress()
    {
        var computer = Node("computer");
        var phone = Node("phone");
        await Link(computer, phone);
        var progress = new List<(string Name, long Received, long Size)>();
        computer.Receiving += (from, name, received, size) => { lock (progress) progress.Add((name, received, size)); };

        Assert.NotNull(await phone.ProbeAsync(computer.Identity.NodeId));
        await phone.SendAsync(computer.Identity.NodeId, [_temp.File("send/notes.txt", "twelve bytes")]);
        computer.Dispose();

        Assert.Null(await phone.ProbeAsync(computer.Identity.NodeId));
        Assert.Null(await phone.ProbeAsync("not-linked"));
        Assert.Equal(("notes.txt", 12L, 12L), progress[^1]);
    }

    [Fact]
    public async Task Linked_devices_swap_their_current_endpoints_with_the_routers_public_one()
    {
        var computer = Node("computer");
        var phone = Node("phone");
        await Link(computer, phone);
        computer.PublicEndpoint = IPEndPoint.Parse("203.0.113.7:47821");

        var contacted = new TaskCompletionSource<string>();
        computer.Contacted += contacted.SetResult;

        Assert.NotNull(await phone.ExchangeEndpointsAsync(computer.Identity.NodeId));
        Assert.Equal(phone.Identity.NodeId, await contacted.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Contains("203.0.113.7:47821", phone.Trust.Find(computer.Identity.NodeId)!.Endpoints);
        Assert.Equal(phone.Endpoints, computer.Trust.Find(phone.Identity.NodeId)!.Endpoints);
        Assert.Null(await phone.ExchangeEndpointsAsync("not-linked"));
    }

    [Fact]
    public async Task A_transfer_waits_for_the_receivers_answer_and_a_no_sends_nothing()
    {
        var computer = Node("computer");
        var phone = Node("phone");
        await Link(computer, phone);
        IncomingTransfer? asked = null;
        var answer = false;
        computer.AcceptTransfer = transfer => { asked = transfer; return Task.FromResult(answer); };
        var file = _temp.File("send/report.pdf", "pdf bytes");
        var inbox = Path.Combine(_temp.Path, "computer", "inbox");

        var declined = await Assert.ThrowsAsync<TransferDeclinedException>(() => phone.SendAsync(computer.Identity.NodeId, [file]));

        Assert.False(declined.TimedOut);
        Assert.Equal(phone.Identity.NodeId, asked!.NodeId);
        Assert.Equal(["report.pdf"], asked.Request.Names);
        Assert.Equal((1, 9L), (asked.Request.Files, asked.Request.Bytes));
        Assert.False(File.Exists(Path.Combine(inbox, "report.pdf")));

        answer = true;
        await phone.SendAsync(computer.Identity.NodeId, [file]);
        Assert.Equal("pdf bytes", File.ReadAllText(Path.Combine(inbox, "report.pdf")));
    }

    [Fact]
    public async Task A_device_trusted_for_one_transfer_is_forgotten_after_it()
    {
        var computer = Node("computer");
        var phone = Node("phone");
        await Link(computer, phone, computerKeeps: TrustLifetime.QuickTransfer, phoneKeeps: TrustLifetime.QuickTransfer);
        var done = new TaskCompletionSource();
        computer.Trust.Changed += () => { if (computer.Trust.Find(phone.Identity.NodeId) is null) done.TrySetResult(); };

        await phone.SendAsync(computer.Identity.NodeId, [_temp.File("a.txt", "a")]);

        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(phone.Trust.Find(computer.Identity.NodeId));
    }

    [Fact]
    public async Task A_wrong_code_and_an_unknown_sender_are_turned_down()
    {
        var computer = Node("computer");
        var phone = Node("phone");
        var stranger = Node("stranger");
        var invitation = computer.Invite(TrustLifetime.Remember);

        await Assert.ThrowsAnyAsync<Exception>(() => phone.JoinAsync(invitation with { Secret = new byte[PairingInvitation.SecretLength] }, TrustLifetime.Remember));
        Assert.Empty(computer.Trust.All);

        // The stranger knows where the computer is and its node id, but the computer does not trust it.
        stranger.Trust.Trust(new PairedNode(computer.Identity.NodeId, computer.Identity.PublicKey, "computer"), TrustLifetime.Session, DateTimeOffset.UtcNow);
        stranger.Trust.SetEndpoints(computer.Identity.NodeId, computer.Endpoints);
        await Assert.ThrowsAnyAsync<Exception>(() => stranger.SendAsync(computer.Identity.NodeId, [_temp.File("x.txt")]));
        Assert.False(Directory.Exists(Path.Combine(_temp.Path, "computer", "inbox")));
    }
}
