using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Spine.FileManager.DeviceLink;

namespace Spine.FileManager.Transfer;

public sealed class ChannelException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// A TLS connection between two nodes, each showing a certificate on its node key and pinning the other's key by its
/// node id. QUIC would add streams and faster reconnects, but .NET has it only where msquic is (Windows, Linux), not on
/// the Mac, iOS or Android (Spike 6), so TCP + TLS is the transport every platform has.
/// </summary>
public static class SecureChannel
{
    public static Task<(SslStream Stream, string PeerNodeId)> AcceptAsync(TcpClient client, NodeIdentity self, Func<string, bool> isTrusted, CancellationToken cancel = default) =>
        AcceptAsync(client.GetStream(), self, isTrusted, cancel);

    /// <summary>TLS over a connection that has already said what it is for.</summary>
    public static async Task<(SslStream Stream, string PeerNodeId)> AcceptAsync(Stream connection, NodeIdentity self, Func<string, bool> isTrusted, CancellationToken cancel = default)
    {
        var stream = new SslStream(connection, leaveInnerStreamOpen: false);
        string? peer = null;
        try
        {
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = self.CreateCertificate(),
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                RemoteCertificateValidationCallback = (_, certificate, _, _) => (peer = NodeIdOf(certificate)) is { } id && isTrusted(id),
            }, cancel);
        }
        catch (AuthenticationException e)
        {
            await stream.DisposeAsync();
            throw new ChannelException(peer is null ? "The other device showed no node key." : $"Node {peer} is not trusted.", e);
        }
        return (stream, peer!);
    }

    public static async Task<SslStream> ConnectAsync(IPEndPoint endpoint, NodeIdentity self, string expectedNodeId, CancellationToken cancel = default)
    {
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(endpoint, cancel);
        return await ConnectAsync(client.GetStream(), self, expectedNodeId, cancel);
    }

    /// <summary>TLS over a connection that has already said what it is for.</summary>
    public static async Task<SslStream> ConnectAsync(Stream connection, NodeIdentity self, string expectedNodeId, CancellationToken cancel = default)
    {
        var stream = new SslStream(connection, leaveInnerStreamOpen: false);
        string? peer = null;
        try
        {
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = expectedNodeId,
                ClientCertificates = [self.CreateCertificate()],
                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                RemoteCertificateValidationCallback = (_, certificate, _, _) => (peer = NodeIdOf(certificate)) == expectedNodeId,
            }, cancel);
        }
        catch (AuthenticationException e)
        {
            await stream.DisposeAsync();
            throw new ChannelException(peer is null ? "The other device showed no node key." : $"Expected node {expectedNodeId}, reached {peer}.", e);
        }
        return stream;
    }

    // The node id is the fingerprint of the certificate's key, so a certificate proves the node only by its key.
    private static string? NodeIdOf(X509Certificate? certificate) =>
        certificate is null ? null : NodeIdentity.Fingerprint(X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()).PublicKey.ExportSubjectPublicKeyInfo());
}
