using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Applies Outcast's hits on the world itself: a shot or a lightsaber swing that hit a tree, a rock or the ground.
    /// Outcast only sees these as the grid of columns the terrain streamer sent, so the hit point is near the object,
    /// not exactly on it: whatever destructible thing is closest to it takes the damage.
    /// </summary>
    public class WorldHits
    {

        private int _applied, _nothing;
        private float _nextReport;

        // Apply queued world hits from the player side; drop them if we can't apply them yet.
        public void Update(VoLink link)
        {
            if (link == null) return;
            Player player = Player.m_localPlayer;

            if (!link.Connected || !PlayerFollow.Active || !PlayerFollow.Anchored || player == null)
            {
                link.HitWorlds.Clear();

                return;
            }

            while (link.HitWorlds.Count > 0) Apply(link.HitWorlds.Dequeue(), player);

            if ((_applied > 0 || _nothing > 0) && Time.realtimeSinceStartup >= _nextReport)
            {
                _nextReport = Time.realtimeSinceStartup + 10f;
                Plugin.LogMessage("world hits from Outcast: " + _applied + " damaged something, " + _nothing + " found nothing to damage (ground)");
                _applied = _nothing = 0;
            }
        }

        private void Apply(VoLink.HitWorldMsg message, Player player)
        {
            Vector3 point = PlayerFollow.WireToWorld(new Vector3(message.PosX, message.PosY, message.PosZ));
            Vector3 direction = new Vector3(message.DirX, message.DirY, -message.DirZ).normalized; // wire to Unity: flip Z

            IDestructible best = null;
            Collider bestCollider = null;
            float bestDistance = float.MaxValue;

            foreach (Collider collider in Physics.OverlapSphere(point, Tuning.WorldHitSearchRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                IDestructible destructible = collider.GetComponentInParent<IDestructible>();

                // Creatures are hit through their own boxes. Players' buildings are left alone: a stray shot should not wreck a base.
                if (destructible == null || destructible is Character || destructible is WearNTear) continue;
                float distance = (collider.ClosestPoint(point) - point).sqrMagnitude;

                if (distance < bestDistance)
                {
                    best = destructible;
                    bestCollider = collider;
                    bestDistance = distance;
                }
            }

            if (best == null)
            {
                _nothing++;

                return;
            }

            var hitData = new HitData();

            // Not knowing what it is (tree, rock, ore), the blow counts as both an axe and a pickaxe; each thing takes the one it is weak to.
            hitData.m_damage.m_chop = message.Damage;
            hitData.m_damage.m_pickaxe = message.Damage;
            hitData.m_toolTier = Tuning.WorldHitToolTier;
            hitData.m_point = point;
            hitData.m_dir = direction;
            hitData.m_hitCollider = bestCollider;
            hitData.m_attacker = player.GetZDOID();
            hitData.m_hitType = HitData.HitType.PlayerHit;
            best.Damage(hitData);
            _applied++;
            Plugin.LogMessage("world hit: " + (message.Kind == 1 ? "lightsaber" : "shot") + " damaged " + bestCollider.transform.root.name + " for " + message.Damage.ToString("0"));
        }
    }
}
