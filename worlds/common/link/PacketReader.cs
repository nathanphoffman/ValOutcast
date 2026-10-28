using System;

namespace ValOutcast
{
    /// <summary>Reads a packet field by field, little-endian, starting just after the header.</summary>
    internal sealed class PacketReader
    {
        private readonly byte[] _bytes;
        private int _position;

        public PacketReader(byte[] bytes)
        {
            _bytes = bytes;
            _position = VoLink.HeaderSize;
        }

        public byte ReadUInt8() => _bytes[_position++];

        public uint ReadUInt32()
        {
            uint value = BitConverter.ToUInt32(_bytes, _position);
            _position += sizeof(uint);

            return value;
        }

        public float ReadFloat32()
        {
            float value = BitConverter.ToSingle(_bytes, _position);
            _position += sizeof(float);

            return value;
        }

        public void ReadVector3(float[] vector)
        {
            for (int axis = 0; axis < vector.Length; axis++) vector[axis] = ReadFloat32();
        }
    }
}
