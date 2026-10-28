using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Tells the player side how the Valheim world is lit right now (sun direction, sun color, ambient color),
    /// so the other game's models are lit to match: bright by day, dark at night.
    /// </summary>
    public class WorldLight
    {
        private const float SendInterval = 0.25f;
        private float _next;
        private readonly float[] _sunDirection = new float[3];
        private readonly float[] _sunColor = new float[3];
        private readonly float[] _ambient = new float[3];

        public void Update(VoLink link)
        {
            if (link == null || !link.Connected || !PlayerFollow.Active || Player.m_localPlayer == null) return;

            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + SendInterval;

            EnvMan environment = EnvMan.instance;

            if (environment == null || environment.m_dirLight == null) return;
            Light sunLight = environment.m_dirLight;

            // The light travels along its forward direction, so the sun is the other way. Unity to wire: flip Z.
            Vector3 toSun = -sunLight.transform.forward;
            _sunDirection[0] = toSun.x; _sunDirection[1] = toSun.y; _sunDirection[2] = -toSun.z;

            Color sunColor = sunLight.color * sunLight.intensity;
            _sunColor[0] = sunColor.r; _sunColor[1] = sunColor.g; _sunColor[2] = sunColor.b;

            Color ambientColor = RenderSettings.ambientLight;
            _ambient[0] = ambientColor.r; _ambient[1] = ambientColor.g; _ambient[2] = ambientColor.b;

            link.SendWorldContext(_sunDirection, _sunColor, _ambient);
        }
    }
}
