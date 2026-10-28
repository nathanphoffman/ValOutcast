/* Packets about actors: spawn, remove, hits and force powers. See PROTOCOL.md. */
#include "vo_internal.h"

int vo_encode_actor_upsert(uint8_t *packet, uint32_t sequence, const vo_actor *actor)
{
	uint8_t *cursor = write_header(packet, VO_MSG_ACTOR_UPSERT, sequence);
	cursor = write_u32(cursor, actor->id);
	cursor = write_vec3(cursor, actor->pos);
	cursor = write_f32(cursor, actor->radius);
	cursor = write_f32(cursor, actor->height);
	cursor = write_u8(cursor, (uint8_t)actor->hp);
	cursor = write_u8(cursor, (uint8_t)actor->flags);
	cursor = write_padding(cursor, ACTOR_UPSERT_PADDING);

	return (int)(cursor - packet);
}

int vo_decode_actor_upsert(const uint8_t *payload, int length, vo_actor *actor)
{
	uint8_t hit_points, flags;
	const uint8_t *cursor;

	if (length < SIZE_ACTOR_UPSERT - VO_HEADER_SIZE) return -1;
	cursor = read_u32(payload, &actor->id);
	cursor = read_vec3(cursor, actor->pos);
	cursor = read_f32(cursor, &actor->radius);
	cursor = read_f32(cursor, &actor->height);
	cursor = read_u8(cursor, &hit_points);
	read_u8(cursor, &flags);
	actor->hp = hit_points;
	actor->flags = flags;

	return 0;
}

int vo_encode_actor_remove(uint8_t *packet, uint32_t sequence, uint32_t actor_id)
{
	uint8_t *cursor = write_header(packet, VO_MSG_ACTOR_REMOVE, sequence);
	cursor = write_u32(cursor, actor_id);

	return (int)(cursor - packet);
}

int vo_decode_actor_remove(const uint8_t *payload, int length, uint32_t *actor_id)
{
	if (length < SIZE_ACTOR_REMOVE - VO_HEADER_SIZE) return -1;
	read_u32(payload, actor_id);

	return 0;
}

/* Hits and force powers share one layout (actor, amount, direction); only the message type differs. */
int encode_force_power(uint8_t *packet, uint32_t sequence, uint32_t actor_id, float strength, const float direction[VO_VEC3], int type)
{
	uint8_t *cursor = write_header(packet, type, sequence);
	cursor = write_u32(cursor, actor_id);
	cursor = write_f32(cursor, strength);
	cursor = write_vec3(cursor, direction);

	return (int)(cursor - packet);
}

int vo_encode_hit_actor(uint8_t *packet, uint32_t sequence, uint32_t actor_id, float damage, const float direction[VO_VEC3])
{
	return encode_force_power(packet, sequence, actor_id, damage, direction, VO_MSG_HIT_ACTOR);
}

int vo_encode_actor_push(uint8_t *packet, uint32_t sequence, uint32_t actor_id, float strength, const float direction[VO_VEC3])
{
	return encode_force_power(packet, sequence, actor_id, strength, direction, VO_MSG_ACTOR_PUSH);
}

int vo_encode_actor_charm(uint8_t *packet, uint32_t sequence, uint32_t actor_id, float strength, const float direction[VO_VEC3])
{
	return encode_force_power(packet, sequence, actor_id, strength, direction, VO_MSG_ACTOR_CHARM);
}

int vo_encode_actor_grip(uint8_t *packet, uint32_t sequence, uint32_t actor_id, float strength, const float direction[VO_VEC3])
{
	return encode_force_power(packet, sequence, actor_id, strength, direction, VO_MSG_ACTOR_GRIP);
}

int vo_encode_actor_heal(uint8_t *packet, uint32_t sequence, uint32_t actor_id, float strength, const float direction[VO_VEC3])
{
	return encode_force_power(packet, sequence, actor_id, strength, direction, VO_MSG_ACTOR_HEAL);
}

int vo_encode_hit_world(uint8_t *packet, uint32_t sequence, const float position[VO_VEC3], const float direction[VO_VEC3], float damage, int kind)
{
	uint8_t *cursor = write_header(packet, VO_MSG_HIT_WORLD, sequence);
	cursor = write_vec3(cursor, position);
	cursor = write_vec3(cursor, direction);
	cursor = write_f32(cursor, damage);
	cursor = write_u8(cursor, (uint8_t)kind);
	cursor = write_padding(cursor, HIT_WORLD_PADDING);

	return (int)(cursor - packet);
}

int vo_encode_player_hurt(uint8_t *packet, uint32_t sequence, const vo_hurt *hurt)
{
	uint8_t *cursor = write_header(packet, VO_MSG_PLAYER_HURT, sequence);
	cursor = write_f32(cursor, hurt->damage);
	cursor = write_vec3(cursor, hurt->dir);
	cursor = write_u32(cursor, hurt->source);

	return (int)(cursor - packet);
}

int vo_decode_player_hurt(const uint8_t *payload, int length, vo_hurt *hurt)
{
	const uint8_t *cursor;

	if (length < SIZE_PLAYER_HURT - VO_HEADER_SIZE) return -1;
	cursor = read_f32(payload, &hurt->damage);
	cursor = read_vec3(cursor, hurt->dir);
	read_u32(cursor, &hurt->source);

	return 0;
}
