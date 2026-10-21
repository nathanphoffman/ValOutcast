/* Test for vo_link.c. Run with tools/test_c.sh (needs python3 for the fake peer). */
#include "vo_link.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

static void logger(const char *m) { printf("[c_test] %s\n", m); fflush(stdout); }

static void check(int ok, const char *what)
{
	if (!ok) { printf("FAIL: %s\n", what); exit(1); }
}

/* Reads one packet by name from protocol/golden.txt (run from the project root). */
static int golden(const char *name, uint8_t *out, int max)
{
	char line[512], key[64], hex[400];
	FILE *f = fopen("protocol/golden.txt", "r");
	int n = -1;
	if (!f) return -1;
	while (fgets(line, sizeof(line), f)) {
		if (line[0] == '#') continue;
		if (sscanf(line, "%63s %399s", key, hex) == 2 && strcmp(key, name) == 0) {
			int i, len = (int)strlen(hex) / 2;
			for (i = 0; i < len && i < max; i++) {
				unsigned v;
				sscanf(hex + 2 * i, "%2x", &v);
				out[i] = (uint8_t)v;
			}
			n = len;
			break;
		}
	}
	fclose(f);
	return n;
}

int main(int argc, char **argv)
{
	static const uint8_t hello[16] = {0x56,0x4F,1,1, 1,0,0,0, 1,0,0,0, 0xD2,0x04,0,0};
	static const uint8_t beat[12] = {0x56,0x4F,1,2, 2,0,0,0, 0x88,0x13,0,0};
	static const uint8_t state[32] = {0x56,0x4F,1,3, 3,0,0,0, 0,0,0x80,0x3F, 0,0,0,0x40, 0,0,0x40,0x40,
	                                  0,0,0,0, 0,0,0,0, 0,0,0x80,0xBF};
	const float spos[3] = {1, 2, 3}, slook[3] = {0, 0, -1};
	float rpos[3], rlook[3];
	uint8_t out[32];
	uint32_t seq = 0;
	vo_link link;
	int i;

	check(vo_encode_hello(out, 1, VO_ROLE_PLAYER, 1234) == 16 && memcmp(out, hello, 16) == 0, "hello bytes");
	check(vo_encode_heartbeat(out, 2, 5000) == 12 && memcmp(out, beat, 12) == 0, "heartbeat bytes");
	check(vo_encode_player_state(out, 3, spos, slook) == 32 && memcmp(out, state, 32) == 0, "player state bytes");
	vo_decode_player_state(state + 8, rpos, rlook);
	check(rpos[0] == 1 && rpos[1] == 2 && rpos[2] == 3 && rlook[2] == -1, "decode player state");
	{
		int16_t h[256], h2[256];
		uint8_t big[528];
		int32_t cx, cz;
		static const uint8_t head[16] = {0x56,0x4F,1,4, 4,0,0,0, 0xFF,0xFF,0xFF,0xFF, 2,0,0,0};
		for (i = 0; i < 256; i++) h[i] = (int16_t)(i * 100 - 12000);
		h[0] = VO_NO_HEIGHT;
		check(vo_encode_collision_section(big, 4, -1, 2, h) == 528, "collision size");
		check(memcmp(big, head, 16) == 0, "collision header bytes");
		check(vo_decode_collision_section(big + 8, 520, &cx, &cz, h2) == 0 && cx == -1 && cz == 2, "collision decode");
		check(memcmp(h, h2, sizeof(h)) == 0 && h2[0] == -32768 && h2[255] == 255 * 100 - 12000, "collision heights");
		check(vo_decode_collision_section(big + 8, 100, &cx, &cz, h2) == -1, "collision short");
	}
	{
		uint8_t want[64], got[64];
		int n;
		vo_actor a, a2;
		vo_hurt h, h2;
		vo_input in, in2;
		uint32_t id = 0;
		const float dir[3] = {0, 0, -1};

		a.id = 7; a.pos[0] = 1; a.pos[1] = 2; a.pos[2] = 3; a.radius = 0.5f; a.height = 2; a.hp = 200; a.flags = 1;
		n = golden("actor_upsert", want, 64);
		check(n > 0 && vo_encode_actor_upsert(got, 6, &a) == n && memcmp(got, want, n) == 0, "actor_upsert bytes");
		check(vo_decode_actor_upsert(want + 8, n - 8, &a2) == 0 && a2.id == 7 && a2.pos[2] == 3 && a2.hp == 200 && a2.flags == 1 && a2.height == 2, "actor_upsert decode");
		check(vo_decode_actor_upsert(want + 8, 10, &a2) == -1, "actor_upsert short");

		n = golden("actor_remove", want, 64);
		check(n > 0 && vo_encode_actor_remove(got, 7, 7) == n && memcmp(got, want, n) == 0, "actor_remove bytes");
		check(vo_decode_actor_remove(want + 8, n - 8, &id) == 0 && id == 7, "actor_remove decode");

		n = golden("hit_actor", want, 64);
		check(n > 0 && vo_encode_hit_actor(got, 8, 7, 25.0f, dir) == n && memcmp(got, want, n) == 0, "hit_actor bytes");

		h.damage = 10; h.dir[0] = 1; h.dir[1] = 0; h.dir[2] = 0; h.source = 42;
		n = golden("player_hurt", want, 64);
		check(n > 0 && vo_encode_player_hurt(got, 9, &h) == n && memcmp(got, want, n) == 0, "player_hurt bytes");
		check(vo_decode_player_hurt(want + 8, n - 8, &h2) == 0 && h2.damage == 10 && h2.dir[0] == 1 && h2.source == 42, "player_hurt decode");

		in.buttons = 3; in.forward = 1; in.right = -1; in.up = 0; in.yaw = 0.5f; in.pitch = -0.5f;
		n = golden("input", want, 64);
		check(n > 0 && vo_encode_input(got, 10, &in) == n && memcmp(got, want, n) == 0, "input bytes");
		check(vo_decode_input(want + 8, n - 8, &in2) == 0 && in2.buttons == 3 && in2.right == -1 && in2.pitch == -0.5f, "input decode");
		check(vo_decode_input(want + 8, 4, &in2) == -1, "input short");
	}
	check(vo_decode(hello, 16, &seq) == VO_MSG_HELLO && seq == 1, "decode hello");
	check(vo_decode(hello, 4, NULL) == -1, "decode short");

	if (argc > 1 && strcmp(argv[1], "player") == 0) {
		/* Wait up to 6 s for the fake world side, then wait for it to go away. */
		int connected_seen = 0, lost_seen = 0;
		check(vo_link_open(&link, VO_ROLE_PLAYER, logger) == 0, "open");
		for (i = 0; i < 600 && !lost_seen; i++) {
			vo_link_tick(&link);
			if (link.connected) connected_seen = 1;
			if (connected_seen && !link.connected) lost_seen = 1;
			usleep(10000);
		}
		check(connected_seen, "never connected");
		check(lost_seen, "never noticed lost peer");
	}
	printf("c protocol tests passed\n");
	return 0;
}
