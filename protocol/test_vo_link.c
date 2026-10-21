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
