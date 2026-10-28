/* Test for vo_link.c. Run with tools/test_c.sh (needs python3 for the fake peer). */
#include "vo_link.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

static void logger(const char *message) { printf("[c_test] %s\n", message); fflush(stdout); }

static void check(int ok, const char *what)
{
	if (!ok) { printf("FAIL: %s\n", what); exit(1); }
}

/* Reads one packet by name from protocol/golden.txt (run from the project root). */
static int golden(const char *name, uint8_t *packet, int capacity)
{
	char line[512], name_key[64], hex_text[400];
	FILE *file = fopen("protocol/golden.txt", "r");
	int length = -1;

	if (!file) return -1;

	while (fgets(line, sizeof(line), file)) {
		if (line[0] == '#') continue;

		if (sscanf(line, "%63s %399s", name_key, hex_text) == 2 && strcmp(name_key, name) == 0) {
			int i, hex_length = (int)strlen(hex_text) / 2;

			for (i = 0; i < hex_length && i < capacity; i++) {
				unsigned value;
				sscanf(hex_text + 2 * i, "%2x", &value);
				packet[i] = (uint8_t)value;
			}

			length = hex_length;

			break;
		}
	}

	fclose(file);

	return length;
}

#define GOLDEN_MAX 64
#define HEADER_BYTES 8

static const float forward_direction[3] = {0, 0, -1};

/* Encoded `got` (n bytes) must equal the golden packet called `name`; returns that packet's length. */
static int check_golden(const char *name, const uint8_t *encoded, int length, uint8_t *want)
{
	int want_len = golden(name, want, GOLDEN_MAX);
	check(want_len > 0 && length == want_len && memcmp(encoded, want, length) == 0, name);

	return want_len;
}

/* Hello and heartbeat are the first packets any new world must get right, so check exact bytes. */
static void test_hello_packets(void)
{
	static const uint8_t hello[16] = {0x56,0x4F,1,1, 1,0,0,0, 1,0,0,0, 0xD2,0x04,0,0};
	static const uint8_t beat[12] = {0x56,0x4F,1,2, 2,0,0,0, 0x88,0x13,0,0};
	uint8_t packet[32];
	uint32_t sequence = 0;

	check(vo_encode_hello(packet, 1, VO_ROLE_PLAYER, 1234) == 16 && memcmp(packet, hello, 16) == 0, "hello bytes");
	check(vo_encode_heartbeat(packet, 2, 5000) == 12 && memcmp(packet, beat, 12) == 0, "heartbeat bytes");
	check(vo_decode(hello, 16, &sequence) == VO_MSG_HELLO && sequence == 1, "decode hello");
	check(vo_decode(hello, 4, NULL) == -1, "decode short");
}

/* Player position and look direction round-trip through the wire format. */
static void test_player_state(void)
{
	static const uint8_t state[32] = {0x56,0x4F,1,3, 3,0,0,0, 0,0,0x80,0x3F, 0,0,0,0x40, 0,0,0x40,0x40,
	                                  0,0,0,0, 0,0,0,0, 0,0,0x80,0xBF};
	const float spos[3] = {1, 2, 3}, slook[3] = {0, 0, -1};
	float rpos[3], rlook[3];
	uint8_t packet[32];

	check(vo_encode_player_state(packet, 3, spos, slook) == 32 && memcmp(packet, state, 32) == 0, "player state bytes");
	vo_decode_player_state(state + 8, rpos, rlook);
	check(rpos[0] == 1 && rpos[1] == 2 && rpos[2] == 3 && rlook[2] == -1, "decode player state");
}

