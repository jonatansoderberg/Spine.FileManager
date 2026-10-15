using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace Spine.FileManager.Transfer;

/// <summary>
/// A file offered by the sender, in pieces of <see cref="PieceSize"/> bytes with the SHA-256 of each and of the whole.
/// <see cref="Id"/> stays the same across reconnects and restarts, so the receiver finds what it already has.
/// </summary>
public sealed record FileOffer(string Id, string Name, long Size, byte[] Sha256, int PieceSize, IReadOnlyList<byte[]> Pieces);

/// <summary>The receiver's answer to an offer, and after each round: the pieces it still needs, none when it has them all.</summary>
public sealed record FileNeed(IReadOnlyList<int> Pieces);

public sealed record FileDone(bool Verified, string? Error);

/// <summary>
/// Sends files over a channel and receives them in pieces, as BitTorrent does between two peers (plan §26–27, peer link
/// step 4). The receiver keeps a file it is receiving in a staging folder under the offer's id, checks every piece as
/// it arrives and asks for the pieces it lacks or that arrived damaged; a cut transfer goes on from the pieces it has,
/// after a reconnect or a restart. The whole file's SHA-256 is checked before it gets its name. Messages are
/// length-prefixed JSON; piece bytes follow raw.
/// </summary>
public static class FileTransfer
{
    public const int ChunkSize = 1024 * 1024;

    /// <summary>Rounds of asking for damaged pieces before the receiver gives up on a file.</summary>
    public const int Rounds = 3;

    /// <summary>The hidden folder, beside received files, where a file waits until all its pieces are in.</summary>
    public const string StagingFolder = ".spine-incoming";

    public static async Task<byte[]> HashAsync(string path, CancellationToken cancel = default)
    {
        await using var file = File.OpenRead(path);
        return await SHA256.HashDataAsync(file, cancel);
    }

    /// <summary>1 MB pieces, doubled until a file has at most 4096 of them, so an offer stays small.</summary>
    public static int PieceSizeFor(long size)
    {
        var piece = ChunkSize;
        while (size / piece > 4096 && piece < 64 * ChunkSize)
            piece *= 2;
        return piece;
    }

