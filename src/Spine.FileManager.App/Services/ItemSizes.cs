namespace Spine.FileManager.Services;

public enum ItemSize { Small, Medium, Large }

/// <summary>
/// How large the list's rows, the icon grid and the sidebar are, chosen in Settings. The sizes are app resources the
/// views take as DynamicResource, so every window follows a change at once; code that lays rows out reads them here.
/// </summary>
public static class ItemSizes
{
    private const string Key = "items.size";

    public static ItemSize Current { get; private set; } = (ItemSize)Preferences.Get(Key, (int)ItemSize.Medium);

    public static event Action? Changed;

    public static double RowHeight => Pick(26, 32, 38);
    public static double IconSize => Pick(16, 20, 24);
    public static double FontSize => Pick(12, 14, 15);
    public static double GridIconSize => Pick(40, 52, 68);
    public static double GridCellWidth => Pick(88, 104, 128);

    /// <summary>A sidebar row with its point of margin above and below.</summary>
    public static double SidebarRowHeight => Pick(28, 32, 36);
    public static double SidebarFontSize => Pick(12, 13.5, 15);

    public static void Use(ItemSize size)
    {
        Current = size;
        Preferences.Set(Key, (int)size);
        Apply();
        Changed?.Invoke();
    }

    public static void Apply()
    {
        if (Application.Current?.Resources is not { } resources)
            return;
        resources["ItemRowHeight"] = RowHeight;
        resources["ItemIconSize"] = IconSize;
        resources["ItemFontSize"] = FontSize;
        resources["GridIconSize"] = GridIconSize;
        // The whole column less the grid's spacing: the gap between highlights is the same across as down.
        resources["GridCellWidth"] = GridCellWidth - 4;
        resources["SidebarItemHeight"] = SidebarRowHeight - 2;
        resources["SidebarFontSize"] = SidebarFontSize;
    }

    private static double Pick(double small, double medium, double large) => Current switch
    {
        ItemSize.Small => small,
        ItemSize.Large => large,
        _ => medium,
    };
}
