using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace Spine.FileManager.Transfer;

/// <summary>A file offered by the sender. <see cref="Id"/> stays the same across reconnects, so the receiver can resume.</summary>
public sealed record FileOffer(string Id, string Name, long Size, byte[] Sha256);

public sealed record FileAccept(long Offset);

public sealed record FileDone(bool Verified, string? Error);

/// <summary>
/// Sends files over a channel and receives them, resumably (plan §26–27). The receiver writes each file as a hidden
/// part file and answers an offer with how much of it it already has; the sender goes on from there; the receiver
/// checks the whole file's SHA-256 before giving it its name. Messages are length-prefixed JSON, file bytes follow raw.
/// </summary>
public static class FileTransfer
{
    public const int ChunkSize = 1024 * 1024;

    public static async Task<byte[]> HashAsync(string path, CancellationToken cancel = default)
    {
        await using var file = File.OpenRead(path);
        return await SHA256.HashDataAsync(file, cancel);
    }

    /// <summary>Sends one file; returns how many bytes went over the wire (less than the size after a resume).</summary>
    public static async Task<long> SendAsync(Stream channel, string path, FileOffer offer, Action<long>? progress = null, CancellationToken cancel = default)
    {
        await WriteAsync(channel, offer, cancel);
        var accept = await ReadAsync<FileAccept>(channel, cancel);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true);
        file.Seek(accept.Offset, SeekOrigin.Begin);
        var buffer = new byte[ChunkSize];
        long sent = 0;
        int read;
        while ((read = await file.ReadAsync(buffer, cancel)) > 0)
        {
            await channel.WriteAsync(buffer.AsMemory(0, read), cancel);
            sent += read;
            progress?.Invoke(accept.Offset + sent);
        }
        await channel.FlushAsync(cancel);
        var done = await ReadAsync<FileDone>(channel, cancel);
        if (!done.Verified)
            throw new IOException(done.Error ?? "The receiver could not verify the file.");
        return sent;
    }

    /// <summary>Receives one file into <paramref name="folder"/>; null when the sender has no more files.</summary>
    public static async Task<string?> ReceiveAsync(Stream channel, string folder, Action<long>? progress = null, CancellationToken cancel = default)
    {
        var offer = await ReadAsync<FileOffer?>(channel, cancel);
        if (offer is null)
            return null;
        var name = Path.GetFileName(offer.Name);
        var part = Path.Combine(folder, $".{name}.{offer.Id}.spine-part");
        // The checksum is computed as the bytes arrive; after a resume the kept part is read once first.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var file = new FileStream(part, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, ChunkSize, useAsync: true))
        {
            // What a cut-off transfer left is kept: the sender goes on from its end. A wrong byte in it (a crash
            // while writing) shows in the checksum and the file is sent again whole.
            var offset = Math.Min(file.Length, offer.Size);
            file.SetLength(offset);
            var buffer = new byte[ChunkSize];
            int read;
            while ((read = await file.ReadAsync(buffer, cancel)) > 0)
                hash.AppendData(buffer, 0, read);
            await WriteAsync(channel, new FileAccept(offset), cancel);
            var remaining = offer.Size - offset;
            while (remaining > 0)
            {
                read = await channel.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancel);
                if (read == 0)
                    throw new EndOfStreamException("The connection closed in the middle of a file.");
                await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                hash.AppendData(buffer, 0, read);
                // Handed to the OS chunk by chunk, so a cut connection leaves what arrived in the part file.
                await file.FlushAsync(cancel);
                remaining -= read;
                progress?.Invoke(offer.Size - remaining);
            }
        }
        var verified = hash.GetHashAndReset().AsSpan().SequenceEqual(offer.Sha256);
        if (!verified)
        {
            File.Delete(part);
            await WriteAsync(channel, new FileDone(false, "The file arrived damaged; it will be sent again."), cancel);
            throw new IOException($"{name} did not match its checksum.");
        }
        var target = Path.Combine(folder, name);
        File.Move(part, target, overwrite: false);
        await WriteAsync(channel, new FileDone(true, null), cancel);
        return target;
    }

    /// <summary>Tells the receiver there are no more files.</summary>
    public static Task EndAsync(Stream channel, CancellationToken cancel = default) => WriteAsync<FileOffer?>(channel, null, cancel);

    private static async Task WriteAsync<T>(Stream channel, T message, CancellationToken cancel)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message);
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, json.Length);
        await channel.WriteAsync(length, cancel);
        await channel.WriteAsync(json, cancel);
        await channel.FlushAsync(cancel);
    }

    private static async Task<T> ReadAsync<T>(Stream channel, CancellationToken cancel)
    {
        var length = new byte[4];
        await channel.ReadExactlyAsync(length, cancel);
        var size = BinaryPrimitives.ReadInt32BigEndian(length);
        if (size is < 0 or > 1 << 20)
            throw new IOException("The other device sent a message of an impossible size.");
        var json = new byte[size];
        await channel.ReadExactlyAsync(json, cancel);
        return JsonSerializer.Deserialize<T>(json)!;
    }
}
