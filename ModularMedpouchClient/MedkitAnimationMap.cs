using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace Manimal.ModularMedpouch
{
    // loads the originalTpl -> phantomAnimTpl mapping written by the server-side
    // MedkitContainerConverter. uses the same path-walk fallback as MedkitContainerConfig.
    internal static class MedkitAnimationMap
    {
        public static Dictionary<string, string> Load()
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (dir == null) return new Dictionary<string, string>();

            var rootUp3 = Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
            var candidates = new[]
            {
                Path.Combine(rootUp3, "SPT", "user", "mods", "ModularMedpouchServer", "config", "medkitAnimationMap.json"),
                Path.Combine(rootUp3, "user", "mods", "ModularMedpouchServer", "config", "medkitAnimationMap.json"),
                Path.Combine(dir, "medkitAnimationMap.json"),
            };

            string chosen = null;
            foreach (var c in candidates) { if (File.Exists(c)) { chosen = c; break; } }
            if (chosen == null) return new Dictionary<string, string>();

            try
            {
                var json = File.ReadAllText(chosen);
                Plugin.LogSource?.LogInfo($"[ModularMedpouch] loaded animation map from {chosen}");
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource?.LogWarning($"[ModularMedpouch] failed to parse animation map: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }
    }
}
