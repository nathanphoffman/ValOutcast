# valheim

The world side. BepInEx + Harmony plugin (C#). `docs/PLAN.md` has the architecture; `docs/worlds/valheim.md` has the per-file roles.

Source is in `src/`, grouped by job (`Input/`, `Player/`, `Collision/`, `Combat/`, `Lighting/`, `Overlay/`, `Items/`, `Config/`, `Debug/`). `docs/worlds/valheim.md` lists the files. Connection code shared with other worlds is in `../common/`. Valheim-specific numbers are in `src/Config/Tuning.cs`, and the item list is `src/Items/ItemCatalog.cs`.

Build with `dotnet build`. It copies itself into Valheim's `BepInEx/plugins/ValOutcast`.
Valheim must be started with the Steam launch option `./start_game_bepinex.sh %command%`.
Look at `BepInEx/LogOutput.log` (`link:`, `overlay:`, `perf:`, `creatures:`, `hit applied`, `world hit`, `use:`, `tool mode`).
After a Valheim update run `tools/worlds/valheim/check_valheim_api.sh`; to look inside Valheim's code use `tools/worlds/valheim/valheim_il`.
