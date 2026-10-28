"""ValOutcast protocol v1: message numbers, sizes and timing. See PROTOCOL.md."""
import struct

MAGIC = 0x4F56
VERSION = 1
MSG_HELLO = 1
MSG_HEARTBEAT = 2
MSG_PLAYER_STATE = 3
MSG_COLLISION_SECTION = 4
MSG_COLLISION_RESET = 5
MSG_ACTOR_UPSERT = 6
MSG_ACTOR_REMOVE = 7
MSG_HIT_ACTOR = 8
MSG_PLAYER_HURT = 9
MSG_INPUT = 10
MSG_WORLD_CONTEXT = 11
MSG_HIT_WORLD = 12
MSG_PLAYER_LIGHT = 13
MSG_LOADOUT = 14
MSG_ACTOR_PUSH = 15
MSG_ACTOR_CHARM = 16
MSG_ACTOR_GRIP = 17
MSG_ACTOR_HEAL = 18
BUTTON_ATTACK = 1
BUTTON_ALT_ATTACK = 2
BUTTON_USE = 4
ABILITY_COUNT = 7


def button_ability(n):
    """Button bit of ability n (1 to 7): bits 3 to 9."""

    # Ability n is bit n+2 (abilities 1..7 are bits 3..9), so shift 8 left by n-1.
    return 8 << (n - 1)


ACTOR_HOSTILE = 1
ACTOR_LOOSE = 2
CHUNK_CELLS = 16
CELL_SIZE_M = 0.5
HEIGHT_UNITS_PER_M = 32
NO_HEIGHT = -32768
PLAYER_STATE_TIMEOUT_MS = 500
ROLE_WORLD = 0
ROLE_PLAYER = 1
PORTS = {ROLE_WORLD: 47100, ROLE_PLAYER: 47101}
HEARTBEAT_MS = 1000
TIMEOUT_MS = 3000

HEADER = struct.Struct("<HBBI")

# Payload layouts after the header (little-endian: I = u32, i = i32, f = float, B = byte, H = u16, h = i16, x = padding byte).
HELLO = struct.Struct("<B3xI")
UINT32 = struct.Struct("<I")
PLAYER_STATE = struct.Struct("<6f")
COLLISION_SECTION = struct.Struct("<ii256h")
ACTOR_UPSERT = struct.Struct("<I3f2fBBH")
ACTOR_AMOUNT = struct.Struct("<I4f")
HIT_WORLD_WRITE = struct.Struct("<7fB3x")
HIT_WORLD_READ = struct.Struct("<7fB")
PLAYER_HURT = struct.Struct("<4fI")
INPUT = struct.Struct("<I5fi")
WORLD_CONTEXT = struct.Struct("<9f")
COLOR_INTENSITY = struct.Struct("<4f")
