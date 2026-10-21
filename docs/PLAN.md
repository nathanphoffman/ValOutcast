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
- OpenJK: built from `third_party/OpenJK`. Run folder is `third_party/jk2run` (symlinks to the build and your Steam game files). Built with the Outcast targets (`buildjo/`, not the Jedi Academy ones). Start with `cd third_party/jk2run && ./openjo_sp.x86_64 +set fs_basepath "$PWD"`.
- Hello plugin: `valheim/` builds and copies itself into Valheim's `BepInEx/plugins/ValOutcast`.
- **Confirmed:** the hello plugin loads in Valheim (log line `ValOutcast hello: plugin loaded`). Valheim version l-1.0.20, BepInEx 5.4.23.5. Steam launch option must be `./start_game_bepinex.sh %command%`, otherwise BepInEx doesn't load.
- **Confirmed:** Outcast runs under OpenJK and `map kejim_post` loads (the level intro plays). **Phase 0 is done.**

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

**Phase 1 status:** built and tested headless; only the real Valheim-to-Outcast run is left.
- Spec: `protocol/PROTOCOL.md` (ports 47100 world / 47101 player, golden packets).
- Python, C and C# links all pass the golden packets and the connect/lose tests: `tools/test_protocol.py`, `tools/test_c.sh`, `tools/test_csharp.sh`.
- Outcast side confirmed against `tools/fake_world.py`: logs "connected", and "lost connection" when the fake quits.
- Note: Outcast stops ticking while a map loads, so a long load can show a brief "lost connection" then "connected" again. Expected.
- **Confirmed with the real games:** Valheim logged `link: connected` while Outcast was running. The real-game "lost connection" check was not isolated (both games were closed together); the fake-partner tests cover it. **Phase 1 is done.**

### Phase 2: Player sync
**Status: done.** Confirmed with the real games: walking and turning in Outcast moves and turns the Valheim character. Outcast sends PlayerState (position + look direction) every frame; the Valheim plugin moves the player to match, anchored to where the player stands, and ignores keyboard and mouse while data is arriving. Terrain is ignored for now, so the Valheim character can float or clip (fixed in Phase 3). Test without Outcast: run `tools/fake_player.py`, it walks the Valheim player in a circle.
- Outcast sends PlayerState (position, look direction) every frame.
- Valheim moves its player to match and hides its own input.
- **Done when:** walking in Outcast moves the Valheim character the same way.

### Phase 3: Terrain collision (hardest)
**Status:** built and tested against a fake world (walls block, ground holds). Not yet seen with real Valheim terrain.
- Valheim samples ground height (and "something is in the way" for trees, rocks, buildings) on a 0.5 m grid around the player and streams it as 8 m chunks (`CollisionSection`). See `protocol/PROTOCOL.md`.
- Outcast keeps the chunks (`outcast/src/vo_field.cpp`, unit tested with `tools/test_field.sh`) and wraps the player's movement trace (`VO_PlayerTrace`). Over Valheim ground, the level's own walls and floors are ignored.
- The bridge only switches on when the player is free and standing on the level's floor, and switches off for cutscenes and death. (Turning it on mid-air in an intro made Kyle fall and die, and left a cutscene waiting.)
- Limits for now: terrain is a heightfield (no caves, overhangs, or walking under roofs), things standing on the ground (trees, rocks, walls) become columns as tall as they really are (low ones, under about half a meter, can be stepped onto; no climbing on anything else), ground is cut into 0.5 m steps.
- Test without Valheim: `tools/fake_world.py` makes flat ground inside a 6 m ring wall, and Outcast could not walk out of it.

### Phase 4: Creatures and combat
**Status:** built. Tested against a fake world: creatures appear as boxes, hits come back, damage kills the player. Not yet tried against real Valheim creatures.
- Valheim sends creatures within 40 m (`ActorUpsert`, 10 times a second). Outcast makes an invisible box for each (`vo_actor`), so you can walk into it and hit it with anything that hurts entities.
- Damage taken by a box is sent back (`HitActor`) and the box is healed, so Valheim decides who dies. Valheim turns it into a normal hit on the creature.
- Hits on the Valheim player are caught, sent to Outcast (`PlayerHurt`) and applied there. Only attacks and harmful effects from creatures are passed on (falls, drowning and so on are ignored, because the other game moves the player and Valheim would count that as falling). Damage is also ignored for 3 seconds after the two games line up, and one blow can take at most 50 from Outcast's player. Outcast's health is the real one. Valheim damage x4 = Outcast damage. These numbers are guesses and live in `outcast/src/vo_bridge.cpp` (`HURT_SCALE`) and `valheim/src/Combat.cs` (`OutcastToValheim`).
- Test helpers: `VO_DEBUG_HIT=1` makes the player "hit" the first creature every few seconds.

