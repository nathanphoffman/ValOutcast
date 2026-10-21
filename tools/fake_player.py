#!/usr/bin/env python3
"""Pretend to be the player side (Outcast): walks in a 3 m circle so the world side can be tested alone."""
import math
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "protocol"))
import vo_protocol as vo

link = vo.Link(vo.ROLE_PLAYER, log=lambda m: print("[fake_player]", m, flush=True))
print("[fake_player] waiting for world side on port", vo.PORTS[vo.ROLE_PLAYER], flush=True)
start = time.time()
last_send = 0
try:
    while True:
        link.tick()
        now = time.time()
        if now - last_send >= 0.03:  # ~30 updates per second
            last_send = now
            a = (now - start) * 0.6  # one lap about every 10 s
            pos = (3 * math.cos(a), 0.0, 3 * math.sin(a))
            look = (-math.sin(a), 0.0, math.cos(a))  # walking direction
            link.send_player_state(pos, look)
        time.sleep(0.005)
except KeyboardInterrupt:
    pass
