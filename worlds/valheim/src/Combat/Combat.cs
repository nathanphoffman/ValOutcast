using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Creatures: sends the nearby ones to the player side (as boxes it can hit), and applies the hits it reports.
    /// Also sends loose items lying on the ground, which force push and pull can move.
    /// </summary>
    public class CreatureStreamer
    {
        private const float SendInterval = 0.1f;

        private readonly Dictionary<uint, Character> _byId = new Dictionary<uint, Character>();
        private readonly Dictionary<uint, ItemDrop> _items = new Dictionary<uint, ItemDrop>();
        private static readonly System.Reflection.FieldInfo ItemInstancesField = AccessTools.Field(typeof(ItemDrop), "s_instances");
        private static readonly AccessTools.FieldRef<ItemDrop, ZNetView> ItemView = AccessTools.FieldRefAccess<ItemDrop, ZNetView>("m_nview");

        private static readonly System.Reflection.MethodInfo TameMethod = AccessTools.Method(typeof(Tameable), "Tame");

        private static List<ItemDrop> ItemInstances()
        {
            return ItemInstancesField.GetValue(null) as List<ItemDrop> ?? new List<ItemDrop>();
        }

        private readonly HashSet<uint> _sentNow = new HashSet<uint>();
        private readonly List<uint> _gone = new List<uint>();
        private readonly ForcePowers _force = new ForcePowers();
        private float _nextSend;
        private float _nextReport;
        private int _hitsApplied, _hitsUnknown, _sentCount;

        public void Update(VoLink link)
        {
            if (link == null) return;
            Player player = Player.m_localPlayer;
            bool enabled = link.Connected && PlayerFollow.Active && PlayerFollow.Anchored && player != null;

            if (!enabled)
            {
                _byId.Clear();
                _items.Clear();
                _sentNow.Clear();
                link.HitActors.Clear();
                link.ActorPushes.Clear();
                link.ActorCharms.Clear();
                link.ActorGrips.Clear();
                link.ActorHeals.Clear();
                _force.Release();

                return;
            }

            ApplyHits(link, player);
            _force.Update(link, player);

            if (Time.realtimeSinceStartup >= _nextReport)
            {
                _nextReport = Time.realtimeSinceStartup + 10f;
                Plugin.LogMessage("creatures: " + _sentCount + " in range of Outcast, hits applied so far " + _hitsApplied + ", for unknown creatures " + _hitsUnknown);
            }

            if (Time.realtimeSinceStartup < _nextSend) return;
            _nextSend = Time.realtimeSinceStartup + SendInterval;

            var seen = new HashSet<uint>();
            Vector3 center = player.transform.position;
            var position = new float[3];

            foreach (Character creature in Character.GetAllCharacters())
            {
                if (creature == null || creature.IsPlayer() || creature.IsDead()) continue;

                if ((creature.transform.position - center).sqrMagnitude > Tuning.CreatureRange * Tuning.CreatureRange) continue;

                uint creatureId = (uint)creature.GetInstanceID();
                seen.Add(creatureId);
                _byId[creatureId] = creature;

                Vector3 wire = PlayerFollow.WorldToWire(creature.transform.position);
                position[0] = wire.x; position[1] = wire.y; position[2] = wire.z;
                float radius = Mathf.Max(0.2f, creature.GetRadius());
                float height = Mathf.Max(0.5f, creature.GetHeight());
                byte healthPercent = (byte)Mathf.Clamp(Mathf.RoundToInt(creature.GetHealthPercentage() * 255f), 0, 255);
                byte flags = (!creature.IsTamed() && BaseAI.IsEnemy(creature, player)) ? VoLink.ActorHostile : (byte)0;
                link.SendActorUpsert(creatureId, position, radius, height, healthPercent, flags);
            }

            foreach (ItemDrop drop in ItemInstances())
            {
                if (drop == null || seen.Count >= Tuning.MaxActors) continue;
                var body = drop.GetComponent<Rigidbody>();

                if (body == null || body.isKinematic) continue;

                if ((drop.transform.position - center).sqrMagnitude > Tuning.ItemRange * Tuning.ItemRange) continue;

                uint creatureId = (uint)drop.gameObject.GetInstanceID();
                seen.Add(creatureId);
                _items[creatureId] = drop;

                Vector3 wire = PlayerFollow.WorldToWire(drop.transform.position);
                position[0] = wire.x; position[1] = wire.y; position[2] = wire.z;
                link.SendActorUpsert(creatureId, position, 0.2f, 0.4f, 255, VoLink.ActorLoose);
            }

            // Tell the player side about creatures that left range or died.
            _gone.Clear();

            foreach (uint creatureId in _sentNow) if (!seen.Contains(creatureId)) _gone.Add(creatureId);

            foreach (uint creatureId in _gone)
            {
                link.SendActorRemove(creatureId);
                _byId.Remove(creatureId);
                _items.Remove(creatureId);
            }

            _sentNow.Clear();

            foreach (uint creatureId in seen) _sentNow.Add(creatureId);
            _sentCount = seen.Count;
        }

        /// <summary>Makes the creature tame and has it follow the player. Creatures with no taming of their own are tamed the same way.</summary>
        private static void Charm(Character animal, Player player)
        {
            if (animal.IsTamed()) return;
            var tameable = animal.GetComponent<Tameable>();

            if (tameable != null) TameMethod.Invoke(tameable, null);
            else animal.SetTamed(true);
            var monsterAi = animal.GetComponent<MonsterAI>();

            if (monsterAi != null) monsterAi.SetFollowTarget(player.gameObject);
            Plugin.LogMessage("mind trick: " + animal.m_name + " is tamed and follows you" + (animal.IsTamed() ? "" : " (taming failed)"));
        }

        /// <summary>Force push or pull on an item lying on the ground: a speed change plus a small hop, so it slides or rolls free.</summary>
        private static void PushItem(ItemDrop drop, Vector3 direction, float strength)
        {
            var body = drop.GetComponent<Rigidbody>();

            if (body == null) return;
            ZNetView view = ItemView(drop);

            if (view != null && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
            body.WakeUp();
            body.AddForce(direction * (Tuning.ItemPushSpeed * strength) + Vector3.up * (Tuning.ItemPushHop * strength), ForceMode.VelocityChange);
            Plugin.LogMessage("force push on item " + drop.name + ", strength " + strength.ToString("0.00"));
        }

        private void ApplyHits(VoLink link, Player player)
        {
            // Mind trick: at full strength (level 3) the creature is tamed and follows the player.
            while (link.ActorCharms.Count > 0)
            {
                VoLink.HitActorMsg charm = link.ActorCharms.Dequeue();

                if (!_byId.TryGetValue(charm.ActorId, out Character animal) || animal == null || animal.IsDead() || animal.IsPlayer()) continue;

                // Damage holds the strength here: levels 1 and 2 only calm the creature.
                if (charm.Damage < Tuning.CharmMinStrength) _force.Calm(charm.ActorId, animal, charm.Damage);
                else Charm(animal, player);
            }

            while (link.ActorGrips.Count > 0)
            {
                VoLink.HitActorMsg grip = link.ActorGrips.Dequeue();

                if (_byId.TryGetValue(grip.ActorId, out Character held) && held != null && !held.IsDead() && !held.IsPlayer()) _force.Grip(grip.ActorId, held, grip.Damage, player);
            }

            while (link.ActorHeals.Count > 0)
            {
                VoLink.HitActorMsg heal = link.ActorHeals.Dequeue();

                if (_byId.TryGetValue(heal.ActorId, out Character friend) && friend != null && !friend.IsDead() && !friend.IsPlayer()) _force.Heal(friend, heal.Damage);
            }

            // Force push and pull: the player side says which creatures it moved and which way.
            while (link.ActorPushes.Count > 0)
            {
                VoLink.HitActorMsg push = link.ActorPushes.Dequeue();
                var direction = new Vector3(push.DirX, push.DirY, -push.DirZ).normalized; // wire to Unity: flip Z

                if (_items.TryGetValue(push.ActorId, out ItemDrop drop) && drop != null)
                {
                    PushItem(drop, direction, push.Damage);   // Damage holds the strength here

                    continue;
                }

                if (!_byId.TryGetValue(push.ActorId, out Character mover) || mover == null || mover.IsDead()) continue;
                mover.ApplyPushback(direction, Tuning.ForcePushForce * push.Damage);   // Damage holds the strength here
                Plugin.LogMessage("force push on " + mover.m_name + ", strength " + push.Damage.ToString("0.00"));
            }

            while (link.HitActors.Count > 0)
            {
                VoLink.HitActorMsg message = link.HitActors.Dequeue();

                if (!_byId.TryGetValue(message.ActorId, out Character target) || target == null || target.IsDead())
                {
                    _hitsUnknown++;
                    Plugin.LogMessage("hit from Outcast for a creature that is not there (id " + message.ActorId + ", damage " + message.Damage.ToString("0") + ")");

                    continue;
                }

                if (target.IsTamed()) continue;   // a creature you tamed is on your side: shots and lightning do not hurt it

                var hitMessage = new HitData();
                hitMessage.m_damage.m_slash = message.Damage * Tuning.PlayerDamageToValheim;
                hitMessage.m_point = target.GetCenterPoint();
                hitMessage.m_dir = new Vector3(message.DirX, message.DirY, -message.DirZ).normalized; // wire to Unity: flip Z
                hitMessage.m_pushForce = Tuning.CreaturePushForce;
                hitMessage.m_attacker = player.GetZDOID();
                float before = target.GetHealth();
                target.Damage(hitMessage);
                _hitsApplied++;
                Plugin.LogMessage("hit applied to " + target.m_name + ": damage " + message.Damage.ToString("0") + ", health " + before.ToString("0") + " -> " + target.GetHealth().ToString("0"));
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

        // Only attacks and harmful effects from creatures are passed on. Falls, drowning, the edge of the world and
        // so on are Valheim's own business: the other game moves the player, so they would be wrong.
        private static bool Passes(HitData.HitType type)
        {
            switch (type)
            {
                case HitData.HitType.EnemyHit:
                case HitData.HitType.Burning:
                case HitData.HitType.Freezing:
                case HitData.HitType.Poisoned:
                case HitData.HitType.Turret:
                case HitData.HitType.Catapult:
                case HitData.HitType.CinderFire:
                    return true;

                default:
                    return false;
            }
        }

        private static bool Prefix(Character __instance, HitData hit)
        {
            if (__instance != Player.m_localPlayer || !PlayerFollow.Active || Plugin.Link == null) return true;

            float damage = hit.GetTotalDamage();
            bool grace = Time.unscaledTime - PlayerFollow.AnchoredAt < Tuning.HurtGraceSeconds;

            if (grace || !Passes(hit.m_hitType))
            {
                Plugin.LogMessage("damage ignored: " + damage.ToString("0") + " (" + hit.m_hitType + (grace ? ", just started" : "") + ")");

                return false; // the Valheim stand-in takes no damage of its own either
            }

            Vector3 direction = hit.m_dir;

            // Unity to wire: flip Z.
            Plugin.Link.SendPlayerHurt(damage, new[] { direction.x, direction.y, -direction.z }, 0);
            Plugin.LogMessage("damage sent to Outcast: " + damage.ToString("0") + " (" + hit.m_hitType + ")");

            return false;
        }
    }
}
