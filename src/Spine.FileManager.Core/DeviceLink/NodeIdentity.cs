using System.Security.Cryptography;

namespace Spine.FileManager.DeviceLink;

/// <summary>
/// An installation's identity (plan §19): a P-256 key pair whose public key's fingerprint is the node id. Other nodes
/// trust the key, not a name or an address; the private key never leaves the device.
/// </summary>
public sealed class NodeIdentity : IDisposable
{
    private readonly ECDsa _key;

    private NodeIdentity(ECDsa key)
    {
        _key = key;
        PublicKey = key.ExportSubjectPublicKeyInfo();
        NodeId = Fingerprint(PublicKey);
    }

    /// <summary>The public key as SubjectPublicKeyInfo (DER), what peers store and pin.</summary>
    public byte[] PublicKey { get; }

    public string NodeId { get; }

    public static NodeIdentity Create() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public static NodeIdentity Import(byte[] privateKey)
    {
        var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privateKey, out _);
        return new NodeIdentity(key);
    }

    /// <summary>The private key (PKCS#8) for the platform's secure storage — Keychain, Keystore, DPAPI.</summary>
    public byte[] ExportPrivateKey() => _key.ExportPkcs8PrivateKey();

    public byte[] Sign(ReadOnlySpan<byte> data) => _key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public static bool Verify(byte[] publicKey, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out _);
            return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>The first 20 bytes of the key's SHA-256 in lower-case base32: 32 characters, safe in a URL and a file name.</summary>
    public static string Fingerprint(byte[] publicKey) => Base32(SHA256.HashData(publicKey).AsSpan(0, 20));

    private static string Base32(ReadOnlySpan<byte> bytes)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var text = new char[(bytes.Length * 8 + 4) / 5];
        int buffer = 0, bits = 0, at = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                text[at++] = alphabet[(buffer >> (bits - 5)) & 31];
                bits -= 5;
            }
        }
        if (bits > 0)
            text[at++] = alphabet[(buffer << (5 - bits)) & 31];
        return new string(text, 0, at);
    }

    public void Dispose() => _key.Dispose();
}
