using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Creatures: sends the nearby ones to the player side (as boxes it can hit), and applies the hits it reports.
    /// </summary>
    public class CreatureStreamer
    {
        private const float Range = 40f;
        private const float SendInterval = 0.1f;
        /// <summary>Outcast damage points to Valheim damage points.</summary>
        private const float OutcastToValheim = 1f;
        private const float PushForce = 20f;

        private readonly Dictionary<uint, Character> _byId = new Dictionary<uint, Character>();
        private readonly HashSet<uint> _sentNow = new HashSet<uint>();
        private readonly List<uint> _gone = new List<uint>();
        private float _nextSend;

        public void Update(VoLink link)
        {
            if (link == null) return;
            Player player = Player.m_localPlayer;
            bool on = link.Connected && PlayerFollow.Active && PlayerFollow.Anchored && player != null;
            if (!on)
            {
                _byId.Clear();
                _sentNow.Clear();
                link.HitActors.Clear();
                return;
            }

            ApplyHits(link, player);

            if (Time.realtimeSinceStartup < _nextSend) return;
            _nextSend = Time.realtimeSinceStartup + SendInterval;

            var seen = new HashSet<uint>();
            Vector3 center = player.transform.position;
            var pos = new float[3];
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsPlayer() || c.IsDead()) continue;
                if ((c.transform.position - center).sqrMagnitude > Range * Range) continue;

                uint id = (uint)c.GetInstanceID();
                seen.Add(id);
                _byId[id] = c;

                Vector3 wire = PlayerFollow.WorldToWire(c.transform.position);
                pos[0] = wire.x; pos[1] = wire.y; pos[2] = wire.z;
                float radius = Mathf.Max(0.2f, c.GetRadius());
                float height = Mathf.Max(0.5f, c.GetHeight());
                byte hp = (byte)Mathf.Clamp(Mathf.RoundToInt(c.GetHealthPercentage() * 255f), 0, 255);
                byte flags = (!c.IsTamed() && BaseAI.IsEnemy(c, player)) ? VoLink.ActorHostile : (byte)0;
                link.SendActorUpsert(id, pos, radius, height, hp, flags);
            }

            // Tell the player side about creatures that left range or died.
            _gone.Clear();
            foreach (uint id in _sentNow) if (!seen.Contains(id)) _gone.Add(id);
            foreach (uint id in _gone)
            {
                link.SendActorRemove(id);
                _byId.Remove(id);
            }
            _sentNow.Clear();
            foreach (uint id in seen) _sentNow.Add(id);
        }

        private void ApplyHits(VoLink link, Player player)
        {
            while (link.HitActors.Count > 0)
            {
                VoLink.HitActorMsg msg = link.HitActors.Dequeue();
                if (!_byId.TryGetValue(msg.Id, out Character target) || target == null || target.IsDead()) continue;

                var hit = new HitData();
                hit.m_damage.m_slash = msg.Damage * OutcastToValheim;
                hit.m_point = target.GetCenterPoint();
                hit.m_dir = new Vector3(msg.DirX, msg.DirY, -msg.DirZ).normalized; // wire to Unity: flip Z
                hit.m_pushForce = PushForce;
                hit.m_attacker = player.GetZDOID();
                target.Damage(hit);
            }
        }
    }

    /// <summary>
    /// The Valheim player is only a stand-in for the Outcast player, so damage meant for it is sent to Outcast
    /// instead, and Outcast's own health is what counts.
    /// </summary>
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class PlayerDamagePatch
    {
        private static bool Prefix(Character __instance, HitData hit)
        {
            if (__instance != Player.m_localPlayer || !PlayerFollow.Active || Plugin.Link == null) return true;

            Vector3 dir = hit.m_dir;
            // Unity to wire: flip Z.
            Plugin.Link.SendPlayerHurt(hit.GetTotalDamage(), new[] { dir.x, dir.y, -dir.z }, 0);
            return false;
        }
    }
}
