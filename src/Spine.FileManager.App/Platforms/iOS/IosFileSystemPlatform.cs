using Spine.FileManager.Storage;
using Spine.FileManager.Storage.FileSystem;

namespace Spine.FileManager;

/// <summary>
/// The phone's places: the app's own Documents (what the Files app shows as On My iPhone › Spine File Manager, where
/// received files land) and the folders the user gave it (<see cref="FolderGrants"/>). The sandbox's root and "/" are
/// left out: nothing outside them can be read.
/// </summary>
public sealed class IosFileSystemPlatform : DefaultFileSystemPlatform
{
    public override IReadOnlyList<(string Path, string Name, StorageRootKind Kind)> GetRoots() =>
        [(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Spine File Manager", StorageRootKind.Documents), .. FolderGrants.Roots];
}
