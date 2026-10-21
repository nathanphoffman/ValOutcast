# valheim

The world side. BepInEx + Harmony plugin (C#).

- `src/Plugin.cs` is the plugin entry point.
- `src/VoLink.cs` is the connection code (no Unity types, so it can be tested alone).

Build with `dotnet build`. It copies itself into Valheim's `BepInEx/plugins/ValOutcast`.
Valheim must be started with the Steam launch option `./start_game_bepinex.sh %command%`.
Look for `link:` lines in `BepInEx/LogOutput.log`.
