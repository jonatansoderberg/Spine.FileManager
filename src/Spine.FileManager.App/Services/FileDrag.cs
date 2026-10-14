using Spine.FileManager.Pages;

namespace Spine.FileManager.Services;

/// <summary>
/// The files being dragged from one of the app's lists, shared by every window. A drop from another app has none of
/// this and brings file URLs instead (<see cref="ExplorerPage"/>).
/// </summary>
public static class FileDrag
{
    public sealed record Drag(ExplorerPageViewModel From, IReadOnlyList<StorageLocation> Items);

    public static Drag? Current { get; private set; }

    public static void Start(ExplorerPageViewModel from, IReadOnlyList<StorageLocation> items) => Current = new(from, items);

    public static void End() => Current = null;
}
