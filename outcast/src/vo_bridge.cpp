// ValOutcast bridge, Outcast side. This is the player side of the link.
#include "vo_bridge.h"
#include "vo_field.h"

#include <stdlib.h>

extern "C" {
#include "vo_link.h"
}

// Outcast uses 32 units per meter, X forward, Y left, Z up. The wire is meters, X right, Y up, -Z forward.
static const float UNITS_PER_METER = 32.0f;
static const int KEEP_CHUNKS = 12;  // forget terrain chunks farther than this (in chunks of 8 m)

static void VO_ToWire(const vec3_t q, float out[3], float scale)
{
	out[0] = -q[1] / scale;
	out[1] = q[2] / scale;
	out[2] = -q[0] / scale;
}

extern bool in_camera;            // true during scripted camera cutscenes
extern qboolean player_locked;    // true when a script has taken control of the player

static vo_link s_link;
static qboolean s_active = qfalse;  // the player is free and was standing on the level's floor: sync and terrain are on
static qboolean s_open = qfalse;
static qboolean s_onField = qfalse;
static int s_framesSinceEvict = 0;
static int s_debugFrames = 0;  // set VO_DEBUG=1 in the environment to print the player's position every second or so

static void VO_Log(const char *message)
{
	gi.Printf("ValOutcast: %s\n", message);
}

static void VO_OnMessage(void *user, int type, const uint8_t *payload, int len)
{
	(void)user;
	if (type == VO_MSG_COLLISION_SECTION) {
		int32_t cx, cz;
		int16_t heights[VO_CHUNK_CELLS * VO_CHUNK_CELLS];
		if (vo_decode_collision_section(payload, len, &cx, &cz, heights) == 0)
			VoField_SetChunk(cx, cz, heights);
	} else if (type == VO_MSG_COLLISION_RESET) {
		VoField_Clear();
	}
}

void VO_Init(void)
{
	if (s_open) return;
	VoField_Clear();
	if (vo_link_open(&s_link, VO_ROLE_PLAYER, VO_Log) == 0) {
		s_link.on_message = VO_OnMessage;
		s_open = qtrue;
		gi.Printf("ValOutcast: link open on port %d\n", VO_PORT_PLAYER);
	} else {
		gi.Printf("ValOutcast: could not open link (port %d in use?)\n", VO_PORT_PLAYER);
	}
}

void VO_Frame(void)
{
	if (!s_open) return;
	vo_link_tick(&s_link);

	gentity_t *ent = &g_entities[0];
	if (!ent->client) return;

	// The wire position is the player's feet (bottom center of the hull).
	vec3_t feet;
	VectorCopy(ent->client->ps.origin, feet);
	feet[2] += ent->mins[2];

	// Switch on only when the player is free and standing on the level's floor (not mid-air in an intro).
	// Switch off for cutscenes and death, so scripts that wait for the player behave normally.
	const qboolean free = (!in_camera && !player_locked && ent->health > 0) ? qtrue : qfalse;
	if (!s_active) {
		if (free && ent->client->ps.groundEntityNum == ENTITYNUM_WORLD) s_active = qtrue;
	} else if (!free) {
		s_active = qfalse;
	}

	if (s_link.connected && s_active) {
		vec3_t forward;
		float pos[3], look[3];
		AngleVectors(ent->client->ps.viewangles, forward, NULL, NULL);
		VO_ToWire(feet, pos, UNITS_PER_METER);
		VO_ToWire(forward, look, 1.0f);
		vo_link_send_player_state(&s_link, pos, look);
	}

	if (++s_framesSinceEvict >= 100) {
		s_framesSinceEvict = 0;
		VoField_EvictFar(ent->client->ps.origin, KEEP_CHUNKS);
	}

	if (getenv("VO_DEBUG") && ++s_debugFrames >= 40) {
		s_debugFrames = 0;
		gi.Printf("ValOutcast: feet at (%.1f, %.1f, %.1f), %d chunks\n", feet[0], feet[1], feet[2], VoField_ChunkCount());
	}

	qboolean onField = (s_active && VoField_Covers(ent->client->ps.origin)) ? qtrue : qfalse;
	if (onField != s_onField) {
		s_onField = onField;
		gi.Printf("ValOutcast: %s (%d chunks)\n", onField ? "now on Valheim ground" : "left Valheim ground",
		          VoField_ChunkCount());
	}
}

void VO_Shutdown(void)
{
	if (!s_open) return;
	vo_link_close(&s_link);
	VoField_Clear();
	s_open = qfalse;
	s_onField = qfalse;
	s_active = qfalse;
}

void VO_PlayerTrace(trace_t *results, const vec3_t start, const vec3_t mins, const vec3_t maxs, const vec3_t end,
                    const int passEntityNum, const int contentMask, const EG2_Collision eG2TraceType, const int useLod)
{
	if (!s_open || !s_active || !VoField_Covers(start)) {
		gi.trace(results, start, mins, maxs, end, passEntityNum, contentMask, eG2TraceType, useLod);
		return;
	}

	// Over Valheim ground: the level's own solid brushes are ignored (other entities still count).
	const unsigned int mask = (unsigned int)contentMask & ~(CONTENTS_SOLID | CONTENTS_PLAYERCLIP);
	gi.trace(results, start, mins, maxs, end, passEntityNum, (int)mask, eG2TraceType, useLod);

	static const vec3_t zero = {0, 0, 0};
	const float *bmins = mins ? mins : zero;
	const float *bmaxs = maxs ? maxs : zero;
	float fraction, normal[3];
	if (VoField_Trace(start, bmins, bmaxs, end, &fraction, normal) && fraction < results->fraction) {
		results->allsolid = qfalse;
		results->startsolid = qfalse;
		results->fraction = fraction;
		for (int i = 0; i < 3; i++) results->endpos[i] = start[i] + fraction * (end[i] - start[i]);
		VectorCopy(normal, results->plane.normal);
		results->plane.dist = DotProduct(normal, results->endpos);
		results->plane.type = 3;
		results->plane.signbits = 0;
		results->surfaceFlags = 0;
		results->contents = CONTENTS_SOLID;
		results->entityNum = ENTITYNUM_WORLD;
	}
}
