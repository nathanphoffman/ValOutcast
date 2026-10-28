/* The connection: open, tick (connect, timeout, heartbeat) and close. See PROTOCOL.md. */
#include "vo_internal.h"

#include <arpa/inet.h>
#include <errno.h>
#include <fcntl.h>
#include <string.h>
#include <sys/socket.h>
#include <time.h>
#include <unistd.h>

static uint32_t now_ms(void)
{
	struct timespec clock_time;
	clock_gettime(CLOCK_MONOTONIC, &clock_time);

	return (uint32_t)(clock_time.tv_sec * MS_PER_SECOND + clock_time.tv_nsec / NS_PER_MS);
}

void send_to_peer(vo_link *link, const uint8_t *buffer, int length)
{
	struct sockaddr_in peer;
	memset(&peer, 0, sizeof(peer));
	peer.sin_family = AF_INET;
	peer.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
	peer.sin_port = htons(link->role == VO_ROLE_WORLD ? VO_PORT_PLAYER : VO_PORT_WORLD);
	sendto(link->sock, buffer, length, 0, (struct sockaddr *)&peer, sizeof(peer));
}

static void send_hello(vo_link *link, uint32_t current_ms)
{
	uint8_t buffer[SIZE_HELLO];
	int length = vo_encode_hello(buffer, ++link->seq, link->role, (uint32_t)getpid());
	send_to_peer(link, buffer, length);
	link->last_send_ms = current_ms;
}

static void send_heartbeat(vo_link *link, uint32_t current_ms)
{
	uint8_t buffer[SIZE_HEARTBEAT];
	int length = vo_encode_heartbeat(buffer, ++link->seq, current_ms - link->start_ms);
	send_to_peer(link, buffer, length);
	link->last_send_ms = current_ms;
}

// Each role binds its own fixed port and sends to the other, so the two games find each other without setup.
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

/* A packet arrived: remember when, pass it on, and treat the first one as "connected".
 * Returns 1 when we should answer with a hello (the first packet was the other side's hello). */
static int handle_packet(vo_link *link, int type, const uint8_t *buffer, int length, uint32_t current_ms)
{
	link->last_recv_ms = current_ms;

	if (type != VO_MSG_HELLO && type != VO_MSG_HEARTBEAT && link->on_message)
		link->on_message(link->user, type, buffer + VO_HEADER_SIZE, length - VO_HEADER_SIZE);

	if (link->connected) return 0;
	link->connected = 1;

	if (link->log) link->log("connected");

	return type == VO_MSG_HELLO;
}

static void drain_socket(vo_link *link, uint32_t current_ms)
{
	uint8_t buffer[SIZE_RECV_BUFFER];

	for (;;) {
		int length = (int)recv(link->sock, buffer, sizeof(buffer), 0);
		int type;

		// A negative read normally means the non-blocking socket has no more packets waiting.
		if (length < 0) {
			if (errno != EAGAIN && errno != EWOULDBLOCK && link->log) link->log("recv failed");

			return;
		}

		type = vo_decode(buffer, length, NULL);

		if (type >= 0) {
			if (handle_packet(link, type, buffer, length, current_ms)) send_hello(link, current_ms);
		} else if (link->log) link->log("dropped malformed packet");
	}
}

/* Silence for timeout_ms means the other game closed or crashed, so go back peer saying hello. */
static void check_timeout(vo_link *link, uint32_t current_ms)
{
	if (!link->connected || (int)(current_ms - link->last_recv_ms) <= link->timeout_ms) return;
	link->connected = 0;

	if (link->log) link->log("lost connection");
}

/* Say hello until the other side answers, then send a heartbeat every VO_HEARTBEAT_MS. */
static void send_when_due(vo_link *link, uint32_t current_ms)
{
	if ((int)(current_ms - link->last_send_ms) < VO_HEARTBEAT_MS) return;

	if (link->connected) send_heartbeat(link, current_ms);
	else send_hello(link, current_ms);
}

// Call every frame: reads all waiting packets, tracks connect/timeout, and sends a hello or heartbeat when due.
void vo_link_tick(vo_link *link)
{
	uint32_t current_ms = now_ms();

	if (link->sock < 0) return;
	drain_socket(link, current_ms);
	check_timeout(link, current_ms);
	send_when_due(link, current_ms);
}

void vo_link_close(vo_link *link)
{
	if (link->sock >= 0) close(link->sock);
	link->sock = -1;
}
