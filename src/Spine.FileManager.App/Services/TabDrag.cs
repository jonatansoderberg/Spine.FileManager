using Spine.FileManager.Pages;

namespace Spine.FileManager.Services;

/// <summary>
/// The tab being dragged, shared by every window: a drag and its drop can be in different windows of the app, and
/// the tab itself never leaves the process.
/// </summary>
public static class TabDrag
{
    public sealed record Drag(ExplorerPageViewModel From, ExplorerTab Tab)
    {
        public bool Dropped { get; set; }

        /// <summary>Whether the pointer is over one of the app's windows: let go anywhere else, the tab gets a window of its own.</summary>
        public bool OverWindow { get; set; } = true;
    }

    public static Drag? Current { get; private set; }

    public static void Start(ExplorerPageViewModel from, ExplorerTab tab) => Current = new(from, tab);

    public static Drag? End()
    {
        var drag = Current;
        Current = null;
        return drag;
    }
}
