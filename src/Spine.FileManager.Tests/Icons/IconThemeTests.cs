using Spine.FileManager.Icons;
using Spine.FileManager.Tests.Storage;

namespace Spine.FileManager.Tests.Icons;

public sealed class IconThemeTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private IconTheme Theme(string json, params string[] pictures)
    {
        var folder = _temp.Folder("themes/Mine");
        File.WriteAllText(Path.Combine(folder, IconTheme.FileName), json);
        foreach (var picture in pictures)
            _temp.File($"themes/Mine/{picture}", "png");
        return IconTheme.Load(folder)!;
    }

    [Fact]
    public void An_extension_wins_over_its_kind_and_a_kind_over_the_general_file_icon()
    {
        var theme = Theme("""
            { "name": "Mine", "folder": "folder.png", "file": "file.png",
              "kinds": { "document": "doc.png" }, "extensions": { ".pdf": "pdf.png", "DOCX": "word.png" } }
            """, "folder.png", "file.png", "doc.png", "pdf.png", "word.png");

        Assert.Equal("Mine", theme.Name);
        Assert.EndsWith("pdf.png", theme.IconFor(false, "pdf"));
        Assert.EndsWith("word.png", theme.IconFor(false, "docx"));
        Assert.EndsWith("doc.png", theme.IconFor(false, "odt"));
        Assert.EndsWith("file.png", theme.IconFor(false, "zip"));
        Assert.EndsWith("folder.png", theme.IconFor(true, ""));
    }

    [Fact]
    public void What_the_theme_leaves_out_or_cannot_find_keeps_the_apps_icon()
    {
        var theme = Theme("""{ "kinds": { "image": "missing.png", "video": "../outside.png" } }""");

        Assert.Equal("Mine", theme.Name);
        Assert.Null(theme.IconFor(false, "jpg"));
        Assert.Null(theme.IconFor(false, "mp4"));
        Assert.Null(theme.IconFor(true, ""));
    }

    [Fact]
    public void A_folder_without_a_readable_theme_file_is_no_theme()
    {
        _temp.Folder("themes/Empty");
        File.WriteAllText(Path.Combine(_temp.Folder("themes/Broken"), IconTheme.FileName), "{ not json");
        Theme("{}");

        Assert.Equal(["Mine"], IconTheme.All(Path.Combine(_temp.Path, "themes")).Select(t => t.Name));
    }

    [Fact]
    public void Extensions_have_kinds()
    {
        Assert.Equal(FileKinds.Spreadsheet, FileKinds.Of("XLSX"));
        Assert.Equal(FileKinds.Text, FileKinds.Of("unknown"));
    }
}
