using Foundation;
using Microsoft.Maui.Handlers;
using Spine.FileManager.Controls;
using UIKit;

namespace Spine.FileManager;

/// <summary>A plain view that can be first responder and turns key presses into <see cref="KeySink"/> keys.</summary>
public sealed class KeySinkHandler : ViewHandler<KeySink, KeySinkHandler.SinkView>
{
    public static readonly CommandMapper<KeySink, KeySinkHandler> Commands = new(ViewCommandMapper)
    {
        [nameof(KeySink.Activate)] = (handler, _, _) => handler.PlatformView.BecomeFirstResponder(),
    };

    public KeySinkHandler() : base(ViewMapper, Commands)
    {
    }

    protected override SinkView CreatePlatformView() => new() { Owner = VirtualView };

    public sealed class SinkView : UIView
    {
        public KeySink? Owner { get; set; }

        public override bool CanBecomeFirstResponder => true;

        public override void PressesBegan(NSSet<UIPress> presses, UIPressesEvent evt)
        {
            var handled = false;
            foreach (var press in presses)
            {
                if (press.Key is not { } key || Owner is null)
                    continue;
                // A shortcut with ⌘, ⌃ or ⌥ is the menu's.
                if ((key.ModifierFlags & (UIKeyModifierFlags.Command | UIKeyModifierFlags.Control | UIKeyModifierFlags.Alternate)) != 0)
                    continue;
                var shift = key.ModifierFlags.HasFlag(UIKeyModifierFlags.Shift);
                ListKey? listKey = key.KeyCode switch
                {
                    UIKeyboardHidUsage.KeyboardUpArrow => ListKey.Up,
                    UIKeyboardHidUsage.KeyboardDownArrow => ListKey.Down,
                    UIKeyboardHidUsage.KeyboardReturnOrEnter or UIKeyboardHidUsage.KeypadEnter => ListKey.Return,
                    UIKeyboardHidUsage.KeyboardSpacebar => ListKey.Space,
                    _ => key.Characters is { Length: 1 } c && !char.IsControl(c[0]) ? ListKey.Text : null,
                };
                if (listKey is { } k)
                {
                    Owner.Raise(k, shift, k == ListKey.Text ? key.Characters : null);
                    handled = true;
                }
            }
            if (!handled)
                base.PressesBegan(presses, evt);
        }
    }
}
