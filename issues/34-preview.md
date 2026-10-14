# Issue #34 — Preview: Quick Look and a preview pane

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/34
**Branch:** issue/34-preview
**Status:** Completed in code; how it looks is unchecked (the screen was locked and then dark)

## Plan
Plan §37 for the desktop MVP: the system's own previews rather than our own renderers. That means Quick Look on the Mac, and on every platform a preview pane with the system's thumbnail, the start of a text file and the item's facts.

## Changes
- `Services/Thumbnails`: QuickLookThumbnailing on Apple platforms (PDF pages, images, Office, video), the shell's `GetThumbnailAsync` on Windows.
- `Services/QuickLookPreview`: `QLPreviewController` over the selection (or the whole folder), paging between files.
- Preview pane at the right of the list: View › Show Preview (⇧⌘P / Alt+P). It shows the thumbnail, the name, the kind, size and dates, and for text files (by extension, or any extensionless file without a NUL byte) the first 32 KB.
- Quick Look: File › Quick Look (⌘Y on the Mac), and Space in the list.
- `Controls/KeySink` and the Mac's `KeySinkHandler`, a first responder of its own that takes the list's keys while it has focus:
  - arrows, with Shift to extend
  - Return to rename, as in Finder
  - Space for Quick Look
  - typing the beginning of a name to jump to it (a pause of a second starts over)
  
  The list scrolls to keep the selection in view.
- The Mac menu no longer has shortcuts without a modifier (Return for Rename, the arrows): as menu key equivalents they would take those keys from text fields such as the rename prompt and the filter field. Windows keeps them in its menu.
- English and Swedish.

## Verification (Mac Catalyst, scratch harness; no captures possible)
- The PDF's preview gave a `StreamImageSource` from QuickLookThumbnailing, with "PDF-fil | 593 bytes | Ändrad … | Skapad …".
- The Markdown file showed its text ("# Anteckningar…").
- Keys through `OnListKey`: Down moved to dokument.pdf; Shift+Up extended to bild.jpg + dokument.pdf; typing "do" jumped to dokument.pdf; "a" after a pause jumped to a.txt.
- Two bugs found and fixed:
  - Asking `UIScreen` for its scale off the UI thread threw and lost every thumbnail.
  - Reading the text waited for the thumbnail's 5 s timeout.

**Needs a person:** how the pane looks, Quick Look opening, and real key presses reaching the list (and not reaching it while a text field has focus).

## Decisions
- No own renderers for PDF or Office: the system thumbnail plus Quick Look covers them on the Mac, and the shell's thumbnails on Windows. Markdown shows as text until a Spine release carries MarkdownView (merged after 0.1.14).
