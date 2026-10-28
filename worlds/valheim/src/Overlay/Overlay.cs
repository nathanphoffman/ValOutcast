using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Shows Outcast's picture (hands, saber, effects, HUD on a transparent background) over Valheim's.
    /// The picture comes from shared memory, see protocol/FRAME.md.
    /// </summary>
    public class Overlay
    {
        private const string Path = "/dev/shm/valoutcast_frame";
        private const float StaleSeconds = 1f;
        private const double WaitMs = 25;   // longest wait for Outcast's picture before carrying on without it

        private readonly FrameReader _reader = new FrameReader(Path);
        private byte[] _pixels;
        private Texture2D _texture;
        private float _lastChange;
        private bool _show;
        private bool _loggedFirst;
        private bool _late;   // the last picture did not arrive in time: the next one to arrive belongs to an old request
        private readonly Action<string> _logger;

        public Overlay(Action<string> logger)
        {
            _logger = logger;
        }

        /// <summary>Call every frame.</summary>
        public void Update()
        {
            // Only while playing: not on the title screen or while loading (no player yet), and not while a Valheim menu is open
            // (the inventory, a chest, crafting, a trader, the hammer's menu): Outcast's picture would be drawn over it.
            if (!PlayerFollow.Active || Player.m_localPlayer == null || Menu.IsVisible()
                || InventoryGui.IsVisible() || StoreGui.IsVisible() || Hud.IsPieceSelectionVisible() || TextInput.IsVisible())
            {
                _show = false;
                PlayerFollow.Lockstep = false;

                return;
            }

            try
            {
                _reader.RequestFrame(HorizontalFov()); // one picture per Valheim frame, so the two games' frames keep step
                // Wait for that picture (Outcast makes it in a few milliseconds), so it is shown in the same frame it was asked for.
                bool gotPicture = false;
                int width = 0, height = 0;
                long start = Stopwatch.GetTimestamp();

                while (true)
                {
                    gotPicture = _reader.TryRead(ref _pixels, out width, out height);

                    if (gotPicture && _late)
                    {
                        _late = false; // an old picture that arrived late: skip it and wait for the one for this frame
                        gotPicture = false;
                    }

                    if (gotPicture) break;

                    if ((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency > WaitMs) break;
                    Thread.Sleep(0);
                }

                PlayerFollow.Lockstep = gotPicture;

                if (!gotPicture && _lastChange > 0f) _late = true;

                if (gotPicture)
                {
                    if (_texture == null || _texture.width != width || _texture.height != height)
                    {
                        if (_texture != null) UnityEngine.Object.Destroy(_texture);
                        _texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                        _texture.filterMode = FilterMode.Bilinear;
                        _texture.wrapMode = TextureWrapMode.Clamp;
                    }

                    _texture.LoadRawTextureData(_pixels);
                    _texture.Apply(false);
                    _lastChange = Time.realtimeSinceStartup;
                    _show = true;

                    if (!_loggedFirst)
                    {
                        _loggedFirst = true;
                        _logger("overlay: showing Outcast's picture, " + width + "x" + height);
                    }
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                _logger("overlay: frame file unreadable, hiding picture: " + error.Message);
                _reader.Close();
                _show = false;
            }

            if (_show && Time.realtimeSinceStartup - _lastChange > StaleSeconds)
            {
                _show = false; // Outcast stopped drawing: don't leave a frozen picture on screen
            }
        }

        /// <summary>
        /// Valheim's horizontal field of view in degrees (0 if there is no camera). Outcast draws with the same one, otherwise its
        /// things slide across the screen at a different speed than Valheim's world as you turn.
        /// </summary>
        private static float HorizontalFov()
        {
            Camera camera = Camera.main;

            if (camera == null || camera.pixelHeight <= 0) return 0f;
            float aspect = (float)camera.pixelWidth / camera.pixelHeight;

            return 2f * Mathf.Atan(Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) * aspect) * Mathf.Rad2Deg;
        }

        /// <summary>Call from OnGUI.</summary>
        public void Draw()
        {
            if (!_show || _texture == null || Event.current.type != EventType.Repaint) return;

            var rect = new Rect(0f, 0f, Screen.width, Screen.height);
            float brightness = Mathf.Clamp(Plugin.OverlayBrightness, 0f, 2f);
            Color previousColor = GUI.color;
            GUI.color = new Color(brightness, brightness, brightness, 1f); // dims the picture without making it see-through
            GUI.DrawTexture(rect, _texture, ScaleMode.StretchToFill, true);
            GUI.color = previousColor;
        }

        public void Close()
        {
            _reader.Close();
        }
    }
}
