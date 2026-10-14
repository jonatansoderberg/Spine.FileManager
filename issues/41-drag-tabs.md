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
- `ExplorerPageViewModel.ReceiveTab` / `Release`: the move between panes or windows; the pane's last tab stays.
- The tab bar always shows, at a fixed height.
- New windows are resized by a point and back once on screen, which makes them draw right (`ExplorerWindows.SettleLayout`).
- Restored and background tabs keep their folder in the saved session; before, every restart dropped all tabs but the active one's window state.

## Decisions
- **The tab drags an empty `NSItemProvider`.** By default MAUI makes a picture of the view, which Finder would accept as an image file. Nothing outside the app can take the tab.
- **"Outside the windows" is the page's own drop target leaving the drag.** The title bar is not part of the page, so letting go there also gives the tab a new window.
- **Fixed tab bar height (32).** A horizontal `ScrollView` measured while empty kept that height when the tabs arrived and drew the bar half under the title bar. It only showed while it had been hidden until the second tab.
- **`SettleLayout` instead of safe-area settings.** A window opened at runtime was laid out at another window's size and drawn about 100 pt too high until resized; `SafeAreaEdges` did not change that, a programmatic resize does.
- **The pane's last tab cannot leave.** A window without tabs would have to close itself, and the main window is Spine's host.
