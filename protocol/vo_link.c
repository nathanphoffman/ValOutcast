/* ValOutcast protocol v1, C side. See PROTOCOL.md. */
#include "vo_link.h"

#include <arpa/inet.h>
#include <errno.h>
#include <fcntl.h>
#include <string.h>
#include <sys/socket.h>
#include <time.h>
#include <unistd.h>

static uint32_t now_ms(void)
{
	struct timespec ts;
	clock_gettime(CLOCK_MONOTONIC, &ts);
	return (uint32_t)(ts.tv_sec * 1000u + ts.tv_nsec / 1000000u);
}

static void put_u16(uint8_t *p, uint16_t v) { p[0] = v & 0xFF; p[1] = v >> 8; }
static void put_u32(uint8_t *p, uint32_t v)
{
	p[0] = v & 0xFF; p[1] = (v >> 8) & 0xFF; p[2] = (v >> 16) & 0xFF; p[3] = v >> 24;
}
static uint32_t get_u32(const uint8_t *p)
{
	return p[0] | (p[1] << 8) | (p[2] << 16) | ((uint32_t)p[3] << 24);
}

static void put_header(uint8_t *out, int type, uint32_t seq)
{
	put_u16(out, VO_MAGIC);
	out[2] = VO_VERSION;
	out[3] = (uint8_t)type;
	put_u32(out + 4, seq);
}

int vo_encode_hello(uint8_t *out, uint32_t seq, int role, uint32_t pid)
{
	put_header(out, VO_MSG_HELLO, seq);
	out[8] = (uint8_t)role;
	out[9] = out[10] = out[11] = 0;
	put_u32(out + 12, pid);
	return 16;
}

int vo_encode_heartbeat(uint8_t *out, uint32_t seq, uint32_t uptime_ms)
{
	put_header(out, VO_MSG_HEARTBEAT, seq);
	put_u32(out + 8, uptime_ms);
	return 12;
}

static void put_f32(uint8_t *p, float f)
{
	uint32_t v;
	memcpy(&v, &f, 4);
	put_u32(p, v);
}
static float get_f32(const uint8_t *p)
{
	uint32_t v = get_u32(p);
	float f;
	memcpy(&f, &v, 4);
	return f;
}

int vo_encode_player_state(uint8_t *out, uint32_t seq, const float pos[3], const float look[3])
{
	int i;
	put_header(out, VO_MSG_PLAYER_STATE, seq);
	for (i = 0; i < 3; i++) {
		put_f32(out + 8 + 4 * i, pos[i]);
		put_f32(out + 20 + 4 * i, look[i]);
	}
	return 32;
}

void vo_decode_player_state(const uint8_t *payload, float pos[3], float look[3])
{
	int i;
	for (i = 0; i < 3; i++) {
		pos[i] = get_f32(payload + 4 * i);
		look[i] = get_f32(payload + 12 + 4 * i);
	}
}

int vo_encode_collision_section(uint8_t *out, uint32_t seq, int32_t cx, int32_t cz, const int16_t *heights)
{
	int i;
	put_header(out, VO_MSG_COLLISION_SECTION, seq);
	put_u32(out + 8, (uint32_t)cx);
	put_u32(out + 12, (uint32_t)cz);
	for (i = 0; i < VO_CHUNK_CELLS * VO_CHUNK_CELLS; i++)
		put_u16(out + 16 + 2 * i, (uint16_t)heights[i]);
	return 16 + 512;
}

int vo_decode_collision_section(const uint8_t *payload, int len, int32_t *cx, int32_t *cz, int16_t *heights)
{
	int i;
	if (len < 8 + 512) return -1;
	*cx = (int32_t)get_u32(payload);
	*cz = (int32_t)get_u32(payload + 4);
	for (i = 0; i < VO_CHUNK_CELLS * VO_CHUNK_CELLS; i++)
		heights[i] = (int16_t)(payload[8 + 2 * i] | (payload[9 + 2 * i] << 8));
	return 0;
}

