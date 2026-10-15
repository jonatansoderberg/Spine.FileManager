namespace Spine.FileManager.Storage;

/// <summary>
/// What a location allows. Capabilities belong to the location, not the provider: the same provider may write in one
/// folder and not in another. Copy and move are always possible through streams when Read and Write are;
/// <see cref="NativeCopy"/> and <see cref="NativeMove"/> say the provider does them itself.
/// </summary>
[Flags]
public enum StorageCapabilities
{
    None = 0,
    List = 1 << 0,
    Read = 1 << 1,
    Write = 1 << 2,
    CreateFolder = 1 << 3,
    Rename = 1 << 4,
    Delete = 1 << 5,
    NativeCopy = 1 << 6,
    NativeMove = 1 << 7,
    Watch = 1 << 8,
    RandomAccess = 1 << 9,
    NativePath = 1 << 10,
    PersistentGrant = 1 << 11,
    Trash = 1 << 12,

    /// <summary>The system asks before it deletes (the photo library), so the app does not ask as well.</summary>
    SystemConfirmsDelete = 1 << 13,

    Modify = Write | CreateFolder | Rename | Delete | NativeCopy | NativeMove | Trash,
}
