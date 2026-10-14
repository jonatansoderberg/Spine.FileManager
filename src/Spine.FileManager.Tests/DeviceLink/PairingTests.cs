using Microsoft.Extensions.Time.Testing;
using Spine.FileManager.DeviceLink;
using Spine.FileManager.Tests.Storage;

namespace Spine.FileManager.Tests.DeviceLink;

public sealed class PairingTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 14, 20, 0, 0, TimeSpan.Zero));
    private readonly NodeIdentity _mac = NodeIdentity.Create();
    private readonly NodeIdentity _phone = NodeIdentity.Create();

    public void Dispose()
    {
        _mac.Dispose();
        _phone.Dispose();
        _temp.Dispose();
    }

    private PairingInvitation Invite() => PairingInvitation.Create(_mac, "Jonatans MacBook", ["192.168.1.20:47100"], _time.GetUtcNow());

    [Fact]
    public void An_identity_survives_export_and_import_and_signs_verifiably()
    {
        using var again = NodeIdentity.Import(_mac.ExportPrivateKey());
        Assert.Equal(_mac.NodeId, again.NodeId);
        Assert.Equal(32, _mac.NodeId.Length);
        var signature = again.Sign("hej"u8);
        Assert.True(NodeIdentity.Verify(_mac.PublicKey, "hej"u8, signature));
        Assert.False(NodeIdentity.Verify(_phone.PublicKey, "hej"u8, signature));
        Assert.False(NodeIdentity.Verify(_mac.PublicKey, "hej!"u8, signature));
    }

    [Fact]
    public void An_invitation_round_trips_through_its_QR_text_and_carries_no_key_of_its_own()
    {
        var invitation = Invite();
        var text = invitation.ToUri();
        var parsed = PairingInvitation.Parse(text);
        Assert.Equal(invitation.SessionId, parsed.SessionId);
        Assert.Equal(invitation.Secret, parsed.Secret);
        Assert.Equal(_mac.NodeId, parsed.NodeId);
        Assert.Equal("Jonatans MacBook", parsed.Name);
        Assert.Equal(["192.168.1.20:47100"], parsed.Endpoints);
        Assert.Equal(invitation.Expires.ToUnixTimeSeconds(), parsed.Expires.ToUnixTimeSeconds());
        Assert.DoesNotContain(Convert.ToBase64String(_mac.ExportPrivateKey())[..20], text);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("spinefm://pair?v=2&s=a")]
    [InlineData("spinefm://pair?v=1&s=a&k=short&n=x&name=y&e=z&x=1")]
    public void Anything_else_is_not_a_pairing_code(string text) => Assert.Throws<FormatException>(() => PairingInvitation.Parse(text));

    [Fact]
    public void Both_sides_learn_each_others_key_and_name()
    {
        var invitation = PairingInvitation.Parse(Invite().ToUri());
        var host = new Pairing.Host(_mac, invitation, "Jonatans MacBook", _time);
        var joiner = new Pairing.Joiner(_phone, invitation, "Jonatans iPhone", _time);

        var welcome = host.Answer(joiner.Hello());
        var (confirm, macSeen) = joiner.Accept(welcome);
        var phoneSeen = host.Complete(confirm);

        Assert.Equal(_mac.NodeId, macSeen.NodeId);
        Assert.Equal("Jonatans MacBook", macSeen.Name);
        Assert.Equal(_phone.NodeId, phoneSeen.NodeId);
        Assert.Equal(_phone.PublicKey, phoneSeen.PublicKey);
        Assert.Equal("Jonatans iPhone", phoneSeen.Name);
    }

    [Fact]
    public void A_device_without_the_code_is_turned_away()
    {
        var invitation = Invite();
        var host = new Pairing.Host(_mac, invitation, "Mac", _time);
        var guessed = invitation with { Secret = new byte[32] };
        var error = Assert.Throws<PairingException>(() => host.Answer(new Pairing.Joiner(_phone, guessed, "Phone", _time).Hello()));
        Assert.Contains("does not know", error.Message);
    }

    [Fact]
    public void A_device_that_intercepts_the_connection_cannot_pose_as_the_computer()
    {
        // The attacker saw the QR (so it knows the secret) and answers the phone with its own key.
        var invitation = Invite();
        using var attacker = NodeIdentity.Create();
        var impostor = new Pairing.Host(attacker, invitation, "Jonatans MacBook", _time);
        var joiner = new Pairing.Joiner(_phone, invitation, "Phone", _time);
        var welcome = impostor.Answer(joiner.Hello());
        var error = Assert.Throws<PairingException>(() => joiner.Accept(welcome));
        Assert.Contains("not the computer", error.Message);
    }

    [Fact]
    public void A_changed_answer_fails_its_proof()
    {
        var invitation = Invite();
        var host = new Pairing.Host(_mac, invitation, "Mac", _time);
        var joiner = new Pairing.Joiner(_phone, invitation, "Phone", _time);
        var welcome = host.Answer(joiner.Hello()) with { Name = "Someone else" };
        Assert.Throws<PairingException>(() => joiner.Accept(welcome));
    }

    [Fact]
    public void A_code_works_once_and_only_while_it_is_fresh()
    {
        var invitation = Invite();
        var host = new Pairing.Host(_mac, invitation, "Mac", _time);
        var hello = new Pairing.Joiner(_phone, invitation, "Phone", _time).Hello();
        host.Answer(hello);
        Assert.Contains("already been used", Assert.Throws<PairingException>(() => host.Answer(hello)).Message);

        var late = Invite();
        var lateHost = new Pairing.Host(_mac, late, "Mac", _time);
        var lateJoiner = new Pairing.Joiner(_phone, late, "Phone", _time);
        _time.Advance(PairingInvitation.Lifetime + TimeSpan.FromSeconds(1));
        Assert.Contains("expired", Assert.Throws<PairingException>(() => lateJoiner.Hello()).Message);
    }

    [Fact]
    public void A_forged_confirmation_is_refused()
    {
        var invitation = Invite();
        var host = new Pairing.Host(_mac, invitation, "Mac", _time);
        var joiner = new Pairing.Joiner(_phone, invitation, "Phone", _time);
        host.Answer(joiner.Hello());
        Assert.Throws<PairingException>(() => host.Complete(new PairingConfirm(new byte[64])));
    }

    [Fact]
    public void Remembered_nodes_are_kept_on_disk_with_their_alias_and_others_are_not()
    {
        var path = Path.Combine(_temp.Path, "trust.json");
        var trust = new TrustStore(path);
        var phone = new PairedNode(_phone.NodeId, _phone.PublicKey, "Jonatans iPhone");
        using var other = NodeIdentity.Create();
        trust.Trust(phone, TrustLifetime.Remember, _time.GetUtcNow());
        trust.Trust(new PairedNode(other.NodeId, other.PublicKey, "Guest"), TrustLifetime.Session, _time.GetUtcNow());
        trust.Rename(_phone.NodeId, "Telefonen");

        var reloaded = new TrustStore(path);
        Assert.Equal(["Telefonen"], reloaded.All.Select(n => n.DisplayName));
        Assert.Equal("Jonatans iPhone", reloaded.Find(_phone.NodeId)!.SourceName);
        Assert.Equal(2, trust.All.Count);
    }

    [Fact]
    public void A_trusted_node_reconnects_with_a_signed_challenge_until_it_is_revoked()
    {
        var trust = new TrustStore(Path.Combine(_temp.Path, "trust.json"));
        trust.Trust(new PairedNode(_phone.NodeId, _phone.PublicKey, "Phone"), TrustLifetime.Remember, _time.GetUtcNow());

        var challenge = Reconnect.Challenge();
        var proof = Reconnect.Prove(_phone, challenge, _mac.NodeId);
        Assert.True(Reconnect.Verify(trust, _phone.NodeId, challenge, _mac.NodeId, proof));
        Assert.False(Reconnect.Verify(trust, _phone.NodeId, Reconnect.Challenge(), _mac.NodeId, proof));
        Assert.False(Reconnect.Verify(trust, _phone.NodeId, challenge, "someone-else", proof));

        using var stranger = NodeIdentity.Create();
        Assert.False(Reconnect.Verify(trust, stranger.NodeId, challenge, _mac.NodeId, Reconnect.Prove(stranger, challenge, _mac.NodeId)));

        trust.Revoke(_phone.NodeId);
        Assert.False(Reconnect.Verify(trust, _phone.NodeId, challenge, _mac.NodeId, proof));
    }
}
