// ValOutcast bridge, Outcast side. This is the player side of the link.
#include "vo_bridge.h"
#include "vo_field.h"

#include <math.h>
#include <stdlib.h>
#include <time.h>

extern "C" {
#include "vo_link.h"
}

// Outcast uses 32 units per meter, X forward, Y left, Z up. The wire is meters, X right, Y up, -Z forward.
static const float UNITS_PER_METER = 32.0f;
static const int KEEP_CHUNKS = 12;  // forget terrain chunks farther than this (in chunks of 8 m)
static const int MAX_PROXIES = 64;
static const int PROXY_HEALTH = 100000;      // proxies never die here; Valheim decides who is alive
static const int PROXY_TIMEOUT_MS = 1000;    // drop a proxy this long after the last update
static const float HURT_SCALE = 4.0f;        // Valheim damage points to Outcast damage points (Valheim's player has about 25 health, Outcast's 100)
static const int INPUT_TIMEOUT_MS = 500;

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

struct Proxy {
	uint32_t id;
	gentity_t *ent;
	int lastSeenMs;
};
static Proxy s_proxies[64];
static int s_proxyCount = 0;

static vo_input s_input;
static uint32_t s_inputMs = 0;
static float s_yaw = 0.0f, s_pitch = 0.0f;   // Outcast angles, degrees, while Valheim drives the view
static qboolean s_inputLive = qfalse;
static uint32_t s_buttonLatch = 0;   // button presses not yet used by a game frame (a click can be shorter than a frame)
static int s_weaponRequest = 0;      // 1-9 pick a slot, 10 next, 11 previous

static uint32_t VO_NowMs(void)
{
	struct timespec ts;
	clock_gettime(CLOCK_MONOTONIC, &ts);
	return (uint32_t)(ts.tv_sec * 1000u + ts.tv_nsec / 1000000u);
}

// Wire (meters, X right, Y up, -Z forward) to Outcast (units, X forward, Y left, Z up).
static void VO_FromWire(const float w[3], vec3_t out, float scale)
{
	out[0] = -w[2] * scale;
	out[1] = -w[0] * scale;
	out[2] = w[1] * scale;
}

static void VO_RemoveProxy(int index)
{
	Proxy &p = s_proxies[index];
	if (p.ent && p.ent->inuse) {
		gi.unlinkentity(p.ent);
		G_FreeEntity(p.ent);
	}
	s_proxies[index] = s_proxies[--s_proxyCount];
}

static void VO_ClearProxies(void)
{
	while (s_proxyCount > 0) VO_RemoveProxy(s_proxyCount - 1);
}

static void VO_UpsertActor(const vo_actor *a)
{
	int i;
	for (i = 0; i < s_proxyCount; i++) {
		if (s_proxies[i].id == a->id) break;
	}
	if (i == s_proxyCount) {
		if (s_proxyCount >= MAX_PROXIES) return;
		gentity_t *ent = G_Spawn();
		if (!ent) return;
		ent->classname = "vo_actor";
		ent->svFlags = SVF_NOCLIENT;          // invisible: Valheim draws the creature
		ent->contents = CONTENTS_BODY;        // the player can walk into it and hit it
		ent->clipmask = 0;
		ent->takedamage = qtrue;
		ent->health = ent->max_health = PROXY_HEALTH;
		ent->e_ThinkFunc = thinkF_NULL;
		ent->nextthink = 0;
		s_proxies[i].id = a->id;
		s_proxies[i].ent = ent;
		s_proxyCount++;
	}
	Proxy &p = s_proxies[i];
	p.lastSeenMs = (int)VO_NowMs();

	const float half = a->radius * UNITS_PER_METER;
	VectorSet(p.ent->mins, -half, -half, 0.0f);
	VectorSet(p.ent->maxs, half, half, a->height * UNITS_PER_METER);
	vec3_t origin;
	VO_FromWire(a->pos, origin, UNITS_PER_METER);
	G_SetOrigin(p.ent, origin);
	gi.linkentity(p.ent);
}

static void VO_RemoveActorById(uint32_t id)
{
	for (int i = 0; i < s_proxyCount; i++) {
		if (s_proxies[i].id == id) {
			VO_RemoveProxy(i);
			return;
		}
	}
}

// Damage the player did to a proxy since the last frame goes to Valheim; the proxy's health is topped up again.
static void VO_SendProxyHits(const gentity_t *player)
{
	for (int i = 0; i < s_proxyCount; i++) {
		gentity_t *ent = s_proxies[i].ent;
		const int damage = PROXY_HEALTH - ent->health;
		if (damage <= 0) continue;
		ent->health = PROXY_HEALTH;

		vec3_t toActor;
		VectorSubtract(ent->currentOrigin, player->currentOrigin, toActor);
		toActor[2] = 0.0f;
		VectorNormalize(toActor);
		float wire[3];
		VO_ToWire(toActor, wire, 1.0f);
		vo_link_send_hit_actor(&s_link, s_proxies[i].id, (float)damage, wire);
	}
}

