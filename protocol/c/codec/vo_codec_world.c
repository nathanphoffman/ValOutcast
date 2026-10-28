/* Packets about the world: terrain chunks and lighting. See PROTOCOL.md. */
#include "vo_internal.h"

int vo_encode_collision_section(uint8_t *packet, uint32_t sequence, int32_t chunk_x, int32_t chunk_z, const int16_t *heights)
{
	int cell;
	uint8_t *cursor = write_header(packet, VO_MSG_COLLISION_SECTION, sequence);
	cursor = write_u32(cursor, (uint32_t)chunk_x);
	cursor = write_u32(cursor, (uint32_t)chunk_z);

	for (cell = 0; cell < VO_CHUNK_CELLS * VO_CHUNK_CELLS; cell++) cursor = write_u16(cursor, (uint16_t)heights[cell]);

	return (int)(cursor - packet);
}

int vo_decode_collision_section(const uint8_t *payload, int length, int32_t *chunk_x, int32_t *chunk_z, int16_t *heights)
{
	int cell;
	uint32_t raw_x, raw_z;
	const uint8_t *cursor;

	if (length < SIZE_COLLISION_SECTION - VO_HEADER_SIZE) return -1;
	cursor = read_u32(payload, &raw_x);
	cursor = read_u32(cursor, &raw_z);
	*chunk_x = (int32_t)raw_x;
	*chunk_z = (int32_t)raw_z;

	for (cell = 0; cell < VO_CHUNK_CELLS * VO_CHUNK_CELLS; cell++) {
		uint16_t raw_height;
		cursor = read_u16(cursor, &raw_height);
		heights[cell] = (int16_t)raw_height;
	}

	return 0;
}

int vo_encode_world_context(uint8_t *packet, uint32_t sequence, const vo_world *world)
{
	uint8_t *cursor = write_header(packet, VO_MSG_WORLD_CONTEXT, sequence);
	cursor = write_vec3(cursor, world->sun_dir);
	cursor = write_vec3(cursor, world->sun_rgb);
	cursor = write_vec3(cursor, world->ambient);

	return (int)(cursor - packet);
}

int vo_decode_world_context(const uint8_t *payload, int length, vo_world *world)
{
	const uint8_t *cursor;

	if (length < SIZE_WORLD_CONTEXT - VO_HEADER_SIZE) return -1;
	cursor = read_vec3(payload, world->sun_dir);
	cursor = read_vec3(cursor, world->sun_rgb);
	read_vec3(cursor, world->ambient);

	return 0;
}
