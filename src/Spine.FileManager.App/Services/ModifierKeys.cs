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

    public static bool Option => Pressed(GameController.GCKeyCode.LeftAlt) || Pressed(GameController.GCKeyCode.RightAlt);

    /// <summary>The key that makes a drop copy: Option on the Mac, Ctrl elsewhere.</summary>
    public static bool CopyKey => Option;

    /// <summary>The key that makes a drop move: Command on the Mac, Shift elsewhere.</summary>
    public static bool MoveKey => Command;

    static bool Pressed(nint key) =>
        GameController.GCKeyboard.CoalescedKeyboard?.KeyboardInput?.GetButton(key)?.IsPressed == true;
#elif WINDOWS
    public static bool Command => Pressed(Windows.System.VirtualKey.Control);

    public static bool Shift => Pressed(Windows.System.VirtualKey.Shift);

    public static bool CopyKey => Command;

    public static bool MoveKey => Shift;

    static bool Pressed(Windows.System.VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
#else
    public static bool Command => false;

    public static bool Shift => false;

    public static bool CopyKey => false;

    public static bool MoveKey => false;
#endif
}
