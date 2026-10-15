namespace Spine.FileManager.Controls;

public enum ListKey { Up, Down, Return, Space, Text, Home, End, PageUp, PageDown, Delete }

/// <summary>
/// Keys for the file list while it, not a text field, has the keyboard: arrows, Return, Space, Home, End, Page Up and
/// Down, Delete and typing to jump to a name. On the Mac menu shortcuts without a modifier would take these keys from text fields too, so they live here
/// (a first responder of its own, see KeySinkHandler). Elsewhere the menu's shortcuts cover them.
/// </summary>
public class KeySink : ContentView
{
    public event Action<ListKey, bool, string?>? Pressed;

    /// <summary>Asks for the keyboard; a text field the user clicks takes it back.</summary>
    public void Activate() => Handler?.Invoke(nameof(Activate));

    internal void Raise(ListKey key, bool shift, string? text = null) => Pressed?.Invoke(key, shift, text);
}
