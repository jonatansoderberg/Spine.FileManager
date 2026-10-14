using System.Globalization;
using Spine.FileManager.Storage;

namespace Spine.FileManager.Tests.Storage;

public sealed class StorageNamesTests
{
    [Theory]
    [InlineData("report.pdf", true)]
    [InlineData("Ärenden 2026", true)]
    [InlineData(".gitignore", true)]
    [InlineData("a:b", false)]
    [InlineData("tab\there", false)]
    [InlineData("trailing ", false)]
    [InlineData("nul.txt", false)]
    [InlineData("   ", false)]
    public void Validates_names(string name, bool valid) => Assert.Equal(valid, StorageNames.IsValid(name));

    [Fact]
    public void Finds_the_first_free_name()
    {
        var taken = new HashSet<string> { "photo.jpg", "photo (2).jpg" };
        Assert.Equal("photo (3).jpg", StorageNames.Unique("photo.jpg", taken.Contains));
        Assert.Equal("other.jpg", StorageNames.Unique("other.jpg", taken.Contains));
    }

    [Fact]
    public void Sorts_numbers_by_value_and_letters_by_culture()
    {
        string[] names = ["file10.txt", "File2.txt", "file1.txt", "ärende", "apa", "file02.txt", "zebra"];
        var sorted = names.Order(new NaturalComparer(CultureInfo.GetCultureInfo("sv-SE"))).ToArray();
        Assert.Equal(["apa", "file1.txt", "File2.txt", "file02.txt", "file10.txt", "zebra", "ärende"], sorted);
    }
}
