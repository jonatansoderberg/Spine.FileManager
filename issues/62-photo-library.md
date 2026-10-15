# Issue #62 — Photo Library as a place: show, copy out, add and delete photos

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/62
**Branch:** issue/62-photo-library
**Status:** In Progress

## Plan
Jonatan asked whether the Pictures folder's `Photos Library.photoslibrary` could show its photos. It is a package (Show Package Contents gives `originals/` with UUID names), and double-clicking it opens Photos. Both stay. The photo library also becomes a place of its own.

Decided with Jonatan:
- **What it shows:** All Photos, with names, dates and thumbnails.
- **What it allows:** read, copy out, add and delete.
- **Where:** a "Photo Library" row under This Device, after Pictures.

## Changes
- `Services/PhotoLibraryProvider.cs`, a PhotoKit `IStorageProvider` for Mac and iOS:
  - **Listing:** one folder of every photo and video, under the name it came with (`originalFilename`, read from the asset rather than its resources).
  - **Reading:** exports the original (fetching it from iCloud if needed) to a temporary file that deletes itself on close.
  - **Writing:** stages the file; the operation queue's rename to the final name imports it with that name. The queue is unchanged.
  - **Deleting:** one change for all selected photos, so the system asks once; they go to Recently Deleted.
- `StorageCapabilities.SystemConfirmsDelete` lets the explorer skip its own question when the system asks.
- `IDeletesTogether` lets a provider delete a selection in one call.
- `StorageRootKind.PhotoLibrary`, placed after Pictures in the sidebar.
- **Thumbnails** from `PHImageManager`:
  - The list and grid ask only for the rows they show, and each row updates itself when its picture arrives (`FileIcons.For(item, ready)`), rather than rebuilding the list.
  - Up to 3000 thumbnails are kept.
  - The preview shows a 512-point picture.
- `NSPhotoLibraryUsageDescription` and `NSPhotoLibraryAddUsageDescription` added for Mac and iOS.

- **Newest first:** the photo library opens newest first, as Photos does, and the tab gets its own sort back when it leaves. The date column is when the photo was taken.
- **Thumbnails that never showed:** MAUI cancels a source's load when the same `StreamImageSource` starts loading again, so one source shared by the cells that ask at the same moment showed in none of them. The photo cache keeps bytes, and the icon cache hands each view a stream source of its own (the system's icon of a kind is shared by every row of that kind, so it had the same trap).
- **Preview pane (concept step 6):**
  - the picture
  - name, and kind · size in the accent colour
  - Open, and Open With (the apps from `NSWorkspace`, the default first)
  - facts: created, modified, size in bytes, kind, dimensions (ImageIO for files, the asset for photos), and the location as a link that opens the folder with the item selected
  - Share, Copy Path, Send to Device (the linked devices), and More (Show in Finder, Add to Favorites, Rename, Delete)
  - × to close

- **Sizes in the list:** each photo's size is read from its original resource (`fileSize`) while listing and kept in the saved listing with its name, so it is looked up once per photo (6450 photos: 14 s once in Debug, then 0.7 s).
- **Total in the status bar:** a place without a folder on disk whose listing is files only (the photo library) adds up its sizes for "… in all".

## Decisions
- No Show Photos item in the context menu: the menu is the same for every row, so it would show (dimmed) on every file. The sidebar row is one click away.
- The asset's `filename` property is the library's own UUID name for its copy. `originalFilename` is the name it came with, and Photos exports under it.
- A thumbnail that fails is not cached and is asked for again the next time its row shows.

## Not yet verified
- copying out and in
- deleting (it would touch real photos)
- iOS
