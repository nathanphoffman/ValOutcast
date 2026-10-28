using HarmonyLib;

namespace ValOutcast
{
    /// <summary>
    /// Typing the cheat code in Valheim's chat box (Enter, type, Enter) gives the player every Outcast weapon with full ammo
    /// and every force power. The text is swallowed, so it is not sent to other players.
    /// </summary>
    [HarmonyPatch(typeof(Chat), "SendText")]
    internal static class CheatCodePatch
    {
        private const string Code = "valleyofthejedi";

        private static bool Prefix(string text)
        {
            if (text == null || text.Trim().ToLowerInvariant() != Code) return true;

            if (Plugin.Input == null || !PlayerFollow.Active) return true;   // Outcast is not driving: treat it as normal chat
            Plugin.Input.RequestGiveAll();
            Plugin.LogMessage("cheat: valleyofthejedi");

            if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, "The Force is with you");

            return false;
        }
    }
}
