namespace Spine.FileManager.Services;

/// <summary>
/// The system's own picture of a file: Quick Look thumbnails on Apple platforms (PDF pages, images, Office, video),
/// the shell's thumbnails on Windows. Null where there is none.
/// </summary>
public static class Thumbnails
{
    public static async Task<ImageSource?> GetAsync(string path, int size)
    {
#if MACCATALYST || IOS
        // Scale 2 rather than UIScreen's: this runs off the UI thread, where UIKit may not be asked.
        var request = new QuickLookThumbnailing.QLThumbnailGenerationRequest(Foundation.NSUrl.FromFilename(path), new CoreGraphics.CGSize(size, size),
            2, QuickLookThumbnailing.QLThumbnailGenerationRequestRepresentationTypes.All);
        var done = new TaskCompletionSource<UIKit.UIImage?>();
        QuickLookThumbnailing.QLThumbnailGenerator.SharedGenerator.GenerateBestRepresentation(request, (thumbnail, error) =>
        {
            if (error is not null)
                System.Diagnostics.Debug.WriteLine($"[Thumbnails] {path}: {error.LocalizedDescription}");
            done.TrySetResult(thumbnail?.UIImage);
        });
        if (await done.Task.WaitAsync(TimeSpan.FromSeconds(5)) is not { } image || image.AsPNG() is not { } png)
            return null;
        var bytes = png.ToArray();
        return ImageSource.FromStream(() => new MemoryStream(bytes));
#elif WINDOWS
        try
        {
            Windows.Storage.IStorageItemProperties item = Directory.Exists(path)
                ? await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path)
                : await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await item.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, (uint)size);
            if (thumbnail is null)
                return null;
            using var stream = thumbnail.AsStreamForRead();
            var bytes = new byte[thumbnail.Size];
            await stream.ReadExactlyAsync(bytes);
            return ImageSource.FromStream(() => new MemoryStream(bytes));
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
