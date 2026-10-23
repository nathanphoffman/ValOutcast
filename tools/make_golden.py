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
    "actor_push": vo.encode_actor_push(15, 7, 0.5, (0.0, 0.0, -1.0)),
    "actor_charm": vo.encode_actor_charm(16, 7, 1.0, (0.0, 0.0, -1.0)),
    "hit_world": vo.encode_hit_world(12, (1.0, 2.0, 3.0), (0.0, 0.0, -1.0), 25.0, 1),
    "player_hurt": vo.encode_player_hurt(9, 10.0, (1.0, 0.0, 0.0), 42),
    "input": vo.encode_input(10, 3, 1.0, -1.0, 0.0, 0.5, -0.5, 4),
    "player_light": vo.encode_player_light(13, (0.25, 0.5, 1.0), 0.75),
    "loadout": vo.encode_loadout(14, 0x2004),
    "world_context": vo.encode_world_context(11, (0.0, 0.8, 0.6), (1.0, 0.9, 0.5), (0.25, 0.5, 0.75)),
}
with open(os.path.join(root, "protocol", "golden.txt"), "w") as f:
    f.write("# name hex. Every implementation (Python, C, C#) must encode these exact bytes. Regenerate with tools/make_golden.py.\n")
    for name, packet in golden.items():
        f.write("%s %s\n" % (name, packet.hex()))
