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
                    // FindObjectsOfType does not return objects that are switched off, which is what the bars are after the first
                    // hide, so new ones are added to the list and the list is never emptied (or they could not be switched back on).
                    _bars.RemoveAll(b => b == null);
                    foreach (HotkeyBar bar in Object.FindObjectsOfType<HotkeyBar>())
                    {
                        if (!_bars.Contains(bar)) _bars.Add(bar);
                    }
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
