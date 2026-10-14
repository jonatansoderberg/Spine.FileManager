# Issue #8 — Local file operations

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/8
**Branch:** issue/8-local-operations
**Status:** Completed (persistence and the full Operation Center are milestone B)

## Plan
The explorer never copies itself: copy, move, paste and duplicate become operations in a queue (plan §10), run in the background one at a time, cancellable, with progress throttled to ten reports a second, and with a question when a name is taken. Copy, cut and paste moved here from #5.

## Changes
- Core `Operations/`: `FileOperation` (kind, sources, target, state, progress, errors, results), `OperationQueue`:
  - counts the items and bytes first, then copies or moves item by item
  - native copy for files up to 16 MB and a 1 MB-buffer stream above that, so big files show bytes and cancel midway
  - native move (a rename) within a provider; across providers copy then delete
  - a folder onto a folder merges and asks per file
  - Keep both names "x (2)"; an item copied into its own folder becomes its numbered copy
  - never a folder into itself
  - errors per item while the rest go on
  - Stop cancels; answers "for all" are remembered
  - 11 tests (63 in all)
- App: Copy, Cut, Paste and Duplicate (⌘C/⌘X/⌘V through the system Edit menu and `copy:`/`cut:`/`paste:` on the AppDelegate on the Mac; an Edit menu with Ctrl+X/C/V on Windows; ⌘D Duplicate). A strip in place of the status line shows the running operation ("Copying stor.bin — 1.2 GB / 1.5 GB · 1.7 GB/s") with Cancel. Conflicts ask through an action sheet (Replace, Keep Both, Skip and their "all" variants, Stop). Pasted and duplicated items are selected afterwards; errors show in the banner. English and Swedish.
- `Services/FileClipboard`: the app's own clipboard, and on the Mac the system pasteboard as file URLs both ways (copy here → paste in Finder; copy in Finder → paste here).

## Verification (Mac Catalyst)
The screen was locked during the run, so the explorer's own commands were driven from a scratch harness:
- Select all and Copy in Källa put 4 file URLs on the pasteboard (`clipboard info` shows «class furl»).
- Paste in Mål showed the strip from 8 % to 98 % at ~1.7 GB/s for a 1.5 GB file and ended with the 4 items selected.
- Duplicate gave "a (2).txt", selected.
- Cut and Paste moved b.txt into Källa/Bilder.

The conflict sheet and the Cancel button need a person at the screen; the engine's conflict paths are covered by tests.

## Decisions
- One operation at a time for now; parallel transfers and their scheduling belong to the transfer planner (§11, §28).
- "Replace" on a folder merges rather than deleting the existing folder: safer, and what Explorer does; Finder's "Replace" (delete then copy) can be added as a choice later.
- The 16 MB native-copy limit is a stopgap until Spike 3 picks strategies per volume (APFS clones big files instantly; the stream copy does not).