static void VO_ApplyPlayerHurt(const vo_hurt *h)
{
	gentity_t *player = &g_entities[0];
	if (!s_active || !player->client || player->health <= 0) return;
	const int damage = (int)ceilf(h->damage * HURT_SCALE);
	if (damage <= 0) return;
	// h->dir is the direction the blow travels (wire space); G_Damage wants it in Outcast space.
	vec3_t dir;
	VO_FromWire(h->dir, dir, 1.0f);
	G_Damage(player, &g_entities[ENTITYNUM_WORLD], &g_entities[ENTITYNUM_WORLD], dir, NULL, damage, 0, MOD_UNKNOWN);
}

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
	} else if (type == VO_MSG_ACTOR_UPSERT) {
		vo_actor a;
		if (s_active && vo_decode_actor_upsert(payload, len, &a) == 0) VO_UpsertActor(&a);
	} else if (type == VO_MSG_ACTOR_REMOVE) {
		uint32_t id;
		if (vo_decode_actor_remove(payload, len, &id) == 0) VO_RemoveActorById(id);
	} else if (type == VO_MSG_PLAYER_HURT) {
		vo_hurt h;
		if (vo_decode_player_hurt(payload, len, &h) == 0) VO_ApplyPlayerHurt(&h);
	} else if (type == VO_MSG_INPUT) {
		vo_input in;
		if (vo_decode_input(payload, len, &in) == 0) {
			s_input.buttons = in.buttons;
			s_buttonLatch |= in.buttons;
			if (in.weapon != 0) s_weaponRequest = in.weapon;
			s_input.forward = in.forward;
			s_input.right = in.right;
			s_input.up = in.up;
			s_input.yaw += in.yaw;       // look changes add up until the next game frame uses them
			s_input.pitch += in.pitch;
			s_inputMs = VO_NowMs();
		}
	}
}

void VO_Init(void)
{
	if (s_open) return;
	VoField_Clear();
	s_proxyCount = 0;   // the entities themselves were thrown away with the old level
	memset(&s_input, 0, sizeof(s_input));
	s_inputLive = qfalse;
	s_buttonLatch = 0;
	s_weaponRequest = 0;
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

	if (s_weaponRequest != 0) {
		if (s_active) {
			char cmd[32];
			if (s_weaponRequest == 10) Com_sprintf(cmd, sizeof(cmd), "weapnext\n");
			else if (s_weaponRequest == 11) Com_sprintf(cmd, sizeof(cmd), "weapprev\n");
			else Com_sprintf(cmd, sizeof(cmd), "weapon %d\n", s_weaponRequest);
			gi.SendConsoleCommand(cmd);
		}
		s_weaponRequest = 0;
	}

	if (!s_active || !s_link.connected) {
		VO_ClearProxies();
	} else {
		const int nowMs = (int)VO_NowMs();
		for (int i = s_proxyCount - 1; i >= 0; i--) {
			if (nowMs - s_proxies[i].lastSeenMs > PROXY_TIMEOUT_MS) VO_RemoveProxy(i);
		}
		VO_SendProxyHits(ent);
	}

	if (getenv("VO_DEBUG_SKIP") && in_camera && (level.framenum % 40) == 0) {
		gi.SendConsoleCommand("exitview\n");  // test helper: skip cutscenes with no one at the keyboard
	}

	if (getenv("VO_DEBUG_HIT") && s_active && s_proxyCount > 0 && (level.framenum % 120) == 0) {
		// Test helper: the player "hits" the first creature for 25 damage every few seconds.
		vec3_t dir = {1, 0, 0};
		G_Damage(s_proxies[0].ent, ent, ent, dir, NULL, 25, 0, MOD_MELEE);
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
		gi.Printf("ValOutcast: feet at (%.1f, %.1f, %.1f), %d chunks, %d creatures, active %d (camera %d, locked %d, health %d, ground %d) time %d\n",
		          feet[0], feet[1], feet[2], VoField_ChunkCount(), s_proxyCount, (int)s_active, (int)in_camera,
		          (int)player_locked, ent->health, ent->client->ps.groundEntityNum, level.time);
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
	s_proxyCount = 0;
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

void VO_ModifyUserCmd(gentity_t *ent, usercmd_t *ucmd)
{
	if (!s_open || !s_active || ent->s.number != 0 || !ent->client) return;
	if (VO_NowMs() - s_inputMs > (uint32_t)INPUT_TIMEOUT_MS || s_inputMs == 0) {
		s_inputLive = qfalse;   // Valheim is not sending input: leave the Outcast keyboard alone
		return;
	}

	playerState_t *ps = &ent->client->ps;
	if (!s_inputLive) {
		// Valheim just took over the view: start from where the player is looking now.
		s_inputLive = qtrue;
		s_yaw = ps->viewangles[YAW];
		s_pitch = AngleNormalize180(ps->viewangles[PITCH]);
		s_input.yaw = s_input.pitch = 0.0f;
	}

	s_yaw += s_input.yaw;                 // positive yaw turns left
	s_pitch -= s_input.pitch;             // Outcast pitch is positive looking down
	s_input.yaw = s_input.pitch = 0.0f;
	if (s_pitch > 85.0f) s_pitch = 85.0f;
	if (s_pitch < -85.0f) s_pitch = -85.0f;

	ucmd->angles[YAW] = (ANGLE2SHORT(s_yaw) - ps->delta_angles[YAW]) & 0xFFFF;
	ucmd->angles[PITCH] = (ANGLE2SHORT(s_pitch) - ps->delta_angles[PITCH]) & 0xFFFF;

	ucmd->forwardmove = (signed char)(127.0f * fmaxf(-1.0f, fminf(1.0f, s_input.forward)));
	ucmd->rightmove = (signed char)(127.0f * fmaxf(-1.0f, fminf(1.0f, s_input.right)));
	ucmd->upmove = (signed char)(127.0f * fmaxf(-1.0f, fminf(1.0f, s_input.up)));

	const uint32_t buttons = s_input.buttons | s_buttonLatch;
	s_buttonLatch = 0;
	ucmd->buttons &= ~(BUTTON_ATTACK | BUTTON_ALT_ATTACK | BUTTON_USE);
	if (buttons & VO_BUTTON_ATTACK) ucmd->buttons |= BUTTON_ATTACK;
	if (buttons & VO_BUTTON_ALT_ATTACK) ucmd->buttons |= BUTTON_ALT_ATTACK;
	if (buttons & VO_BUTTON_USE) ucmd->buttons |= BUTTON_USE;
}
