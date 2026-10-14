# Issue #1 — Spike 1: file-system capabilities on each platform

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/1
**Branch:** issue/1-spike-file-system-capabilities
**Status:** In Progress — automated runs done; Windows and real-hardware pass outstanding, Mac distribution decision open

## Plan
A throwaway probe app, `spikes/FileSystem` (Spine, one page), that runs the same steps against every location a platform offers and writes a report:

- **Steps per location:** exists, volume, list (count and time), stat, read, create folder, write 1 MB, random access, rename, copy, move, watch, delete. The write steps work inside a `.spine-probe-<id>` folder that is deleted again.
- **Known locations** without asking: the app's own folders, the user's folders, volumes and roots for each platform.
- **Picked locations:** the platform's folder picker (UIDocumentPicker on iOS/Mac Catalyst, `ACTION_OPEN_DOCUMENT_TREE` on Android, `FolderPicker` on Windows). The grant is persisted (security-scoped bookmarks in two variants, persisted URI permissions, the path) and probed again on the next launch, which answers "access after restart".
- **Android SAF** goes through `DocumentsContract` (no paths), plus a check of whether a picked tree also opens through `System.IO`, and the probe again with *All files access* (`MANAGE_EXTERNAL_STORAGE`, which Play allows for file managers).
- **Photos** (iOS, Mac Catalyst) as its own provider through PhotoKit: authorization, counts, streaming one original.
- **Mac Catalyst** built twice: sandboxed (Mac App Store) and without the sandbox (Developer ID).

Where it runs: Mac Catalyst on this Mac, iOS on the iPhone 17 simulator, Android on the Pixel 10 Pro emulator. Windows is compiled here and needs a run on a Windows machine; USB, SD card, network shares, OneDrive and iCloud need real hardware or accounts and are listed for a manual pass.

Output: `docs/spikes/file-system.md` (matrix, limits, API decisions) and the `StorageCapabilities` model for #2.

## Changes
- `spikes/FileSystem`: the probe app — `PathProbe` (System.IO), `SafProbe` (Android Storage Access Framework), `PhotosProbe` (PhotoKit), `FolderAccess` per platform (picker plus persisted grant), `KnownLocations`, a Markdown report.
- Ran it on Mac Catalyst with and without the sandbox, the iPhone 17 simulator (iOS 26.4) and the Pixel 10 Pro emulator (Android 17, API 37), with and without All files access and through an SAF tree; compiled the Windows code on the Mac.
- `docs/spikes/file-system.md`: results, limits, the decisions for #2 and the `StorageCapabilities` draft.

## Decisions
- Raw probe reports are not committed: they hold the user's file names and paths, and the repository is public. The findings document summarises them.
- SAF watching needs an open children cursor; the probe keeps one open while it waits (observers on the URIs alone got nothing from ExternalStorageProvider).
- TCC must be tested through LaunchServices (`open`); a child process inherits its parent's grants and never prompts.
- The spike app stays in the repository under `spikes/` (in the solution's `/spikes/` folder) so the Windows and hardware passes can be rerun; its UI is English only and is not part of the product.
