# protocol

Shared, game-neutral message definitions. See "Game-neutral rule" in `docs/PLAN.md`.

Rules: no game names in messages, meters and Y-up on the wire, game-specific data goes in an opaque `kind` string or extras field. Unit and axis conversion happens in each game's adapter, not here.

Planned messages:

- Hello / Heartbeat
- WorldContext (time, weather)
- ActorUpsert / ActorRemove (creatures, NPCs)
- CollisionSection
- PlayerState (player side to world side)
- HitActor (player side to world side)
- PlayerHurt (world side to player side)
- FrameReady

Keep one source of truth here and have both sides match it.
