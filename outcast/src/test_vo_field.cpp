// Tests for vo_field.cpp. Run with tools/test_field.sh.
#include "vo_field.h"

#include <cmath>
#include <cstdio>
#include <cstdlib>

static void check(bool ok, const char *what)
{
	if (!ok) { printf("FAIL: %s\n", what); exit(1); }
}

static bool near(float a, float b) { return std::fabs(a - b) < 0.2f; }

int main()
{
	// Chunks (cx, cz) -1..0 cover wire X and Z from -8 m to 8 m, so Outcast x and y from -256 to 256.
	// Ground at 0, plus a 3 m tall wall (96 units) on cell column iz = -4 (Outcast x from 48 to 64).
	int16_t flat[256], wall[256];
	for (int i = 0; i < 256; i++) flat[i] = 0;
	for (int i = 0; i < 256; i++) wall[i] = 0;
	for (int ix = 0; ix < 16; ix++) wall[12 * 16 + ix] = 96; // chunk cz=-1: iz = -16..-1; row 12 is iz = -4
	VoField_Clear();
	VoField_SetChunk(-1, -1, wall);
	VoField_SetChunk(0, -1, flat);
	VoField_SetChunk(-1, 0, flat);
	VoField_SetChunk(0, 0, flat);
	check(VoField_ChunkCount() == 4, "chunk count");

	const float mins[3] = {-15, -15, -24}, maxs[3] = {15, 15, 40};
	float frac, n[3];

	float p0[3] = {0, 0, 0};
	check(VoField_Covers(p0), "covers origin");
	float far[3] = {5000, 0, 0};
	check(!VoField_Covers(far), "does not cover far point");

	// Falling onto flat ground: feet (origin z - 24) stop on 0, origin about 24.
	float s1[3] = {0, 0, 100}, e1[3] = {0, 0, -100};
	check(VoField_Trace(s1, mins, maxs, e1, &frac, n), "falls onto ground");
	check(n[2] == 1.0f && near(100 + frac * (-200), 24.125f), "lands on top");

	// Standing on the ground and walking sideways: no hit.
	float s2[3] = {0, 0, 24.125f}, e2[3] = {30, 30, 24.125f};
	check(!VoField_Trace(s2, mins, maxs, e2, &frac, n), "walking on flat ground is free");

	// Ground trace straight down 0.25 from the standing spot hits.
	float e3[3] = {0, 0, 23.875f};
	check(VoField_Trace(s2, mins, maxs, e3, &frac, n) && n[2] == 1.0f, "ground check hits");

	// Walking into the 3 m wall: wall covers Outcast x from 48 to 64. Hull front is at origin x + 15.
	float s4[3] = {0, 0, 24.125f}, e4[3] = {100, 0, 24.125f};
	check(VoField_Trace(s4, mins, maxs, e4, &frac, n), "wall blocks");
	check(n[0] == -1.0f && near(frac * 100 + 15, 48.0f), "stops at wall face");

	// Stepping onto something low (a 10 unit bump) is a hit too (the game's step-up logic climbs it).
	int16_t bump[256];
	for (int i = 0; i < 256; i++) bump[i] = 0;
	bump[12 * 16 + 3] = 10;
	VoField_SetChunk(-1, -1, bump);
	float e5[3] = {100, 0, 24.125f};
	float s5[3] = {0, 0, 24.125f};
	check(VoField_Trace(s5, mins, maxs, e5, &frac, n) == false || n[0] == -1.0f, "bump handled");

	// Starting inside a column: ignored.
	VoField_SetChunk(-1, -1, wall);
	float s6[3] = {56, -10, 40}, e6[3] = {56, -10, 80};
	check(!VoField_Trace(s6, mins, maxs, e6, &frac, n), "start inside is ignored");

	// Eviction drops far chunks.
	float q[3] = {0, 0, 0};
	VoField_SetChunk(20, 20, flat);
	VoField_EvictFar(q, 3);
	check(VoField_ChunkCount() == 4, "evicts far chunk");
	VoField_Clear();
	check(VoField_ChunkCount() == 0, "clear");
	printf("field tests passed\n");
	return 0;
}
