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
            public Character Creature;
            public BaseAI Controller;
            public Rigidbody Body;
            public bool Gravity;
            public float BaseY;
            public float LastMessage;
            public float NextDamage;
        }

        private class Calmed
        {
            public Character Creature;
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
        public void Grip(uint creatureId, Character creature, float strength, Player player)
        {
            if (creature.IsTamed()) return;

            if (!_held.TryGetValue(creatureId, out Held held))
            {
                held = new Held { Creature = creature, Controller = creature.GetComponent<BaseAI>(), Body = creature.GetComponent<Rigidbody>(), BaseY = creature.transform.position.y };

                if (held.Controller != null) held.Controller.enabled = false;

                if (held.Body != null) { held.Gravity = held.Body.useGravity; held.Body.useGravity = false; }
                _held[creatureId] = held;
                Plugin.LogMessage("force grip on " + creature.m_name);
            }

            held.LastMessage = Time.time;

            // From level 2 up the grip hurts.
            if (strength > 0.5f && Time.time >= held.NextDamage)
            {
                held.NextDamage = Time.time + 0.5f;
                var hitInfo = new HitData();
                hitInfo.m_damage.m_blunt = Tuning.GripDamagePerSecond * 0.5f * strength;
                hitInfo.m_point = creature.GetCenterPoint();
                hitInfo.m_dir = Vector3.down;
                hitInfo.m_attacker = player.GetZDOID();
                creature.Damage(hitInfo);
            }
        }

        /// <summary>Force heal: tame creatures in reach get a share of their health back.</summary>
        public void Heal(Character creature, float strength)
        {
            if (!creature.IsTamed()) return;
            creature.Heal(creature.GetMaxHealth() * Tuning.HealFraction * strength, true);
        }

        /// <summary>The weaker mind tricks: the creature forgets what it was after and stays calm for a while.</summary>
        public void Calm(uint creatureId, Character creature, float strength)
        {
            if (creature.IsTamed()) return;
            float seconds = strength < 0.5f ? Tuning.CalmSecondsLevel1 : Tuning.CalmSecondsLevel2;
            _calmed[creatureId] = new Calmed { Creature = creature, Until = Time.time + seconds };
            Plugin.LogMessage("mind trick: " + creature.m_name + " loses interest for " + seconds.ToString("0") + " s");
        }

        public void Release()
        {
            foreach (Held held in _held.Values) Free(held);
            _held.Clear();
            _calmed.Clear();
            _deflected.Clear();
        }

        // Hand control of a released creature back to the game: re-enable its AI and gravity.
        private static void Free(Held held)
        {
            if (held.Creature == null) return;

            if (held.Controller != null) held.Controller.enabled = true;

            if (held.Body != null) { held.Body.useGravity = held.Gravity; held.Body.velocity = Vector3.zero; }
        }

        // Each frame, lift every gripped creature, and release any that died or stopped receiving grip messages.
        public void Update(VoLink link, Player player)
        {
            var done = new List<uint>();

            foreach (KeyValuePair<uint, Held> pair in _held)
            {
                Held held = pair.Value;

                if (held.Creature == null || held.Creature.IsDead() || Time.time - held.LastMessage > 0.3f)
                {
                    Free(held);
                    done.Add(pair.Key);

                    continue;
                }

                Vector3 position = held.Creature.transform.position;
                position.y = Mathf.MoveTowards(position.y, held.BaseY + Tuning.GripLiftMeters, Tuning.GripLiftSpeed * Time.deltaTime);
                held.Creature.SetMoveDir(Vector3.zero);

                if (held.Body != null) { held.Body.velocity = Vector3.zero; held.Body.position = position; }
                held.Creature.transform.position = position;
            }

            foreach (uint creatureId in done) _held.Remove(creatureId);

            done.Clear();

            foreach (KeyValuePair<uint, Calmed> pair in _calmed)
            {
                Calmed charmed = pair.Value;

                if (charmed.Creature == null || charmed.Creature.IsDead() || Time.time > charmed.Until) { done.Add(pair.Key); continue; }
                var monsterAi = charmed.Creature.GetComponent<MonsterAI>();

                if (monsterAi == null) continue;
                TargetCreature(monsterAi) = null;
                TargetStatic(monsterAi) = null;
                SetAlerted.Invoke(monsterAi, new object[] { false });
            }

            foreach (uint creatureId in done) _calmed.Remove(creatureId);

            DeflectProjectiles(link, player);
        }

        /// <summary>A lit lightsaber knocks back projectiles coming at the player, to the one that fired them where possible.</summary>
        private void DeflectProjectiles(VoLink link, Player player)
        {
            if (Time.time < _nextBlockScan) return;
            _nextBlockScan = Time.time + 0.05f;

            if (!link.PlayerLightFresh || link.PlayerLight.Intensity <= 0f || ToolMode.Active) return;

            Vector3 eyePoint = player.transform.position + Vector3.up * 1.4f;
            Vector3 look = player.GetLookDir();
            float radius = Tuning.SaberBlockRadius;

            foreach (Projectile projectile in Object.FindObjectsOfType<Projectile>())
            {
                if (projectile == null || _deflected.Contains(projectile.GetInstanceID())) continue;
                Character owner = ProjectileOwner(projectile);

                if (owner == player) continue;
                Vector3 toProjectile = projectile.transform.position - eyePoint;

                if (toProjectile.sqrMagnitude > radius * radius || Vector3.Dot(toProjectile.normalized, look) < 0.2f) continue;
                Vector3 velocity = ProjectileVelocity(projectile);

                if (velocity.sqrMagnitude < 1f || Vector3.Dot(velocity.normalized, -toProjectile.normalized) < 0.5f) continue;   // not coming at the player

                Vector3 back = owner != null ? (owner.GetCenterPoint() - projectile.transform.position).normalized : -velocity.normalized;
                ProjectileVelocity(projectile) = back * velocity.magnitude;
                ProjectileOwner(projectile) = player;   // now it is the player's shot, so it hurts the creatures it hits
                _deflected.Add(projectile.GetInstanceID());
                Plugin.LogMessage("saber deflected a projectile");
            }

            if (_deflected.Count > 200) _deflected.Clear();
        }
    }
}
