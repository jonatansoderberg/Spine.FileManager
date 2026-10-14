using System.Windows.Input;
using Plugin.Maui.Spine.Common;
using Spine.FileManager.Pages;

namespace Spine.FileManager.Services;

/// <summary>
/// The menu bar and its shortcuts on the Mac and Windows: Finder's keys on the Mac, Explorer's on Windows. Spine has no
/// key commands yet (docs/spine-capability-map.md), so the shortcuts ride on MAUI's menu bar.
/// </summary>
public static class AppMenu
{
    /// <summary>The explorer the menu acts on; on the Mac also the end of the responder chain for the Edit menu.</summary>
    public static ExplorerPageViewModel? Explorer { get; private set; }

    /// <summary>Whether a menu with these item titles is one of the app's own (Mac: telling them from the system's).</summary>
    public static bool Owns(IEnumerable<string> titles)
    {
        var strings = SpineStrings.Current;
        string[] own = [strings["Menu.NewFolder"], strings["Menu.AsList"]];
        return titles.Any(own.Contains);
    }

    public static void Install(ExplorerPage page, ExplorerPageViewModel vm)
    {
        Explorer = vm;
        if (DeviceInfo.Idiom != DeviceIdiom.Desktop || page.Window?.Page is not Page host)
            return;
        var strings = SpineStrings.Current;
        Build(host, vm, strings);
        strings.Changed += (_, _) => Build(host, vm, strings);
    }

    static void Build(Page host, ExplorerPageViewModel vm, ISpineStrings strings)
    {
        var mac = DeviceInfo.Platform == DevicePlatform.MacCatalyst;
        var command = mac ? KeyboardAcceleratorModifiers.Cmd : KeyboardAcceleratorModifiers.Ctrl;

        MenuFlyoutItem Item(string key, ICommand action, KeyboardAcceleratorModifiers modifiers, string? shortcut, object? parameter = null)
        {
            var item = new MenuFlyoutItem { Text = strings[key], Command = action, CommandParameter = parameter };
            if (shortcut is not null)
                item.KeyboardAccelerators.Add(new KeyboardAccelerator { Modifiers = modifiers, Key = shortcut });
            return item;
        }

        MenuBarItem Menu(string key, params Microsoft.Maui.IMenuElement[] items)
        {
            var menu = new MenuBarItem { Text = strings[key] };
            foreach (var item in items)
                menu.Add(item);
            return menu;
        }

        host.MenuBarItems.Clear();
        host.MenuBarItems.Add(Menu("Menu.File",
            Item("Menu.NewFolder", vm.NewFolderCommand, command | KeyboardAcceleratorModifiers.Shift, "N"),
            Item("Menu.Open", vm.ActivateCommand, mac ? command : KeyboardAcceleratorModifiers.None, mac ? "O" : "Enter"),
            Item("Menu.Rename", vm.RenameCommand, KeyboardAcceleratorModifiers.None, mac ? "Enter" : "F2"),
            new MenuFlyoutSeparator(),
            Item("Menu.Delete", vm.DeleteCommand, mac ? command : KeyboardAcceleratorModifiers.None, mac ? "Back" : "Delete"),
            Item("Menu.DeletePermanently", vm.DeletePermanentlyCommand, mac ? command | KeyboardAcceleratorModifiers.Alt : KeyboardAcceleratorModifiers.Shift, mac ? "Back" : "Delete")));
        // On the Mac the system's Edit menu stays (text fields need it) and its Select All reaches the explorer through AppDelegate.
        if (!mac)
            host.MenuBarItems.Add(Menu("Menu.Edit",
                Item("Menu.SelectAll", vm.SelectAllCommand, command, "A")));
        host.MenuBarItems.Add(Menu("Menu.View",
            Item("Menu.AsList", vm.ShowAsCommand, mac ? command : command | KeyboardAcceleratorModifiers.Shift, mac ? "1" : "6", "List"),
            Item("Menu.AsIcons", vm.ShowAsCommand, mac ? command : command | KeyboardAcceleratorModifiers.Shift, mac ? "2" : "2", "Icons"),
            new MenuFlyoutSeparator(),
            Item("Menu.ShowHidden", vm.ToggleHiddenCommand, mac ? command | KeyboardAcceleratorModifiers.Shift : command, mac ? "." : "H"),
            Item("Menu.Refresh", vm.RefreshCommand, mac ? command : KeyboardAcceleratorModifiers.None, mac ? "R" : "F5"),
            new MenuFlyoutSeparator(),
            Item("Menu.SortName", vm.SortByCommand, KeyboardAcceleratorModifiers.None, null, "Name"),
            Item("Menu.SortModified", vm.SortByCommand, KeyboardAcceleratorModifiers.None, null, "Modified"),
            Item("Menu.SortSize", vm.SortByCommand, KeyboardAcceleratorModifiers.None, null, "Size"),
            Item("Menu.SortKind", vm.SortByCommand, KeyboardAcceleratorModifiers.None, null, "Kind")));
        host.MenuBarItems.Add(Menu("Menu.Go",
            Item("Menu.Back", vm.BackCommand, mac ? command : KeyboardAcceleratorModifiers.Alt, mac ? "[" : "Left"),
            Item("Menu.Forward", vm.ForwardCommand, mac ? command : KeyboardAcceleratorModifiers.Alt, mac ? "]" : "Right"),
            Item("Menu.Up", vm.UpCommand, mac ? command : KeyboardAcceleratorModifiers.Alt, "Up"),
            Item("Menu.OpenSelected", vm.ActivateCommand, mac ? command : KeyboardAcceleratorModifiers.Alt, "Down"),
            new MenuFlyoutSeparator(),
            Item("Menu.NextItem", vm.StepCommand, KeyboardAcceleratorModifiers.None, "Down", "1"),
            Item("Menu.PreviousItem", vm.StepCommand, KeyboardAcceleratorModifiers.None, "Up", "-1"),
            Item("Menu.ExtendDown", vm.StepCommand, KeyboardAcceleratorModifiers.Shift, "Down", "+1"),
            Item("Menu.ExtendUp", vm.StepCommand, KeyboardAcceleratorModifiers.Shift, "Up", "+-1")));
#if MACCATALYST
        // Asked again a moment later: a rebuild asked while the window was still coming up was sometimes skipped,
        // leaving the bar without the app's menus.
        UIKit.UIMenuSystem.MainSystem.SetNeedsRebuild();
        host.Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1), UIKit.UIMenuSystem.MainSystem.SetNeedsRebuild);
#endif
    }
}
