# Spine capability map

**Inventoried:** 2026-10-14 against Maui.Spine `master` at 29d4196 (latest release v0.1.14). Paths are relative to the Maui.Spine repository.

Spine is built for mobile-first, single-window apps. It already covers menus, context menus, sheets, page actions, search, state views, theming, strings and rows well. The desktop core of a file manager is missing: split panes, document tabs, multiple windows, keyboard shortcuts, drag and drop and multi-selection.

Actions: **Use** as is · **Extend** in Spine (PR in Maui.Spine, release, bump `SpineVersion`) · **Build** in File Manager · **Spike** first.

| Requirement | Existing Spine component (package) | Gap | Action |
|---|---|---|---|
| Navigation: regions, history, back | `[NavigableRegion]`, `INavigationService` (`NavigateToAsync`, `ShowAsync`, `BackAsync`, `SetRootAsync`, `NavigateToWithResultAsync`), `OnBackRequestedAsync`, back-swipe (`Plugin.Maui.Spine`); `docs/wiki/regions.md` | One root region per app. No forward history, no named or secondary regions. `NavigationRegion` and the host pages are DI singletons (`src/Plugin.Maui.Spine/Extensions/MauiAppBuilderExtensions.cs:69-132`). | Use for page flow; **Build** folder history and breadcrumbs. |
| Sheets | `[NavigableSheet]` with detents, overlays, dismiss guard, nested stack; `docs/wiki/sheets.md` | On Windows a bottom-sheet host, not a desktop dialog. | Use |
| Tabs | `[NavigableTab]`, `SwitchToTabAsync`, `ITabBadgeService`; `TopTabs` / `SegmentedControl` for in-page tabs | At most 5, declared statically by attribute, not closable or reorderable, no dynamic tabs; Windows gets a plain `TabbedPage` (`docs/wiki/tab-host.md:22,48`). `TopTabs` is a segmented control over static content. | **Build** document tabs. Tab host fits mobile's Files / Devices / Transfers / Backup. |
| Flyouts / popovers | `SpineOverlay.Show(view, back, passTouches)` (`src/Plugin.Maui.Spine/Core/SpineOverlay.cs`); an anchored action sheet is a popover on iPad, a flyout on Windows | No general popover anchored to a view with arbitrary content (the breadcrumb chevron's folder list needs one). | **Spike** on SpineOverlay, then Extend. |
| Dialogs / alerts | None of its own; MAUI `DisplayAlert`; `ShowActionsAsync` for a destructive confirm (`docs/wiki/menus.md`) | No text-input prompt (rename, new folder), no custom modal, no conflict dialog (Replace / Skip / Keep both). | **Build**; a sheet can stand in. |
| Action sheets | `INavigationService.ShowActionsAsync(ActionSheet, anchor)` with `MenuAction` rows: `UIAlertController`, Material `BottomSheetDialog`, `MenuFlyout` on Windows | Flat list, no sections or submenus. | Use (mobile selection actions). |
| Menus / menu buttons | `MenuItems`, `MenuAction`, `MenuSection`, `SubMenu`, `MenuPicker`; `MenuButton.Items`; `PageAction(title, menu)` — native on all four | No app menu bar (Mac has only a tray item, `src/Plugin.Maui.Spine/Platforms/MacCatalyst/SpineApplication.MacCatalyst.cs:100`; Windows none). Menu rows carry no accelerator text. | Use for buttons; **Build** a menu bar with MAUI `MenuBarItem`, or Extend. |
| Context menus | `ContextMenu.Items`, `ContextMenu.CommandParameter`, `ContextMenu.Show(view)`: long-press on touch, right-click on Mac/Windows/Android; `DataGrid.RowContextMenu` | No multi-selection context (one parameter). `Show(view)` returns false on Apple platforms. | Use; Extend for selections. |
| Keyboard | Nothing. Spine's "shortcuts" are launcher quick actions, jump list and tray (`IShortcutHandler`, `docs/wiki/shortcuts.md`). Only soft-keyboard avoidance and Escape in the Windows Spotlight overlay. | No `UIKeyCommand` / `KeyboardAccelerator` anywhere; ⌘F deferred (`docs/proposals/spine-header-search.md:70`); no arrow-key list navigation or focus management. | **Spike**, then Extend. |
| Drag and drop | `Reorder.Mode` within one `CollectionView` (`docs/wiki/reorder.md`) | No drag between lists, none with the OS (`UIDragInteraction`, WinUI), no drop targets, no file promises; reorder skips grouped lists. | **Spike**, then Build (file drag) / Extend (primitives). |
| Responsive layout, split view | Only `DataGrid` named layouts via `AdaptiveTrigger` (`docs/wiki/data-grid.md:59`); search moves into the bar at ≥ 600 pt on iPad/Mac | No sidebar, split view, master/detail, breakpoint service or resizable splitter. | **Build** (split host may later move to Spine). |
| Lists, selection, sorting | `DataGrid` (`Plugin.Maui.Spine.Controls.DataGrid`): layouts, sorting, grouping, swipe, load more, cell copy; `HeroCollectionView`; `SafeArea.PageMargin`, `SafeArea.ScrollInset` | DataGrid hard-codes `SelectionMode.None` (`DataGrid.cs:60`); no multiselect, column resize, column drag or horizontal scroll; striping is O(n) (`data-grid.md:304`). | **Build** the file list on `CollectionView` with own selection; Spike 2 measures. |
| Reorder | `Reorder.Mode` / `Reorder.Command` / `Reorder.IsHandle`, all four | Single list. | Use (favorites). |
| Search | `[PageSearch]` / `PageSearch`; Windows title-bar field; `ISearchIndex` for Spotlight/Android | No suggestions, scopes or ⌘F; Windows field untested (Maui.Spine #501). | Use; Extend for scopes. |
| Languages | `ISpineStrings` (`Plugin.Maui.Spine.Common`): XML documents per culture, `{String Key}` in XAML with args and plurals, runtime switch stored in `Preferences`, `.resx` provider; `docs/wiki/strings.md` (v0.1.12+) | `[NavigableRegion(Title = …)]` is literal text and `NavigableMeta.Apply` writes it to `vm.Title` on navigation (`src/Plugin.Maui.Spine/Services/NavigableMeta.cs:25`), so a localised title must be set in `OnAppearingAsync`. | Use; **Extend**: a string key for navigable titles. |
| Themes / dark mode | `IThemeService`, `SpineTheme`, token dictionaries, `Material.Kind`, Mica/Acrylic `options.Windows.Backdrop` | — | Use |
| Animations / transitions | `ISpineTransitions`, shared element and zoom, `SpineTranslateToAsync`, `Highlight`, `Shimmer`, `AnimatedLabel` | Shared element/zoom fall back on Windows. | Use |
| Windows and multi-window | `options.Windows.*` (size, min size, persisted position, backdrop, tray, `CloseToBackground`, `AllowMultipleInstances` default false); `SpineApplication.MinimizeWindow` / `MaximizeWindow` / `ToggleFullscreen` / …; Windows title bar with page actions | Single window by design: `CreateWindow` reuses the singleton host page (`src/Plugin.Maui.Spine/Core/SpineApplication.cs:56-79`). No scenes, no new-window API, no Mac window options beyond the tray. | **Spike 4**; likely Extend (a host and navigation service per window). |
| Platform services | `Plugin.Maui.Spine.Biometrics`, `.BackgroundTasks` (Windows/Mac only while running), `Haptics`, `SpineScreen.ExcludeFromCapture` | No file pickers, share, secure storage or file-system wrappers. | MAUI Essentials; **Build** file-system access (Spike 1). |
| QR / barcodes | `Plugin.Maui.Spine.Barcodes` (`Barcode.Encode`, `BarcodeView`, all four); `Plugin.Maui.Spine.Scanner` (`BarcodeScannerView`, `BarcodeScannerPage`) | No scanner on Windows (fine: the computer shows the code, the phone scans). | Use (Spike 5). |
| Operations / progress | `TaskState<T>`, `DataGrid` status text, `Highlight`, push/local notifications | No progress bar, toast/snackbar, status bar or queue UI. | **Build** the Operation Center. |
| Preview | `MarkdownView`, Lightbox (photo pager, zoom, share), `Plugin.Maui.Spine.Images` (cache, downsampling, BlurHash) | Image cache targets `UriImageSource`, not local thumbnails; no text, PDF or media preview. | Use the parts that fit; **Build** thumbnails and preview. |
| Icons | `Plugin.Maui.Spine.Svg.Icons` (228, `SpineIcons`), `SvgImageSource` | No Folder, File, Document, Drive, USB, Archive, Cut, Paste or Rename. Usable: Copy, Trashcan, Download, Upload, Cloud, Share, Image, Text, Lock, Tag, Star, Pin, Sort, Filter, Grid, List, Phone, Tv, Info. | **Extend** via `/spine-symbol` in Maui.Spine. |
| Header bar page actions | `[PageAction]`, `PageActionRole`, menus on actions; Windows title bar `LeadingContent` / `TrailingContent` | No toolbar overflow on desktop. | Use |
| Rows / settings | `SpineRow`, `SpineSection` (`Plugin.Maui.Spine.Controls.Rows`), `Tap.Command`, `Semantic.Merge` | — | Use (settings, properties). |
| State views | `TaskState<T>`, `StateView`, `Load(...)`, `Skeleton.IsActive` | Not yet run on a Windows device (`loading-states.md:45`). | Use |
| Showcase patterns | `samples/MauiSpineSampleApp/Pages/`: three-file pages, ContextMenus, Menus, ActionSheets, DataGrid, Reorder, Search, State, Rows, PageActions, Theme, Sheets | No desktop layout sample. | Copy |

## Gaps that matter most

1. **Multiple windows.** One singleton host page, navigation region and navigation service per app (`SpineApplication.cs:56-79`, `MauiAppBuilderExtensions.cs:54-125`), single instance on Windows by default. A second window needs a host per window in Spine or secondary windows outside Spine. Spike 4 decides.
2. **Split panes and sidebar.** No split host; every page fills the one region. Sidebar, tree and splitter are built inside one page.
3. **Document tabs.** The tab host is a static bottom bar of at most five (`docs/proposals/spine-tab-host.md:17-23` lists desktop chrome as a non-goal). Browser-style tabs are a custom strip plus a state-per-tab model here.
4. **Keyboard shortcuts.** No key commands or accelerators in Spine at all; menus show none. A file manager without Ctrl/Cmd+C, V, T, W, arrows and Enter is not usable on desktop — the most likely first Spine extension.
5. **Drag and drop.** Same-list reorder only.
6. **Multi-selection.** DataGrid has none, context menus and action sheets take one parameter.
7. **Menu bar.** Mac only a tray item, Windows nothing.
8. **Operations UI and dialogs.** No progress bar, toast, status bar, input prompt or conflict dialog.
9. **File-type icons.** No folder, file, drive or device symbols.
10. **Windows maturity.** StateView, Highlight, Shimmer, Images, the title-bar search and Rows taps compile but have not run on a Windows device (Maui.Spine #501). Verify early — Windows is a first-class target here.

## Spine candidates found while building

Solved in this repository for now (decision 2026-10-14); each could move into Spine later.

- **Key commands and a menu bar.** The explorer's shortcuts ride on MAUI's `MenuBarItems` (`Services/AppMenu.cs`); on the Mac the system's File/Format/View menus are removed in `AppDelegate.BuildMenu` and Edit's Select All is caught as `selectAll:` at the end of the responder chain. A Spine menu-bar builder with per-platform shortcuts would serve every desktop app.
- **Modifier keys for clicks.** MAUI's tap events carry no modifiers; `Services/ModifierKeys.cs` reads GCKeyboard (Mac) and InputKeyboardSource (Windows).
- **Localised navigable titles.** `[NavigableRegion(Title = …)]` is literal text (see the table).
- **Rebuilding a native menu per `MenuItems` change.** Filling a menu button's items one by one rebuilt the menu each time: 500 rows froze the window for 27 s (Spike 2). Build the `MenuItems` first and assign it once; a batch-update API in Spine would prevent the trap.
- **CollectionViewHandler2 with 100 000 rows.** MAUI's compositional-layout handler lays out every row up front; the file lists use the flow-layout handler instead (`Controls/FileListView`). Worth knowing for Spine's DataGrid and HeroCollectionView with large lists.
- **A sheet from the root page's first appearance does not show** on Mac Catalyst, and `ShowAsync` awaited there never returns, so the page stays empty. The explorer shows its first-run sheet after its first folder has loaded instead.

## Observed while setting up

- On Mac Catalyst the start page shows no header bar title under the traffic lights; the window title shows `AppTitle`. Check what Spine intends on the Mac before the explorer's header is designed.
