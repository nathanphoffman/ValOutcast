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

typedef void (*vo_log_fn)(const char *message);
/* Called for every valid packet that is not Hello or Heartbeat. payload points just past the 8-byte header. */
typedef void (*vo_message_fn)(void *user, int type, const uint8_t *payload, int len);

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
int vo_encode_hello(uint8_t *out, uint32_t seq, int role, uint32_t pid);
int vo_encode_heartbeat(uint8_t *out, uint32_t seq, uint32_t uptime_ms);
int vo_encode_player_state(uint8_t *out, uint32_t seq, const float pos[3], const float look[3]);
/* CollisionSection payload: 8 + 512 bytes. heights has 256 entries. Returns 0 on success, -1 if too short. */
int vo_encode_collision_section(uint8_t *out, uint32_t seq, int32_t cx, int32_t cz, const int16_t *heights);
int vo_decode_collision_section(const uint8_t *payload, int len, int32_t *cx, int32_t *cz, int16_t *heights);
/* Reads the 24-byte payload that follows the header. */
void vo_decode_player_state(const uint8_t *payload, float pos[3], float look[3]);
/* Sends a PlayerState if connected. Position in meters, look is a unit vector, wire coordinates (see PROTOCOL.md). */
void vo_link_send_player_state(vo_link *link, const float pos[3], const float look[3]);
/* Returns the message type, or -1 if the packet is not valid. */
int vo_decode(const uint8_t *data, int len, uint32_t *seq_out);

/* Returns 0 on success, -1 on failure (port in use, etc). */
int vo_link_open(vo_link *link, int role, vo_log_fn log);
/* Call often (every game frame is fine). Never blocks. */
void vo_link_tick(vo_link *link);
void vo_link_close(vo_link *link);

#ifdef __cplusplus
}
#endif

#endif
