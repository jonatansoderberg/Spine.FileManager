namespace FileSystemSpike;

/// <summary>Runs every step against a folder through System.IO, writing only inside a probe folder it deletes again.</summary>
public static class PathProbe
{
    const int WriteSize = 1024 * 1024;

    public static LocationReport Run(string name, string path, bool write, string via = "System.IO")
    {
        var report = new LocationReport(name, path, via);
        Probe(report, path, write);
        return report;
    }

    public static void Probe(LocationReport report, string path, bool write)
    {
        if (!report.Run("exists", () => Directory.Exists(path) ? null : throw new DirectoryNotFoundException(path)))
            return;

        report.Run("volume", () =>
        {
            var drive = DriveInfo.GetDrives()
                .Where(d => path.StartsWith(d.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase))
                .MaxBy(d => d.RootDirectory.FullName.Length) ?? throw new DriveNotFoundException();
            return $"{drive.RootDirectory.FullName} {drive.DriveFormat} {drive.DriveType}, {Gb(drive.AvailableFreeSpace)} free of {Gb(drive.TotalSize)}";
        });

        string? sample = null;
        report.Run("list", () =>
        {
            int folders = 0, files = 0;
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo)
                    folders++;
                else
                {
                    files++;
                    sample ??= entry.FullName;
                }
            }
            return $"{folders} folders, {files} files";
        });

        report.Run("stat", () => sample is null ? throw new SkipException("no file to stat")
            : new FileInfo(sample) is var info ? $"{info.Name}: {info.Length:N0} bytes, modified {info.LastWriteTime:yyyy-MM-dd HH:mm}, attributes {info.Attributes}" : null);

        report.Run("read", () =>
        {
            if (sample is null)
                throw new SkipException("no file to read");
            using var stream = File.OpenRead(sample);
            var buffer = new byte[64 * 1024];
            return $"{stream.Read(buffer, 0, buffer.Length):N0} bytes of {Path.GetFileName(sample)}";
        });

        if (!write)
        {
            report.Notes.Add("Write steps not run here.");
            return;
        }

        var probe = Path.Combine(path, $".spine-probe-{Guid.NewGuid().ToString("N")[..8]}");
        try
        {
            if (!report.Run("create-folder", () => { Directory.CreateDirectory(probe); return null; }))
                return;

            var a = Path.Combine(probe, "a.bin");
            var data = new byte[WriteSize];
            Random.Shared.NextBytes(data);
            report.Run("write", () =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                File.WriteAllBytes(a, data);
                return $"1 MB at {WriteSize / 1048576.0 / watch.Elapsed.TotalSeconds:0} MB/s";
            });
            report.Run("random-access", () =>
            {
                using var stream = new FileStream(a, FileMode.Open, FileAccess.ReadWrite);
                stream.Seek(512 * 1024, SeekOrigin.Begin);
                var buffer = new byte[4096];
                stream.ReadExactly(buffer);
                stream.Seek(-4096, SeekOrigin.Current);
                stream.Write(buffer);
                return buffer.AsSpan().SequenceEqual(data.AsSpan(512 * 1024, 4096)) ? "seek, read and write in place" : throw new IOException("read back other bytes");
            });
            var b = Path.Combine(probe, "b.bin");
            report.Run("rename", () => { File.Move(a, b); return null; });
            var c = Path.Combine(probe, "c.bin");
            report.Run("copy", () => { File.Copy(b, c); return new FileInfo(c).Length == WriteSize ? null : throw new IOException("copy has the wrong size"); });
            report.Run("move", () =>
            {
                var sub = Directory.CreateDirectory(Path.Combine(probe, "sub")).FullName;
                File.Move(c, Path.Combine(sub, "c.bin"));
                return null;
            });
            report.Run("watch", () =>
            {
                using var watcher = new FileSystemWatcher(probe);
                using var created = new ManualResetEventSlim();
                watcher.Created += (_, _) => created.Set();
                watcher.EnableRaisingEvents = true;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                File.WriteAllText(Path.Combine(probe, "watched.txt"), "x");
                return created.Wait(TimeSpan.FromSeconds(3)) ? $"event after {watch.ElapsedMilliseconds} ms" : throw new TimeoutException("no event in 3 s");
            });
            report.Run("delete", () =>
            {
                Directory.Delete(probe, recursive: true);
                return Directory.Exists(probe) ? throw new IOException("still there") : null;
            });
        }
        finally
        {
            try
            {
                if (Directory.Exists(probe))
                    Directory.Delete(probe, recursive: true);
            }
            catch (Exception e)
            {
                report.Notes.Add($"Could not remove {probe}: {e.Message}");
            }
        }
    }

    static string Gb(long bytes) => $"{bytes / 1e9:0.#} GB";
}
