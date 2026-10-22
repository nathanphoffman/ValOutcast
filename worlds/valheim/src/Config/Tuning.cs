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
        /// <summary>Right after the two games line up, damage to the player is ignored: Valheim may still be settling it.</summary>
        public const float HurtGraceSeconds = 3f;
        /// <summary>Valheim tool tier of a blow on the world. 4 lets the lightsaber and the guns cut or break anything.</summary>
        public const short WorldHitToolTier = 4;
        /// <summary>A column face is at most about half a cell (0.5 m) from the real surface, so look a bit wider than that.</summary>
        public const float WorldHitSearchRadius = 0.9f;
    }
}
