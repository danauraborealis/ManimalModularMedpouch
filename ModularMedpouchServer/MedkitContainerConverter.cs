using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ModularMedpouchMod;

// in place mutation of vanilla medkit templates so they behave like containers
// with an internal grid. attributes stay intact,
// we only touch Parent and Properties.Grids
//
// also clones each medkit template under a fresh ID with the original Med parent
// preserved, so the client can build a phantom MedsItemClass for the unzip animation
// flourish before the heal chain fires. the clone -> original mapping is written to
// medkitAnimationMap.json for the client to consume.
public sealed class MedkitContainerConverter
{
    // EFT base item id for SimpleContainer.
    private const string SimpleContainerParent = "5795f317245977243854e041";

    private readonly DatabaseService _databaseService;
    private readonly ISptLogger<MedkitContainerConverter> _logger;
    private readonly ICloner _cloner;

    public static Dictionary<string, string> BotMedkitMap { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public MedkitContainerConverter(
        DatabaseService databaseService,
        ISptLogger<MedkitContainerConverter> logger,
        ICloner cloner)
    {
        _databaseService = databaseService;
        _logger = logger;
        _cloner = cloner;
    }

    public void Apply(MedkitContainerConfig config)
    {
        if (config.Uninstall)
        {
            _logger.Info("[ModularMedpouch] uninstall=true, leaving medkits as vanilla MedsItems");
            return;
        }

        if (config.Medkits.Count == 0)
        {
            _logger.Warning("[ModularMedpouch] no medkit entries configured, nothing to convert");
            return;
        }

        var items = _databaseService.GetItems();
        var animMap = new Dictionary<string, string>();
        var botMedMap = new Dictionary<string, string>();
        foreach (var (tpl, entry) in config.Medkits)
        {
            if (!items.TryGetValue(tpl, out var template))
            {
                _logger.Warning($"[ModularMedpouch] medkit tpl {tpl} ({entry.Name}) not in items db, skipping");
                continue;
            }
            if (template.Properties == null)
            {
                _logger.Warning($"[ModularMedpouch] medkit tpl {tpl} ({entry.Name}) has no Properties, skipping");
                continue;
            }

            // clone before mutation so the clone keeps the original Med parent + meds props
            // intact. the clone is registered under a fresh ID, the client uses it to
            // build a phantom MedsItemClass for the unzip animation flourish.
            var clone = _cloner.Clone(template);
            if (clone != null)
            {
                var animTpl = (MongoId)DerivedTpl(tpl, "anim");
                clone.Id = animTpl;
                items[animTpl] = clone;
                animMap[tpl] = animTpl;
            }

            // separate live-bot clone. bot AI needs a real Med parent while alive.
            // the client swaps this back to the searchable container on death.
            var botClone = _cloner.Clone(template);
            if (botClone != null)
            {
                var botTpl = (MongoId)DerivedTpl(tpl, "bot");
                botClone.Id = botTpl;
                items[botTpl] = botClone;
                botMedMap[tpl] = botTpl;
            }

            var filterTpls = (entry.Filter is { Count: > 0 } ? entry.Filter : config.Filter);
            var grid = BuildGrid(tpl, entry, filterTpls);

            template.Parent = SimpleContainerParent;
            template.Properties.Grids = new[] { grid };
            // EFT's CompoundItem.ctor adds CantPutIntoDuringRaidComponent if this is
            // false/null, blocking in-raid inserts. vanilla medkits default to false
            // since it's irrelevant for non-containers. flip true so the converted
            // container actually accepts inserts mid-raid.
            template.Properties.CanPutIntoDuringTheRaid = true;

            _logger.Info($"[ModularMedpouch] converted {entry.Name ?? tpl} ({tpl}) -> {entry.CellsH}x{entry.CellsV} container");
        }

        WriteAnimationMap(animMap);
        BotMedkitMap = new Dictionary<string, string>(botMedMap, StringComparer.OrdinalIgnoreCase);
        WriteBotMedkitMap(botMedMap);
    }

    // deterministic clone IDs keep the client and server in sync across restarts.
    private static string DerivedTpl(string tpl, string purpose)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"Manimal.ModularMedpouch.{purpose}.{tpl}");
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).Substring(0, 24).ToLowerInvariant();
    }

    // writes the original-ID -> phantom-anim-ID mapping next to the config so the client
    // can find it via the existing path-walk fallback chain.
    private void WriteAnimationMap(Dictionary<string, string> map)
    {
        try
        {
            var modDir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (modDir == null) return;
            var dir = System.IO.Path.Combine(modDir, "config");
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "medkitAnimationMap.json");
            var json = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            _logger.Info($"[ModularMedpouch] wrote animation map ({map.Count} entries) to {path}");
        }
        catch (Exception ex)
        {
            _logger.Warning($"[ModularMedpouch] failed to write animation map: {ex.Message}");
        }
    }

    // writes original container ID -> live-bot med ID. the client uses the inverse map
    // when a bot dies so the corpse has searchable medkits but the live bot had real meds.
    private void WriteBotMedkitMap(Dictionary<string, string> map)
    {
        try
        {
            var modDir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (modDir == null) return;
            var dir = System.IO.Path.Combine(modDir, "config");
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "botMedkitMap.json");
            var json = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            _logger.Info($"[ModularMedpouch] wrote bot medkit map ({map.Count} entries) to {path}");
        }
        catch (Exception ex)
        {
            _logger.Warning($"[ModularMedpouch] failed to write bot medkit map: {ex.Message}");
        }
    }

    private static Grid BuildGrid(string tpl, MedkitContainerConfig.MedkitContainerEntry entry, IEnumerable<string> filterTpls)
    {
        var filterSet = new HashSet<MongoId>();
        foreach (var f in filterTpls) filterSet.Add(f);

        return new Grid
        {
            // deterministic ids derived from the parent ID so reload-after-reload they stay stable
            Id = $"mm_grid_{tpl}",
            Name = "1",
            Parent = tpl,
            Prototype = "55d329c24bdc2d892f8b4567",
            Properties = new GridProperties
            {
                CellsH = entry.CellsH,
                CellsV = entry.CellsV,
                MinCount = 0,
                MaxCount = 0,
                MaxWeight = 0,
                IsSortingTable = false,
                Filters = new[]
                {
                    new GridFilter
                    {
                        Filter = filterSet,
                        ExcludedFilter = new HashSet<MongoId>()
                    }
                }
            }
        };
    }
}
