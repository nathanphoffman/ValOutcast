"""Packets about the world: terrain chunks and lighting."""
from vo_constants import *
from vo_constants import HEADER


def encode_collision_section(sequence, chunk_x, chunk_z, heights):
    assert len(heights) == CHUNK_CELLS * CHUNK_CELLS

    return HEADER.pack(MAGIC, VERSION, MSG_COLLISION_SECTION, sequence) + COLLISION_SECTION.pack(chunk_x, chunk_z, *heights)


def decode_collision_section(payload):
    """Return (cx, cz, heights) or None if the payload is too short."""

    if len(payload) < COLLISION_SECTION.size:
        return None

    values = COLLISION_SECTION.unpack_from(payload)

    return values[0], values[1], list(values[2:])


def encode_collision_reset(sequence):
    return HEADER.pack(MAGIC, VERSION, MSG_COLLISION_RESET, sequence)


def encode_world_context(sequence, sun_dir, sun_rgb, ambient_rgb):
    return HEADER.pack(MAGIC, VERSION, MSG_WORLD_CONTEXT, sequence) + WORLD_CONTEXT.pack(*sun_dir, *sun_rgb, *ambient_rgb)


def decode_world_context(payload):
    """Return (sun_dir, sun_rgb, ambient_rgb) or None."""

    if len(payload) < WORLD_CONTEXT.size:
        return None

    values = WORLD_CONTEXT.unpack_from(payload)

    return values[0:3], values[3:6], values[6:9]
