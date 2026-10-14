# Spike 1 — File-system capabilities

**Issue:** [#1](https://github.com/jonatansoderberg/Spine.FileManager/issues/1) · **Run:** 2026-10-14 · **Probe app:** [`spikes/FileSystem`](../../spikes/FileSystem)

**Question:** what file access is practical on each platform, and what must the storage abstraction model?

## How it was measured

The probe app runs the same steps against every location: exists, volume, list, stat, read, create folder, write 1 MB, random access (seek, read and write in place), rename, copy, move, watch (a change event within 3 s), delete. All the write steps work inside a `.spine-probe-<id>` folder that is deleted again. It probes the platform's known folders without asking. A folder the user picks is probed through the platform picker, and its grant is persisted and probed again after a restart. Photos go through PhotoKit as their own provider.

Run it yourself: build `spikes/FileSystem` for the platform and read `probe-report.md` in the app's data folder (on the Mac the report also goes to stdout). Launch arguments: `SPIKE_PICK=1` opens the picker after the probe, `SPIKE_PHOTOS=1` adds the photo library. On Android pass them as intent extras (`am start -n <activity> -e pick 1`). `-p:SpikeSandbox=false` builds the Mac app without the App Sandbox.

| Platform | Where | Status |
|---|---|---|
| Mac Catalyst, sandboxed | macOS 27.0, this Mac | ✅ run |
| Mac Catalyst, no sandbox (Developer ID) | macOS 27.0, this Mac | ✅ run |
| iOS | iPhone 17 simulator, iOS 26.4 | ✅ run (the simulator enforces less than a device) |
| Android | Pixel 10 Pro emulator, Android 17 (API 37) | ✅ run |
| Windows | — | compiles on the Mac; **not run** |

✅ ok · ⛔ denied · ❌ failed · — unsupported

## Mac Catalyst

**Sandboxed (Mac App Store).**

| Location | list | read | write…delete | watch |
|---|---|---|---|---|
| Container (app data, cache, temp, `HOME`) | ✅ | ✅ | ✅ | ✅ FSEvents, ~12 ms |
| ~/Documents, ~/Downloads, ~/Desktop, ~/Pictures, ~ | ⛔ | | ⛔ | |
| iCloud Drive, ~/Library/CloudStorage (OneDrive etc.) | ⛔ | | ⛔ | |
| /Applications, / | ✅ | ⛔ (/) | | |
| Picked folder (~/Documents through the open panel) | ✅ | ✅ | ✅ all | ✅ |
| Same folder after a restart, from a bookmark | ✅ | ✅ | ✅ all | ✅ |

- In the sandbox `HOME` and `SpecialFolder.UserProfile` point into the container (`~/Library/Containers/<bundle id>/Data`), and the container's `Library` holds symlinks to the real folders that the app cannot follow. A file manager must never use `HOME` as "the user's home".
- A picked folder works fully through **System.IO** once `StartAccessingSecurityScopedResource` has been called. Native paths, FileSystemWatcher and random access all work.
- **Both** a security-scoped bookmark (`WithSecurityScope`) and a plain bookmark resolved and gave full access after a restart. Use the security-scoped one, which is the documented contract. The entitlements needed are `files.user-selected.read-write` and `files.bookmarks.app-scope`.
- A sandboxed file manager starts empty: it shows nothing until the user grants folders one by one. A good onboarding is "Give access to your home folder" in one pick.

**Without the sandbox (Developer ID).** Started through LaunchServices (`open`), with every TCC prompt answered *Allow*:

| Location | list | read | write…delete | watch |
|---|---|---|---|---|
| ~/Documents, ~/Downloads, ~/Desktop, ~/Pictures | ✅ after a TCC prompt each | ✅ | ✅ | ✅ |
| ~ (home), /Applications | ✅ | ✅ | | |
| iCloud Drive (`~/Library/Mobile Documents/com~apple~CloudDocs`) | ✅ | ✅ | ✅ | ✅ |
| `~/Library/CloudStorage` File Providers (MacDroid: phones over MTP) | ✅ slow (1.5 s) | ❌ timed out | ⛔ | |
| / and the system volume | ✅ | ⛔ | | |
| Photo library (PhotoKit) | ✅ thousands of assets, albums, smart albums | ✅ streamed a 2.6 MB original in 3 chunks, ~1 s | | |

- Each protected folder (Documents, Desktop, Downloads) asks **once, the first time it is touched**, and the call blocks until the user answers. The first listing took 3–3.5 s while the prompt was open. The explorer must never touch those folders on the UI thread, and should touch them on purpose (onboarding) rather than at a random moment. Full Disk Access is the alternative: one trip to System Settings instead of several prompts.
- Other volumes and network shares ask under *Removable Volumes* / *Network Volumes* the same way (not tested here, no drive attached).
- File Providers under `~/Library/CloudStorage` look like folders but can be slow or refuse writes. This depends on the provider and is behaviour the model must tolerate (time-outs, read-only).
- **A testing trap:** started as a child of another process (a shell, an IDE, Claude), the app inherits *that* process's TCC grants and never prompts. The first run looked fully open for that reason. Test TCC through `open` or Finder.

**Which to ship on the Mac:** without the sandbox, a file manager behaves like Finder after a few prompts. In the sandbox it needs the user to pick every root. Both work technically. The sandbox means the Mac App Store; no sandbox means Developer ID, notarised and distributed outside the store. **Decision needed** — see below.

## iOS

| Location | list | read | write…delete | watch |
|---|---|---|---|---|
| Documents, Library, Caches, tmp | ✅ | ✅ | ✅ | — |
| App bundle | ✅ | ✅ | (read-only) | |
| Picked folder (On My iPhone root, a File Provider folder outside the app) | ✅ | ✅ | ✅ | — |
| Same folder after a restart, plain and minimal bookmarks | ✅ | ✅ | ✅ | — |
| Photo library (PhotoKit) | ✅ 6 assets, albums, smart albums | ✅ streamed a 2.8 MB HEIC original in 3 chunks | | |

- **`FileSystemWatcher` throws `PlatformNotSupportedException` on iOS.** Watching needs `NSFilePresenter` / `NSMetadataQuery` (iCloud) or a `DispatchSource` on a file descriptor.
- A picked folder gives a real path that System.IO can use while security-scoped access is held, and the bookmark survives a restart.
- `UIFileSharingEnabled` + `LSSupportsOpeningDocumentsInPlace` show the app's own Documents in Files, as "On My iPhone › FS Spike".
- `simctl privacy grant photos` did not answer iOS's *full access* prompt. The prompt still appeared and had to be accepted.
- **Not covered by the simulator:** the real sandbox on a device, iCloud Drive and third-party providers (OneDrive, Google Drive). Their files can be placeholders not yet downloaded, which needs `NSFileCoordinator` to read. All three go into the manual pass on the iPhone.

## Android (API 37)

**Without permissions** (no runtime storage permission exists for non-media files on API 33+):

| Location | list | read others' files | write…delete | watch |
|---|---|---|---|---|
| App internal / cache / external files dir | ✅ | — | ✅ | ✅ inotify |
| Shared root `/storage/emulated/0` | ✅ (folders) | | ⛔ create | |
| Download, Documents | ✅ | ⛔ invisible | ✅ own files | ✅ |
| DCIM, Pictures, Music | ✅ | ⛔ invisible | ⛔ a `.bin` (MediaStore MIME rules) | |
| `Android/data`, `/storage`, `/` | ⛔ | | | |

The app sees only the files it created itself. A file another app put in Download is not listed at all.

**With *All files access*** (`MANAGE_EXTERNAL_STORAGE`, granted in Settings; the probe used `appops`): the whole shared storage works through System.IO, including reading other apps' files, writing in DCIM/Pictures/Music, FileSystemWatcher, random access, rename, copy and move. `Android/data` and `/storage` stay closed.

**SAF tree** (`ACTION_OPEN_DOCUMENT_TREE`, picked DCIM):

| Step | Result |
|---|---|
| list, stat, read | ✅ through `DocumentsContract` and content URIs |
| create folder, write, rename, move, delete | ✅ (write 6–24 MB/s for 1 MB, far slower than System.IO) |
| random access | ✅ through `OpenFileDescriptor` + `FileInputStream.Channel` (`/proc/self/fd/<n>` is denied) |
| copy | — `ExternalStorageProvider` does not offer `FLAG_SUPPORTS_COPY`; copy as a stream |
| watch | ✅ **only** with an open cursor over the folder's children (`cursor.RegisterContentObserver`); observers on the URIs alone got nothing |
| persisted grant after restart | ✅ `PersistedUriPermissions` still lists it, probe identical |

- The picker **refuses** the shared root and `Download` ("Can't use this folder"; Android 11+). DCIM, Documents and subfolders can be picked.
- A picked tree does not open its files to System.IO. The native path of `primary:DCIM` lists the folders, but other apps' files stay hidden.

**Conclusion for Android:** a real file manager needs *All files access*. Play allows it for file managers, after a declaration form. SAF is the fallback for SD cards and USB drives, where the path is not reachable, and for a store build without the permission.

## Windows

Not run. The Windows code compiles (checked on the Mac with WinUI's App.xaml left out). Expected: an unpackaged desktop app (`WindowsPackageType=None`) has the user's full access through System.IO, like any Win32 program. `FutureAccessList` needs package identity, and the probe records whether it throws.

**To run on Windows:** `dotnet build spikes/FileSystem -f net10.0-windows10.0.19041.0`, start it, use *Pick folder* on a USB stick and a network share, and send back `%LOCALAPPDATA%\…\probe-report.md` (the path is printed at the top of the page). Set `SPIKE_UNC=\\server\share` first to have a share probed without picking. Check OneDrive's online-only files: reading one should download it.

## Manual pass still to do

- [ ] Windows: everything above, plus a USB drive, a network share, OneDrive with online-only files, and file watching on a share.
- [ ] Mac: a USB drive, an SMB volume, iCloud Drive and an online-only OneDrive file, with and without the sandbox.
- [ ] iPhone (device): the real sandbox, iCloud Drive, a third-party provider, a USB drive through the Files app.
- [ ] Android device: an SD card and a USB OTG drive (paths under `/storage/<id>`, with and without All files access, and through SAF).

## Open decision

**Mac distribution: sandboxed Mac App Store build, or Developer ID without the sandbox?** Developer ID gives the Finder-like experience: home, iCloud Drive and volumes with one-time prompts. The store build must start with "give access to your home folder" and can never see a folder the user did not pick. The storage model handles both (a granted root is just a location with a persisted grant), so this does not block #2, but it decides the Mac onboarding. Android has the same split: *All files access* needs Play's declaration; without it the app is SAF-only.

## Decisions for the storage abstraction (#2)

1. **The identity is never a path.** A location is the provider plus the provider's own item id (`content://…` document id, bookmark, path, PhotoKit local identifier). The native path is optional (`NativePath` capability), and only where System.IO really works.
2. **Access is a grant that the provider persists and restores.** Security-scoped bookmarks on Apple, persisted URI permissions on Android, plain paths on Windows. Restoring can fail or come back stale, so a favorite or tab must be able to show "access lost — pick again".
3. **Capabilities belong to the location, not the provider.** The same System.IO provider can write in Documents but not in DCIM on Android, and read `/` but not write it on the Mac. Probe-and-cache per root, and let a failed operation downgrade the capabilities.
4. **Watching has three implementations:** FileSystemWatcher (Windows, Mac, Android paths), an open-cursor observer (SAF), and `NSFilePresenter` / dispatch sources (iOS). The model needs a `Watch` capability that is honestly false where none works.
5. **Copy is not always native.** SAF often lacks `SupportsCopy`, so the operations engine falls back to a stream copy. That is the transfer planner's first rule.
6. **Photos is its own provider**, read-only in v1, streaming originals through `PHAssetResourceManager` (verified).

```csharp
[Flags]
public enum StorageCapabilities
{
    None = 0,
    List = 1 << 0,
    Read = 1 << 1,
    Write = 1 << 2,          // create files, overwrite
    CreateFolder = 1 << 3,
    Rename = 1 << 4,
    Delete = 1 << 5,
    NativeCopy = 1 << 6,     // provider copies itself (File.Copy, clonefile, DocumentsContract.CopyDocument)
    NativeMove = 1 << 7,     // provider moves itself (rename on the same volume, MoveDocument)
    Watch = 1 << 8,
    RandomAccess = 1 << 9,
    NativePath = 1 << 10,    // System.IO works on the location's path
    PersistentGrant = 1 << 11,
}
```

Copy and Move are always possible through streams when Read and Write are. The flags say whether the provider does them natively, which is what the planner needs to know.
