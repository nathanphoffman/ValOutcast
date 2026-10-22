# ValOutcast: plan and handoff notes

Last updated 2026-10-21. If you are picking this project up cold, read "Read this first" and "How it fits together", then skim "Status" and "Next steps". The rest is reference.

## Read this first

**What it is.** A bridge between *Star Wars Jedi Knight: Jedi Outcast* (run through the open-source OpenJK engine) and *Valheim* (native Linux, BepInEx mod). Both games run at the same time on one PC. You look at and play in the **Valheim window**. Outcast runs the player (movement, mouse look, guns, lightsaber, health) with its window hidden, and draws only its own things (gun, saber, effects, HUD) onto a transparent picture that Valheim shows on top of its own. Valheim supplies the world: terrain, trees, creatures, sun. Idea inspired by SkyCraft (Skyrim + Minecraft).

**Where things stand.** Everything is built. The user plays it in the real games. Confirmed working in the real games: connection, player movement, terrain collision, the overlay with frame sync and correct camera/field of view, shots that you can see, shooting creatures (a boar dies; the F8 test key spawns one), switching to Valheim's items with R (the hammer works). Built and compiling but **not yet confirmed in the real games**: the Use key (E), shots and the saber damaging trees and rocks, the lighter terrain collision, menu mode (inventory, chests, crafting), the hotbar coming back in tool mode, and the on-screen copy of the held item (`HeldItemView`, just written). See "Status" for the full list and "Next steps".

