/* Private helpers shared by the C protocol files. The public API is vo_link.h. */
#ifndef VO_INTERNAL_H
#define VO_INTERNAL_H

#include "vo_link.h"
/* Packet sizes in bytes, header included. */
#define SIZE_HELLO 16
#define SIZE_HEARTBEAT 12
#define SIZE_PLAYER_STATE 32
#define SIZE_COLLISION_SECTION 528
#define SIZE_ACTOR_UPSERT 36
#define SIZE_ACTOR_REMOVE 12
#define SIZE_HIT_ACTOR 28
#define SIZE_HIT_WORLD 40
#define SIZE_PLAYER_LIGHT 24
#define SIZE_LOADOUT 12
#define SIZE_PLAYER_HURT 28
#define SIZE_INPUT 36
#define SIZE_WORLD_CONTEXT 44
#define SIZE_RECV_BUFFER 2048

#define BYTE_MASK 0xFF
#define HALF_MASK 0xFFFF
#define BITS_PER_BYTE 8
#define BITS_PER_HALF 16
#define MS_PER_SECOND 1000u
#define NS_PER_MS 1000000u

/* Where the header fields sit in a packet: magic (2 bytes), version, type, then the sequence number. */
#define HEADER_VERSION_AT 2
#define HEADER_TYPE_AT 3
#define HEADER_SEQUENCE_AT 4

/* Padding bytes after the fields of messages whose size is rounded up to a multiple of 4. */
#define HELLO_PADDING 3
#define ACTOR_UPSERT_PADDING 2
#define HIT_WORLD_PADDING 3

/* Each writer stores one value at the cursor and returns the cursor just past it, so a packet is written field by field
 * with no byte offsets. Each reader does the same for reading. Everything is little-endian (codec/vo_wire.c). */
uint8_t *write_u8(uint8_t *cursor, uint8_t value);
uint8_t *write_u16(uint8_t *cursor, uint16_t value);
uint8_t *write_u32(uint8_t *cursor, uint32_t value);
uint8_t *write_f32(uint8_t *cursor, float value);
uint8_t *write_vec3(uint8_t *cursor, const float vec[VO_VEC3]);
uint8_t *write_padding(uint8_t *cursor, int count);
uint8_t *write_header(uint8_t *packet, int type, uint32_t sequence);
const uint8_t *read_u8(const uint8_t *cursor, uint8_t *value);
const uint8_t *read_u16(const uint8_t *cursor, uint16_t *value);
const uint8_t *read_u32(const uint8_t *cursor, uint32_t *value);
const uint8_t *read_f32(const uint8_t *cursor, float *value);
const uint8_t *read_vec3(const uint8_t *cursor, float vec[VO_VEC3]);

/* Force powers share the HitActor layout; only the message type differs (codec/vo_codec_actor.c). */
int encode_force_power(uint8_t *packet, uint32_t sequence, uint32_t actor_id, float strength, const float direction[VO_VEC3], int type);

/* Sends one finished packet to the other side (vo_link.c). */
void send_to_peer(vo_link *link, const uint8_t *buffer, int length);

#endif
