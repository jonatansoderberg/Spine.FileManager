# Issue #54 — Search the folder and its subfolders

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/54
**Branch:** issue/54-search (merged through issue/55-concept-ui)
**Status:** Completed

## Plan
The filter field searches the folder shown; a segmented control widens it to the subfolders, as Finder's "This Mac / folder" scope does.

## Changes
- `Explorer/FileSearch.cs`: breadth-first search from a folder with a name query, up to 10 000 results, cancellable; it skips links and packages and follows the hidden-files setting. Tests in `FileSearchTests`.
- The explorer shows results as rows with the folder each is in (`ItemRow.Where`), a This Folder / Including Subfolders control under the toolbar, a spinner and Stop while it runs; leaving the folder ends the search.
- Later (#55 step 5): the search field folds out of a magnifier (⌘F opens it) into a capsule of its own and folds back when left empty.
- #46 follow-up on the same branch: the tabs back in the title bar's row without a scroll view under it, since a scroll view there makes macOS draw its title bar over the tabs and take their clicks.

## Decisions
- Breadth first, so nearby matches show first in a deep tree.
- Packages (`.app`, bundles) are not searched into, as Finder does.
