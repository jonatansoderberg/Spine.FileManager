namespace Spine.FileManager.Services;

/// <summary>
/// Which modifier keys are held right now, for Cmd/Ctrl-click and Shift-click in a list; MAUI's tap events carry
/// no modifiers. <see cref="Command"/> is ⌘ on the Mac and Ctrl elsewhere.
/// </summary>
public static class ModifierKeys
{
#if MACCATALYST
    public static bool Command => Pressed(GameController.GCKeyCode.LeftGui) || Pressed(GameController.GCKeyCode.RightGui);

    public static bool Shift => Pressed(GameController.GCKeyCode.LeftShift) || Pressed(GameController.GCKeyCode.RightShift);

    static bool Pressed(nint key) =>
        GameController.GCKeyboard.CoalescedKeyboard?.KeyboardInput?.GetButton(key)?.IsPressed == true;
#elif WINDOWS
    public static bool Command => Pressed(Windows.System.VirtualKey.Control);

    public static bool Shift => Pressed(Windows.System.VirtualKey.Shift);

    static bool Pressed(Windows.System.VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
#else
    public static bool Command => false;

    public static bool Shift => false;
#endif
}
