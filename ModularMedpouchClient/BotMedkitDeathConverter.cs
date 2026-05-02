using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

namespace Manimal.ModularMedpouch
{
    // bots carry real med clones while alive so vanilla AI can heal.
    // right before corpse creation we swap those clones back to searchable containers.
    internal static class BotMedkitDeathConverter
    {
        private static readonly Random Rng = new Random();

        // live-bot med ID -> searchable container ID
        private static Dictionary<string, string> _botMedToContainer = new Dictionary<string, string>();
        private static MedkitContainerConfig _cfg = new MedkitContainerConfig();

        public static void Configure(MedkitContainerConfig cfg, Dictionary<string, string> botMedToContainer)
        {
            _cfg = cfg ?? new MedkitContainerConfig();
            _botMedToContainer = botMedToContainer ?? new Dictionary<string, string>();
        }

        public static void Convert(Player player)
        {
            if (player == null) return;
            if (!_cfg.ConvertBotMedsOnDeath || _cfg.Uninstall) return;
            if (_botMedToContainer.Count == 0) return;

            var equipment = player.Inventory?.Equipment;
            if (equipment == null) return;

            Convert(equipment, BotRole(player));
        }

        public static void Convert(InventoryEquipment equipment, string botRole = "")
        {
            if (equipment == null) return;
            Convert((CompoundItem)equipment, botRole);
        }

        public static void Convert(CompoundItem root, string botRole = "")
        {
            if (root == null) return;
            if (!_cfg.ConvertBotMedsOnDeath || _cfg.Uninstall) return;
            if (_botMedToContainer.Count == 0) return;

            var items = new List<Item>(root.GetAllVisibleItems());
            foreach (var item in items)
            {
                if (item == null || item.CurrentAddress == null) continue;
                if (!_botMedToContainer.TryGetValue(item.TemplateId, out var containerTpl)) continue;
                SwapToContainer(item, containerTpl, botRole);
            }
        }

        public static bool TryConvertItem(ref Item item, string botRole = "")
        {
            if (item == null) return false;
            if (!_cfg.ConvertBotMedsOnDeath || _cfg.Uninstall) return false;
            if (_botMedToContainer.Count == 0) return false;
            if (!_botMedToContainer.TryGetValue(item.TemplateId, out var containerTpl)) return false;

            var replacement = SwapToContainer(item, containerTpl, botRole);
            if (replacement == null) return false;

            item = replacement;
            return true;
        }

        private static string BotRole(Player player)
        {
            return player?.Profile?.Info?.Settings?.Role.ToString() ?? string.Empty;
        }

        private static Item SwapToContainer(Item liveMed, string containerTpl, string botRole)
        {
            try
            {
                var factory = Singleton<ItemFactoryClass>.Instance;
                if (factory == null) return null;

                var address = liveMed.CurrentAddress;
                var removed = address.RemoveWithoutRestrictions(liveMed);
                if (removed.Failed) return null;

                var replacement = factory.CreateItem(liveMed.Id, containerTpl, null);
                replacement.SpawnedInSession = liveMed.SpawnedInSession;

                var added = address.AddWithoutRestrictions(replacement);
                if (added.Failed) return null;

                var compound = replacement as CompoundItem;
                if (compound != null) Fill(compound, botRole);
                return replacement;
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning($"[Medpouch] bot med death conversion failed: {ex.Message}");
                return null;
            }
        }

        private static void Fill(CompoundItem container, string botRole)
        {
            if (container.Grids == null || container.Grids.Length == 0) return;

            var grid = container.Grids[0];
            int capacity = Math.Max(0, grid.GridWidth * grid.GridHeight);
            if (capacity <= 0) return;

            var loot = _cfg.Loot ?? new MedkitContainerConfig.LootFillSettings();
            if (!loot.Fill) return;

            var fillPool = (loot.FillItems != null && loot.FillItems.Count > 0) ? loot.FillItems : _cfg.Filter;
            fillPool = fillPool ?? new List<string>();

            var guaranteedTpl = string.IsNullOrEmpty(loot.GuaranteedTpl) ? "5755356824597772cb798962" : loot.GuaranteedTpl;
            var randomPool = new List<string>();
            foreach (var tpl in fillPool)
            {
                if (!string.Equals(tpl, guaranteedTpl, StringComparison.OrdinalIgnoreCase))
                    randomPool.Add(tpl);
            }

            int extras = randomPool.Count == 0 ? 0 : Rng.Next(0, capacity);
            int total = Math.Min(capacity, 1 + extras);

            var settings = CategorySettings(loot, botRole);
            var factory = Singleton<ItemFactoryClass>.Instance;
            for (int i = 0; i < total; i++)
            {
                var tpl = i == 0 ? guaranteedTpl : randomPool[Rng.Next(randomPool.Count)];
                if (settings.Subs.TryGetValue(tpl, out var swapped) && Rng.NextDouble() < settings.Chance)
                    tpl = swapped;

                var child = factory.CreateItem(MongoID.Generate(true), tpl, null);
                int x = i % grid.GridWidth;
                int y = i / grid.GridWidth;
                grid.AddItemWithoutRestrictions(child, new LocationInGrid(x, y, ItemRotation.Horizontal));
            }
        }

        private sealed class CategoryLootSettings
        {
            public float Chance;
            public Dictionary<string, string> Subs;
        }

        private static CategoryLootSettings CategorySettings(MedkitContainerConfig.LootFillSettings loot, string botRole)
        {
            var defaults = CaseInsensitive(loot.Substitutions);
            if (string.IsNullOrEmpty(botRole)) return new CategoryLootSettings { Chance = 0f, Subs = defaults };
            if (botRole.StartsWith("sectant", StringComparison.OrdinalIgnoreCase))
                return new CategoryLootSettings { Chance = Clamp01(loot.CultistSubstitutionChance), Subs = CaseInsensitive(loot.CultistSubstitutions) ?? defaults };
            if (botRole.StartsWith("follower", StringComparison.OrdinalIgnoreCase))
                return new CategoryLootSettings { Chance = Clamp01(loot.FollowerSubstitutionChance), Subs = CaseInsensitive(loot.FollowerSubstitutions) ?? defaults };
            if (botRole.StartsWith("boss", StringComparison.OrdinalIgnoreCase))
                return new CategoryLootSettings { Chance = Clamp01(loot.BossSubstitutionChance), Subs = CaseInsensitive(loot.BossSubstitutions) ?? defaults };
            if (IsPmcLike(botRole))
                return new CategoryLootSettings { Chance = Clamp01(loot.PmcSubstitutionChance), Subs = CaseInsensitive(loot.PmcSubstitutions) ?? defaults };
            return new CategoryLootSettings { Chance = 0f, Subs = defaults };
        }

        private static bool IsPmcLike(string botRole)
        {
            return string.Equals(botRole, "bear", StringComparison.OrdinalIgnoreCase)
                || string.Equals(botRole, "usec", StringComparison.OrdinalIgnoreCase)
                || string.Equals(botRole, "pmcbear", StringComparison.OrdinalIgnoreCase)
                || string.Equals(botRole, "pmcusec", StringComparison.OrdinalIgnoreCase)
                || string.Equals(botRole, "pmcbot", StringComparison.OrdinalIgnoreCase)
                || string.Equals(botRole, "exusec", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> CaseInsensitive(Dictionary<string, string> src)
        {
            return src == null ? null : new Dictionary<string, string>(src, StringComparer.OrdinalIgnoreCase);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
