# Issue #5 — Basic FileExplorer on desktop

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/5
**Branch:** issue/5-file-explorer
**Status:** Completed (copy/cut/paste moves to #8 with the operations engine)

## Plan
The explorer as the app's root page, the default layout chosen 2026-10-14: a sidebar of places, a toolbar, a details list (Name, Date modified, Size, Kind) and a status line. The logic that can be tested without UI goes to Core.

## Changes
- Core `Explorer/`: `NavigationHistory` (back/forward like a browser), `ItemSort` (folders first, natural names, a column's second click reverses it, ties by name A–Z either way), `Selection` (click, Cmd/Ctrl-click, Shift-click from the anchor, arrow steps), `ByteSize` (decimal units, the culture's decimal separator). 14 new tests (45 in all).
- `StorageItem.DisplayName` and `IFileSystemPlatform.FolderDisplayName`: Finder's localised folder names ("Skrivbord", "Bibliotek", "Virtual Machines" without ".localized"), folders only.
- `Pages/ExplorerPage`: sidebar (places with icons, the current one marked), toolbar (back, forward, enclosing folder, refresh, show hidden files), error banner, sortable column headers, rows with selection, double-click to open (folders navigate, files open in their default app), status line ("18 objekt · 1 markerat"), the list reloads itself when the folder changes on disk (FileSystemWatcher, 300 ms debounce, selection kept). Narrow windows hide the sidebar, Date modified and Kind.
- New folder (suggests a free "New Folder (2)"), rename, move to trash, delete permanently (confirmed; also asked when the location has no trash) — all in English and Swedish.
- `Services/AppMenu`: the menu bar with Finder's shortcuts on the Mac (⇧⌘N, ⌘O, ↩ rename, ⌘⌫, ⌥⌘⌫, ⇧⌘., ⌘R, ⌘[ ⌘] ⌘↑ ⌘↓, arrows) and Explorer's on Windows (Ctrl+Shift+N, Enter, F2, Delete, Shift+Delete, Ctrl+H, F5, Alt+arrows, Ctrl+A). On the Mac the system's File, Format and View menus are removed; its Edit menu stays for text fields and its Select All reaches the explorer through `selectAll:` on the AppDelegate.
- `Services/ModifierKeys` (GCKeyboard on the Mac, InputKeyboardSource on Windows) for Cmd/Ctrl- and Shift-click; `Services/FileLauncher` (a file URL through UIApplication on the Mac, which opens the default app; Launcher elsewhere).
- `Pages/FirstRunPage`: the Mac's first launch gathers the TCC prompts (Desktop, Documents, Downloads, cloud folders) behind Continue, with a link to Full Disk Access; shown once.
- Folder, file, drive and cloud icons (SVG); the start page with only the places is gone.

## Verification (Mac Catalyst, Swedish)
Driven through the accessibility tree: selection and status line, open through ⌘O, New Folder → "Projekt 2026" created and selected, rename to "Arkiv 2026" with the selection following, files created from a shell appeared on their own, Edit › Select All → "3 objekt · 3 markerade", Move to Trash emptied the folder without a prompt, Back, sorting by Size. The first-launch sheet showed once and not after a restart. iOS simulator: the list at phone width. Windows, iOS and Android build. Real double-click and modifier clicks need a human (the tool sends AXPress).

## Decisions
- Copy, cut and paste wait for #8: they need conflict handling and stream copies across providers, which is the operations engine's job.
- Keyboard shortcuts ride on MAUI's menu bar rather than Spine (Spine has no key commands; noted in the capability map).
- The first-run sheet is shown after the first folder has loaded: presented from the root page's first `OnAppearingAsync` it never appeared and the awaited call held the explorer empty (a Spine candidate).
