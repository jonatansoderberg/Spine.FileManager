# Issue #7 — List and grid views + Spike 2: file-list performance

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/7
**Branch:** issue/7-list-grid-performance
**Status:** Completed

## Plan
Measure the explorer with 1 000 – 100 000 items before optimising, fix what the numbers point at, then add the icon grid.

## Changes
- Spike 2 in `docs/spikes/file-list-performance.md`: 10 000 items went from a 27 s freeze to 93 ms, and 100 000 to 0.5 s.
- Breadcrumb drop-downs are built once per segment (`BreadcrumbSegment.Children` is set, not filled).
- Listing and sorting off the UI thread; `RowFormat` + lazy `ItemRow` texts; `MeasureFirstItem`.
- `Controls/FileListView` with MAUI's flow-layout `CollectionViewHandler` on iOS/Mac Catalyst.
- Core: `NaturalComparer.GetKey` / `NaturalKey` and `ItemSort.Sort(StorageItem[])` (parallel keys, identical order to the comparer — a 400-item randomised test per column); an ordinal last tie-break in both.
- Icon grid: a second `FileListView` with a `GridItemsLayout` whose span follows the window, list/icons buttons in the toolbar and View › as List / as Icons (⌘1/⌘2; Ctrl+Shift+6/2 on Windows), in English and Swedish.
- Mac menu bar fixes found on the way: MAUI lets an app menu titled exactly "File"/"View" replace the system's (English), so the system menus are now removed only when they are not the app's own (`AppMenu.Owns`); the system's document commands (Duplicate, Move, Rename…, Export As…) are removed; the rebuild is asked again a second after install because a rebuild asked during startup was sometimes skipped.

## Decisions
- Performance is judged on Release builds only: Debug on Mac Catalyst runs the Mono interpreter.
- The flow-layout handler only for the file lists, not app-wide, so Spine's own CollectionViews keep MAUI's default.
- Progressive loading (first screenful before the rest) is not built: 100 000 items appear in 1.5 s with a 0.5 s freeze, and 10 000 well under the target.
