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
