#if ANDROID
using Android.Content;
using Android.Database;
using Android.Provider;
using AUri = Android.Net.Uri;
using Document = Android.Provider.DocumentsContract.Document;

namespace FileSystemSpike;

/// <summary>The same steps through the Storage Access Framework: document ids and content URIs, no paths.</summary>
public static class SafProbe
{
    const int WriteSize = 1024 * 1024;

    public static LocationReport Run(ContentResolver resolver, AUri tree, string name)
    {
        var report = new LocationReport(name, tree.ToString()!, "SAF");
        var rootId = DocumentsContract.GetTreeDocumentId(tree)!;
        var root = DocumentsContract.BuildDocumentUriUsingTree(tree, rootId)!;

        if (!report.Run("exists", () =>
            {
                using var cursor = resolver.Query(root, [Document.ColumnDisplayName, Document.ColumnFlags], null, null, null) ?? throw new IOException("no cursor");
                return cursor.MoveToFirst() ? $"{cursor.GetString(0)}, flags {(DocumentContractFlags)cursor.GetInt(1)}" : throw new FileNotFoundException(rootId);
            }))
            return report;

        string? sampleId = null;
        long sampleSize = 0;
        long sampleModified = 0;
        report.Run("list", () =>
        {
            using var cursor = resolver.Query(DocumentsContract.BuildChildDocumentsUriUsingTree(tree, rootId)!,
                [Document.ColumnDocumentId, Document.ColumnMimeType, Document.ColumnSize, Document.ColumnLastModified], null, null, null) ?? throw new IOException("no cursor");
            int folders = 0, files = 0;
            while (cursor.MoveToNext())
            {
                if (cursor.GetString(1) == Document.MimeTypeDir)
                    folders++;
                else
                {
                    files++;
                    if (sampleId is null)
                        (sampleId, sampleSize, sampleModified) = (cursor.GetString(0), cursor.GetLong(2), cursor.GetLong(3));
                }
            }
            return $"{folders} folders, {files} files";
        });
        report.Run("stat", () => sampleId is null ? throw new SkipException("no file")
            : $"{sampleId}: {sampleSize:N0} bytes, modified {DateTimeOffset.FromUnixTimeMilliseconds(sampleModified).LocalDateTime:yyyy-MM-dd HH:mm}");
        report.Run("read", () =>
        {
            if (sampleId is null)
                throw new SkipException("no file");
            using var stream = resolver.OpenInputStream(DocumentsContract.BuildDocumentUriUsingTree(tree, sampleId)!)!;
            var buffer = new byte[64 * 1024];
            return $"{stream.Read(buffer, 0, buffer.Length):N0} bytes";
        });

        report.Run("native path", () =>
        {
            var path = NativePath(rootId) ?? throw new SkipException($"no path for {rootId}");
            return $"{path}: System.IO lists {Directory.GetFileSystemEntries(path).Length} entries";
        });

        AUri? probe = null;
        try
        {
            if (!report.Run("create-folder", () => { probe = DocumentsContract.CreateDocument(resolver, root, Document.MimeTypeDir, $".spine-probe-{Guid.NewGuid().ToString("N")[..8]}") ?? throw new IOException("null uri"); return null; }))
                return report;

            AUri? file = null;
            var data = new byte[WriteSize];
            Random.Shared.NextBytes(data);
            report.Run("write", () =>
            {
                file = DocumentsContract.CreateDocument(resolver, probe!, "application/octet-stream", "a.bin") ?? throw new IOException("null uri");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                using (var stream = resolver.OpenOutputStream(file)!)
                    stream.Write(data);
                return $"1 MB at {WriteSize / 1048576.0 / watch.Elapsed.TotalSeconds:0} MB/s";
            });
            if (file is null)
                return report;

            report.Run("random-access", () =>
            {
                using var descriptor = resolver.OpenFileDescriptor(file, "rw") ?? throw new IOException("no descriptor");
                using var input = new Java.IO.FileInputStream(descriptor.FileDescriptor);
                using var channel = input.Channel!;
                channel.Position(512 * 1024);
                var buffer = Java.Nio.ByteBuffer.Allocate(4096);
                return $"read {channel.Read(buffer)} bytes at 512 KB through a file descriptor";
            });
            report.Run("rename", () => { file = DocumentsContract.RenameDocument(resolver, file, "b.bin") ?? file; return file.LastPathSegment; });
            report.Run("copy", () =>
            {
                var flags = Flags(resolver, file);
                if (!flags.HasFlag(DocumentContractFlags.SupportsCopy))
                    throw new NotSupportedException($"provider does not offer copy (flags {flags}); copy as a stream instead");
                return DocumentsContract.CopyDocument(resolver, file, probe!)?.LastPathSegment;
            });
            report.Run("move", () =>
            {
                var flags = Flags(resolver, file);
                var sub = DocumentsContract.CreateDocument(resolver, probe!, Document.MimeTypeDir, "sub")!;
                if (!flags.HasFlag(DocumentContractFlags.SupportsMove))
                    throw new NotSupportedException($"provider does not offer move (flags {flags})");
                return DocumentsContract.MoveDocument(resolver, file, probe!, sub)?.LastPathSegment;
            });
            report.Run("watch", () =>
            {
                // ExternalStorageProvider watches a folder only while a cursor over its children is open,
                // and notifies through that cursor.
                var probeId = DocumentsContract.GetDocumentId(probe!)!;
                using var changed = new ManualResetEventSlim();
                using var cursor = resolver.Query(DocumentsContract.BuildChildDocumentsUriUsingTree(tree, probeId)!, [Document.ColumnDocumentId], null, null, null)
                    ?? throw new IOException("no cursor");
                var observer = new Observer(changed);
                cursor.RegisterContentObserver(observer);
                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    DocumentsContract.CreateDocument(resolver, probe!, "text/plain", "watched.txt");
                    return changed.Wait(TimeSpan.FromSeconds(3)) ? $"cursor observer called after {watch.ElapsedMilliseconds} ms" : throw new TimeoutException("no change notification in 3 s");
                }
                finally
                {
                    cursor.UnregisterContentObserver(observer);
                }
            });
            report.Run("delete", () => DocumentsContract.DeleteDocument(resolver, probe!) ? null : throw new IOException("delete returned false"));
            probe = null;
        }
        finally
        {
            if (probe is not null)
                try { DocumentsContract.DeleteDocument(resolver, probe); } catch { }
        }
        return report;
    }

    static DocumentContractFlags Flags(ContentResolver resolver, AUri document)
    {
        using var cursor = resolver.Query(document, [Document.ColumnFlags], null, null, null)!;
        return cursor.MoveToFirst() ? (DocumentContractFlags)cursor.GetInt(0) : 0;
    }

    // ExternalStorageProvider's ids are "primary:Download/x" or "1A2B-3C4D:DCIM".
    static string? NativePath(string documentId)
    {
        var colon = documentId.IndexOf(':');
        if (colon < 0)
            return null;
        var volume = documentId[..colon] == "primary" ? "/storage/emulated/0" : $"/storage/{documentId[..colon]}";
        return Path.Combine(volume, documentId[(colon + 1)..]);
    }

    sealed class Observer(ManualResetEventSlim changed) : ContentObserver(null)
    {
        public override void OnChange(bool selfChange) => changed.Set();
    }
}
#endif
