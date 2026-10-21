using BepInEx;

namespace ValOutcast
{
    [BepInPlugin("com.valoutcast.bridge", "ValOutcast", "0.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        private VoLink _link;

        private void Awake()
        {
            Logger.LogInfo("ValOutcast hello: plugin loaded");
            try
            {
                _link = new VoLink(VoLink.RoleWorld, msg => Logger.LogInfo("link: " + msg));
            }
            catch (System.Exception e)
            {
                Logger.LogError("could not open link (port " + VoLink.PortWorld + " in use?): " + e.Message);
            }
        }

        private void Update()
        {
            _link?.Tick();
        }

        private void OnDestroy()
        {
            _link?.Close();
        }
    }
}
