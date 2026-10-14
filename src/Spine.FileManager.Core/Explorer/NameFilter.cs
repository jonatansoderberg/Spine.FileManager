using System.Globalization;

namespace Spine.FileManager.Explorer;

/// <summary>The current folder's filter: a name matches when it contains the text, case and accents ignored.</summary>
public static class NameFilter
{
    public static bool Matches(string name, string? filter, CultureInfo? culture = null) =>
        string.IsNullOrWhiteSpace(filter)
        || (culture ?? CultureInfo.CurrentCulture).CompareInfo.IndexOf(name, filter.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
}
