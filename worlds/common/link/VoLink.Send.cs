using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ValOutcast
{
    public partial class VoLink
    {
        /// <summary>Sends one terrain chunk (256 heights). Does nothing if not connected.</summary>
        public void SendCollisionSection(int chunkX, int chunkZ, short[] heights)
        {
            if (!Connected) return;
            Send(EncodeCollisionSection(++_sequence, chunkX, chunkZ, heights), _clock.ElapsedMilliseconds);
        }

        /// <summary>Tells the player side to forget all chunks. Does nothing if not connected.</summary>
        public void SendCollisionReset()
        {
            if (!Connected) return;
            Send(EncodeCollisionReset(++_sequence), _clock.ElapsedMilliseconds);
        }

        /// <summary>Tells the player side which weapons the player owns (bit n = the player side's weapon number n).</summary>
        public void SendLoadout(uint weapons)
        {
            if (Connected) SendPacket(EncodeLoadout(++_sequence, weapons));
        }

        /// <summary>How the world is lit: direction toward the sun, sun color (brightness applied) and ambient color.</summary>
        public void SendWorldContext(float[] sunDir, float[] sunRgb, float[] ambient)
        {
            if (Connected) SendPacket(EncodeWorldContext(++_sequence, sunDir, sunRgb, ambient));
        }

        private void SendPacket(byte[] packet)
        {
            if (Connected) Send(packet, _clock.ElapsedMilliseconds);
        }

        /// <summary>Position is the creature's feet in wire space (meters). Does nothing if not connected.</summary>
        public void SendActorUpsert(uint actorId, float[] position, float radius, float height, byte hitPoints, byte flags)
        {
            if (Connected) SendPacket(EncodeActorUpsert(++_sequence, actorId, position, radius, height, hitPoints, flags));
        }

        public void SendActorRemove(uint actorId)
        {
            if (Connected) SendPacket(EncodeActorRemove(++_sequence, actorId));
        }

        public void SendPlayerHurt(float damage, float[] direction, uint source)
        {
            if (Connected) SendPacket(EncodePlayerHurt(++_sequence, damage, direction, source));
        }

        /// <summary>weapon: 0 none, 1 to 9 pick that slot, 10 next, 11 previous.</summary>
        public void SendInput(uint buttons, float forward, float right, float upward, float yawDelta, float pitch, int weapon = 0)
        {
            if (Connected) SendPacket(EncodeInput(++_sequence, buttons, forward, right, upward, yawDelta, pitch, weapon));
        }

        private void Send(byte[] packet, long currentMs)
        {
            try { _sock.SendTo(packet, _peer); } catch (SocketException error) { _logger?.Invoke("send failed: " + error.Message); }
            _lastSend = currentMs;
        }
    }
}
