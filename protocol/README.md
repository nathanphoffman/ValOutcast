# protocol

Shared, game-neutral message definitions. See "Game-neutral rule" in `docs/PLAN.md`.

Rules: no game names in messages, meters and Y-up on the wire, game-specific data goes in an opaque `kind` string or extras field. Unit and axis conversion happens in each game's adapter, not here.

- `PROTOCOL.md`: the UDP messages (Hello, Heartbeat, PlayerState, CollisionSection, CollisionReset, ActorUpsert, ActorRemove, HitActor, PlayerHurt, Input, WorldContext, HitWorld) and the rules around them.
- `FRAME.md`: the picture file in shared memory (header, request counter, field of view, pixels).
- `c/` (C, used by Outcast: `vo_link.h` is the public API, `codec/` has the packet encoders), `python/` (import `vo_protocol`; used by the fake games and tests); the C# version is `worlds/common/VoLink.cs` plus `worlds/common/link/`.
- `golden.txt`: the exact bytes every implementation must produce. Regenerate it only when the protocol changes on purpose: `python3 tools/make_golden.py`.

Keep one source of truth here and have all sides match it. Adding a message means touching all three implementations, `tools/make_golden.py`, the three test files and `PROTOCOL.md` (list in `docs/PLAN.md`, "Message list").
