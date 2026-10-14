using Spine.FileManager.Explorer;

namespace Spine.FileManager.Tests.Explorer;

public sealed class TabListTests
{
    private sealed record Tab(string Name);

    private static string Names(TabList<Tab> tabs) => string.Join(",", tabs.Tabs.Select(t => t.Name == tabs.Active.Name ? $"[{t.Name}]" : t.Name));

    [Fact]
    public void A_new_tab_opens_after_the_active_one_and_becomes_active()
    {
        var a = new Tab("a");
        var tabs = new TabList<Tab>(a);
        tabs.Add(new("b"));
        tabs.Activate(a);
        tabs.Add(new("c"));
        Assert.Equal("a,[c],b", Names(tabs));
        tabs.Add(new("d"), activate: false);
        Assert.Equal("a,[c],d,b", Names(tabs));
    }

    [Fact]
    public void Closing_the_active_tab_activates_its_right_neighbour_or_else_its_left()
    {
        var a = new Tab("a");
        var tabs = new TabList<Tab>(a);
        var b = new Tab("b");
        var c = new Tab("c");
        tabs.Add(b);
        tabs.Add(c);
        tabs.Activate(b);
        Assert.True(tabs.Close(b));
        Assert.Equal("a,[c]", Names(tabs));
        Assert.True(tabs.Close(c));
        Assert.Equal("[a]", Names(tabs));
        Assert.False(tabs.Close(a));
    }

    [Fact]
    public void Closing_another_tab_keeps_the_active_one()
    {
        var a = new Tab("a");
        var tabs = new TabList<Tab>(a);
        var b = new Tab("b");
        tabs.Add(b);
        tabs.Close(a);
        Assert.Equal("[b]", Names(tabs));
    }

    [Fact]
    public void Steps_wrap_around_and_tabs_can_be_moved()
    {
        var a = new Tab("a");
        var tabs = new TabList<Tab>(a);
        tabs.Add(new("b"));
        tabs.Add(new("c"));
        tabs.Step(1);
        Assert.Equal("[a],b,c", Names(tabs));
        tabs.Step(-1);
        Assert.Equal("a,b,[c]", Names(tabs));
        tabs.Move(tabs.Active, 0);
        Assert.Equal("[c],a,b", Names(tabs));
    }
}
