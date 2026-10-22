// ValOutcast bridge, Outcast side. Called from the game code in g_main.cpp and g_active.cpp.
#ifndef VO_BRIDGE_H
#define VO_BRIDGE_H

#include "g_headers.h"
#include "g_local.h"

void VO_Init(void);
void VO_Frame(void);
void VO_Shutdown(void);

// Drop-in for gi.trace, used for the player's movement (pm.trace). While the player is over
// Valheim ground it ignores the level's own walls and floors and uses the Valheim terrain instead.
void VO_PlayerTrace(trace_t *results, const vec3_t start, const vec3_t mins, const vec3_t maxs, const vec3_t end,
                    const int passEntityNum, const int contentMask, const EG2_Collision eG2TraceType, const int useLod);

// Drop-in for gi.pointcontents: over Valheim ground the level's water, lava and fog volumes do not exist.
int VO_PointContents(const vec3_t point, int passEntityNum);

// Called with the player's usercmd just before movement runs. While Valheim's window is sending input,
// it replaces the keyboard and mouse state in the cmd.
void VO_ModifyUserCmd(gentity_t *ent, usercmd_t *ucmd);
// Sets the held force power buttons (grip, lightning); called just before the game's force power update.
void VO_ForceButtons(gentity_t *ent, usercmd_t *ucmd);

// The player's shot or lightsaber hit the world itself (not a creature) at pos, blow direction dir (Outcast space).
// kind: 0 = ranged attack (a shot), 1 = melee attack (the lightsaber). Goes to the world side so it can damage whatever stands there (trees, rocks).
void VO_ReportWorldHit(const vec3_t pos, const vec3_t dir, float damage, int kind);

#endif
