# outcast

The player side. Changes on top of OpenJK (C/C++) that run the player and talk to the world side.

- `src/` is our bridge code (`vo_bridge.cpp/h`). It uses `protocol/vo_link.c`.
- `patches/` has the small patch to OpenJK's own files (`g_main.cpp`, `CMakeLists.txt`).

Install into the OpenJK checkout and build:

```
tools/apply_outcast.sh
cmake --build third_party/OpenJK/buildjo -j
```

Look for `ValOutcast:` lines in the game's console output.
