using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Manimal.ModularMedpouch
{
    // loads original container ID -> live-bot med ID. inverted at load so death
    // conversion can spot bot-only meds and swap them back to searchable containers.
    internal static class BotMedkitMap
    {
        public static Dictionary<string, string> LoadInverse()
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (dir == null) return new Dictionary<string, string>();

            var rootUp3 = Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
            var candidates = new[]
            {
                Path.Combine(rootUp3, "SPT", "user", "mods", "ModularMedpouchServer", "config", "botMedkitMap.json"),
                Path.Combine(rootUp3, "user", "mods", "ModularMedpouchServer", "config", "botMedkitMap.json"),
                Path.Combine(dir, "botMedkitMap.json"),
            };

            string chosen = null;
            foreach (var c in candidates) { if (File.Exists(c)) { chosen = c; break; } }
            if (chosen == null) return new Dictionary<string, string>();

            try
            {
                var json = File.ReadAllText(chosen);
                var forward = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                var inverse = new Dictionary<string, string>();
                foreach (var kv in forward)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                        inverse[kv.Value] = kv.Key;
                }
                Plugin.LogSource?.LogInfo($"[ModularMedpouch] loaded bot medkit map from {chosen}");
                return inverse;
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource?.LogWarning($"[ModularMedpouch] failed to parse bot medkit map: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        public static Dictionary<string, string> DerivedInverse(MedkitContainerConfig cfg)
        {
            var inverse = new Dictionary<string, string>();
            if (cfg?.Medkits == null) return inverse;

            foreach (var kv in cfg.Medkits)
            {
                inverse[DerivedTpl(kv.Key, "bot")] = kv.Key;
            }
            return inverse;
        }

        // must match the server-side clone ID derivation.
        private static string DerivedTpl(string tpl, string purpose)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes($"Manimal.ModularMedpouch.{purpose}.{tpl}");
                var hash = sha.ComputeHash(bytes);
                var chars = new char[24];
                for (int i = 0; i < 12; i++)
                {
                    var b = hash[i];
                    chars[i * 2] = Nibble((b >> 4) & 0xF);
                    chars[i * 2 + 1] = Nibble(b & 0xF);
                }
                return new string(chars);
            }
        }

        private static char Nibble(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + value - 10);
        }
    }
}
