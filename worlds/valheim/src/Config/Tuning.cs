namespace ValOutcast
{
    /// <summary>
    /// Numbers that belong to Valheim (how its damage, ranges and tools map to the player side). A plugin for another world
    /// game has its own version of this file. Items and weapon numbers are in Items/ItemCatalog.cs.
    /// </summary>
    internal static class Tuning
    {
        /// <summary>How far around the player creatures are sent to the player side, in meters.</summary>
        public const float CreatureRange = 40f;
        /// <summary>The player side's damage points to Valheim damage points.</summary>
        public const float PlayerDamageToValheim = 1f;
        public const float CreaturePushForce = 20f;
        /// <summary>Valheim push force of a full strength force push or pull on a creature (a sword hit uses about 20-60). Tune this.</summary>
        public const float ForcePushForce = 150f;
        /// <summary>Items on the ground are sent to the player side so they can be pushed and pulled: how far (m), and the most sent at once.</summary>
        /// <summary>Mind trick strength (the force level divided by 3) needed to tame a creature: 1 means level 3 only.</summary>
        public const float CharmMinStrength = 0.99f;
        /// <summary>Mind trick levels 1 and 2: how long (s) the creature loses interest.</summary>
        public const float CalmSecondsLevel1 = 6f;
        public const float CalmSecondsLevel2 = 20f;
        /// <summary>Force grip: how high (m) a creature is lifted, how fast (m/s), and the damage per second at full strength (level 2 and up).</summary>
        public const float GripLiftMeters = 1.8f;
        public const float GripLiftSpeed = 4f;
        public const float GripDamagePerSecond = 12f;
        /// <summary>Force heal: the share of a tame creature's full health restored by a full strength heal.</summary>
        public const float HealFraction = 0.5f;
        /// <summary>A lit lightsaber turns projectiles back when they come this close (m).</summary>
        public const float SaberBlockRadius = 3.5f;
        public const float ItemRange = 18f;
        public const int MaxActors = 48;
        /// <summary>Speed (m/s) given to a loose item by a full strength push or pull, plus a small upward hop so it does not stick.</summary>
        public const float ItemPushSpeed = 9f;
        public const float ItemPushHop = 2.5f;
        /// <summary>Right after the two games line up, damage to the player is ignored: Valheim may still be settling it.</summary>
        public const float HurtGraceSeconds = 3f;
        /// <summary>Valheim tool tier of a blow on the world. 4 lets the lightsaber and the guns cut or break anything.</summary>
        public const short WorldHitToolTier = 4;
        /// <summary>A column face is at most about half a cell (0.5 m) from the real surface, so look a bit wider than that.</summary>
        public const float WorldHitSearchRadius = 0.9f;
    }
}
