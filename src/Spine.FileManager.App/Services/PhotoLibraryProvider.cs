#if IOS || MACCATALYST
using System.Runtime.CompilerServices;
using Foundation;
using Photos;
using Plugin.Maui.Spine.Common;

namespace Spine.FileManager.Services;

/// <summary>
/// The photo library (PhotoKit) as one folder of files, newest first: photos and videos under their original names.
/// Reading exports the original. Writing stages the file and imports it when the operation queue renames it to its
/// final name, so copies into the library go through the queue like any other. Deleting moves to Photos' Recently
/// Deleted, after the system's own question.
/// </summary>
public sealed class PhotoLibraryProvider(ISpineStrings strings) : IStorageProvider, IDeletesTogether
{
    public static readonly StorageProviderId ProviderId = new("photos");

    private const string StagedPrefix = "staged/";

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { "mov", "mp4", "m4v", "avi", "3gp" };

    private static readonly string Staging = Path.Combine(AppPaths.Data, "photo-library");

    public StorageProviderId Id => ProviderId;

    public StorageLocation Root => new(ProviderId, "", strings["Photos.Library"]);

    public Task<IReadOnlyList<StorageRoot>> GetRootsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StorageRoot>>([new StorageRoot(Root, strings["Photos.Library"], StorageRootKind.PhotoLibrary)]);

    public ValueTask<StorageCapabilities> GetCapabilitiesAsync(StorageLocation location, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(location.ItemId.Length == 0
            ? StorageCapabilities.List | StorageCapabilities.Write | StorageCapabilities.Delete | StorageCapabilities.Trash | StorageCapabilities.SystemConfirmsDelete
            : StorageCapabilities.Read | StorageCapabilities.Delete | StorageCapabilities.Trash | StorageCapabilities.SystemConfirmsDelete);

    public StorageLocation? GetParent(StorageLocation location) => location.ItemId.Length == 0 ? null : Root;

    public async Task<StorageItem> GetItemAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        if (location.ItemId.Length == 0)
            return new StorageItem(Root, strings["Photos.Library"], StorageItemKind.Folder);
        if (Staged(location) is { } path)
            return File.Exists(path)
                ? new StorageItem(location, Path.GetFileName(path), StorageItemKind.File) { Size = new FileInfo(path).Length }
                : throw new StorageException(StorageErrorKind.NotFound, location, $"{location.DisplayPath} is not there.");
        await Authorize(location);
        var asset = Asset(location);
        var resource = Original(asset);
        return Item(asset, resource.OriginalFilename) with { Size = (resource.ValueForKey(new NSString("fileSize")) as NSNumber)?.Int64Value };
    }

