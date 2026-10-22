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
        public static Action<string> LogMessage = _ => { };

        /// <summary>Degrees of turning per mouse movement (on top of Valheim's own mouse sensitivity setting).</summary>
        public static float MouseDegreesPerCount = 1f;
        /// <summary>1 = the picture as Outcast drew it, lower is dimmer.</summary>
        public static float OverlayBrightness = 0.85f;
        private Harmony _harmony;
        private readonly TerrainStreamer _streamer = new TerrainStreamer();
        private readonly CreatureStreamer _creatures = new CreatureStreamer();
        private readonly InputForwarder _input = new InputForwarder();
        private Overlay _overlay;
        private readonly HudHider _hud = new HudHider();
        private readonly WorldLight _light = new WorldLight();
        private readonly TestSpawner _spawner = new TestSpawner();
        private readonly WorldHits _worldHits = new WorldHits();
        private readonly Interactor _interactor = new Interactor();
        private PerfStats _perf;

        // A part that throws is switched off after logging once, so one bug can't flood the log or stop the others.
        private bool _worldHitsOk = true, _interactOk = true, _lightOk = true, _hudOk = true, _followOk = true, _streamerOk = true, _creaturesOk = true, _inputOk = true, _overlayOk = true;

        private void Awake()
        {
            LogMessage = msg => Logger.LogInfo(msg);
            Logger.LogInfo("ValOutcast hello: plugin loaded");
            MouseDegreesPerCount = Config.Bind("Input", "MouseDegreesPerCount", 1f,
                "How far the view turns per mouse movement. Higher is faster. Valheim's own mouse sensitivity setting applies on top.").Value;
            ToolMode.Configure(Config.Bind("Input", "ToolModeKey", "R",
                "Key that switches between Outcast's weapons and Valheim's own items (sword, bow, hammer, hoe, torch). A Unity KeyCode name, for example R or V.").Value);
            ToolMode.CameraOffset = new Vector3(
                Config.Bind("ToolMode", "CameraRight", 0f, "In tool mode the camera can move this far to the right of the eyes (meters). 0 = at the eyes.").Value,
                Config.Bind("ToolMode", "CameraUp", 0f, "In tool mode the camera can move this far up from the eyes (meters).").Value,
                -Config.Bind("ToolMode", "CameraBack", 0f, "In tool mode the camera can move this far back from the eyes (meters).").Value);
            HeldItemView.Shift = new Vector3(
                Config.Bind("ToolMode", "ItemRight", 0f, "Where the held item is drawn: moved this far to the right of where the real item is (meters).").Value,
                Config.Bind("ToolMode", "ItemUp", 0.4f, "Where the held item is drawn: moved this far up from where the real item is (meters). Raise it if the item is out of view, lower it if it is too high.").Value,
                Config.Bind("ToolMode", "ItemForward", 0.55f, "Where the held item is drawn: moved this far forward from where the real item is (meters).").Value);
            OverlayBrightness = Config.Bind("Overlay", "Brightness", 0.85f,
                "Brightness of Outcast's picture drawn over Valheim. 1 is as drawn, lower is dimmer.").Value;
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
            _perf = new PerfStats(msg => Logger.LogInfo(msg));
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
                _perf.Measure(name, action);
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
            // Order matters for the 1:1 frame sync: send this frame's input, ask Outcast for the picture and wait for it,
            // then read the position Outcast sent while making it, so the picture and the camera come from the same Outcast frame.
            ToolMode.Update(_input);
            Run(ref _inputOk, "input", () => _input.Update(Link));
            Run(ref _overlayOk, "overlay", () => _overlay.Update());
            Link?.Tick();
            Run(ref _followOk, "follow", () => PlayerFollow.Tick(Player.m_localPlayer));
            Run(ref _streamerOk, "terrain", () => _streamer.Update(Link));
            Run(ref _creaturesOk, "creatures", () => _creatures.Update(Link));
            Run(ref _worldHitsOk, "worldhits", () => _worldHits.Update(Link));
            Run(ref _interactOk, "interact", () => { if (!ToolMode.Active) _interactor.Update(); });
            _spawner.Update();
            Run(ref _lightOk, "light", () => _light.Update(Link));
            Run(ref _hudOk, "hud", () => _hud.Update(PlayerFollow.Active && !ToolMode.Active && Player.m_localPlayer != null));
            _perf.EndFrame(PlayerFollow.Active);
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
