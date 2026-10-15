# The explorer after the concept image

**Roadmap issue:** [#55](https://github.com/jonatansoderberg/Spine.FileManager/issues/55)
**Last updated:** 2026-10-15 — steps 3, 4 and 8 done; 1, 2, 5 and 6 in progress.

The concept image (October 2026, from Jonatan) shows the desktop explorer in light mode, a Home page, a split view, the iPhone and the iPad. This plan takes the app there step by step. Each step is one PR, with the image as the reference and a screenshot in light and in dark mode before it merges.

## Where the app is and where the image is

| Area | Today | In the image |
|---|---|---|
| Sidebar | Favorites, Places, Other Devices; accent glyphs; rounded selection | The app's name and icon on top; **Home**; Favorites with coloured folder icons; **This Device**; Other Devices with device icons and a green dot when online; **Network**, **Backups**; *Link Device…* as a filled button; **Settings** at the bottom |
| Tabs | A plain row in the title bar; text and × | Edge-like: an icon per tab, the active tab raised and lighter with rounded top corners, a **+** for a new tab |
| Breadcrumb | Segments with a ▸ menu after each | The same, in a field-like capsule, the last segment with a drop-down; Refresh and **Search** beside it |
| Command bar | Back, forward, up, refresh; view switch; hidden files | Windows-like: **+ New**, Cut, Copy, Paste/Duplicate, Rename, **Delete** (red); view switch with three modes (list, icons, columns) |
| List | Coloured type icons, chevrons, resizable columns | Kinds spelled out ("JPG image", "PDF document"), selection a light-blue rounded row |
| Preview | Picture, name, a few facts | Large picture, name, *kind · size* in the accent, **Open** with a drop-down (Open With), facts (Created, Modified, Size, Kind, Dimensions, Location as a link), quick actions **Share**, **Copy Path**, **Send to Device**, **…**, a close button |
| Status / operations | Status text; operation progress in the bar | An **operation bar**: the transfer's icon, *MacBook Pro → iPhone 17*, progress, speed and time left, pause and cancel; on the right *5 items · 1 selected · 499 KB* |
| Home | — | Favorites as cards, Other Devices as cards with their state, Recent files, Transfers, Backups; a search for files, devices and more |
| Split view | Dual Pane, each pane with its own tabs | The same, two panes side by side with separate tabs and one operation bar |
| iPhone | The desktop UI as is | Files with a breadcrumb, rows with thumbnails and two lines, a bottom tab bar (Files, Devices, Transfers, Backup) |
| iPad | The desktop UI as is | Dual pane between devices (iPhone 17 ↔ Gaming PC), photos as a grid |
| Appearance | View › Appearance in the menu | A Settings place with light, dark or the system's |

## Steps

| # | Step | What | Status |
|---|---|---|---|
| 1 | **Settings and appearance** | *Settings* at the bottom of the sidebar opens a sheet: Appearance (Match System / Light / Dark as a segmented control), window material on/off, icon style, where received files go. The View menu keeps its entries. | 🔄 |
| 2 | **Colours and type** | One palette for light and dark (Spine theme tokens): background, sidebar, surfaces, separators, the selection's light blue, the accent; type sizes for sidebar, list, headers and preview. Light mode checked against the image. | Started: Settings › Size of files and folders (Small / Medium / Large) sets row height, icons and text in the lists and the sidebar, through app resources every window follows. 🔄 |
| 3 | **Sidebar** | The app's name on top, Home, This Device, Network and Backups (placeholders until their providers), coloured folder icons for favorites, device icons with an online dot, Link Device as a button, Settings at the bottom; sections as in the image. | Done 2026-10-15 without the app name (Jonatan's choice); Network and Backups dimmed until built; the online dot waits for discovery (#39). ✅ |
| 4 | **Tabs** | Edge-like tabs: icon, raised active tab, + button, close on hover; still in the title bar's row (no scroll view there, see #46). Windows tab shortcuts: Ctrl+T/W, Ctrl+Shift+T reopens a closed tab, Ctrl+Tab, Ctrl+1–8 and Ctrl+9 for the last (⌘⌥ on the Mac, where ⌘1/⌘2 pick the view), Ctrl+Enter and Ctrl-double-click open a folder in a new tab. | ✅ |
| 5 | **Command bar and breadcrumb** | A second row: New, Cut, Copy, Paste, Duplicate, Rename, Delete (red); the view switch with a columns view; breadcrumb in a capsule with drop-downs on its separators; Refresh and Search beside it. Done: the two rows, the capsule taking all the room left, segments folding from the root end into « as in Windows' address bar, search folding out of a magnifier. The command bar as in the image: a framed + New with its label, the clipboard and rename in one framed group, Delete in red, glyphs about 18 points. Left: a columns view. | 🔄 |
| 6 | **Preview pane** | Large picture, kind · size, Open with Open With, the facts (dimensions from the image, location as a link), quick actions Share, Copy Path, Send to Device, More; closable. | Built 2026-10-15 (#62 branch): picture, name, kind · size, Open and Open With (NSWorkspace), facts with dimensions and the location as a link, Share, Copy Path, Send to Device, More (Show in Finder, Add to Favorites, Rename, Delete), × to close. Not yet tried by hand. 🔄 |
| 7 | **Operation bar and status** | The active operation with source → target, progress, speed, time left, pause and cancel at the bottom of the window; item count, selection and its size on the right. | ⬜ |
| 8 | **Home** | A start page: Favorites and Other Devices as cards, Recent files, Transfers, Backups, search for files and devices. New tabs open on Home. | Done 2026-10-15 with what Jonatan picked: disk cards (capacity, free space as Finder counts it, % used, red from 90 %), Other Devices, Recent Locations; new tabs and a first start open Home; Home is in the tab history. ✅ |
| 9 | **Split view** | Dual Pane as in the image: a pane's own tabs and breadcrumb, one shared operation bar, a draggable divider (done). | ⬜ |
| 10 | **iPhone** | Milestone E: Home as the start, Files with breadcrumb and two-line rows with thumbnails, a bottom tab bar (Files, Devices, Transfers, Backup), a sheet for the selected file (Open, Share, Send to Device), locations added through the Files picker. | ⬜ |
| 11 | **iPad** | Dual pane between devices, photos as a grid, the same tabs as the phone. | ⬜ |

Steps 1–7 are the desktop window and can go in any order after 2; 8 needs 3 (Home is a sidebar entry); 10–11 build on 8 and on milestone E's storage work (picked folders, security-scoped bookmarks — Spike 1).

## Decisions

- **The image is direction, not pixels**, as the spec says of its own concept images. Where the image and the platform disagree (the Mac's title bar takes clicks over a scroll view, #46), the platform wins.
- **Light mode is the reference**, dark mode is designed to match it; the system's choice is the default.
- **Placeholders for what does not exist yet** (Network, Backups, Recent): shown when they have something, so the sidebar does not offer empty places.
