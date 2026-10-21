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

        private static bool _anchored;
        private static Vector3 _anchorWire;
        private static Vector3 _anchorWorld;

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
                return;
            }
            var link = Plugin.Link;
            Vector3 wirePos = WireToUnity(link.StatePos);
            if (!_anchored)
            {
                _anchored = true;
                _anchorWire = wirePos;
                _anchorWorld = player.transform.position;
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
