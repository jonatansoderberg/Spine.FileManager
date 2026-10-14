# Issue #6 — Breadcrumb bar with folder drop-downs

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/6
**Branch:** issue/6-breadcrumb
**Status:** Completed

## Plan
Windows-like on every platform: each segment opens its folder, the chevron after it lists that folder's subfolders. Segments from the provider (`GetParent` up to the root), so any provider works.

## Changes
- Core `Explorer/Breadcrumb`: `Segments` (root → location) and `SubfoldersAsync` (folders only, hidden left out unless shown, natural order, capped at 500). Two tests.
- The toolbar's path became a horizontal bar of segment buttons, each followed by a "›" button whose `MenuButton.Items` (Spine) is the native menu (UIButton.Menu on the Mac, MenuFlyout on Windows) of its subfolders, with folder icons. The segments appear at once; display names and drop-downs fill in after. The bar scrolls to keep the current folder in view; showing hidden files rebuilds the drop-downs.
- Root and top-level names from the platform: "Macintosh HD" and "Användare" on the Mac (`NSFileManager.DisplayName`), "Data (D:)" on Windows.

## Verification (Mac Catalyst, Swedish)
"Macintosh HD › Användare › jonatansoderberg"; the chevron after the home folder opened a native menu of its subfolders with Finder's names; picking "Code" opened it and added the segment; clicking "Användare" opened /Users.

## Decisions
- The drop-downs are filled when the bar is built rather than when a chevron is pressed: Spine's menu buttons have no "about to open" hook. One folder listing per segment, in the background, is cheap next to the list itself.
- Typing a path into the bar (Explorer's address box) is left for later; ⇧⌘G "Go to Folder" fits the command framework (§36).
