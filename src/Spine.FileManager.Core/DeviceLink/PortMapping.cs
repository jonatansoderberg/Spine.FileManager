using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace Spine.FileManager.DeviceLink;

/// <summary>A port the router forwards to this machine: the public address, how long it lasts, and how it was made.</summary>
public sealed record MappedPort(IPEndPoint Public, TimeSpan Lifetime, string Method);

/// <summary>
/// Asks the home router to forward a public TCP port to this machine, so a linked device anywhere on the internet can
/// reach it with the same TLS transport as on the local network (plan, rung 2). NAT-PMP (RFC 6886) first, as Apple's and
/// Google's routers answer it; UPnP IGD otherwise. A router that answers neither, or a phone on mobile data, gets no port.
/// </summary>
public static class PortMapping
{
    private const int NatPmpPort = 5351;

    public static async Task<MappedPort?> MapTcpAsync(int port, TimeSpan lifetime, CancellationToken cancel = default)
    {
        if (LocalAddress() is not { } local)
            return null;
        return await NatPmpAsync(Gateway(local), port, lifetime, cancel)
            ?? await UpnpAsync(local, port, lifetime, cancel);
    }

    /// <summary>Takes the mapping down: NAT-PMP with a lifetime of zero, UPnP with DeletePortMapping.</summary>
    public static async Task UnmapTcpAsync(MappedPort mapped, int port)
    {
        try
        {
            if (mapped.Method == "NAT-PMP" && LocalAddress() is { } local)
                await NatPmpAsync(Gateway(local), port, TimeSpan.Zero, CancellationToken.None);
            else if (mapped.Method == "UPnP" && _upnpControl is { } control)
                await SoapAsync(control.Url, control.Service, "DeletePortMapping",
                    [("NewRemoteHost", ""), ("NewExternalPort", mapped.Public.Port.ToString()), ("NewProtocol", "TCP")], CancellationToken.None);
        }
        catch (Exception e) when (e is IOException or SocketException or HttpRequestException or OperationCanceledException)
        {
            // The router drops it when its lifetime runs out anyway.
        }
    }

