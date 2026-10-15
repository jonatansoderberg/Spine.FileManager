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

    /// <summary>
    /// A folder the system shows as one file, as the Mac shows an app or a bundle: it opens rather than being browsed
    /// into. It is still a folder to copy and move.
    /// </summary>
    public bool IsPackage { get; init; }

    public bool IsFolder => Kind == StorageItemKind.Folder;

    /// <summary>A folder to browse into: a folder that is not a package.</summary>
    public bool IsBrowsable => IsFolder && !IsPackage;

    /// <summary>The part after the last dot, without the dot, or empty for folders (not packages) and names without one.</summary>
    public string Extension => IsBrowsable ? "" : System.IO.Path.GetExtension(Name).TrimStart('.');
}

public enum StorageRootKind
{
    Home, Desktop, Documents, Downloads, Pictures, Music, Videos,
    Volume, RemovableVolume, NetworkVolume, Cloud, AppData, Other,
}

/// <summary>A place the sidebar offers: a known folder, a volume, a cloud folder.</summary>
public sealed record StorageRoot(StorageLocation Location, string Name, StorageRootKind Kind);
