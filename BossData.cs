using CounterStrikeSharp.API.Core;

namespace EntBossHP
{
    public enum BossType
    {
        Invalid = -1,
        Breakable = 0,
        MathCounter = 1,
        HPBar = 2,
    }

    public class BossData
    {
        public string BossName { get; set; } = string.Empty;
        public int Health { get; set; }
        public int MaxHealth { get; set; }
        public int LastHP { get; set; }
        public double LastHit { get; set; }
        public BossType Type { get; set; }
        public bool Enabled { get; set; }
        public int HpOffset { get; set; } = 0;
        public bool DefeatPending { get; set; }

    }

    public abstract class SegmentedBossData : BossData
    {
        public bool IsSegmented { get; set; }
        public string HealthSegmentCounterName { get; set; } = string.Empty;
        public CEntityInstance? HealthSegmentCounterEntity { get; set; }
        public int HealthSegments { get; set; }
        public int TotalHealthSegments { get; set; }
        public int HealthSegmentCounterMode { get; set; } = 1;
    }

    public class BreakableBoss : SegmentedBossData
    {
        public CEntityInstance? BreakableEntity { get; set; }
        public string BreakableEntityName { get; set; } = string.Empty;
    }

    public class MathCounterBoss : SegmentedBossData
    {
        public CEntityInstance? MathCounterEntity { get; set; }
        public int MathCounterHitMode { get; set; } = -1;
        public string MathCounterName { get; set; } = string.Empty;
        public int MathCounterStartValue { get; set; }
        public int MathCounterMaxValue { get; set; }
        public int MathCounterMinValue { get; set; }
    }
}
