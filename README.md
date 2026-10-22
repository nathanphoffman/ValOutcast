# ValOutcast

A bridge between **Star Wars Jedi Knight: Jedi Outcast** and **Valheim**, inspired by [SkyCraft](https://github.com/chasmlol/SkyCraft).

- **Valheim** (the "Skyrim" side) is the main game. It draws the world and you look at its window.
- **Jedi Outcast** (the "Minecraft" side) runs the player: movement, saber, combat. It draws only its own things (hands, saber, effects, HUD), which Valheim shows over its picture.

Each game runs normally. A small plugin in each one passes messages to the other. See [docs/PLAN.md](docs/PLAN.md) for the plan and the status of each phase.

**Status:** all phases are built. Played in the real games: movement, terrain collision, the overlay with frame sync, shooting creatures, tool mode. Newer features (Use key, damaging trees and rocks, menus, the held-item view) are built but not all confirmed in the real games yet. `docs/PLAN.md` is the full handoff document: how it fits together, repository map, debug switches, lessons learned, status and next steps.

## Layout

| Folder | What it is |
|---|---|
| `valheim/` | Valheim plugin (C#, BepInEx + Harmony) |
| `outcast/` | Jedi Outcast side: our code (`src/`, `renderer/`) and a patch for OpenJK (`patches/`) |
| `protocol/` | The messages between the two games, plus the C and Python versions of the connection code |
| `tools/` | Tests, fake games, helper scripts |
| `docs/` | Plan and design notes |

## Requirements

- Linux. Valheim (native) with [BepInEx](https://github.com/BepInEx/BepInEx), started with the Steam launch option `./start_game_bepinex.sh %command%`
- A copy of Jedi Outcast (Steam)
- Build tools: .NET SDK, CMake, gcc/g++, and `libjpeg-dev libpng-dev libsdl2-dev libopenal-dev`

## Build

```
# Valheim plugin (also copies it into Valheim's BepInEx/plugins)
cd valheim && dotnet build

# Outcast: OpenJK plus our bridge
git clone https://github.com/JACoders/OpenJK third_party/OpenJK
tools/apply_outcast.sh                       # copies our code in and patches OpenJK
mkdir -p third_party/OpenJK/buildjo && cd third_party/OpenJK/buildjo
cmake .. -DCMAKE_BUILD_TYPE=Release -DBuildSPEngine=OFF -DBuildSPGame=OFF -DBuildSPRdVanilla=OFF \
  -DBuildMPCGame=OFF -DBuildMPGame=OFF -DBuildMPUI=OFF -DBuildMPDed=OFF -DBuildMPEngine=OFF \
  -DBuildMPRdVanilla=OFF -DBuildMPRend2=OFF -DBuildJK2SPEngine=ON -DBuildJK2SPGame=ON -DBuildJK2SPRdVanilla=ON
make -j$(nproc)
```

Then make `third_party/jk2run` with links to the three built files and your game's `base/*.pk3` (see `docs/PLAN.md`, "Phase 0 status").

## Run

1. Start Valheim, load a world, and wait until your character is standing on the ground.
2. Run `tools/run_outcast.sh` (optionally: `tools/run_outcast.sh artus_mine 1280 720`). Use the same shape as your Valheim window. The Outcast window stays hidden (it draws into an off-screen buffer, so it can't take focus or grab your mouse), and Outcast's cutscenes are skipped automatically. Set `VO_OVERLAY_VISIBLE=1` to show its window when debugging.
3. Play in the Valheim window.

## Controls and tuning

While Outcast drives: WASD, mouse, jump, crouch, left and right click and Use work as in Valheim. The hotbar number keys 1-8 pick an Outcast weapon, and the mouse wheel goes to the next or previous one. Esc still opens Valheim's menu.

`tools/run_outcast.sh` gives you every weapon, the lightsaber included, for testing (`VO_GIVE_ALL=` with nothing after it turns that off). Outcast stays silent until you are in a Valheim world. Typing `valleyofthejedi` in Valheim's chat box (Enter, type, Enter) gives every weapon with full ammo and all force powers.

R switches between Outcast weapons and Valheim's own items (tool mode: sword, bow, hammer, hoe, torch). In tool mode the hotbar comes back and Valheim handles the mouse buttons; press R again to go back to Outcast. E (Use) works on what the crosshair is over: doors, berries, items on the ground. Chests, crafting stations, signs, traders and the inventory (Tab) open Valheim's own menus: Outcast stands still and its picture is hidden while one is open. Shots and the lightsaber damage trees, rocks and ore.

Testing aid: F8 (in Valheim) spawns a boar 6 m in front of you, to try Outcast's weapons on. The Valheim log (`BepInEx/LogOutput.log`) shows `hit applied ...` lines when Outcast's hits reach a creature.

Settings are in `BepInEx/config/com.valoutcast.bridge.cfg` inside the Valheim folder (created on the first run): `MouseDegreesPerCount` (mouse speed) and `Brightness` (overlay brightness). Set `VO_SV_FPS` before `tools/run_outcast.sh` to change how often Outcast's game logic runs (default 60; Outcast itself uses 20).

## Test

`tools/test_all.sh` runs every test that needs no game running. `tools/fake_world.py` and `tools/fake_player.py` stand in for the games; `tools/start_test_session.sh` runs Outcast against the fake world (see `docs/PLAN.md`, "Build, run, test"). Close the real games first.
