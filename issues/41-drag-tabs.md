# Issue #41 — Drag tabs within and between windows

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/41
**Branch:** fix/window-focus-background-menu
**Status:** In Progress

## Plan
Plan step B6. MAUI's drag and drop gestures on the tab bar, with the dragged tab kept in process (`Services/TabDrag`) so a drop in another window moves the same object, with its history, view and selection.
- Reorder within the bar: drop on a tab lands before or after it, by which half it was dropped on.
- Another window: drop on its bar moves the tab there and shows it.
- Outside the app's windows: the tab gets a window of its own (Move Tab to New Window).
- The tab bar always shows, with at least one tab (asked for during testing).

## Changes
- `TabList.Drop(tab, index)`: one of the list's own tabs moves before `index`, counted as the bar shows it before the drop; a tab from elsewhere joins there and becomes active. Two tests.
- `Services/TabDrag`: the tab being dragged, whether it was dropped and whether the pointer is still over a window.
- `ExplorerPage`: drag on each tab, drop on each tab and on the bar, and a drop target on the whole page that accepts nothing but tells the drag it is still over a window. Released outside every window, the tab moves to a new window.
- `ExplorerPageViewModel.ReceiveTab` / `Release`: the move between panes or windows. A window's only tab can be dropped on another window, which closes the one it left; the second pane's only tab dropped on the first closes Dual Pane.
- `ExplorerWindows.Close`: ends the window's scene; MAUI's `CloseWindow` only hid it on the Mac. The empty window at a restored launch uses it too.
- `AppMenu` keeps the explorers it was installed for: when a window closes, the menu goes to the explorer opened last until another window is activated. Before, it kept acting on the closed window's explorer and every menu command did nothing.
- The tab bar always shows, at a fixed height.
- New windows are resized by a point and back once on screen, which makes them draw right (`ExplorerWindows.SettleLayout`).
- Restored and background tabs keep their folder in the saved session; before, every restart dropped all tabs but the active one's window state.

## Decisions
- **The tab drags an empty `NSItemProvider`.** By default MAUI makes a picture of the view, which Finder would accept as an image file. Nothing outside the app can take the tab.
- **"Outside the windows" is the page's own drop target leaving the drag.** The title bar is not part of the page, so letting go there also gives the tab a new window.
- **Fixed tab bar height (32).** A horizontal `ScrollView` measured while empty kept that height when the tabs arrived and drew the bar half under the title bar. It only showed while it had been hidden until the second tab.
- **`SettleLayout` instead of safe-area settings.** A window opened at runtime was laid out at another window's size and drawn about 100 pt too high until resized; `SafeAreaEdges` did not change that, a programmatic resize does.
- **A window's only tab may leave for another window, and its window closes** (asked for during testing). Not the main window's: it is Spine's host and Spine's sheets (Operation Center, first run) show only there. Not the first pane's in Dual Pane, which keeps the sidebar.
- **Letting a window's only tab go outside the app does nothing:** a new window for it would only replace the one it left.
