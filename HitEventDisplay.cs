using CounterStrikeSharp.API.Core;
using System.Text;
using System.Text.RegularExpressions;

namespace EntBossHP
{
    internal partial class HitEventDisplay
    {
        private const int MaxSplits = 20;
        private const int HudTextCapacity = 192;

        [GeneratedRegex(@"_\d{3,}$")]
        private static partial Regex BossNameSuffixRegex();

        private static readonly string[] FilledBars = BuildBars('█', MaxSplits + 1);
        private static readonly string[] EmptyBars = BuildBars('░', MaxSplits + 1);

        private static string[] BuildBars(char ch, int length)
        {
            var arr = new string[length];
            for (var i = 0; i < length; i++)
            {
                arr[i] = new string(ch, i);
            }
            return arr;
        }

        private readonly EntBossHP _plugin;

        public HitEventDisplay(EntBossHP plugin)
        {
            _plugin = plugin;
        }

        private string SanitizeBossName(string entityName)
        {
            if (string.IsNullOrEmpty(entityName)) return string.Empty;
            return BossNameSuffixRegex().Replace(entityName, "_");
        }

        private string GetBossDisplayName(string entityName)
        {
            if (_plugin.BossConfigs == null) return SanitizeBossName(entityName);

            var sanitizedName = SanitizeBossName(entityName);

            if (_plugin.BossConfigs.BreakableList != null)
            {
                foreach (var breakable in _plugin.BossConfigs.BreakableList)
                {
                    if (breakable != null && MatchesDisplayName(breakable.Breakable, sanitizedName))
                        return breakable.Name ?? sanitizedName;
                }
            }

            if (_plugin.BossConfigs.MathCounterList != null)
            {
                foreach (var mathCounter in _plugin.BossConfigs.MathCounterList)
                {
                    if (mathCounter != null && MatchesDisplayName(mathCounter.MathCounter, sanitizedName))
                        return mathCounter.Name ?? sanitizedName;
                }
            }

            return sanitizedName;
        }

        public void ShowHitEvent(CCSPlayerController controller, BossData boss, int health)
        {
            if (controller == null || !controller.IsValid || boss == null) return;

            if (!boss.Enabled) return;

            var displayHealth = Math.Max(0, health + boss.HpOffset);

            if (boss.MaxHealth <= 0 || health > boss.MaxHealth)
            {
                boss.MaxHealth = health;
            }

            var maxHealth = boss.MaxHealth > 0 ? boss.MaxHealth : health;
            var displayMaxHealth = maxHealth + boss.HpOffset;
            if (displayMaxHealth <= 0) displayMaxHealth = 1;

            int p;
            if (displayHealth <= 0)
            {
                p = 0;
            }
            else
            {
                p = (displayHealth * 100 + displayMaxHealth - 1) / displayMaxHealth;
                if (p > 100) p = 100;
            }

            var healthBarCount = p <= 0 ? 0 : Math.Min(p / (100 / MaxSplits) + 1, MaxSplits);
            var filledSegment = FilledBars[healthBarCount];
            var emptySegment = EmptyBars[MaxSplits - healthBarCount];

            var builder = new StringBuilder(HudTextCapacity);

            var displayName = GetBossDisplayName(boss.BossName);
            var segmentCount = GetSegmentCount(boss);

            builder.Append("<font class='fontSize-lg' color='#FFFFFF'>").Append(displayName).Append("</font> : ");
            builder.Append("<font color='#31700'>").Append(displayHealth).Append("</font>");

            if (segmentCount >= 1)
            {
                builder.Append(" <font color='#FFFF00'>(").Append(segmentCount).Append(")</font>");
            }

            builder.Append("<br>");
            if (healthBarCount > 0)
            {
                builder.Append("<font class='fontSize-lg' color='#00FF00'>").Append(filledSegment).Append("</font>");
            }
            builder.Append("<font class='fontSize-lg' color='#363636'>").Append(emptySegment).Append("</font>");

            controller.PrintToCenterHtml(builder.ToString(), 3);
        }

        public void ShowBossDefeated(CCSPlayerController controller, BossData boss)
        {
            if (controller == null || !controller.IsValid || boss == null) return;

            var displayName = GetBossDisplayName(boss.BossName);
            var builder = new StringBuilder(HudTextCapacity);

            builder.Append("<font class='fontSize-lg' color='#888888'>").Append(displayName).Append("</font> : ");
            builder.Append("<font color='#888888'>0</font>");
            builder.Append("<br>");
            builder.Append("<font class='fontSize-lg' color='#363636'>").Append(EmptyBars[MaxSplits]).Append("</font>");

            controller.PrintToCenterHtml(builder.ToString(), 3);
        }

        private static int GetSegmentCount(BossData boss)
        {
            return boss switch
            {
                MathCounterBoss m when m.IsSegmented && m.HealthSegments >= 0 => m.HealthSegments,
                BreakableBoss b when b.IsSegmented && b.HealthSegments >= 0 => b.HealthSegments,
                _ => -1,
            };
        }

        private static bool MatchesDisplayName(string configuredName, string sanitizedName)
        {
            return !string.IsNullOrEmpty(configuredName)
                && !string.IsNullOrEmpty(sanitizedName)
                && configuredName.StartsWith(sanitizedName, StringComparison.Ordinal);
        }
    }
}
