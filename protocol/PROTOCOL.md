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
