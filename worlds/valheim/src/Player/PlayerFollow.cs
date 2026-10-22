using HarmonyLib;
using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Makes the local Valheim player copy the PlayerState sent by the player side (Outcast).
    /// The first position received is anchored to the ground under the Valheim player,
    /// so only movement matters, not the absolute coordinates of the other game's map.
    /// Outcast sends about 20 positions a second, so the movement is smoothed between them.
    /// </summary>
    public static class PlayerFollow
    {
        private const float TeleportMeters = 10f;

        private static readonly AccessTools.FieldRef<Character, Rigidbody> BodyRef =
            AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        private static readonly AccessTools.FieldRef<Character, float> MaxAirAltitudeRef =
            AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");
        private static readonly AccessTools.FieldRef<Character, GameObject> VisualRef =
            AccessTools.FieldRefAccess<Character, GameObject>("m_visual");

        private static bool _anchored;
        private static Vector3 _anchorWire;
        private static Vector3 _anchorWorld;

        // The two newest samples (Valheim world position and look direction) and when the newest arrived.
        private static bool _hasSample;
        private static int _serial = -1;
        private static Vector3 _prevPos, _latestPos, _prevLook, _latestLook;
        private static float _latestTime;
        private static float _interval = 0.05f;
        private static Vector3 _wireOfLatest;

        private static Vector3 _currentPos;
        private static Vector3 _currentLook = Vector3.forward;
        private static bool _hidden;

        /// <summary>
        /// True while every Valheim frame gets its own Outcast frame (see Overlay). The newest position is then exact for this
        /// frame, so it is used as it is instead of being smoothed between two older ones.
        /// </summary>
        public static bool Lockstep;

        /// <summary>True once the first position has been matched to the Valheim player.</summary>
        public static bool Anchored => _anchored;

        /// <summary>Goes up by one every time the mapping between the two games changes.</summary>
        public static int AnchorGeneration { get; private set; }

        /// <summary>Real time of the last time the mapping changed.</summary>
        public static float AnchoredAt { get; private set; }

        /// <summary>Where the Valheim player is right now (smoothed), and where it looks.</summary>
        public static bool HasCurrent => _hasSample;
        public static Vector3 CurrentPosition => _currentPos;
        public static Vector3 CurrentLook => _currentLook;

        /// <summary>Valheim world position to the player side's wire coordinates (meters, Y up, -Z forward).</summary>
        public static Vector3 WorldToWire(Vector3 world)
        {
            Vector3 u = _anchorWire + (world - _anchorWorld);
            return new Vector3(u.x, u.y, -u.z);
        }

        /// <summary>A point in the player side's wire coordinates to the Valheim world.</summary>
        public static Vector3 WireToWorld(Vector3 wire)
        {
            Vector3 u = new Vector3(wire.x, wire.y, -wire.z);
            return _anchorWorld + (u - _anchorWire);
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

        private static void ShowBody(Player player, bool show)
        {
            if (player == null) return;
            GameObject visual = VisualRef(player);
            if (visual != null && visual.activeSelf != show) visual.SetActive(show);
            _hidden = !show;
        }

        /// <summary>Call every frame.</summary>
        public static void Tick(Player player)
        {
            if (player == null) return;
            if (!Active)
            {
                _anchored = false;
                _hasSample = false;
                _serial = -1;
                if (_hidden) ShowBody(player, true);
                return;
            }

            VoLink link = Plugin.Link;
            if (link.StateSerial != _serial)
            {
                _serial = link.StateSerial;
                AddSample(link, player);
            }
            if (!_hasSample) return;

            // Show the newest two samples one after the other, so movement is smooth even though they arrive 20 times a second.
            float t = Lockstep ? 1f : Mathf.Clamp01((Time.unscaledTime - _latestTime) / _interval);
            _currentPos = Vector3.Lerp(_prevPos, _latestPos, t);
            _currentLook = Vector3.Slerp(_prevLook, _latestLook, t);

            Place(player);
            // Outcast draws the hands, so Valheim's own body would only get in the way. In tool mode it is Valheim's arms and item that are used.
            if (_hidden == ToolMode.Active) ShowBody(player, ToolMode.Active);
        }

        private static void AddSample(VoLink link, Player player)
        {
            Vector3 wirePos = WireToUnity(link.StatePos);
            Vector3 look = WireToUnity(link.StateLook);
            if (look.sqrMagnitude < 0.0001f) look = _latestLook.sqrMagnitude > 0f ? _latestLook : Vector3.forward;
            look.Normalize();

            bool teleported = _hasSample && (wirePos - _wireOfLatest).magnitude > TeleportMeters;
            if (teleported) _anchored = false;     // the other game teleported its player: stay put and re-anchor

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
                AnchoredAt = Time.unscaledTime;
                _hasSample = false;
            }

            Vector3 world = _anchorWorld + (wirePos - _anchorWire);
            float now = Time.unscaledTime;
            if (!_hasSample)
            {
                _prevPos = _latestPos = world;
                _prevLook = _latestLook = look;
                _hasSample = true;
            }
            else
            {
                _prevPos = _currentPos;      // continue from where the picture is now, so nothing jumps
                _prevLook = _currentLook;
                _latestPos = world;
                _latestLook = look;
                _interval = Mathf.Lerp(_interval, Mathf.Clamp(now - _latestTime, 0.015f, 0.1f), 0.2f);
            }
            _latestTime = now;
            _wireOfLatest = wirePos;
        }

        private static void Place(Player player)
        {
            Rigidbody body = BodyRef(player);
            body.position = _currentPos;
            body.linearVelocity = Vector3.zero;
            // The body is moved around by the other game, so Valheim must not count that as falling (fall damage).
            MaxAirAltitudeRef(player) = _currentPos.y;
            player.transform.position = _currentPos;
            if (_currentLook.sqrMagnitude > 0.0001f) player.SetLookDir(_currentLook);
        }

        /// <summary>Called after Valheim's own physics step, to put the player back where it should be.</summary>
        public static void ApplyAfterPhysics(Player player)
        {
            if (Active && _hasSample) Place(player);
        }
    }

    [HarmonyPatch(typeof(Player), "FixedUpdate")]
    internal static class PlayerFixedUpdatePatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
            {
                PlayerFollow.ApplyAfterPhysics(__instance);
            }
        }
    }

    /// <summary>
    /// While another game drives the player, Valheim's own actions (hotbar, build, use) are off too.
    /// The keys go to the other game instead.
    /// </summary>
    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class PlayerTakeInputPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (PlayerFollow.Active && !ToolMode.Active)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>Ignore the keyboard and mouse while another game drives the player.</summary>
    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class PlayerControllerTakeInputPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (PlayerFollow.Active && !ToolMode.Active)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// While Outcast drives, the camera sits exactly at the player's eyes (first person), whatever distance
    /// or tilt rules Valheim would normally apply.
    /// </summary>
    [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
    internal static class GameCameraPatch
    {
        // Outcast walks on Valheim's ground as 0.5 m columns, so on a slope the player climbs tiny steps and the camera shakes.
        // The camera's height is eased over a fraction of a second to hide that; big changes (a real fall or a teleport) are followed at once.
        private const float HeightEaseSeconds = 0.07f;
        private const float HeightSnapMeters = 1.5f;
        private static float _smoothY;
        private static bool _hasSmoothY;

        private static float SmoothHeight(float y)
        {
            if (!_hasSmoothY || Mathf.Abs(y - _smoothY) > HeightSnapMeters) _smoothY = y;
            else _smoothY += (y - _smoothY) * (1f - Mathf.Exp(-Time.unscaledDeltaTime / HeightEaseSeconds));
            _hasSmoothY = true;
            return _smoothY;
        }

        private static void Postfix(GameCamera __instance)
        {
            Player player = Player.m_localPlayer;
            if (player == null || !PlayerFollow.Active || !PlayerFollow.HasCurrent)
            {
                _hasSmoothY = false;
                HeldItemView.Clear();
                SaberGlow.Clear();
                return;
            }
            if (player.m_eye == null || PlayerFollow.CurrentLook.sqrMagnitude < 0.0001f) return;

            Vector3 eyeHeight = player.m_eye.position - player.transform.position;
            Quaternion rotation = Quaternion.LookRotation(PlayerFollow.CurrentLook, Vector3.up);
            Vector3 position = PlayerFollow.CurrentPosition + eyeHeight;
            position.y = SmoothHeight(position.y);
            // Optional: in tool mode the camera can sit a little away from the eyes (see ToolMode.CameraOffset). The held item itself is
            // shown by HeldItemView.
            if (ToolMode.Active) position += rotation * ToolMode.CameraOffset;
            __instance.transform.position = position;
            __instance.transform.rotation = rotation;
            SaberGlow.Update(Plugin.Link, __instance.transform);
            if (ToolMode.Active) HeldItemView.Update(player, __instance.transform);
            else HeldItemView.Clear();
        }
    }
}
