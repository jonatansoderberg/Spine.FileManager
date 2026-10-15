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
    public static ExplorerPageViewModel? Explorer { get; set; }

    private static readonly List<ExplorerPageViewModel> Installed = [];

    /// <summary>Whether a menu with these item titles is one of the app's own (Mac: telling them from the system's).</summary>
    public static bool Owns(IEnumerable<string> titles)
    {
        var strings = SpineStrings.Current;
        string[] own = [strings["Menu.NewFolder"], strings["Menu.AsList"]];
        return titles.Any(own.Contains);
    }

    public static void Install(ExplorerPage page, ExplorerPageViewModel vm)
    {
        Explorer ??= vm;
        if (page.Window is { } window && !Installed.Contains(vm))
        {
            Installed.Add(vm);
            window.Activated += (_, _) => Explorer = vm.FocusedPane;
            // Until another window is activated, the menu goes to the explorer opened last.
            window.Destroying += (_, _) =>
            {
                Installed.Remove(vm);
                if (Explorer == vm || Explorer == vm.Partner)
                    Explorer = Installed.LastOrDefault()?.FocusedPane;
            };
#if MACCATALYST
            // MAUI's Activated does not come reliably for windows opened as scenes; the key window does.
            UIKit.UIWindow.Notifications.ObserveDidBecomeKey((_, e) =>
            {
                if (e.Notification.Object is UIKit.UIWindow key && ReferenceEquals(key, window.Handler?.PlatformView))
                    Explorer = vm.FocusedPane;
            });
#endif
        }
        if (DeviceInfo.Idiom != DeviceIdiom.Desktop || page.Window?.Page is not Page host)
            return;
        var strings = SpineStrings.Current;
        Build(host, vm, strings);
        strings.Changed += (_, _) => Build(host, vm, strings);
        FileIcons.Changed += () => MainThread.BeginInvokeOnMainThread(() => Build(host, vm, strings));
        // The check marks follow a change made in Settings too.
        if (Application.Current is { } app)
            app.RequestedThemeChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() => Build(host, vm, strings));
    }

    static void Build(Page host, ExplorerPageViewModel vm, ISpineStrings strings)
    {
        var mac = DeviceInfo.Platform == DevicePlatform.MacCatalyst;
        var command = mac ? KeyboardAcceleratorModifiers.Cmd : KeyboardAcceleratorModifiers.Ctrl;

        // Every window has its explorer; a menu command goes to the one in the window that has focus.
        MenuFlyoutItem Item(string key, Func<ExplorerPageViewModel, ICommand> action, KeyboardAcceleratorModifiers modifiers, string? shortcut, object? parameter = null)
        {
            var forward = new Command<object?>(p =>
            {
                if (Explorer is { } explorer && action(explorer) is { } target && target.CanExecute(p))
                    target.Execute(p);
            });
            var item = new MenuFlyoutItem { Text = strings[key], Command = forward, CommandParameter = parameter };
            if (shortcut is not null)
                item.KeyboardAccelerators.Add(new KeyboardAccelerator { Modifiers = modifiers, Key = shortcut });
            return item;
        }

        // View › Appearance: light, dark or the system's, through Spine's theme service, which keeps the choice.
        MenuFlyoutSubItem Appearance()
        {
            var appearance = new MenuFlyoutSubItem { Text = strings["Menu.Appearance"] };
            var theme = IPlatformApplication.Current!.Services.GetRequiredService<IThemeService>();
            foreach (var (key, choice) in new[] { ("Menu.Appearance.System", AppTheme.Unspecified), ("Menu.Appearance.Light", AppTheme.Light), ("Menu.Appearance.Dark", AppTheme.Dark) })
                appearance.Add(new MenuFlyoutItem
                {
                    Text = (theme.Current == choice ? "✓ " : "") + strings[key],
                    Command = new Command(() =>
                    {
                        theme.Current = choice;
                        Build(host, vm, strings);
                    }),
                });
            return appearance;
        }

        // View › Icons: Spine's, the system's, and the icon themes the user has added; the one in use has a check mark.
        MenuFlyoutSubItem Icons()
        {
            var icons = new MenuFlyoutSubItem { Text = strings["Menu.Icons"] };
            MenuFlyoutItem Style(string title, string style) =>
                new() { Text = (FileIcons.Style == style ? "✓ " : "") + title, Command = new Command(() => FileIcons.Use(style)) };
            icons.Add(Style(strings["Menu.Icons.Spine"], FileIcons.SpineStyle));
            if (FileIcons.HasSystemIcons)
                icons.Add(Style(strings["Menu.Icons.System"], FileIcons.SystemStyle));
            foreach (var theme in FileIcons.Themes)
                icons.Add(Style(theme.Name, Path.GetFileName(theme.Folder)));
            icons.Add(new MenuFlyoutSeparator());
            icons.Add(new MenuFlyoutItem
            {
                Text = strings["Menu.Icons.OpenThemes"],
                Command = new Command(() => Explorer?.OpenLocationCommand.Execute(Storage.FileSystem.FileSystemStorageProvider.Location(FileIcons.PrepareThemesFolder()))),
            });
            return icons;
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
            Item("Menu.NewWindow", x => x.NewWindowCommand, command, "N"),
            Item("Menu.NewTab", x => x.NewTabCommand, command, "T"),
            Item("Menu.MoveTabToNewWindow", x => x.MoveTabToNewWindowCommand, KeyboardAcceleratorModifiers.None, null),
            Item("Menu.OpenInNewTab", x => x.OpenInNewTabCommand, mac ? command | KeyboardAcceleratorModifiers.Ctrl : command | KeyboardAcceleratorModifiers.Shift, mac ? "O" : "T"),
            Item("Menu.CloseTab", x => x.CloseTabCommand, command, "W"),
            new MenuFlyoutSeparator(),
            Item("Menu.OpenInTerminal", x => x.OpenInTerminalCommand, mac ? command | KeyboardAcceleratorModifiers.Alt : KeyboardAcceleratorModifiers.Ctrl | KeyboardAcceleratorModifiers.Shift, mac ? "T" : "Enter"),
            Item("Devices.Link", x => x.LinkDeviceCommand, KeyboardAcceleratorModifiers.None, null),
            Item("Settings.Open", x => x.ShowSettingsCommand, command, ","),
            new MenuFlyoutSeparator(),
            Item("Menu.NewFolder", x => x.NewFolderCommand, command | KeyboardAcceleratorModifiers.Shift, "N"),
            Item("Menu.Open", x => x.ActivateCommand, mac ? command : KeyboardAcceleratorModifiers.None, mac ? "O" : "Enter"),
            // On the Mac Return renames from the list itself (KeySink): a menu shortcut without ⌘ would take Return from text fields too.
            Item("Menu.Rename", x => x.RenameCommand, KeyboardAcceleratorModifiers.None, mac ? null : "F2"),
            Item("Menu.QuickLook", x => x.QuickLookSelectionCommand, command, mac ? "Y" : null),
            Item("Menu.Duplicate", x => x.DuplicateCommand, command, mac ? "D" : null),
            Item("Menu.CopyToOtherPane", x => x.CopyToOtherPaneCommand, mac ? command | KeyboardAcceleratorModifiers.Ctrl : command | KeyboardAcceleratorModifiers.Alt, "C"),
            Item("Menu.MoveToOtherPane", x => x.MoveToOtherPaneCommand, mac ? command | KeyboardAcceleratorModifiers.Ctrl : command | KeyboardAcceleratorModifiers.Alt, "M"),
            Item("Menu.AddFavorite", x => x.AddFavoriteCommand, command | KeyboardAcceleratorModifiers.Ctrl, mac ? "T" : null),
            new MenuFlyoutSeparator(),
            Item("Menu.Delete", x => x.DeleteCommand, mac ? command : KeyboardAcceleratorModifiers.None, mac ? "Back" : "Delete"),
            Item("Menu.DeletePermanently", x => x.DeletePermanentlyCommand, mac ? command | KeyboardAcceleratorModifiers.Alt : KeyboardAcceleratorModifiers.Shift, mac ? "Back" : "Delete")));
        // On the Mac the system's Edit menu stays (text fields need it) and its Select All reaches the explorer through AppDelegate.
        if (!mac)
            host.MenuBarItems.Add(Menu("Menu.Edit",
                Item("Menu.Undo", x => x.UndoLastCommand, command, "Z"),
                new MenuFlyoutSeparator(),
                Item("Menu.Cut", x => x.CutCommand, command, "X"),
                Item("Menu.Copy", x => x.CopyCommand, command, "C"),
                Item("Menu.Paste", x => x.PasteCommand, command, "V"),
                new MenuFlyoutSeparator(),
                Item("Menu.SelectAll", x => x.SelectAllCommand, command, "A")));
        host.MenuBarItems.Add(Menu("Menu.View",
            Item("Menu.AsList", x => x.ShowAsCommand, mac ? command : command | KeyboardAcceleratorModifiers.Shift, mac ? "1" : "6", "List"),
            Item("Menu.AsIcons", x => x.ShowAsCommand, mac ? command : command | KeyboardAcceleratorModifiers.Shift, mac ? "2" : "2", "Icons"),
            new MenuFlyoutSeparator(),
            Item("Menu.ShowHidden", x => x.ToggleHiddenCommand, mac ? command | KeyboardAcceleratorModifiers.Shift : command, mac ? "." : "H"),
            Item("Menu.Refresh", x => x.RefreshCommand, mac ? command : KeyboardAcceleratorModifiers.None, mac ? "R" : "F5"),
            Item("Menu.Find", x => x.FindCommand, command, "F"),
            Item("Menu.Preview", x => x.TogglePreviewCommand, mac ? command | KeyboardAcceleratorModifiers.Shift : KeyboardAcceleratorModifiers.Alt, "P"),
            Item("Menu.DualPane", x => x.ToggleSplitCommand, command | KeyboardAcceleratorModifiers.Shift, "D"),
            Item("Menu.Operations", x => x.ShowOperationsCommand, mac ? command | KeyboardAcceleratorModifiers.Alt : command | KeyboardAcceleratorModifiers.Shift, mac ? "L" : "J"),
            new MenuFlyoutSeparator(),
            Item("Menu.SortName", x => x.SortByCommand, KeyboardAcceleratorModifiers.None, null, "Name"),
            Item("Menu.SortModified", x => x.SortByCommand, KeyboardAcceleratorModifiers.None, null, "Modified"),
            Item("Menu.SortSize", x => x.SortByCommand, KeyboardAcceleratorModifiers.None, null, "Size"),
            Item("Menu.SortKind", x => x.SortByCommand, KeyboardAcceleratorModifiers.None, null, "Kind"),
            new MenuFlyoutSeparator(),
            Appearance(),
            Icons()));
        host.MenuBarItems.Add(Menu("Menu.Go",
            Item("Menu.Back", x => x.BackCommand, mac ? command : KeyboardAcceleratorModifiers.Alt, mac ? "[" : "Left"),
            Item("Menu.Forward", x => x.ForwardCommand, mac ? command : KeyboardAcceleratorModifiers.Alt, mac ? "]" : "Right"),
            Item("Menu.Up", x => x.UpCommand, mac ? command : KeyboardAcceleratorModifiers.Alt, "Up"),
            Item("Menu.OpenSelected", x => x.ActivateCommand, mac ? command : KeyboardAcceleratorModifiers.Alt, "Down"),
            new MenuFlyoutSeparator(),
            Item("Menu.NextTab", x => x.StepTabCommand, mac ? command | KeyboardAcceleratorModifiers.Shift : KeyboardAcceleratorModifiers.Ctrl, mac ? "]" : "Tab", "1"),
            Item("Menu.PreviousTab", x => x.StepTabCommand, mac ? command | KeyboardAcceleratorModifiers.Shift : KeyboardAcceleratorModifiers.Ctrl | KeyboardAcceleratorModifiers.Shift, mac ? "[" : "Tab", "-1"),
            new MenuFlyoutSeparator(),
            // The arrows come from the list itself on the Mac (KeySink), for the same reason as Return.
            Item("Menu.NextItem", x => x.StepCommand, KeyboardAcceleratorModifiers.None, mac ? null : "Down", "1"),
            Item("Menu.PreviousItem", x => x.StepCommand, KeyboardAcceleratorModifiers.None, mac ? null : "Up", "-1"),
            Item("Menu.ExtendDown", x => x.StepCommand, KeyboardAcceleratorModifiers.Shift, mac ? null : "Down", "+1"),
            Item("Menu.ExtendUp", x => x.StepCommand, KeyboardAcceleratorModifiers.Shift, mac ? null : "Up", "+-1")));
#if MACCATALYST
        // Asked again a moment later: a rebuild asked while the window was still coming up was sometimes skipped,
        // leaving the bar without the app's menus.
        UIKit.UIMenuSystem.MainSystem.SetNeedsRebuild();
        host.Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1), UIKit.UIMenuSystem.MainSystem.SetNeedsRebuild);
#endif
    }
}
