/* ValOutcast protocol v1, C side. See PROTOCOL.md. POSIX sockets (Linux). */
#ifndef VO_LINK_H
#define VO_LINK_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define VO_MAGIC 0x4F56
#define VO_VERSION 1
#define VO_MSG_HELLO 1
#define VO_MSG_HEARTBEAT 2
#define VO_MSG_PLAYER_STATE 3
#define VO_MSG_COLLISION_SECTION 4
#define VO_MSG_COLLISION_RESET 5
#define VO_MSG_ACTOR_UPSERT 6
#define VO_MSG_ACTOR_REMOVE 7
#define VO_MSG_HIT_ACTOR 8
#define VO_MSG_PLAYER_HURT 9
#define VO_MSG_INPUT 10
#define VO_MSG_WORLD_CONTEXT 11
#define VO_MSG_HIT_WORLD 12
#define VO_MSG_PLAYER_LIGHT 13
#define VO_MSG_LOADOUT 14
#define VO_MSG_ACTOR_PUSH 15
#define VO_MSG_ACTOR_CHARM 16
#define VO_MSG_ACTOR_GRIP 17
#define VO_MSG_ACTOR_HEAL 18
#define VO_BUTTON_ATTACK 1
#define VO_BUTTON_ALT_ATTACK 2
#define VO_BUTTON_USE 4
/* Abilities 1 to 7: bits 3 to 9 (value 8 << (n - 1)). Outcast: 1 push, 2 pull, 3 speed, 4 mind trick, 5 heal (fire on press), 6 grip, 7 lightning (while held). */
// Same as vo_protocol.py button_ability: ability n is bit n+2.
#define VO_BUTTON_ABILITY(n) (8u << ((n) - 1))
#define VO_ABILITY_COUNT 7
#define VO_ACTOR_HOSTILE 1
#define VO_ACTOR_LOOSE 2   /* a loose object (an item on the ground): can be pushed and pulled, not hit, not solid */
#define VO_CHUNK_CELLS 16
#define VO_CELL_SIZE_M 0.5f
#define VO_HEIGHT_UNITS_PER_M 32
#define VO_NO_HEIGHT (-32768)
#define VO_ROLE_WORLD 0
#define VO_ROLE_PLAYER 1
#define VO_PORT_WORLD 47100
#define VO_PORT_PLAYER 47101
#define VO_HEARTBEAT_MS 1000
#define VO_TIMEOUT_MS 3000
#define VO_HEADER_SIZE 8
#define VO_VEC3 3          /* floats in a position or direction */

typedef struct vo_actor {
	uint32_t id;
	float pos[VO_VEC3];       /* feet, meters, wire space */
	float radius, height;
	int hp;             /* 0..255, 255 = full */
	int flags;
} vo_actor;

typedef struct vo_hurt {
	float damage;
	float dir[VO_VEC3];
	uint32_t source;
} vo_hurt;

typedef struct vo_input {
	uint32_t buttons;
	float forward, right, up;
	float yaw, pitch;   /* degrees changed since the last packet */
	int weapon;         /* 0 none, 1-9 slot, 10 next, 11 previous */
} vo_input;

typedef struct vo_world {
	float sun_dir[VO_VEC3];   /* toward the sun, wire space, unit length */
	float sun_rgb[VO_VEC3];   /* sun color with brightness applied */
	float ambient[VO_VEC3];
} vo_world;

typedef void (*vo_log_fn)(const char *message);

/* Called for every valid packet that is not Hello or Heartbeat. payload points just past the 8-byte header. */
typedef void (*vo_message_fn)(void *user, int type, const uint8_t *payload, int length);

typedef struct vo_link {
	int sock;
	int role;
	int connected;
	uint32_t seq;
	uint32_t start_ms;
	uint32_t last_send_ms;
	uint32_t last_recv_ms;
	int timeout_ms;
	vo_log_fn log;
	vo_message_fn on_message;
	void *user;
} vo_link;

/* Packet codec. Return the number of bytes written. */
int vo_encode_hello(uint8_t *packet, uint32_t sequence, int role, uint32_t process_id);
int vo_encode_heartbeat(uint8_t *packet, uint32_t sequence, uint32_t uptime_ms);
int vo_encode_player_state(uint8_t *packet, uint32_t sequence, const float position[VO_VEC3], const float look[VO_VEC3]);

/* CollisionSection payload: 8 + 512 bytes. heights has 256 entries. Returns 0 on success, -1 if too short. */
int vo_encode_collision_section(uint8_t *packet, uint32_t sequence, int32_t chunk_x, int32_t chunk_z, const int16_t *heights);
int vo_decode_collision_section(const uint8_t *payload, int length, int32_t *chunk_x, int32_t *chunk_z, int16_t *heights);

/* Reads the 24-byte payload that follows the header. */
void vo_decode_player_state(const uint8_t *payload, float position[VO_VEC3], float look[VO_VEC3]);

/* Sends a PlayerState if connected. Position in meters, look is a unit vector, wire coordinates (see PROTOCOL.md). */
void vo_link_send_player_state(vo_link *link, const float position[VO_VEC3], const float look[VO_VEC3]);

