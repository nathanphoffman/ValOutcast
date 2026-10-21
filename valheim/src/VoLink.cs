using System;
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
        public const byte RoleWorld = 0;
        public const byte RolePlayer = 1;
        public const int PortWorld = 47100;
        public const int PortPlayer = 47101;
        public const int HeartbeatMs = 1000;
        public int TimeoutMs = 3000;
        public const int HeaderSize = 8;

        public bool Connected { get; private set; }

        /// <summary>Newest PlayerState (wire coordinates), valid when HasPlayerState is true.</summary>
        public float[] StatePos = new float[3];
        public float[] StateLook = new float[3];
        public bool HasPlayerState;
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
                if (type == MsgPlayerState && n >= 32)
                {
                    DecodePlayerState(_recvBuf, StatePos, StateLook);
                    HasPlayerState = true;
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
