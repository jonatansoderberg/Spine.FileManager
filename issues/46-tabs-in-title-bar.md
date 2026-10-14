# Issue #46 — Tabs in the title bar, no window title

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/46
**Branch:** issue/46-tabs-in-title-bar
**Status:** Completed

## Plan
On the Mac the explorer reaches into the title bar. The tab bar sits in the title bar's row, right of the traffic lights over the sidebar, and the window shows no title.

## Changes
- **The main window:** `ExplorerPage` is a full-bleed Spine page on the Mac (`SafeAreaEdges = None`, no header bar). Spine moves it up under the title bar and hides the window's title.
- **Further windows:** they hide the title through the scene's titlebar and move the page up themselves.
- **`TitleBarHeight`:** read from Spine's `SafeAreaInsets`, or from the window's safe area in a window of its own, once either is known. Dual Pane's second pane takes the first's.
  - The tab bar is as tall as the title bar, with its tabs centred on the traffic lights' row.
  - The sidebar starts below the traffic lights.
  - Without a sidebar, the tab bar keeps clear of the traffic lights.

## Decisions
- **Spine's own full-bleed mode** for the main window rather than AppKit calls in the app.
- **Dragging the tab bar's empty part moves the window** (checked on the Mac). Tabs still drag as tabs.
- **Other platforms keep their safe areas.** The attribute differs only on the Mac.