### Phase 5: Rendering
**Status:** built. Outcast's side confirmed by viewing real frames; Valheim's drawing code is written but not seen on screen yet.
- Outcast draws only its own things (first-person weapon, saber, effects, HUD) on a transparent background and publishes each frame in shared memory (`/dev/shm/valoutcast_frame`, see `protocol/FRAME.md`). Turned on by `VO_OVERLAY=1` (`tools/run_outcast.sh` does it). The level, its people, rain and props are not drawn.
- Valheim reads the frame (`FrameReader.cs`, tested against a real frame) and draws it over its own picture (`Overlay.cs`). Its camera is forced into first person while Outcast drives.
- Inspect a frame without Valheim: `tools/dump_frame.py out.png`.
- Limits: the overlay has no depth, so Outcast's things always draw on top of Valheim's world (nothing hides behind a Valheim wall). Frames go through the CPU (about 3.7 MB each at 1280x720), which should be fine for now. The Outcast window is hidden and it draws into an off-screen buffer (`VO_OVERLAY_VISIBLE=1` shows it for debugging), so it never takes focus or the mouse. Cutscenes are skipped automatically in this mode.

### Phase 6: World sync and polish
**Status:** partly done.
- Done: Valheim keyboard and mouse drive Outcast (`Input` message, `InputForwarder.cs`). Input stops while any Valheim menu, chat or console is open. Bridge switches itself off in cutscenes and on death.
- Done: weapon switching (Valheim hotbar keys 1-8 pick a weapon slot, mouse wheel = next/previous weapon), and short clicks are not lost. While Outcast drives, Valheim's own hotbar, build and inventory screens are off (Esc still opens Valheim's menu).
- Done: Outcast's sound keeps playing when its window is not focused, and the game logic runs at 60 updates a second (`VO_SV_FPS`, default 60) instead of 20, so movement is smooth and responds quicker. Walking speed was checked to be the same at 60.
- Done: tuning in `BepInEx/config/com.valoutcast.bridge.cfg` (created the first time Valheim runs with the plugin): `MouseDegreesPerCount` (mouse speed, default 1) and `Brightness` (overlay brightness, default 0.85).
- Done: sound is silent until Valheim is in a world and Outcast has taken over. It is muted again in cutscenes, while loading and after death, and music is off. (`vo_mute` console command, sent by the game code.)
- Done: Outcast only switches on once Valheim is in a world (Valheim sends input only then), so it idles at Valheim's title screen.
- Done: while Outcast drives, Valheim's hotbar (the numbered tool slots) is hidden.
- Done: shots, aiming and the crosshair now use Valheim's ground and creatures, not the hidden Outcast level (before, shots hit the level and the crosshair drifted toward the gun). The level's water, lava and fog are ignored too.
- Done: `VO_GIVE_ALL` (on in `tools/run_outcast.sh`) gives every weapon, the lightsaber included, and 999 of every ammo type at the start of each level, topped up every half second, for testing.
- Done: right click (Valheim's "Block") and middle click ("SecondaryAttack") are both Outcast's alternate fire.
- Done: the lightsaber blade now shows in levels where Kyle starts without his saber (`give all` skipped the saber's setup; the bridge now does it). Models in the picture are lit evenly instead of by the invisible level's lighting (Kyle was a black silhouette in `artus_mine`). The picture is published 45 times a second (`VO_OVERLAY_FPS`), because fast things like bullets can be missed at lower rates.
- Done: bullets, beams and effect events are always sent to Outcast's own view (they used to vanish once they left the hidden level's "visible from here" area). A bullet flying straight away from you is a small glowing dot near the crosshair, which is correct for a first-person view.
- Done: lighting. Valheim sends the sun direction and sun and ambient colors (`WorldContext`, 4 times a second) and Outcast lights its models to match: bright by day, dark at night. (This replaces "time and weather sync" below for the part that matters for a picture of hands and a saber.)
- Done: frame sync. Valheim asks for one picture per frame it draws (a counter in the shared frame file) and Outcast draws exactly one picture per request, instead of two free-running clocks. Tested: 298 requests gave 298 pictures, and no requests gave no drawing. The picture is about one frame behind. It is not a lockstep where Valheim waits.
- Done: see-through pixels at the edges of Outcast's picture now carry the color of their neighbors, so smoothing no longer makes dark outlines (the "green screen" look).
- Known: with the lightsaber out, Outcast's camera sits behind and above Kyle (that is how Jedi Outcast shows the saber), so the picture shows his back, while Valheim's camera is at his eyes.
- Equipment is not linked: Valheim's items and inventory are not connected to Outcast's weapons. Outcast uses its own. Mapping (for example, an equipped Valheim sword to the lightsaber) would be a table that the Valheim plugin sends as a weapon request, but it is not built.
- Done: plugin parts switch themselves off after one error instead of flooding the log.
- Not done: weather (rain, fog) and time of day beyond the lighting above.
- Not done: save syncing. Loading a Valheim save does not rewind Outcast (and the reverse).
- Not done: death and respawn. When the Outcast player dies, Outcast's own death handling runs and Valheim carries on.

### Phase 7: Release
**Status:** basic version. Everything builds from this folder (see the main README). `tools/test_all.sh` runs all tests that need no game.

## Risks

- Rendering and terrain collision are the hardest parts.
- Valheim updates often break mods.
- OpenJK uses an old engine, so GPU texture sharing may take extra work.

## Size estimate

About 10-20k lines of code. Up to ~30k with GPU rendering and full save sync.

## Out of scope (for now)

Multiplayer, Discord, Windows-only GPU sharing, time and weather sync, save syncing, depth sharing between the two pictures.

## Open questions

- Which Valheim version to target?
- Where does the Outcast view get displayed (overlay, texture on a screen, full window)?
- How are saves tied together?
