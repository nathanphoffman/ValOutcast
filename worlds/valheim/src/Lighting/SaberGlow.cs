using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// A lit lightsaber on the player side lights up the Valheim world around it, like a bright blue torch.
    /// The light is a plain Unity point light that follows the camera, at about where the blade is held.
    /// </summary>
    public static class SaberGlow
    {
        /// <summary>Meters right, up and forward of the camera. The blade is drawn at the lower right of the screen.</summary>
        private static readonly Vector3 Offset = new Vector3(0.25f, -0.2f, 0.7f);
        private const float Range = 14f;
        private const float BaseIntensity = 3.5f;

        private static Light _light;

        public static void Update(VoLink link, Transform camera)
        {
            if (link == null || !link.PlayerLightFresh || link.PlayerLight.Intensity <= 0f || ToolMode.Active)
            {
                Clear();

                return;
            }

            if (_light == null)
            {
                var gameObject = new GameObject("ValOutcast saber glow");
                _light = gameObject.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.shadows = LightShadows.None;
                _light.range = Range;
            }

            var message = link.PlayerLight;
            _light.color = new Color(message.Red, message.Green, message.Blue);

            // A slow, gentle pulse (about 1.6 times a second, 4% deep) so it feels alive without flickering.
            _light.intensity = BaseIntensity * message.Intensity * (0.96f + 0.04f * Mathf.Sin(Time.time * 10f));
            _light.transform.position = camera.position + camera.rotation * Offset;
            _light.enabled = true;
        }

        public static void Clear()
        {
            if (_light != null) _light.enabled = false;
        }
    }
}
