# Issue #24 — Terminal integration

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/24
**Branch:** issue/24-terminal
**Status:** Completed for Open in Terminal; the embedded terminal pane is later (plan §17)

## Plan
Open in Terminal for the selected folder, or the folder shown, from the File menu and a row's context menu. All process launching sits in one service, so a sandboxed Mac App Store build can leave it out (decision of 2026-10-14).

## Changes
- `Services/TerminalLauncher`:
  - **Mac:** `open -a <terminal> <folder>`, started with `posix_spawn` (`Process.Start` does not exist on Mac Catalyst; posix_spawn works outside the sandbox). It finds Terminal plus iTerm, Warp or Ghostty when installed.
  - **Windows:** Windows Terminal (`wt -d`) when present, else PowerShell (pwsh before powershell) or Command Prompt, started in the folder.
  - **iOS and Android:** not supported; the menu items stay hidden.
- File › Open in Terminal (⌥⌘T on the Mac, Ctrl+Shift+Enter on Windows) and Open in Terminal in a row's context menu. A failure shows in the banner. English and Swedish.

## Verification
- **Mac:** `posix_spawn` from inside the app ran `/usr/bin/touch` on a file in the test folder (`returned True, file exists True`), and Terminal was found. No Terminal window was opened during the unattended run; open one by hand to see it.
- **Windows:** compiles, not run.

## Decisions
- The first installed terminal is used. A choice among several (iTerm when both are there) belongs in settings, which do not exist yet; `TerminalLauncher.Installed` already lists them.
