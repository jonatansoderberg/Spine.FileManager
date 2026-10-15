using Spine.FileManager.Storage.FileSystem;

namespace Spine.FileManager.Tests.Storage;

public sealed class FolderTrashTests : IDisposable
{
    private readonly string _trash = Directory.CreateTempSubdirectory("trash-").FullName;

    public void Dispose() => Directory.Delete(_trash, recursive: true);

    [Fact]
    public void Measure_counts_top_level_items_and_the_bytes_inside_folders()
    {
        File.WriteAllBytes(Path.Combine(_trash, "a.bin"), new byte[100]);
        Directory.CreateDirectory(Path.Combine(_trash, "folder", "deeper"));
        File.WriteAllBytes(Path.Combine(_trash, "folder", "deeper", "b.bin"), new byte[50]);
        File.WriteAllBytes(Path.Combine(_trash, ".DS_Store"), new byte[10]);

        Assert.Equal(new TrashContents(2, 150), FolderTrash.Measure(_trash));
    }

    [Fact]
    public void Measure_is_null_for_a_folder_the_app_cannot_read() =>
        Assert.Null(FolderTrash.Measure(Path.Combine(_trash, "missing")));

    [Fact]
    public void Empty_deletes_links_but_not_what_they_point_to()
    {
        var outside = Directory.CreateTempSubdirectory("kept-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "kept");
            Directory.CreateSymbolicLink(Path.Combine(_trash, "link"), outside);
            Directory.CreateDirectory(Path.Combine(_trash, "folder"));
            File.WriteAllText(Path.Combine(_trash, "folder", "x.txt"), "x");
            var progress = new List<double>();

            var failed = FolderTrash.Empty(_trash, new SyncProgress(progress.Add));

            Assert.Empty(failed);
            Assert.Empty(Directory.GetFileSystemEntries(_trash));
            Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
            Assert.Equal(1, progress[^1]);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
