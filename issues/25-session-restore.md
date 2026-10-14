# Issue #25 — Workspaces: the last session (part 1)

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/25
**Branch:** issue/25-session-restore
**Status:** In progress — the last session is restored; named workspaces (save / open / duplicate / delete) follow after split panes (#23)

## Plan
Start with plan §18's "last session": the windows and their tabs come back after a restart. Named workspaces are saved sessions of the same shape, so the storage is built for both.

## Changes
- Core `Workspaces/`: `TabState` (location, sort, hidden files, view), `WindowState` (tabs, active), `SessionStore` (JSON through a temporary file; a damaged file is kept aside). Two tests (76 in all).
- `Services/SessionTracker`: the open explorers in window order, saved half a second after any change (navigation, tabs, sort, view, hidden files). A closed window leaves the session. At start the first explorer reads it back once: it takes the first window's tabs and opens the other windows; tabs whose folder no longer exists (or does not answer in 2 s) are left out.
- `ExplorerWindows.Open` and `ExplorerPageViewModel.Start` take a list of tabs, so a moved tab and a restored window go the same way.

## Verification (Mac Catalyst)
1. A run left three windows: the main window at SpineFileManagerTest in list view, Källa in icon view, and a third at the test folder. `session.json` held all three.
2. A plain restart brought back three windows as they were (window captures).
3. The test session was deleted afterwards, so the next start opens the home folder in one window.

## Decisions
- Back/forward history is not saved, only where each tab is and how it shows (Finder and Edge do the same).
- Restoring is the default start; the "Home / last session / a workspace" choice arrives with the settings page and named workspaces.
