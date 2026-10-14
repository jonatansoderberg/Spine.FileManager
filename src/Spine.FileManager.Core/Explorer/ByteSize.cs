using System.Globalization;

namespace Spine.FileManager.Explorer;

/// <summary>File sizes as people read them, in the decimal units Finder and modern Explorer use.</summary>
public static class ByteSize
{
    private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (bytes < 1000)
            return string.Format(culture, "{0:N0} {1}", bytes, bytes == 1 ? "byte" : "bytes");
        double value = bytes;
        var unit = 0;
        while (value >= 999.95 && unit < Units.Length - 1)
        {
            value /= 1000;
            unit++;
        }
        return string.Format(culture, value < 10 ? "{0:0.#} {1}" : "{0:0} {1}", value, Units[unit]);
    }
}
