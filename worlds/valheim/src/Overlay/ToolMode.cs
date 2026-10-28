using System;
using HarmonyLib;
using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Tool mode: the player holds one of Valheim's own items (sword, bow, hammer, hoe, torch...) instead of an Outcast weapon.
    /// Outcast puts its weapon away and keeps doing the walking and aiming. Valheim gets the mouse buttons and the hotbar back,
    /// shows its own arms and item, and runs its own combat, building and tools.
    /// </summary>
    public static class ToolMode
    {
        /// <summary>The key that switches between Outcast weapons and Valheim items (a Unity KeyCode name, set in the config file).</summary>
        public static KeyCode Key = KeyCode.R;

        /// <summary>Where the camera sits in tool mode, from the eyes: right, up and forward in meters (negative forward is back).</summary>
        public static Vector3 CameraOffset = Vector3.zero;

        private static bool _on;

        /// <summary>The frame the key was last pressed in. Valheim itself uses R to sheathe and draw weapons: that press must not also do that.</summary>
        public static int ToggledFrame = -10;

        /// <summary>True while Valheim items are in use. Always false while Outcast is not driving.</summary>
        public static bool Active => _on && PlayerFollow.Active;

        public static void Configure(string keyName)
        {
            if (Enum.TryParse(keyName, true, out KeyCode key) && key != KeyCode.None) Key = key;
            else Plugin.LogMessage("tool mode: unknown key '" + keyName + "', using " + Key);
        }

        /// <summary>Call every frame.</summary>
        public static void Update(InputForwarder input)
        {
            if (!PlayerFollow.Active || Player.m_localPlayer == null)
            {
                _on = false;

                return;
            }

            if (InputForwarder.MenuOpen() || !ZInput.GetKeyDown(Key)) return;

            _on = !_on;
            ToggledFrame = Time.frameCount;
            input.RequestWeapon(_on ? 12 : 13);   // tells Outcast to put its weapon away, or bring it back
            ItemDrop.ItemData held = Player.m_localPlayer.GetCurrentWeapon();

            // Log line: shows on/off, and the held item's name when tool mode is on.
            Plugin.LogMessage("tool mode " + (_on ? "on: Valheim items (holding " + (held != null ? held.m_shared.m_name : "nothing") + ")" : "off: Outcast weapons"));
        }
    }

    /// <summary>The key that switches tool mode is R by default, which Valheim also uses to sheathe or draw the weapon. Ignore it in the frame it switches mode.</summary>
    [HarmonyPatch(typeof(Humanoid), "HideHandItems")]
    internal static class ToolModeKeyHidePatch
    {
        private static bool Prefix(Humanoid __instance)
        {
            return !(__instance == Player.m_localPlayer && Mathf.Abs(Time.frameCount - ToolMode.ToggledFrame) <= 1);
        }
    }

    [HarmonyPatch(typeof(Humanoid), "ShowHandItems")]
    internal static class ToolModeKeyShowPatch
    {
        private static bool Prefix(Humanoid __instance)
        {
            return !(__instance == Player.m_localPlayer && Mathf.Abs(Time.frameCount - ToolMode.ToggledFrame) <= 1);
        }
    }

    /// <summary>
    /// In tool mode Valheim's own controls work, except moving: Outcast does the walking, and Valheim running on the spot
    /// would only use up stamina (which swinging a sword needs).
    /// </summary>
    [HarmonyPatch(typeof(Player), "SetControls")]
    internal static class ToolModeControlsPatch
    {
        private static void Prefix(Player __instance, ref Vector3 movedir, ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
        {
            if (__instance != Player.m_localPlayer || !ToolMode.Active) return;
            movedir = Vector3.zero;
            jump = crouch = run = autoRun = dodge = false;
        }
    }
}
