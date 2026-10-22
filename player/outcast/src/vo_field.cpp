// ValOutcast collision field, Outcast side. See vo_field.h.
#include "vo_field.h"

#include <cmath>
#include <unordered_map>

namespace {

const int CHUNK_CELLS = 16;
const float CELL_UNITS = 16.0f;       // 0.5 m
const float COLUMN_DEPTH = 8192.0f;   // columns reach this far below their top
const float PULLBACK = 0.125f;        // stop this far before a surface
const float EPS = 0.03125f;

struct Chunk {
	int16_t h[CHUNK_CELLS * CHUNK_CELLS];
};

std::unordered_map<int64_t, Chunk> s_chunks;

int64_t Key(int cx, int cz)
{
	return ((int64_t)cx << 32) | (uint32_t)cz;
}

int FloorDiv(int a, int b)
{
	int q = a / b;
	return (a % b != 0 && ((a < 0) != (b < 0))) ? q - 1 : q;
}

// Outcast units to cell indices. Wire X = -y/32, wire Z = -x/32, cells are 0.5 m (16 units).
int CellX(float qy) { return (int)std::floor(-qy / CELL_UNITS); }
int CellZ(float qx) { return (int)std::floor(-qx / CELL_UNITS); }

// Returns false if there is no column at this cell.
bool ColumnTop(int ix, int iz, float *top)
{
	auto it = s_chunks.find(Key(FloorDiv(ix, CHUNK_CELLS), FloorDiv(iz, CHUNK_CELLS)));
	if (it == s_chunks.end()) return false;
	int16_t h = it->second.h[(iz - FloorDiv(iz, CHUNK_CELLS) * CHUNK_CELLS) * CHUNK_CELLS +
	                         (ix - FloorDiv(ix, CHUNK_CELLS) * CHUNK_CELLS)];
	if (h == INT16_MIN) return false;
	*top = (float)h;
	return true;
}

} // namespace

void VoField_Clear(void)
{
	s_chunks.clear();
}

void VoField_SetChunk(int cx, int cz, const int16_t *heights)
{
	Chunk &c = s_chunks[Key(cx, cz)];
	for (int i = 0; i < CHUNK_CELLS * CHUNK_CELLS; i++) c.h[i] = heights[i];
}

void VoField_EvictFar(const float q[3], int keepChunks)
{
	int pcx = FloorDiv(CellX(q[1]), CHUNK_CELLS);
	int pcz = FloorDiv(CellZ(q[0]), CHUNK_CELLS);
	for (auto it = s_chunks.begin(); it != s_chunks.end();) {
		int cx = (int)(it->first >> 32);
		int cz = (int)(int32_t)(it->first & 0xFFFFFFFF);
		if (std::abs(cx - pcx) > keepChunks || std::abs(cz - pcz) > keepChunks) it = s_chunks.erase(it);
		else ++it;
	}
}

int VoField_ChunkCount(void)
{
	return (int)s_chunks.size();
}

bool VoField_Covers(const float q[3])
{
	int ix = CellX(q[1]), iz = CellZ(q[0]);
	return s_chunks.count(Key(FloorDiv(ix, CHUNK_CELLS), FloorDiv(iz, CHUNK_CELLS))) != 0;
}

bool VoField_Trace(const float start[3], const float mins[3], const float maxs[3], const float end[3],
                   float *fraction, float normal[3])
{
	float d[3], lo[3], hi[3];
	for (int a = 0; a < 3; a++) {
		d[a] = end[a] - start[a];
		lo[a] = (start[a] < end[a] ? start[a] : end[a]) + mins[a];
		hi[a] = (start[a] > end[a] ? start[a] : end[a]) + maxs[a];
	}
	int ix0 = CellX(hi[1]), ix1 = CellX(lo[1]);
	int iz0 = CellZ(hi[0]), iz1 = CellZ(lo[0]);

	bool hit = false;
	float bestT = 1.0f;
	int bestAxis = 0;

	for (int iz = iz0; iz <= iz1; iz++) {
		for (int ix = ix0; ix <= ix1; ix++) {
			float top;
			if (!ColumnTop(ix, iz, &top)) continue;
			if (lo[2] >= top || hi[2] <= top - COLUMN_DEPTH) continue;

			// Column box in Outcast units, grown by the moving box (so the start point can be swept as a ray).
			float bmin[3] = {-(iz + 1) * CELL_UNITS - maxs[0], -(ix + 1) * CELL_UNITS - maxs[1], top - COLUMN_DEPTH - maxs[2]};
			float bmax[3] = {-iz * CELL_UNITS - mins[0], -ix * CELL_UNITS - mins[1], top - mins[2]};

			bool inside = true;
			float tEnter = -1e30f, tExit = 1e30f;
			int enterAxis = 0;
			bool miss = false;
			for (int a = 0; a < 3 && !miss; a++) {
				if (!(start[a] > bmin[a] + EPS && start[a] < bmax[a] - EPS)) inside = false;
				if (std::fabs(d[a]) < 1e-9f) {
					if (start[a] <= bmin[a] + EPS || start[a] >= bmax[a] - EPS) miss = true;
				} else {
					float t1 = (bmin[a] - start[a]) / d[a];
					float t2 = (bmax[a] - start[a]) / d[a];
					if (t1 > t2) { float t = t1; t1 = t2; t2 = t; }
					if (t1 > tEnter) { tEnter = t1; enterAxis = a; }
					if (t2 < tExit) tExit = t2;
				}
			}
			if (miss || inside) continue;
			if (tEnter >= tExit || tExit < 0.0f || tEnter >= bestT) continue;
			if (tEnter < -1e-4f) continue; // started past the entry face: ignore
			if (tEnter < 0.0f) tEnter = 0.0f;
			bestT = tEnter;
			bestAxis = enterAxis;
			hit = true;
		}
	}
	if (!hit) return false;

	float back = PULLBACK / std::fabs(d[bestAxis]);
	float t = bestT - back;
	*fraction = t < 0.0f ? 0.0f : t;
	normal[0] = normal[1] = normal[2] = 0.0f;
	normal[bestAxis] = d[bestAxis] > 0 ? -1.0f : 1.0f;
	return true;
}
