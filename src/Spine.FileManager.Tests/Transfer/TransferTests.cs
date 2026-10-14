using System.Net;
using System.Net.Sockets;
using Spine.FileManager.DeviceLink;
using Spine.FileManager.Tests.Storage;
using Spine.FileManager.Transfer;

namespace Spine.FileManager.Tests.Transfer;

public sealed class TransferTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly NodeIdentity _mac = NodeIdentity.Create();
    private readonly NodeIdentity _phone = NodeIdentity.Create();
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public TransferTests() => _listener.Start();

    public void Dispose()
    {
        _listener.Stop();
        _mac.Dispose();
        _phone.Dispose();
        _temp.Dispose();
    }

    private IPEndPoint Endpoint => (IPEndPoint)_listener.LocalEndpoint;

    private string Source(string name, int size)
    {
        var path = Path.Combine(_temp.Path, "src", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = new byte[size];
        new Random(size).NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    private string Inbox => Directory.CreateDirectory(Path.Combine(_temp.Path, "inbox")).FullName;

    private async Task<FileOffer> Offer(string path, string id = "t1") =>
        new(id, Path.GetFileName(path), new FileInfo(path).Length, await FileTransfer.HashAsync(path));

    [Fact]
    public async Task Both_ends_authenticate_by_their_node_keys()
    {
        var server = Task.Run(async () =>
        {
            var (stream, peer) = await SecureChannel.AcceptAsync(await _listener.AcceptTcpClientAsync(), _mac, id => id == _phone.NodeId);
            await stream.DisposeAsync();
            return peer;
        });
        await using (await SecureChannel.ConnectAsync(Endpoint, _phone, _mac.NodeId)) { }
        Assert.Equal(_phone.NodeId, await server);
    }

    [Fact]
    public async Task A_device_with_another_key_is_refused_on_both_sides()
    {
        using var stranger = NodeIdentity.Create();
        // The phone expects the Mac but reaches a device with another key.
        var impostor = Task.Run(async () =>
        {
            try { await SecureChannel.AcceptAsync(await _listener.AcceptTcpClientAsync(), stranger, _ => true); } catch { }
        });
        await Assert.ThrowsAsync<ChannelException>(() => SecureChannel.ConnectAsync(Endpoint, _phone, _mac.NodeId));
        await impostor;

        // The Mac refuses a phone it does not trust.
        var server = Task.Run(async () => await SecureChannel.AcceptAsync(await _listener.AcceptTcpClientAsync(), _mac, _ => false));
        try { await using var _ = await SecureChannel.ConnectAsync(Endpoint, stranger, _mac.NodeId); } catch { }
        await Assert.ThrowsAsync<ChannelException>(() => server);
    }

    [Fact]
    public async Task A_file_arrives_whole_under_its_name()
    {
        var path = Source("video.mp4", 8 * 1024 * 1024 + 123);
        var receiver = Task.Run(async () =>
        {
            var (stream, _) = await SecureChannel.AcceptAsync(await _listener.AcceptTcpClientAsync(), _mac, _ => true);
            await using (stream)
                return await FileTransfer.ReceiveAsync(stream, Inbox);
        });
        await using (var channel = await SecureChannel.ConnectAsync(Endpoint, _phone, _mac.NodeId))
            await FileTransfer.SendAsync(channel, path, await Offer(path));

        var received = await receiver;
        Assert.Equal(Path.Combine(Inbox, "video.mp4"), received);
        Assert.Equal(await FileTransfer.HashAsync(path), await FileTransfer.HashAsync(received!));
        Assert.Empty(Directory.GetFiles(Inbox, "*.spine-part", new EnumerationOptions { AttributesToSkip = 0 }));
    }

    [Fact]
    public async Task A_transfer_cut_off_midway_resumes_where_it_stopped()
    {
        var path = Source("big.bin", 12 * 1024 * 1024);
        var offer = await Offer(path);

        // First attempt: the connection drops after about 5 MB.
        var first = Task.Run(async () =>
        {
            var (stream, _) = await SecureChannel.AcceptAsync(await _listener.AcceptTcpClientAsync(), _mac, _ => true);
            await using (stream)
                try { await FileTransfer.ReceiveAsync(stream, Inbox); } catch (Exception e) when (e is IOException or EndOfStreamException) { }
        });
        var channel = await SecureChannel.ConnectAsync(Endpoint, _phone, _mac.NodeId);
        await Assert.ThrowsAnyAsync<Exception>(() => FileTransfer.SendAsync(channel, path, offer, sent =>
        {
            if (sent > 5 * 1024 * 1024)
                channel.Dispose();
        }));
        await first;
        var part = Assert.Single(Directory.GetFiles(Inbox, "*.spine-part", new EnumerationOptions { AttributesToSkip = 0 }));
        var kept = new FileInfo(part).Length;
        Assert.InRange(kept, 1, offer.Size - 1);

        // Second attempt with the same offer: only the rest goes over.
        var second = Task.Run(async () =>
        {
            var (stream, _) = await SecureChannel.AcceptAsync(await _listener.AcceptTcpClientAsync(), _mac, _ => true);
            await using (stream)
                return await FileTransfer.ReceiveAsync(stream, Inbox);
        });
        long resent;
        await using (var again = await SecureChannel.ConnectAsync(Endpoint, _phone, _mac.NodeId))
            resent = await FileTransfer.SendAsync(again, path, offer);

        var received = await second;
        Assert.Equal(offer.Size - kept, resent);
        Assert.Equal(offer.Sha256, await FileTransfer.HashAsync(received!));
    }

    [Fact]
    public async Task Many_small_files_go_over_one_connection()
    {
        var files = Enumerable.Range(0, 2000).Select(i => Source($"f{i:0000}.txt", 200 + i % 300)).ToList();
        var receiver = Task.Run(async () =>
        {
            var (stream, _) = await SecureChannel.AcceptAsync(await _listener.AcceptTcpClientAsync(), _mac, _ => true);
            var count = 0;
            await using (stream)
                while (await FileTransfer.ReceiveAsync(stream, Inbox) is not null)
                    count++;
            return count;
        });
        await using (var channel = await SecureChannel.ConnectAsync(Endpoint, _phone, _mac.NodeId))
        {
            foreach (var file in files)
                await FileTransfer.SendAsync(channel, file, await Offer(file, Path.GetFileName(file)));
            await FileTransfer.EndAsync(channel);
        }
        Assert.Equal(2000, await receiver);
        Assert.Equal(2000, Directory.GetFiles(Inbox).Length);
    }
}
