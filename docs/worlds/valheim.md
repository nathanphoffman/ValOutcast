# Valheim world notes

The Valheim side of ValOutcast: a BepInEx 5 + Harmony plugin in `worlds/valheim/`. Tested on Valheim 1.0.20, BepInEx 5.4.23.5. General design is in `docs/PLAN.md`.

## Plugin parts (`worlds/valheim/src/`)

Grouped by job, so another world plugin can use the same folder names. All code is in the `ValOutcast` namespace.

| Folder | Files |
|---|---|
| (root) | `Plugin` (calls each part per frame; a part that throws switches itself off) |
| `Input/` | `InputForwarder` (keys, mouse, menus), `Interactor` (E key), `CheatCode` (`valleyofthejedi`) |
| `Player/` | `PlayerFollow` (moves player and camera) |
| `Collision/` | `TerrainStreamer` (0.5 m ground columns in 8 m chunks) |
| `Combat/` | `Combat` (creatures out, hits in, damage to player), `WorldHits` (trees/rocks) |
| `Lighting/` | `WorldLight`, `SaberGlow` |
| `Overlay/` | `Overlay`, `HudHider`, `ToolMode` + `HeldItemView` (R key, Valheim's own items) |
| `Items/` | `OutcastItems` + `Loadout` (weapons as craftable items, weapon limit), `ItemCatalog` (which item is which Outcast weapon, and its cost) |
| `Config/` | `Tuning` (Valheim-specific numbers: damage scale, creature range, tool tier, grace time) |
| `Debug/` | `PerfStats`, `TestSpawner` (F8 boar) |

Shared connection code (`VoLink.cs`, `FrameReader.cs`) is in `worlds/common/`.

## Build and tools

- `cd worlds/valheim && dotnet build` copies the plugin into Valheim's `BepInEx/plugins/ValOutcast`. Start Valheim with the Steam launch option `./start_game_bepinex.sh %command%`.
- After a Valheim update run `tools/worlds/valheim/check_valheim_api.sh` (lists every Valheim function/field the plugin needs that went missing).
- Look inside Valheim's code: `dotnet run --project tools/worlds/valheim/valheim_il -- members|calls|il|callers ...`.
- Log: `BepInEx/LogOutput.log` (`link:`, `overlay:`, `perf:`, `creatures:`, `items:`, `cheat:`).

## Controls

WASD, mouse, jump, crouch, clicks drive Outcast. Keys 1-8 / wheel pick owned weapons. **F1-F7** force powers, as in Outcast (push, pull, speed, mind trick, heal, grip and lightning; hold F6/F7). **R** tool mode (Valheim's own items). **E** use. **Tab** inventory. **F8** spawn boar. Cheat: chat `valleyofthejedi`. Config: `BepInEx/config/com.valoutcast.bridge.cfg`.

## Lessons

1. Valheim has no first-person view; `HeldItemView` draws a copy of the held item in front of the camera.
2. `FindObjectsOfType` skips inactive objects: keep your own list of anything you deactivate.
3. Valheim input is gated by `Player.TakeInput`/`PlayerController.TakeInput` (patched). `Player.Interact` is private (reflection).
4. Terrain collision: 0.5 m columns, player box narrowed to 70%, only layers the player really hits, things under 0.3 m are ground.
5. Valheim menus close when they see the Use key go down; `Interactor` calls `ZInput.ResetButtonStatus("Use")` after `Interact`.
6. Two games on one GPU cost Valheim frames (about 50 to 37-45 fps).

## Known limits

- Force push and pull move creatures through `ActorPush` and `Character.ApplyPushback`; the strength is `Tuning.ForcePushForce` and has not been tuned in the real game. Lightning damages creatures (about 10 per hit in tests). Push and pull also move loose items on the ground (up to `Tuning.ItemRange` away, speed `Tuning.ItemPushSpeed`); `CreatureStreamer` sends them as loose actors. Lightning does not hurt items, and nothing here affects trees or rocks.
- Tool mode copies only meshes, so bows, torch flames and shields may look wrong.
- Only 2 of 13 weapons are craftable items; ammo is not crafted. Crafted items drop as flint.
- Terrain is a heightfield (no caves or interiors).
- Valheim updates can break the plugin.
