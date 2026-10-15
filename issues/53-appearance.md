# Issue #53 — Appearance: light/dark, window material, resizable sections, chevron sort

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/53
**Branch:** issue/53-appearance (merged through issue/55-concept-ui)
**Status:** Completed

## Plan
Asked for during testing on 2026-10-14:
- resizable sections
- chevrons as sort arrows
- a Mica/acrylic-like window material
- light and dark mode
- a system tray with useful shortcuts

## Changes
- **Resizable sections:** the sidebar, the preview and the list's Modified, Size and Kind columns resize by a grip on their edge; widths are kept in Preferences. Later (#55): the pointer turns into the resize arrows and the grip's line into the accent; widths apply at most once a frame.
- **Sorting:** the sorted column shows a chevron.
- **Appearance:** match the system, light or dark, through Spine's `IThemeService`, in View › Appearance and in Settings (#55 step 1).
- **Window material:**
  - Windows: Mica.
  - Mac: an `NSVisualEffectView` (sidebar material, behind the window) under the content view (`Platforms/MacCatalyst/WindowMaterial.cs`); the sidebar is transparent over it and the files keep a solid background.
  - The window's `NSAppearance` follows the chosen theme, else the sidebar text vanished in light mode.
- **Tray** (menu bar on the Mac): New Window, Downloads, Link Device, Transfers; closing the window keeps the app running.

## Decisions
- The material is reached through the Objective-C runtime: Mac Catalyst has no binding for `NSWindow` or `NSVisualEffectView`. The window is found through `NSWindowDidBecomeKeyNotification`.
- `Application.Current` is null in `FinishedLaunching`; the theme subscription waits for the first key window (a launch crash otherwise).
