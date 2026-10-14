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