int vo_encode_actor_upsert(uint8_t *out, uint32_t seq, const vo_actor *a)
{
	int i;
	put_header(out, VO_MSG_ACTOR_UPSERT, seq);
	put_u32(out + 8, a->id);
	for (i = 0; i < 3; i++) put_f32(out + 12 + 4 * i, a->pos[i]);
	put_f32(out + 24, a->radius);
	put_f32(out + 28, a->height);
	out[32] = (uint8_t)a->hp;
	out[33] = (uint8_t)a->flags;
	out[34] = out[35] = 0;
	return 36;
}

int vo_decode_actor_upsert(const uint8_t *payload, int len, vo_actor *a)
{
	int i;
	if (len < 28) return -1;
	a->id = get_u32(payload);
	for (i = 0; i < 3; i++) a->pos[i] = get_f32(payload + 4 + 4 * i);
	a->radius = get_f32(payload + 16);
	a->height = get_f32(payload + 20);
	a->hp = payload[24];
	a->flags = payload[25];
	return 0;
}

int vo_encode_actor_remove(uint8_t *out, uint32_t seq, uint32_t id)
{
	put_header(out, VO_MSG_ACTOR_REMOVE, seq);
	put_u32(out + 8, id);
	return 12;
}

int vo_decode_actor_remove(const uint8_t *payload, int len, uint32_t *id)
{
	if (len < 4) return -1;
	*id = get_u32(payload);
	return 0;
}

int vo_encode_hit_actor(uint8_t *out, uint32_t seq, uint32_t id, float damage, const float dir[3])
{
	int i;
	put_header(out, VO_MSG_HIT_ACTOR, seq);
	put_u32(out + 8, id);
	put_f32(out + 12, damage);
	for (i = 0; i < 3; i++) put_f32(out + 16 + 4 * i, dir[i]);
	return 28;
}

int vo_encode_player_hurt(uint8_t *out, uint32_t seq, const vo_hurt *h)
{
	int i;
	put_header(out, VO_MSG_PLAYER_HURT, seq);
	put_f32(out + 8, h->damage);
	for (i = 0; i < 3; i++) put_f32(out + 12 + 4 * i, h->dir[i]);
	put_u32(out + 24, h->source);
	return 28;
}

int vo_decode_player_hurt(const uint8_t *payload, int len, vo_hurt *h)
{
	int i;
	if (len < 20) return -1;
	h->damage = get_f32(payload);
	for (i = 0; i < 3; i++) h->dir[i] = get_f32(payload + 4 + 4 * i);
	h->source = get_u32(payload + 16);
	return 0;
}

int vo_encode_input(uint8_t *out, uint32_t seq, const vo_input *in)
{
	put_header(out, VO_MSG_INPUT, seq);
	put_u32(out + 8, in->buttons);
	put_f32(out + 12, in->forward);
	put_f32(out + 16, in->right);
	put_f32(out + 20, in->up);
	put_f32(out + 24, in->yaw);
	put_f32(out + 28, in->pitch);
	put_u32(out + 32, (uint32_t)in->weapon);
	return 36;
}

int vo_decode_input(const uint8_t *payload, int len, vo_input *in)
{
	if (len < 28) return -1;
	in->buttons = get_u32(payload);
	in->forward = get_f32(payload + 4);
	in->right = get_f32(payload + 8);
	in->up = get_f32(payload + 12);
	in->yaw = get_f32(payload + 16);
	in->pitch = get_f32(payload + 20);
	in->weapon = (int)get_u32(payload + 24);
	return 0;
}

int vo_decode(const uint8_t *data, int len, uint32_t *seq_out)
{
	if (len < VO_HEADER_SIZE) return -1;
	if ((data[0] | (data[1] << 8)) != VO_MAGIC || data[2] != VO_VERSION) return -1;
	if (seq_out) *seq_out = get_u32(data + 4);
	return data[3];
}

