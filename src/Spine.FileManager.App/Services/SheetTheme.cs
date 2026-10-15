using Plugin.Maui.Spine.Core;

namespace Spine.FileManager.Services;

/// <summary>
/// A sheet's page follows a change of light and dark. An AppThemeBinding hears of a change through a resource change on
/// its own element, which MAUI passes down from the Application through the element tree; Spine shows sheets outside
/// that tree, so a sheet open while the theme changed kept the colours it opened with. Each element of the page is told
/// itself, through Spine's Track, which lives as long as the page (Application.RequestedThemeChanged is a weak event:
/// a handler held by nothing else is collected and never runs).
/// </summary>
public static class SheetTheme
{
    private const string Key = "__MAUI_ApplicationTheme__";

    public static void Follow(VisualElement page)
    {
        var theme = IPlatformApplication.Current?.Services.GetService<IThemeService>();
        theme?.Track(page, () =>
        {
            foreach (var element in page.GetVisualTreeDescendants().OfType<VisualElement>().Prepend(page))
                element.Resources[Key] = theme.Effective;
        });
    }
}
