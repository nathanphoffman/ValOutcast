using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ValOutcast
{
    public partial class VoLink
    {
        public bool Connected { get; private set; }

        /// <summary>The player side hit a creature. Drained by the game code every frame.</summary>
        public struct HitActorMsg
        {
            public uint ActorId;
            public float Damage;
            public float DirX, DirY, DirZ;
        }

        public readonly Queue<HitActorMsg> HitActors = new Queue<HitActorMsg>();

        /// <summary>The player side pushed or pulled a creature (a force push). Damage holds the strength, 0 to 1; Dir is where the creature should move.</summary>
        public readonly Queue<HitActorMsg> ActorPushes = new Queue<HitActorMsg>();

        /// <summary>The player side used a mind power (mind trick) on a creature. Damage holds the strength, 0 to 1; Dir points from the player to the creature.</summary>
        public readonly Queue<HitActorMsg> ActorCharms = new Queue<HitActorMsg>();
        public readonly Queue<HitActorMsg> ActorGrips = new Queue<HitActorMsg>();
        public readonly Queue<HitActorMsg> ActorHeals = new Queue<HitActorMsg>();

        /// <summary>The player side hit the world itself (a tree, a rock, the ground) rather than a creature. Positions are wire space.</summary>
        public struct HitWorldMsg
        {
            public float PosX, PosY, PosZ;
            public float DirX, DirY, DirZ;
            public float Damage;
            public byte Kind;   // 0 = shot, 1 = lightsaber
        }

        public struct PlayerLightMsg
        {
            public float Red, Green, Blue, Intensity;   // Intensity 0 = off
        }

        /// <summary>Newest PlayerLight from the player side (a lit lightsaber, say).</summary>
        public PlayerLightMsg PlayerLight;
        private long _lastLight;

        /// <summary>True while PlayerLight packets keep arriving (they are resent twice a second), so a dead player side never leaves a light on.</summary>
        public bool PlayerLightFresh => Connected && _clock.ElapsedMilliseconds - _lastLight <= 1500;
        public readonly Queue<HitWorldMsg> HitWorlds = new Queue<HitWorldMsg>();

        /// <summary>Newest PlayerState (wire coordinates), valid when HasPlayerState is true.</summary>
        public float[] StatePos = new float[3];
        public float[] StateLook = new float[3];
        public bool HasPlayerState;

        /// <summary>Goes up by one for every PlayerState that arrives.</summary>
        public int StateSerial;

        /// <summary>True while PlayerState packets are arriving (newer than PlayerStateTimeoutMs).</summary>
        public bool PlayerStateFresh => Connected && HasPlayerState && _clock.ElapsedMilliseconds - _lastState <= PlayerStateTimeoutMs;
    }
}
