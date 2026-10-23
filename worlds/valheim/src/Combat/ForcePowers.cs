using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// What the player side's force powers do to Valheim beyond push and pull: grip holds a creature in the air, heal mends
    /// tame creatures, the weaker mind tricks make creatures lose interest, and a lit lightsaber turns projectiles back.
    /// Outcast works out the aim and the cost; this decides the effect.
    /// </summary>
    internal class ForcePowers
    {
        private class Held
        {
            public Character Who;
            public BaseAI Ai;
            public Rigidbody Body;
            public bool Gravity;
            public float BaseY;
            public float LastMessage;
            public float NextDamage;
        }

        private class Calmed
        {
            public Character Who;
            public float Until;
        }

        private static readonly AccessTools.FieldRef<MonsterAI, Character> TargetCreature = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");
        private static readonly AccessTools.FieldRef<MonsterAI, StaticTarget> TargetStatic = AccessTools.FieldRefAccess<MonsterAI, StaticTarget>("m_targetStatic");
        private static readonly AccessTools.FieldRef<Projectile, Vector3> ProjectileVelocity = AccessTools.FieldRefAccess<Projectile, Vector3>("m_vel");
        private static readonly AccessTools.FieldRef<Projectile, Character> ProjectileOwner = AccessTools.FieldRefAccess<Projectile, Character>("m_owner");

        private static readonly System.Reflection.MethodInfo SetAlerted = AccessTools.Method(typeof(BaseAI), "SetAlerted");

        private readonly Dictionary<uint, Held> _held = new Dictionary<uint, Held>();
        private readonly Dictionary<uint, Calmed> _calmed = new Dictionary<uint, Calmed>();
        private readonly HashSet<int> _deflected = new HashSet<int>();
        private float _nextBlockScan;

        /// <summary>Force grip: the creature is lifted and held for as long as grip messages keep coming.</summary>
        public void Grip(uint id, Character who, float strength, Player player)
        {
            if (who.IsTamed()) return;
            if (!_held.TryGetValue(id, out Held h))
            {
                h = new Held { Who = who, Ai = who.GetComponent<BaseAI>(), Body = who.GetComponent<Rigidbody>(), BaseY = who.transform.position.y };
                if (h.Ai != null) h.Ai.enabled = false;
                if (h.Body != null) { h.Gravity = h.Body.useGravity; h.Body.useGravity = false; }
                _held[id] = h;
                Plugin.LogMessage("force grip on " + who.m_name);
            }
            h.LastMessage = Time.time;
            // From level 2 up the grip hurts.
            if (strength > 0.5f && Time.time >= h.NextDamage)
            {
                h.NextDamage = Time.time + 0.5f;
                var hit = new HitData();
                hit.m_damage.m_blunt = Tuning.GripDamagePerSecond * 0.5f * strength;
                hit.m_point = who.GetCenterPoint();
                hit.m_dir = Vector3.down;
                hit.m_attacker = player.GetZDOID();
                who.Damage(hit);
            }
        }

        /// <summary>Force heal: tame creatures in reach get a share of their health back.</summary>
        public void Heal(Character who, float strength)
        {
            if (!who.IsTamed()) return;
            who.Heal(who.GetMaxHealth() * Tuning.HealFraction * strength, true);
        }

        /// <summary>The weaker mind tricks: the creature forgets what it was after and stays calm for a while.</summary>
        public void Calm(uint id, Character who, float strength)
        {
            if (who.IsTamed()) return;
            float seconds = strength < 0.5f ? Tuning.CalmSecondsLevel1 : Tuning.CalmSecondsLevel2;
            _calmed[id] = new Calmed { Who = who, Until = Time.time + seconds };
            Plugin.LogMessage("mind trick: " + who.m_name + " loses interest for " + seconds.ToString("0") + " s");
        }

        public void Release()
        {
            foreach (Held h in _held.Values) Free(h);
            _held.Clear();
            _calmed.Clear();
            _deflected.Clear();
        }

        private static void Free(Held h)
        {
            if (h.Who == null) return;
            if (h.Ai != null) h.Ai.enabled = true;
            if (h.Body != null) { h.Body.useGravity = h.Gravity; h.Body.velocity = Vector3.zero; }
        }

        public void Update(VoLink link, Player player)
        {
            var done = new List<uint>();
            foreach (KeyValuePair<uint, Held> pair in _held)
            {
                Held h = pair.Value;
                if (h.Who == null || h.Who.IsDead() || Time.time - h.LastMessage > 0.3f)
                {
                    Free(h);
                    done.Add(pair.Key);
                    continue;
                }
                Vector3 pos = h.Who.transform.position;
                pos.y = Mathf.MoveTowards(pos.y, h.BaseY + Tuning.GripLiftMeters, Tuning.GripLiftSpeed * Time.deltaTime);
                h.Who.SetMoveDir(Vector3.zero);
                if (h.Body != null) { h.Body.velocity = Vector3.zero; h.Body.position = pos; }
                h.Who.transform.position = pos;
            }
            foreach (uint id in done) _held.Remove(id);

            done.Clear();
            foreach (KeyValuePair<uint, Calmed> pair in _calmed)
            {
                Calmed c = pair.Value;
                if (c.Who == null || c.Who.IsDead() || Time.time > c.Until) { done.Add(pair.Key); continue; }
                var ai = c.Who.GetComponent<MonsterAI>();
                if (ai == null) continue;
                TargetCreature(ai) = null;
                TargetStatic(ai) = null;
                SetAlerted.Invoke(ai, new object[] { false });
            }
            foreach (uint id in done) _calmed.Remove(id);

            DeflectProjectiles(link, player);
        }

        /// <summary>A lit lightsaber knocks back projectiles coming at the player, to the one that fired them where possible.</summary>
        private void DeflectProjectiles(VoLink link, Player player)
        {
            if (Time.time < _nextBlockScan) return;
            _nextBlockScan = Time.time + 0.05f;
            if (!link.PlayerLightFresh || link.PlayerLight.Intensity <= 0f || ToolMode.Active) return;

            Vector3 eye = player.transform.position + Vector3.up * 1.4f;
            Vector3 look = player.GetLookDir();
            float radius = Tuning.SaberBlockRadius;
            foreach (Projectile p in Object.FindObjectsOfType<Projectile>())
            {
                if (p == null || _deflected.Contains(p.GetInstanceID())) continue;
                Character owner = ProjectileOwner(p);
                if (owner == player) continue;
                Vector3 toProjectile = p.transform.position - eye;
                if (toProjectile.sqrMagnitude > radius * radius || Vector3.Dot(toProjectile.normalized, look) < 0.2f) continue;
                Vector3 vel = ProjectileVelocity(p);
                if (vel.sqrMagnitude < 1f || Vector3.Dot(vel.normalized, -toProjectile.normalized) < 0.5f) continue;   // not coming at the player

                Vector3 back = owner != null ? (owner.GetCenterPoint() - p.transform.position).normalized : -vel.normalized;
                ProjectileVelocity(p) = back * vel.magnitude;
                ProjectileOwner(p) = player;   // now it is the player's shot, so it hurts the creatures it hits
                _deflected.Add(p.GetInstanceID());
                Plugin.LogMessage("saber deflected a projectile");
            }
            if (_deflected.Count > 200) _deflected.Clear();
        }
    }
}
