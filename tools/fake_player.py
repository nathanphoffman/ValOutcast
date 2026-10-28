#!/usr/bin/env python3
"""Pretend to be the player side (Outcast): walks in a 3 m circle so the world side can be tested alone."""
import math
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "protocol", "python"))
import vo_protocol as vo

link = vo.Link(vo.ROLE_PLAYER, logger=lambda message: print("[fake_player]", message, flush=True))
print("[fake_player] waiting for world side on port", vo.PORTS[vo.ROLE_PLAYER], flush=True)
start = time.time()
last_send = 0

try:
    while True:
        link.tick()
        current_ms = time.time()

        if current_ms - last_send >= 0.03:  # ~30 updates per second
            last_send = current_ms
            angle = (current_ms - start) * 0.6  # one lap about every 10 s
            position = (3 * math.cos(angle), 0.0, 3 * math.sin(angle))
            look = (-math.sin(angle), 0.0, math.cos(angle))  # walking direction
            link.send_player_state(position, look)

        time.sleep(0.005)
except KeyboardInterrupt:
    # Ctrl+C is the normal way to stop this tool.
    print("[fake_player] stopped", flush=True)
