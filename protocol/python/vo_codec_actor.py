"""Packets about actors: spawn, remove, hits and force powers."""
from vo_constants import *
from vo_constants import HEADER


def encode_actor_upsert(sequence, actor_id, position, radius, height, hit_points, flags):
    return HEADER.pack(MAGIC, VERSION, MSG_ACTOR_UPSERT, sequence) + ACTOR_UPSERT.pack(actor_id, *position, radius, height, hit_points, flags, 0)


def decode_actor_upsert(payload):
    """Return (id, pos, radius, height, hp, flags) or None."""

    if len(payload) < ACTOR_UPSERT.size:
        return None

    values = ACTOR_UPSERT.unpack_from(payload)

    return values[0], values[1:4], values[4], values[5], values[6], values[7]


def encode_actor_remove(sequence, actor_id):
    return HEADER.pack(MAGIC, VERSION, MSG_ACTOR_REMOVE, sequence) + UINT32.pack(actor_id)


def decode_actor_remove(payload):
    return UINT32.unpack_from(payload)[0] if len(payload) >= UINT32.size else None


def encode_hit_actor(sequence, actor_id, damage, direction):
    return HEADER.pack(MAGIC, VERSION, MSG_HIT_ACTOR, sequence) + ACTOR_AMOUNT.pack(actor_id, damage, *direction)


def decode_hit_actor(payload):
    """Return (id, damage, direction) or None."""

    if len(payload) < ACTOR_AMOUNT.size:
        return None

    values = ACTOR_AMOUNT.unpack_from(payload)

    return values[0], values[1], values[2:5]


def encode_actor_push(sequence, actor_id, strength, direction):
    return HEADER.pack(MAGIC, VERSION, MSG_ACTOR_PUSH, sequence) + ACTOR_AMOUNT.pack(actor_id, strength, *direction)


def decode_actor_push(payload):
    """Return (id, strength, direction) or None."""

    return decode_hit_actor(payload)


def encode_actor_charm(sequence, actor_id, strength, direction):
    return HEADER.pack(MAGIC, VERSION, MSG_ACTOR_CHARM, sequence) + ACTOR_AMOUNT.pack(actor_id, strength, *direction)


def decode_actor_charm(payload):
    """Return (id, strength, direction) or None."""

    return decode_hit_actor(payload)


def encode_actor_grip(sequence, actor_id, strength, direction):
    return HEADER.pack(MAGIC, VERSION, MSG_ACTOR_GRIP, sequence) + ACTOR_AMOUNT.pack(actor_id, strength, *direction)


def decode_actor_grip(payload):
    """Return (id, strength, direction) or None."""

    return decode_hit_actor(payload)


def encode_actor_heal(sequence, actor_id, strength, direction):
    return HEADER.pack(MAGIC, VERSION, MSG_ACTOR_HEAL, sequence) + ACTOR_AMOUNT.pack(actor_id, strength, *direction)


def decode_actor_heal(payload):
    """Return (id, strength, direction) or None."""

    return decode_hit_actor(payload)


def encode_hit_world(sequence, position, direction, damage, kind):
    return HEADER.pack(MAGIC, VERSION, MSG_HIT_WORLD, sequence) + HIT_WORLD_WRITE.pack(*position, *direction, damage, kind)


def decode_hit_world(payload):
    """Return (position, direction, damage, kind) or None."""

    if len(payload) < HIT_WORLD_WRITE.size:
        return None

    values = HIT_WORLD_READ.unpack_from(payload)

    return values[0:3], values[3:6], values[6], values[7]


def encode_player_hurt(sequence, damage, direction, source):
    return HEADER.pack(MAGIC, VERSION, MSG_PLAYER_HURT, sequence) + PLAYER_HURT.pack(damage, *direction, source)


def decode_player_hurt(payload):
    """Return (damage, direction, source) or None."""

    if len(payload) < ACTOR_AMOUNT.size:
        return None

    values = PLAYER_HURT.unpack_from(payload)

    return values[0], values[1:4], values[4]
