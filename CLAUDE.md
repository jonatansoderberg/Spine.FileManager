# Spine File Manager — Claude Instructions

## Repository
- **GitHub:** https://github.com/jonatansoderberg/Spine.FileManager (public)
- **Solution file:** `Spine.FileManager.slnx`
- **Source:** `src/Spine.FileManager.App` (MAUI app), `src/Spine.FileManager.Core` (plain .NET), `src/Spine.FileManager.Tests` (xUnit)
- **Plan:** `docs/plan.md` is the living plan. Read it before starting a step; update the step's status, the decisions and the "Last updated" line in the same PR.

## Code Standards
- Modern C# / .NET (latest language features, nullable reference types enabled).
- Match the style of the existing code before introducing new patterns.
- No unnecessary abstractions — solve the problem at hand. Projects from the plan's §3 are split out when their code arrives.
- No comments unless the *why* is non-obvious.
- Documentation, commit messages, PR bodies and `issues/` changelogs in English.

## Architecture rules
- The central identity is provider + location + capabilities (`StorageLocation`), never a path string.
- The explorer never copies files itself; it enqueues a persistent operation.
- All file-system and network I/O is async and cancellable; the UI never waits on it.
- A component that knows what a file, folder, disk or path is belongs here. A general building block (windows, tabs, split host, key commands, drag-and-drop primitives) belongs in Spine: PR in Maui.Spine, release, bump `SpineVersion`. Never copy framework code into this repository.

## Spine
Built on Plugin.Maui.Spine from nuget.org; every `Plugin.Maui.Spine*` package at `SpineVersion` in `Directory.Packages.props`. Pages follow the three-file pattern (`XxxPage.cs`, `XxxPage.View.xaml`, `XxxPage.ViewModel.cs`) with the two `DependentUpon` entries in the csproj. Page margins use `HeaderBarConstants.PagePadding` / `PageMargin`, inside the scroll source, never on it. `docs/spine-capability-map.md` lists what Spine offers and its gaps.

## Languages
English and Swedish. Every user-facing text is a key in `src/Spine.FileManager.App/Resources/Strings/strings.xml` (English, the neutral document) **and** `strings.sv.xml` (Swedish) — add both in the same change. `{String Key}` in XAML, `ISpineStrings` in code; plurals as `Key.one` / `Key.other`. Never hard-code UI text. A page's title is set from `ISpineStrings` in `OnAppearingAsync` (`[NavigableRegion(Title = …)]` takes literal text).

## Build
On macOS the `dotnet` first in PATH may lack the MAUI workload; use `/usr/local/share/dotnet` (`export PATH="/usr/local/share/dotnet:$PATH" DOTNET_ROOT=/usr/local/share/dotnet`).

```bash
dotnet test src/Spine.FileManager.Tests
dotnet build src/Spine.FileManager.App -f net10.0-maccatalyst
dotnet build src/Spine.FileManager.App -f net10.0-ios -r iossimulator-arm64 -p:CodesignKey=-
dotnet build src/Spine.FileManager.App -f net10.0-android
```

## GitHub issue workflow
- Branch `issue/<id>-<kebab-case-title>`.
- `issues/<id>-<slug>.md` is the living changelog: header with the issue URL, branch and status, then **Plan** (written before code), **Changes**, **Decisions**.
- If a decision needs the user's input, ask before writing code.
- The PR links the issue and the changelog.
- Spikes record their findings in `docs/spikes/<name>.md`; their UI is thrown away.