/* Creature, combat and input messages. Encoders return the packet size. Decoders take the payload after the header and return 0, or -1 if it is too short. */
int vo_encode_actor_upsert(uint8_t *packet, uint32_t sequence, const vo_actor *actor);
int vo_decode_actor_upsert(const uint8_t *payload, int length, vo_actor *actor);
int vo_encode_actor_remove(uint8_t *packet, uint32_t sequence, uint32_t actor_id);
int vo_decode_actor_remove(const uint8_t *payload, int length, uint32_t *actor_id);
int vo_encode_hit_actor(uint8_t *packet, uint32_t sequence, uint32_t actor_id, float damage, const float direction[VO_VEC3]);
int vo_encode_player_hurt(uint8_t *packet, uint32_t sequence, const vo_hurt *hurt);
int vo_decode_player_hurt(const uint8_t *payload, int length, vo_hurt *hurt);
int vo_encode_input(uint8_t *packet, uint32_t sequence, const vo_input *input);
int vo_decode_input(const uint8_t *payload, int length, vo_input *input);
int vo_encode_world_context(uint8_t *packet, uint32_t sequence, const vo_world *world);
int vo_decode_world_context(const uint8_t *payload, int length, vo_world *world);

/* ActorPush: the player side moved a creature (a force push or pull). Same layout as HitActor: id, strength 0 to 1 (fraction of the
 * player side's full push; the world side scales it), direction (unit vector, wire space) the creature should move. 28 bytes. */
int vo_encode_actor_push(uint8_t *packet, uint32_t sequence, uint32_t actor_id, float strength, const float direction[VO_VEC3]);
void vo_link_send_actor_push(vo_link *link, uint32_t actor_id, float strength, const float direction[VO_VEC3]);

/* ActorCharm: the player side used a mind power on a creature (Outcast: mind trick). Same layout as HitActor: id, strength 0 to 1
 * (1 = the most the player side can do; the world side decides what that means), direction from the player to the creature. 28 bytes. */
int vo_encode_actor_charm(uint8_t *packet, uint32_t sequence, uint32_t actor_id, float strength, const float direction[VO_VEC3]);
void vo_link_send_actor_charm(vo_link *link, uint32_t actor_id, float strength, const float direction[VO_VEC3]);

/* ActorGrip: the player side holds a creature with a held power (Outcast: force grip). Sent every frame while held. Same layout as
 * HitActor: id, strength 0 to 1 (the power level), direction from the player to the creature. 28 bytes. */
int vo_encode_actor_grip(uint8_t *packet, uint32_t sequence, uint32_t actor_id, float strength, const float direction[VO_VEC3]);
void vo_link_send_actor_grip(vo_link *link, uint32_t actor_id, float strength, const float direction[VO_VEC3]);

/* ActorHeal: sent once per creature in reach, so the world side can heal each one separately. Same layout as HitActor: id, strength 0 to 1, direction. 28 bytes. */
int vo_encode_actor_heal(uint8_t *packet, uint32_t sequence, uint32_t actor_id, float strength, const float direction[VO_VEC3]);
void vo_link_send_actor_heal(vo_link *link, uint32_t actor_id, float strength, const float direction[VO_VEC3]);

/* Silently does nothing while not connected, so callers need no connection check. */
void vo_link_send_hit_actor(vo_link *link, uint32_t actor_id, float damage, const float direction[VO_VEC3]);

/* HitWorld: the player side hit the world itself (not a creature) at pos (meters), blow direction dir (unit vector), with damage
 * in its own points. kind: 0 = ranged attack (a shot), 1 = melee attack (a blade swing). 40 bytes. */
int vo_encode_hit_world(uint8_t *packet, uint32_t sequence, const float position[VO_VEC3], const float direction[VO_VEC3], float damage, int kind);
void vo_link_send_hit_world(vo_link *link, const float position[VO_VEC3], const float direction[VO_VEC3], float damage, int kind);

/* PlayerLight: lets the world side light up the surroundings, for example around a lit lightsaber. color 0..1 per channel, intensity 0 = off. 24 bytes. */
int vo_encode_player_light(uint8_t *packet, uint32_t sequence, const float color[VO_VEC3], float intensity);
void vo_link_send_player_light(vo_link *link, const float color[VO_VEC3], float intensity);

/* Loadout: which weapons the world side says the player owns, as a bit mask (bit n = the player side's weapon number n). 12 bytes. */
int vo_encode_loadout(uint8_t *packet, uint32_t sequence, uint32_t weapons);
int vo_decode_loadout(const uint8_t *payload, int length, uint32_t *weapons);

/* Returns the message type, or -1 if the packet is not valid. */
int vo_decode(const uint8_t *data, int length, uint32_t *sequence_out);

/* Returns 0 on success, -1 on failure (port in use, etc). */
int vo_link_open(vo_link *link, int role, vo_log_fn log);

/* Call often (every game frame is fine). Never blocks. */
void vo_link_tick(vo_link *link);
void vo_link_close(vo_link *link);

#ifdef __cplusplus
}

#endif

#endif
