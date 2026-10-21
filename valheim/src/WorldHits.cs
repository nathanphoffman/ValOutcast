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
        private const float SearchRadius = 0.9f;   // a column face is at most about half a cell (0.5 m) from the real surface
        /// <summary>Valheim tool tier of the blow. 4 lets the lightsaber and the guns cut or break anything.</summary>
        public static short ToolTier = 4;

        private int _applied, _nothing;
        private float _nextReport;

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

        private void Apply(VoLink.HitWorldMsg msg, Player player)
        {
            Vector3 point = PlayerFollow.WireToWorld(new Vector3(msg.PosX, msg.PosY, msg.PosZ));
            Vector3 dir = new Vector3(msg.DirX, msg.DirY, -msg.DirZ).normalized; // wire to Unity: flip Z

            IDestructible best = null;
            Collider bestCollider = null;
            float bestDistance = float.MaxValue;
            foreach (Collider c in Physics.OverlapSphere(point, SearchRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                IDestructible d = c.GetComponentInParent<IDestructible>();
                // Creatures are hit through their own boxes. Players' buildings are left alone: a stray shot should not wreck a base.
                if (d == null || d is Character || d is WearNTear) continue;
                float distance = (c.ClosestPoint(point) - point).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = d;
                    bestCollider = c;
                    bestDistance = distance;
                }
            }

            if (best == null)
            {
                _nothing++;
                return;
            }

            var hit = new HitData();
            // Not knowing what it is (tree, rock, ore), the blow counts as both an axe and a pickaxe; each thing takes the one it is weak to.
            hit.m_damage.m_chop = msg.Damage;
            hit.m_damage.m_pickaxe = msg.Damage;
            hit.m_toolTier = ToolTier;
            hit.m_point = point;
            hit.m_dir = dir;
            hit.m_hitCollider = bestCollider;
            hit.m_attacker = player.GetZDOID();
            hit.m_hitType = HitData.HitType.PlayerHit;
            best.Damage(hit);
            _applied++;
            Plugin.LogMessage("world hit: " + (msg.Kind == 1 ? "lightsaber" : "shot") + " damaged " + bestCollider.transform.root.name + " for " + msg.Damage.ToString("0"));
        }
    }
}
