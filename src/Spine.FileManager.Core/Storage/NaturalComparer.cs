using System.Globalization;

namespace Spine.FileManager.Storage;

/// <summary>Orders names the way people read them: "file2" before "file10", case and accents by the current culture.</summary>
public sealed class NaturalComparer(CultureInfo? culture = null) : IComparer<string>
{
    private readonly CompareInfo _compare = (culture ?? CultureInfo.CurrentCulture).CompareInfo;

    public static NaturalComparer Current => new();

    /// <summary>
    /// A precomputed key that orders exactly as <see cref="Compare"/> does. Sorting 100 000 names with keys took a
    /// fraction of comparing them pairwise with the culture's rules (Spike 2).
    /// </summary>
    public NaturalKey GetKey(string text) => new(text, _compare);

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

/// <summary>A name split into its text and number runs, the text as the culture's sort key.</summary>
public sealed class NaturalKey : IComparable<NaturalKey>
{
    private readonly object[] _runs;

    internal NaturalKey(string text, CompareInfo compare)
    {
        var runs = new List<object>();
        for (var i = 0; i < text.Length;)
        {
            var start = i;
            var digits = char.IsAsciiDigit(text[i]);
            while (i < text.Length && char.IsAsciiDigit(text[i]) == digits)
                i++;
            var run = text[start..i];
            runs.Add(digits ? new Number(run.TrimStart('0'), run.Length) : compare.GetSortKey(run, CompareOptions.IgnoreCase));
        }
        _runs = [.. runs];
    }

    public int CompareTo(NaturalKey? other)
    {
        if (other is null)
            return 1;
        for (var k = 0; k < _runs.Length && k < other._runs.Length; k++)
        {
            var order = (_runs[k], other._runs[k]) switch
            {
                (Number x, Number y) => x.CompareTo(y),
                (SortKey x, SortKey y) => SortKey.Compare(x, y),
                // A number against text: the number sorts first, as an empty text run would.
                (Number, _) => -1,
                _ => 1,
            };
            if (order != 0)
                return order;
        }
        return _runs.Length.CompareTo(other._runs.Length);
    }

    private sealed record Number(string Digits, int Length) : IComparable<Number>
    {
        public int CompareTo(Number? other)
        {
            if (Digits.Length != other!.Digits.Length)
                return Digits.Length.CompareTo(other.Digits.Length);
            var digits = string.CompareOrdinal(Digits, other.Digits);
            return digits != 0 ? Math.Sign(digits) : Length.CompareTo(other.Length);
        }
    }
}
