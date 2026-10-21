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
// Call right before the frame is shown.
void VO_OverlayPublish(void);

#endif
