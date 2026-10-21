using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// The Use key (E) on what the crosshair is over: opens doors, picks things up, and so on. Valheim's own handling of the key
    /// is switched off while Outcast drives (it is part of the input Valheim ignores), so it is done here with the same call.
    /// Things that need one of Valheim's menus (chests, crafting stations, signs, traders) are left alone for now.
    /// </summary>
    public class Interactor
    {
        // Player.Interact is private in Valheim.
        private static readonly MethodInfo InteractMethod = AccessTools.Method(typeof(Player), "Interact", new[] { typeof(GameObject), typeof(bool), typeof(bool) });

        public void Update()
        {
            if (!PlayerFollow.Active || InputForwarder.MenuOpen()) return;
            Player player = Player.m_localPlayer;
            if (player == null || !ZInput.GetButtonDown("Use")) return;

            GameObject hover = player.GetHoverObject();
            if (hover == null) return;

            if (NeedsMenu(hover))
            {
                Plugin.LogMessage("use: " + hover.name + " needs one of Valheim's menus, which do not work yet while Outcast drives");
                return;
            }
            if (InteractMethod == null)
            {
                Plugin.LogMessage("use: Player.Interact not found (did the game update?)");
                return;
            }
            InteractMethod.Invoke(player, new object[] { hover, false, false });
            Plugin.LogMessage("use: " + hover.name);
        }

        private static bool NeedsMenu(GameObject go)
        {
            return go.GetComponentInParent<Container>() != null
                || go.GetComponentInParent<CraftingStation>() != null
                || go.GetComponentInParent<Sign>() != null
                || go.GetComponentInParent<Trader>() != null;
        }
    }
}
