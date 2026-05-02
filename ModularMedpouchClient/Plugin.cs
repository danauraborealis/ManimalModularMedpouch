using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using Manimal.ModularMedpouch.Patches;

namespace Manimal.ModularMedpouch
{
    [BepInPlugin("Manimal.ModularMedpouch", "ModularMedpouch", "1.0.0")]
    [BepInDependency("com.wtt.commonlib")]
    public class Plugin : BaseUnityPlugin
    {
        // id of the medpouch declared in ServerModFiles/db/CustomItems/ModularMedpouch.json
        // keep in sync if you regenerate the id
        public const string ModularMedpouchTpl = "6d1a9c5e8f2b4d3a7c0e1f8b";

        public static ManualLogSource LogSource;
        // exposed for coroutines fired from non-MonoBehaviour helpers
        public static Plugin Instance;

        // IDs treated as container-medkits by the bindable + hotkey patches. populated from
        // config/medkitContainers.json at startup. always includes the medpouch ID; includes
        // converted vanilla medkits unless the config has uninstall=true.
        public static HashSet<string> ContainerTpls = new HashSet<string> { ModularMedpouchTpl };

        private void Awake()
        {
            LogSource = Logger;
            Instance = this;

            var cfg = MedkitContainerConfig.Load();
            ContainerTpls = cfg.ContainerTpls(ModularMedpouchTpl);
            MedpouchHealChain.Configure(cfg.EffectPriority, cfg.EffectItems);
            var botMedMap = BotMedkitMap.DerivedInverse(cfg);
            foreach (var kv in BotMedkitMap.LoadInverse()) botMedMap[kv.Key] = kv.Value;
            BotMedkitDeathConverter.Configure(cfg, botMedMap);

            var animMap = MedkitAnimationMap.Load();
            // build per-ID delay overrides from the medkits block. -1 sentinel means
            // "unset, use the top-level default".
            var perTplDelay = new Dictionary<string, float>();
            foreach (var kv in cfg.Medkits)
            {
                if (kv.Value != null && kv.Value.AnimationDelaySeconds >= 0f)
                    perTplDelay[kv.Key] = kv.Value.AnimationDelaySeconds;
            }
            MedpouchUnzipFlourish.Configure(animMap, cfg.AnimationDelaySeconds, perTplDelay);

            LogSource.LogInfo($"ModularMedpouch loaded! container tpls: {ContainerTpls.Count} (uninstall={cfg.Uninstall}); priority buckets: {cfg.EffectPriority.Count}; anim entries: {animMap.Count} delay: {cfg.AnimationDelaySeconds}s");

            new MedpouchBindablePatch().Enable();
            new MedpouchHotkeyUsePatch().Enable();
            new PhantomMedShortCircuitPatch().Enable();
            new PhantomMedsMethod5Patch().Enable();
            new BotMedkitDeathConvertPatch().Enable();
            new CorpseMedkitConvertPatch().Enable();
            new SearchPanelMedkitConvertPatch().Enable();
            new GridItemViewMedkitConvertPatch().Enable();
        }
    }
}
