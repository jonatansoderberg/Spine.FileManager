# Issue #9 — Favorites

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/9
**Branch:** issue/9-favorites
**Status:** Completed (right-click and drag-to-reorder need a check by hand)

## Plan
One concept (plan §15): Favorites in the sidebar above the places, for any provider's folder, kept across launches, reorderable, and shown as unavailable rather than dropped when their folder is gone or offline. Added from a right-click menu on a folder or the File menu.

## Changes
- Core `Favorites/FavoritesStore`: add (once), remove, reorder, JSON in a file written through a temporary file; a damaged file gives an empty list and is kept aside as `.broken`. Three tests (66 in all).
- Sidebar: a Favorites section (Spine `Reorder.Mode="LongPress"`, saved after a drop) with a hint while empty; a favorite's folder that does not answer within 3 s is dimmed; opening an unavailable one says so in the banner; the current folder's favorite is marked.
- Context menus (Spine `ContextMenu.Items`, right-click on the Mac and Windows, long-press on touch):
  - On a list row or grid tile: Open, Add to Favorites, Rename, Copy, Cut, Duplicate, Move to Trash. A row that was not selected is selected alone first, as in Finder and Explorer.
  - On a favorite: Open, Remove from Favorites.
- File › Add to Favorites (⌃⌘T on the Mac, as Finder's Add to Sidebar); with nothing selected it adds the folder shown.
- `Services/AppPaths.Data`: the app's data folder. Without the sandbox MAUI's `FileSystem.AppDataDirectory` is the whole `~/Library` on the Mac, where the first favorites file landed; it is now `~/Library/Application Support/se.cosmomedia.filemanager`.

## Verification (Mac Catalyst, screen locked, driven from a scratch harness)
- Favorites added from the File menu command and from a row's context-menu command.
- A favorite pointing at a missing folder showed as unavailable; opening it said "… är inte tillgänglig just nu".
- Opening Mål marked its favorite.
- Removing one worked.
- After a restart the two favorites were back, at the right path.
- A window capture shows the sidebar section.

**Needs a person:** the right-click menus on rows and favorites, and dragging favorites into a new order.

## Decisions
- Only folders can be favorites; a file shows "Only folders can be favorites." (Explorer's rule; a favorite file would open an app rather than a place).
