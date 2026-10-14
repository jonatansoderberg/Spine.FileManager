namespace Spine.FileManager.Storage;

/// <summary>Names a provider: <c>local</c>, <c>saf</c>, <c>photos</c>, <c>remote:&lt;node&gt;</c>.</summary>
public readonly record struct StorageProviderId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Where an item is: the provider and the provider's own id for it (a path, a document id, a bookmark, an asset id).
/// Two locations are the same item when provider and id match; <see cref="DisplayPath"/> and <see cref="NativePath"/>
/// only describe it.
/// </summary>
public sealed record StorageLocation(StorageProviderId Provider, string ItemId, string DisplayPath, string? NativePath = null)
{
    public bool Equals(StorageLocation? other) =>
        other is not null && Provider == other.Provider && string.Equals(ItemId, other.ItemId, StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Provider, StringComparer.Ordinal.GetHashCode(ItemId));

    public override string ToString() => $"{Provider}:{DisplayPath}";
}
