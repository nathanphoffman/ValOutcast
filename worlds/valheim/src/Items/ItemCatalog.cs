namespace ValOutcast
{
    /// <summary>One Outcast weapon as a Valheim item.</summary>
    internal class ItemDef
    {
        public string ItemId, Name, Description, Icon;
        public string Station;                       // crafting station prefab name, null = given at the start instead
        public (string prefab, int amount)[] Cost;
        public bool GiveAtStart;
        public int Weapon;                           // the player side's weapon number (Outcast: 2 = bryar pistol, 13 = stun baton, ...)
    }

    /// <summary>Which Valheim items stand for which Outcast weapons, and what they cost. Change this file to add or tune items.</summary>
    internal static class ItemCatalog
    {
        public static readonly ItemDef[] Items =
        {
            new ItemDef { ItemId = "VO_StunBaton", Name = "Stun Baton", Icon = "stun_baton.png", GiveAtStart = true, Weapon = 13,
                      Description = "A crackling stun baton. You start with one." },
            new ItemDef { ItemId = "VO_BryarPistol", Name = "Bryar Pistol", Icon = "bryar_pistol.png", Weapon = 2, Station = "piece_workbench",
                      Description = "A small blaster pistol, hand built from forest scraps.",
                      Cost = new[] { ("Wood", 8), ("Flint", 10), ("LeatherScraps", 4), ("Resin", 5) } },
        };
    }
}
