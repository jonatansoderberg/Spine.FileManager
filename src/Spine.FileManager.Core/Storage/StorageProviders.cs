namespace Spine.FileManager.Storage;

/// <summary>The registered providers, found by the provider id every location carries.</summary>
public sealed class StorageProviders(IEnumerable<IStorageProvider> providers)
{
    private readonly Dictionary<StorageProviderId, IStorageProvider> _providers = providers.ToDictionary(p => p.Id);

    public IEnumerable<IStorageProvider> All => _providers.Values;

    public IStorageProvider this[StorageLocation location] =>
        _providers.TryGetValue(location.Provider, out var provider)
            ? provider
            : throw new StorageException(StorageErrorKind.Unavailable, location, $"No provider '{location.Provider}' is registered.");
}
