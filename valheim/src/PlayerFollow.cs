using HarmonyLib;
using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Makes the local Valheim player copy the PlayerState sent by the player side (Outcast).
    /// The first position received is anchored to wherever the Valheim player stands,
    /// so only movement matters, not the absolute coordinates of the other game's map.
    /// </summary>
    public static class PlayerFollow
    {
        private static readonly AccessTools.FieldRef<Character, Rigidbody> BodyRef =
            AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");

        private const float TeleportMeters = 10f;

        private static bool _anchored;
        private static bool _hasLast;
        private static Vector3 _lastWire;
        private static Vector3 _anchorWire;
        private static Vector3 _anchorWorld;

        /// <summary>True once the first position has been matched to the Valheim player.</summary>
        public static bool Anchored => _anchored;

        /// <summary>Goes up by one every time the mapping between the two games changes.</summary>
        public static int AnchorGeneration { get; private set; }

        /// <summary>Valheim world position to the player side's wire coordinates (meters, Y up, -Z forward).</summary>
        public static Vector3 WorldToWire(Vector3 world)
        {
            Vector3 u = _anchorWire + (world - _anchorWorld);
            return new Vector3(u.x, u.y, -u.z);
        }

        /// <summary>Wire X and Z of a point to the Valheim world X and Z (Y is left at 0).</summary>
        public static Vector3 WireToWorldXZ(float wireX, float wireZ)
        {
            Vector3 u = new Vector3(wireX, 0f, -wireZ);
            Vector3 w = _anchorWorld + (u - _anchorWire);
            return new Vector3(w.x, 0f, w.z);
        }

        /// <summary>Valheim height to wire height.</summary>
        public static float WorldYToWireY(float worldY)
        {
            return _anchorWire.y + (worldY - _anchorWorld.y);
        }

        /// <summary>True while another game is driving the player.</summary>
        public static bool Active => Plugin.Link != null && Plugin.Link.PlayerStateFresh;

        // Wire is right-handed Y-up (-Z forward). Unity is left-handed Y-up (+Z forward): flip Z.
        private static Vector3 WireToUnity(float[] v)
        {
            return new Vector3(v[0], v[1], -v[2]);
        }

        public static void Apply(Player player)
        {
            if (!Active)
            {
                _anchored = false;
                _hasLast = false;
                return;
            }
            var link = Plugin.Link;
            Vector3 wirePos = WireToUnity(link.StatePos);
            if (_hasLast && (wirePos - _lastWire).magnitude > TeleportMeters)
            {
                _anchored = false; // the other game teleported its player: stay put and re-anchor
            }
            _hasLast = true;
            _lastWire = wirePos;
            if (!_anchored)
            {
                _anchored = true;
                _anchorWire = wirePos;
                _anchorWorld = player.transform.position;
                // Anchor to the real ground under the Valheim player, not to wherever the body is right now
                // (it may be falling in or standing on something). The other game's feet then start on the ground.
                if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(_anchorWorld, out float groundY))
                {
                    _anchorWorld.y = groundY;
                }
                AnchorGeneration++;
            }

            Vector3 target = _anchorWorld + (wirePos - _anchorWire);
            Rigidbody body = BodyRef(player);
            body.position = target;
            body.linearVelocity = Vector3.zero;
            player.transform.position = target;

            Vector3 look = WireToUnity(link.StateLook);
            if (look.sqrMagnitude > 0.0001f)
            {
                player.SetLookDir(look);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "FixedUpdate")]
    internal static class PlayerFixedUpdatePatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
            {
                PlayerFollow.Apply(__instance);
            }
        }
    }

    /// <summary>Ignore the keyboard and mouse while another game drives the player.</summary>
    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class PlayerControllerTakeInputPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (PlayerFollow.Active)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
