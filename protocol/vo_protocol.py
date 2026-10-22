"""ValOutcast protocol v1: packet codec and connection state. See PROTOCOL.md."""
import os
import socket
import struct
import time

MAGIC = 0x4F56
VERSION = 1
MSG_HELLO = 1
MSG_HEARTBEAT = 2
MSG_PLAYER_STATE = 3
MSG_COLLISION_SECTION = 4
MSG_COLLISION_RESET = 5
MSG_ACTOR_UPSERT = 6
MSG_ACTOR_REMOVE = 7
MSG_HIT_ACTOR = 8
MSG_PLAYER_HURT = 9
MSG_INPUT = 10
MSG_WORLD_CONTEXT = 11
MSG_HIT_WORLD = 12
MSG_PLAYER_LIGHT = 13
BUTTON_ATTACK = 1
BUTTON_ALT_ATTACK = 2
BUTTON_USE = 4
ACTOR_HOSTILE = 1
CHUNK_CELLS = 16
CELL_SIZE_M = 0.5
HEIGHT_UNITS_PER_M = 32
NO_HEIGHT = -32768
PLAYER_STATE_TIMEOUT_MS = 500
ROLE_WORLD = 0
ROLE_PLAYER = 1
PORTS = {ROLE_WORLD: 47100, ROLE_PLAYER: 47101}
HEARTBEAT_MS = 1000
TIMEOUT_MS = 3000

_HEADER = struct.Struct("<HBBI")


def encode_hello(seq, role, pid):
    return _HEADER.pack(MAGIC, VERSION, MSG_HELLO, seq) + struct.pack("<B3xI", role, pid)


def encode_heartbeat(seq, uptime_ms):
    return _HEADER.pack(MAGIC, VERSION, MSG_HEARTBEAT, seq) + struct.pack("<I", uptime_ms)


def encode_player_state(seq, pos, look):
    return _HEADER.pack(MAGIC, VERSION, MSG_PLAYER_STATE, seq) + struct.pack("<6f", *pos, *look)


def decode_player_state(payload):
    """Return (pos, look) as two 3-tuples, or None if the payload is too short."""
    if len(payload) < 24:
        return None
    v = struct.unpack_from("<6f", payload)
    return v[:3], v[3:]


def encode_collision_section(seq, cx, cz, heights):
    assert len(heights) == CHUNK_CELLS * CHUNK_CELLS
    return _HEADER.pack(MAGIC, VERSION, MSG_COLLISION_SECTION, seq) + struct.pack("<ii256h", cx, cz, *heights)


def decode_collision_section(payload):
    """Return (cx, cz, heights) or None if the payload is too short."""
    if len(payload) < 8 + 512:
        return None
    cx, cz = struct.unpack_from("<ii", payload)
    return cx, cz, list(struct.unpack_from("<256h", payload, 8))


def encode_collision_reset(seq):
    return _HEADER.pack(MAGIC, VERSION, MSG_COLLISION_RESET, seq)


def encode_actor_upsert(seq, actor_id, pos, radius, height, hp, flags):
    return _HEADER.pack(MAGIC, VERSION, MSG_ACTOR_UPSERT, seq) + struct.pack("<I3f2fBBH", actor_id, *pos, radius, height, hp, flags, 0)


def decode_actor_upsert(payload):
    """Return (id, pos, radius, height, hp, flags) or None."""
    if len(payload) < 28:
        return None
    v = struct.unpack_from("<I3f2fBBH", payload)
    return v[0], v[1:4], v[4], v[5], v[6], v[7]


def encode_actor_remove(seq, actor_id):
    return _HEADER.pack(MAGIC, VERSION, MSG_ACTOR_REMOVE, seq) + struct.pack("<I", actor_id)


def decode_actor_remove(payload):
    return struct.unpack_from("<I", payload)[0] if len(payload) >= 4 else None


def encode_hit_actor(seq, actor_id, damage, direction):
    return _HEADER.pack(MAGIC, VERSION, MSG_HIT_ACTOR, seq) + struct.pack("<I4f", actor_id, damage, *direction)


def decode_hit_actor(payload):
    """Return (id, damage, direction) or None."""
    if len(payload) < 20:
        return None
    v = struct.unpack_from("<I4f", payload)
    return v[0], v[1], v[2:5]


def encode_hit_world(seq, position, direction, damage, kind):
    return _HEADER.pack(MAGIC, VERSION, MSG_HIT_WORLD, seq) + struct.pack("<7fB3x", *position, *direction, damage, kind)


def decode_hit_world(payload):
    """Return (position, direction, damage, kind) or None."""
    if len(payload) < 32:
        return None
    v = struct.unpack_from("<7fB", payload)
    return v[0:3], v[3:6], v[6], v[7]


def encode_player_light(seq, rgb, intensity):
    return _HEADER.pack(MAGIC, VERSION, MSG_PLAYER_LIGHT, seq) + struct.pack("<4f", *rgb, intensity)


def decode_player_light(payload):
    """Return (rgb, intensity) or None."""
    if len(payload) < 16:
        return None
    v = struct.unpack_from("<4f", payload)
    return v[0:3], v[3]


