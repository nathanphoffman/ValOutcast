# ValOutcast plan

Updated 2026-10-22. The long version of this file is in git history (before the commit that added this one).

## What it is

Two games run at once on one Linux PC. You play in the **world** game's window (Valheim today). **Outcast** (Jedi Outcast, via OpenJK) runs the player: movement, guns, saber, health. Its window is hidden. It draws only its own things (gun, saber, effects) onto a transparent picture that the world game shows on top. The world game supplies terrain, creatures and light.

- **Roles, not games:** *player side* (Outcast, always the overlay) and *world side* (Valheim today, others later).
- Small UDP messages (`protocol/PROTOCOL.md`, ports 47100 world / 47101 player) plus a shared-memory picture (`protocol/FRAME.md`, `/dev/shm/valoutcast_frame`).
- The protocol never names a game. Units on the wire: meters, Y up, right-handed, forward is -Z. Each game's adapter converts.

## Frame sync

Each Valheim frame: read messages, send input, bump the request counter in the frame file, wait up to 25 ms for the new picture, read the PlayerState sent with it, move the player. Outcast wakes on the counter, runs one game frame, draws one picture, publishes it. If Valheim stops asking for 250 ms, Outcast draws on its own again. Anchoring: the first Outcast position is matched to where the Valheim player stands; after that only movement counts. A jump over 10 m is a teleport and re-anchors.

## Layout

Layout after the restructure.

