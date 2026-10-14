using System.Security.Cryptography;
using System.Web;

namespace Spine.FileManager.DeviceLink;

/// <summary>
/// What the computer's QR code carries (plan §20): a session, a one-time secret, the computer's node id (its key's
/// fingerprint), its name, where to reach it and when the offer ends. No permanent password: the secret works for
/// one pairing within a couple of minutes.
/// </summary>
public sealed record PairingInvitation(string SessionId, byte[] Secret, string NodeId, string Name, IReadOnlyList<string> Endpoints, DateTimeOffset Expires)
{
    public const string Scheme = "spinefm";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public static PairingInvitation Create(NodeIdentity host, string name, IReadOnlyList<string> endpoints, DateTimeOffset now) =>
        new(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), RandomNumberGenerator.GetBytes(32), host.NodeId, name, endpoints, now + Lifetime);

    public bool IsExpired(DateTimeOffset now) => now >= Expires;

    public string ToUri() =>
        $"{Scheme}://pair?v=1&s={SessionId}&k={Base64Url(Secret)}&n={NodeId}&name={Uri.EscapeDataString(Name)}" +
        $"&e={Uri.EscapeDataString(string.Join(',', Endpoints))}&x={Expires.ToUnixTimeSeconds()}";

    public static PairingInvitation Parse(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Scheme || uri.Host != "pair")
            throw new FormatException("Not a Spine File Manager pairing code.");
        var query = HttpUtility.ParseQueryString(uri.Query);
        if (query["v"] != "1")
            throw new FormatException("This pairing code is from a newer version.");
        string Required(string key) => query[key] is { Length: > 0 } value ? value : throw new FormatException($"The pairing code has no '{key}'.");
        var secret = FromBase64Url(Required("k"));
        if (secret.Length != 32)
            throw new FormatException("The pairing code's secret has the wrong length.");
        return new PairingInvitation(Required("s"), secret, Required("n"), Required("name"),
            [.. Required("e").Split(',', StringSplitOptions.RemoveEmptyEntries)],
            DateTimeOffset.FromUnixTimeSeconds(long.Parse(Required("x"), System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            throw new FormatException("The pairing code's secret is damaged.");
        }
    }
}