def encode_player_hurt(seq, damage, direction, source):
    return _HEADER.pack(MAGIC, VERSION, MSG_PLAYER_HURT, seq) + struct.pack("<4fI", damage, *direction, source)


def decode_player_hurt(payload):
    """Return (damage, direction, source) or None."""
    if len(payload) < 20:
        return None
    v = struct.unpack_from("<4fI", payload)
    return v[0], v[1:4], v[4]


def encode_input(seq, buttons, forward, right, up, yaw, pitch, weapon=0):
    return _HEADER.pack(MAGIC, VERSION, MSG_INPUT, seq) + struct.pack("<I5fi", buttons, forward, right, up, yaw, pitch, weapon)


def decode_input(payload):
    """Return (buttons, forward, right, up, yaw, pitch, weapon) or None."""
    if len(payload) < 28:
        return None
    return struct.unpack_from("<I5fi", payload)


def encode_world_context(seq, sun_dir, sun_rgb, ambient_rgb):
    return _HEADER.pack(MAGIC, VERSION, MSG_WORLD_CONTEXT, seq) + struct.pack("<9f", *sun_dir, *sun_rgb, *ambient_rgb)


def decode_world_context(payload):
    """Return (sun_dir, sun_rgb, ambient_rgb) or None."""
    if len(payload) < 36:
        return None
    v = struct.unpack_from("<9f", payload)
    return v[0:3], v[3:6], v[6:9]


def decode(data):
    """Return (type, seq, payload) or None if the packet is not valid."""
    if len(data) < _HEADER.size:
        return None
    magic, version, mtype, seq = _HEADER.unpack_from(data)
    if magic != MAGIC or version != VERSION:
        return None
    return mtype, seq, data[_HEADER.size:]


def _now_ms():
    return int(time.monotonic() * 1000)


class Link:
    def __init__(self, role, log=print):
        self.role = role
        self.log = log
        self.connected = False
        self.seq = 0
        self.start_ms = _now_ms()
        self.last_send = -HEARTBEAT_MS
        self.last_recv = 0
        self.player_state = None      # (pos, look) of the newest PlayerState
        self.player_state_ms = 0
        self.inbox = []               # (type, decoded) for HitActor, PlayerHurt, Input, ActorUpsert, ActorRemove, collision
        self.peer = ("127.0.0.1", PORTS[ROLE_PLAYER if role == ROLE_WORLD else ROLE_WORLD])
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(("127.0.0.1", PORTS[role]))
        self.sock.setblocking(False)

    def _send_hello(self, now):
        self.seq += 1
        self.sock.sendto(encode_hello(self.seq, self.role, os.getpid()), self.peer)
        self.last_send = now

    def _send_heartbeat(self, now):
        self.seq += 1
        self.sock.sendto(encode_heartbeat(self.seq, now - self.start_ms), self.peer)
        self.last_send = now

    def send_player_state(self, pos, look):
        if not self.connected:
            return
        self.seq += 1
        self.sock.sendto(encode_player_state(self.seq, pos, look), self.peer)

    def send_collision_section(self, cx, cz, heights):
        if not self.connected:
            return
        self.seq += 1
        self.sock.sendto(encode_collision_section(self.seq, cx, cz, heights), self.peer)

    def send_raw(self, make_packet):
        """Send a packet built by make_packet(seq), if connected."""
        if not self.connected:
            return
        self.seq += 1
        self.sock.sendto(make_packet(self.seq), self.peer)

    def send_collision_reset(self):
        if not self.connected:
            return
        self.seq += 1
        self.sock.sendto(encode_collision_reset(self.seq), self.peer)

    def tick(self):
        now = _now_ms()
        while True:
            try:
                data, _ = self.sock.recvfrom(2048)
            except BlockingIOError:
                break
            msg = decode(data)
            if msg is None:
                continue
            self.last_recv = now
            if msg[0] == MSG_PLAYER_STATE:
                state = decode_player_state(msg[2])
                if state is not None:
                    self.player_state = state
                    self.player_state_ms = now
            decoders = {
                MSG_ACTOR_UPSERT: decode_actor_upsert, MSG_ACTOR_REMOVE: decode_actor_remove,
                MSG_HIT_ACTOR: decode_hit_actor, MSG_HIT_WORLD: decode_hit_world, MSG_PLAYER_HURT: decode_player_hurt, MSG_INPUT: decode_input, MSG_WORLD_CONTEXT: decode_world_context,
                MSG_PLAYER_LIGHT: decode_player_light,
                MSG_COLLISION_SECTION: decode_collision_section,
            }
            if msg[0] in decoders:
                value = decoders[msg[0]](msg[2])
                if value is not None:
                    self.inbox.append((msg[0], value))
            if not self.connected:
                self.connected = True
                self.log("connected")
                if msg[0] == MSG_HELLO:
                    self._send_hello(now)
        if self.connected and now - self.last_recv > TIMEOUT_MS:
            self.connected = False
            self.log("lost connection")
        if now - self.last_send >= HEARTBEAT_MS:
            if self.connected:
                self._send_heartbeat(now)
            else:
                self._send_hello(now)

    def close(self):
        self.sock.close()
