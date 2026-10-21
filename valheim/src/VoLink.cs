using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ValOutcast
{
    /// <summary>ValOutcast protocol v1, C# side. See protocol/PROTOCOL.md. No Unity types, so it can be tested alone.</summary>
    public class VoLink
    {
        public const ushort Magic = 0x4F56;
        public const byte Version = 1;
        public const byte MsgHello = 1;
        public const byte MsgHeartbeat = 2;
        public const byte MsgPlayerState = 3;
        public const int PlayerStateTimeoutMs = 500;
        public const byte MsgCollisionSection = 4;
        public const byte MsgCollisionReset = 5;
        public const byte MsgActorUpsert = 6;
        public const byte MsgActorRemove = 7;
        public const byte MsgHitActor = 8;
        public const byte MsgPlayerHurt = 9;
        public const byte MsgInput = 10;
        public const byte MsgWorldContext = 11;
        public const uint ButtonAttack = 1;
        public const uint ButtonAltAttack = 2;
        public const uint ButtonUse = 4;
        public const byte ActorHostile = 1;
        public const int ChunkCells = 16;
        public const float CellSizeM = 0.5f;
        public const int HeightUnitsPerM = 32;
        public const short NoHeight = short.MinValue;
        public const byte RoleWorld = 0;
        public const byte RolePlayer = 1;
        public const int PortWorld = 47100;
        public const int PortPlayer = 47101;
        public const int HeartbeatMs = 1000;
        public int TimeoutMs = 3000;
        public const int HeaderSize = 8;

        public bool Connected { get; private set; }

        /// <summary>The player side hit a creature. Drained by the game code every frame.</summary>
        public struct HitActorMsg
        {
            public uint Id;
            public float Damage;
            public float DirX, DirY, DirZ;
        }

        public readonly Queue<HitActorMsg> HitActors = new Queue<HitActorMsg>();

        /// <summary>Newest PlayerState (wire coordinates), valid when HasPlayerState is true.</summary>
        public float[] StatePos = new float[3];
        public float[] StateLook = new float[3];
        public bool HasPlayerState;
        /// <summary>Goes up by one for every PlayerState that arrives.</summary>
        public int StateSerial;
        /// <summary>True while PlayerState packets are arriving (newer than PlayerStateTimeoutMs).</summary>
        public bool PlayerStateFresh => Connected && HasPlayerState && _clock.ElapsedMilliseconds - _lastState <= PlayerStateTimeoutMs;

        private readonly byte _role;
        private readonly Action<string> _log;
        private readonly Socket _sock;
        private readonly IPEndPoint _peer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly byte[] _recvBuf = new byte[2048];
        private uint _seq;
        private long _lastSend = -HeartbeatMs;
        private long _lastRecv;
        private long _lastState;

        public VoLink(byte role, Action<string> log)
        {
            _role = role;
            _log = log;
            int myPort = role == RoleWorld ? PortWorld : PortPlayer;
            int peerPort = role == RoleWorld ? PortPlayer : PortWorld;
            _peer = new IPEndPoint(IPAddress.Loopback, peerPort);
            _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _sock.Bind(new IPEndPoint(IPAddress.Loopback, myPort));
            _sock.Blocking = false;
        }

        public static byte[] EncodeHello(uint seq, byte role, uint pid)
        {
            var b = new byte[16];
            WriteHeader(b, MsgHello, seq);
            b[8] = role;
            BitConverter.GetBytes(pid).CopyTo(b, 12);
            return b;
        }

        public static byte[] EncodeHeartbeat(uint seq, uint uptimeMs)
        {
            var b = new byte[12];
            WriteHeader(b, MsgHeartbeat, seq);
            BitConverter.GetBytes(uptimeMs).CopyTo(b, 8);
            return b;
        }

        public static byte[] EncodePlayerState(uint seq, float[] pos, float[] look)
        {
            var b = new byte[32];
            WriteHeader(b, MsgPlayerState, seq);
            for (int i = 0; i < 3; i++)
            {
                BitConverter.GetBytes(pos[i]).CopyTo(b, 8 + 4 * i);
                BitConverter.GetBytes(look[i]).CopyTo(b, 20 + 4 * i);
            }
            return b;
        }

        public static byte[] EncodeCollisionSection(uint seq, int cx, int cz, short[] heights)
        {
            var b = new byte[16 + 512];
            WriteHeader(b, MsgCollisionSection, seq);
            BitConverter.GetBytes(cx).CopyTo(b, 8);
            BitConverter.GetBytes(cz).CopyTo(b, 12);
            for (int i = 0; i < ChunkCells * ChunkCells; i++)
                BitConverter.GetBytes(heights[i]).CopyTo(b, 16 + 2 * i);
            return b;
        }

        public static byte[] EncodeCollisionReset(uint seq)
        {
            var b = new byte[HeaderSize];
            WriteHeader(b, MsgCollisionReset, seq);
            return b;
        }

        /// <summary>Sends one terrain chunk (256 heights). Does nothing if not connected.</summary>
        public void SendCollisionSection(int cx, int cz, short[] heights)
        {
            if (!Connected) return;
            Send(EncodeCollisionSection(++_seq, cx, cz, heights), _clock.ElapsedMilliseconds);
        }

        /// <summary>Tells the player side to forget all chunks. Does nothing if not connected.</summary>
        public void SendCollisionReset()
        {
            if (!Connected) return;
            Send(EncodeCollisionReset(++_seq), _clock.ElapsedMilliseconds);
        }

        public static byte[] EncodeActorUpsert(uint seq, uint id, float[] pos, float radius, float height, byte hp, byte flags)
        {
            var b = new byte[36];
            WriteHeader(b, MsgActorUpsert, seq);
            BitConverter.GetBytes(id).CopyTo(b, 8);
            for (int i = 0; i < 3; i++) BitConverter.GetBytes(pos[i]).CopyTo(b, 12 + 4 * i);
            BitConverter.GetBytes(radius).CopyTo(b, 24);
            BitConverter.GetBytes(height).CopyTo(b, 28);
            b[32] = hp;
            b[33] = flags;
            return b;
        }

        public static byte[] EncodeActorRemove(uint seq, uint id)
        {
            var b = new byte[12];
            WriteHeader(b, MsgActorRemove, seq);
            BitConverter.GetBytes(id).CopyTo(b, 8);
            return b;
        }

        public static byte[] EncodeHitActor(uint seq, uint id, float damage, float[] dir)
        {
            var b = new byte[28];
            WriteHeader(b, MsgHitActor, seq);
            BitConverter.GetBytes(id).CopyTo(b, 8);
            BitConverter.GetBytes(damage).CopyTo(b, 12);
            for (int i = 0; i < 3; i++) BitConverter.GetBytes(dir[i]).CopyTo(b, 16 + 4 * i);
            return b;
        }

        public static byte[] EncodePlayerHurt(uint seq, float damage, float[] dir, uint source)
        {
            var b = new byte[28];
            WriteHeader(b, MsgPlayerHurt, seq);
            BitConverter.GetBytes(damage).CopyTo(b, 8);
            for (int i = 0; i < 3; i++) BitConverter.GetBytes(dir[i]).CopyTo(b, 12 + 4 * i);
            BitConverter.GetBytes(source).CopyTo(b, 24);
            return b;
        }

        public static byte[] EncodeInput(uint seq, uint buttons, float forward, float right, float up, float yaw, float pitch, int weapon = 0)
        {
            var b = new byte[36];
            WriteHeader(b, MsgInput, seq);
            BitConverter.GetBytes(buttons).CopyTo(b, 8);
            BitConverter.GetBytes(forward).CopyTo(b, 12);
            BitConverter.GetBytes(right).CopyTo(b, 16);
            BitConverter.GetBytes(up).CopyTo(b, 20);
            BitConverter.GetBytes(yaw).CopyTo(b, 24);
            BitConverter.GetBytes(pitch).CopyTo(b, 28);
            BitConverter.GetBytes(weapon).CopyTo(b, 32);
            return b;
        }

        public static byte[] EncodeWorldContext(uint seq, float[] sunDir, float[] sunRgb, float[] ambient)
        {
            var b = new byte[44];
            WriteHeader(b, MsgWorldContext, seq);
            for (int i = 0; i < 3; i++)
            {
                BitConverter.GetBytes(sunDir[i]).CopyTo(b, 8 + 4 * i);
                BitConverter.GetBytes(sunRgb[i]).CopyTo(b, 20 + 4 * i);
                BitConverter.GetBytes(ambient[i]).CopyTo(b, 32 + 4 * i);
            }
            return b;
        }

        /// <summary>How the world is lit: direction toward the sun, sun color (brightness applied) and ambient color.</summary>
        public void SendWorldContext(float[] sunDir, float[] sunRgb, float[] ambient)
        {
            if (Connected) SendPacket(EncodeWorldContext(++_seq, sunDir, sunRgb, ambient));
        }

        /// <summary>Reads a HitActor packet. Returns false if it is too short.</summary>
        public static bool DecodeHitActor(byte[] data, int len, out HitActorMsg msg)
        {
            msg = default;
            if (len < 28) return false;
            msg.Id = BitConverter.ToUInt32(data, 8);
            msg.Damage = BitConverter.ToSingle(data, 12);
            msg.DirX = BitConverter.ToSingle(data, 16);
            msg.DirY = BitConverter.ToSingle(data, 20);
            msg.DirZ = BitConverter.ToSingle(data, 24);
            return true;
        }

        private void SendPacket(byte[] packet)
        {
            if (Connected) Send(packet, _clock.ElapsedMilliseconds);
        }

        /// <summary>Position is the creature's feet in wire space (meters). Does nothing if not connected.</summary>
        public void SendActorUpsert(uint id, float[] pos, float radius, float height, byte hp, byte flags)
        {
            if (Connected) SendPacket(EncodeActorUpsert(++_seq, id, pos, radius, height, hp, flags));
        }

        public void SendActorRemove(uint id)
        {
            if (Connected) SendPacket(EncodeActorRemove(++_seq, id));
        }

        public void SendPlayerHurt(float damage, float[] dir, uint source)
        {
            if (Connected) SendPacket(EncodePlayerHurt(++_seq, damage, dir, source));
        }

        /// <summary>weapon: 0 none, 1 to 9 pick that slot, 10 next, 11 previous.</summary>
        public void SendInput(uint buttons, float forward, float right, float up, float yaw, float pitch, int weapon = 0)
        {
            if (Connected) SendPacket(EncodeInput(++_seq, buttons, forward, right, up, yaw, pitch, weapon));
        }

        /// <summary>Reads the 24-byte payload that starts at offset 8.</summary>
        public static void DecodePlayerState(byte[] data, float[] pos, float[] look)
        {
            for (int i = 0; i < 3; i++)
            {
                pos[i] = BitConverter.ToSingle(data, 8 + 4 * i);
                look[i] = BitConverter.ToSingle(data, 20 + 4 * i);
            }
        }

        /// <summary>Returns the message type, or -1 if the packet is not valid.</summary>
        public static int Decode(byte[] data, int len, out uint seq)
        {
            seq = 0;
            if (len < HeaderSize) return -1;
            if ((data[0] | (data[1] << 8)) != Magic || data[2] != Version) return -1;
            seq = BitConverter.ToUInt32(data, 4);
            return data[3];
        }

        private static void WriteHeader(byte[] b, byte type, uint seq)
        {
            b[0] = Magic & 0xFF;
            b[1] = Magic >> 8;
            b[2] = Version;
            b[3] = type;
            BitConverter.GetBytes(seq).CopyTo(b, 4);
        }

        private void Send(byte[] packet, long now)
        {
            try { _sock.SendTo(packet, _peer); } catch (SocketException) { }
            _lastSend = now;
        }

        /// <summary>Call often (every frame is fine). Never blocks.</summary>
        public void Tick()
        {
            long now = _clock.ElapsedMilliseconds;
            while (_sock.Available > 0)
            {
                int n;
                try { n = _sock.Receive(_recvBuf); } catch (SocketException) { break; }
                int type = Decode(_recvBuf, n, out _);
                if (type < 0) continue;
                _lastRecv = now;
                if (type == MsgHitActor && DecodeHitActor(_recvBuf, n, out HitActorMsg hit))
                {
                    if (HitActors.Count < 256) HitActors.Enqueue(hit);
                }
                if (type == MsgPlayerState && n >= 32)
                {
                    DecodePlayerState(_recvBuf, StatePos, StateLook);
                    HasPlayerState = true;
                    StateSerial++;
                    _lastState = now;
                }
                if (!Connected)
                {
                    Connected = true;
                    _log("connected");
                    if (type == MsgHello) Send(EncodeHello(++_seq, _role, (uint)Process.GetCurrentProcess().Id), now);
                }
            }
            if (Connected && now - _lastRecv > TimeoutMs)
            {
                Connected = false;
                _log("lost connection");
            }
            if (now - _lastSend >= HeartbeatMs)
            {
                if (Connected) Send(EncodeHeartbeat(++_seq, (uint)now), now);
                else Send(EncodeHello(++_seq, _role, (uint)Process.GetCurrentProcess().Id), now);
            }
        }

        public void Close()
        {
            _sock.Close();
        }
    }
}
