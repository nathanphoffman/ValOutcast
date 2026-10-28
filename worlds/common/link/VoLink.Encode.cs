using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ValOutcast
{
    public partial class VoLink
    {
        // Packet sizes in bytes, header included.
        private const int SizeHello = 16;
        private const int SizeHeartbeat = 12;
        private const int SizePlayerState = 32;
        private const int SizeCollisionSection = 528;
        private const int SizeActorUpsert = 36;
        private const int SizeActorRemove = 12;
        private const int SizeHitActor = 28;
        private const int SizeHitWorld = 40;
        private const int SizePlayerHurt = 28;
        private const int SizeInput = 36;
        private const int SizeLoadout = 12;
        private const int SizePlayerLight = 24;
        private const int SizeWorldContext = 44;

        // Header layout: magic (2 bytes), version, type, sequence (4 bytes).
        private const int VersionIndex = sizeof(ushort);
        private const int TypeIndex = VersionIndex + sizeof(byte);
        private const int SequenceIndex = TypeIndex + sizeof(byte);

        // Padding bytes that round a message up to a multiple of 4 bytes.
        private const int HelloPadding = 3;
        private const int ActorUpsertPadding = 2;
        private const int HitWorldPadding = 3;

        public static byte[] EncodeHello(uint sequence, byte role, uint processId)
        {
            return new PacketWriter(SizeHello, MsgHello, sequence).UInt8(role).Padding(HelloPadding).UInt32(processId).Bytes;
        }

        public static byte[] EncodeHeartbeat(uint sequence, uint uptimeMs)
        {
            return new PacketWriter(SizeHeartbeat, MsgHeartbeat, sequence).UInt32(uptimeMs).Bytes;
        }

        public static byte[] EncodePlayerState(uint sequence, float[] position, float[] look)
        {
            return new PacketWriter(SizePlayerState, MsgPlayerState, sequence).Vec3(position).Vec3(look).Bytes;
        }

        public static byte[] EncodeCollisionSection(uint sequence, int chunkX, int chunkZ, short[] heights)
        {
            var writer = new PacketWriter(SizeCollisionSection, MsgCollisionSection, sequence).Int32(chunkX).Int32(chunkZ);

            for (int cell = 0; cell < ChunkCells * ChunkCells; cell++) writer.Int16(heights[cell]);

            return writer.Bytes;
        }

        public static byte[] EncodeCollisionReset(uint sequence)
        {
            return new PacketWriter(HeaderSize, MsgCollisionReset, sequence).Bytes;
        }

        public static byte[] EncodeActorUpsert(uint sequence, uint actorId, float[] position, float radius, float height, byte hitPoints, byte flags)
        {
            return new PacketWriter(SizeActorUpsert, MsgActorUpsert, sequence)
                .UInt32(actorId).Vec3(position).Float32(radius).Float32(height).UInt8(hitPoints).UInt8(flags).Padding(ActorUpsertPadding).Bytes;
        }

        public static byte[] EncodeActorRemove(uint sequence, uint actorId)
        {
            return new PacketWriter(SizeActorRemove, MsgActorRemove, sequence).UInt32(actorId).Bytes;
        }

        // Hits and force powers share one layout (actor, amount, direction); only the message type differs.
        private static byte[] EncodeActorMessage(byte type, uint sequence, uint actorId, float amount, float[] direction)
        {
            return new PacketWriter(SizeHitActor, type, sequence).UInt32(actorId).Float32(amount).Vec3(direction).Bytes;
        }

        public static byte[] EncodeHitActor(uint sequence, uint actorId, float damage, float[] direction)
        {
            return EncodeActorMessage(MsgHitActor, sequence, actorId, damage, direction);
        }

        public static byte[] EncodeActorPush(uint sequence, uint actorId, float strength, float[] direction)
        {
            return EncodeActorMessage(MsgActorPush, sequence, actorId, strength, direction);
        }

        public static byte[] EncodeActorCharm(uint sequence, uint actorId, float strength, float[] direction)
        {
            return EncodeActorMessage(MsgActorCharm, sequence, actorId, strength, direction);
        }

        public static byte[] EncodeActorGrip(uint sequence, uint actorId, float strength, float[] direction)
        {
            return EncodeActorMessage(MsgActorGrip, sequence, actorId, strength, direction);
        }

        public static byte[] EncodeActorHeal(uint sequence, uint actorId, float strength, float[] direction)
        {
            return EncodeActorMessage(MsgActorHeal, sequence, actorId, strength, direction);
        }

        public static byte[] EncodeHitWorld(uint sequence, float[] position, float[] direction, float damage, byte kind)
        {
            return new PacketWriter(SizeHitWorld, MsgHitWorld, sequence)
                .Vec3(position).Vec3(direction).Float32(damage).UInt8(kind).Padding(HitWorldPadding).Bytes;
        }

        public static byte[] EncodePlayerHurt(uint sequence, float damage, float[] direction, uint source)
        {
            return new PacketWriter(SizePlayerHurt, MsgPlayerHurt, sequence).Float32(damage).Vec3(direction).UInt32(source).Bytes;
        }

        public static byte[] EncodeInput(uint sequence, uint buttons, float forward, float right, float upward, float yawDelta, float pitch, int weapon = 0)
        {
            return new PacketWriter(SizeInput, MsgInput, sequence)
                .UInt32(buttons).Float32(forward).Float32(right).Float32(upward).Float32(yawDelta).Float32(pitch).Int32(weapon).Bytes;
        }

        public static byte[] EncodeLoadout(uint sequence, uint weapons)
        {
            return new PacketWriter(SizeLoadout, MsgLoadout, sequence).UInt32(weapons).Bytes;
        }

        public static byte[] EncodePlayerLight(uint sequence, float[] color, float intensity)
        {
            return new PacketWriter(SizePlayerLight, MsgPlayerLight, sequence).Vec3(color).Float32(intensity).Bytes;
        }

        public static byte[] EncodeWorldContext(uint sequence, float[] sunDirection, float[] sunColor, float[] ambient)
        {
            return new PacketWriter(SizeWorldContext, MsgWorldContext, sequence).Vec3(sunDirection).Vec3(sunColor).Vec3(ambient).Bytes;
        }

        /// <summary>Reads a PlayerLight packet. Returns false if it is too short.</summary>
        public static bool DecodePlayerLight(byte[] data, int length, out PlayerLightMsg message)
        {
            message = default;

            if (length < SizePlayerLight) return false;
            var reader = new PacketReader(data);
            message.Red = reader.ReadFloat32();
            message.Green = reader.ReadFloat32();
            message.Blue = reader.ReadFloat32();
            message.Intensity = reader.ReadFloat32();

            return true;
        }

        /// <summary>Reads a HitActor packet. Returns false if it is too short.</summary>
        public static bool DecodeHitActor(byte[] data, int length, out HitActorMsg message)
        {
            message = default;

            if (length < SizeHitActor) return false;
            var reader = new PacketReader(data);
            message.ActorId = reader.ReadUInt32();
            message.Damage = reader.ReadFloat32();
            message.DirX = reader.ReadFloat32();
            message.DirY = reader.ReadFloat32();
            message.DirZ = reader.ReadFloat32();

            return true;
        }

        /// <summary>Reads a HitWorld packet. Returns false if it is too short.</summary>
        public static bool DecodeHitWorld(byte[] data, int length, out HitWorldMsg message)
        {
            message = default;

            if (length < SizeHitWorld) return false;
            var reader = new PacketReader(data);
            message.PosX = reader.ReadFloat32();
            message.PosY = reader.ReadFloat32();
            message.PosZ = reader.ReadFloat32();
            message.DirX = reader.ReadFloat32();
            message.DirY = reader.ReadFloat32();
            message.DirZ = reader.ReadFloat32();
            message.Damage = reader.ReadFloat32();
            message.Kind = reader.ReadUInt8();

            return true;
        }

        /// <summary>Reads the position and look direction that follow the header.</summary>
        public static void DecodePlayerState(byte[] data, float[] position, float[] look)
        {

            for (int axis = 0; axis < position.Length; axis++)
            {
                position[axis] = BitConverter.ToSingle(data, HeaderSize + axis * sizeof(float));
                look[axis] = BitConverter.ToSingle(data, HeaderSize + (position.Length + axis) * sizeof(float));
            }
        }

        /// <summary>Returns the message type, or -1 if the packet is not valid.</summary>
        public static int Decode(byte[] data, int length, out uint sequence)
        {
            sequence = 0;

            if (length < HeaderSize) return -1;

            if (BitConverter.ToUInt16(data, 0) != Magic || data[VersionIndex] != Version) return -1;
            sequence = BitConverter.ToUInt32(data, SequenceIndex);

            return data[TypeIndex];
        }
    }
}
