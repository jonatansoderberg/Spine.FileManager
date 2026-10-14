using System.Globalization;

namespace Spine.FileManager.Storage;

/// <summary>Orders names the way people read them: "file2" before "file10", case and accents by the current culture.</summary>
public sealed class NaturalComparer(CultureInfo? culture = null) : IComparer<string>
{
    private readonly CompareInfo _compare = (culture ?? CultureInfo.CurrentCulture).CompareInfo;

    public static NaturalComparer Current => new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                var numberX = x.AsSpan(startX, i - startX).TrimStart('0');
                var numberY = y.AsSpan(startY, j - startY).TrimStart('0');
                if (numberX.Length != numberY.Length)
                    return numberX.Length.CompareTo(numberY.Length);
                var digits = numberX.SequenceCompareTo(numberY);
                if (digits != 0)
                    return Math.Sign(digits);
                // "01" and "1" are equal as numbers; the shorter run comes first so the order is stable.
                if (i - startX != j - startY)
                    return (i - startX).CompareTo(j - startY);
            }
            else
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && !char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && !char.IsAsciiDigit(y[j])) j++;
                var text = _compare.Compare(x.AsSpan(startX, i - startX), y.AsSpan(startY, j - startY), CompareOptions.IgnoreCase);
                if (text != 0)
                    return text;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
