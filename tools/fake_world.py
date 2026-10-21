#!/usr/bin/env python3
"""Pretend to be the world side (Valheim) so the player side can be tested alone."""
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "protocol"))
import vo_protocol as vo

link = vo.Link(vo.ROLE_WORLD, log=lambda m: print("[fake_world]", m, flush=True))
print("[fake_world] waiting for player side on port", vo.PORTS[vo.ROLE_WORLD], flush=True)
last_print = 0
try:
    while True:
        link.tick()
        if link.player_state and time.time() - last_print > 1:
            last_print = time.time()
            pos, look = link.player_state
            print("[fake_world] player at (%.2f, %.2f, %.2f) looking (%.2f, %.2f, %.2f)" % (pos + look), flush=True)
        time.sleep(0.02)
except KeyboardInterrupt:
    pass
