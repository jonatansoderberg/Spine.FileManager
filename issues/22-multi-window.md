# Issue #22 — Spike 4: multiple windows and tab detach

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/22
**Branch:** issue/22-multi-window
**Status:** Completed (dragging tabs between windows waits for drag and drop)

## Plan
Find out how far MAUI windows go with Spine's single host, and keep what works: New Window and Move Tab to New Window on the Mac.

## Changes
See [`docs/spikes/multi-window.md`](../docs/spikes/multi-window.md). In short:
- Scene manifest and `SceneDelegate` on Mac Catalyst.
- `Services/ExplorerWindows`: further windows as plain MAUI pages holding their own `ExplorerPage` (transient view model).
- Menu items forward to the explorer of the window in focus.
- The view model's prompts go to its own window.
- New Window (⌘N / Ctrl+N), and Move Tab to New Window in the File menu and on a tab's right-click menu.

## Decisions
- No change to Spine: further windows live outside its host. The cost is that Spine sheets (Operation Center, first run) open in the main window; the spike doc lists it as a Spine candidate.
- B4 (multiple windows) is done with this; B6 (dragging tabs between windows) moves to drag and drop (§38).
