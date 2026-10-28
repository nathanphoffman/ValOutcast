/* The public vo_link_send_* functions: encode a packet and send it if connected. See PROTOCOL.md. */
#include "vo_internal.h"

/* Senders do nothing before the other side has answered, so callers need no connection check. */
static int can_send(const vo_link *link)
{
	return link->sock >= 0 && link->connected;
}

void vo_link_send_player_state(vo_link *link, const float position[VO_VEC3], const float look[VO_VEC3])
{
	uint8_t packet[SIZE_PLAYER_STATE];

	if (!can_send(link)) return;
	send_to_peer(link, packet, vo_encode_player_state(packet, ++link->seq, position, look));
}

void vo_link_send_hit_actor(vo_link *link, uint32_t actor_id, float damage, const float direction[VO_VEC3])
{
	uint8_t packet[SIZE_HIT_ACTOR];

	if (!can_send(link)) return;
	send_to_peer(link, packet, vo_encode_hit_actor(packet, ++link->seq, actor_id, damage, direction));
}

static void send_force_power(vo_link *link, uint32_t actor_id, float strength, const float direction[VO_VEC3], int type)
{
	uint8_t packet[SIZE_HIT_ACTOR];

	if (!can_send(link)) return;
	send_to_peer(link, packet, encode_force_power(packet, ++link->seq, actor_id, strength, direction, type));
}

void vo_link_send_actor_push(vo_link *link, uint32_t actor_id, float strength, const float direction[VO_VEC3])
{
	send_force_power(link, actor_id, strength, direction, VO_MSG_ACTOR_PUSH);
}

void vo_link_send_actor_charm(vo_link *link, uint32_t actor_id, float strength, const float direction[VO_VEC3])
{
	send_force_power(link, actor_id, strength, direction, VO_MSG_ACTOR_CHARM);
}

void vo_link_send_actor_grip(vo_link *link, uint32_t actor_id, float strength, const float direction[VO_VEC3])
{
	send_force_power(link, actor_id, strength, direction, VO_MSG_ACTOR_GRIP);
}

void vo_link_send_actor_heal(vo_link *link, uint32_t actor_id, float strength, const float direction[VO_VEC3])
{
	send_force_power(link, actor_id, strength, direction, VO_MSG_ACTOR_HEAL);
}

void vo_link_send_hit_world(vo_link *link, const float position[VO_VEC3], const float direction[VO_VEC3], float damage, int kind)
{
	uint8_t packet[SIZE_HIT_WORLD];

	if (!can_send(link)) return;
	send_to_peer(link, packet, vo_encode_hit_world(packet, ++link->seq, position, direction, damage, kind));
}

void vo_link_send_player_light(vo_link *link, const float color[VO_VEC3], float intensity)
{
	uint8_t packet[SIZE_PLAYER_LIGHT];

	if (!can_send(link)) return;
	send_to_peer(link, packet, vo_encode_player_light(packet, ++link->seq, color, intensity));
}