    // The machine's address on its local network: the interface the router serves, not a VPN, bridge or mobile data.
    private static IPAddress? LocalAddress() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
                && !n.Name.StartsWith("bridge", StringComparison.Ordinal) && !n.Name.StartsWith("vEthernet", StringComparison.Ordinal))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(IsPrivate);

    // The platform reports no gateway on macOS and iOS; a home router is the .1 of its network in nearly every case.
    private static IPAddress Gateway(IPAddress local)
    {
        var bytes = local.GetAddressBytes();
        bytes[3] = 1;
        return new IPAddress(bytes);
    }

    /// <summary>Whether an IPv4 address is on a local network (RFC 1918, link-local or shared address space).</summary>
    public static bool IsPrivate(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes() is [10, _, _, _] or [192, 168, _, _] or [169, 254, _, _]
            or [172, >= 16 and <= 31, _, _] or [100, >= 64 and <= 127, _, _];

    private static async Task<MappedPort?> NatPmpAsync(IPAddress gateway, int port, TimeSpan lifetime, CancellationToken cancel)
    {
        using var udp = new UdpClient();
        udp.Connect(gateway, NatPmpPort);
        var external = await AskAsync(udp, [0, 0], cancel);
        if (external is not { Length: >= 12 } || external[1] != 128 || BinaryPrimitives.ReadUInt16BigEndian(external.AsSpan(2)) != 0)
            return null;
        var address = new IPAddress(external.AsSpan(8, 4));
        var request = new byte[12];
        request[1] = 2; // TCP
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4), (ushort)port);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6), (ushort)port);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(8), (uint)lifetime.TotalSeconds);
        var mapped = await AskAsync(udp, request, cancel);
        if (mapped is not { Length: >= 16 } || mapped[1] != 130 || BinaryPrimitives.ReadUInt16BigEndian(mapped.AsSpan(2)) != 0)
            return null;
        // A router behind another NAT (an operator's) hands out an address nobody outside reaches.
        if (IsPrivate(address))
            return null;
        return new MappedPort(new IPEndPoint(address, BinaryPrimitives.ReadUInt16BigEndian(mapped.AsSpan(10))),
            TimeSpan.FromSeconds(BinaryPrimitives.ReadUInt32BigEndian(mapped.AsSpan(12))), "NAT-PMP");
    }

    private static async Task<byte[]?> AskAsync(UdpClient udp, byte[] request, CancellationToken cancel)
    {
        // RFC 6886: 250 ms, doubling; three tries are enough on a home network.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await udp.SendAsync(request, cancel);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(250 << attempt));
                return (await udp.ReceiveAsync(timeout.Token)).Buffer;
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
            }
            catch (SocketException)
            {
                return null;
            }
        }
        return null;
    }

    private static (Uri Url, string Service)? _upnpControl;

    private static async Task<MappedPort?> UpnpAsync(IPAddress local, int port, TimeSpan lifetime, CancellationToken cancel)
    {
        try
        {
            if (await FindControlAsync(cancel) is not { } control)
                return null;
            _upnpControl = control;
            var external = await SoapAsync(control.Url, control.Service, "GetExternalIPAddress", [], cancel);
            if (!IPAddress.TryParse(Value(external, "NewExternalIPAddress"), out var address) || IsPrivate(address))
                return null;
            await SoapAsync(control.Url, control.Service, "AddPortMapping",
            [
                ("NewRemoteHost", ""), ("NewExternalPort", port.ToString()), ("NewProtocol", "TCP"), ("NewInternalPort", port.ToString()),
                ("NewInternalClient", local.ToString()), ("NewEnabled", "1"), ("NewPortMappingDescription", "Spine File Manager"),
                ("NewLeaseDuration", ((int)lifetime.TotalSeconds).ToString()),
            ], cancel);
            return new MappedPort(new IPEndPoint(address, port), lifetime, "UPnP");
        }
        catch (Exception e) when (e is IOException or SocketException or HttpRequestException or System.Xml.XmlException or OperationCanceledException && !cancel.IsCancellationRequested)
        {
            return null;
        }
    }

    // SSDP: the gateway answers a multicast search with the URL of its description, which names the WAN service's control URL.
    private static async Task<(Uri Url, string Service)?> FindControlAsync(CancellationToken cancel)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var search = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n" +
            "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n");
        await udp.SendAsync(search, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900), cancel);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            while (true)
            {
                var answer = Encoding.ASCII.GetString((await udp.ReceiveAsync(timeout.Token)).Buffer);
                var location = answer.Split("\r\n").FirstOrDefault(l => l.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))?[9..].Trim();
                if (location is null || !Uri.TryCreate(location, UriKind.Absolute, out var url))
                    continue;
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var description = XDocument.Parse(await http.GetStringAsync(url, cancel));
                foreach (var service in description.Descendants().Where(e => e.Name.LocalName == "service"))
                {
                    var type = service.Elements().FirstOrDefault(e => e.Name.LocalName == "serviceType")?.Value ?? "";
                    var control = service.Elements().FirstOrDefault(e => e.Name.LocalName == "controlURL")?.Value;
                    if (control is not null && (type.Contains("WANIPConnection") || type.Contains("WANPPPConnection")))
                        return (new Uri(url, control), type);
                }
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<XDocument> SoapAsync(Uri control, string service, string action, (string Name, string Value)[] arguments, CancellationToken cancel)
    {
        var body = new StringBuilder($"<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
            $"s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:{action} xmlns:u=\"{service}\">");
        foreach (var (name, value) in arguments)
            body.Append($"<{name}>{System.Security.SecurityElement.Escape(value)}</{name}>");
        body.Append($"</u:{action}></s:Body></s:Envelope>");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using var request = new HttpRequestMessage(HttpMethod.Post, control) { Content = new StringContent(body.ToString(), Encoding.UTF8, "text/xml") };
        request.Headers.Add("SOAPAction", $"\"{service}#{action}\"");
        using var response = await http.SendAsync(request, cancel);
        var text = await response.Content.ReadAsStringAsync(cancel);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The router turned {action} down ({(int)response.StatusCode}): {text}");
        return XDocument.Parse(text);
    }

    private static string? Value(XDocument document, string name) => document.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
}
