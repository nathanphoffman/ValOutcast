using BepInEx;

namespace ValOutcast
{
    [BepInPlugin("com.valoutcast.bridge", "ValOutcast", "0.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            Logger.LogInfo("ValOutcast hello: plugin loaded");
        }
    }
}
