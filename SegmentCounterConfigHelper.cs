namespace EntBossHP
{
    internal static class SegmentCounterConfigHelper
    {
        public static bool EnsureDisabledMathCounterEntry(
            BossConfig config,
            string segmentCounter,
            Func<string, string, bool> namesMatch)
        {
            if (string.IsNullOrWhiteSpace(segmentCounter)) return false;

            var existingEntry = config.MathCounterList.FirstOrDefault(entry => namesMatch(segmentCounter, entry.MathCounter));
            if (existingEntry != null)
            {
                if (!existingEntry.Enabled) return false;

                existingEntry.Enabled = false;
                return true;
            }

            config.MathCounterList.Add(new MathCounterConfig
            {
                Name = segmentCounter,
                Enabled = false,
                MathCounter = segmentCounter,
                MathCounterMode = 1,
                HealthSegmentCounter = null,
                HealthSegmentCounterMode = 1,
                HpOffset = 0
            });
            return true;
        }

        public static int GetSegmentCounterHpOffset(
            BossConfig config,
            string segmentCounter,
            Func<string, string, bool> namesMatch)
        {
            if (string.IsNullOrWhiteSpace(segmentCounter)) return 0;

            return config.MathCounterList
                .FirstOrDefault(entry => namesMatch(segmentCounter, entry.MathCounter))
                ?.HpOffset ?? 0;
        }
    }
}
