namespace ValOutcast
{
    /// <summary>
    /// Tells the player side which Outcast weapons the player owns (the crafted items in the Valheim inventory).
    /// Sent a few times a second, so the player side follows the inventory (and a restart of either game heals).
    /// </summary>
    public class Loadout
    {
        private const float SendInterval = 0.25f;
        private float _next;

        // Tell the player side which weapons we own, at most once per SendInterval.
        public void Update(VoLink link)
        {
            if (link == null || !link.Connected || Player.m_localPlayer == null) return;

            if (UnityEngine.Time.unscaledTime < _next) return;
            _next = UnityEngine.Time.unscaledTime + SendInterval;
            link.SendLoadout(OutcastItems.OwnedWeapons(Player.m_localPlayer));
        }
    }
}
