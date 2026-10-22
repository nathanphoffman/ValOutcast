# valheim

The world side. BepInEx + Harmony plugin (C#). `docs/PLAN.md` has the architecture and the per-file roles.

- `src/Plugin.cs`: entry point, config, the per-frame order of everything.
- `src/VoLink.cs`: the connection code (no Unity types, so it can be tested alone).
- `src/Overlay.cs`, `FrameReader.cs`: Outcast's picture (request, wait, draw).
- `src/PlayerFollow.cs`: moves the Valheim player and camera to match Outcast; blocks Valheim's own input while Outcast drives.
- `src/InputForwarder.cs`: keys and mouse to Outcast; what counts as "a menu is open".
- `src/TerrainStreamer.cs`, `Combat.cs`, `WorldHits.cs`: ground and objects out, creatures out and hits in, hits on trees and rocks.
- `src/Interactor.cs`, `ToolMode.cs`, `HeldItemView.cs`, `HudHider.cs`: the Use key, tool mode (R), the held-item view, the hotbar.
- `src/WorldLight.cs`, `PerfStats.cs`, `TestSpawner.cs`: light to Outcast, timing log, F8 boar.

Build with `dotnet build`. It copies itself into Valheim's `BepInEx/plugins/ValOutcast`.
Valheim must be started with the Steam launch option `./start_game_bepinex.sh %command%`.
Look at `BepInEx/LogOutput.log` (`link:`, `overlay:`, `perf:`, `creatures:`, `hit applied`, `world hit`, `use:`, `tool mode`).
After a Valheim update run `tools/check_valheim_api.sh`; to look inside Valheim's code use `tools/valheim_il`.
