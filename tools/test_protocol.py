#!/usr/bin/env python3
"""Checks the Python codec against the golden packets and the connect/lose rules."""
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "protocol"))
import vo_protocol as vo

HELLO = bytes.fromhex("564F010101000000" "01000000D2040000")
HEARTBEAT = bytes.fromhex("564F010202000000" "88130000")

assert vo.encode_hello(1, vo.ROLE_PLAYER, 1234) == HELLO
assert vo.encode_heartbeat(2, 5000) == HEARTBEAT
STATE = bytes.fromhex("564F010303000000" "0000803F" "00000040" "00004040" "00000000" "00000000" "000080BF")
assert vo.encode_player_state(3, (1, 2, 3), (0, 0, -1)) == STATE
assert vo.decode_player_state(STATE[8:]) == ((1.0, 2.0, 3.0), (0.0, 0.0, -1.0))
heights = [i * 100 - 12000 for i in range(256)]
heights[0] = vo.NO_HEIGHT
sec = vo.encode_collision_section(4, -1, 2, heights)
assert len(sec) == 528 and sec[:16] == bytes.fromhex("564F0104" "04000000" "FFFFFFFF" "02000000")
assert vo.decode_collision_section(sec[8:]) == (-1, 2, heights)
assert vo.decode_collision_section(sec[8:100]) is None
assert vo.encode_collision_reset(5) == bytes.fromhex("564F0105" "05000000")
assert vo.decode(HELLO)[:2] == (vo.MSG_HELLO, 1)
assert vo.decode(b"\x00\x01") is None
assert vo.decode(b"XX" + HELLO[2:]) is None
assert vo.decode(HELLO[:2] + b"\x09" + HELLO[3:]) is None

# golden.txt: shared test packets for the newer messages
golden = {}
with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "protocol", "golden.txt")) as f:
    for line in f:
        if line.strip() and not line.startswith("#"):
            name, hexed = line.split()
            golden[name] = bytes.fromhex(hexed)
assert vo.encode_actor_upsert(6, 7, (1, 2, 3), 0.5, 2.0, 200, 1) == golden["actor_upsert"]
assert vo.decode_actor_upsert(golden["actor_upsert"][8:]) == (7, (1.0, 2.0, 3.0), 0.5, 2.0, 200, 1)
assert vo.encode_actor_remove(7, 7) == golden["actor_remove"] and vo.decode_actor_remove(golden["actor_remove"][8:]) == 7
assert vo.encode_hit_actor(8, 7, 25.0, (0, 0, -1)) == golden["hit_actor"]
assert vo.decode_hit_actor(golden["hit_actor"][8:]) == (7, 25.0, (0.0, 0.0, -1.0))
assert vo.encode_player_hurt(9, 10.0, (1, 0, 0), 42) == golden["player_hurt"]
assert vo.decode_player_hurt(golden["player_hurt"][8:]) == (10.0, (1.0, 0.0, 0.0), 42)
assert vo.encode_input(10, 3, 1.0, -1.0, 0.0, 0.5, -0.5) == golden["input"]
assert vo.decode_input(golden["input"][8:]) == (3, 1.0, -1.0, 0.0, 0.5, -0.5)
assert vo.decode_hit_actor(b"\x00") is None and vo.decode_input(b"") is None

logs = []
world = vo.Link(vo.ROLE_WORLD, log=logs.append)
player = vo.Link(vo.ROLE_PLAYER, log=logs.append)
deadline = time.time() + 3
while not (world.connected and player.connected) and time.time() < deadline:
    world.tick(); player.tick(); time.sleep(0.01)
assert world.connected and player.connected, "did not connect"

player.close()
vo.TIMEOUT_MS = 500
deadline = time.time() + 3
while world.connected and time.time() < deadline:
    world.tick(); time.sleep(0.05)
assert not world.connected, "did not notice lost peer"
assert "lost connection" in logs
world.close()
print("python protocol tests passed")
