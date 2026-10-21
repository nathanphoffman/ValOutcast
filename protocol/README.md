# protocol

Shared message definitions for both plugins. Planned messages:

- Hello / Heartbeat
- WorldContext (time, weather)
- CreatureUpsert / CreatureRemove
- CollisionSection
- PlayerState (Outcast to Valheim)
- HitCreature (Outcast to Valheim)
- PlayerHurt (Valheim to Outcast)
- FrameReady

Keep one source of truth here and have both sides match it.
