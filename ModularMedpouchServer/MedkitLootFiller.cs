using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;

namespace ModularMedpouchMod;

// generates child items to seed converted-medkit containers when SPT spawns one
// every spawn gets at least one guaranteed ID (AI-2 by default)
// then a random count of items drawn from a fill pool, 
// packed 1x1 row-major into the configured grid.
//
// state is global / static. patches run as static methods so we cant pass instance
// state in. populated once at OnLoad via Configure().
public static class MedkitLootFiller
{
    // grid name set by MedkitContainerConverter when it converts a vanilla medkit
    private const string GridSlotId = "1";

    // ID of the item every loot medkit must contain at least once. defaults to AI-2.
    private static string _guaranteedTpl = "5755356824597772cb798962";

    // IDs eligible for the random additional fill. populated from the config's
    // top-level filter list (the same set converted medkits accept).
    private static List<string> _fillPool = new();

    // ID -> grid dimensions, populated from config.medkits
    private static Dictionary<string, (int H, int V)> _gridDims = new();
    private static Dictionary<string, string> _botAliveTpls = new();
    private static bool _convertBotMedsOnDeath = true;
    private static bool _fillTraderPurchases = true;
    // trader-purchase pool restrictions: IDs never inserted, and per-ID min loyalty gate.
    private static HashSet<string> _traderFillExclude = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, int> _traderFillMinLoyalty = new(StringComparer.OrdinalIgnoreCase);

    // categories with distinct substitution behaviour. each has its own chance and an
    // optional override map; when the override is null, _defaultSubs is used.
    public enum Category { None, Boss, Follower, Cultist, Pmc, Loot }

    private static Dictionary<string, string> _defaultSubs   = new();
    private static Dictionary<string, string>? _bossSubs     = null;
    private static Dictionary<string, string>? _followerSubs = null;
    private static Dictionary<string, string>? _cultistSubs  = null;
    private static Dictionary<string, string>? _pmcSubs      = null;
    private static Dictionary<string, string>? _lootSubs     = null;

    private static float _bossSubstitutionChance     = 1.0f;
    private static float _followerSubstitutionChance = 1.0f;
    private static float _pmcSubstitutionChance      = 0.5f;
    private static float _cultistSubstitutionChance  = 0.6f;
    private static float _lootSubstitutionChance     = 0.3f;

