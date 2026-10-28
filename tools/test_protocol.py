#!/usr/bin/env python3
"""Checks the Python codec against the golden packets and the connect/lose rules."""
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "protocol", "python"))
import vo_protocol as vo

HELLO = bytes.fromhex("564F010101000000" "01000000D2040000")
HEARTBEAT = bytes.fromhex("564F010202000000" "88130000")
STATE = bytes.fromhex("564F010303000000" "0000803F" "00000040" "00004040" "00000000" "00000000" "000080BF")
HEADER_SIZE = 8


def load_golden():
    """golden.txt holds the shared test packets for the newer messages."""
    golden = {}

    with open(os.path.join(HERE, "..", "protocol", "golden.txt")) as golden_file:
        for line in golden_file:
            if line.strip() and not line.startswith("#"):
                name, hexed = line.split()
                golden[name] = bytes.fromhex(hexed)

    return golden


GOLDEN = load_golden()


def payload(name):
    return GOLDEN[name][HEADER_SIZE:]


def test_hello_packets():
    assert vo.encode_hello(1, vo.ROLE_PLAYER, 1234) == HELLO
    assert vo.encode_heartbeat(2, 5000) == HEARTBEAT


def test_player_state():
    assert vo.encode_player_state(3, (1, 2, 3), (0, 0, -1)) == STATE
    assert vo.decode_player_state(STATE[HEADER_SIZE:]) == ((1.0, 2.0, 3.0), (0.0, 0.0, -1.0))


def test_collision_section():
    heights = [cell * 100 - 12000 for cell in range(256)]
    heights[0] = vo.NO_HEIGHT
    section = vo.encode_collision_section(4, -1, 2, heights)
    assert len(section) == 528 and section[:16] == bytes.fromhex("564F0104" "04000000" "FFFFFFFF" "02000000")
    assert vo.decode_collision_section(section[HEADER_SIZE:]) == (-1, 2, heights)


def test_collision_section_too_short():
    assert vo.decode_collision_section(b"\x00" * 92) is None


def test_collision_reset():
    assert vo.encode_collision_reset(5) == bytes.fromhex("564F0105" "05000000")


def test_decode_header():
    assert vo.decode(HELLO)[:2] == (vo.MSG_HELLO, 1)


def test_decode_rejects_bad_packets():
    assert vo.decode(b"\x00\x01") is None
    assert vo.decode(b"XX" + HELLO[2:]) is None
    assert vo.decode(HELLO[:2] + b"\x09" + HELLO[3:]) is None


def test_actor_upsert():
    assert vo.encode_actor_upsert(6, 7, (1, 2, 3), 0.5, 2.0, 200, 1) == GOLDEN["actor_upsert"]
    assert vo.decode_actor_upsert(payload("actor_upsert")) == (7, (1.0, 2.0, 3.0), 0.5, 2.0, 200, 1)


def test_actor_remove():
    assert vo.encode_actor_remove(7, 7) == GOLDEN["actor_remove"]
    assert vo.decode_actor_remove(payload("actor_remove")) == 7


def test_hit_actor():
    assert vo.encode_hit_actor(8, 7, 25.0, (0, 0, -1)) == GOLDEN["hit_actor"]
    assert vo.decode_hit_actor(payload("hit_actor")) == (7, 25.0, (0.0, 0.0, -1.0))
    assert vo.decode_hit_actor(b"\x00") is None


def test_force_powers():
    cases = [("actor_push", vo.encode_actor_push, vo.decode_actor_push, 15, 0.5),
             ("actor_charm", vo.encode_actor_charm, vo.decode_actor_charm, 16, 1.0),
             ("actor_grip", vo.encode_actor_grip, vo.decode_actor_grip, 17, 1.0),
             ("actor_heal", vo.encode_actor_heal, vo.decode_actor_heal, 18, 1.0)]

    for name, encode, decode, sequence, strength in cases:
        assert encode(sequence, 7, strength, (0, 0, -1)) == GOLDEN[name]
        assert decode(payload(name)) == (7, strength, (0.0, 0.0, -1.0))


def test_player_hurt():
    assert vo.encode_player_hurt(9, 10.0, (1, 0, 0), 42) == GOLDEN["player_hurt"]
    assert vo.decode_player_hurt(payload("player_hurt")) == (10.0, (1.0, 0.0, 0.0), 42)


def test_input():
    assert vo.encode_input(10, 3, 1.0, -1.0, 0.0, 0.5, -0.5, 4) == GOLDEN["input"]
    assert vo.decode_input(payload("input")) == (3, 1.0, -1.0, 0.0, 0.5, -0.5, 4)
    assert vo.decode_input(b"") is None


def test_world_context():
    light = (0.0, 0.8, 0.6), (1.0, 0.9, 0.5), (0.25, 0.5, 0.75)
    assert vo.encode_world_context(11, *light) == GOLDEN["world_context"]
    assert vo.decode_world_context(payload("world_context")) == (
        (0.0, 0.800000011920929, 0.6000000238418579), (1.0, 0.8999999761581421, 0.5), (0.25, 0.5, 0.75))

    assert vo.decode_world_context(b"\x00" * 10) is None


def test_hit_world():
    assert vo.encode_hit_world(12, (1, 2, 3), (0, 0, -1), 25.0, 1) == GOLDEN["hit_world"]
    assert vo.decode_hit_world(payload("hit_world")) == ((1.0, 2.0, 3.0), (0.0, 0.0, -1.0), 25.0, 1)
    assert vo.decode_hit_world(b"\x00") is None


def test_player_light():
    assert vo.encode_player_light(13, (0.25, 0.5, 1.0), 0.75) == GOLDEN["player_light"]
    assert vo.decode_player_light(payload("player_light")) == ((0.25, 0.5, 1.0), 0.75)
    assert vo.decode_player_light(b"\x00" * 10) is None


def test_loadout():
    assert vo.encode_loadout(14, 0x2004) == GOLDEN["loadout"]
    assert vo.decode_loadout(payload("loadout")) == 0x2004
    assert vo.decode_loadout(b"\x00") is None


def test_peer_connection():
    """Two real sockets must connect, and the world side must notice when the player side vanishes."""

    logs = []
    world = vo.Link(vo.ROLE_WORLD, logger=logs.append)
    player = vo.Link(vo.ROLE_PLAYER, logger=logs.append)
    deadline = time.time() + 3

    while not (world.connected and player.connected) and time.time() < deadline:
        world.tick(); player.tick(); time.sleep(0.01)

    assert world.connected and player.connected, "did not connect"

    player.close()
    world.timeout_ms = 500
    deadline = time.time() + 3

    while world.connected and time.time() < deadline:
        world.tick(); time.sleep(0.05)

    assert not world.connected, "did not notice lost peer"
    assert "lost connection" in logs
    world.close()


if __name__ == "__main__":
    for name, test in list(globals().items()):
        if name.startswith("test_"):
            test()

    print("python protocol tests passed")
