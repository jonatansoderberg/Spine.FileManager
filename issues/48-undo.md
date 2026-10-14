# Issue #48 — Undo for copy, move, rename and new folder

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/48
**Branch:** issue/48-undo
**Status:** Completed

## Plan
Edit › Undo (⌘Z, Ctrl+Z) takes back the last file action, as Finder does:
- a copy goes to the Trash
- a move goes back where it came from
- a rename gets its old name
- a new folder goes to the Trash

There is one history for the whole app.

## Changes
- `FileOperation.Placed`: each top-level item's source and where it ended up, recorded by the queue. It is not journaled, since undo is for this run.
- `Operations/Undo`:
  - `UndoSteps.For(operation)` turns a finished copy into `TrashItems` and a finished move into `MoveBack`, one group per source folder.
  - `UndoHistory` keeps the last 50 steps.
  - 4 tests, one of which runs a move back through the queue.
- `Services/UndoService`:
  - records every finished copy and move; renames and new folders are recorded by the explorer
  - takes the last step back, with moves back going through the queue
  - does not record the moves back themselves
- **Mac:** the system's Edit › Undo reaches the explorer as `undo:` at the end of the responder chain. A text field being edited takes ⌘Z itself.
- **Windows:** Edit › Undo, Ctrl+Z.

## Decisions
- **Undoing a copy trashes the copies** rather than deleting them, as Finder does.
- **No redo yet,** and Trash cannot be undone yet: the provider's trash does not return where the item went. Both are follow-ups.
- **A copy or move that replaced a file is undone too,** but the replaced file stays gone. The queue replaced it permanently.
- **Verified on the Mac:**
  - Duplicate then Undo: the copy went to the Trash.
  - A drag onto a folder then Undo: the file moved back.
