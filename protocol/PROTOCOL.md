# ValOutcast wire protocol (v1)

Game-neutral. Plain UDP on 127.0.0.1. Every number is little-endian.

## Roles and ports

| Role | Value | Listens on |
|---|---|---|
| world side (Valheim today) | 0 | 127.0.0.1:47100 |
| player side (Outcast today) | 1 | 127.0.0.1:47101 |

Each side sends to the other side's port.

## Header (8 bytes, on every packet)

| Offset | Type | Field |
|---|---|---|
| 0 | u16 | magic = 0x4F56 (bytes `56 4F`, "VO") |
| 2 | u8 | protocol version = 1 |
| 3 | u8 | message type |
| 4 | u32 | sequence number (counts up per sender, starts at 1) |

Packets with the wrong magic or version, or too short, are dropped silently.

## Messages

| Type | Name | Payload |
|---|---|---|
| 1 | Hello | u8 role, 3 bytes zero, u32 pid (8 bytes) |
| 2 | Heartbeat | u32 uptime in milliseconds (4 bytes) |
| 3 | PlayerState | 6 x f32: position x y z (meters), look direction x y z (unit vector). 24 bytes. Player side to world side. Position is the **feet** (bottom center of the player). |
| 4 | CollisionSection | i32 chunkX, i32 chunkZ, then 256 x i16 heights (row by row, 16 rows of 16). 520 bytes. World side to player side. |
| 5 | CollisionReset | no payload. World side to player side: forget all collision chunks (the coordinate mapping changed). |
| 6 | ActorUpsert | u32 id, 3 x f32 feet position (meters), f32 radius (m), f32 height (m), u8 health 0-255 (255 = full), u8 flags (bit 0 = hostile), u16 zero. 28 bytes. World side to player side, about 10 times a second per creature. |
| 7 | ActorRemove | u32 id. 4 bytes. World side to player side. |
| 8 | HitActor | u32 id, f32 damage (the player side's own damage points), 3 x f32 direction of the blow (unit vector, wire space). 20 bytes. Player side to world side. |
| 9 | PlayerHurt | f32 damage (the world side's own damage points), 3 x f32 direction the blow travels (unit vector, wire space), u32 source id (0 = unknown). 20 bytes. World side to player side. |
| 10 | Input | u32 buttons, f32 forward, f32 right, f32 up (each -1..1), f32 look yaw change, f32 look pitch change (degrees since last packet; positive yaw turns left, positive pitch looks up), i32 weapon request. 28 bytes. World side to player side. |
| 11 | WorldContext | 9 x f32: direction toward the sun (unit vector, wire space), sun color x3 (brightness already applied, about 0 to 1.5), ambient light color x3 (0 to 1). 36 bytes. World side to player side, about 4 times a second. The player side uses it to light its models the way the world is lit. |
| 12 | HitWorld | 3 x f32 position (meters), 3 x f32 direction of the blow (unit vector), f32 damage (the player side's own damage points), u8 kind (0 = shot, 1 = lightsaber), 3 bytes zero. 32 bytes. Player side to world side. Sent when a shot or a lightsaber swing hits the world itself (not a creature), so the world side can damage whatever stands there (trees, rocks). |

Input buttons: bit 0 primary attack, bit 1 secondary attack, bit 2 use. Weapon request: 0 = none, 1 to 9 = pick that weapon slot, 10 = next weapon, 11 = previous weapon. The world side sends a request once, in the packet right after the key press. The player side remembers a button press until it has used it, so a very short click is not lost. Input is sent about 60 times a second while the world side's window has the player's attention (no menu open). If nothing arrives for 500 ms the player side goes back to reading its own keyboard.

Damage numbers are each game's own points. Each side scales what it receives to its own scale (see the adapters; today Outcast turns Valheim damage into Outcast damage by multiplying by 4, and Valheim takes Outcast damage as is).

### Creatures

The world side sends the nearby creatures (within about 40 m) as ActorUpsert. The player side makes an invisible box for each one, so the player can walk into it and hit it. Each creature is identified by its id. An id the player side has not heard about for 1 s is dropped. The player side reports damage with HitActor and keeps the box's own health full, so the world side decides who is alive.

### Teleports

If PlayerState jumps more than 10 m between two updates, the world side treats it as a teleport on the player side: it keeps its own player where it is, re-anchors to the new position, and sends CollisionReset followed by a fresh field. The same happens after PlayerState stops arriving for 500 ms.

### Collision field

The ground is a grid of vertical columns. A column is solid from far below up to its height.

- Cell size: 0.5 m. A chunk is 16 x 16 cells (8 m x 8 m). Chunk (cx, cz) covers wire X from cx*8 to cx*8+8 and wire Z from cz*8 to cz*8+8.
- Heights are i16 in 1/32 m (Y, same space as PlayerState positions). The value -32768 means "no data here" (no column).
- Row index runs along Z, column index along X: `heights[iz * 16 + ix]`.
- The world side expresses the field in the **player side's** coordinate space, so the player side can use it as is.
- The player side drops chunks that are far away. The world side resends chunks now and then, so a lost packet is repaired.

### Coordinates on the wire

Meters. Right-handed, Y up: +X right, +Y up, -Z forward (like OpenGL). Look direction is a unit vector in the same space.
Positions are absolute in the sender's own map. The receiver decides what they mean (the world side anchors the first position to the real player, so only movement matters).

## Connection rules

- Heartbeat interval: 1000 ms. Timeout: 3000 ms.
- Not connected: send Hello every interval.
- Any valid packet from the peer while not connected: become **Connected**. If it was a Hello, answer with one Hello right away.
- Connected: send Heartbeat every interval.
- Connected and nothing received for the timeout: become **Disconnected** and log "lost connection". Keep running and keep sending Hello so the peer can come back.
- Log "connected" on connecting.

## Golden packets (every implementation is tested against these)

| Packet | Bytes |
|---|---|
| Hello, role 1, pid 1234, seq 1 | `56 4F 01 01 01 00 00 00  01 00 00 00 D2 04 00 00` |
| Heartbeat, uptime 5000, seq 2 | `56 4F 01 02 02 00 00 00  88 13 00 00` |
| PlayerState, pos (1,2,3), look (0,0,-1), seq 3 | `56 4F 01 03 03 00 00 00  00 00 80 3F  00 00 00 40  00 00 40 40  00 00 00 00  00 00 00 00  00 00 80 BF` |
