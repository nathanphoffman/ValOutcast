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
