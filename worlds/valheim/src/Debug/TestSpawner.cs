using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Testing aid: F8 spawns a boar a few meters in front of the player, so Outcast's weapons can be tried on a creature
    /// without walking around looking for one.
    /// </summary>
    public class TestSpawner
    {
        private const string Prefab = "Boar";
        private const float Distance = 6f;

        public void Update()
        {
            if (!ZInput.GetKeyDown(KeyCode.F8)) return;
            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null) return;

            GameObject prefab = ZNetScene.instance.GetPrefab(Prefab);
            if (prefab == null)
            {
                Plugin.LogMessage("test spawn: no prefab called " + Prefab);
                return;
            }

            Vector3 forward = PlayerFollow.HasCurrent ? PlayerFollow.CurrentLook : player.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            Vector3 pos = player.transform.position + forward.normalized * Distance;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float height)) pos.y = height + 0.1f;
            Object.Instantiate(prefab, pos, Quaternion.LookRotation(-forward));
            Plugin.LogMessage("test spawn: " + Prefab + " " + Distance + " m ahead");
        }
    }
}
