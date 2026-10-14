namespace Spine.FileManager.Storage;

public enum StorageErrorKind
{
    NotFound,
    AccessDenied,
    AlreadyExists,
    NotSupported,
    InvalidName,
    /// <summary>The device, volume or connection is not there now (offline, unplugged, timed out).</summary>
    Unavailable,
    /// <summary>A persisted grant no longer opens the location; the user has to pick it again.</summary>
    AccessLost,
    Other,
}

/// <summary>A failed storage call, in terms the UI can act on rather than a platform exception.</summary>
public sealed class StorageException(StorageErrorKind kind, StorageLocation? location, string message, Exception? inner = null)
    : IOException(message, inner)
{
    public StorageErrorKind Kind { get; } = kind;
    public StorageLocation? Location { get; } = location;
}
