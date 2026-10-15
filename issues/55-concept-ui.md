# Issue #55 — Roadmap: the explorer after the concept image

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/55
**Branch:** issue/55-concept-ui
**Status:** In Progress (roadmap; steps tracked in `docs/plans/concept-ui.md`)

## Plan
Eleven steps toward the concept image, written in `docs/plans/concept-ui.md` before any code. Design choices are put to Jonatan before building.

## Changes
- **Step 1, Settings:** Settings at the sidebar's foot opens a sheet:
  - appearance as a segmented control
  - window material on or off
  - icon style
  - where received files go
- **Step 4, tabs (done):**
  - Edge-like look: an icon per tab, the active tab raised and joined to the toolbar, × on the active and the hovered tab, and a + button.
  - Windows shortcuts:
    - Ctrl+T new tab, Ctrl+W close tab
    - Ctrl+Shift+T reopens a closed tab with its history
    - Ctrl+Tab / Ctrl+Shift+Tab next and previous tab
    - Ctrl+1–8 a tab by number, Ctrl+9 the last (⌘⌥ on the Mac, where ⌘1/⌘2 choose the view as in Finder)
    - Ctrl+Enter and Ctrl-double-click open a folder in a new tab
- **Step 5, toolbar (in progress):**
  - Two rows: navigation, the breadcrumb in a capsule taking all the room left, refresh and search; below them new folder, cut, copy, paste, duplicate, rename and delete, then hidden files and the view switch.
  - The breadcrumb folds from the root end into « as in Windows' address bar; « lists the folded folders, nearest first.
  - Missing glyphs (cut, paste, duplicate, new folder) are drawn in Spine's style as embedded SVGs.
  - Left: a columns view.
- **Step 3, sidebar (done):**
  - Home, Favorites with coloured folder icons.
  - This Device: places, disks (hidden volumes left out) and the Trash.
  - Other Devices with Link Device as a button.
  - Network and Backups dimmed until they exist.
- **Step 8, Home (done):**
  - Disk cards: capacity, free space as Finder counts it (purgeable included), % used, red from 90 %.
  - The Trash: items and size, Empty behind a confirmation.
  - Other Devices and Recent Locations.
  - New tabs and a first start open Home, and Home is in the tab history, so Back returns to it.
- **Command bar and sizes** (branch issue/55-toolbar-and-sizes):
  - The command bar as in the concept image, with glyphs about 18 points instead of 13.
  - Settings › Size of files and folders, Small / Medium / Large (asked 2026-10-15). It sets row height, file icons and text in the list, the icon grid and the sidebar. The sizes are app resources (`Services/ItemSizes.cs`) the views take as DynamicResource, so every window follows a change. The lists measure their first row again.
- **Cloud section and hiding** (branch issue/55-cloud-section): iCloud Drive and the CloudStorage providers in a Cloud section of their own; right-click › Hide from Sidebar, with the hidden places listed in Settings to show again (asked 2026-10-15; MacDroid left folders behind after it was turned off).
- **Apps:** a Mac package (`NSURLIsPackageKey`) shows its own icon and Finder's name, sorts among the files, opens on double-click, takes no drops and has Show Package Contents (`StorageItem.IsPackage`, `IsBrowsable`).
- **Toolbar glyphs:** Spine renders an SVG at the button's size inside its own padding (5 by default), so the button's padding shrank it twice; buttons now set `SvgImageSource.Padding` only.
- **Selection rectangle:** in the details list a drag that does not start on a name or icon draws a selection rectangle (Cmd/Ctrl keeps the selection); it follows the wheel and scrolls by itself near the top and bottom edge. Files are dragged only by name or icon.
- **Fixes on the way:**
  - The Dual Pane divider is a grip like the sidebar's: the old one took no drags.
  - The sidebar's lists are as tall as their rows, so only the sidebar scrolls.
  - The search field has no native border or focus ring of another shape.
- **Tab titles and column headers** use the sidebar's font size, so they follow the Small/Medium/Large setting with the rest of the window.
- **Icon view names and the status bar** follow the size setting too; the status bar has a line above it and a tinted background, so it reads as its own strip.
- **Icon names wrap to two lines** as in Finder (UIKit only middle-truncates a single line, so the cut is at the end), in a two-line row that follows the size setting. **Sort chevron:** only the sorted column shows one, in a 22 pt square (the glyph fills only its middle half).
- **List keys:** Home and End select the first and last item as in Windows (Shift extends the selection to it), Page Up and Down scroll by a screen less a row; Delete moves the selection to the Trash and Shift+Delete deletes it permanently, each after its confirmation. On the Mac they come from the list's KeySink; on Windows from Go menu shortcuts, with Delete and Shift+Delete as before.
- **Icon view scroll bar at the pane's edge:** the wrap grid's spare room is a right content inset of the scroll view on Apple platforms, not the list's margin, which pulled the scroll bar in.
- **Trash in the sidebar is marked** while it is the folder shown, and the Mac shows it by Finder's name ("Trash", "Papperskorgen") in the tab and breadcrumb instead of ".Trash".
- **List or icons per folder:** the view picked in a folder is kept for it (Preferences, the 1000 latest), as Finder does; a folder without a choice opens in the view the tab has. Only a change counts as a choice, since the segmented control writes back the value it is given.
- **Path field closes on Esc and on any folder change:** an app-level Escape key command, taken only while the field is open, so sheets keep their own Esc.

## Decisions
- Asked 2026-10-15:
  - Sidebar as in the concept image, without the app name; Network and Backups shown dimmed.
  - Home: disks, Other Devices, Recent Locations, and later the Trash.
  - Mac Trash: ask for Full Disk Access at once (first run, or once at start for existing installs), since macOS lets apps read `~/.Trash` only with it.
  - Emptying: confirm with count and size, then empty in the app.
- The online dot on devices waits for discovery (#39); the app does not know whether a device is reachable yet.
- Home lives in the tab history as a location of its own provider id (`home`), so Back and Forward pass through it without special cases.
- The breadcrumb measures segments as laid out and keeps the widths of folded ones, since a hidden view measures nothing.
- Windows' Recycle Bin is counted and emptied through the shell (`SHQueryRecycleBin`, `SHEmptyRecycleBin`); it is not a folder the explorer can list, so its row opens Explorer's.
