# Issue #51 — Icons and theme after the concept images

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/51
**Branch:** issue/51-icons-and-theme
**Status:** Completed

## Plan
Bring the explorer closer to the spec's Concept A (desktop) and C (iOS):
- coloured file-type icons
- a glyph per sidebar entry
- rounded selection in the sidebar

Then, asked for during testing:
- the system's own file icons as a choice
- icon themes the user adds as files

## Changes
- **Spine's file-type icons** (`Resources/Images/type_*.svg`): a coloured tile per kind, as in the concept images.

  | Kind | Colour |
  |---|---|
  | Document | blue W |
  | Spreadsheet | green X |
  | Presentation | orange P |
  | PDF | red |
  | Image | blue |
  | Video | purple |
  | Audio | pink |
  | Archive | brown |
  | Code | slate |
  | App | grey |
  | Text | plain page |

  The folder icon is redrawn in the same style.
- **`Icons/FileKinds` (Core):** the kind of each extension, shared by Spine's icons and by themes.
- **`Icons/IconTheme` (Core):** a folder with a `theme.json`.
  - It names a picture per extension, per kind, for folders and for any other file. An extension wins over its kind, and a kind over the general file icon.
  - What the theme leaves out keeps Spine's icon.
  - Pictures must lie inside the theme's folder.
  - 4 tests.
- **`Services/FileIcons`:** View › Icons offers Spine, System, each installed theme, and Open Icon Themes Folder.
  - Open Icon Themes Folder creates the folder with a commented example theme on first use.
  - **System** reads the system's icon per kind of file in the background: Quick Look's icon representation on Apple platforms, the shell's list icon on Windows. Spine's icon shows until it arrives. Lists build their rows again once per batch of arrivals. App bundles get their own icons.
  - The choice is kept between runs.
- **Sidebar:**
  - a glyph from Spine's set for each favorite, place and device, tinted with the accent
  - rounded selection
  - devices get a phone or a screen glyph

## Decisions
- **Spine is the default style;** it shows the same everywhere, Android included.
- **Quick Look's icon rather than `UIDocumentInteractionController`.** The latter returns no icons on Mac Catalyst.
- **Icons are cached per extension, not per file.** A folder of 100 000 files costs one lookup per kind. Pictures that show their own content are thumbnails, which the preview shows.
- **Glyphs are drawn by an `ImageButton` that ignores taps.** Spine's SVG tint applies to `ImageButton`; the row takes the tap.
- **Themes are PNG pictures:** an SVG from outside the app's resources does not render in MAUI's `Image`.
