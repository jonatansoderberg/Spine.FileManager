namespace Spine.FileManager.Services;

/// <summary>
/// The window's backdrop on the desktops, the user's choice: solid, Mica or Acrylic. On Windows Spine draws Windows' own
/// materials (<c>options.Windows.Backdrop</c>); on the Mac <see cref="WindowMaterial"/> puts an AppKit material behind
/// the window (Mica the calmer under-window one, Acrylic the sidebar one that shows the desktop clearly), as Spine's Mac
/// backdrop tinted and covered the sidebar. The sidebar is clear to show it, as Finder's and Explorer's are; the files
/// keep a solid background. Windows opened after a change get it.
/// </summary>
public static class WindowBackdrops
{
    private const string Key = "window.backdrop";

    public static bool IsDesktop => OperatingSystem.IsMacCatalyst() || OperatingSystem.IsWindows();

    public static WindowBackdrop Current
    {
        get => Enum.TryParse<WindowBackdrop>(Preferences.Get(Key, null), out var backdrop) ? backdrop
            // Before the choice there was the Mac's on/off switch.
            : Preferences.Get("window.material", true) ? WindowBackdrop.Mica : WindowBackdrop.None;
        set
        {
            Preferences.Set(Key, value.ToString());
            if (OperatingSystem.IsWindows() && IPlatformApplication.Current?.Services.GetService<SpineOptions>() is { } options)
                options.Windows.Backdrop = value;
        }
    }

    /// <summary>Whether the windows show the backdrop: on a desktop, chosen, and on the Mac not under Reduce Transparency.</summary>
    public static bool IsOn => IsDesktop && Current is not WindowBackdrop.None
#if MACCATALYST
        && !UIKit.UIAccessibility.IsReduceTransparencyEnabled
#endif
        ;

    /// <summary>
    /// What the views that show the backdrop are painted with instead of clear. Mac Catalyst asks the render server
    /// which layer is under the pointer before it hands a mouse or scroll event to UIKit; where every UIKit layer is
    /// fully clear the answer is the AppKit material behind, and the event is dropped. In the sidebar that stopped the
    /// mouse wheel whenever a gap between rows came under a pointer held still. One percent is not seen.
    /// </summary>
    public static Color HitTestableClear { get; } = Color.FromRgba(0, 0, 0, 0.01);
}
