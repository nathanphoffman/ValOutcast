/* Packets about the connection and the player: hello, state, loadout, light, input. See PROTOCOL.md. */
#include "vo_internal.h"

int vo_encode_hello(uint8_t *packet, uint32_t sequence, int role, uint32_t process_id)
{
	uint8_t *cursor = write_header(packet, VO_MSG_HELLO, sequence);
	cursor = write_u8(cursor, (uint8_t)role);
	cursor = write_padding(cursor, HELLO_PADDING);
	cursor = write_u32(cursor, process_id);

	return (int)(cursor - packet);
}

int vo_encode_heartbeat(uint8_t *packet, uint32_t sequence, uint32_t uptime_ms)
{
	uint8_t *cursor = write_header(packet, VO_MSG_HEARTBEAT, sequence);
	cursor = write_u32(cursor, uptime_ms);

	return (int)(cursor - packet);
}

int vo_encode_player_state(uint8_t *packet, uint32_t sequence, const float position[VO_VEC3], const float look[VO_VEC3])
{
	uint8_t *cursor = write_header(packet, VO_MSG_PLAYER_STATE, sequence);
	cursor = write_vec3(cursor, position);
	cursor = write_vec3(cursor, look);

	return (int)(cursor - packet);
}

void vo_decode_player_state(const uint8_t *payload, float position[VO_VEC3], float look[VO_VEC3])
{
	const uint8_t *cursor = read_vec3(payload, position);
	read_vec3(cursor, look);
}

int vo_encode_loadout(uint8_t *packet, uint32_t sequence, uint32_t weapons)
{
	uint8_t *cursor = write_header(packet, VO_MSG_LOADOUT, sequence);
	cursor = write_u32(cursor, weapons);

	return (int)(cursor - packet);
}

int vo_decode_loadout(const uint8_t *payload, int length, uint32_t *weapons)
{
	if (length < SIZE_LOADOUT - VO_HEADER_SIZE) return -1;
	read_u32(payload, weapons);

	return 0;
}

int vo_encode_player_light(uint8_t *packet, uint32_t sequence, const float color[VO_VEC3], float intensity)
{
	uint8_t *cursor = write_header(packet, VO_MSG_PLAYER_LIGHT, sequence);
	cursor = write_vec3(cursor, color);
	cursor = write_f32(cursor, intensity);

	return (int)(cursor - packet);
}

int vo_encode_input(uint8_t *packet, uint32_t sequence, const vo_input *input)
{
	uint8_t *cursor = write_header(packet, VO_MSG_INPUT, sequence);
	cursor = write_u32(cursor, input->buttons);
	cursor = write_f32(cursor, input->forward);
	cursor = write_f32(cursor, input->right);
	cursor = write_f32(cursor, input->up);
	cursor = write_f32(cursor, input->yaw);
	cursor = write_f32(cursor, input->pitch);
	cursor = write_u32(cursor, (uint32_t)input->weapon);

	return (int)(cursor - packet);
}

int vo_decode_input(const uint8_t *payload, int length, vo_input *input)
{
	uint32_t weapon;
	const uint8_t *cursor;

	if (length < SIZE_INPUT - VO_HEADER_SIZE) return -1;
	cursor = read_u32(payload, &input->buttons);
	cursor = read_f32(cursor, &input->forward);
	cursor = read_f32(cursor, &input->right);
	cursor = read_f32(cursor, &input->up);
	cursor = read_f32(cursor, &input->yaw);
	cursor = read_f32(cursor, &input->pitch);
	read_u32(cursor, &weapon);
	input->weapon = (int)weapon;

	return 0;
}

int vo_decode(const uint8_t *data, int length, uint32_t *sequence_out)
{
	uint16_t magic;
	uint8_t version, type;
	uint32_t sequence;
	const uint8_t *cursor;

	if (length < VO_HEADER_SIZE) return -1;
	cursor = read_u16(data, &magic);
	cursor = read_u8(cursor, &version);
	cursor = read_u8(cursor, &type);
	read_u32(cursor, &sequence);

	if (magic != VO_MAGIC || version != VO_VERSION) return -1;

	if (sequence_out) *sequence_out = sequence;

	return type;
}
