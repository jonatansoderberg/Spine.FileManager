#if IOS || MACCATALYST
using System.Diagnostics;
using Foundation;
using Photos;

namespace FileSystemSpike;

/// <summary>The photo library as its own provider: what a backup could list and stream.</summary>
public static class PhotosProbe
{
    public static async Task<LocationReport> RunAsync()
    {
        var report = new LocationReport("Photo Library", "PHPhotoLibrary", "PhotoKit");
        if (!await report.RunAsync("authorize", async () =>
            {
                var status = await PHPhotoLibrary.RequestAuthorizationAsync(PHAccessLevel.ReadWrite);
                return status is PHAuthorizationStatus.Authorized or PHAuthorizationStatus.Limited ? status.ToString() : throw new UnauthorizedAccessException(status.ToString());
            }))
            return report;

        report.Steps.Add(new("exists", Outcome.Ok, 0, null));
        PHFetchResult? all = null;
        report.Run("list", () =>
        {
            all = PHAsset.FetchAssets((PHFetchOptions?)null);
            var albums = PHAssetCollection.FetchAssetCollections(PHAssetCollectionType.Album, PHAssetCollectionSubtype.Any, null).Count;
            var smart = PHAssetCollection.FetchAssetCollections(PHAssetCollectionType.SmartAlbum, PHAssetCollectionSubtype.Any, null).Count;
            return $"{all.Count} assets ({PHAsset.FetchAssets(PHAssetMediaType.Image, null).Count} images, {PHAsset.FetchAssets(PHAssetMediaType.Video, null).Count} videos), {albums} albums, {smart} smart albums";
        });

        await report.RunAsync("read", async () =>
        {
            if (all is not { Count: > 0 })
                throw new SkipException("no assets");
            var asset = (PHAsset)all.LastObject!;
            var resources = PHAssetResource.GetAssetResources(asset);
            var original = resources.First();
            var done = new TaskCompletionSource<NSError?>();
            long bytes = 0;
            int chunks = 0;
            var watch = Stopwatch.StartNew();
            PHAssetResourceManager.DefaultManager.RequestData(original, new PHAssetResourceRequestOptions { NetworkAccessAllowed = true },
                data => { bytes += (long)data.Length; chunks++; },
                error => done.TrySetResult(error));
            var error = await done.Task;
            if (error is not null)
                throw new IOException(error.LocalizedDescription);
            var created = asset.CreationDate is { } date ? ((DateTime)date).ToString("yyyy-MM-dd") : "?";
            return $"{original.OriginalFilename} ({original.UniformTypeIdentifier}, {created}): {bytes:N0} bytes in {chunks} chunks, {watch.ElapsedMilliseconds} ms; resources: {string.Join(", ", resources.Select(r => r.ResourceType))}";
        });
        return report;
    }
}
#endif