    public async IAsyncEnumerable<StorageItem> EnumerateAsync(StorageLocation folder, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (folder.ItemId.Length != 0)
            throw new StorageException(StorageErrorKind.NotSupported, folder, $"{folder.DisplayPath} is not a folder.");
        await Authorize(folder);
        // Files being copied in show until they are imported, so the queue finds its part file.
        if (Directory.Exists(Staging))
            foreach (var path in Directory.GetFiles(Staging))
                yield return new StorageItem(Location(StagedPrefix + Path.GetFileName(path), Path.GetFileName(path)), Path.GetFileName(path), StorageItemKind.File)
                {
                    Size = new FileInfo(path).Length,
                };
        var options = new PHFetchOptions { SortDescriptors = [new NSSortDescriptor("creationDate", false)] };
        var assets = PHAsset.FetchAssets(options);
        for (nint i = 0; i < assets.Count; i++)
        {
            if (i % 256 == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (assets[i] is not PHAsset asset)
                continue;
            yield return Item(asset, Name(asset));
        }
    }

    public async Task<Stream> OpenReadAsync(StorageLocation file, CancellationToken cancellationToken = default)
    {
        if (Staged(file) is { } staged)
            return File.OpenRead(staged);
        await Authorize(file);
        var resource = Original(Asset(file));
        var export = Path.Combine(Path.GetTempPath(), $"spinefm-{Guid.NewGuid():N}{Path.GetExtension(resource.OriginalFilename)}");
        var done = new TaskCompletionSource<NSError?>();
        // Photos not on this device (iCloud) are downloaded first.
        PHAssetResourceManager.DefaultManager.WriteData(resource, NSUrl.FromFilename(export), new PHAssetResourceRequestOptions { NetworkAccessAllowed = true }, error => done.TrySetResult(error));
        if (await done.Task.WaitAsync(cancellationToken) is { } failed)
            throw new StorageException(StorageErrorKind.Other, file, $"Photos could not export {file.DisplayPath}: {failed.LocalizedDescription}");
        return new FileStream(export, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
    }

    public async Task<Stream> OpenWriteAsync(StorageLocation folder, string name, long? length = null, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        if (folder.ItemId.Length != 0)
            throw new StorageException(StorageErrorKind.NotSupported, folder, "Photos go into the library itself.");
        await Authorize(folder);
        Directory.CreateDirectory(Staging);
        var path = Path.Combine(Staging, name);
        if (File.Exists(path) && !overwrite)
            throw new StorageException(StorageErrorKind.AlreadyExists, folder, $"{name} is already being added.");
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
    }

    /// <summary>Renaming a staged file to its final name imports it into the library under that name.</summary>
    public async Task<StorageItem> RenameAsync(StorageLocation item, string newName, CancellationToken cancellationToken = default)
    {
        if (Staged(item) is not { } path)
            throw new StorageException(StorageErrorKind.NotSupported, item, "Photos cannot be renamed in the library.");
        string? id = null;
        var type = VideoExtensions.Contains(Path.GetExtension(newName).TrimStart('.')) ? PHAssetResourceType.Video : PHAssetResourceType.Photo;
        var (saved, error) = await PerformChanges(() =>
        {
            var request = PHAssetCreationRequest.CreationRequestForAsset();
            request.AddResource(type, NSUrl.FromFilename(path), new PHAssetResourceCreationOptions { OriginalFilename = newName });
            id = request.PlaceholderForCreatedAsset?.LocalIdentifier;
        });
        if (!saved || id is null)
            throw new StorageException(StorageErrorKind.Other, item, $"Photos could not add {newName}: {error?.LocalizedDescription}");
        File.Delete(path);
        return Item(Asset(Location(id, newName)), newName);
    }

    public Task DeleteAsync(StorageLocation item, StorageDeleteMode mode, CancellationToken cancellationToken = default) =>
        DeleteAsync([item], mode, cancellationToken);

    // One change for all of them: the system asks once, and the photos go to Recently Deleted whatever the mode.
    public async Task DeleteAsync(IReadOnlyList<StorageLocation> items, StorageDeleteMode mode, CancellationToken cancellationToken = default)
    {
        foreach (var path in items.Select(Staged).OfType<string>())
            File.Delete(path);
        var ids = items.Where(i => Staged(i) is null && i.ItemId.Length > 0).Select(i => i.ItemId).ToArray();
        if (ids.Length == 0)
            return;
        await Authorize(items[0]);
        var fetched = PHAsset.FetchAssetsUsingLocalIdentifiers(ids, null);
        var assets = Enumerable.Range(0, (int)fetched.Count).Select(i => fetched[i]).OfType<PHAsset>().ToArray();
        var (deleted, error) = await PerformChanges(() => PHAssetChangeRequest.DeleteAssets(assets));
        if (!deleted)
            throw new StorageException(StorageErrorKind.Other, items[0], $"Photos deleted nothing: {error?.LocalizedDescription}");
    }

    public Task<StorageItem> CreateFolderAsync(StorageLocation folder, string name, CancellationToken cancellationToken = default) =>
        throw new StorageException(StorageErrorKind.NotSupported, folder, "The photo library has no folders.");

    public Task<StorageItem> CopyAsync(StorageLocation item, StorageLocation targetFolder, string? newName = null, CancellationToken cancellationToken = default) =>
        throw new StorageException(StorageErrorKind.NotSupported, item, "Photos are copied through the queue.");

    public Task<StorageItem> MoveAsync(StorageLocation item, StorageLocation targetFolder, string? newName = null, CancellationToken cancellationToken = default) =>
        throw new StorageException(StorageErrorKind.NotSupported, item, "Photos are moved through the queue.");

    public IDisposable Watch(StorageLocation folder, Action<StorageChange> changed) => new Unwatched();

    /// <summary>
    /// A picture of the photo or video at most <paramref name="points"/> on its longer side, made by Photos (and fetched
    /// from iCloud when needed), or null for a staged file or one Photos cannot draw.
    /// </summary>
    public static async Task<ImageSource?> ThumbnailAsync(StorageLocation location, double points)
    {
        if (Staged(location) is not null || PHAsset.FetchAssetsUsingLocalIdentifiers([location.ItemId], null).FirstObject is not PHAsset asset)
            return null;
        var done = new TaskCompletionSource<UIKit.UIImage?>();
        var pixels = points * 2;
        PHImageManager.DefaultManager.RequestImageForAsset(asset, new CoreGraphics.CGSize(pixels, pixels), PHImageContentMode.AspectFit,
            new PHImageRequestOptions
            {
                DeliveryMode = PHImageRequestOptionsDeliveryMode.HighQualityFormat,
                ResizeMode = PHImageRequestOptionsResizeMode.Fast,
                NetworkAccessAllowed = true,
            },
            (image, _) => done.TrySetResult(image));
        if (await done.Task is not { } picture || picture.AsJPEG((System.Runtime.InteropServices.NFloat)0.85)?.ToArray() is not { } bytes)
            return null;
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    private StorageLocation Location(string id, string name) => new(ProviderId, id, $"{strings["Photos.Library"]}/{name}");

    private StorageItem Item(PHAsset asset, string name)
    {
        var created = asset.CreationDate is { } c ? new DateTimeOffset((DateTime)c) : (DateTimeOffset?)null;
        return new StorageItem(Location(asset.LocalIdentifier, name), name, StorageItemKind.File)
        {
            Created = created,
            Modified = asset.ModificationDate is { } m ? new DateTimeOffset((DateTime)m) : created,
            IsHidden = asset.Hidden,
        };
    }

    private static readonly ObjCRuntime.Selector OriginalFilename = new("originalFilename");

    // The name the photo came with (IMG_1234.HEIC), read from the asset itself where it answers to it: asking for its
    // resources costs a lookup per photo, and "filename" is the library's own name for its copy (a UUID).
    private static string Name(PHAsset asset) =>
        (asset.RespondsToSelector(OriginalFilename) ? asset.ValueForKey(new NSString("originalFilename"))?.ToString() : null) is { Length: > 0 } name
            ? name
            : Original(asset).OriginalFilename;

    private static string? Staged(StorageLocation location) =>
        location.ItemId.StartsWith(StagedPrefix, StringComparison.Ordinal) ? Path.Combine(Staging, location.ItemId[StagedPrefix.Length..]) : null;

    private static PHAsset Asset(StorageLocation location) =>
        PHAsset.FetchAssetsUsingLocalIdentifiers([location.ItemId], null).FirstObject as PHAsset
            ?? throw new StorageException(StorageErrorKind.NotFound, location, $"{location.DisplayPath} is no longer in the library.");

    // The photo or video as taken or imported; an edit is a resource of its own beside it.
    private static PHAssetResource Original(PHAsset asset)
    {
        var resources = PHAssetResource.GetAssetResources(asset);
        return resources.FirstOrDefault(r => r.ResourceType is PHAssetResourceType.Photo or PHAssetResourceType.Video) ?? resources[0];
    }

    // Asked once; a denied or restricted library fails as denied access, which the explorer shows.
    private static async Task Authorize(StorageLocation location)
    {
        var status = PHPhotoLibrary.GetAuthorizationStatus(PHAccessLevel.ReadWrite);
        if (status == PHAuthorizationStatus.NotDetermined)
            status = await PHPhotoLibrary.RequestAuthorizationAsync(PHAccessLevel.ReadWrite);
        if (status is not (PHAuthorizationStatus.Authorized or PHAuthorizationStatus.Limited))
            throw new StorageException(StorageErrorKind.AccessDenied, location, "Spine File Manager may not open the photo library. Allow it in System Settings › Privacy & Security › Photos.");
    }

    private static Task<(bool Done, NSError? Error)> PerformChanges(Action changes)
    {
        var done = new TaskCompletionSource<(bool, NSError?)>();
        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(changes, (success, error) => done.TrySetResult((success, error)));
        return done.Task;
    }

    private sealed class Unwatched : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
#endif
