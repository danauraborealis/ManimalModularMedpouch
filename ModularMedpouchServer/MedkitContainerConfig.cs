using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModularMedpouchMod;

// shape of config/medkitContainers.json. the client reads this same file directly from
// the server mod's config folder, so keep the property names stable across both ends.
public sealed record MedkitContainerConfig
{
    [JsonPropertyName("uninstall")] public bool Uninstall { get; init; }
    [JsonPropertyName("filter")]    public List<string> Filter { get; init; } = new();
    [JsonPropertyName("medkits")]   public Dictionary<string, MedkitContainerEntry> Medkits { get; init; } = new();
    [JsonPropertyName("loot")]      public LootFillSettings? Loot { get; init; }

    public sealed record MedkitContainerEntry
    {
        [JsonPropertyName("name")]   public string? Name { get; init; }
        [JsonPropertyName("cellsH")] public int CellsH { get; init; } = 1;
        [JsonPropertyName("cellsV")] public int CellsV { get; init; } = 1;
        // per medkit filter override; null/empty falls back to the top-level Filter list
        [JsonPropertyName("filter")] public List<string>? Filter { get; init; }
    }

    // controls auto-fill of converted medkits when SPT spawns them as loot (bot
    // inventories, static loot containers, loose loot). every fired spawn gets the
    // guaranteed ID plus a random count of items drawn from the fill pool, packed
    // row-major into the medkits grid.
    public sealed record LootFillSettings
    {
        [JsonPropertyName("fill")]          public bool Fill { get; init; } = true;
        // ID every loot medkit must contain at least once. defaults to AI-2.
        [JsonPropertyName("guaranteedTpl")] public string GuaranteedTpl { get; init; } = "5755356824597772cb798962";
        // pool of IDs eligible for random extras. null/empty -> use top-level Filter.
        [JsonPropertyName("fillItems")]     public List<string>? FillItems { get; init; }
        // default ID swap map. each entry is original -> upgraded ID. used for any
        // category that doesnt have its own per-category override below.
        [JsonPropertyName("substitutions")] public Dictionary<string, string>? Substitutions { get; init; }
        // optional per-category overrides. when null, the category falls back to the
        // default Substitutions map. used to give bosses a different upgrade target than
        // their followers (e.g. bosses get MEGA, followers get Super).
        [JsonPropertyName("bossSubstitutions")]     public Dictionary<string, string>? BossSubstitutions     { get; init; }
        [JsonPropertyName("followerSubstitutions")] public Dictionary<string, string>? FollowerSubstitutions { get; init; }
        [JsonPropertyName("cultistSubstitutions")]  public Dictionary<string, string>? CultistSubstitutions  { get; init; }
        [JsonPropertyName("pmcSubstitutions")]      public Dictionary<string, string>? PmcSubstitutions      { get; init; }
        [JsonPropertyName("lootSubstitutions")]     public Dictionary<string, string>? LootSubstitutions     { get; init; }
        // probability [0..1] that a substitutable ID gets swapped, by category:
        //   boss (Reshala/Killa/etc), follower (boss guards), cultist (sectant*),
        //   pmc (bear/usec/raiders/rogues), loot (static container + loose world).
        //   regular scavs and everyone else never swap.
        [JsonPropertyName("bossSubstitutionChance")]     public float BossSubstitutionChance     { get; init; } = 1.0f;
        [JsonPropertyName("followerSubstitutionChance")] public float FollowerSubstitutionChance { get; init; } = 1.0f;
        [JsonPropertyName("pmcSubstitutionChance")]      public float PmcSubstitutionChance      { get; init; } = 0.5f;
        [JsonPropertyName("cultistSubstitutionChance")]  public float CultistSubstitutionChance  { get; init; } = 0.6f;
        [JsonPropertyName("lootSubstitutionChance")]     public float LootSubstitutionChance     { get; init; } = 0.3f;
    }

    public static MedkitContainerConfig Load()
    {
        var modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("could not resolve mod directory");
        var path = Path.Combine(modDir, "config", "medkitContainers.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"medkitContainers.json missing at {path}");

        var json = File.ReadAllText(path);
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        var cfg = JsonSerializer.Deserialize<MedkitContainerConfig>(json, opts)
            ?? throw new InvalidOperationException("medkitContainers.json deserialized to null");
        return cfg;
    }
}
