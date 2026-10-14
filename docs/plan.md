# Spine File Manager — Requirements and Plan

**Last updated:** 2026-10-14 — Spike 6 transport (#37); tab drag (#41); Spike 5 identity and pairing (#36); preview (#34), filter (#32), Dual Pane (#23 part 1), last session (#25 part 1), windows (#22), terminal (#24), tabs (#21), journal (#20); milestone A done on the Mac; Windows (#3) compiled only.

A living plan. Each step links its GitHub issue once it has one; update the status, the decisions and the line above in the same PR as the work.

## 1. Product goal

A modern cross-platform file manager in .NET MAUI, with Spine as its UI and platform foundation. It combines:

- Windows Explorer-like productivity
- Finder-like preview and simplicity
- Edge-like tabs and window handling
- Commander-like dual/split panes
- AirDrop-like device transfer
- remote browsing of other devices
- resumable copy/move/backup
- terminal integration
- saved workspaces/layouts
- Windows, macOS (Mac Catalyst), Android and iOS
- multiple languages — English and Swedish to begin with

Desktop is the most complete experience. Mobile uses the same concepts and backend, adapted to touch, smaller screens and each platform's file-system limits.

## 2. Architecture principles

### Spine versus File Manager

Spine holds only general building blocks: window abstractions, tab infrastructure if generally useful, split/docking infrastructure, command/shortcut infrastructure, popovers/sheets, drag-and-drop primitives, a QR/scanner control, operations/progress infrastructure that knows nothing about files.

File Manager owns everything file-specific: FileExplorer, FileBreadcrumbBar, FileList/FileGrid, FilePreview, storage providers, file operations, the transfer queue, remote storage, photo backup, terminal integration, favorites, file workspaces.

> If a component knows what a file, folder, disk or path is, it normally belongs in this repository.

A Spine gap is fixed by a PR in Maui.Spine, a release, and a `SpineVersion` bump here — never by copying framework code into this repository.

### The central abstractions

The centre is not a *file path* but **provider + location + capabilities**. The operation model is not `CopyAsync()` but a **persistent operation + transfer planner + provider capabilities**. That is what lets the same explorer later handle Windows, macOS, Android, iOS Files, iPhone Photos, NAS, remote Spine devices, cloud, ZIP and SFTP without being rewritten.

## 3. Solution structure

Today: `Spine.FileManager.App` (MAUI), `Spine.FileManager.Core` (plain .NET), `Spine.FileManager.Tests` (xUnit). Projects are split out when the code arrives, not ahead of it. The intended boundaries:

```text
Spine.FileManager.App
Spine.FileManager.Core          Models, Commands, Navigation, Workspaces, Settings
Spine.FileManager.Controls      FileExplorer, FileList, FileGrid, FileBreadcrumbBar, FilePreview,
                                FilePane, FileTabs, Favorites, OperationCenter
Spine.FileManager.Storage       IStorageProvider, StorageItem, LocalStorageProvider, capabilities
Spine.FileManager.Platforms.*   Windows, Mac, Android, iOS
Spine.FileManager.Operations    Copy, Move, Delete, Rename, Queue, Journal, RetryResume
Spine.FileManager.DeviceLink    Identity, Pairing, Discovery, Trust
Spine.FileManager.Transfer      Transport, Chunking, Resume, Verification, Scheduling
Spine.FileManager.RemoteStorage
Spine.FileManager.Backup
Spine.FileManager.Terminal
```

They need not become separate NuGet packages; the split is for architectural boundaries.

## 4. Build order

Status: ✅ done · 🔄 in progress · ⬜ not started.

### Milestone A — Local Explorer
Result: a usable desktop file manager. **Done on the Mac 2026-10-14;** Windows has compiled at every step but not run (#3), and Spike 1's Windows and hardware pass is open (#1).

| # | Step | Status | Issue |
|---|---|---|---|
| A1 | Phase 0 — Spine inventory (§5) | ✅ | — |
| A2 | Spike 1 — file-system capabilities (§6) | 🔄 | [#1](https://github.com/jonatansoderberg/Spine.FileManager/issues/1) |
| A3 | Storage abstraction (§7) | ✅ | [#2](https://github.com/jonatansoderberg/Spine.FileManager/issues/2) |
| A4 | Windows local provider | 🔄 compiled, not run | [#3](https://github.com/jonatansoderberg/Spine.FileManager/issues/3) |
| A5 | macOS local provider | ✅ | [#4](https://github.com/jonatansoderberg/Spine.FileManager/issues/4) |
| A6 | Basic FileExplorer (§8) | ✅ | [#5](https://github.com/jonatansoderberg/Spine.FileManager/issues/5) |
| A7 | Breadcrumb bar (§8) | ✅ | [#6](https://github.com/jonatansoderberg/Spine.FileManager/issues/6) |
| A8 | List/grid + Spike 2 — file-list performance (§9) | ✅ | [#7](https://github.com/jonatansoderberg/Spine.FileManager/issues/7) |
| A9 | Local operations | ✅ | [#8](https://github.com/jonatansoderberg/Spine.FileManager/issues/8) |
| A10 | Favorites (§15) | ✅ | [#9](https://github.com/jonatansoderberg/Spine.FileManager/issues/9) |

### Milestone B — Power User Desktop
Result: Explorer/Finder plus Edge/Commander productivity.

| # | Step | Status | Issue |
|---|---|---|---|
| B1 | Operations engine + Operation Center (§10–12) | ✅ | [#20](https://github.com/jonatansoderberg/Spine.FileManager/issues/20) |
| B2 | Tabs (§13) | ✅ | [#21](https://github.com/jonatansoderberg/Spine.FileManager/issues/21) |
| B3 | Spike 4 — multi-window + tab detach (§14) | ✅ | [#22](https://github.com/jonatansoderberg/Spine.FileManager/issues/22) |
| B4 | Multiple windows | ✅ Mac | with #22 |
| B5 | Split panes (§15) | 🔄 Dual Pane | [#23](https://github.com/jonatansoderberg/Spine.FileManager/issues/23) |
| B6 | Drag tabs between windows | ✅ Mac | [#41](https://github.com/jonatansoderberg/Spine.FileManager/issues/41) |
| B7 | Terminal integration (§17) | ✅ Open in Terminal | [#24](https://github.com/jonatansoderberg/Spine.FileManager/issues/24) |
| B8 | Workspaces (§18) | 🔄 last session restored | [#25](https://github.com/jonatansoderberg/Spine.FileManager/issues/25) |

### Milestone C — Device Fabric
Result: AirDrop-like transfer with robust resume.

Node identity (§19) ✅ · Spike 5 — QR pairing (§20) ✅ [#36](https://github.com/jonatansoderberg/Spine.FileManager/issues/36) · Spike 6 — transport ✅ [#37](https://github.com/jonatansoderberg/Spine.FileManager/issues/37) · Other Devices UI [#38](https://github.com/jonatansoderberg/Spine.FileManager/issues/38) · LAN discovery [#39](https://github.com/jonatansoderberg/Spine.FileManager/issues/39) · Other Devices (§21) · connection lifetime · names/aliases · LAN discovery (§22) · Spike 6 — transport (§23) · transfer engine · retry/resume (§27) · distributed Operation Center (§26).

### Milestone D — Remote Explorer
Result: other computers feel like more storage providers.

RemoteStorageProvider (§24) · browse remote file systems · copy local ↔ remote · remote ↔ remote, controller separate from the data path (§25) · remote tabs/favorites/workspaces · MTP/PTP devices ([below](#mtp-and-ptp-devices), spike [#11](https://github.com/jonatansoderberg/Spine.FileManager/issues/11)).

### Milestone E — Mobile
Result: full mobile client.

Android provider · iOS provider · mobile file browser (§29) · mobile tabs (§30) · Other Devices on mobile · send/receive · mobile Operation Center.

### Milestone F — iPhone Backup
Result: robust iPhone photo backup without a Spine cloud.

Spike 7 — iOS background transfer (§31) · PhotoLibraryProvider (§32) · incremental photo manifest · backup engine (§33–34) · background/resume behaviour · Photos → PC backup.

## 5. Phase 0 — Spine inventory ✅

Find what Spine already offers and which Spine controls really need work: navigation, tabs, flyouts/popovers, dialogs, sheets, menus and context menus, keyboard handling, drag and drop, responsive layout, lists and virtualization, themes, animations, window management, multi-window, platform services, Showcase patterns.

Output: [`spine-capability-map.md`](spine-capability-map.md) — requirement, existing Spine component, gap, action.

**Exit:** we know exactly what File Manager can reuse and which Spine controls need improving.

## 6. Spike 1 — File-system capabilities

The first technical spike: what file access is practical on each platform.

- **Windows:** C:, other local volumes, USB, a network share, the OneDrive folder; copy, move, rename, delete, file watching.
- **macOS (Mac Catalyst, sandboxed):** Documents, Downloads, an arbitrary folder, a removable drive, a network volume, security-scoped access, persistent access after restart (bookmarks).
- **Android:** app storage, shared storage, an SAF tree, Downloads, SD card, USB if available, persisted URI permissions.
- **iOS:** the app's own files, the Files/document picker, picked folders, the iCloud provider; the Photo Library separately.

Output: the capability model, for example

```csharp
[Flags]
public enum StorageCapabilities
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    Create = 1 << 2,
    Rename = 1 << 3,
    Delete = 1 << 4,
    Copy = 1 << 5,
    Move = 1 << 6,
    Watch = 1 << 7,
    Search = 1 << 8,
    RandomAccess = 1 << 9,
    NativePath = 1 << 10,
}
```

**Exit:** a throwaway test app can browse, open, create, rename, copy and delete on each platform, wherever the OS allows it.

## 7. Phase 1 — Storage abstraction

```csharp
public interface IStorageProvider
{
    StorageProviderId Id { get; }
    StorageCapabilities Capabilities { get; }

    Task<IReadOnlyList<StorageItem>> GetChildrenAsync(StorageLocation location, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(StorageLocation location, CancellationToken cancellationToken);
    Task<Stream> OpenWriteAsync(StorageLocation location, CancellationToken cancellationToken);
    Task CreateFolderAsync(StorageLocation parent, string name, CancellationToken cancellationToken);
    Task RenameAsync(StorageLocation location, string newName, CancellationToken cancellationToken);
    Task DeleteAsync(StorageLocation location, CancellationToken cancellationToken);
}
```

`StorageLocation` is provider-neutral — never a `string FullPath` as the identity:

```csharp
public sealed record StorageLocation(StorageProviderId ProviderId, string ItemId, string DisplayPath, string? NativePath = null);
```

so that `C:\`, `/Users/jonatan`, `content://…`, an iCloud provider, `spine://device/…` and `photos://…` all go through the same UI.

## 8. Phase 2 — First FileExplorer on desktop

Windows and macOS first; mobile once the model is stable.

V0.1: sidebar, breadcrumb, list view, grid view, selection, multiselect, sorting, navigation history (back, forward, up), refresh, new folder, rename, delete, copy, cut, paste.

**Breadcrumb.** Windows-like on every platform: `Users > Jonatan > Projects > Spine`. Both the segment and the separator are interactive: a segment navigates there, the chevron lists that folder's subfolders (`Projects >` → Maui.Spine, PWOS, Orientera, FileManager). It works with every provider, remote included.

## 9. Spike 2 — File-list performance

Folders of 1 000, 10 000, 50 000 and 100 000 items. Measure initial render, scroll FPS, memory, sort time, selection, metadata loading, thumbnail loading.

Metadata loads lazily: the first model holds only name, kind and a basic icon; size, modified date, thumbnail, tags and extended metadata arrive asynchronously.

**Exit:** 10 000 items still feel instantly usable.

## 10. Phase 3 — Operations engine

The explorer never copies files itself; it enqueues an operation (`OperationManager.Enqueue(new CopyOperation(…))`).

States: Queued, Preparing, Running, Paused, WaitingForDevice, WaitingForNetwork, Retrying, Verifying, Completed, Failed, Cancelled.

Every operation is journaled — id, type, source, destination, items, progress, checkpoint, errors, retries, created, updated — so the app can crash at 82 GB of 140 GB and still know the operation after a restart.

## 11. Spike 3 — Local copy/move performance

Managed versus native: on Windows `FileStream.CopyToAsync`, native copy, same-volume move, big files, many small files, NVMe/HDD/USB; on macOS managed copy, native copy, same-volume move, APFS clone (copy-on-write).

Output: a `TransferPlanner` that picks the strategy — same-volume move → native rename; clone possible → native clone; local copy → native optimised copy; otherwise a streaming pipeline.

## 12. Phase 4 — Operation Center

First-class UI. Compact, at the foot of the window:

```text
2 active operations
MacBook → Gaming PC
██████████░░ 67%
112 MB/s · 4m remaining
```

Full: source, destination, current file, bytes, files, speed, average speed, ETA, retry count, warnings, connection, started, elapsed. Actions: pause, resume, cancel, retry, prioritise, move up/down, show source, show destination. History: active, queued, completed, failed.

## 13. Phase 5 — Tabs and multiple windows

```text
App
 ├ Window 1
 │   ├ Pane → Tabs
 │   └ Pane → Tabs
 └ Window 2
     └ Pane → Tabs
```

Each tab keeps its location, history, view mode, sort and filter. Ctrl/Cmd+T, Ctrl/Cmd+W, middle-click a folder for a new tab, Open in New Tab, Open in New Window, reorder, detach, attach to another window.

## 14. Spike 4 — MAUI multi-window and tab detach

Validate before building much UI: two MAUI windows; a tab in window A; drag it out; create window B; move the tab's state; close the old representation. Then drag it back.

**Exit:** tabs move between windows without losing location or history.

## 15. Phase 6 — Split panes, Phase 7 — Favorites

**Split panes** as a layout tree (`SplitNode` → panes, recursively horizontal/vertical): split left/right/top/bottom, resize, close pane, move a tab to a pane. Up to four visible panes on desktop to begin with, plus a *Dual Pane* command (source | destination) for copying and remote transfer.

**Favorites** — one concept, not Favorites + Pinned + Quick access + Starred. Context menu: Add to Favorites / Remove from Favorites. Works for local folders, remote paths, network shares and cloud providers; an offline favorite stays but shows as unavailable.

## 17. Phase 8 — Terminal integration

Desktop first. Context menu: Open in Terminal, Open in New Terminal — Windows Terminal, PowerShell, Command Prompt on Windows; Terminal on macOS (others later). Later an embedded terminal pane under the files with *Follow current directory*.

## 18. Phase 9 — Workspaces

`Workspace { Name, Windows[] }`; a window has bounds, display and a layout tree; a pane has tabs, the active tab, preview and terminal state; a tab has provider, location, history, view, sort, filters.

Save Workspace…, Save Changes to Workspace, Open Workspace, Duplicate, Delete. Startup: Home, last session or a specific workspace.

## 19. Phase 10 — Device identity

Each installation is a node: `NodeId`, public key, default display name, platform, device type, capabilities. The private identity is stored securely on the device.

Two names: the **source name** the device chooses ("Visible to others as: Jonatan's MacBook") and a **local alias** each receiver gives it ("Office MacBook", shown with "Visible as: Jonatan's MacBook").

## 20. Spike 5 — QR pairing

An isolated prototype: the computer shows *Link Device* with a QR code, the phone scans it. Test the payload, an ephemeral pairing secret, mutual verification, the persisted public key, revoke and reconnect. The QR never holds a permanent password.

## 21. Phase 11 — Other Devices

The concept is **Other Devices**, not My Devices — own devices, a work computer, a family member's, a temporary one, a one-off transfer. At pairing: *Quick transfer only* (pair, transfer, disconnect, drop trust), *This session*, or *Remember this device* (persisted, auto-discover and reconnect).

## 22. Phase 12 — LAN discovery

Trusted devices on the same network appear under Other Devices, distinguished as Nearby, Linked and Previously Seen — with Other Devices as the main concept.

## 23. Spike 6 — Transport protocol

A bare transfer prototype PC ↔ Mac: persistent connection, TLS, QUIC, multiple streams, one big file, 10 000 small files, abrupt disconnect, reconnect, resume. Benchmark on 1 Gbit, 2.5 Gbit and Wi-Fi.

**Exit:** pull the network cable mid-transfer, plug it back, and it continues without data loss.

## 24. Phase 13 — Remote storage provider

A remote device is presented as storage (`RemoteStorageProvider` beside the local ones), browsed with the same explorer, breadcrumb, tabs, favorites and copy as local files:

```text
Other Devices
└ Gaming PC
   ├ C:
   ├ D:
   ├ Desktop
   └ Downloads
```

### MTP and PTP devices

Devices that cannot run our app connect over MTP rather than as a USB disk: a Garmin watch, an Android phone without the app, a Kindle, and cameras over PTP. macOS has no MTP support of its own (MacDroid and Android File Transfer fill the gap). Windows has it built in (Windows Portable Devices), and Android can be an MTP host over USB OTG. iOS cannot.

An `MtpStorageProvider` presents such a device like any other:

```text
Other Devices
└ Garmin Forerunner
   └ Internal Storage
      ├ GARMIN
      └ Music
```

MTP fits the capability model with fewer flags: no `RandomAccess`, `NativePath` or `Watch`, uploads need the size up front, copy goes through streams, and only one program at a time can hold the device. Implementations: libmtp over libusb on the Mac, WPD on Windows, `android.mtp.MtpDevice` on Android.

For a phone that runs our app, our own transfer (§23–27) beats MTP; MTP is for everything else. Until it exists, a File Provider such as MacDroid's shows up under `~/Library/CloudStorage` and the explorer already browses it.

Spike first: [#11](https://github.com/jonatansoderberg/Spine.FileManager/issues/11), after the storage abstraction and the operations engine.

## 25. Phase 14 — Device-to-device transfer

When the iPhone starts *Gaming PC → MacBook*, data goes Gaming PC → MacBook, never through the iPhone. Control and data planes are separate.

## 26. Phase 15 — Distributed operations

The same operation id exists on source, target and controller; each shows its role (Controlling / Sending / Receiving) with progress, speed, ETA, retries and state. Permissions decide who may pause, cancel or retry.

## 27. Phase 16 — Resumable transfer

Big files are chunked; each verified chunk is journaled, so a reconnect resumes at 67 % rather than starting over. The destination is written as `video.mp4.spine-part` and renamed to `video.mp4` after verification.

## 28. Phase 17 — Adaptive transfer performance

Once correctness is stable: the planner looks at file size and count, disk type, source/destination, latency, bandwidth and CPU, and the scheduler tunes parallel files, chunk size and stream count — more concurrency for small files, few large streams for one big sequential file.

## 29. Phase 18 — Mobile file browser

Header (`< Work   Search  …`), the same breadcrumb with separator drop-downs, list/grid, bottom navigation (Files, Devices, Transfers, Backup). A selection brings an action sheet: Open, Share, Send to Device, Copy, Move, Delete, More.

## 30. Mobile split and tabs

Tabs exist on mobile too ("Work · 3 tabs" opens a tab switcher). Landscape/tablet shows pane | pane; a phone can optionally split vertically (iPhone Photos above, MacBook Pictures below), which makes transfer very clear.

## 31. Spike 7 — iOS background transfer

iPhone → PC: app active, switch app, lock the phone, wait, reopen. Measure how long the transfer continues, what happens to the connection, which background APIs help, and resume correctness. The architecture must never require the iPhone to stay active: an operation survives Running → WaitingForDevice → Running without user data loss.

## 32. Phase 19 — Photo Library provider

Photos is its own provider (All Photos, Videos, Favorites, Albums, Years) — never pretend it is a file-system path.

## 33. Phase 20 — iPhone photo backup

Source *iPhone / All Photos*, destination *Gaming PC / D:\Photo Backup*; options for photos, videos, Live Photos, original formats, metadata. A persistent operation with a manifest (12 843 assets: 12 100 backed up, 401 pending, 342 new). Interruptions are normal.

## 34. Phase 21 — Backup framework

Generalise to folder → folder, device → device, photos → folder; modes incremental, mirror, one-way sync. V1 is incremental backup only.

## 35. Phase 22 — Search

**Current-folder filter done 2026-10-14 ([#32](https://github.com/jonatansoderberg/Spine.FileManager/issues/32)).**

Current-folder search as a quick filter; later an index (file name, extension, metadata, tags, content). Remote search runs on the remote node rather than listing its disk over the network.

## 36. Phase 23 — Command framework

A file-specific command system (`IFileCommand`) whose commands depend on selection, provider, capabilities, platform and context: Open, Open in New Tab / Window / Pane, Open in Terminal, Copy, Cut, Paste, Send to Device, Add to Favorites, Rename, Delete, Properties. Providers can add commands.

## 37. Phase 24 — Preview

**Quick Look and the preview pane done 2026-10-14 ([#34](https://github.com/jonatansoderberg/Spine.FileManager/issues/34)).**

V1: images, text, JSON, XML, Markdown, PDF, video and audio metadata. Later: Office, 3D, archives, source code. Space on desktop for Quick Preview.

## 38. Phase 25 — Drag and drop

File → folder, other pane, tab, Other Device; external → File Manager; File Manager → external. Tab drag: reorder, detach, attach, split. One drag infrastructure, two domains (file drag, tab drag).

## 39. Quality requirements from the start

- **Performance:** the UI never waits for disk or network; all I/O is async and cancellable; progress to the UI is throttled to 5–10 updates a second, not every chunk.
- **Memory:** never a whole big file in RAM — streaming, a buffer pool, a bounded pipeline.
- **Recovery:** every destructive or multi-step operation survives a crash.
- **Languages:** every user-facing text goes through Spine's `ISpineStrings` from the first page — English in `Resources/Strings/strings.xml` (the neutral document), Swedish in `strings.sv.xml`, `{String Key}` in XAML and `ISpineStrings` in code. No hard-coded UI text, so a third language is one more document. Sizes, dates and counts are formatted with the current culture, and plurals use the `.one`/`.other` keys. The app follows the system language and can be switched in settings (`ISpineStrings.Culture`, stored by Spine).
- **Logging:** structured, with OperationId, NodeId, ProviderId, TransferId, so distributed transfers can be debugged.

## 40. Spikes before the features they unblock

| Spike | Question |
|---|---|
| File system | What can we really reach on each OS? |
| Huge folders | Does the UI handle 10k–100k items? |
| Native copy | Which native APIs give the best local performance? |
| Multi-window | Can tabs move reliably between MAUI windows? |
| QR pairing | Are the trust model and reconnect right? |
| MTP | Can we list and copy to a Garmin watch on Mac and Windows without MacDroid? |
| QUIC/P2P | What throughput and reconnect do we get? |
| iOS background | What exactly happens on lock/app switch? |
| PhotoKit | Can originals and metadata be streamed robustly? |

Spikes are small and their UI is thrown away. Their output is knowledge, benchmarks, API decisions and platform limitations — not production code at any price. Record each spike's findings under `docs/spikes/`.

## 41. MVP

Not an MVP until this works well:

- **Desktop (Windows + macOS):** browse, list/grid, breadcrumbs, favorites, tabs, copy/move, retry, operation queue, preview, context menu, terminal, new windows.
- **Devices:** QR link; temporary/session/permanent link; custom aliases; send files; resume; operation status on both devices.

Split panes, workspaces and remote browsing are welcome by then but not required for the first external version.

## 42. Definition of Done for every larger feature

Functional on every promised platform · capability-aware (no actions the provider or OS cannot do) · keyboard shortcuts on desktop · reasonable touch targets on mobile · long operations cancellable · operations survive crash/restart where relevant · no needless main-thread or I/O blocking · accessibility (semantics, keyboard navigation) · dark mode from the start · every text in English and Swedish · a demo/test page where it makes sense.

## 43. End goal

Open workspace "Photos": window 1 shows *iPhone Photos | Gaming PC D:\Photos\2026* side by side, window 2 *MacBook / Pictures*. Drag pictures between panes, close the lid, lose Wi-Fi, come back later — the operation continues.

Or workspace "Spine": pane 1 Maui.Spine, pane 2 Spine.FileManager, pane 3 build output, a terminal following pane 1 — and get exactly the same environment back next time.

## Decisions

- **2026-10-14 — Own public repository** `jonatansoderberg/Spine.FileManager`, on Spine from nuget.org like Almanacka and Orientera (public, unlike those two).
- **2026-10-14 — Three projects to start** (App, Core, Tests); the other boundaries in §3 become projects when their code arrives.
- **2026-10-14 — App id `se.cosmomedia.filemanager`, title "Spine File Manager"** — placeholders until a product name is chosen; an App ID cannot be renamed once registered with Apple, so decide before the first device build.
- **2026-10-14 — English and Swedish from the start** through Spine's `ISpineStrings` (XML documents per culture, Spine 0.1.12+); English is the neutral fallback. `[NavigableRegion(Title = …)]` takes literal text and navigation writes it to the view model's `Title`, so a page sets its localised title in `OnAppearingAsync` until Spine accepts a string key there (see the capability map).
- **2026-10-14 — MTP devices as a provider** (Garmin watch and the like) in milestone D, after a spike (#11); MacDroid's File Provider covers the Mac until then.
- **2026-10-14 — Mac means Mac Catalyst.** Spike 1: in the sandbox, picked folders work fully through System.IO and security-scoped bookmarks survive restarts; without it, the user's folders work after one TCC prompt each.
- **2026-10-14 — The Mac app ships with Developer ID, without the App Sandbox** (notarised download). It then works like Finder: home, iCloud Drive, volumes and shares through plain paths, after one TCC prompt per protected folder (Documents, Desktop, Downloads, iCloud Drive, removable and network volumes). The prompts are gathered in a first-launch step that touches those folders on purpose, with Full Disk Access offered as the one-step alternative. It also keeps the embedded terminal possible (a shell started from a sandboxed app inherits the sandbox) and MTP over libusb simple (#11). Updates (e.g. Sparkle) when someone besides Jonatan uses it.
  **The Mac App Store stays open:** iOS is App Store–only and always sandboxed, so the storage model carries persisted grants (security-scoped bookmarks) from the start. A sandboxed Mac build is then a build variant with a "give access to your home folder" onboarding step, not a rewrite. Rules that keep it open: no code assumes a path is reachable without its provider's grant; no private APIs; process launching and USB access go through their own services, so a sandboxed variant can switch them off.
- **2026-10-14 — Android: All files access or SAF-only is open.** Spike 1: without `MANAGE_EXTERNAL_STORAGE` the app cannot see other apps' files at all; with it the whole shared storage works through System.IO. Play grants it to file managers after a declaration. SAF stays the path for SD/USB volumes either way. Decide before the Android provider (milestone E).
- **2026-10-14 — Working mode while Jonatan is away:** Claude merges its own PRs once a step builds and is verified on the platforms it can run; Jonatan reviews afterwards and fixes go in new PRs. Spine gaps are solved in this repository first and noted in the capability map as Spine candidates; Maui.Spine is not touched.
- **2026-10-14 — The explorer's default view is a details list with a sidebar**, like Windows Explorer: sidebar with Favorites and locations, breadcrumb on top, columns Name, Date modified, Size and Kind; an icon grid as the alternative view.
- **2026-10-14 — Delete moves to the system's Trash / Recycle Bin;** Shift+Delete deletes permanently after a confirmation. Where a provider has no trash (SAF, MTP, network shares without one) the app asks first and says it is permanent.
- **2026-10-14 — Device transfer runs over TCP + TLS 1.3 with mutual node-key certificates, not QUIC** (Spike 6, `docs/spikes/transport.md`): `System.Net.Quic` is not supported on the Mac, iOS or Android. Files go as offer / accept-from-offset / bytes / verified, with part files and SHA-256, so a cut transfer resumes.
