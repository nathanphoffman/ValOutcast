#!/usr/bin/env python3
"""Pretend to be the world side (Valheim) so the player side can be tested alone.

Streams a test terrain around the first position it sees: flat ground inside a ring wall
(3 m tall, 6 m from the start), so the player can walk about 6 m in any direction and no further.
"""
import math
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "protocol"))
import vo_protocol as vo

RING_INNER_M = 6.0
RING_OUTER_M = 7.0
WALL_M = 3.0
RADIUS_CHUNKS = 5


def build_chunk(cx, cz, ax, ay, az):
    """Heights for chunk (cx, cz); the ground is at ay (meters) and the ring wall is centered on (ax, az)."""
    heights = []
    for iz in range(vo.CHUNK_CELLS):
        for ix in range(vo.CHUNK_CELLS):
            wx = (cx * vo.CHUNK_CELLS + ix + 0.5) * vo.CELL_SIZE_M
            wz = (cz * vo.CHUNK_CELLS + iz + 0.5) * vo.CELL_SIZE_M
            dist = math.hypot(wx - ax, wz - az)
            top = ay + (WALL_M if RING_INNER_M <= dist < RING_OUTER_M else 0.0)
            heights.append(int(round(top * vo.HEIGHT_UNITS_PER_M)))
    return heights


def send_field(link, ax, ay, az):
    pcx = math.floor(ax / (vo.CHUNK_CELLS * vo.CELL_SIZE_M))
    pcz = math.floor(az / (vo.CHUNK_CELLS * vo.CELL_SIZE_M))
    for cz in range(pcz - RADIUS_CHUNKS, pcz + RADIUS_CHUNKS + 1):
        for cx in range(pcx - RADIUS_CHUNKS, pcx + RADIUS_CHUNKS + 1):
            link.send_collision_section(cx, cz, build_chunk(cx, cz, ax, ay, az))


link = vo.Link(vo.ROLE_WORLD, log=lambda m: print("[fake_world]", m, flush=True))
print("[fake_world] waiting for player side on port", vo.PORTS[vo.ROLE_WORLD], flush=True)
anchor = None
last_pos = None
last_print = 0
last_field = 0
try:
    while True:
        was_connected = link.connected
        link.tick()
        if link.connected and not was_connected:
            anchor = None
        if link.player_state:
            pos, look = link.player_state
            now = time.time()
            if last_pos is not None and math.dist(pos, last_pos) > 10:  # a teleport on the player side
                anchor = None
                print("[fake_world] player jumped, re-anchoring", flush=True)
            last_pos = pos
            if anchor is None:
                anchor = pos
                link.send_collision_reset()
                send_field(link, *anchor)
                last_field = now
                print("[fake_world] terrain sent around (%.2f, %.2f, %.2f)" % anchor, flush=True)
            elif now - last_field > 5:
                send_field(link, *anchor)
                last_field = now
            if now - last_print > 1:
                last_print = now
                print("[fake_world] player at (%.2f, %.2f, %.2f) looking (%.2f, %.2f, %.2f)" % (pos + look), flush=True)
        time.sleep(0.02)
except KeyboardInterrupt:
    pass
