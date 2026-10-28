"""Packets about the connection and the player, plus decode() for the packet header."""
from vo_constants import *
from vo_constants import HEADER


def encode_hello(sequence, role, process_id):
    return HEADER.pack(MAGIC, VERSION, MSG_HELLO, sequence) + HELLO.pack(role, process_id)


def encode_heartbeat(sequence, uptime_ms):
    return HEADER.pack(MAGIC, VERSION, MSG_HEARTBEAT, sequence) + UINT32.pack(uptime_ms)


def encode_player_state(sequence, position, look):
    return HEADER.pack(MAGIC, VERSION, MSG_PLAYER_STATE, sequence) + PLAYER_STATE.pack(*position, *look)


def decode_player_state(payload):
    """Return (pos, look) as two 3-tuples, or None if the payload is too short."""

    if len(payload) < PLAYER_STATE.size:
        return None

    values = PLAYER_STATE.unpack_from(payload)

    return values[:3], values[3:]


def encode_player_light(sequence, color, intensity):
    return HEADER.pack(MAGIC, VERSION, MSG_PLAYER_LIGHT, sequence) + COLOR_INTENSITY.pack(*color, intensity)


def decode_player_light(payload):
    """Return (rgb, intensity) or None."""

    if len(payload) < COLOR_INTENSITY.size:
        return None

    values = COLOR_INTENSITY.unpack_from(payload)

    return values[0:3], values[3]


def encode_loadout(sequence, weapons):
    return HEADER.pack(MAGIC, VERSION, MSG_LOADOUT, sequence) + UINT32.pack(weapons)


def decode_loadout(payload):
    """Return the weapon bit mask or None."""

    if len(payload) < UINT32.size:
        return None

    return UINT32.unpack_from(payload)[0]


def encode_input(sequence, buttons, forward, right, upward, yaw_delta, pitch, weapon=0):
    return HEADER.pack(MAGIC, VERSION, MSG_INPUT, sequence) + INPUT.pack(buttons, forward, right, upward, yaw_delta, pitch, weapon)


def decode_input(payload):
    """Return (buttons, forward, right, up, yaw, pitch, weapon) or None."""

    if len(payload) < INPUT.size:
        return None

    return INPUT.unpack_from(payload)


def decode(data):
    """Return (type, seq, payload) or None if the packet is not valid."""

    if len(data) < HEADER.size:
        return None

    magic, version, mtype, sequence = HEADER.unpack_from(data)

    if magic != MAGIC or version != VERSION:
        return None

    return mtype, sequence, data[HEADER.size:]
