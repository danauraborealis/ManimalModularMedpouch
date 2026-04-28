using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace ModularMedpouchMod;

// reads JSON files under db/TraderAssorts/ and merges each one into the named
// trader's live assort at OnLoad. file format mirrors SPT's standard assort
// shape (items / barter_scheme / loyal_level_items) plus a top-level traderId.
public sealed class TraderAssortInjector
{
    private readonly DatabaseService _databaseService;
    private readonly ISptLogger<TraderAssortInjector> _logger;

    public TraderAssortInjector(DatabaseService databaseService, ISptLogger<TraderAssortInjector> logger)
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    public void InjectAll()
    {
        var modDir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (modDir == null) return;
        var dir = System.IO.Path.Combine(modDir, "db", "TraderAssorts");
        if (!Directory.Exists(dir)) return;

        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        foreach (var path in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(path);
                var injection = JsonSerializer.Deserialize<AssortInjection>(json, opts);
                if (injection == null || string.IsNullOrWhiteSpace(injection.TraderId))
                {
                    _logger.Warning($"[ModularMedpouch] {System.IO.Path.GetFileName(path)} missing traderId, skipping");
                    continue;
                }
                Inject(injection, System.IO.Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                _logger.Warning($"[ModularMedpouch] failed to inject {System.IO.Path.GetFileName(path)}: {ex.Message}");
            }
        }
    }

    private void Inject(AssortInjection injection, string filename)
    {
        var trader = _databaseService.GetTrader(injection.TraderId);
        if (trader?.Assort == null)
        {
            _logger.Warning($"[ModularMedpouch] trader {injection.TraderId} not found or has no assort, skipping {filename}");
            return;
        }

        trader.Assort.Items ??= new List<Item>();
        trader.Assort.BarterScheme ??= new Dictionary<MongoId, List<List<BarterScheme>>>();
        trader.Assort.LoyalLevelItems ??= new Dictionary<MongoId, int>();

        int added = 0;
        if (injection.Items != null)
        {
            foreach (var i in injection.Items)
            {
                if (i?.Id == null) continue;
                // skip if id collides with existing entry
                bool exists = false;
                foreach (var ex in trader.Assort.Items)
                {
                    if (ex.Id == i.Id) { exists = true; break; }
                }
                if (exists) continue;

                trader.Assort.Items.Add(new Item
                {
                    Id = i.Id,
                    Template = i.Tpl,
                    ParentId = i.ParentId,
                    SlotId = i.SlotId,
                    Upd = i.Upd,
                });
                added++;
            }
        }
        if (injection.BarterScheme != null)
        {
            foreach (var kv in injection.BarterScheme)
            {
                var converted = new List<List<BarterScheme>>();
                foreach (var inner in kv.Value)
                {
                    var innerList = new List<BarterScheme>();
                    foreach (var entry in inner)
                    {
                        innerList.Add(new BarterScheme
                        {
                            Count = entry.Count,
                            Template = entry.Tpl ?? string.Empty,
                        });
                    }
                    converted.Add(innerList);
                }
                trader.Assort.BarterScheme[(MongoId)kv.Key] = converted;
            }
        }
        if (injection.LoyalLevelItems != null)
        {
            foreach (var kv in injection.LoyalLevelItems)
                trader.Assort.LoyalLevelItems[(MongoId)kv.Key] = kv.Value;
        }

        _logger.Info($"[ModularMedpouch] injected {added} items into trader {injection.TraderId} from {filename}");
    }

    // System.Text.Json doesn't support MongoId as a dictionary key OR as a property value
    // without a custom converter, so the DTO uses strings throughout. Inject() promotes
    // them to MongoId when merging into the trader's live assort.
    private sealed record AssortInjection
    {
        [JsonPropertyName("traderId")]          public string? TraderId { get; init; }
        [JsonPropertyName("items")]             public List<AssortItem>? Items { get; init; }
        [JsonPropertyName("barter_scheme")]     public Dictionary<string, List<List<BarterEntry>>>? BarterScheme { get; init; }
        [JsonPropertyName("loyal_level_items")] public Dictionary<string, int>? LoyalLevelItems { get; init; }
    }

    private sealed record BarterEntry
    {
        [JsonPropertyName("count")] public double? Count { get; init; }
        [JsonPropertyName("_tpl")]  public string? Tpl { get; init; }
    }

    private sealed record AssortItem
    {
        [JsonPropertyName("_id")]      public string? Id { get; init; }
        [JsonPropertyName("_tpl")]     public string? Tpl { get; init; }
        [JsonPropertyName("parentId")] public string? ParentId { get; init; }
        [JsonPropertyName("slotId")]   public string? SlotId { get; init; }
        [JsonPropertyName("upd")]      public Upd? Upd { get; init; }
    }
}
