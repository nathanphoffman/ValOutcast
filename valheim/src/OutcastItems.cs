using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Outcast's weapons as Valheim items you have to craft. We register them ourselves (no item library): a copy of Valheim's
    /// Flint item with a new name and icon, plus a recipe. The items only sit in the inventory; they are not usable in Valheim
    /// (Outcast will read the inventory to decide what you may use).
    /// </summary>
    public static class OutcastItems
    {
        private class Def
        {
            public string Id, Name, Description, Icon;
            public string Station;                       // crafting station prefab name, null = given at the start instead
            public (string prefab, int amount)[] Cost;
            public bool GiveAtStart;
            public int Weapon;                           // the player side's weapon number (Outcast: 2 = bryar pistol, 13 = stun baton, ...)
        }

        private static readonly Def[] Defs =
        {
            new Def { Id = "VO_StunBaton", Name = "Stun Baton", Icon = "stun_baton.png", GiveAtStart = true, Weapon = 13,
                      Description = "A crackling stun baton. You start with one." },
            new Def { Id = "VO_BryarPistol", Name = "Bryar Pistol", Icon = "bryar_pistol.png", Weapon = 2, Station = "piece_workbench",
                      Description = "A small blaster pistol, hand built from forest scraps.",
                      Cost = new[] { ("Wood", 8), ("Flint", 10), ("LeatherScraps", 4), ("Resin", 5) } },
        };

        private const string GivenKeyPrefix = "vo_given_";
        private static GameObject _holder;
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();

        /// <summary>Adds our items and recipes to the ObjectDB. Safe to call again: it skips what is already there.</summary>
        public static void Register(ObjectDB db)
        {
            if (db == null || db.m_items == null || db.m_items.Count == 0) return;
            GameObject template = db.m_items.FirstOrDefault(i => i != null && i.name == "Flint");
            if (template == null) return;

            if (_holder == null)
            {
                // Inactive parent: the copies never show up in the world on their own.
                _holder = new GameObject("ValOutcast items");
                _holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_holder);
            }

            bool added = false;
            foreach (Def def in Defs)
            {
                GameObject prefab = db.m_items.FirstOrDefault(i => i != null && i.name == def.Id);
                if (prefab == null)
                {
                    prefab = Make(template, def);
                    db.m_items.Add(prefab);
                    added = true;
                }
                if (def.Station != null && !db.m_recipes.Any(r => r != null && r.name == "Recipe_" + def.Id))
                {
                    Recipe recipe = MakeRecipe(db, prefab.GetComponent<ItemDrop>(), def);
                    if (recipe != null) { db.m_recipes.Add(recipe); added = true; }
                }
            }
            if (added) Plugin.LogMessage("items: registered " + string.Join(", ", Defs.Select(d => d.Name)));
            if (added) AccessTools.Method(typeof(ObjectDB), "UpdateRegisters").Invoke(db, null);   // private: rebuilds the by-name lookups
        }

        /// <summary>Makes the prefabs known to the scene, so the items can be dropped and picked up.</summary>
        public static void RegisterPrefabs(ZNetScene scene)
        {
            if (scene == null || ObjectDB.instance == null) return;
            var named = (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);
            foreach (Def def in Defs)
            {
                GameObject prefab = ObjectDB.instance.m_items.FirstOrDefault(i => i != null && i.name == def.Id);
                if (prefab == null) continue;
                int hash = def.Id.GetStableHashCode();
                if (named.ContainsKey(hash)) continue;
                scene.m_prefabs.Add(prefab);
                named[hash] = prefab;
            }
        }

        /// <summary>The start items, given once per character the first time it spawns.</summary>
        public static void GiveStartItems(Player player)
        {
            if (player == null || player.GetInventory() == null) return;
            foreach (Def def in Defs.Where(d => d.GiveAtStart))
            {
                string key = GivenKeyPrefix + def.Id;
                if (player.m_customData.ContainsKey(key)) continue;
                if (player.GetInventory().AddItem(def.Id, 1, 1, 0, 0L, "", false) != null)
                {
                    player.m_customData[key] = "1";   // only once it really is in the inventory, so a failed try is repeated
                    Plugin.LogMessage("items: gave " + def.Name);
                }
            }
        }

        /// <summary>
        /// Bit mask of the player side's weapons that the player owns (bit n = weapon number n): the items in the inventory.
        /// Also makes sure the items are registered and the start items given, in case the early hooks ran too soon to catch it.
        /// </summary>
        public static uint OwnedWeapons(Player player)
        {
            if (player == null || player.GetInventory() == null) return 0;
            if (ObjectDB.instance != null && !ObjectDB.instance.m_items.Any(i => i != null && i.name == Defs[0].Id)) Register(ObjectDB.instance);
            if (ZNetScene.instance != null) RegisterPrefabs(ZNetScene.instance);
            GiveStartItems(player);
            uint mask = 0;
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                Def def = Defs.FirstOrDefault(d => d.Weapon > 0 && item.m_dropPrefab != null && item.m_dropPrefab.name == d.Id);
                if (def != null) mask |= 1u << def.Weapon;
            }
            return mask;
        }

        private static GameObject Make(GameObject template, Def def)
        {
            GameObject go = UnityEngine.Object.Instantiate(template, _holder.transform);
            go.name = def.Id;
            ItemDrop drop = go.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = def.Name;
            s.m_description = def.Description;
            s.m_icons = new[] { LoadIcon(def.Icon) };
            s.m_maxStackSize = 1;
            s.m_weight = 1f;
            s.m_value = 0;
            s.m_teleportable = true;
            drop.m_itemData.m_dropPrefab = go;
            return go;
        }

        private static Recipe MakeRecipe(ObjectDB db, ItemDrop result, Def def)
        {
            CraftingStation station = db.m_recipes
                .Where(r => r != null && r.m_craftingStation != null && r.m_craftingStation.name == def.Station)
                .Select(r => r.m_craftingStation).FirstOrDefault();
            if (station == null) { Plugin.LogMessage("items: no crafting station " + def.Station + " for " + def.Name); return null; }

            var needs = new List<Piece.Requirement>();
            foreach (var (prefab, amount) in def.Cost)
            {
                GameObject res = db.GetItemPrefab(prefab);
                if (res == null) { Plugin.LogMessage("items: ingredient " + prefab + " not found for " + def.Name); return null; }
                needs.Add(new Piece.Requirement { m_resItem = res.GetComponent<ItemDrop>(), m_amount = amount });
            }
            Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_" + def.Id;
            recipe.m_item = result;
            recipe.m_amount = 1;
            recipe.m_enabled = true;
            recipe.m_craftingStation = station;
            recipe.m_minStationLevel = 1;
            recipe.m_resources = needs.ToArray();
            return recipe;
        }

        private static Sprite LoadIcon(string file)
        {
            if (Icons.TryGetValue(file, out Sprite cached) && cached != null) return cached;
            Assembly asm = typeof(OutcastItems).Assembly;
            string resource = asm.GetManifestResourceNames().First(n => n.EndsWith(file, StringComparison.Ordinal));
            using (var stream = asm.GetManifestResourceStream(resource))
            {
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                Icons[file] = sprite;
                return sprite;
            }
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDbAwakePatch
    {
        private static void Postfix(ObjectDB __instance) => Safe(() => OutcastItems.Register(__instance));
        internal static void Safe(Action a) { try { a(); } catch (Exception e) { Plugin.LogMessage("items: " + e); } }
    }

    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class ObjectDbCopyPatch
    {
        private static void Postfix(ObjectDB __instance) => ObjectDbAwakePatch.Safe(() => OutcastItems.Register(__instance));
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        private static void Postfix(ZNetScene __instance) => ObjectDbAwakePatch.Safe(() => OutcastItems.RegisterPrefabs(__instance));
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class PlayerSpawnedPatch
    {
        private static void Postfix(Player __instance) => ObjectDbAwakePatch.Safe(() => OutcastItems.GiveStartItems(__instance));
    }
}
