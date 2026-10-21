using System.Collections.Generic;
using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Hides Valheim's hotbar (the numbered tool slots at the top) while Outcast drives: those slots mean nothing
    /// to Outcast, which has its own weapons.
    /// </summary>
    public class HudHider
    {
        private const float ScanSeconds = 1f;
        private readonly List<HotkeyBar> _bars = new List<HotkeyBar>();
        private float _nextScan;
        private bool _hidden;

        public void Update(bool hide)
        {
            if (hide)
            {
                if (Time.unscaledTime >= _nextScan)
                {
                    _nextScan = Time.unscaledTime + ScanSeconds;
                    _bars.Clear();
                    _bars.AddRange(Object.FindObjectsOfType<HotkeyBar>());
                }
                foreach (HotkeyBar bar in _bars)
                {
                    if (bar != null && bar.gameObject.activeSelf) bar.gameObject.SetActive(false);
                }
                _hidden = true;
            }
            else if (_hidden)
            {
                foreach (HotkeyBar bar in _bars)
                {
                    if (bar != null) bar.gameObject.SetActive(true);
                }
                _hidden = false;
            }
        }
    }
}
