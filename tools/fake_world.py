#!/usr/bin/env python3
"""Pretend to be the world side (Valheim) so the player side can be tested alone.

Options: --walk   send Input (walk forward, slowly turning) as if the Valheim window were being played
         --noactors  leave out the two wandering creatures (they soak up shots fired near the start)
         --turnrate D  degrees per input message for --turn (default 0.5, 60 messages a second)
         --ring M  radius of the ring wall in meters (default 6; use 1.6 to put the wall within reach of the lightsaber)
         --near   put the two creatures about 1.5 m from the start (within reach of the lightsaber)
         --turn   turn slowly in place (use with --fire to check where shots go as the view turns)
         --fire   hold the primary attack button
         --alt    hold the alternate attack button
         --weapon N  pick weapon slot N (1 is the lightsaber) a few seconds after starting (14 = the cheat: everything)
         --power N  press ability N (1 to 7: push, pull, speed, mind trick, heal, grip, lightning) for 1 s every 4 s, from 6 s on
         --dark   send a dim, bluish world light (night) instead of bright daylight
         --nowall leave out the ring wall (flat ground all around)
         --hurt   send PlayerHurt (10 damage) every 8 seconds

Streams a test terrain around the first position it sees: flat ground inside a ring wall
(3 m tall, 6 m from the start), so the player can walk about 6 m in any direction and no further.
"""
import math
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "protocol"))
import vo_protocol as vo

RING_INNER_M = float(sys.argv[sys.argv.index("--ring") + 1]) if "--ring" in sys.argv else 6.0
RING_OUTER_M = RING_INNER_M + 1.0
WALL_M = 3.0
RADIUS_CHUNKS = 5
WALK = "--walk" in sys.argv
TURN = "--turn" in sys.argv
NOACTORS = "--noactors" in sys.argv
NEAR = "--near" in sys.argv
TURN_RATE = float(sys.argv[sys.argv.index("--turnrate") + 1]) if "--turnrate" in sys.argv else 0.5
NOWALL = "--nowall" in sys.argv
if NOWALL:
    WALL_M = 0.0
HURT = "--hurt" in sys.argv
FIRE = "--fire" in sys.argv
DARK = "--dark" in sys.argv
ALT = "--alt" in sys.argv
POWER = int(sys.argv[sys.argv.index("--power") + 1]) if "--power" in sys.argv else 0
WEAPON = int(sys.argv[sys.argv.index("--weapon") + 1]) if "--weapon" in sys.argv else 0


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


def send_actors(link, ax, ay, az, t):
    """Two creatures wandering in small circles near the start."""
    if NOACTORS:
        return
    for n, (dx, dz) in enumerate(((1.2, -0.9), (-1.2, 1.0)) if NEAR else ((3.0, -2.0), (-3.0, 2.5))):
        a = t * 0.5 + n
        pos = (ax + dx + math.cos(a), ay, az + dz + math.sin(a))
        link.send_raw(lambda seq, n=n, pos=pos: vo.encode_actor_upsert(seq, 100 + n, pos, 0.5, 1.8, 255, vo.ACTOR_HOSTILE))


link = vo.Link(vo.ROLE_WORLD, log=lambda m: print("[fake_world]", m, flush=True))
print("[fake_world] waiting for player side on port", vo.PORTS[vo.ROLE_WORLD], flush=True)
anchor = None
last_pos = None
last_actors = 0
last_hurt = time.time()
last_input = 0
last_light = 0
started_at = None
weapon_sent = False
last_print = 0
last_field = 0
try:
    while True:
        was_connected = link.connected
        link.tick()
        if link.connected and not was_connected:
            anchor = None
        now = time.time()
        if now - last_input > 1 / 60:
            # Input is also how the world side says "I am in a world and playing": send it even when standing still.
            last_input = now
            forward = 1.0 if WALK else 0.0
            yaw = TURN_RATE if TURN else (0.0 if (NOWALL or not WALK) else 0.5)
            buttons = (vo.BUTTON_ATTACK if FIRE else 0) | (vo.BUTTON_ALT_ATTACK if ALT else 0)
            if POWER and started_at is not None and now - started_at > 6 and (now - started_at - 6) % 4 < 1:
                buttons |= vo.button_ability(POWER)
            weapon = 0
            if WEAPON and started_at is not None and now - started_at > 4 and not weapon_sent:
                weapon, weapon_sent = WEAPON, True
            link.send_raw(lambda seq: vo.encode_input(seq, buttons, forward, 0.0, 0.0, yaw, 0.0, weapon))
        if now - last_light > 0.25:
            last_light = now
            if DARK:
                link.send_raw(lambda seq: vo.encode_world_context(seq, (0.0, 0.6, 0.8), (0.1, 0.12, 0.2), (0.08, 0.1, 0.18)))
            else:
                link.send_raw(lambda seq: vo.encode_world_context(seq, (0.4, 0.8, 0.45), (1.1, 1.0, 0.85), (0.5, 0.55, 0.6)))
        if link.player_state:
            pos, look = link.player_state
            now = time.time()
            if last_pos is not None and math.dist(pos, last_pos) > 10:  # a teleport on the player side
                anchor = None
                print("[fake_world] player jumped, re-anchoring", flush=True)
            last_pos = pos
            if anchor is None:
                anchor = pos
                started_at = now
                link.send_collision_reset()
                send_field(link, *anchor)
                last_field = now
                print("[fake_world] terrain sent around (%.2f, %.2f, %.2f)" % anchor, flush=True)
            elif now - last_field > 5:
                send_field(link, *anchor)
                last_field = now
            if now - last_actors > 0.1:
                last_actors = now
                send_actors(link, *anchor, now)
            if HURT and now - last_hurt > 8:
                last_hurt = now
                link.send_raw(lambda seq: vo.encode_player_hurt(seq, 10.0, (0.0, 0.0, -1.0), 0))
                print("[fake_world] sent PlayerHurt", flush=True)
            if now - last_print > 1:
                last_print = now
                print("[fake_world] player at (%.2f, %.2f, %.2f) looking (%.2f, %.2f, %.2f)" % (pos + look), flush=True)
        for kind, value in link.inbox:
            if kind == vo.MSG_HIT_ACTOR:
                print("[fake_world] HitActor id=%d damage=%.0f dir=(%.2f, %.2f, %.2f)" % (value[0], value[1], *value[2]), flush=True)
            if kind == vo.MSG_HIT_WORLD:
                print("[fake_world] HitWorld kind=%d damage=%.0f at (%.2f, %.2f, %.2f)" % (value[3], value[2], *value[0]), flush=True)
        link.inbox.clear()
        time.sleep(0.005)
except KeyboardInterrupt:
    pass
