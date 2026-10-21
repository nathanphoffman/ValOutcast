# ValOutcast Plan

## Idea

Run both games at once. Outcast controls the player. Valheim supplies the world.
A plugin in each game talks to the other over a local connection.

## Pieces

1. **Valheim plugin (C#)** in `valheim/`: BepInEx + Harmony. Reads terrain, creatures, time and weather. Applies damage and position from Outcast.
2. **Outcast plugin (C)** in `outcast/`: changes to OpenJK's game code. Runs player movement, saber and Force. Uses Valheim's collision data.
3. **Protocol** in `protocol/`: one list of message types, used by both sides.
4. **Connection:** local UDP sockets first. Shared memory later if too slow.

## Game-neutral rule

We may swap in other games later (for example Skyrim or Doom 2016). So:

- **Two roles, not two games.** A *player side* runs the player (movement, combat). A *world side* supplies the world (terrain, creatures, time). Today Outcast is the player side and Valheim is the world side.
- **Protocol never names a game.** Messages use neutral names: `WorldContext`, `ActorUpsert`, `CollisionSection`, `PlayerState`, `HitActor`, `PlayerHurt`. No "Greydwarf", "saber" or game-specific IDs. Anything game-specific goes in an opaque `kind` string or a generic extras field.
- **Neutral units on the wire:** meters, Y-up, right-handed. Each game's plugin (its *adapter*) converts to and from its own units and axes. Nothing outside an adapter knows about 32 units per meter.
- **Adapters are thin.** An adapter only translates and hooks into its game. Shared logic (connection, heartbeats, message encoding, chunking) lives in `protocol/`, not in the game folders.
- **Don't build a framework yet.** Finish Valheim + Outcast, then pull out what is truly shared before adding a third game.
- **Test without games.** The fake-game scripts in `tools/` speak the neutral protocol, so any new adapter can be tested against them.

## Unit conversion

**Decided:** on the wire it's meters, Y-up. The Outcast adapter converts to 32 Outcast units per meter (Outcast's player is ~56 units tall, close to a 1.8 m person) and to Z-up. The Valheim adapter needs no conversion (meters, Y-up). The conversion code lives only in the adapter.

## Phase 0 status

- Valheim (native Linux) + BepInEx: already installed.
- OpenJK: built from `third_party/OpenJK`. Run folder is `third_party/jk2run` (symlinks to the build and your Steam game files). Start with `cd third_party/jk2run && ./openjk_sp.x86_64 +set fs_basepath "$PWD"`.
- Hello plugin: `valheim/` builds and copies itself into Valheim's `BepInEx/plugins/ValOutcast`.
- Not yet confirmed: the plugin loading inside Valheim, and an Outcast map loading in OpenJK.

## Phases

Each phase ends with something you can run and check. Don't start the next one until "Done when" is true.

### Phase 0: Setup
- Install Valheim + BepInEx, build OpenJK, confirm Jedi Outcast runs under OpenJK.
- Pick the unit scale and the Valheim version to target.
- **Done when:** a "hello" BepInEx plugin loads in Valheim, and your OpenJK build starts a map.

### Phase 1: Connection
- Define Hello/Heartbeat in `protocol/`. Add UDP send/receive on both sides.
- Write `tools/fake_world.py` and `tools/fake_player.py` so each side can be tested alone (neutral names, not game names).
- If one side dies, the other detects it (heartbeat timeout) and carries on.
- **Done when:** both games show "connected" in their logs, and killing one is noticed by the other.

### Phase 2: Player sync
- Outcast sends PlayerState (position, look direction) every frame.
- Valheim moves its player to match and hides its own input.
- **Done when:** walking in Outcast moves the Valheim character the same way.

### Phase 3: Terrain collision (hardest)
- Valheim sends ground and object shapes near the player in chunks.
- Outcast adds them to its collision checks and drops far-away chunks.
- **Done when:** you can walk on Valheim hills and bump into trees and buildings from the Outcast side.

### Phase 4: Creatures and combat
- Each Valheim creature near the player becomes an invisible target in Outcast.
- Saber hits send HitCreature, and Valheim applies the damage.
- Creature attacks on the player send PlayerHurt, scaled, and Outcast applies it.
- **Done when:** you can kill a Greydwarf with a saber and a Greydwarf can kill you.

### Phase 5: Rendering
- First version: copy Outcast's frame into a texture or overlay shown in Valheim (slow is fine).
- Later: faster path (shared GPU memory) if the first version is too slow.
- **Done when:** you see Outcast's view of the Valheim world on screen at a playable frame rate.

### Phase 6: World sync and polish
- Time of day and weather from Valheim to Outcast.
- Menu and input routing (pause, inventory, escape).
- Save syncing so loading a save matches both games.
- **Done when:** a 30-minute play session works without desyncs or crashes.

### Phase 7: Release
- Packaging script in `tools/`, install instructions in the README, known issues list.
- **Done when:** someone else can install it from the README alone.

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
