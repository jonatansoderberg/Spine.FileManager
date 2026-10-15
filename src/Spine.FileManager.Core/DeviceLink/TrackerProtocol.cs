using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Spine.FileManager.DeviceLink;

/// <summary>
/// The tracker's messages (plan, step 2), one JSON object per WebSocket text message with its kind in <c>type</c>.
/// The tracker only tells linked devices where the others are and passes on a request to connect; it never carries files.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TrackerChallenge), "challenge")]
[JsonDerivedType(typeof(TrackerHello), "hello")]
[JsonDerivedType(typeof(TrackerWelcome), "welcome")]
[JsonDerivedType(typeof(TrackerUpdate), "update")]
[JsonDerivedType(typeof(TrackerLookup), "lookup")]
[JsonDerivedType(typeof(TrackerFound), "found")]
[JsonDerivedType(typeof(TrackerCall), "call")]
[JsonDerivedType(typeof(TrackerCalled), "called")]
public abstract record TrackerMessage;

/// <summary>Tracker → device, first: a nonce the device signs with its node key.</summary>
public sealed record TrackerChallenge(byte[] Nonce) : TrackerMessage;

/// <summary>
/// Device → tracker: its public key and signature over the challenge, where it listens now, and the hashed ids of the
/// nodes it trusts (<see cref="TrackerProtocol.HashOf"/>): only those may look it up or call it.
/// </summary>
public sealed record TrackerHello(byte[] PublicKey, byte[] Signature, IReadOnlyList<string> Endpoints, IReadOnlyList<string> Trusted) : TrackerMessage;

/// <summary>Tracker → device: the node id the key proved.</summary>
public sealed record TrackerWelcome(string NodeId) : TrackerMessage;

/// <summary>Device → tracker: new endpoints or a changed trust list.</summary>
public sealed record TrackerUpdate(IReadOnlyList<string> Endpoints, IReadOnlyList<string> Trusted) : TrackerMessage;

/// <summary>Device → tracker: where is <paramref name="Node"/>? <paramref name="Id"/> pairs the answer with the question.</summary>
public sealed record TrackerLookup(int Id, string Node) : TrackerMessage;

/// <summary>
/// Tracker → device: where the node last said it listens, whether it is connected to the tracker now and when it was
/// last seen. A node that does not trust the asker, or that the tracker has never seen, comes back with no endpoints.
/// </summary>
public sealed record TrackerFound(int Id, string Node, IReadOnlyList<string> Endpoints, bool Online, DateTimeOffset? LastSeen) : TrackerMessage;

/// <summary>
/// Device → tracker: ask <paramref name="Node"/> to connect to me at <paramref name="Endpoints"/>, for a device that
/// cannot reach it. <paramref name="Purpose"/>: <see cref="TrackerProtocol.Hello"/> to swap addresses, or
/// <see cref="TrackerProtocol.Send"/> when the caller has files for it (the return path).
/// </summary>
public sealed record TrackerCall(string Node, IReadOnlyList<string> Endpoints, string Purpose = TrackerProtocol.Hello) : TrackerMessage;

/// <summary>Tracker → device: <paramref name="From"/> asks to be connected to at <paramref name="Endpoints"/>, for <paramref name="Purpose"/>.</summary>
public sealed record TrackerCalled(string From, IReadOnlyList<string> Endpoints, string Purpose = TrackerProtocol.Hello) : TrackerMessage;

public static class TrackerProtocol
{
    public const string Hello = "hello";
    public const string Send = "send";

    /// <summary>What a device signs: a fixed prefix and the tracker's nonce, so the signature is good for nothing else.</summary>
    public static byte[] Signed(byte[] nonce) => [.. Encoding.ASCII.GetBytes("spinefm-tracker:"), .. nonce];

    /// <summary>A node id as a trust list carries it: the tracker matches the asker against it without the list naming anyone.</summary>
    public static string HashOf(string nodeId) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nodeId)));
}
