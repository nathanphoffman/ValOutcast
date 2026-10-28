using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ValOutcast
{
    /// <summary>ValOutcast protocol v1, C# side. See protocol/PROTOCOL.md. No Unity types, so it can be tested alone.</summary>
    public partial class VoLink
    {
        private readonly byte _role;
        private readonly Action<string> _logger;
        private readonly Socket _sock;
        private readonly IPEndPoint _peer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly byte[] _recvBuf = new byte[2048];
        private uint _sequence;
        private long _lastSend = -HeartbeatMs;
        private long _lastRecv;
        private long _lastState;

        public VoLink(byte role, Action<string> logger)
        {
            _role = role;
            _logger = logger;
            int myPort = role == RoleWorld ? PortWorld : PortPlayer;
            int peerPort = role == RoleWorld ? PortPlayer : PortWorld;
            _peer = new IPEndPoint(IPAddress.Loopback, peerPort);
            _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _sock.Bind(new IPEndPoint(IPAddress.Loopback, myPort));
            _sock.Blocking = false;
        }

        private static uint ProcessId => (uint)Process.GetCurrentProcess().Id;
        private const int MaxQueued = 256;

        private static void EnqueueBounded<T>(Queue<T> queue, T item)
        {
            if (queue.Count < MaxQueued) queue.Enqueue(item);
        }

        // Each creature-targeted message type has its own queue for the game code to drain.
        private Queue<HitActorMsg> ActorQueueFor(int type)
        {
            switch (type)
            {
                case MsgHitActor: return HitActors;

                case MsgActorPush: return ActorPushes;

                case MsgActorCharm: return ActorCharms;

                case MsgActorGrip: return ActorGrips;

                case MsgActorHeal: return ActorHeals;

                default: return null;
            }
        }

        // Creature messages are queued for the game to drain; player state and light only keep the newest.
        private void StoreMessage(int type, int length, long currentMs)
        {
            Queue<HitActorMsg> actorQueue = ActorQueueFor(type);

            if (actorQueue != null)
            {
                if (DecodeHitActor(_recvBuf, length, out HitActorMsg actorMsg)) EnqueueBounded(actorQueue, actorMsg);
            }
            else if (type == MsgHitWorld)
            {
                if (DecodeHitWorld(_recvBuf, length, out HitWorldMsg worldMsg)) EnqueueBounded(HitWorlds, worldMsg);
            }
            else if (type == MsgPlayerLight && DecodePlayerLight(_recvBuf, length, out PlayerLightMsg light))
            {
                PlayerLight = light;
                _lastLight = currentMs;
            }
            else if (type == MsgPlayerState && length >= SizePlayerState)
            {
                DecodePlayerState(_recvBuf, StatePos, StateLook);
                HasPlayerState = true;
                StateSerial++;
                _lastState = currentMs;
            }
        }

        // Read until the socket is empty so one slow frame never leaves old packets queued.
        private void DrainSocket(long currentMs)
        {
            while (_sock.Available > 0)
            {
                int length;

                try { length = _sock.Receive(_recvBuf); } catch (SocketException error) { _logger?.Invoke("receive failed: " + error.Message); return; }
                int type = Decode(_recvBuf, length, out _);

                if (type < 0) continue;
                _lastRecv = currentMs;
                StoreMessage(type, length, currentMs);

                if (Connected) continue;
                Connected = true;
                _logger("connected");

                if (type == MsgHello) Send(EncodeHello(++_sequence, _role, ProcessId), currentMs);
            }
        }

        private void CheckTimeout(long currentMs)
        {
            if (!Connected || currentMs - _lastRecv <= TimeoutMs) return;
            Connected = false;
            _logger("lost connection");
        }

        // Say hello until the other side answers, then send a heartbeat every HeartbeatMs.
        private void SendWhenDue(long currentMs)
        {
            if (currentMs - _lastSend < HeartbeatMs) return;

            if (Connected) Send(EncodeHeartbeat(++_sequence, (uint)currentMs), currentMs);
            else Send(EncodeHello(++_sequence, _role, ProcessId), currentMs);
        }

        /// <summary>Call often (every frame is fine). Never blocks.</summary>
        public void Tick()
        {
            long currentMs = _clock.ElapsedMilliseconds;
            DrainSocket(currentMs);
            CheckTimeout(currentMs);
            SendWhenDue(currentMs);
        }

        public void Close()
        {
            _sock.Close();
        }
    }
}
