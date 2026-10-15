namespace Spine.FileManager.Services;

/// <summary>
/// The system's own picture of a file: Quick Look thumbnails on Apple platforms (PDF pages, images, Office, video),
/// the shell's thumbnails on Windows. Null where there is none.
/// </summary>
public static class Thumbnails
{
    public static async Task<ImageSource?> GetAsync(string path, int size) => Source(await GetBytesAsync(path, size, icon: false));

    /// <summary>The icon the system shows for the file (Finder's, Explorer's), rather than a picture of its content.</summary>
    public static async Task<ImageSource?> GetIconAsync(string path, int size) => Source(await GetBytesAsync(path, size, icon: true));

    /// <summary>The picture as PNG bytes, for a cache that hands each view a source of its own.</summary>
    public static Task<byte[]?> GetBytesAsync(string path, int size) => GetBytesAsync(path, size, icon: false);

    private static ImageSource? Source(byte[]? bytes) => bytes is null ? null : ImageSource.FromStream(() => new MemoryStream(bytes));

    private static async Task<byte[]?> GetBytesAsync(string path, int size, bool icon)
    {
#if MACCATALYST || IOS
        // Scale 2 rather than UIScreen's: this runs off the UI thread, where UIKit may not be asked.
        var request = new QuickLookThumbnailing.QLThumbnailGenerationRequest(Foundation.NSUrl.FromFilename(path), new CoreGraphics.CGSize(size, size),
            2, icon ? QuickLookThumbnailing.QLThumbnailGenerationRequestRepresentationTypes.Icon : QuickLookThumbnailing.QLThumbnailGenerationRequestRepresentationTypes.All);
        var done = new TaskCompletionSource<UIKit.UIImage?>();
        QuickLookThumbnailing.QLThumbnailGenerator.SharedGenerator.GenerateBestRepresentation(request, (thumbnail, error) =>
        {
            if (error is not null)
                System.Diagnostics.Debug.WriteLine($"[Thumbnails] {path}: {error.LocalizedDescription}");
            done.TrySetResult(thumbnail?.UIImage);
        });
        return await done.Task.WaitAsync(TimeSpan.FromSeconds(5)) is { } image ? image.AsPNG()?.ToArray() : null;
#elif WINDOWS
        try
        {
            Windows.Storage.IStorageItemProperties item = Directory.Exists(path)
                ? await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path)
                : await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await item.GetThumbnailAsync(icon ? Windows.Storage.FileProperties.ThumbnailMode.ListView : Windows.Storage.FileProperties.ThumbnailMode.SingleItem, (uint)size);
            if (thumbnail is null)
                return null;
            using var stream = thumbnail.AsStreamForRead();
            var bytes = new byte[thumbnail.Size];
            await stream.ReadExactlyAsync(bytes);
            return bytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}
