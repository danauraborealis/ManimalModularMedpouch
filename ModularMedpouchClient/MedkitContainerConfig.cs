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

        [JsonProperty("medkits")] public Dictionary<string, MedkitEntry> Medkits = new Dictionary<string, MedkitEntry>();

        // ordered list of effect-bucket names the auto-heal chain considers, top to bottom.
        [JsonProperty("effectPriority")] public List<string> EffectPriority = new List<string>();

        // bucket name -> ordered list of IDs that handle that effect. order is preference order
        // when the pouch has multiple matching items.
        [JsonProperty("effectItems")] public Dictionary<string, List<string>> EffectItems = new Dictionary<string, List<string>>();

        // seconds to play the medkit's vanilla unzip animation before cancelling it
        // and starting the heal chain. zero or negative disables the flourish.
        [JsonProperty("animationDelaySeconds")] public float AnimationDelaySeconds = 0.5f;

        internal sealed class MedkitEntry
        {
            [JsonProperty("name")] public string Name;
            // optional per-medkit override for the unzip animation delay. negative = unset.
            // when unset, falls back to the top-level AnimationDelaySeconds.
            [JsonProperty("animationDelaySeconds")] public float AnimationDelaySeconds = -1f;
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
                Path.Combine(rootUp3, "SPT", "user", "mods", "ModularMedpouchServer", "config", "medkitContainers.json")
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
