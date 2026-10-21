using System;
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
        private readonly TerrainStreamer _streamer = new TerrainStreamer();
        private readonly CreatureStreamer _creatures = new CreatureStreamer();
        private readonly InputForwarder _input = new InputForwarder();
        private Overlay _overlay;

        // A part that throws is switched off after logging once, so one bug can't flood the log or stop the others.
        private bool _followOk = true, _streamerOk = true, _creaturesOk = true, _inputOk = true, _overlayOk = true;

        private void Awake()
        {
            Logger.LogInfo("ValOutcast hello: plugin loaded");
            // Keep updating the link while the other game's window has focus.
            Application.runInBackground = true;
            try
            {
                Link = new VoLink(VoLink.RoleWorld, msg => Logger.LogInfo("link: " + msg));
            }
            catch (Exception e)
            {
                Logger.LogError("could not open link (port " + VoLink.PortWorld + " in use?): " + e.Message);
            }
            _overlay = new Overlay(msg => Logger.LogInfo(msg));
            try
            {
                _harmony = new Harmony("com.valoutcast.bridge");
                _harmony.PatchAll();
            }
            catch (Exception e)
            {
                Logger.LogError("could not hook into Valheim (did the game update?): " + e);
            }
        }

        private void Run(ref bool ok, string name, Action action)
        {
            if (!ok) return;
            try
            {
                action();
            }
            catch (Exception e)
            {
                ok = false;
                Logger.LogError(name + " switched off after an error: " + e);
            }
        }

        private void Update()
        {
            Link?.Tick();
            Run(ref _followOk, "follow", () => PlayerFollow.Tick(Player.m_localPlayer));
            Run(ref _streamerOk, "terrain", () => _streamer.Update(Link));
            Run(ref _creaturesOk, "creatures", () => _creatures.Update(Link));
            Run(ref _inputOk, "input", () => _input.Update(Link));
            Run(ref _overlayOk, "overlay", () => _overlay.Update());
        }

        private void OnGUI()
        {
            if (_overlayOk) _overlay?.Draw();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _overlay?.Close();
            Link?.Close();
            Link = null;
        }
    }
}
