#!/usr/bin/env python3
"""Pretend to be the world side (Valheim) so the player side can be tested alone."""
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "protocol"))
import vo_protocol as vo

link = vo.Link(vo.ROLE_WORLD, log=lambda m: print("[fake_world]", m, flush=True))
print("[fake_world] waiting for player side on port", vo.PORTS[vo.ROLE_WORLD], flush=True)
try:
    while True:
        link.tick()
        time.sleep(0.02)
except KeyboardInterrupt:
    pass
