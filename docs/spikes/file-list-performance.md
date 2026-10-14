# Spike 2 — File-list performance

**Issue:** [#7](https://github.com/jonatansoderberg/Spine.FileManager/issues/7) · **Run:** 2026-10-14 on Mac Catalyst (Apple silicon, macOS 27)

**Question:** does the explorer stay usable with 1 000 – 100 000 items in one folder? **Exit criterion:** 10 000 items feel instantly usable.

## How it was measured

Folders of 1 000, 10 000, 50 000 and 100 000 entries (every twentieth a folder, the rest small files with mixed extensions). A scratch harness, compiled into the app only for the run, opens each folder through the explorer's own command and records:

- time until the rows are on screen
- the longest gap between ticks of an 8 ms timer on the UI thread while loading and while jumping to the end and the middle (how long the window froze)
- the working set

All final numbers are from **Release** builds. Debug builds on Mac Catalyst run the C# in the Mono interpreter (for hot reload), which makes managed code many times slower: they are useless for performance work.

## Results

The longest UI-thread stall while loading, in ms:

| Items | First version | Menus built once | + work off the UI thread, lazy cells, one measured row | + flow-layout handler, keyed sort (final) |
|---:|---:|---:|---:|---:|
| 1 000 | 272 | 323 | 222 | 133 |
| 10 000 | **26 973** | 463 | 210 | **93** |
| 50 000 | 25 912 | 1 855 | 852 | 287 |
| 100 000 | 25 989 | 3 983 | 1 951 | **500** |

Final, per folder:

| Items | Rows on screen | UI stall loading | UI stall jumping to end/middle | Read (background) | Sort by name (background) |
|---:|---:|---:|---:|---:|---:|
| 1 000 | 0.2 s | 133 ms | 112 ms | 5 ms | 6 ms |
| 10 000 | 0.16 s | 93 ms | 115 ms | 41 ms | 43 ms |
| 50 000 | 0.8 s | 287 ms | 120 ms | 218 ms | 229 ms |
| 100 000 | 1.5 s | 500 ms | 117 ms | 446 ms | 485 ms |

The working set grows by 25–60 MB, even at 100 000 items.

**Exit criterion met:** 10 000 items are on screen in 0.16 s and freeze the window for less than 0.1 s.

## What made the difference

1. **The breadcrumb's drop-down menus (27 s → 0.5 s).** Each segment's subfolder menu was filled row by row. Spine rebuilds a native menu on every change to its `MenuItems`, so 500 subfolders meant 500 rebuilds of a growing menu on the UI thread, plus 700 MB of garbage. Now the menu is built and set once. *Lesson for Spine: building a `MenuItems` off-screen and assigning it should be the documented way; a batch-update API would help.*
2. **Nothing per item on the UI thread.** Reading (`await foreach`) and sorting moved into one `Task.Run`, and only the finished rows cross over. Each `await` in the loop had been a hop to the UI thread.
3. **Lazy cell texts.** A row formats its date, size and kind only when a cell shows it, so 100 000 items cost 100 000 small objects rather than 300 000 strings.
4. **`ItemSizingStrategy="MeasureFirstItem"`.** Every row has the same height.
5. **MAUI's flow-layout `CollectionViewHandler` for the file lists on Apple platforms** (`FileListView`, registered in `MauiProgram`). MAUI 10's default `CollectionViewHandler2` uses compositional layout, which lays out every row up front: 100 000 rows held the window for 1.75 s, and a jump to the end for 1.6 s. The flow-layout handler took 0.47 s and 0.08 s, with a third of the memory. Other lists (Spine's own) keep the default handler.
6. **Keyed natural sort.** Comparing names pairwise with the culture's rules took 1.4 s for 100 000 names. Precomputed keys (the culture's `SortKey` for text runs, numbers for digit runs), made in parallel, take 0.49 s and give exactly the same order (tested). A final ordinal comparison keeps "File" and "file" in a fixed order.

## Not measured here, still to do

- **Scrolling FPS** under a real trackpad fling (the harness measures jumps, not continuous scrolling). Check by hand on a 100 000-item folder.
- **Windows, iOS, Android.** Windows uses its own `CollectionView` (ItemsRepeater), and the 100 000-item run should be repeated there in the Windows pass (#3).
- **Thumbnails and slow metadata.** All metadata here came with the listing (local APFS). Network shares and SAF need the lazy metadata loading the plan describes; that arrives with the providers that need it.
- **The remaining 0.5 s at 100 000 items** is MAUI taking the new items source. Showing the first screenful before the whole list (progressive loading) would hide it, if it matters in practice.
