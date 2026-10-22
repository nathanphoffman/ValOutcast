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
        private readonly float[] _dir = new float[3];
        private readonly float[] _sun = new float[3];
        private readonly float[] _ambient = new float[3];

        public void Update(VoLink link)
        {
            if (link == null || !link.Connected || !PlayerFollow.Active || Player.m_localPlayer == null) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + SendInterval;

            EnvMan env = EnvMan.instance;
            if (env == null || env.m_dirLight == null) return;
            Light sun = env.m_dirLight;

            // The light travels along its forward direction, so the sun is the other way. Unity to wire: flip Z.
            Vector3 toSun = -sun.transform.forward;
            _dir[0] = toSun.x; _dir[1] = toSun.y; _dir[2] = -toSun.z;

            Color c = sun.color * sun.intensity;
            _sun[0] = c.r; _sun[1] = c.g; _sun[2] = c.b;

            Color a = RenderSettings.ambientLight;
            _ambient[0] = a.r; _ambient[1] = a.g; _ambient[2] = a.b;

            link.SendWorldContext(_dir, _sun, _ambient);
        }
    }
}
