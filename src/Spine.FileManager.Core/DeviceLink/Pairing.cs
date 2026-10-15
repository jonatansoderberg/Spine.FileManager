using System.Security.Cryptography;
using System.Text;

namespace Spine.FileManager.DeviceLink;

/// <summary>The joining device's first message: who it is, a fresh nonce, and proof that it knows the QR's secret.</summary>
public sealed record PairingHello(string SessionId, byte[] PublicKey, byte[] Nonce, string Name, byte[] Mac);

/// <summary>The computer's answer: who it is (its key must match the QR's node id), its nonce, its proof of the secret and its signature.</summary>
public sealed record PairingWelcome(byte[] PublicKey, byte[] Nonce, string Name, byte[] Mac, byte[] Signature);

/// <summary>The joining device's signature over the whole exchange, which completes it.</summary>
public sealed record PairingConfirm(byte[] Signature);

/// <summary>Who the other side turned out to be.</summary>
public sealed record PairedNode(string NodeId, byte[] PublicKey, string Name);

public sealed class PairingException(string message) : Exception(message);

/// <summary>
/// The pairing exchange, independent of the transport (plan §20). Both sides prove knowledge of the QR's one-time
/// secret with an HMAC over everything said so far; the joiner checks the computer's key against the QR's node id,
/// so a device that intercepts the connection cannot pose as the computer; both sign the exchange with their
/// identity keys, which are what each side keeps as trust.
/// </summary>
public static class Pairing
{
    /// <summary>The computer's side: one invitation, one successful pairing.</summary>
    public sealed class Host(NodeIdentity identity, PairingInvitation invitation, string name, TimeProvider? time = null)
    {
        private readonly TimeProvider _time = time ?? TimeProvider.System;
        private PairingHello? _hello;
        private byte[]? _nonce;
        private bool _used;

        public PairingWelcome Answer(PairingHello hello)
        {
            if (_used)
                throw new PairingException("This pairing code has already been used.");
            if (invitation.IsExpired(_time.GetUtcNow()))
                throw new PairingException("This pairing code has expired.");
            if (hello.SessionId != invitation.SessionId)
                throw new PairingException("This pairing code belongs to another session.");
            if (!CryptographicOperations.FixedTimeEquals(hello.Mac, Mac(invitation.Secret, "join", JoinTranscript(hello))))
                throw new PairingException("The other device does not know this pairing code.");
            // One attempt per code: a second device, or a second try, needs a new code.
            _used = true;
            _hello = hello;
            _nonce = RandomNumberGenerator.GetBytes(32);
            var transcript = Transcript(hello, identity.PublicKey, _nonce, name);
            return new PairingWelcome(identity.PublicKey, _nonce, name, Mac(invitation.Secret, "host", transcript), identity.Sign(transcript));
        }

        public PairedNode Complete(PairingConfirm confirm)
        {
            if (_hello is null || _nonce is null)
                throw new PairingException("The pairing has not started.");
            if (!NodeIdentity.Verify(_hello.PublicKey, Transcript(_hello, identity.PublicKey, _nonce, name), confirm.Signature))
                throw new PairingException("The other device's signature does not match.");
            return new PairedNode(NodeIdentity.Fingerprint(_hello.PublicKey), _hello.PublicKey, _hello.Name);
        }
    }

    /// <summary>The joining device's side, after scanning the QR.</summary>
    public sealed class Joiner(NodeIdentity identity, PairingInvitation invitation, string name, TimeProvider? time = null)
    {
        private readonly TimeProvider _time = time ?? TimeProvider.System;
        private PairingHello? _hello;

        public PairingHello Hello()
        {
            if (invitation.IsExpired(_time.GetUtcNow()))
                throw new PairingException("This pairing code has expired.");
            var nonce = RandomNumberGenerator.GetBytes(32);
            var hello = new PairingHello(invitation.SessionId, identity.PublicKey, nonce, name, []);
            return _hello = hello with { Mac = Mac(invitation.Secret, "join", JoinTranscript(hello)) };
        }

        public (PairingConfirm Confirm, PairedNode Host) Accept(PairingWelcome welcome)
        {
            if (_hello is null)
                throw new PairingException("The pairing has not started.");
            if (NodeIdentity.Fingerprint(welcome.PublicKey) != invitation.NodeId)
                throw new PairingException("The device that answered is not the computer that showed the code.");
            var transcript = Transcript(_hello, welcome.PublicKey, welcome.Nonce, welcome.Name);
            if (!CryptographicOperations.FixedTimeEquals(welcome.Mac, Mac(invitation.Secret, "host", transcript)))
                throw new PairingException("The computer does not know this pairing code.");
            if (!NodeIdentity.Verify(welcome.PublicKey, transcript, welcome.Signature))
                throw new PairingException("The computer's signature does not match.");
            return (new PairingConfirm(identity.Sign(transcript)), new PairedNode(invitation.NodeId, welcome.PublicKey, welcome.Name));
        }
    }

    /// <summary>Proof that whoever hands over <paramref name="invitation"/> knows the receiving computer's code.</summary>
    public static byte[] IntroductionMac(byte[] secret, string invitation) => Mac(secret, "introduce", Frame("spinefm-introduce-v1", invitation));

    private static byte[] Mac(byte[] secret, string role, byte[] transcript) =>
        HMACSHA256.HashData(secret, (byte[])[.. Encoding.UTF8.GetBytes(role), .. transcript]);

    private static byte[] JoinTranscript(PairingHello hello) => Frame("spinefm-pair-v1", hello.SessionId, hello.PublicKey, hello.Nonce, hello.Name);

    private static byte[] Transcript(PairingHello hello, byte[] hostKey, byte[] hostNonce, string hostName) =>
        [.. JoinTranscript(hello), .. Frame(hostKey, hostNonce, hostName)];

    // Every field length-prefixed, so no two different exchanges read as the same bytes.
    internal static byte[] Frame(params object[] fields)
    {
        using var stream = new MemoryStream();
        foreach (var field in fields)
        {
            var bytes = field as byte[] ?? Encoding.UTF8.GetBytes((string)field);
            stream.Write(BitConverter.GetBytes(bytes.Length));
            stream.Write(bytes);
        }
        return stream.ToArray();
    }
}
