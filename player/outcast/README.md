# outcast

The player side. Changes on top of OpenJK (C/C++) that run the player and talk to the world side.

- `src/vo_bridge.cpp/h`: the bridge in the game module (input, player state, creatures as boxes, hits, tool-mode weapon requests, lighting, sound).
- `src/vo_field.cpp/h`: the collision field (Valheim's ground as columns) and the sweep test. Pure math, unit tested (`tools/test_field.sh`).
- `renderer/vo_overlay.cpp/h`: the renderer side of the overlay (off-screen buffer, alpha, GPU read-back, shared-memory picture).
- `patches/0001-bridge-hooks.patch`: our edits to OpenJK's own files (list in `docs/PLAN.md`, "The OpenJK patch").

Install into the OpenJK checkout and build:

```
tools/apply_outcast.sh
cd third_party/OpenJK/buildjo && make -j$(nproc)
```

`tools/apply_outcast.sh` copies the files above into `third_party/OpenJK` and applies the patch. To change OpenJK's own files, edit them in `third_party/OpenJK`, then regenerate the patch: `cd third_party/OpenJK && git diff > ../../player/outcast/patches/0001-bridge-hooks.patch`. Edit our added files (`src/`, `renderer/`, `protocol/vo_link.*`) here, not the copies in `third_party/`.

Look for `ValOutcast:` lines in the game's console output. Run it with `tools/run_outcast.sh` (see the main README and `docs/PLAN.md` for the environment switches).