static void send_to_peer(vo_link *link, const uint8_t *buf, int len)
{
	struct sockaddr_in to;
	memset(&to, 0, sizeof(to));
	to.sin_family = AF_INET;
	to.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
	to.sin_port = htons(link->role == VO_ROLE_WORLD ? VO_PORT_PLAYER : VO_PORT_WORLD);
	sendto(link->sock, buf, len, 0, (struct sockaddr *)&to, sizeof(to));
}

static void send_hello(vo_link *link, uint32_t now)
{
	uint8_t buf[16];
	int n = vo_encode_hello(buf, ++link->seq, link->role, (uint32_t)getpid());
	send_to_peer(link, buf, n);
	link->last_send_ms = now;
}

static void send_heartbeat(vo_link *link, uint32_t now)
{
	uint8_t buf[12];
	int n = vo_encode_heartbeat(buf, ++link->seq, now - link->start_ms);
	send_to_peer(link, buf, n);
	link->last_send_ms = now;
}

void vo_link_send_player_state(vo_link *link, const float pos[3], const float look[3])
{
	uint8_t buf[32];
	int n;
	if (link->sock < 0 || !link->connected) return;
	n = vo_encode_player_state(buf, ++link->seq, pos, look);
	send_to_peer(link, buf, n);
}

void vo_link_send_hit_actor(vo_link *link, uint32_t id, float damage, const float dir[3])
{
	uint8_t buf[28];
	int n;
	if (link->sock < 0 || !link->connected) return;
	n = vo_encode_hit_actor(buf, ++link->seq, id, damage, dir);
	send_to_peer(link, buf, n);
}

int vo_link_open(vo_link *link, int role, vo_log_fn log)
{
	struct sockaddr_in addr;
	memset(link, 0, sizeof(*link));
	link->role = role;
	link->log = log;
	link->timeout_ms = VO_TIMEOUT_MS;
	link->start_ms = now_ms();
	link->last_send_ms = link->start_ms - VO_HEARTBEAT_MS;
	link->sock = socket(AF_INET, SOCK_DGRAM, 0);
	if (link->sock < 0) return -1;
	fcntl(link->sock, F_SETFL, fcntl(link->sock, F_GETFL, 0) | O_NONBLOCK);
	memset(&addr, 0, sizeof(addr));
	addr.sin_family = AF_INET;
	addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
	addr.sin_port = htons(role == VO_ROLE_WORLD ? VO_PORT_WORLD : VO_PORT_PLAYER);
	if (bind(link->sock, (struct sockaddr *)&addr, sizeof(addr)) < 0) {
		close(link->sock);
		link->sock = -1;
		return -1;
	}
	return 0;
}

void vo_link_tick(vo_link *link)
{
	uint8_t buf[2048];
	uint32_t now = now_ms();

	if (link->sock < 0) return;
	for (;;) {
		int n = (int)recv(link->sock, buf, sizeof(buf), 0);
		int type;
		if (n < 0) break;
		type = vo_decode(buf, n, NULL);
		if (type < 0) continue;
		link->last_recv_ms = now;
		if (type != VO_MSG_HELLO && type != VO_MSG_HEARTBEAT && link->on_message)
			link->on_message(link->user, type, buf + VO_HEADER_SIZE, n - VO_HEADER_SIZE);
		if (!link->connected) {
			link->connected = 1;
			if (link->log) link->log("connected");
			if (type == VO_MSG_HELLO) send_hello(link, now);
		}
	}
	if (link->connected && (int)(now - link->last_recv_ms) > link->timeout_ms) {
		link->connected = 0;
		if (link->log) link->log("lost connection");
	}
	if ((int)(now - link->last_send_ms) >= VO_HEARTBEAT_MS) {
		if (link->connected) send_heartbeat(link, now);
		else send_hello(link, now);
	}
}

void vo_link_close(vo_link *link)
{
	if (link->sock >= 0) close(link->sock);
	link->sock = -1;
}
