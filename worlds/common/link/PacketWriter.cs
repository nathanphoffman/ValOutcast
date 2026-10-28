using System;

namespace ValOutcast
{
    /// <summary>Writes a packet field by field, little-endian, so encoders never count byte offsets.</summary>
    internal sealed class PacketWriter
    {
        private readonly byte[] _bytes;
        private int _position;

        public PacketWriter(int size, byte type, uint sequence)
        {
            _bytes = new byte[size];
            BitConverter.GetBytes(VoLink.Magic).CopyTo(_bytes, _position);
            _position += sizeof(ushort);
            _bytes[_position++] = VoLink.Version;
            _bytes[_position++] = type;
            BitConverter.GetBytes(sequence).CopyTo(_bytes, _position);
            _position += sizeof(uint);
        }

        public byte[] Bytes => _bytes;

        public PacketWriter UInt8(byte value)
        {
            _bytes[_position++] = value;

            return this;
        }

        public PacketWriter UInt32(uint value)
        {
            BitConverter.GetBytes(value).CopyTo(_bytes, _position);
            _position += sizeof(uint);

            return this;
        }

        public PacketWriter Int32(int value)
        {
            BitConverter.GetBytes(value).CopyTo(_bytes, _position);
            _position += sizeof(int);

            return this;
        }

        public PacketWriter Int16(short value)
        {
            BitConverter.GetBytes(value).CopyTo(_bytes, _position);
            _position += sizeof(short);

            return this;
        }

        public PacketWriter Float32(float value)
        {
            BitConverter.GetBytes(value).CopyTo(_bytes, _position);
            _position += sizeof(float);

            return this;
        }

        public PacketWriter Vec3(float[] vector)
        {
            foreach (float component in vector) Float32(component);

            return this;
        }

        /// <summary>Leaves the next count bytes zero (messages are padded to a multiple of 4 bytes).</summary>
        public PacketWriter Padding(int count)
        {
            _position += count;

            return this;
        }
    }
}
