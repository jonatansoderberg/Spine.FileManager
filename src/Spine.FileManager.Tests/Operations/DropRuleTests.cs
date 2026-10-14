using Spine.FileManager.Operations;
using Spine.FileManager.Storage;

namespace Spine.FileManager.Tests.Operations;

public sealed class DropRuleTests
{
    private static StorageLocation At(string path) => new(new("local"), path, path);

    private static StorageLocation? Parent(StorageLocation l) =>
        Path.GetDirectoryName(l.ItemId) is { Length: > 0 } parent ? At(parent) : null;

    private static string Volume(StorageLocation l) => l.ItemId.StartsWith("/Volumes/USB", StringComparison.Ordinal) ? "/Volumes/USB" : "/";

    private static DropAction Choose(string[] items, string target, bool copyKey = false, bool moveKey = false) =>
        DropRule.Choose([.. items.Select(At)], At(target), Parent, Volume, copyKey, moveKey);

    [Fact]
    public void Items_move_on_their_volume_and_are_copied_to_another()
    {
        Assert.Equal(DropAction.Move, Choose(["/Users/a/x.txt"], "/Users/a/Docs"));
        Assert.Equal(DropAction.Copy, Choose(["/Users/a/x.txt"], "/Volumes/USB"));
        Assert.Equal(DropAction.Copy, Choose(["/Users/a/x.txt", "/Volumes/USB/y.txt"], "/Users/a/Docs"));
    }

    [Fact]
    public void The_copy_or_move_key_decides()
    {
        Assert.Equal(DropAction.Copy, Choose(["/Users/a/x.txt"], "/Users/a/Docs", copyKey: true));
        Assert.Equal(DropAction.Move, Choose(["/Users/a/x.txt"], "/Volumes/USB", moveKey: true));
    }

    [Fact]
    public void A_folder_never_goes_into_itself_or_below_itself()
    {
        Assert.Equal(DropAction.None, Choose(["/Users/a/Docs"], "/Users/a/Docs"));
        Assert.Equal(DropAction.None, Choose(["/Users/a/Docs"], "/Users/a/Docs/Old"));
        Assert.Equal(DropAction.None, Choose(["/Users/a/Docs"], "/Users/a/Docs/Old", copyKey: true));
    }

    [Fact]
    public void Moving_to_the_folder_the_items_are_in_does_nothing_but_copying_duplicates()
    {
        Assert.Equal(DropAction.None, Choose(["/Users/a/x.txt"], "/Users/a"));
        Assert.Equal(DropAction.Copy, Choose(["/Users/a/x.txt"], "/Users/a", copyKey: true));
    }
}