    /// <summary>An offer for the file at <paramref name="path"/>: its pieces' hashes and the whole file's, in one read.</summary>
    public static async Task<FileOffer> OfferAsync(string path, string id, string name, CancellationToken cancel = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true);
        var size = file.Length;
        var pieceSize = PieceSizeFor(size);
        using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var pieces = new List<byte[]>();
        var buffer = new byte[pieceSize];
        int read;
        while ((read = await ReadFullAsync(file, buffer, cancel)) > 0)
        {
            whole.AppendData(buffer, 0, read);
            pieces.Add(SHA256.HashData(buffer.AsSpan(0, read)));
        }
        return new FileOffer(id, name, size, whole.GetHashAndReset(), pieceSize, pieces);
    }

    private static long PieceLength(FileOffer offer, int index) => Math.Min(offer.PieceSize, offer.Size - (long)index * offer.PieceSize);

    /// <summary>
    /// Sends one file: the pieces the receiver asks for, round after round. <paramref name="progress"/> gets how much of
    /// the file the receiver has; <paramref name="checkpoint"/> runs between pieces (a paused operation waits there).
    /// Returns how many bytes went over the wire, less than the size after a resume.
    /// </summary>
    public static async Task<long> SendAsync(Stream channel, string path, FileOffer offer, Action<long>? progress = null, CancellationToken cancel = default,
        Func<CancellationToken, Task>? checkpoint = null)
    {
        await WriteAsync(channel, offer, cancel);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true);
        var buffer = new byte[offer.PieceSize];
        long sent = 0;
        while (await ReadAsync<FileNeed>(channel, cancel) is { Pieces.Count: > 0 } need)
        {
            var have = offer.Size - need.Pieces.Sum(i => PieceLength(offer, i));
            foreach (var index in need.Pieces)
            {
                if (checkpoint is not null)
                    await checkpoint(cancel);
                var length = (int)PieceLength(offer, index);
                file.Seek((long)index * offer.PieceSize, SeekOrigin.Begin);
                await file.ReadExactlyAsync(buffer.AsMemory(0, length), cancel);
                await channel.WriteAsync(buffer.AsMemory(0, length), cancel);
                sent += length;
                have += length;
                progress?.Invoke(have);
            }
            await channel.FlushAsync(cancel);
        }
        progress?.Invoke(offer.Size);
        var done = await ReadAsync<FileDone>(channel, cancel);
        if (!done.Verified)
            throw new IOException(done.Error ?? "The receiver could not verify the file.");
        return sent;
    }

    /// <summary>Receives one file into <paramref name="folder"/>; null when the sender has no more files.</summary>
    public static Task<string?> ReceiveAsync(Stream channel, string folder, Action<long>? progress = null, CancellationToken cancel = default) =>
        ReceiveAsync(channel, offer => Path.Combine(folder, Path.GetFileName(offer.Name)), Path.Combine(folder, StagingFolder), progress, cancel);

    /// <summary>
    /// Receives one file to the path <paramref name="target"/> chooses for its offer, through <paramref name="staging"/>;
    /// null when the sender has no more files.
    /// </summary>
    public static async Task<string?> ReceiveAsync(Stream channel, Func<FileOffer, string> target, string staging, Action<long>? progress = null, CancellationToken cancel = default)
    {
        var offer = await ReadAsync<FileOffer?>(channel, cancel);
        if (offer is null)
            return null;
        // The id names a file here: letters, digits, '-', '_' and '.', never leading with a dot or climbing out.
        if (offer.Id.Length is 0 or > 128 || offer.Id[0] == '.' || offer.Id.Contains("..") || !offer.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new IOException("The other device sent a file id that cannot be used here.");
        var count = offer.Size == 0 ? 0 : (int)((offer.Size + offer.PieceSize - 1) / offer.PieceSize);
        if (offer.PieceSize <= 0 || offer.Pieces.Count != count)
            throw new IOException("The other device sent pieces that do not add up to the file.");
        var path = target(offer);
        Directory.CreateDirectory(staging);
        var part = Path.Combine(staging, $"{offer.Id}.part");
        var have = new bool[count];
        var buffer = new byte[offer.PieceSize];
        await using (var file = new FileStream(part, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, ChunkSize, useAsync: true))
        {
            // What an earlier attempt left: every whole piece in it is checked, a wrong one is asked for again.
            var kept = file.Length;
            for (var i = 0; i < count; i++)
            {
                var length = (int)PieceLength(offer, i);
                if ((long)i * offer.PieceSize + length > kept)
                    break;
                file.Seek((long)i * offer.PieceSize, SeekOrigin.Begin);
                await file.ReadExactlyAsync(buffer.AsMemory(0, length), cancel);
                have[i] = SHA256.HashData(buffer.AsSpan(0, length)).AsSpan().SequenceEqual(offer.Pieces[i]);
            }
            file.SetLength(offer.Size);
            long received = Enumerable.Range(0, count).Where(i => have[i]).Sum(i => PieceLength(offer, i));
            for (var round = 0; ; round++)
            {
                var need = Enumerable.Range(0, count).Where(i => !have[i]).ToList();
                if (need.Count > 0 && round == Rounds)
                {
                    await WriteAsync(channel, new FileNeed([]), cancel);
                    await WriteAsync(channel, new FileDone(false, "Pieces kept arriving damaged."), cancel);
                    throw new IOException($"{offer.Name}: pieces kept arriving damaged.");
                }
                await WriteAsync(channel, new FileNeed(need), cancel);
                if (need.Count == 0)
                    break;
                foreach (var index in need)
                {
                    var length = (int)PieceLength(offer, index);
                    await channel.ReadExactlyAsync(buffer.AsMemory(0, length), cancel);
                    if (!SHA256.HashData(buffer.AsSpan(0, length)).AsSpan().SequenceEqual(offer.Pieces[index]))
                        continue;
                    file.Seek((long)index * offer.PieceSize, SeekOrigin.Begin);
                    await file.WriteAsync(buffer.AsMemory(0, length), cancel);
                    // Handed to the OS piece by piece, so a cut connection leaves every whole piece in the part file.
                    await file.FlushAsync(cancel);
                    have[index] = true;
                    received += length;
                    progress?.Invoke(received);
                }
            }
            file.Seek(0, SeekOrigin.Begin);
            if (!(await SHA256.HashDataAsync(file, cancel)).AsSpan().SequenceEqual(offer.Sha256))
            {
                await WriteAsync(channel, new FileDone(false, "The file arrived damaged; it will be sent again."), cancel);
                file.Close();
                File.Delete(part);
                throw new IOException($"{offer.Name} did not match its checksum.");
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Move(part, path, overwrite: false);
        await WriteAsync(channel, new FileDone(true, null), cancel);
        return path;
    }

    private static async Task<int> ReadFullAsync(Stream stream, byte[] buffer, CancellationToken cancel)
    {
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), cancel)) > 0)
            total += read;
        return total;
    }

    /// <summary>Tells the receiver there are no more files.</summary>
    public static Task EndAsync(Stream channel, CancellationToken cancel = default) => WriteAsync<FileOffer?>(channel, null, cancel);

    /// <summary>One length-prefixed JSON message, the framing of every exchange between two devices.</summary>
    public static async Task WriteAsync<T>(Stream channel, T message, CancellationToken cancel = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message);
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, json.Length);
        await channel.WriteAsync(length, cancel);
        await channel.WriteAsync(json, cancel);
        await channel.FlushAsync(cancel);
    }

    public static async Task<T> ReadAsync<T>(Stream channel, CancellationToken cancel = default)
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
