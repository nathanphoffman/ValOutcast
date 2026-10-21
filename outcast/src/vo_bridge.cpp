// ValOutcast bridge, Outcast side. This is the player side of the link.
#include "g_headers.h"
#include "g_local.h"
#include "vo_bridge.h"

extern "C" {
#include "vo_link.h"
}

// Outcast uses 32 units per meter, X forward, Y left, Z up. The wire is meters, X right, Y up, -Z forward.
static const float UNITS_PER_METER = 32.0f;

static void VO_ToWire(const vec3_t q, float out[3], float scale)
{
	out[0] = -q[1] / scale;
	out[1] = q[2] / scale;
	out[2] = -q[0] / scale;
}

static vo_link s_link;
static qboolean s_open = qfalse;

static void VO_Log(const char *message)
{
	gi.Printf("ValOutcast: %s\n", message);
}

void VO_Init(void)
{
	if (s_open) return;
	if (vo_link_open(&s_link, VO_ROLE_PLAYER, VO_Log) == 0) {
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
	if (s_link.connected && ent->client) {
		vec3_t forward;
		float pos[3], look[3];
		AngleVectors(ent->client->ps.viewangles, forward, NULL, NULL);
		VO_ToWire(ent->client->ps.origin, pos, UNITS_PER_METER);
		VO_ToWire(forward, look, 1.0f);
		vo_link_send_player_state(&s_link, pos, look);
	}
}

void VO_Shutdown(void)
{
	if (!s_open) return;
	vo_link_close(&s_link);
	s_open = qfalse;
}
