using System.Text.Json.Serialization;

namespace EntBossHP
{
    public class BossConfig
    {
        [JsonPropertyName("Breakable")]
        public List<BreakableConfig> BreakableList { get; set; } = [];

        [JsonPropertyName("MathCounter")]
        public List<MathCounterConfig> MathCounterList { get; set; } = [];

        [JsonPropertyName("HPBar")]
        [Obsolete("HPBar is deprecated. Use MathCounter with health_segment_counter instead.")]
        public List<HPBarConfig> HPBarList { get; set; } = [];
    }

    public class BreakableConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("breakable")]
        public string Breakable { get; set; } = string.Empty;

        [JsonPropertyName("health_segment_counter")]
        public string? HealthSegmentCounter { get; set; }

        [JsonPropertyName("health_segment_counter_mode")]
        public int HealthSegmentCounterMode { get; set; } = 1;

        [JsonPropertyName("hp_offset")]
        public int HpOffset { get; set; } = 0;
    }

    public class MathCounterConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("mathcounter")]
        public string MathCounter { get; set; } = string.Empty;

        [JsonPropertyName("mathcounter_mode")]
        public int MathCounterMode { get; set; } = 1;

        [JsonPropertyName("health_segment_counter")]
        public string? HealthSegmentCounter { get; set; }

        [JsonPropertyName("health_segment_counter_mode")]
        public int HealthSegmentCounterMode { get; set; } = 1;

        [JsonPropertyName("hp_offset")]
        public int HpOffset { get; set; } = 0;
    }

    public class HPBarConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("mathcounter")]
        public string MathCounter { get; set; } = string.Empty;

        [JsonPropertyName("mathcounter_mode")]
        public int MathCounterMode { get; set; } = 1;

        [JsonPropertyName("iterator")]
        public string Iterator { get; set; } = string.Empty;

        [JsonPropertyName("iterator_mode")]
        public int IteratorMode { get; set; } = 1;

        [JsonPropertyName("backup")]
        public string Backup { get; set; } = string.Empty;

        [JsonPropertyName("hp_offset")]
        public int HpOffset { get; set; } = 0;
    }
}
