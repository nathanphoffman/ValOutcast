using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ValOutcast
{
    [BepInPlugin("com.valoutcast.bridge", "ValOutcast", "0.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        public static VoLink Link;
        private Harmony _harmony;

        private void Awake()
        {
            Logger.LogInfo("ValOutcast hello: plugin loaded");
            // Keep updating the link while the other game's window has focus.
            Application.runInBackground = true;
            try
            {
                Link = new VoLink(VoLink.RoleWorld, msg => Logger.LogInfo("link: " + msg));
            }
            catch (System.Exception e)
            {
                Logger.LogError("could not open link (port " + VoLink.PortWorld + " in use?): " + e.Message);
            }
            _harmony = new Harmony("com.valoutcast.bridge");
            _harmony.PatchAll();
        }

        private void Update()
        {
            Link?.Tick();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Link?.Close();
            Link = null;
        }
    }
}
