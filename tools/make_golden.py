#!/usr/bin/env python3
"""Rewrites protocol/golden.txt from the Python encoders. Only run this when the protocol itself changes on purpose."""
import os
import sys

root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
sys.path.insert(0, os.path.join(root, "protocol"))
import vo_protocol as vo

golden = {
    "actor_upsert": vo.encode_actor_upsert(6, 7, (1.0, 2.0, 3.0), 0.5, 2.0, 200, 1),
    "actor_remove": vo.encode_actor_remove(7, 7),
    "hit_actor": vo.encode_hit_actor(8, 7, 25.0, (0.0, 0.0, -1.0)),
    "player_hurt": vo.encode_player_hurt(9, 10.0, (1.0, 0.0, 0.0), 42),
    "input": vo.encode_input(10, 3, 1.0, -1.0, 0.0, 0.5, -0.5),
}
with open(os.path.join(root, "protocol", "golden.txt"), "w") as f:
    f.write("# name hex. Every implementation (Python, C, C#) must encode these exact bytes. Regenerate with tools/make_golden.py.\n")
    for name, packet in golden.items():
        f.write("%s %s\n" % (name, packet.hex()))
