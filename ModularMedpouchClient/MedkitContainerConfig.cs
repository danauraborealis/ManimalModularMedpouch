using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace Manimal.ModularMedpouch
{
    // mirrors ServerModFiles/config/medkitContainers.json. only the bits the client cares
    // about are read (uninstall flag + ID set). cells/filter are server-only concerns.
#pragma warning disable 0649 // fields populated by Newtonsoft via reflection
    internal sealed class MedkitContainerConfig
    {
        [JsonProperty("uninstall")] public bool Uninstall;
        [JsonProperty("convertBotMedsOnDeath")] public bool ConvertBotMedsOnDeath = true;
        [JsonProperty("filter")] public List<string> Filter = new List<string>();

        [JsonProperty("medkits")] public Dictionary<string, MedkitEntry> Medkits = new Dictionary<string, MedkitEntry>();

        // ordered list of effect-bucket names the auto-heal chain considers, top to bottom.
        [JsonProperty("effectPriority")] public List<string> EffectPriority = new List<string>();

        // bucket name -> ordered list of IDs that handle that effect. order is preference order
        // when the pouch has multiple matching items.
        [JsonProperty("effectItems")] public Dictionary<string, List<string>> EffectItems = new Dictionary<string, List<string>>();

        // seconds to play the medkit's vanilla unzip animation before cancelling it
        // and starting the heal chain. zero or negative disables the flourish.
        [JsonProperty("animationDelaySeconds")] public float AnimationDelaySeconds = 0.5f;
        [JsonProperty("loot")] public LootFillSettings Loot = new LootFillSettings();

        internal sealed class MedkitEntry
        {
            [JsonProperty("name")] public string Name;
            // optional per-medkit override for the unzip animation delay. negative = unset.
            // when unset, falls back to the top-level AnimationDelaySeconds.
            [JsonProperty("animationDelaySeconds")] public float AnimationDelaySeconds = -1f;
        }

        internal sealed class LootFillSettings
        {
            [JsonProperty("fill")] public bool Fill = true;
            [JsonProperty("guaranteedTpl")] public string GuaranteedTpl = "5755356824597772cb798962";
            [JsonProperty("fillItems")] public List<string> FillItems;
            [JsonProperty("substitutions")] public Dictionary<string, string> Substitutions;
            [JsonProperty("bossSubstitutions")] public Dictionary<string, string> BossSubstitutions;
            [JsonProperty("followerSubstitutions")] public Dictionary<string, string> FollowerSubstitutions;
            [JsonProperty("cultistSubstitutions")] public Dictionary<string, string> CultistSubstitutions;
            [JsonProperty("pmcSubstitutions")] public Dictionary<string, string> PmcSubstitutions;
            [JsonProperty("lootSubstitutions")] public Dictionary<string, string> LootSubstitutions;
            [JsonProperty("bossSubstitutionChance")] public float BossSubstitutionChance = 1.0f;
            [JsonProperty("followerSubstitutionChance")] public float FollowerSubstitutionChance = 1.0f;
            [JsonProperty("pmcSubstitutionChance")] public float PmcSubstitutionChance = 0.5f;
            [JsonProperty("cultistSubstitutionChance")] public float CultistSubstitutionChance = 0.6f;
            [JsonProperty("lootSubstitutionChance")] public float LootSubstitutionChance = 0.3f;
        }
#pragma warning restore 0649

        // IDs the client should treat as container-medkits, including the modular medpouch
        // itself. when uninstall=true only the medpouch is included so the patches keep
        // working for it but leave vanilla medkits alone.
        public HashSet<string> ContainerTpls(string medpouchTpl)
        {
            var set = new HashSet<string> { medpouchTpl };
            if (!Uninstall)
            {
                foreach (var key in Medkits.Keys) set.Add(key);
            }
            return set;
        }

        public static MedkitContainerConfig Load()
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (dir == null) throw new InvalidOperationException("could not resolve plugin directory");

            //servers config is at:
            var rootUp3 = Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
            var candidates = new[]
            {
                Path.Combine(rootUp3, "SPT", "user", "mods", "ModularMedpouchServer", "config", "medkitContainers.json"),
                Path.Combine(rootUp3, "user", "mods", "ModularMedpouchServer", "config", "medkitContainers.json"),
                Path.Combine(dir, "medkitContainers.json")
            };

            string chosen = null;
            foreach (var c in candidates) { if (File.Exists(c)) { chosen = c; break; } }

            if (chosen == null)
            {
                Plugin.LogSource?.LogWarning($"[ModularMedpouch] medkitContainers.json not found (tried: {string.Join(" | ", candidates)}), medpouch-only behavior");
                return new MedkitContainerConfig();
            }

            try
            {
                var json = File.ReadAllText(chosen);
                Plugin.LogSource?.LogInfo($"[ModularMedpouch] loaded config from {chosen}");
                // newtonsoft handles the leading-underscore comment keys in the json fine;
                // they just deserialize as ignored extra members.
                return JsonConvert.DeserializeObject<MedkitContainerConfig>(json) ?? new MedkitContainerConfig();
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource?.LogError($"[ModularMedpouch] failed to parse {chosen}: {ex.Message}");
                return new MedkitContainerConfig();
            }
        }
    }
}
