using System.Text.Json.Serialization;

namespace Leaves_LEWDS;

// Mirrors config/config.jsonc.
public record LewdsConfig
{
    [JsonPropertyName("categories")]
    public required Dictionary<string, double> Categories { get; set; }

    [JsonPropertyName("items")]
    public required Dictionary<string, double> Items { get; set; }

    // Eagerly reads every map's loose loot at startup just to log spawn counts immediately,
    // instead of waiting for a raid to naturally load that map. Confirmed cost: unlike SPT 3.x
    // (whole DB loaded into memory once and kept there), SPT 4.0's LazyLoad<LooseLoot> never
    // caches - every .Value access re-reads and re-deserializes that map's (often multi-MB)
    // loot JSON from disk. Turning this on means paying that cost for all maps back-to-back at
    // boot instead of once per map on first natural use. Not fixable from mod code - it's the
    // engine's own no-cache-by-design behavior, not a bug in this loop.
    [JsonPropertyName("logAtStartup")]
    public required bool LogAtStartup { get; set; }
}
