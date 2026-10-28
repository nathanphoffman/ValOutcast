// ValOutcast overlay, renderer side. Turned on by setting the environment variable VO_OVERLAY=1.
// In overlay mode Outcast draws only its own things (hands, saber, effects, HUD) on a transparent
// background and publishes each finished frame in shared memory (/dev/shm/valoutcast_frame) for Valheim to show.
#ifndef VO_OVERLAY_H
#define VO_OVERLAY_H

#include "qgl.h"
#include "glext.h"

bool VO_OverlayEnabled(void);

// Replacement for qglBlendFunc: also keeps the alpha channel meaningful (see vo_overlay.cpp).
void VO_BlendFunc(GLenum src, GLenum dst);

// Light levels for models (0 to 255) and the direction toward the light, from how the Valheim world is lit.
void VO_GetLighting(float ambient[3], float directed[3], float dir[3]);

// Call at the start of every frame, before anything is drawn. In overlay mode the picture goes into an
// off-screen buffer instead of the window, so the window can stay hidden.
void VO_OverlayBeginFrame(void);

// Call right before the frame is shown.
void VO_OverlayPublish(void);

#endif
