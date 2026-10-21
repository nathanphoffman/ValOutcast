// ValOutcast bridge, Outcast side. This is the player side of the link.
#include "g_headers.h"
#include "g_local.h"
#include "vo_bridge.h"

extern "C" {
#include "vo_link.h"
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
	if (s_open) vo_link_tick(&s_link);
}

void VO_Shutdown(void)
{
	if (!s_open) return;
	vo_link_close(&s_link);
	s_open = qfalse;
}
