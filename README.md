# Spine File Manager

A cross-platform file manager for Windows, macOS, Android and iOS, built with .NET MAUI on [Spine](https://github.com/jonatansoderberg/Maui.Spine).

It aims to combine Explorer-like productivity, Finder-like preview, browser-style tabs and windows, Commander-style dual panes, AirDrop-like transfer between your devices, and copy/move/backup operations that survive crashes and lost connections. English and Swedish from the start.

**Status:** just started — see [the plan](docs/plan.md) for the milestones and [the Spine capability map](docs/spine-capability-map.md) for what the framework already offers.

## Build

Requires the .NET 10 SDK with the MAUI workload (`global.json` pins the version).

```bash
dotnet test src/Spine.FileManager.Tests
dotnet build src/Spine.FileManager.App -f net10.0-maccatalyst
dotnet build src/Spine.FileManager.App -f net10.0-ios -r iossimulator-arm64 -p:CodesignKey=-
dotnet build src/Spine.FileManager.App -f net10.0-android
```

The Windows target builds on Windows.

## Layout

| Path | |
|---|---|
| `src/Spine.FileManager.App` | The MAUI app |
| `src/Spine.FileManager.Core` | Plain .NET: models, storage abstraction, operations |
| `src/Spine.FileManager.Tests` | xUnit |
| `docs/plan.md` | Requirements and the living plan |
| `docs/spikes/` | Findings from the technical spikes |
| `issues/` | One changelog per GitHub issue |

## License

MIT — see [LICENSE.txt](LICENSE.txt).
