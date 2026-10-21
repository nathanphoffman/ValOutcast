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
        private readonly Action<string> _log;

        public Overlay(Action<string> log)
        {
            _log = log;
        }

        /// <summary>Call every frame.</summary>
        public void Update()
        {
            // Only while playing: not on the title screen or while loading (no player yet), and not while Valheim's own menu is open.
            if (!PlayerFollow.Active || Player.m_localPlayer == null || Menu.IsVisible())
            {
                _show = false;
                PlayerFollow.Lockstep = false;
                return;
            }

            try
            {
                _reader.RequestFrame(HorizontalFov()); // one picture per Valheim frame, so the two games' frames keep step
                // Wait for that picture (Outcast makes it in a few milliseconds), so it is shown in the same frame it was asked for.
                bool got = false;
                int width = 0, height = 0;
                long start = Stopwatch.GetTimestamp();
                while (true)
                {
                    got = _reader.TryRead(ref _pixels, out width, out height);
                    if (got && _late)
                    {
                        _late = false; // an old picture that arrived late: skip it and wait for the one for this frame
                        got = false;
                    }
                    if (got) break;
                    if ((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency > WaitMs) break;
                    Thread.Sleep(0);
                }
                PlayerFollow.Lockstep = got;
                if (!got && _lastChange > 0f) _late = true;
                if (got)
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
                        _log("overlay: showing Outcast's picture, " + width + "x" + height);
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
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
            Camera cam = Camera.main;
            if (cam == null || cam.pixelHeight <= 0) return 0f;
            float aspect = (float)cam.pixelWidth / cam.pixelHeight;
            return 2f * Mathf.Atan(Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f) * aspect) * Mathf.Rad2Deg;
        }

        /// <summary>Call from OnGUI.</summary>
        public void Draw()
        {
            if (!_show || _texture == null || Event.current.type != EventType.Repaint) return;

            var rect = new Rect(0f, 0f, Screen.width, Screen.height);
            float b = Mathf.Clamp(Plugin.OverlayBrightness, 0f, 2f);
            Color old = GUI.color;
            GUI.color = new Color(b, b, b, 1f); // dims the picture without making it see-through
            GUI.DrawTexture(rect, _texture, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        public void Close()
        {
            _reader.Close();
        }
    }
}
