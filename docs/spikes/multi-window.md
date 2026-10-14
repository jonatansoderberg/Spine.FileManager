# Spike 4 — Multiple windows and moving a tab to a window

**Issue:** [#22](https://github.com/jonatansoderberg/Spine.FileManager/issues/22) · **Run:** 2026-10-14 on Mac Catalyst

**Question:** can the explorer open more windows, and can a tab move to a window of its own without losing its folder, history or view, while Spine hosts exactly one window?

## What Spine allows

`SpineApplication.CreateWindow` wraps one singleton host page (`SpineHostPage`), and `NavigationRegion` and `INavigationService` are singletons too. A second Spine-hosted window would put the same page into two windows, which MAUI refuses. But `SpinePage<TViewModel>` is a `ContentView` that resolves its view model from DI in its constructor, and navigable pages and their view models are **transient** by default (`NavigableAttribute.Lifetime`). So a second window can be a plain MAUI `Window(new ContentPage { Content = new ExplorerPage() })` holding its own explorer and view model.

## What was built (kept, not thrown away)

- **Scenes on the Mac:** `UIApplicationSceneManifest` with multiple scenes and MAUI's `SceneDelegate` (`Platforms/MacCatalyst`). Spine's main window keeps working under scenes.
- **`Services/ExplorerWindows.Open(tab, from)`:** opens a window with a new `ExplorerPage`, started either from a tab moved out of another window (its history, sort, view, hidden-files and selection come along) or from a folder. MAUI's `OpenWindow` brings it up.
- **The menu bar follows the window in focus:** every menu item forwards to `AppMenu.Explorer`, which the window's `Activated` sets, instead of being bound to the first explorer. Prompts and alerts go to the explorer's own window.
- **Commands:**
  - File › New Window (⌘N / Ctrl+N) opens a window at the current folder.
  - File › Move Tab to New Window, also on a tab's right-click menu, moves the active tab out (only when the window has more than one tab).

## Result

The run, through a scratch harness with a capture of each window:

1. The main window opened Källa in a second tab and switched it to icons.
2. Move Tab to New Window left the main window with one tab, at SpineFileManagerTest.
3. The new window showed Källa in icon view with its own sidebar (12 places, 2 favorites) and one tab.
4. New Window brought a third window, at the main window's folder.

**Exit criterion met:** a tab moves to another window without losing its location or history.

## Limits found

- **Spine sheets show in the main window only.** `INavigationService` belongs to the Spine host, so the Operation Center and the first-run sheet open over the main window even when asked from another. The explorer's own prompts and alerts already go to its own window.
- **Attaching a tab back** into another window, and tear-off by dragging, came with #41: the state moves as the same object.
- **A window opened at runtime draws shifted up** under its title bar (by about 100 pt) until it is resized: it is laid out at another window's size. `ExplorerWindows.SettleLayout` resizes it by a point and back once it is on screen.
- **A drag between windows reaches the other window only when nothing modal is in front.** With a TCC prompt waiting, the other window never saw the drag.
- **Info.plist changes need `obj/…/AppManifest.plist` removed** before a build picks them up. The first run had no scenes for that reason, and `OpenWindow` did nothing without a word.
- **Windows (OS)** and the iPad: `OpenWindow` is MAUI's on both, but neither was run. The iPad also needs the scene manifest in `Platforms/iOS`.
- **Windows are not restored after a restart.** That is the workspaces' job (#25).

## For Spine

A host per window, so that each window has its own region and `INavigationService` (and sheets open in their own window), would let every Spine app have more windows. Until then the pattern here works without Spine changes.
