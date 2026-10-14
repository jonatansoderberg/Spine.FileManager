namespace Spine.FileManager.Storage;

/// <summary>What a file or folder may be called, checked the same everywhere so a name valid on one OS is not lost on another.</summary>
public static class StorageNames
{
    // Windows' rules are the strictest; following them keeps files copyable to every platform.
    private static readonly char[] Invalid = ['/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0'];
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool IsValid(string? name) => Problem(name) is null;

    /// <summary>Why <paramref name="name"/> is not allowed, or null when it is.</summary>
    public static StorageNameProblem? Problem(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return StorageNameProblem.Empty;
        if (name is "." or "..")
            return StorageNameProblem.Reserved;
        if (name.IndexOfAny(Invalid) >= 0 || name.Any(char.IsControl))
            return StorageNameProblem.InvalidCharacter;
        if (name.EndsWith(' ') || name.EndsWith('.'))
            return StorageNameProblem.TrailingSpaceOrDot;
        if (Reserved.Contains(Path.GetFileNameWithoutExtension(name)))
            return StorageNameProblem.Reserved;
        if (name.Length > 255)
            return StorageNameProblem.TooLong;
        return null;
    }

    /// <summary>"name (2).ext", "name (3).ext" … — the first name <paramref name="exists"/> says is free.</summary>
    public static string Unique(string name, Func<string, bool> exists)
    {
        if (!exists(name))
            return name;
        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem} ({n}){extension}";
            if (!exists(candidate))
                return candidate;
        }
    }
}

public enum StorageNameProblem { Empty, InvalidCharacter, TrailingSpaceOrDot, Reserved, TooLong }
