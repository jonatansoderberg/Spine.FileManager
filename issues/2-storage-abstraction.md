# Issue #2 — Storage abstraction: IStorageProvider, StorageLocation, capabilities

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/2
**Branch:** issue/2-storage-abstraction
**Status:** Completed

## Plan
The model from Spike 1's six decisions (`docs/spikes/file-system.md`), in `Spine.FileManager.Core` (plain .NET, no MAUI), plus a System.IO provider that the Mac (#4) and Windows (#3) providers build on, tested against real temp folders.

## Changes
- `Storage/`: `StorageProviderId`, `StorageLocation` (equal by provider and item id; display and native path only describe it), `StorageItem` (name and kind always, size/dates/flags when cheap), `StorageRoot`, `StorageCapabilities`, `StorageException` with `StorageErrorKind` (including `Unavailable` and `AccessLost`), `IStorageProvider`, `StorageProviders` (lookup by a location's provider id).
- `StorageNames`: one set of naming rules everywhere (Windows' strictest rules, so a name never fails when copied elsewhere) and "name (2).ext" for conflicts. `NaturalComparer`: "file2" before "file10", letters by culture (Swedish å/ä/ö after z).
- `FileSystem/FileSystemStorageProvider`: listing streamed through a bounded channel from a pool thread (`FileSystemEnumerable`, no `FileInfo` per entry); create, rename (a change of case only is allowed), write (refuses to overwrite unless asked), copy and move (folders too, never a folder into itself), delete to the trash or permanently, `FileSystemWatcher`; platform exceptions translated to `StorageException`. A refused write drops the folder's modify capabilities.
- `FileSystem/IFileSystemPlatform` + `DefaultFileSystemPlatform`: roots, trash and whether watching works — what #3/#4 and the mobile platforms override.
- 30 tests (`Spine.FileManager.Tests/Storage`).

## Decisions
- `EnumerateAsync` returns `IAsyncEnumerable<StorageItem>` so a 100 000-item folder shows its first rows at once; the list (#7) batches them.
- `OpenWriteAsync` takes the length up front (optional) because MTP needs it (#11).
- Capabilities are asked per location (`GetCapabilitiesAsync(location)`); the file-system provider starts from everything and downgrades a folder after an `AccessDenied` on a write, instead of probing every folder.
- Copy/Move on the provider are only the native operations; stream copies and conflict handling belong to the operations engine (#8).
- Persisted grants (bookmarks, URI permissions) are not in this PR: they arrive with the first provider that needs them (Mac sandbox variant, iOS, Android); `StorageErrorKind.AccessLost` and the `PersistentGrant` capability are the hooks.
