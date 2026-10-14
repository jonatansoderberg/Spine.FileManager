# Issue #32 — Filter the current folder

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/32
**Branch:** issue/32-filter
**Status:** Completed

## Plan
Plan §35's first level: a filter field in the toolbar, cleared when the folder changes, as in Finder.

## Changes
- Core `Explorer/NameFilter`: a name matches when it contains the text, case and accents ignored. Five tests (81 in all).
- A filter field at the end of the toolbar (wide windows), View › Filter This Folder (⌘F / Ctrl+F) puts the cursor in it; the list follows each keystroke; the status line says "1 träff av 18 objekt"; opening another folder clears it.

## Verification (Mac Catalyst, scratch harness + capture)
1. In the home folder "DOK" left only "Dokument". The display name matched, case ignored, and the status line said "1 träff av 18 objekt".
2. Opening that folder cleared the filter.
