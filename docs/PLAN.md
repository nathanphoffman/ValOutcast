# ValOutcast Plan

## Idea

Run both games at once. Outcast controls the player. Valheim supplies the world.
A plugin in each game talks to the other over a local connection.

## Pieces

1. **Valheim plugin (C#)** in `valheim/`: BepInEx + Harmony. Reads terrain, creatures, time and weather. Applies damage and position from Outcast.
2. **Outcast plugin (C)** in `outcast/`: changes to OpenJK's game code. Runs player movement, saber and Force. Uses Valheim's collision data.
3. **Protocol** in `protocol/`: one list of message types, used by both sides.
4. **Connection:** local UDP sockets first. Shared memory later if too slow.

## Unit conversion (to decide)

Valheim uses meters. Outcast uses its own units (roughly 1 meter = ~32 units). Pick one scale and keep it in one place.

## Steps

1. **Hello world:** both plugins connect and send heartbeats. If one dies, the other recovers.
2. **Player sync:** Outcast drives the player. Valheim's player follows.
3. **Terrain collision:** stream Valheim ground and objects near the player to Outcast as collision shapes. (Hardest part.)
4. **Creatures:** each Valheim creature becomes an invisible target in Outcast. Saber hits become damage in Valheim.
5. **Damage back:** creature hits on the player become Outcast damage, with a scaling factor.
6. **Rendering:** show Outcast's view inside Valheim. Start with a simple screen copy, optimize later.
7. **Polish:** time and weather sync, menus and input routing, save syncing.

## Risks

- Rendering and terrain collision are the hardest parts.
- Valheim updates often break mods.
- OpenJK uses an old engine, so GPU texture sharing may take extra work.

## Size estimate

About 10-20k lines of code. Up to ~30k with GPU rendering and full save sync.

## Out of scope (for now)

Multiplayer, Discord, Windows-only GPU sharing.

## Open questions

- Which Valheim version to target?
- Where does the Outcast view get displayed (overlay, texture on a screen, full window)?
- How are saves tied together?
