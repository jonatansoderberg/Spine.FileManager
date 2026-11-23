namespace Spine.FileManager.Services;

/// <summary>What is behind the sidebar on the desktops.</summary>
public enum Backdrop
{
    /// <summary>A solid sidebar and window.</summary>
    Solid,

    /// <summary>Tinted by the desktop picture, nearly opaque, as Finder's sidebar is.</summary>
    Mica,

    /// <summary>The desktop blurred through the sidebar.</summary>
    Acrylic,
}

/// <summary>
/// The window's backdrop on the desktops, the user's choice, applied at once to the open windows. On Windows Spine draws
/// Windows' own materials (<c>options.Windows.Backdrop</c>, and the open windows' <c>SystemBackdrop</c>); on the Mac
/// <see cref="WindowMaterial"/> puts an AppKit material behind the window (Mica the window-background one, Acrylic the
/// sidebar one), as Spine's Mac backdrop tinted and covered the sidebar. The sidebar is clear to show it, as Finder's and
/// Explorer's are; the files keep a solid background (<see cref="Pages.ExplorerPage"/> follows <see cref="Changed"/>).
/// </summary>
public static class WindowBackdrops
{
    private const string Key = "window.backdrop";

    public static bool IsDesktop => OperatingSystem.IsMacCatalyst() || OperatingSystem.IsWindows();

    /// <summary>Raised on the main thread when the choice changes.</summary>
    public static event Action? Changed;

    public static Backdrop Current
    {
        get
        {
            var stored = Preferences.Get(Key, null);
            return stored switch
            {
                null => Preferences.Get("window.material", true) ? Backdrop.Mica : Backdrop.Solid, // the Mac's old switch
                "None" => Backdrop.Solid,
                _ => Enum.TryParse<Backdrop>(stored, out var b) ? b : Backdrop.Mica,
            };
        }
        set
        {
            if (value == Current)
                return;
            Preferences.Set(Key, value.ToString());
            if (IPlatformApplication.Current?.Services.GetService<SpineOptions>() is { } options)
                options.Windows.Backdrop = ForWindows;
#if WINDOWS
            foreach (var window in Application.Current?.Windows ?? [])
                if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
                    native.SystemBackdrop = ForWindows switch
                    {
                        WindowBackdrop.Mica => new Microsoft.UI.Xaml.Media.MicaBackdrop(),
                        WindowBackdrop.Acrylic => new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
                        _ => null,
                    };
#endif
            Changed?.Invoke();
        }
    }

    /// <summary>Spine's backdrop for Windows.</summary>
    public static WindowBackdrop ForWindows => Current switch
    {
        Backdrop.Mica => WindowBackdrop.Mica,
        Backdrop.Acrylic => WindowBackdrop.Acrylic,
        _ => WindowBackdrop.None,
    };

    /// <summary>Whether the windows show the backdrop: on a desktop, chosen, and on the Mac not under Reduce Transparency.</summary>
    public static bool IsOn => IsDesktop && Current is not Backdrop.Solid
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

    /// <summary>
    /// The sidebar's paint over the backdrop in dark mode. Mica on the Mac (the window-background material) is darker
    /// than the files and was hard to tell from Solid; a 4 % white veil lifts it to Finder's sidebar (measured beside
    /// Finder, macOS 27: 37,35,41 there). Elsewhere it is clear.
    /// </summary>
    public static Color SidebarVeilDark => OperatingSystem.IsMacCatalyst() && Current is Backdrop.Mica
        ? Color.FromRgba(255, 255, 255, 0.04)
        : HitTestableClear;
}