**Working with this user.** Keep replies short and in plain English (a mid-level developer, not an expert). Never ask multiple-choice questions; ask plain questions. Answer what was asked, then stop; give a short summary of what changed after finishing work. The user runs and plays the real games and reports what they see, so ask them to restart a game after you change it and tell them what to look for. The user makes the git commits; do not commit unless asked. Never kill processes you did not start (the user's real Outcast and Valheim may be running). Update this file and the README whenever behavior changes.

## How it fits together

```
        Valheim window (you play here)                              Outcast (hidden window, OpenJK + our changes)
 +--------------------------------------------+   UDP localhost   +-----------------------------------------------+
 | BepInEx plugin  (valheim/src, C#)          |  47100 <-> 47101  | outcast/src/vo_bridge.cpp  (game module)      |
 |  - InputForwarder: keys, mouse -> Input -----------------------> |  - applies Input: moves/turns/fires the player|
 |  - PlayerFollow: moves the Valheim player  | <---- PlayerState -- |  - sends PlayerState every game frame         |
 |    and camera to match Outcast's player    |                   |                                               |
 |  - TerrainStreamer: ground + objects ------------ CollisionSection ->  vo_field.cpp: wraps the movement trace  |
 |  - CreatureStreamer: creatures as boxes ---------- ActorUpsert ----->  invisible "vo_actor" boxes to hit       |
 |    applies hits   <-------------------------------- HitActor -------|                                          |
 |  - WorldHits: damages trees/rocks  <-------------- HitWorld --------|  shots / saber that hit the ground columns |
 |  - PlayerDamagePatch: creature hits ---------------- PlayerHurt ---->  Outcast health is the real health       |

 |  - SaberGlow: blue point light <------------------- PlayerLight ----|  sent while the saber is lit                  |
 |  - WorldLight: sun/ambient ------------------------- WorldContext -->  lights Outcast's models like Valheim's   |
 |                                                                                                               |
 |  Overlay.cs  <======= /dev/shm/valoutcast_frame (shared memory, RGBA picture) ======= vo_overlay.cpp (renderer)|
 |  draws the picture over Valheim (OnGUI)    request counter + FOV written by Valheim, picture by Outcast       |
 +--------------------------------------------+                   +-----------------------------------------------+
```

**Two kinds of traffic.** (1) Small UDP messages (`protocol/PROTOCOL.md`), both directions. (2) The finished Outcast picture goes through a shared-memory file (`protocol/FRAME.md`), because it is big (3.7 MB at 1280x720) and only the newest one matters.

**Roles.** The code is written in terms of a *player side* (Outcast today) and a *world side* (Valheim today), so another game could take either role later. See "Game-neutral rule".

**One frame, step by step (frame sync, "lockstep").** Valheim's `Plugin.Update` runs, in this order: (1) `Link.Tick` reads messages; (2) `ToolMode.Update`; (3) `InputForwarder.Update` sends this frame's keys and mouse; (4) `Overlay.Update` bumps the *request counter* in the frame file (and writes Valheim's horizontal field of view next to it) and waits up to 25 ms for the new picture; (5) `Link.Tick` again, which picks up the PlayerState Outcast sent while making that picture; (6) `PlayerFollow.Tick` moves the Valheim player (no smoothing when the picture arrived in time); then terrain, creatures, hits, Use key, light, hotbar. In `LateUpdate` the patched `GameCamera.UpdateCamera` puts the camera at the player's eyes. In `OnGUI` the picture is drawn. On the Outcast side the main loop (`Com_Frame`, patched in `common.cpp`) wakes as soon as the request counter changes, runs one game frame (reads Input, moves the player, sends PlayerState), draws exactly one picture (`SCR_UpdateScreen`, patched in `cl_scrn.cpp`, skips drawing when there is no new request), reads it back from the GPU and publishes it. So the picture and the camera pose come from the same Outcast frame and appear in the same Valheim frame. If Outcast is slower than 25 ms the wait times out and that frame shows the previous picture (a late picture is discarded so the two do not drift apart). If Valheim stops asking (menu, loading, quit), requests stop changing; after 250 ms Outcast treats that as "nobody is asking" and draws on its own again.

**Coordinates and units.** On the wire: meters, Y up, right-handed, forward is -Z. Outcast uses 32 units per meter, X forward, Y left, Z up; `VO_ToWire`/`VO_FromWire` in `vo_bridge.cpp` convert. Valheim is meters, Y up, left-handed; the plugin flips Z (`PlayerFollow.WireToUnity`, `WorldToWire`, `WireToWorld`). The two worlds are unrelated maps, so the plugin *anchors* them: the first position Outcast reports is matched to where the Valheim player stands (feet on the real ground under them); after that only movement matters. A jump of more than 10 m in PlayerState counts as a teleport and re-anchors.

## Repository map

| Path | What it is |
|---|---|
| `valheim/` | The Valheim plugin (C#, BepInEx 5 + Harmony). `dotnet build` also copies it into Valheim's `BepInEx/plugins/ValOutcast`. |
| `valheim/src/Plugin.cs` | Entry point. Config entries. Calls every part each frame, in order; a part that throws is switched off after logging once. |
| `valheim/src/VoLink.cs` | The UDP connection and message encode/decode. No Unity types, so it is testable alone. |
| `valheim/src/FrameReader.cs`, `Overlay.cs` | Read the shared-memory picture (seqlock-style, see `protocol/FRAME.md`); request pictures, wait for them, draw them. |
| `valheim/src/PlayerFollow.cs` | Moves the Valheim player/camera to match Outcast's; hides Valheim's body; Harmony patches that stop Valheim's own input, set the camera at the eyes. |
| `valheim/src/InputForwarder.cs` | Sends keys/mouse/weapon requests to Outcast; defines "a menu is open" (`MenuOpen`). |
| `valheim/src/TerrainStreamer.cs` | Samples ground height and solid objects on a 0.5 m grid and streams 8 m chunks. |
| `valheim/src/Combat.cs` | `CreatureStreamer` (creatures out, hits in) and `PlayerDamagePatch` (damage to the Valheim player goes to Outcast). |
| `valheim/src/WorldHits.cs` | Applies HitWorld: damages trees/rocks/ore. |
| `valheim/src/Interactor.cs` | The Use key (E) on what the crosshair is over. |
| `valheim/src/ToolMode.cs`, `HeldItemView.cs` | Tool mode (R) and the on-screen copy of the held Valheim item. |
| `valheim/src/OutcastItems.cs`, `valheim/assets/icons/` | Outcast weapons as craftable Valheim items (a copy of the Flint item with a new name/icon, plus a recipe), registered by Harmony patches on `ObjectDB`/`ZNetScene`; start items given once via `Player.m_customData`. Icons are Outcast's own `w_icon_*.tga`, converted to PNG and embedded. Inventory-only for now: not yet linked to Outcast. |
| `valheim/src/SaberGlow.cs` | Blue point light at the player's hand while the lightsaber is lit (PlayerLight message). Off in tool mode. Tuning constants at the top of the file. |
| `valheim/src/HudHider.cs`, `WorldLight.cs`, `PerfStats.cs`, `TestSpawner.cs` | Hides the hotbar; sends sun/ambient light; timing log every 10 s; F8 spawns a boar. |
| `outcast/src/vo_bridge.cpp/h` | The game-module side of the bridge (copied into OpenJK's `codeJK2/game/valoutcast/`). |
| `outcast/src/vo_field.cpp/h` | The collision field (grid of columns) and its sweep test. Pure math; unit tested. |
| `outcast/renderer/vo_overlay.cpp/h` | Renderer side: off-screen buffer, alpha handling, GPU read-back, shared-memory publish (copied into `code/rd-vanilla/`). |
| `outcast/patches/0001-bridge-hooks.patch` | Our edits to OpenJK's own files (see below). |
| `protocol/` | `PROTOCOL.md` (messages), `FRAME.md` (picture file), `vo_link.c/h` (C connection), `vo_protocol.py` (Python), `golden.txt` (exact bytes every implementation must produce). |
| `tools/` | Tests, fake games, helper scripts (see "Build, run, test"). |
| `third_party/` | **Not in git** (see `.gitignore`): `OpenJK/` (a git checkout we modify), `jk2run/` (symlinks to the built game and to the user's Steam copy of Jedi Outcast's `base/*.pk3`). |

**The OpenJK patch.** `third_party/OpenJK` is a git checkout with our edits. The files we change: `code/client/cl_scrn.cpp` (skip drawing without a request), `code/qcommon/common.cpp` (wake on request), `code/rd-vanilla/` (`tr_backend.cpp` off-screen buffer and publish, `tr_light.cpp` lighting, `tr_world.cpp` no world drawing, `CMakeLists.txt`), `code/server/sv_snapshot.cpp` (always send missiles/beams/effects), `codeJK2/cgame/` (`cg_ents.cpp` hide the level's entities, `cg_predict.cpp` no level collision + draw from the game's player state, `cg_view.cpp` use Valheim's field of view, `cg_weapons.cpp` remember where the gun barrel is), `codeJK2/game/` (`g_weapon.cpp` shot start point and speed, `g_missile.cpp` and `wp_saber.cpp` report world hits, `g_active.cpp`/`g_main.cpp` call the bridge, `CMakeLists.txt`), `shared/sdl/` (hidden window with alpha, sound while unfocused). **Workflow:** edit files in `third_party/OpenJK`, then regenerate the patch with `cd third_party/OpenJK && git diff > ../../outcast/patches/0001-bridge-hooks.patch`. Files we *add* (`vo_bridge.*`, `vo_field.*`, `vo_link.*`, `vo_overlay.*`) are edited in `outcast/` and `protocol/` and copied in by `tools/apply_outcast.sh`, which also applies the patch to a fresh checkout. Always run `tools/apply_outcast.sh` before building so the copies are current. Do not edit the copies under `third_party/` for those files.

## Build, run, test

**Requirements:** Linux. Valheim (native) with BepInEx 5.4.23.5, started with the Steam launch option `./start_game_bepinex.sh %command%` (without it BepInEx does not load). Valheim version tested: 1.0.20 (Valheim updates often break mods; `tools/check_valheim_api.sh` reports what disappeared). A Steam copy of Jedi Outcast. .NET SDK (10), CMake, gcc/g++, `libjpeg-dev libpng-dev libsdl2-dev libopenal-dev`.

**Build:** `cd valheim && dotnet build` (plugin, copied into Valheim). Outcast: `tools/apply_outcast.sh`, then `cd third_party/OpenJK/buildjo && make -j$(nproc)` (the cmake line is in `README.md`; only the JK2 single-player targets are on). The run folder `third_party/jk2run` holds links to the three built files and the game's `base/*.pk3`.

**Run:** start Valheim, load a world, wait until standing on the ground, then `tools/run_outcast.sh [map] [width height]` (default `artus_mine 1280 720`; use the shape of the Valheim window). Play in the Valheim window.

**Tests with no game:** `tools/test_all.sh` (protocol in Python, C and C#; collision field; Valheim API check). The protocol tests bind the real UDP ports, so close the real games first.

**Test with a fake partner (Outcast for real, Valheim faked):** `tools/start_test_session.sh <options>` starts Outcast silently (SDL dummy audio) and `tools/fake_world.py`; `tools/stop_test_session.sh` stops only what it started. Logs are in `/tmp/valoutcast_test/`. Options of `fake_world.py`: `--walk` `--turn` `--turnrate D` `--fire` `--alt` `--weapon N` `--dark` `--nowall` `--ring M` (ring wall radius) `--noactors` `--near` `--hurt`. It streams a flat test terrain inside a 6 m ring wall, two wandering creatures, light, and prints every HitActor/HitWorld it receives. Typical: `tools/start_test_session.sh --fire --turn --nowall --noactors --weapon 3` (blaster, turning in place). Wait 15-20 s for the level to load. **Close the real games first** (shared ports and `/dev/shm/valoutcast_frame`).

**Looking at what Outcast draws:** `tools/dump_frame.py out.png` (newest picture), `tools/frame_series.py out.png` (the frames right after a shot), `tools/fake_overlay_client.py SECONDS [FRAME_MS]` (acts like Valheim's overlay: requests pictures and prints Outcast's answer time). These three read the shared file; the last two request pictures themselves, so they only work while the real Valheim is not running. To look at pictures use the image-reading ability of the tool you have; transparent areas show as a checkerboard.

**Looking inside Valheim's code:** there is no decompiler. `dotnet run --project tools/valheim_il -- members <Type> <text>` / `calls <Type> <Method>` / `il <Type> <Method>` / `callers <text>` (uses the Mono.Cecil that ships with BepInEx). This is how Valheim's input, hover, spawn and camera code was checked before hooking into it. `tools/check_valheim_api.sh` lists every Valheim function/field/type the plugin depends on.

## Controls and settings (what the player sees)

- WASD, mouse, jump, crouch, left/right click work as in Valheim but drive Outcast. Hotbar keys 1-8 pick an Outcast weapon, mouse wheel = next/previous. Right click (Valheim "Block") and middle click ("SecondaryAttack") are Outcast's alternate fire. Esc opens Valheim's menu.
- **R**: tool mode on/off (Valheim's own items; see below). **E**: Use. **Tab**: Valheim inventory. **F8**: spawn a boar 6 m ahead (testing aid).
- `BepInEx/config/com.valoutcast.bridge.cfg` (created on first run): `Input.MouseDegreesPerCount` (mouse speed, default 1), `Input.ToolModeKey` (default R), `Overlay.Brightness` (0.85), `ToolMode.CameraRight/CameraUp/CameraBack` (move the camera in tool mode, default 0), `ToolMode.ItemRight/ItemUp/ItemForward` (where the held-item copy is drawn, defaults 0 / 0.4 / 0.55).

## Debug switches

| Switch | Where | Effect |
|---|---|---|
| `VO_OVERLAY=1` | env, set by `run_outcast.sh` | Overlay mode: hidden window, off-screen drawing, publish pictures. |
| `VO_OVERLAY_VISIBLE=1` | env | Show Outcast's window (debugging). |
| `VO_OVERLAY_FPS` | env, default 45 | Publish rate when Valheim is not requesting pictures. |
| `VO_SV_FPS` | env, default 60 | Outcast game-logic rate (Outcast itself uses 20; low feels laggy). |
| `VO_GIVE_ALL=1` | env, on in `run_outcast.sh` | Every weapon (saber included) and 999 of every ammo at each level start, topped up every 0.5 s. Set `VO_GIVE_ALL=` (empty) for off. |
| `VO_BOLT_SPEED` | env, default 0.3 | Speed of the player's shots (1 = Outcast's normal 72 m/s, too fast to see). Thrown weapons unaffected. |
| `VO_FIELD_BOX` | env, default 0.7 | Width of the player's box against Valheim ground (1 = full). Lower = lighter collision. |
| `VO_DEBUG_BOLTS=1` | env | Logs what each of the player's shots hits, creature hits sent, and world hits sent. |
| `VO_DEBUG=1`, `VO_DEBUG_HIT=1`, `VO_DEBUG_SKIP` | env | Position log; player "hits" the first creature every few seconds; skip cutscenes. |
| `cg_drawCrosshair 0`, `cg_drawGun 1` | set in `run_outcast.sh` | Crosshair off (Valheim's is used). `cg_drawGun` is set to 0/1 at run time by tool mode, and is saved in Outcast's config on exit, so `run_outcast.sh` resets it to 1 every start. |
| `vo_light`, `vo_mute` | Outcast cvar / console command | Set by the bridge: lighting values; mute/unmute sound. |
| Valheim log | `BepInEx/LogOutput.log` | `link:`, `overlay:`, `perf:` (every 10 s: frames/s and ms per plugin part), `creatures:`, `hit applied`, `world hit`, `world hits from Outcast`, `use:`, `tool mode on/off`, `terrain: objects that block the player are on layers ...`. |

## Message list (summary; the spec is `protocol/PROTOCOL.md`)

Header 8 bytes (magic, version, type, sequence). Types: 13 PlayerLight (color, intensity; a lit lightsaber), 1 Hello, 2 Heartbeat, 3 PlayerState (player to world), 4 CollisionSection, 5 CollisionReset, 6 ActorUpsert, 7 ActorRemove, 8 HitActor (player to world), 9 PlayerHurt (world to player), 10 Input (buttons, move axes, look deltas, weapon request), 11 WorldContext (sun, ambient), 12 HitWorld (player to world), 13 PlayerLight (player to world). Weapon request values: 0 none, 1-9 slot, 10 next, 11 previous, 12 tool mode on (put weapon away), 13 tool mode off, 14 cheat: give everything. The picture file header carries the request counter (byte 20) and the wanted horizontal FOV (byte 24). Adding a message means touching: `vo_link.h/c`, `vo_protocol.py`, `VoLink.cs`, `tools/make_golden.py` (then run it to rewrite `golden.txt`), the three test files, `PROTOCOL.md`.

## Game-neutral rule

We may swap in other games later (for example Skyrim or Doom 2016). So:

- **Two roles, not two games.** A *player side* runs the player (movement, combat). A *world side* supplies the world (terrain, creatures, time). Today Outcast is the player side and Valheim is the world side.
- **Protocol never names a game.** Messages use neutral names: `WorldContext`, `ActorUpsert`, `CollisionSection`, `PlayerState`, `HitActor`, `HitWorld`, `PlayerHurt`. No "Greydwarf", "saber" or game-specific IDs. Game-specific things go in an opaque `kind` string or a generic extras field. (HitWorld's `kind` is 0 shot / 1 lightsaber today; that is a small leak to keep an eye on.)
- **Neutral units on the wire:** meters, Y-up, right-handed. Each game's plugin (its *adapter*) converts. Nothing outside an adapter knows about 32 units per meter.
- **Adapters are thin.** Shared logic (connection, heartbeats, encoding, chunking) lives in `protocol/`.
- **Don't build a framework yet.** Finish Valheim + Outcast first.
- **Test without games.** The fake-game scripts in `tools/` speak the neutral protocol.

## Hard-won lessons (read before debugging)

1. **Outcast's renderer never knew the player turned.** Valheim's mouse turns the player inside Outcast's *game* logic, but the *client/render* side only knows its own (empty) input, so its camera stayed put. Gun and crosshair are screen-fixed so they looked fine; world things (shots) were drawn from the wrong angle, worse the more you turned. Fixed in `CG_PredictPlayerState` (`cg_predict.cpp`): the predicted state's view angles and origin are copied from the game's player state. If anything in the overlay looks wrong relative to Valheim's world, check the drawn angle against the game's first (debug print of `cg.refdef` vs `g_entities[0].client->ps`).
2. **The player model never turns with the view** (only `currentAngles` follows; `legsYaw` stays). Anything taken from the model (muzzle point, saber base) is wrong. Shots now start at the drawn gun barrel (`vo_muzzleRel` measured in `cg_weapons.cpp`, applied in `CalcMuzzlePoint`); an eye-height start looked like a dot and was invisible.
3. **Shots at normal speed are invisible** (72 m/s = over a meter per frame, a dot after two frames). Slowed to 30%.
4. **Field of view must match** Valheim's (it is the player's setting; Outcast defaults to a fixed 80). Otherwise shots slide across the screen at the wrong speed when turning.
5. **Valheim has no first-person view.** The camera is at the eyes; a held item hangs far below the view. `HeldItemView` draws a copy in front of the camera, following the real arm's animation (the real body is hidden by disabling renderers, not by `SetActive`, so the Animator keeps running).
6. **Valheim's `FindObjectsOfType` skips inactive objects.** The hotbar hider lost its list this way and never brought the hotbar back. Keep your own list of anything you deactivate.
7. **Valheim's input is gated by `Player.TakeInput` and `PlayerController.TakeInput`** (patched to false while Outcast drives and tool mode is off). `Player.UpdateHover` still runs, so `GetHoverObject()` works; `Player.Interact` is private (called by reflection in `Interactor`). Valheim's R key (sheathe) is suppressed in the frame the tool key is pressed.
8. **Terrain collision is a grid of 0.5 m solid columns.** Square columns catch a full-width box, so the player box is narrowed to 70% against them; only layers the player really collides with (from Valheim's collision matrix) count; objects only block if solid at body height; things under 0.3 m are ground. Taller things above head height are walked under.
9. **Two games, one GPU:** Outcast's picture read-back waits for the GPU. In the real session the overlay wait averaged 5-17 ms with occasional 25 ms timeouts (in tests against a fake partner it is about 6 ms). This costs Valheim frames (about 50 fps to 37-45 fps). See "Next steps".
10. **Cutscenes and death:** the bridge only turns on when the player is free and on the level's floor (turning it on mid-air in an intro killed Kyle and left a cutscene waiting). Cutscenes are skipped automatically in overlay mode (`exitview`). Outcast stops ticking while a map loads, so a long load can show a brief "lost connection".
11. **Leftover state on the machine:** `/dev/shm/valoutcast_frame` outlives the games (a stale request counter once froze a fresh Outcast; now handled by the 250 ms rule). UDP ports 47100/47101 are held by whichever games are running. Several Outcast copies at once fight over the shared file. `pkill -f <text>` kills your own shell if the shell's command line contains the text; use the start/stop scripts.
12. **Outcast saves cvars (`CVAR_ARCHIVE`) on exit**, so anything set at run time (like `cg_drawGun`) must be reset on start.
13. **Creatures are sparse in Valheim** (0-2 within 40 m in the Meadows; spawning is timer-based per zone and does not depend on the player walking; checked in Valheim's `SpawnSystem`). Use F8 to test.
14. **Windows:** the code uses Linux-only calls (mmap, /dev/shm). Windows would need shared-memory replacements.

## Status

**Phases (original plan):** 0 setup done, 1 connection done, 2 player sync done, 3 terrain collision done, 4 creatures and combat done, 5 rendering done, 6 world sync and polish (below), 7 release basic (everything builds from the repo).

**Confirmed in the real games by the user:** connection; moving and turning the Valheim character from Outcast; terrain collision (feel improved after the "less brutal" change; the newest lighter version not yet re-checked); the overlay, frame sync, field of view and the camera fix ("that finally did it"); shots visible and aligned; creature hits ("boar hits work great"); tool mode toggling and the hammer working.

**Built, compiled, tested only with the fake partner or not at all in real games:** Use key (E); HitWorld (shots/saber damaging trees and rocks; tested that Outcast sends the messages, not that a real tree takes damage); lighter terrain collision; menu mode; hotbar returning in tool mode (fixed after the user reported it missing); `HeldItemView` (just written; the user reported seeing no held item before it); config entries for item/camera offsets.

### Feature notes by area

**Connection and sync.** UDP localhost, ports 47100 (world) / 47101 (player), golden-packet tests in Python, C, C#. Heartbeat timeout detection on both sides. Frame sync as described in "How it fits together". Outcast only switches on once Valheim is in a world (Valheim sends input only then); it idles at Valheim's title screen. Plugin parts switch themselves off after one error. The `PerfStats` log shows per-part cost.

**Player sync.** Outcast sends PlayerState every game frame (game logic runs at 60 Hz, `VO_SV_FPS`); Valheim moves its player to match, anchored as described. With a fresh picture each frame the position is used as is (no smoothing); without one it blends between the two newest samples. Valheim's keyboard/mouse are ignored for its own character while Outcast drives; they are forwarded as `Input`. Sound: silent until Valheim is in a world and Outcast has taken over; muted in cutscenes, loading and after death; music off; keeps playing when Outcast's window is unfocused.

**Terrain.** `TerrainStreamer` measures cells within 4 chunks (8 m each) of the player with a 1 ms per-frame budget, re-measures chunks every 0.5 s, resends unchanged ones every 5 s. Each cell: `ZoneSystem.GetGroundHeight`, then a raycast from 30 m up for solid objects (layers from the collision matrix), with the body-height check for tall things. `vo_field.cpp` sweeps the player's box against the columns; `VO_PlayerTrace` wraps `gi.trace`: over Valheim ground the level's own solids are ignored for every trace (movement, shots, AI). Limits: heightfield only (no caves, overhangs, walking under roofs, climbing), 0.5 m steps, low things (< ~0.5 m) can be stepped onto. The Outcast level's water/lava/fog volumes are ignored too.

**Creatures and combat.** Valheim sends creatures within 40 m 10 times a second; Outcast makes invisible `vo_actor` boxes (contents BODY); guns and the saber hit them like any entity. Damage on a box is sent back as HitActor and the box is healed; Valheim applies it as a slash hit (Outcast damage x1). Damage to the Valheim player is intercepted (`PlayerDamagePatch`), sent as PlayerHurt, applied to Outcast's health (Valheim damage x4, capped at 50 per blow, ignored for 3 s after the games line up; falls/drowning ignored). Outcast's health is the real one. Numbers are guesses (`HURT_SCALE` in `vo_bridge.cpp`, `OutcastToValheim` in `Combat.cs`). Log lines: `creatures:` and `hit applied`.

**Hitting the world (trees, rocks, ore).** A shot or saber swing that hits the world over Valheim ground sends HitWorld (position, direction, damage, kind). `WorldHits` damages the closest destructible object within 0.9 m of that point (creatures and your own buildings are skipped) as both an axe and a pickaxe blow at tool tier 4. Shot = its Outcast damage (14 for the blaster); saber = 25, at most 4 per second. Hits on plain ground find nothing and are counted.

**Overlay picture.** Outcast draws into an off-screen buffer (RGBA, transparent background; the level, its people, rain and props are not drawn), converts to straight alpha with edge-color fix (no dark outlines), publishes it. Valheim draws it stretched over the whole screen with brightness 0.85; start Outcast with the same window shape as Valheim's. No depth sharing: Outcast's things always draw on top of Valheim's world. Hidden while any Valheim menu is open. Lighting: Valheim sends sun direction and sun/ambient colors 4x a second; Outcast lights its models with them (bright by day, dark at night).

**Saber glow.** While the lightsaber is lit, Outcast sends PlayerLight (blue, intensity 1; off otherwise, resent every 0.5 s). `SaberGlow` shows a Unity point light (range 14 m, intensity 3.5, slow gentle pulse) 0.25 right / 0.2 down / 0.7 forward of the camera. Built, not yet confirmed in the real games; brightness and offset are guesses (constants in `SaberGlow.cs`).

**Cheat code.** Typing `valleyofthejedi` in Valheim's chat box (`CheatCode.cs`, patch on `Chat.SendText`; the text is not sent on) sends weapon request 14; Outcast gives every weapon with full ammo (`G_Give all`, saber set up) and every force power at level 3 (`vo_bridge.cpp`). When crafted weapons are linked to Outcast (Next steps 6), this must bypass that check. Built, not yet confirmed in the real games.

**Shots.** Start at the drawn barrel, fly at 30% speed, always sent to Outcast's view (missiles, beams, effects bypass the "visible from here" rules of the hidden level). Valheim's crosshair is used; Outcast's is off.

**Tool mode (R).** Switches between Outcast weapons and Valheim's own items (sword, bow, hammer, hoe, torch...). Outcast puts its weapon away (weapon request 12: `cg_drawGun 0`; a drawn saber is swapped for a gun first because the saber puts Outcast's camera behind the player) and keeps walking and aiming. Valheim gets its input back (the `TakeInput` blocks are off), shows the hotbar (number keys pick Valheim items, the wheel is Valheim's) and runs its own attacks, blocking, building and tools. Walking/jumping/running are zeroed on the Valheim side (`SetControls` patch) because Outcast moves the player and running on the spot would drain the stamina a swing needs. The Use key is handled by Valheim itself in this mode. `HeldItemView` draws the held items (right and left hand) in front of the camera.

**Menu mode.** Valheim's screens (inventory with Tab, chests, crafting stations, cooking, traders, signs, the hammer's piece menu) work while Outcast drives: they handle the cursor themselves; Outcast stands still and does not turn while one is open (`InputForwarder.MenuOpen`), and the picture is hidden (`Overlay`). Nothing blocks them any more.

**Use key.** `Interactor` calls Valheim's `Player.Interact` on `GetHoverObject()` when E is pressed (outside tool mode and menus): doors, pickables, items on the ground, beds, chests, crafting stations.

## Known limits and rough edges

- The lightsaber camera: with the saber out, Outcast's camera sits behind and above Kyle (that is how Jedi Outcast shows the saber), so the picture shows his back while Valheim's camera is at his eyes. Not fixed.
- In tool mode, Valheim's whole body is hidden and only a copy of the held item is drawn. Bows (string animation), torch flames, shields and other special items may look wrong (only meshes are copied).
- Equipment is not linked: Valheim's items and Outcast's weapons are separate. Outcast uses its own weapons; Valheim's items are used only in tool mode.
- Outcast's health/ammo and Valheim's health/stamina are separate. Only creature damage to the player is mapped.
- Overlay latency: see lesson 9. Frames per second drop when both games share one GPU.
- No depth sharing between the pictures; Outcast's effects draw over walls.
- Terrain is a heightfield; no caves, interiors or climbing.
- Death and respawn: if the Outcast player dies, Outcast's own death handling runs and Valheim carries on.
- Weather (rain, fog) and time of day reach Outcast only as lighting.
- Save syncing: loading a Valheim save does not rewind Outcast (and the reverse).
- Valheim updates can break the plugin; run `tools/check_valheim_api.sh` after an update.

## Next steps (suggested order)

1. **Death and respawn.** Outcast death should kill the Valheim player and the respawn should reset Outcast (or the reverse). Hooks: Outcast's death path in `g_active.cpp`/`g_main.cpp` (the bridge already switches off on death) and a Valheim-side `Player.OnDeath`/respawn patch; probably a new message or reuse of `PlayerHurt` plus a "reset" message.
2. **Health and stamina link.** Food, potions and healing in Valheim should affect Outcast health (and the reverse); decide which side is the truth. Probably: Valheim's max health and healing events map to Outcast health through a new `PlayerVitals` message.
3. **Performance.** Reduce the overlay wait: read the picture back asynchronously (pixel buffer objects, double buffering), do the alpha conversion on the GPU, and/or accept one frame of latency when the wait would exceed a budget. Measure with the `perf:` log line and `tools/fake_overlay_client.py`.
4. **Verify the unconfirmed features in the real games** (see Status) and fix what the user reports: Use key, HitWorld on a real tree/rock (damage may be too low for large trees), lighter collision, menu mode, hotbar, `HeldItemView` offsets and special items.
5. **Lightsaber camera and first-person body.** Either force Outcast's first-person view while the saber is out or accept the view; draw Valheim's own arms properly.
6. **Crafted Outcast weapons (decided by the user, not built yet).** Outcast weapons and ammo become Valheim items that must be crafted; Outcast only allows what is in the Valheim inventory (new message for owned weapons/ammo; ammo count has to stay in sync). Unlock order:

| Weapon | Where | Material idea (first guess, to tune) |
|---|---|---|
| Stun baton | Start (no crafting) | none |
| Bryar pistol | Meadows | wood, flint, leather scraps, resin |
| E-11 blaster | Black Forest | bronze/copper, core wood, resin |
| Bowcaster | Swamp | iron, ancient bark, leech/entrails |
| Repeater | Mountain | silver, obsidian, wolf pelt |
| Flechette | Plains | black metal, flax, tar |
| Lightsaber | Mistlands | refined eitr, carapace, yggdrasil wood |
| Rocket launcher | Ashlands | flametal, ashlands items |
| Disruptor, DEMP2, thermal detonator, trip mine, detonation pack | Deep North | deep north materials |

**Built so far:** stun baton (given once at first spawn) and bryar pistol (workbench: 8 wood, 10 flint, 4 leather scraps, 5 resin) exist as inventory items; Outcast does not read them yet and ammo is not made. Ammo is crafted too, from the same biome's cheap materials. **No Jotunn or other item library (user's decision):** register the items and recipes ourselves with Harmony patches on `ObjectDB`, so users need nothing beyond BepInEx. Open: the exact material lists, and how Outcast's ammo counter follows the Valheim stack.
7. **Equipment mapping.** Map an equipped Valheim item to an Outcast weapon (a table sent as a weapon request) so the Valheim inventory matters.
8. **Weather and time of day** beyond lighting; **save syncing.**
9. **Multiplayer (long term).** Each player runs both games; Valheim's networking carries positions and damage. Others would see a normal Valheim character unless Kyle's model and animations are converted for Unity and weapon effects are recreated from synced events. Discussed, not planned in detail.
10. **Release polish:** one command to install everything, a Windows path (shared memory), documentation for other users.

## Risks

- Rendering and terrain collision were the hardest parts and remain the fragile ones.
- Valheim updates often break mods.
- OpenJK is an old engine; GPU texture sharing would take extra work.
- Real-game verification depends on the user; many features are tested only against fakes.

## Open questions

- Should Outcast's health or Valheim's be the truth for healing and food?
- How should death, respawn and saves be tied together?
- Is a lightsaber third-person view acceptable, or should the saber also be drawn first-person?

## Original phase notes (history)

**Phase 0 (setup):** Valheim (native Linux) + BepInEx installed; OpenJK built from `third_party/OpenJK` with the Outcast targets (`buildjo/`, not the Jedi Academy ones); `third_party/jk2run` has symlinks to the build and the Steam game files. Hello plugin loaded in Valheim (version 1.0.20, BepInEx 5.4.23.5); Outcast runs under OpenJK and `map kejim_post` loads.

**Phase 1 (connection):** spec in `protocol/PROTOCOL.md`; Python, C and C# links pass the golden packets and the connect/lose tests (`tools/test_protocol.py`, `tools/test_c.sh`, `tools/test_csharp.sh`). Confirmed with the real games (`link: connected`). Outcast stops ticking while a map loads, so a long load can show a brief "lost connection".

**Phase 2 (player sync):** walking and turning in Outcast moves and turns the Valheim character. `tools/fake_player.py` walks the Valheim player in a circle without Outcast.

**Phase 3 (terrain collision):** see "Feature notes". The bridge switches on only when the player is free and standing on the level's floor, and off for cutscenes and death.

**Phase 4 (creatures and combat):** see "Feature notes". Test helper: `VO_DEBUG_HIT=1`.

**Phase 5 (rendering):** see "How it fits together" and "Feature notes". `protocol/FRAME.md` is the file spec.

**Phase 6 (world sync and polish):** everything under "Feature notes" and "Status" beyond the first five phases.

**Phase 7 (release):** basic. Everything builds from this folder (see `README.md`); `tools/test_all.sh` runs all tests that need no game.

## Size

About 4,900 lines of our own code (C#, C/C++, Python, shell), plus an 800-line patch to OpenJK.
