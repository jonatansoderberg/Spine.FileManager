using System.Globalization;
using Spine.FileManager.Explorer;

namespace Spine.FileManager.Tests.Explorer;

public sealed class NameFilterTests
{
    [Theory]
    [InlineData("Résumé.pdf", "resume", true)]
    [InlineData("Annual REPORT 2026.xlsx", "report 20", true)]
    [InlineData("photo.jpg", "  ", true)]
    [InlineData("photo.jpg", "png", false)]
    [InlineData("notes.txt", " notes ", true)]
    public void Matches_by_contained_text_ignoring_case_and_accents(string name, string filter, bool matches) =>
        Assert.Equal(matches, NameFilter.Matches(name, filter, CultureInfo.InvariantCulture));
}
