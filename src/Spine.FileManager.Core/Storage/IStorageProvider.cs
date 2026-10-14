namespace Spine.FileManager.Storage;

public enum StorageDeleteMode { Trash, Permanent }

public enum StorageChangeKind { Created, Deleted, Changed, Renamed }

/// <summary>Something changed in a watched folder. <see cref="OldLocation"/> is set for a rename.</summary>
public sealed record StorageChange(StorageChangeKind Kind, StorageLocation Location, StorageLocation? OldLocation = null);

/// <summary>
/// One kind of storage — the local file system, an Android document tree, the photo library, another device. Every
/// call is asynchronous and cancellable and fails with <see cref="StorageException"/>.
/// </summary>
public interface IStorageProvider
{
    StorageProviderId Id { get; }

    /// <summary>The places this provider offers in the sidebar.</summary>
    Task<IReadOnlyList<StorageRoot>> GetRootsAsync(CancellationToken cancellationToken = default);

    ValueTask<StorageCapabilities> GetCapabilitiesAsync(StorageLocation location, CancellationToken cancellationToken = default);

    Task<StorageItem> GetItemAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>The folder above, or null at a root the provider has nothing above.</summary>
    StorageLocation? GetParent(StorageLocation location);

    /// <summary>A folder's children as they are read, so a large folder shows its first items at once.</summary>
    IAsyncEnumerable<StorageItem> EnumerateAsync(StorageLocation folder, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(StorageLocation file, CancellationToken cancellationToken = default);

    /// <summary>
    /// A new file in <paramref name="folder"/>. <paramref name="length"/> is the size when known; some providers (MTP)
    /// need it up front. Fails with <see cref="StorageErrorKind.AlreadyExists"/> unless <paramref name="overwrite"/>.
    /// </summary>
    Task<Stream> OpenWriteAsync(StorageLocation folder, string name, long? length = null, bool overwrite = false, CancellationToken cancellationToken = default);

    Task<StorageItem> CreateFolderAsync(StorageLocation folder, string name, CancellationToken cancellationToken = default);

    Task<StorageItem> RenameAsync(StorageLocation item, string newName, CancellationToken cancellationToken = default);

    Task DeleteAsync(StorageLocation item, StorageDeleteMode mode, CancellationToken cancellationToken = default);

    /// <summary>Copies within the provider. Only where <see cref="StorageCapabilities.NativeCopy"/>; otherwise copy through streams.</summary>
    Task<StorageItem> CopyAsync(StorageLocation item, StorageLocation targetFolder, string? newName = null, CancellationToken cancellationToken = default);

    /// <summary>Moves within the provider. Only where <see cref="StorageCapabilities.NativeMove"/>.</summary>
    Task<StorageItem> MoveAsync(StorageLocation item, StorageLocation targetFolder, string? newName = null, CancellationToken cancellationToken = default);

    /// <summary>Reports changes in <paramref name="folder"/> until disposed. Only where <see cref="StorageCapabilities.Watch"/>.</summary>
    IDisposable Watch(StorageLocation folder, Action<StorageChange> changed);
}
