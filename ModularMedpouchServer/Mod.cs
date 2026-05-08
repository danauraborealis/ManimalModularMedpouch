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
    public override string ModGuid { get; init; } = Manimal.ModularMedpouch.ModInfo.Guid;
    public override string Name { get; init; } = Manimal.ModularMedpouch.ModInfo.ServerName;
    public override string Author { get; init; } = Manimal.ModularMedpouch.ModInfo.Author;
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new(Manimal.ModularMedpouch.ModInfo.Version);
    public override SemanticVersioning.Range SptVersion { get; init; } = new(Manimal.ModularMedpouch.ModInfo.SptVersion);
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
    IReadOnlyList<SPTarkov.Server.Core.Models.Spt.Mod.SptMod> installedMods,
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

                // acid's progressive bot system bypasses vanilla BotLootGenerator entirely
                // and uses its own CustomBotLootGenerator
                TryPatchApbs(harmony);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[ModularMedpouch] medkit conversion / loot fill failed: {ex}");
        }
    }

    private const string ApbsModGuid = "com.acidphantasm.progressivebotsystem";

    private void TryPatchApbs(Harmony harmony)
    {
        try
        {
            // look up APBS via the SPT mod registry by its ModGuid
            var apbsMod = installedMods.FirstOrDefault(m => m.ModMetadata?.ModGuid == ApbsModGuid);
            if (apbsMod == null)
            {
                logger.Info("[ModularMedpouch] APBS not detected, skipping APBS bot loot patch");
                return;
            }

            Type? apbsType = null;
            foreach (var asm in apbsMod.Assemblies)
            {
                try { apbsType = asm.GetTypes().FirstOrDefault(t => t.Name == "CustomBotLootGenerator"); }
                catch { /* type-load mismatch, skip */ }
                if (apbsType != null) break;
            }

            if (apbsType == null)
            {
                logger.Warning($"[ModularMedpouch] APBS detected ({ApbsModGuid}) but CustomBotLootGenerator type not found; bot medkits may spawn empty");
                return;
            }

            var apbsMethod = AccessTools.Method(apbsType, "AddRequiredChildItemsToParent");
            if (apbsMethod == null)
            {
                logger.Warning("[ModularMedpouch] APBS detected but CustomBotLootGenerator.AddRequiredChildItemsToParent not found; bot medkits may spawn empty");
                return;
            }

            // Postfix is a private static method on BotMedkitFillPatch; use a string lookup
            // because nameof() needs accessible scope.
            var ourPostfix = AccessTools.Method(typeof(Patches.BotMedkitFillPatch), "Postfix");
            harmony.Patch(apbsMethod, postfix: new HarmonyMethod(ourPostfix));
            logger.Info("[ModularMedpouch] APBS detected, patched CustomBotLootGenerator.AddRequiredChildItemsToParent");
        }
        catch (Exception ex)
        {
            logger.Warning($"[ModularMedpouch] APBS detection / patch failed: {ex.Message}");
        }
    }
}
