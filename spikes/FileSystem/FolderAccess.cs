namespace FileSystemSpike;

/// <summary>A folder the user picks, the grant kept across launches, and the probe through it.</summary>
public static partial class FolderAccess
{
    public static partial Task<IReadOnlyList<LocationReport>> PickAndProbeAsync();

    public static partial Task<IReadOnlyList<LocationReport>> ProbeGrantedAsync();

    public static void Forget() => Preferences.Remove(Key);

    const string Key = "spike.granted";

    static List<string> Load() => [.. Preferences.Get(Key, "").Split('\n', StringSplitOptions.RemoveEmptyEntries)];

    static void Save(IEnumerable<string> entries) => Preferences.Set(Key, string.Join('\n', entries));
}
