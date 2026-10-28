/* Little-endian helpers every packet encoder and decoder uses. See PROTOCOL.md. */
#include "vo_internal.h"

#include <string.h>

uint8_t *write_u8(uint8_t *cursor, uint8_t value)
{
	*cursor = value;

	return cursor + sizeof(uint8_t);
}

uint8_t *write_u16(uint8_t *cursor, uint16_t value)
{
	// Little-endian: low byte first, then the high byte shifted down.
	cursor[0] = value & BYTE_MASK;
	cursor[1] = value >> BITS_PER_BYTE;   // high byte

	return cursor + sizeof(uint16_t);
}

uint8_t *write_u32(uint8_t *cursor, uint32_t value)
{
	// Little-endian: byte i is the value shifted right by 8 * i bits, then cut to its low 8 bits.
	cursor[0] = value & BYTE_MASK;
	cursor[1] = (value >> BITS_PER_BYTE) & BYTE_MASK;   // second byte
	cursor[2] = (value >> (2 * BITS_PER_BYTE)) & BYTE_MASK;   // third byte
	cursor[3] = value >> (3 * BITS_PER_BYTE);   // fourth (highest) byte

	return cursor + sizeof(uint32_t);
}

/* A float travels as its 32 raw bits. */
static uint32_t float_bits(float value)
{
	uint32_t bits;
	memcpy(&bits, &value, sizeof(bits));

	return bits;
}

uint8_t *write_f32(uint8_t *cursor, float value)
{
	return write_u32(cursor, float_bits(value));
}

uint8_t *write_vec3(uint8_t *cursor, const float vec[VO_VEC3])
{
	int axis;

	for (axis = 0; axis < VO_VEC3; axis++) cursor = write_u32(cursor, float_bits(vec[axis]));

	return cursor;
}

uint8_t *write_padding(uint8_t *cursor, int count)
{
	memset(cursor, 0, count);

	return cursor + count;
}

uint8_t *write_header(uint8_t *packet, int type, uint32_t sequence)
{
	int byte_index;

	// Written without calling the other writers, so every encoder is one call shallower.
	packet[0] = VO_MAGIC & BYTE_MASK;
	packet[1] = VO_MAGIC >> BITS_PER_BYTE;   // high byte
	packet[HEADER_VERSION_AT] = VO_VERSION;
	packet[HEADER_TYPE_AT] = (uint8_t)type;

	for (byte_index = 0; byte_index < (int)sizeof(sequence); byte_index++)
		packet[HEADER_SEQUENCE_AT + byte_index] = (sequence >> (byte_index * BITS_PER_BYTE)) & BYTE_MASK;   // little-endian

	return packet + VO_HEADER_SIZE;
}

const uint8_t *read_u8(const uint8_t *cursor, uint8_t *value)
{
	*value = *cursor;

	return cursor + sizeof(uint8_t);
}

const uint8_t *read_u16(const uint8_t *cursor, uint16_t *value)
{
	// Rebuild the value: the second byte is the high byte, shifted back up.
	*value = (uint16_t)(cursor[0] | (cursor[1] << BITS_PER_BYTE));

	return cursor + sizeof(uint16_t);
}

const uint8_t *read_u32(const uint8_t *cursor, uint32_t *value)
{
	// Rebuild the value: each byte is shifted back up by 8 * its position.
	*value = (uint32_t)cursor[0] | ((uint32_t)cursor[1] << BITS_PER_BYTE)
		| ((uint32_t)cursor[2] << (2 * BITS_PER_BYTE)) | ((uint32_t)cursor[3] << (3 * BITS_PER_BYTE));

	return cursor + sizeof(uint32_t);
}

const uint8_t *read_f32(const uint8_t *cursor, float *value)
{
	uint32_t bits;
	cursor = read_u32(cursor, &bits);
	memcpy(value, &bits, sizeof(*value));

	return cursor;
}

const uint8_t *read_vec3(const uint8_t *cursor, float vec[VO_VEC3])
{
	int axis;

	for (axis = 0; axis < VO_VEC3; axis++) cursor = read_f32(cursor, &vec[axis]);

	return cursor;
}