    // PMC, rogue, raider bot roles. lower-case for case-insensitive comparison.
    private static readonly HashSet<string> PmcLikeRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "bear", "usec",
        "pmcbear", "pmcusec",
        "pmcbot",   // raiders
        "exusec",   // rogues
    };

    private static bool _enabled;
    private static readonly Random Rng = new();

    public static void Configure(MedkitContainerConfig cfg)
    {
        _gridDims = cfg.Medkits.ToDictionary(
            kv => kv.Key,
            kv => (Math.Max(1, kv.Value.CellsH), Math.Max(1, kv.Value.CellsV)));
        _convertBotMedsOnDeath = cfg.ConvertBotMedsOnDeath;

        var loot = cfg.Loot ?? new MedkitContainerConfig.LootFillSettings();
        _enabled = loot.Fill && !cfg.Uninstall;
        _fillTraderPurchases = loot.FillTraderPurchases;
        _traderFillExclude = new HashSet<string>(loot.TraderFillExcludePool ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        _traderFillMinLoyalty = loot.TraderFillMinLoyalty != null
            ? new Dictionary<string, int>(loot.TraderFillMinLoyalty, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(loot.GuaranteedTpl)) _guaranteedTpl = loot.GuaranteedTpl;
        var pool = (loot.FillItems is { Count: > 0 } ? loot.FillItems : cfg.Filter) ?? new List<string>();
        // exclude the guaranteed ID from the random pool so it doesnt double-up cheaply
        _fillPool = pool.Where(t => !string.Equals(t, _guaranteedTpl, StringComparison.OrdinalIgnoreCase)).ToList();
        _defaultSubs  = ToCaseInsensitive(loot.Substitutions);
        _bossSubs     = loot.BossSubstitutions     != null ? ToCaseInsensitive(loot.BossSubstitutions)     : null;
        _followerSubs = loot.FollowerSubstitutions != null ? ToCaseInsensitive(loot.FollowerSubstitutions) : null;
        _cultistSubs  = loot.CultistSubstitutions  != null ? ToCaseInsensitive(loot.CultistSubstitutions)  : null;
        _pmcSubs      = loot.PmcSubstitutions      != null ? ToCaseInsensitive(loot.PmcSubstitutions)      : null;
        _lootSubs     = loot.LootSubstitutions     != null ? ToCaseInsensitive(loot.LootSubstitutions)     : null;

        _bossSubstitutionChance     = Math.Clamp(loot.BossSubstitutionChance,     0f, 1f);
        _followerSubstitutionChance = Math.Clamp(loot.FollowerSubstitutionChance, 0f, 1f);
        _pmcSubstitutionChance      = Math.Clamp(loot.PmcSubstitutionChance,      0f, 1f);
        _cultistSubstitutionChance  = Math.Clamp(loot.CultistSubstitutionChance,  0f, 1f);
        _lootSubstitutionChance     = Math.Clamp(loot.LootSubstitutionChance,     0f, 1f);
    }

    public static void ConfigureBotAliveTpls(Dictionary<string, string> botAliveTpls)
    {
        _botAliveTpls = botAliveTpls != null
            ? new Dictionary<string, string>(botAliveTpls, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>();
    }

    private static Dictionary<string, string> ToCaseInsensitive(Dictionary<string, string>? src)
    {
        return src != null
            ? new Dictionary<string, string>(src, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>();
    }

    // category for a bot role. cultist check runs first so sectantPriest goes there
    // even though it might also match boss-tier elsewhere in SPT.
    //
    // cross mod custom roles handled here too. adding explicit equality checks
    // keeps the integration in the medpouch mod (which already owns the role
    // categorization) rather than forcing each downstream mod to reach into our
    // private data structures. Add new entries below when supporting more mods.
    public static Category CategoryForBotRole(string? botRole)
    {
        if (string.IsNullOrEmpty(botRole)) return Category.None;

        // MitsuruMod Shadow Ops squad leader → MEGA AI-2 (boss tier),
        // followers → Super AI-2 (follower tier). custom roles registered
        // by the Mitsuru prepatch don't match the prefix-based checks
        // below so they need explicit handling.
        if (string.Equals(botRole, "shadowopsleader",   StringComparison.OrdinalIgnoreCase)) return Category.Boss;
        if (string.Equals(botRole, "shadowopsfollower", StringComparison.OrdinalIgnoreCase)) return Category.Follower;

        if (botRole.StartsWith("sectant", StringComparison.OrdinalIgnoreCase))  return Category.Cultist;
        if (botRole.StartsWith("follower", StringComparison.OrdinalIgnoreCase)) return Category.Follower;
        if (botRole.StartsWith("boss", StringComparison.OrdinalIgnoreCase))     return Category.Boss;
        if (PmcLikeRoles.Contains(botRole))                                     return Category.Pmc;
        return Category.None;
    }

    private static (float chance, Dictionary<string, string> subs) GetCategorySettings(Category cat)
    {
        return cat switch
        {
            Category.Boss     => (_bossSubstitutionChance,     _bossSubs     ?? _defaultSubs),
            Category.Follower => (_followerSubstitutionChance, _followerSubs ?? _defaultSubs),
            Category.Cultist  => (_cultistSubstitutionChance,  _cultistSubs  ?? _defaultSubs),
            Category.Pmc      => (_pmcSubstitutionChance,      _pmcSubs      ?? _defaultSubs),
            Category.Loot     => (_lootSubstitutionChance,     _lootSubs     ?? _defaultSubs),
            _                 => (0f,                           _defaultSubs),
        };
    }

    // returns true if the given ID is one of our converted medkits AND loot fill is enabled
    public static bool ShouldFill(MongoId parentTpl)
    {
        return _enabled && _gridDims.ContainsKey(parentTpl);
    }

    // same as ShouldFill but also gated on the trader-purchase toggle.
    public static bool ShouldFillTraderPurchase(MongoId parentTpl)
    {
        return _enabled && _fillTraderPurchases && _gridDims.ContainsKey(parentTpl);
    }

    public static bool TryGetBotAliveTpl(MongoId containerTpl, out string botAliveTpl)
    {
        botAliveTpl = null;
        return _enabled
            && _convertBotMedsOnDeath
            && _botAliveTpls.TryGetValue(containerTpl, out botAliveTpl);
    }

    // build child items to drop into the parent medkits grid. all our med IDs are 1x1
    // so we pack row-major: guaranteed ID first, then 0..(gridArea-1) random fill items.
    // category drives both the swap-chance and which substitution map is used so e.g.
    // bosses can upgrade to MEGA AI-2 while their followers upgrade to Super AI-2.
    public static List<Item> BuildChildren(MongoId parentTpl, MongoId parentId, Category category = Category.None)
    {
        return BuildChildrenCore(parentTpl, parentId, category, _fillPool);
    }

    // trader-purchase variant: same guaranteed + random fill, but the random pool is filtered
    // by the configured exclusions and per-ID loyalty gates (e.g. MEGA never, Super only at
    // LL2+). always Category.None so the guaranteed AI-2 is never upgraded by a buy.
    public static List<Item> BuildTraderChildren(MongoId parentTpl, MongoId parentId, int loyaltyLevel)
    {
        var pool = _fillPool.Where(tpl =>
            !_traderFillExclude.Contains(tpl)
            && (!_traderFillMinLoyalty.TryGetValue(tpl, out var minLevel) || loyaltyLevel >= minLevel)
        ).ToList();
        return BuildChildrenCore(parentTpl, parentId, Category.None, pool);
    }

    // shared builder. `pool` is the set of IDs eligible for the random extra slots; the
    // guaranteed slot always uses _guaranteedTpl regardless of pool.
    private static List<Item> BuildChildrenCore(MongoId parentTpl, MongoId parentId, Category category, List<string> pool)
    {
        if (!_gridDims.TryGetValue(parentTpl, out var dims)) return new List<Item>();

        int capacity = dims.H * dims.V;
        if (capacity <= 0) return new List<Item>();

        var (substitutionChance, subs) = GetCategorySettings(category);

        // 1 guaranteed + 0..(capacity-1) random extras. floor of 1, ceiling of capacity.
        int extras = pool.Count == 0 ? 0 : Rng.Next(0, capacity); // [0, capacity-1] inclusive
        int total = Math.Min(capacity, 1 + extras);

        var children = new List<Item>(total);
        for (int i = 0; i < total; i++)
        {
            string tpl = i == 0 ? _guaranteedTpl : pool[Rng.Next(pool.Count)];
            if (substitutionChance > 0f
                && subs.TryGetValue(tpl, out var swapped)
                && Rng.NextDouble() < substitutionChance)
            {
                tpl = swapped;
            }
            int x = i % dims.H;
            int y = i / dims.H;
            children.Add(new Item
            {
                Id = new MongoId(),
                Template = tpl,
                ParentId = parentId,
                SlotId = GridSlotId,
                Location = new Location
                {
                    X = x,
                    Y = y,
                    R = "Horizontal",
                    IsSearched = false,
                },
            });
        }
        return children;
    }
}
