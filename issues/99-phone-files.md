# Issue #99 — iPhone: reach the phone's files

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/99
**Branch:** issue/99-phone-files
**Status:** In Progress

## Plan
The owner asked for all four on 2026-10-16, in this order:
1. **The app's folder in the Files app:** `UIFileSharingEnabled` and `LSSupportsOpeningDocumentsInPlace`, so On My iPhone › Spine File Manager shows the received files and other apps open them in place.
2. **Photos:** the PhotoKit provider is shared with the Mac and already registered on iOS; check it on the phone (permission prompt, thumbnails, copy out and in).
3. **Add Location:** iOS's folder picker (`UIDocumentPickerViewController` for folders). The picked folder is kept as a security-scoped bookmark (the file-system spike showed it survives a restart on iOS and that System.IO works on its path while access is held), opened again at every start and shown as a place. On My iPhone or iCloud Drive as a whole is the way to see everything the Files app shows.
4. **Linked computers in the Files app:** a File Provider extension. An app extension with its own bundle id, provisioning and an App Group; a spike first.

Limits: other apps' private data and the system stay out of reach; iOS has no file watching, so lists refresh when opened.

## Changes
- `Info.plist`: the two keys for the Files app.
- `IosFileSystemPlatform`: the phone's places are the app's Documents ("Spine File Manager") and the granted folders; the sandbox root and "/" are left out (the disk stays a card on Home).
- `FolderGrants`: the picker, security-scoped bookmarks in `folder-grants.json`, opened at start (a stale bookmark is made anew), the Files app's own name for each place ("På min iPhone", "iCloud Drive"), Cloud for iCloud and provider folders. Removing such a place from the sidebar gives its access back.
- "Add Location…" under This Device in the sidebar (iPad) and on Home on the phone, with a hint to pick On My iPhone or iCloud Drive. The places are built again when a folder is added or removed.

## Decisions
- **A bookmark, not a copy:** the picker opens the folder in place (`asCopy: false`), so files are read and written where they are.
