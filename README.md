# ValOutcast

A bridge between **Star Wars Jedi Knight: Jedi Outcast** and **Valheim**, inspired by [SkyCraft](https://github.com/chasmlol/SkyCraft).

- **Jedi Outcast** (the "Minecraft" side) runs the player: movement, lightsaber, Force powers.
- **Valheim** (the "Skyrim" side) runs the world: terrain, creatures, time, weather.

Each game runs normally. A small plugin in each one passes messages to the other.

**Status:** scaffolding only. Nothing works yet. See [docs/PLAN.md](docs/PLAN.md).

## Layout

| Folder | What it is |
|---|---|
| `valheim/` | Valheim plugin (C#, BepInEx + Harmony) |
| `outcast/` | Jedi Outcast changes (C, on top of OpenJK) |
| `protocol/` | Shared message definitions used by both plugins |
| `tools/` | Fake-game test scripts, packaging |
| `docs/` | Plan and design notes |

## Requirements (planned)

- Linux (Windows later, maybe)
- Valheim with [BepInEx](https://github.com/BepInEx/BepInEx)
- [OpenJK](https://github.com/JACoders/OpenJK) and your own copy of Jedi Outcast game files
- .NET SDK for the Valheim plugin, CMake + a C compiler for OpenJK