/* A terrain chunk encodes to the golden header and decodes back to the same heights. */
static void test_collision_section(void)
{
	static const uint8_t head[16] = {0x56,0x4F,1,4, 4,0,0,0, 0xFF,0xFF,0xFF,0xFF, 2,0,0,0};
	int16_t heights[256], decoded_heights[256];
	uint8_t collision_packet[528];
	int32_t chunk_x, chunk_z;
	int i;

	for (i = 0; i < 256; i++) heights[i] = (int16_t)(i * 100 - 12000);
	heights[0] = VO_NO_HEIGHT;
	check(vo_encode_collision_section(collision_packet, 4, -1, 2, heights) == 528, "collision size");
	check(memcmp(collision_packet, head, 16) == 0, "collision header bytes");
	check(vo_decode_collision_section(collision_packet + 8, 520, &chunk_x, &chunk_z, decoded_heights) == 0 && chunk_x == -1 && chunk_z == 2, "collision decode");
	check(memcmp(heights, decoded_heights, sizeof(heights)) == 0 && decoded_heights[0] == -32768 && decoded_heights[255] == 255 * 100 - 12000, "collision heights");
	check(vo_decode_collision_section(collision_packet + 8, 100, &chunk_x, &chunk_z, decoded_heights) == -1, "collision short");
}

/* Actors are added and removed with the exact golden bytes, and short packets are rejected. */
static void test_actor_lifecycle(void)
{
	uint8_t expected[GOLDEN_MAX], encoded[GOLDEN_MAX];
	vo_actor actor, decoded_actor;
	uint32_t actor_id = 0;
	int length;

	actor.id = 7; actor.pos[0] = 1; actor.pos[1] = 2; actor.pos[2] = 3; actor.radius = 0.5f; actor.height = 2; actor.hp = 200; actor.flags = 1;
	length = check_golden("actor_upsert", encoded, vo_encode_actor_upsert(encoded, 6, &actor), expected);
	check(vo_decode_actor_upsert(expected + HEADER_BYTES, length - HEADER_BYTES, &decoded_actor) == 0 && decoded_actor.id == 7 && decoded_actor.pos[2] == 3 && decoded_actor.hp == 200 && decoded_actor.flags == 1 && decoded_actor.height == 2, "actor_upsert decode");
	check(vo_decode_actor_upsert(expected + HEADER_BYTES, 10, &decoded_actor) == -1, "actor_upsert short");

	length = check_golden("actor_remove", encoded, vo_encode_actor_remove(encoded, 7, 7), expected);
	check(vo_decode_actor_remove(expected + HEADER_BYTES, length - HEADER_BYTES, &actor_id) == 0 && actor_id == 7, "actor_remove decode");
}

/* Push, charm, grip and heal reuse the hit layout with their own message type. */
static void test_hit_actor_variants(void)
{
	uint8_t expected[GOLDEN_MAX], encoded[GOLDEN_MAX];

	check_golden("hit_actor", encoded, vo_encode_hit_actor(encoded, 8, 7, 25.0f, forward_direction), expected);
	check_golden("actor_push", encoded, vo_encode_actor_push(encoded, 15, 7, 0.5f, forward_direction), expected);
	check_golden("actor_charm", encoded, vo_encode_actor_charm(encoded, 16, 7, 1.0f, forward_direction), expected);
	check_golden("actor_grip", encoded, vo_encode_actor_grip(encoded, 17, 7, 1.0f, forward_direction), expected);
	check_golden("actor_heal", encoded, vo_encode_actor_heal(encoded, 18, 7, 1.0f, forward_direction), expected);
}

static void test_hit_world(void)
{
	uint8_t expected[GOLDEN_MAX], encoded[GOLDEN_MAX];
	const float wpos[3] = {1, 2, 3};

	check_golden("hit_world", encoded, vo_encode_hit_world(encoded, 12, wpos, forward_direction, 25.0f, 1), expected);
}

/* The hurt message carries damage, direction and source. */
static void test_player_hurt(void)
{
	uint8_t expected[GOLDEN_MAX], encoded[GOLDEN_MAX];
	vo_hurt hurt, decoded_hurt;
	int length;

	hurt.damage = 10; hurt.dir[0] = 1; hurt.dir[1] = 0; hurt.dir[2] = 0; hurt.source = 42;
	length = check_golden("player_hurt", encoded, vo_encode_player_hurt(encoded, 9, &hurt), expected);
	check(vo_decode_player_hurt(expected + HEADER_BYTES, length - HEADER_BYTES, &decoded_hurt) == 0 && decoded_hurt.damage == 10 && decoded_hurt.dir[0] == 1 && decoded_hurt.source == 42, "player_hurt decode");
}

