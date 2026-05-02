using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ModularMedpouchMod;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.Manimal.ModularMedpouch";
    public override string Name { get; init; } = "ModularMedpouch";
    public override string Author { get; init; } = "Manimal";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; } = "";
    public override bool? IsBundleMod { get; init; } = true;
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 2)]
#pragma warning disable CS0618
public class ModularMedpouchServer(
    WTTServerCommonLib.WTTServerCommonLib wttCommon,
    DatabaseService databaseService,
    ICloner cloner,
    ISptLogger<ModularMedpouchServer> logger,
    ISptLogger<MedkitContainerConverter> converterLogger,
    ISptLogger<TraderAssortInjector> assortLogger) : IOnLoad
#pragma warning restore CS0618
{
    public async Task OnLoad()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly);
        await wttCommon.CustomLocaleService.CreateCustomLocales(assembly);

        // inject any custom trader assort files in db/TraderAssorts/. runs after WTT
        // so newly registered custom items can be referenced by their ID.
        try { new TraderAssortInjector(databaseService, assortLogger).InjectAll(); }
        catch (Exception ex) { logger.Error($"[ModularMedpouch] assort injection failed: {ex}"); }

        try
        {
            var cfg = MedkitContainerConfig.Load();
            new MedkitContainerConverter(databaseService, converterLogger, cloner).Apply(cfg);

            // configure the loot filler with the same config and apply the three Harmony
            // postfix patches that auto-fill spawned medkits. skip when uninstalling so
            // vanilla medkits stay vanilla.
            if (!cfg.Uninstall)
            {
                MedkitLootFiller.Configure(cfg);
                MedkitLootFiller.ConfigureBotAliveTpls(MedkitContainerConverter.BotMedkitMap);
                var harmony = new Harmony("com.Manimal.ModularMedpouch.lootfill");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                logger.Info("[ModularMedpouch] loot fill patches applied");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[ModularMedpouch] medkit conversion / loot fill failed: {ex}");
        }
    }
}
