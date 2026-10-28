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
        private const string GivenKeyPrefix = "vo_given_";
        private static GameObject _holder;
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();

        /// <summary>Adds our items and recipes to the ObjectDB. Safe to call again: it skips what is already there.</summary>
        public static void Register(ObjectDB objectDb)
        {
            if (objectDb == null || objectDb.m_items == null || objectDb.m_items.Count == 0) return;
            GameObject template = objectDb.m_items.FirstOrDefault(i => i != null && i.name == "Flint");

            if (template == null) return;

            if (_holder == null)
            {
                // Inactive parent: the copies never show up in the world on their own.
                _holder = new GameObject("ValOutcast items");
                _holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_holder);
            }

            bool added = false;

            foreach (ItemDef itemDef in ItemCatalog.Items)
            {
                GameObject prefab = objectDb.m_items.FirstOrDefault(i => i != null && i.name == itemDef.ItemId);

                if (prefab == null)
                {
                    prefab = Make(template, itemDef);
                    objectDb.m_items.Add(prefab);
                    added = true;
                }

                if (itemDef.Station != null && !objectDb.m_recipes.Any(r => r != null && r.name == "Recipe_" + itemDef.ItemId))
                {
                    Recipe recipe = MakeRecipe(objectDb, prefab.GetComponent<ItemDrop>(), itemDef);

                    if (recipe != null) { objectDb.m_recipes.Add(recipe); added = true; }
                }
            }

            if (added) Plugin.LogMessage("items: registered " + string.Join(", ", ItemCatalog.Items.Select(d => d.Name)));

            if (added) AccessTools.Method(typeof(ObjectDB), "UpdateRegisters").Invoke(objectDb, null);   // private: rebuilds the by-name lookups
        }

        /// <summary>Makes the prefabs known to the scene, so the items can be dropped and picked up.</summary>
        public static void RegisterPrefabs(ZNetScene scene)
        {
            if (scene == null || ObjectDB.instance == null) return;
            var named = (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);

            foreach (ItemDef itemDef in ItemCatalog.Items)
            {
                GameObject prefab = ObjectDB.instance.m_items.FirstOrDefault(i => i != null && i.name == itemDef.ItemId);

                if (prefab == null) continue;
                int hash = itemDef.ItemId.GetStableHashCode();

                if (named.ContainsKey(hash)) continue;
                scene.m_prefabs.Add(prefab);
                named[hash] = prefab;
            }
        }

        /// <summary>The start items, given once per character the first time it spawns.</summary>
        public static void GiveStartItems(Player player)
        {
            if (player == null || player.GetInventory() == null) return;

            foreach (ItemDef itemDef in ItemCatalog.Items.Where(d => d.GiveAtStart))
            {
                string itemKey = GivenKeyPrefix + itemDef.ItemId;

                if (player.m_customData.ContainsKey(itemKey)) continue;

                if (player.GetInventory().AddItem(itemDef.ItemId, 1, 1, 0, 0L, "", false) != null)
                {
                    player.m_customData[itemKey] = "1";   // only once it really is in the inventory, so a failed try is repeated
                    Plugin.LogMessage("items: gave " + itemDef.Name);
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

            if (ObjectDB.instance != null && !ObjectDB.instance.m_items.Any(i => i != null && i.name == ItemCatalog.Items[0].ItemId)) Register(ObjectDB.instance);

            if (ZNetScene.instance != null) RegisterPrefabs(ZNetScene.instance);
            GiveStartItems(player);
            uint mask = 0;

            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                ItemDef itemDef = ItemCatalog.Items.FirstOrDefault(d => d.Weapon > 0 && item.m_dropPrefab != null && item.m_dropPrefab.name == d.ItemId);

                // Set this weapon's bit in the loadout mask.
                if (itemDef != null) mask |= 1u << itemDef.Weapon;
            }

            return mask;
        }

        private static GameObject Make(GameObject template, ItemDef itemDef)
        {
            GameObject gameObject = UnityEngine.Object.Instantiate(template, _holder.transform);

            gameObject.name = itemDef.ItemId;
            ItemDrop drop = gameObject.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData text = drop.m_itemData.m_shared;
            text.m_name = itemDef.Name;
            text.m_description = itemDef.Description;
            text.m_icons = new[] { LoadIcon(itemDef.Icon) };
            text.m_maxStackSize = 1;
            text.m_weight = 1f;
            text.m_value = 0;
            text.m_teleportable = true;
            drop.m_itemData.m_dropPrefab = gameObject;

            return gameObject;
        }

        // Build a craft recipe for the item, using the crafting station the item definition names.
        private static Recipe MakeRecipe(ObjectDB objectDb, ItemDrop result, ItemDef itemDef)
        {
            CraftingStation station = objectDb.m_recipes
                .Where(r => r != null && r.m_craftingStation != null && r.m_craftingStation.name == itemDef.Station)
                .Select(r => r.m_craftingStation).FirstOrDefault();

            if (station == null) { Plugin.LogMessage("items: no crafting station " + itemDef.Station + " for " + itemDef.Name); return null; }

            var needs = new List<Piece.Requirement>();

            foreach (var (prefab, amount) in itemDef.Cost)
            {
                GameObject resources = objectDb.GetItemPrefab(prefab);

                if (resources == null) { Plugin.LogMessage("items: ingredient " + prefab + " not found for " + itemDef.Name); return null; }
                needs.Add(new Piece.Requirement { m_resItem = resources.GetComponent<ItemDrop>(), m_amount = amount });
            }

            Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_" + itemDef.ItemId;
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
            Assembly assembly = typeof(OutcastItems).Assembly;
            string resource = assembly.GetManifestResourceNames().First(n => n.EndsWith(file, StringComparison.Ordinal));
            using (var stream = assembly.GetManifestResourceStream(resource))
            {
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.LoadImage(bytes);
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                Icons[file] = sprite;

                return sprite;
            }
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDbAwakePatch
    {
        // Safe wraps the call so a failure here can't break the game's own Awake.
        private static void Postfix(ObjectDB __instance) => Safe(() => OutcastItems.Register(__instance));

        internal static void Safe(Action action) { try { action(); } catch (Exception error) { Plugin.LogMessage("items: " + error); } }
    }

    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class ObjectDbCopyPatch
    {
        // Safe wraps the call so a failure here can't break the game's own Awake.
        private static void Postfix(ObjectDB __instance) => ObjectDbAwakePatch.Safe(() => OutcastItems.Register(__instance));
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        // Safe wraps the call so a failure here can't break the game's own Awake.
        private static void Postfix(ZNetScene __instance) => ObjectDbAwakePatch.Safe(() => OutcastItems.RegisterPrefabs(__instance));
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class PlayerSpawnedPatch
    {
        // Safe wraps the call so a failure here can't break the game's own Awake.
        private static void Postfix(Player __instance) => ObjectDbAwakePatch.Safe(() => OutcastItems.GiveStartItems(__instance));
    }
}
