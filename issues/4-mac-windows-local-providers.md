# Issue #4 — macOS (Mac Catalyst) local storage provider (with #3 — Windows)

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/4 · https://github.com/jonatansoderberg/Spine.FileManager/issues/3
**Branch:** issue/4-mac-windows-providers
**Status:** Completed for the Mac; Windows compiled, not run (#3 stays open)

## Plan
Both desktop providers are the System.IO `FileSystemStorageProvider` from #2 with an `IFileSystemPlatform` each: the sidebar's places, the trash and watching. The Mac build drops the App Sandbox (the decision in the plan) and keeps a sandboxed App Store variant one property away. The start page lists the places, the sidebar's content in #5.

## Changes
- `Platforms/MacCatalyst/MacFileSystemPlatform`: home (from the password database, so it is the real home in a sandboxed build too), Desktop, Documents, Downloads, Pictures, Music, Movies, Applications, iCloud Drive, every File Provider under `~/Library/CloudStorage`, and mounted volumes (`NSFileManager.GetMountedVolumes`: network, removable or local, the boot volume left out); names from `NSFileManager.DisplayName`, so Finder's localised names ("Skrivbord", "Hämtade filer"). Trash through `NSFileManager.TrashItem`; network volumes report no trash.
- `Platforms/Windows/WindowsFileSystemPlatform`: profile, known folders (Downloads through `SHGetKnownFolderPath`), OneDrive folders from the environment, ready drives with label and type. Recycle Bin through `SHFileOperation` with undo, on fixed drives only.
- `Entitlements.plist` without the sandbox; `Entitlements.AppStore.plist` and `-p:MacAppStore=true` for the sandboxed variant.
- TCC prompt texts (`NS…FolderUsageDescription`, removable/network volumes, File Providers) in English and Swedish (`Resources/{en,sv}.lproj/InfoPlist.strings`).
- `MauiProgram` registers the platform, the provider and `StorageProviders`; the start page lists the places with their kind, in English and Swedish.

## Verification
- Mac: the places list in both languages (screenshots checked); moving a file from `~/SpineFileManagerTest` to the Trash through the provider (`TrashItem` succeeded, the file left the folder; `~/.Trash` itself is TCC-protected and cannot be listed to confirm).
- Windows: compiles on the Mac (WinUI's App.xaml left out); **not run** — #3 stays open.
- iOS and Android build with the default platform.

## Decisions
- Recycle Bin on fixed drives only: `SHFileOperation` with `FOF_ALLOWUNDO` deletes permanently without a word on removable and network drives, so those report no trash and the app asks before a permanent delete.
- The first-launch step that gathers the TCC prompts belongs with the explorer (#5), where there is a first page to put it on.
