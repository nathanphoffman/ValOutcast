#!/usr/bin/env python3
"""Pretend to be the player side (Outcast) so the world side can be tested alone."""
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "protocol"))
import vo_protocol as vo

link = vo.Link(vo.ROLE_PLAYER, log=lambda m: print("[fake_player]", m, flush=True))
print("[fake_player] waiting for world side on port", vo.PORTS[vo.ROLE_PLAYER], flush=True)
try:
    while True:
        link.tick()
        time.sleep(0.02)
except KeyboardInterrupt:
    pass
