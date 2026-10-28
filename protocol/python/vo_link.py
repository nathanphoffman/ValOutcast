"""The connection: a UDP socket plus the connect, timeout and heartbeat rules."""
import os
import select
import socket
import time

from vo_codec_actor import *
from vo_codec_state import *
from vo_codec_world import *
from vo_constants import *

RECV_BUFFER_BYTES = 2048

# Which decoder handles the payload of each message type that goes into the inbox.
DECODERS = {
    MSG_ACTOR_UPSERT: decode_actor_upsert, MSG_ACTOR_REMOVE: decode_actor_remove,
    MSG_HIT_ACTOR: decode_hit_actor, MSG_HIT_WORLD: decode_hit_world, MSG_PLAYER_HURT: decode_player_hurt,
    MSG_INPUT: decode_input, MSG_WORLD_CONTEXT: decode_world_context, MSG_PLAYER_LIGHT: decode_player_light,
    MSG_ACTOR_PUSH: decode_actor_push, MSG_ACTOR_CHARM: decode_actor_charm, MSG_ACTOR_GRIP: decode_actor_grip,
    MSG_ACTOR_HEAL: decode_actor_heal, MSG_LOADOUT: decode_loadout, MSG_COLLISION_SECTION: decode_collision_section,
}


def _now_ms():
    return int(time.monotonic() * 1000)


class Link:
    def __init__(self, role, logger=print):
        self.role = role
        self.logger = logger
        self.connected = False
        self.sequence = 0
        self.start_ms = _now_ms()
        self.last_send = -HEARTBEAT_MS
        self.last_recv = 0
        self.timeout_ms = TIMEOUT_MS
        self.player_state = None      # (pos, look) of the newest PlayerState
        self.player_state_ms = 0
        self.inbox = []               # (type, decoded) for HitActor, PlayerHurt, Input, ActorUpsert, ActorRemove, collision
        self.peer = ("127.0.0.1", PORTS[ROLE_PLAYER if role == ROLE_WORLD else ROLE_WORLD])
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(("127.0.0.1", PORTS[role]))
        self.sock.setblocking(False)

    def _send_hello(self, current_ms):
        self.sequence += 1
        self.sock.sendto(encode_hello(self.sequence, self.role, os.getpid()), self.peer)
        self.last_send = current_ms

    def _send_heartbeat(self, current_ms):
        self.sequence += 1
        self.sock.sendto(encode_heartbeat(self.sequence, current_ms - self.start_ms), self.peer)
        self.last_send = current_ms

    def send_player_state(self, position, look):
        if not self.connected:
            return

        self.sequence += 1
        self.sock.sendto(encode_player_state(self.sequence, position, look), self.peer)

    def send_collision_section(self, chunk_x, chunk_z, heights):
        if not self.connected:
            return

        self.sequence += 1
        self.sock.sendto(encode_collision_section(self.sequence, chunk_x, chunk_z, heights), self.peer)

    def send_raw(self, make_packet):
        """Send a packet built by make_packet(seq), if connected."""

        if not self.connected:
            return

        self.sequence += 1
        self.sock.sendto(make_packet(self.sequence), self.peer)

    def send_collision_reset(self):
        if not self.connected:
            return

        self.sequence += 1
        self.sock.sendto(encode_collision_reset(self.sequence), self.peer)

    def _handle_packet(self, message, current_ms):
        """Remember when it arrived, store what it carries, and treat the first packet as "connected"."""

        self.last_recv = current_ms

        if message[0] == MSG_PLAYER_STATE:
            state = decode_player_state(message[2])

            if state is not None:
                self.player_state = state
                self.player_state_ms = current_ms

        if message[0] in DECODERS:
            value = DECODERS[message[0]](message[2])

            if value is not None:
                self.inbox.append((message[0], value))

        if not self.connected:
            self.connected = True
            self.logger("connected")

            if message[0] == MSG_HELLO:
                self._send_hello(current_ms)

    def _drain_socket(self, current_ms):
        # select with a zero timeout asks "is a packet waiting?" without blocking.
        while select.select([self.sock], [], [], 0)[0]:
            data, _ = self.sock.recvfrom(RECV_BUFFER_BYTES)
            message = decode(data)

            if message is not None:
                self._handle_packet(message, current_ms)

    def _check_timeout(self, current_ms):
        if self.connected and current_ms - self.last_recv > self.timeout_ms:
            self.connected = False
            self.logger("lost connection")

    def _send_when_due(self, current_ms):
        """Say hello until the other side answers, then send a heartbeat every HEARTBEAT_MS."""

        if current_ms - self.last_send < HEARTBEAT_MS:
            return

        if self.connected:
            self._send_heartbeat(current_ms)
        else:
            self._send_hello(current_ms)

    # Call every frame: reads all waiting packets and sends a hello or heartbeat when due.
    def tick(self):
        current_ms = _now_ms()
        self._drain_socket(current_ms)
        self._check_timeout(current_ms)
        self._send_when_due(current_ms)

    def close(self):
        self.sock.close()
