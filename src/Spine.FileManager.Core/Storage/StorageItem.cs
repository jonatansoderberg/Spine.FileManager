namespace Spine.FileManager.Storage;

public enum StorageItemKind { File, Folder }

/// <summary>
/// One entry in a folder. Name and kind are always there; size, dates and flags are filled in when the provider gets
/// them cheaply with the listing, and are null otherwise.
/// </summary>
public sealed record StorageItem(StorageLocation Location, string Name, StorageItemKind Kind)
{
    /// <summary>What the user sees: Finder's localised folder names ("Skrivbord" for Desktop) or the name itself.</summary>
    public string DisplayName { get => field ?? Name; init; }

    public long? Size { get; init; }
    public DateTimeOffset? Modified { get; init; }
    public DateTimeOffset? Created { get; init; }
    public bool IsHidden { get; init; }
    public bool IsReadOnly { get; init; }
    public bool IsLink { get; init; }

    public bool IsFolder => Kind == StorageItemKind.Folder;

    /// <summary>The part after the last dot, without the dot, or empty for folders and names without one.</summary>
    public string Extension => IsFolder ? "" : System.IO.Path.GetExtension(Name).TrimStart('.');
}

public enum StorageRootKind
{
    Home, Desktop, Documents, Downloads, Pictures, Music, Videos,
    Volume, RemovableVolume, NetworkVolume, Cloud, AppData, Other,
}

/// <summary>A place the sidebar offers: a known folder, a volume, a cloud folder.</summary>
public sealed record StorageRoot(StorageLocation Location, string Name, StorageRootKind Kind);