| Path | What |
|---|---|
| `protocol/` | Messages, frame file spec, C/Python connection code, `golden.txt` (exact bytes every implementation must produce). Shared by all games. |
| `player/outcast/` | `src/` (`vo_bridge`, `vo_field`), `renderer/vo_overlay`, `patches/0001-bridge-hooks.patch` for OpenJK. |
| `worlds/common/` | C# code any C# world plugin can use: `VoLink.cs`, `FrameReader.cs`. |
| `worlds/valheim/` | Valheim plugin (C#, BepInEx 5 + Harmony). `Plugin.cs` calls each part per frame; a part that throws switches itself off. |
| `tools/` | Tests, fake games, run scripts. Valheim-only tools are in `tools/worlds/valheim/`. |
| `third_party/` | Not in git. `OpenJK/` (our modified checkout), `jk2run/` (links to the build and the Steam game files). |
| `docs/` | This file. Per-game notes in `docs/worlds/<game>.md`. |
| `todo.md` | The user's own notes. Do not edit. |

Valheim plugin parts, controls and Valheim-specific lessons: `docs/worlds/valheim.md`.

**OpenJK patch workflow:** edit in `third_party/OpenJK`, regenerate with `cd third_party/OpenJK && git diff > ../../player/outcast/patches/0001-bridge-hooks.patch`. Files we add (`vo_bridge.*`, `vo_field.*`, `vo_link.*`, `vo_overlay.*`) are edited in this repo and copied in by `tools/apply_outcast.sh`. Run it before every build. Never edit those copies under `third_party/`.

## Build, run, test

- Needs: Linux, Valheim with BepInEx 5.4.23.5 (Steam launch option `./start_game_bepinex.sh %command%`), a Steam copy of Jedi Outcast, .NET SDK, CMake, gcc/g++, `libjpeg-dev libpng-dev libsdl2-dev libopenal-dev`.
- Build: `cd valheim && dotnet build` (copies into Valheim). Outcast: `tools/apply_outcast.sh`, then `cd third_party/OpenJK/buildjo && make -j$(nproc)`.
- Run: start Valheim, load a world, then `tools/run_outcast.sh [map] [width height]`.
- Tests without games: `tools/test_all.sh` (close the real games first, they use the same ports).
- Fake partner: `tools/start_test_session.sh <options>` / `tools/stop_test_session.sh` (logs in `/tmp/valoutcast_test/`). See `tools/fake_world.py` for options.
- Look at the picture: `tools/dump_frame.py out.png`. Look inside Valheim: `dotnet run --project tools/worlds/valheim/valheim_il -- members|calls|il|callers ...`. After a Valheim update run `tools/worlds/valheim/check_valheim_api.sh`.
- Adding a message touches: `vo_link.h/c`, `vo_protocol.py`, `VoLink.cs`, `tools/make_golden.py` (rerun to rewrite `golden.txt`), the three test files, `PROTOCOL.md`.

## Controls

WASD, mouse, jump, crouch, clicks drive Outcast. Keys 1-8 / wheel pick owned weapons. **R** tool mode (Valheim's own items). **E** use. **Tab** inventory. **F8** spawn boar. Cheat: chat `valleyofthejedi`. Config: `BepInEx/config/com.valoutcast.bridge.cfg`.

## Debug switches (env)

`VO_OVERLAY=1` overlay mode (set by `run_outcast.sh`), `VO_OVERLAY_VISIBLE=1` show Outcast's window, `VO_SV_FPS` (60), `VO_GIVE_ALL=1` (ammo only), `VO_BOLT_SPEED` (0.3), `VO_FIELD_BOX` (0.7), `VO_DEBUG_BOLTS=1`, `VO_DEBUG=1`. Outcast cvar `s_show 1` logs every sound. Valheim log: `BepInEx/LogOutput.log` (`link:`, `overlay:`, `perf:`, `creatures:`, `items:`, `cheat:`).

## Lessons (read before debugging)

1. Outcast's renderer didn't know the player turned; fixed in `CG_PredictPlayerState` by copying view angles and origin from the game's player state.
2. The player model never turns with the view, so muzzle/saber points come from the drawn gun barrel (`vo_muzzleRel`).
3. Shots at normal speed are invisible; slowed to 30%. Field of view must match Valheim's.
4. Valheim has no first-person view; `HeldItemView` draws a copy of the held item in front of the camera.
5. `FindObjectsOfType` skips inactive objects: keep your own list of anything you deactivate.
6. Valheim input is gated by `Player.TakeInput`/`PlayerController.TakeInput` (patched). `Player.Interact` is private (reflection).
7. Terrain collision: 0.5 m columns, player box narrowed to 70%, only layers the player really hits, things under 0.3 m are ground.
8. Two games on one GPU: the picture read-back costs Valheim frames (about 50 to 37-45 fps).
9. The bridge only turns on when the player is free and on the floor (a mid-air start in a cutscene killed Kyle). Cutscenes are skipped. A map load can show a brief "lost connection".
10. Stale `/dev/shm/valoutcast_frame` and held UDP ports are common leftovers. Never `pkill -f` text that appears in your own shell command; use the start/stop scripts.
11. Outcast saves cvars on exit; reset run-time ones (like `cg_drawGun`) on start.
12. The hidden level still runs, so its noises leak (falling scream, datapad beep). Find them with `s_show 1` and a fake partner.
13. Valheim menus close when they see the Use key go down; `Interactor` calls `ZInput.ResetButtonStatus("Use")` after `Interact`.
14. Client game code (`codeJK2/cgame`) is built into `jospgamex86_64.so`, not the exe. Check its timestamp after edits, and ask the user to restart.
15. Linux only (mmap, /dev/shm).

## Status (2026-10-22)

All original phases (setup, connection, player sync, terrain collision, creatures and combat, overlay rendering, polish) are built.

**Confirmed in the real games:** connection, movement, terrain collision, overlay/frame sync/FOV, shots, creature hits, tool mode with the hammer, saber glow.

**Built but not confirmed in the real games:** E key and workbench menu fix, HitWorld on real trees/rocks, lighter collision, menu mode, `HeldItemView`, crafted weapon items (stun baton, bryar pistol) and the weapon limit, cheat code, load-time scream and datapad fixes, hill camera smoothing, item loadout and start weapons (latest commit).

## Known limits

- Saber out puts Outcast's camera behind Kyle (third person) while Valheim's camera is at the eyes.
- Tool mode copies only meshes, so bows, torch flames and shields may look wrong.
- Only 2 of 13 weapons are craftable items; ammo is not crafted (999 given). Crafted items drop as flint.
- Outcast health/ammo and Valheim health/stamina are separate; only creature damage to the player is mapped.
- No depth sharing between pictures. Terrain is a heightfield (no caves or interiors).
- Death, respawn, weather and saves are not synced.
- Valheim updates can break the plugin.

## Next steps

1. **Restructure** (below).
2. Confirm the unconfirmed features in the real games and fix what turns up.
3. Death and respawn link (new message or reuse `PlayerHurt` plus a reset message).
4. Health/stamina link (decide which side is the truth; probably a `PlayerVitals` message).
5. Performance: asynchronous read-back, GPU alpha conversion, or accept one frame of latency past a budget.
6. Crafted weapons: remaining 11 items (icons are `gfx/hud/w_icon_*.tga` in Outcast's pk3s), ammo items, ammo counter following the Valheim stack. No Jotunn; register items with our own Harmony patches. Unlock order (first guess): stun baton at start, bryar pistol Meadows, E-11 Black Forest, bowcaster Swamp, repeater Mountain, flechette Plains, lightsaber Mistlands, rocket launcher Ashlands, the rest Deep North.
7. Lightsaber first-person view, saving Outcast state in `Player.m_customData`, weather, save syncing, multiplayer (long term), release polish (installer, Windows).

## Restructure (decided 2026-10-22; phases 1-4 done: folders moved, paths fixed, shared C# in `worlds/common/`, Valheim tools and notes split out, tests pass; the rest is not done)

Goal: the player side may change games and the world side will change games; neither should need a rewrite of the shared parts.

1. DONE: `outcast/` is now `player/outcast/`; `valheim/` is now `worlds/valheim/`.
2. DONE: moved `VoLink.cs` and `FrameReader.cs` (no Valheim types) to `worlds/common/`, shared by C# world plugins. C/C++ worlds use `protocol/vo_link.c`.
3. DONE: Valheim-only tools moved to `tools/worlds/valheim/`; per-game notes to `docs/worlds/valheim.md`.
4. Every world plugin does the same jobs with the same file names: Input, Collision, Actors, Combat, Lighting, Overlay. A new game starts as a copy.
5. Per-game numbers (damage scale, item-to-weapon mapping) live in that world's folder. The weapon list belongs to the player side.
6. Update paths in scripts (`apply_outcast.sh`, `run_outcast.sh`, tests, csproj), `README.md` and `.gitignore`. Run `tools/test_all.sh` after.
7. Known gap: collision is a height grid, which fits terrain. A level with walls and ceilings (Doom) needs a new message type and a protocol version bump. Also check `HitWorld.kind` (0 shot, 1 saber): it leaks Outcast ideas into the protocol.

## Working rules

Short plain-English replies, no multiple-choice questions. The user makes git commits unless asked. Never kill processes you didn't start. Update this file and `README.md` when behavior changes. Don't build a framework before a second world exists.
