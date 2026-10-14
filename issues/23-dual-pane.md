# Issue #23 — Split panes and Dual Pane (part 1: Dual Pane)

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/23
**Branch:** issue/23-dual-pane
**Status:** In progress — Dual Pane (source | destination) done; splitting top/bottom, more than two panes and moving a tab to a pane follow

## Plan
Plan §15's Dual Pane first, the part that pays for copying between folders, built without rewriting the explorer: the second pane is a second `ExplorerPage` (its own transient view model with tabs, history and selection) beside the first, without a sidebar.

## Changes
- View › Dual Pane (⇧⌘D / Ctrl+Shift+D) opens a second pane at the same folder and gives it focus; again closes it.
- Focus follows the pane last clicked (a `PointerGestureRecognizer` on each pane), marked by a blue line. The menu bar acts on the focused pane (`AppMenu.Explorer`), and the sidebar's places and favorites open in it.
- File › Copy to Other Pane (⌃⌘C / Ctrl+Alt+C) and Move to Other Pane (⌃⌘M / Ctrl+Alt+M) enqueue the selection to the other pane's folder.
- A splitter between the panes can be dragged (240 pt minimum).
- The second pane does not show the operation strips and is not saved in the session.
- The breadcrumb keeps the current folder in view as names fill in and panes resize (it showed the start of the path in a narrow pane).

## Verification (Mac Catalyst, scratch harness + capture)
1. Dual Pane opened the second pane at Källa with focus, the menu bar following it, and the sidebar only on the first pane.
2. The Mål favorite opened in the focused second pane.
3. Copy to Other Pane copied a.txt from the first pane, and it showed in the second.
4. Closing Dual Pane released the second pane.
5. A capture shows both panes, the focus line and both breadcrumbs ending at their folders.

## Decisions
- A second explorer rather than a pane layout tree: it reuses every feature (tabs, operations, menus) as is. The layout tree of §15 (top/bottom, up to four panes) needs the pane and the page separated, which the workspaces' pane layouts (#25) will want anyway.
- Dual Pane is not restored after a restart yet; it joins the session when the layout tree does.
