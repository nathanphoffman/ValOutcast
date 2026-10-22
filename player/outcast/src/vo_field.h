// ValOutcast collision field, Outcast side. See protocol/PROTOCOL.md ("Collision field").
// Pure math, no engine types, so it can be tested alone (tools/test_field.sh).
// All positions here are in Outcast units (1 unit = 1/32 m, X forward, Y left, Z up).
#ifndef VO_FIELD_H
#define VO_FIELD_H

#include <stdint.h>

void VoField_Clear(void);
// heights: 256 values, [iz * 16 + ix], 1/32 m. -32768 means no data.
void VoField_SetChunk(int cx, int cz, const int16_t *heights);
// Drops chunks more than keepChunks away (in chunks) from the chunk that contains q.
void VoField_EvictFar(const float q[3], int keepChunks);
int VoField_ChunkCount(void);
// True if a chunk exists under/around the point (x, y of q).
bool VoField_Covers(const float q[3]);
// Sweeps the box (mins/maxs around the origin) from start to end.
// Returns true on a hit, with the fraction (0..1) and the surface normal.
// Boxes the start position already overlaps are ignored, so the player is never stuck in the field.
bool VoField_Trace(const float start[3], const float mins[3], const float maxs[3], const float end[3],
                   float *fraction, float normal[3]);

#endif
