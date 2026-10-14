# Issue #44 — Drag and drop files

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/44
**Branch:** issue/44-file-drag-drop
**Status:** In Progress

## Plan
Plan §38, file domain (the tab domain came with #41).
- **Sources:** a row in the list or the icon grid drags the selection, or the row alone when it is not selected.
- **Targets:** a folder row, the pane (the folder shown), a tab, a place, a favorite or a breadcrumb segment.
- **Copy or move:** Finder's rule, kept in Core and tested.
- **Other apps:** files dropped from them come as file URLs; files dragged to them go as the files themselves.
- **The work:** every drop is an operation in the queue, so it is journaled, shows progress and asks about conflicts like a paste.

## Changes
- `Operations/DropRule`: same volume moves, another volume copies; the copy and move keys decide instead. A folder never goes into itself or below itself, and moving items into the folder they are in does nothing. Four tests.
- `Services/FileDrag`: the items dragged from the app's lists, across windows.
- `Services/Volumes`: a location's volume, the longest mount point its path starts with.
- `Services/ModifierKeys`: `CopyKey` (Option / Ctrl) and `MoveKey` (Command / Shift).
- `ExplorerPage`:
  - drag on rows, carrying the files' URLs for other apps
  - drop on folder rows (highlighted while a drag is over them), the pane, tabs, places, favorites and breadcrumb segments
  - file URLs read from drops by other apps
- `ExplorerPageViewModel`: `DragItems`, `DropActionFor`, `DropFiles` (selects what arrives when the target is the folder shown).
- **Restored tabs keep their order.** `Add` put each one right after the first, so every restart reversed them.
- **Each pane decides its own list columns** (`HasColumns`, from the list's width) instead of the whole page. In a narrow Dual Pane the rows were wider than the pane and ran over the sidebar.
- **The Dual Pane focus line no longer moves the pane.** It is always there in Dual Pane and only its opacity follows focus. Before, a line appearing on the press shifted the pane under the drag that press started.

## Decisions
- **Drops from other apps copy by default.** The move key moves. A drop should not take a file away from where another app keeps it.
- **The pane takes drops, not the list.** A CollectionView receives no drop of its own, so the Grid around the list is the target.
- **Verified on the Mac**, single pane:
  - a row onto a folder row
  - a row onto a favorite in the sidebar
  - a row onto a breadcrumb segment
  - conflicts asking as with paste
- **Not verified:**
  - From pane to pane in Dual Pane. Synthetic drags there started but never reached a target.
  - To and from Finder. Synthetic drags do not start in Finder at all.
  - Both need a real mouse.