/* Input keeps its buttons, axes and weapon, and rejects packets cut short. */
static void test_input(void)
{
	uint8_t expected[GOLDEN_MAX], encoded[GOLDEN_MAX];
	vo_input input, decoded_input;
	int length;

	input.buttons = 3; input.forward = 1; input.right = -1; input.up = 0; input.yaw = 0.5f; input.pitch = -0.5f; input.weapon = 4;
	length = check_golden("input", encoded, vo_encode_input(encoded, 10, &input), expected);
	check(vo_decode_input(expected + HEADER_BYTES, length - HEADER_BYTES, &decoded_input) == 0 && decoded_input.buttons == 3 && decoded_input.right == -1 && decoded_input.pitch == -0.5f && decoded_input.weapon == 4, "input decode");
	check(vo_decode_input(expected + HEADER_BYTES, 4, &decoded_input) == -1, "input short");
	check(vo_decode_input(expected + HEADER_BYTES, 24, &decoded_input) == -1, "input without weapon is short");
}

/* The weapon bitmask round-trips and short packets are rejected. */
static void test_loadout(void)
{
	uint8_t expected[GOLDEN_MAX], encoded[GOLDEN_MAX];
	uint32_t mask = 0;
	int length;

	length = check_golden("loadout", encoded, vo_encode_loadout(encoded, 14, 0x2004), expected);
	check(vo_decode_loadout(expected + HEADER_BYTES, length - HEADER_BYTES, &mask) == 0 && mask == 0x2004, "loadout decode");
	check(vo_decode_loadout(expected + HEADER_BYTES, 2, &mask) == -1, "loadout short");
}

static void test_player_light(void)
{
	uint8_t expected[GOLDEN_MAX], encoded[GOLDEN_MAX];
	const float lrgb[3] = { 0.25f, 0.5f, 1.0f };

	check_golden("player_light", encoded, vo_encode_player_light(encoded, 13, lrgb, 0.75f), expected);
}

/* Sun and ambient light round-trip, and short packets are rejected. */
static void test_world_context(void)
{
	uint8_t expected[GOLDEN_MAX], encoded[GOLDEN_MAX];
	vo_world world, decoded_world;
	int length;

	world.sun_dir[0] = 0.0f; world.sun_dir[1] = 0.8f; world.sun_dir[2] = 0.6f;
	world.sun_rgb[0] = 1.0f; world.sun_rgb[1] = 0.9f; world.sun_rgb[2] = 0.5f;
	world.ambient[0] = 0.25f; world.ambient[1] = 0.5f; world.ambient[2] = 0.75f;
	length = check_golden("world_context", encoded, vo_encode_world_context(encoded, 11, &world), expected);
	check(vo_decode_world_context(expected + HEADER_BYTES, length - HEADER_BYTES, &decoded_world) == 0 && decoded_world.sun_dir[1] == 0.8f && decoded_world.sun_rgb[2] == 0.5f && decoded_world.ambient[2] == 0.75f, "world_context decode");
	check(vo_decode_world_context(expected + HEADER_BYTES, 10, &decoded_world) == -1, "world_context short");
}

/* Wait up to 6 s for the fake world side, then wait for it to go away. */
static void test_peer_connection(void)
{
	int connected_seen = 0, lost_seen = 0;
	vo_link link;
	int attempt;

	check(vo_link_open(&link, VO_ROLE_PLAYER, logger) == 0, "open");

	for (attempt = 0; attempt < 600 && !lost_seen; attempt++) {
		vo_link_tick(&link);

		if (link.connected) connected_seen = 1;

		if (connected_seen && !link.connected) lost_seen = 1;
		usleep(10000);
	}

	check(connected_seen, "never connected");
	check(lost_seen, "never noticed lost peer");
}

int main(int argc, char **argv)
{
	test_hello_packets();
	test_player_state();
	test_collision_section();
	test_actor_lifecycle();
	test_hit_actor_variants();
	test_hit_world();
	test_player_hurt();
	test_input();
	test_loadout();
	test_player_light();
	test_world_context();

	if (argc > 1 && strcmp(argv[1], "player") == 0) test_peer_connection();

	printf("c protocol tests passed\n");

	return 0;
}
