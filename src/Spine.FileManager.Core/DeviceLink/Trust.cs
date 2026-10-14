using System.Security.Cryptography;
using System.Text.Json;

namespace Spine.FileManager.DeviceLink;

/// <summary>How long a pairing lasts (plan §21): for one transfer, for this session of the app, or until revoked.</summary>
public enum TrustLifetime { QuickTransfer, Session, Remember }

/// <summary>
/// A node this one trusts: its key, the name it gives itself (<see cref="SourceName"/>) and the name the user gives it
/// here (<see cref="LocalAlias"/>, plan §19).
/// </summary>
public sealed record TrustedNode(string NodeId, byte[] PublicKey, string SourceName, string? LocalAlias, TrustLifetime Lifetime, DateTimeOffset PairedAt)
{
    public string DisplayName => LocalAlias ?? SourceName;
}

/// <summary>The trusted nodes; only <see cref="TrustLifetime.Remember"/> ones are written to disk.</summary>
public sealed class TrustStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private Dictionary<string, TrustedNode>? _nodes;

    public event Action? Changed;

    private Dictionary<string, TrustedNode> Nodes => _nodes ??= Load();

    public IReadOnlyCollection<TrustedNode> All => Nodes.Values;

    public TrustedNode? Find(string nodeId) => Nodes.GetValueOrDefault(nodeId);

    public TrustedNode Trust(PairedNode node, TrustLifetime lifetime, DateTimeOffset now)
    {
        var trusted = new TrustedNode(node.NodeId, node.PublicKey, node.Name, Find(node.NodeId)?.LocalAlias, lifetime, now);
        Nodes[node.NodeId] = trusted;
        Save();
        return trusted;
    }

    public void Rename(string nodeId, string? alias)
    {
        if (Find(nodeId) is { } node)
        {
            Nodes[nodeId] = node with { LocalAlias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim() };
            Save();
        }
    }

    /// <summary>Forgets a node: it can no longer connect until it pairs again.</summary>
    public void Revoke(string nodeId)
    {
        if (Nodes.Remove(nodeId))
            Save();
    }

    private Dictionary<string, TrustedNode> Load()
    {
        try
        {
            var list = File.Exists(path) ? JsonSerializer.Deserialize<List<TrustedNode>>(File.ReadAllText(path), Json) ?? [] : [];
            return list.ToDictionary(n => n.NodeId);
        }
        catch (JsonException)
        {
            File.Move(path, path + ".broken", overwrite: true);
            return [];
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Nodes.Values.Where(n => n.Lifetime == TrustLifetime.Remember).ToList(), Json));
        File.Move(temporary, path, overwrite: true);
        Changed?.Invoke();
    }
}

/// <summary>
/// Reconnecting to a trusted node: each side sends a fresh challenge and signs the other's, together with both node
/// ids, so a proof cannot be replayed to a third node or later.
/// </summary>
public static class Reconnect
{
    public static byte[] Challenge() => RandomNumberGenerator.GetBytes(32);

    public static byte[] Prove(NodeIdentity self, byte[] challenge, string peerNodeId) =>
        self.Sign(Pairing.Frame("spinefm-auth-v1", challenge, self.NodeId, peerNodeId));

    public static bool Verify(TrustStore trust, string peerNodeId, byte[] challenge, string selfNodeId, byte[] proof) =>
        trust.Find(peerNodeId) is { } peer
        && NodeIdentity.Fingerprint(peer.PublicKey) == peerNodeId
        && NodeIdentity.Verify(peer.PublicKey, Pairing.Frame("spinefm-auth-v1", challenge, peerNodeId, selfNodeId), proof);
}
