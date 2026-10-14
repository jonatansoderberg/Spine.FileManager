# Issue #21 — Tabs

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/21
**Branch:** issue/21-tabs
**Status:** Completed (reorder by drag and middle-click wait — see Decisions)

## Plan
Tabs in the explorer pane (plan §13): each tab keeps its folder, history, sort, view mode, hidden-files choice and selection; a browser's rules for where a new tab opens and which tab follows a closed one; the tab bar from the second tab on, as in Finder.

## Changes
- Core `Explorer/TabList<T>`: a new tab opens after the active one; closing the active tab activates its right neighbour, or its left when it was last; the last tab stays; steps wrap; tabs can be moved. Four tests (74 in all).
- `ExplorerTab` holds a pane's state. The view model's history, sort, view and hidden-files follow the active tab. Switching shows the tab's last listing at once (cached items) and refreshes it in the background, with its selection restored. A listing that finishes after its tab was left is dropped.
- Tab bar above the toolbar: titles from the folder's display name, the active tab lit, × to close.
- File › New Tab (⌘T / Ctrl+T), Open in New Tab (⌃⌘O / Ctrl+Shift+T), Close Tab (⌘W / Ctrl+W); Go › Show Next/Previous Tab (⇧⌘] ⇧⌘[ / Ctrl+Tab, Ctrl+Shift+Tab); Open in New Tab in a row's context menu. The Mac's system Close (⌘W) is removed so ⌘W closes a tab.

## Verification (Mac Catalyst, scratch harness + window capture)
1. A new tab was navigated to the test folder, switched to icons and given a selection; Källa was then opened in a new tab.
2. Stepping back restored the second tab with Källa still selected and icon view; the first tab stayed in list view with no back history.
3. Closing the second tab made the Källa tab to its right active.
4. A capture shows three tabs and the active one lit.

## Decisions
- Reordering tabs by dragging comes with drag-and-drop (§38), where tab drag also detaches and attaches (Spike 4, #22).
- Middle-click to open a folder in a new tab needs pointer buttons MAUI's gestures do not expose (only primary and secondary); a Spine or platform-handler candidate, noted rather than built.
- Tabs are not restored after a restart yet: that is workspaces' "last session" (#25).
