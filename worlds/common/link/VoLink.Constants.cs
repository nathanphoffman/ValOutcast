using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ValOutcast
{
    public partial class VoLink
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
        public const byte MsgActorPush = 15;
        public const byte MsgActorCharm = 16;
        public const byte MsgActorGrip = 17;
        public const byte MsgActorHeal = 18;
        public const byte MsgHitWorld = 12;
        public const byte MsgPlayerHurt = 9;
        public const byte MsgInput = 10;
        public const byte MsgWorldContext = 11;
        public const byte MsgPlayerLight = 13;
        public const byte MsgLoadout = 14;
        public const uint ButtonAttack = 1;
        public const uint ButtonAltAttack = 2;
        public const uint ButtonUse = 4;
        public const int AbilityCount = 7;

        /// <summary>Button bit of ability n (1 to 7): bits 3 to 9.</summary>
        // Ability n is bit n+2 (abilities 1..7 are bits 3..9).
        public static uint ButtonAbility(int abilityNumber) { return 8u << (abilityNumber - 1); }
        public const byte ActorHostile = 1;
        public const byte ActorLoose = 2;
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
    }
}
