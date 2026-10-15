using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PeerLinkSpike;

/// <summary>
/// NAT-PMP (RFC 6886): asks the home router to forward a public UDP port to this machine, so a device anywhere can
/// reach it directly, whatever NAT that device is behind. Apple's and Google's routers answer it; UPnP is the other way.
/// </summary>
public static class NatPmp
{
    public static IPAddress? Gateway() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.Name.StartsWith("en"))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
            .Select(a => { var b = a.GetAddressBytes(); b[3] = 1; return new IPAddress(b); })
            .FirstOrDefault();

    public static async Task<IPEndPoint?> MapUdpAsync(IPAddress gateway, int internalPort, TimeSpan lifetime)
    {
        using var udp = new UdpClient();
        udp.Connect(gateway, 5351);
        var external = await Ask(udp, [0, 0]);
        if (external is not { Length: >= 12 } || external[1] != 128 || BinaryPrimitives.ReadUInt16BigEndian(external.AsSpan(2)) != 0)
            return null;
        var address = new IPAddress(external.AsSpan(8, 4));
        var request = new byte[12];
        request[1] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4), (ushort)internalPort);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6), (ushort)internalPort);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(8), (uint)lifetime.TotalSeconds);
        var mapped = await Ask(udp, request);
        if (mapped is not { Length: >= 16 } || mapped[1] != 129 || BinaryPrimitives.ReadUInt16BigEndian(mapped.AsSpan(2)) != 0)
            return null;
        return new IPEndPoint(address, BinaryPrimitives.ReadUInt16BigEndian(mapped.AsSpan(10)));
    }

    private static async Task<byte[]?> Ask(UdpClient udp, byte[] request)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await udp.SendAsync(request);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250 << attempt));
            try
            {
                return (await udp.ReceiveAsync(timeout.Token)).Buffer;
            }
            catch (OperationCanceledException)
            {
            }
        }
        return null;
    }
}
