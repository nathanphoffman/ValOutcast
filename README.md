# ValOutcast

A bridge between **Star Wars Jedi Knight: Jedi Outcast** and **Valheim**, inspired by [SkyCraft](https://github.com/chasmlol/SkyCraft).

- **Valheim** (the "Skyrim" side) is the main game. It draws the world and you look at its window.
- **Jedi Outcast** (the "Minecraft" side) runs the player: movement, saber, combat. It draws only its own things (hands, saber, effects, HUD), which Valheim shows over its picture.

Each game runs normally. A small plugin in each one passes messages to the other. See [docs/PLAN.md](docs/PLAN.md) for the plan and the status of each phase.

**Status:** all phases are built and tested against fake partners. The two real games have been tested together only up to player movement (Phase 2).

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
2. Run `tools/run_outcast.sh` (optionally: `tools/run_outcast.sh artus_mine 1280 720`). Use the same shape as your Valheim window. Leave the Outcast window open behind Valheim, not minimized.
3. Wait for the Outcast cutscene to finish, then click the Valheim window and play there.

## Test

`tools/test_all.sh` runs every test that needs no game running. `tools/fake_world.py` and `tools/fake_player.py` stand in for the games.
