using System;
using System.IO;
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

        private readonly FrameReader _reader = new FrameReader(Path);
        private byte[] _pixels;
        private Texture2D _texture;
        private Material _material;
        private bool _materialTried;
        private float _lastChange;
        private bool _show;
        private bool _loggedFirst;
        private readonly Action<string> _log;

        public Overlay(Action<string> log)
        {
            _log = log;
        }

        /// <summary>Call every frame.</summary>
        public void Update()
        {
            if (!PlayerFollow.Active)
            {
                _show = false;
                return;
            }

            try
            {
                if (_reader.TryRead(ref _pixels, out int width, out int height))
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

        /// <summary>Call from OnGUI.</summary>
        public void Draw()
        {
            if (!_show || _texture == null || Event.current.type != EventType.Repaint) return;

            if (!_materialTried)
            {
                _materialTried = true;
                // This built-in shader blends as premultiplied alpha, which is how the picture is stored.
                // (Sprites/Default looks similar but multiplies the colors by alpha a second time, which washes out the HUD.)
                Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
                if (shader != null)
                {
                    _material = new Material(shader);
                    _log("overlay: using premultiplied alpha blending");
                }
                else
                {
                    _log("overlay: premultiplied shader not found, using plain alpha blending");
                }
            }

            var rect = new Rect(0f, 0f, Screen.width, Screen.height);
            if (_material != null)
            {
                float b = Mathf.Clamp(Plugin.OverlayBrightness, 0f, 2f);
                // The color tints the picture's red, green and blue (alpha stays as drawn), which dims it without making it see-through.
                Graphics.DrawTexture(rect, _texture, new Rect(0f, 0f, 1f, 1f), 0, 0, 0, 0, new Color(b, b, b, 1f), _material);
            }
            else
            {
                GUI.DrawTexture(rect, _texture, ScaleMode.StretchToFill, true);
            }
        }

        public void Close()
        {
            _reader.Close();
        }
    }
}
