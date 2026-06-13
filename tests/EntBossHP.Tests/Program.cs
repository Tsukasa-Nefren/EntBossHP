using System.Reflection;
using EntBossHP;

var failures = new List<string>();

Run("math counter death updates stored health before confirmation", () =>
{
    var boss = new MathCounterBoss
    {
        Health = 250,
        MathCounterHitMode = 1,
        MathCounterMaxValue = 250
    };

    var currentHp = ApplyMathCounterValue(boss, 0);

    AssertEqual(0, currentHp, "current hp");
    AssertEqual(0, boss.Health, "stored health");
});

Run("reverse math counter death updates stored health before confirmation", () =>
{
    var boss = new MathCounterBoss
    {
        Health = 250,
        MathCounterHitMode = 2,
        MathCounterMaxValue = 250
    };

    var currentHp = ApplyMathCounterValue(boss, 250);

    AssertEqual(0, currentHp, "current hp");
    AssertEqual(0, boss.Health, "stored health");
});

Run("negative breakable hp offset uses observed raw hp as display max baseline", () =>
{
    var boss = new BreakableBoss
    {
        HpOffset = -10000
    };

    UpdateBreakableMaxHealth(boss, 11000, 20000);

    AssertEqual(11000, boss.MaxHealth, "max health");
    AssertEqual(1000, boss.MaxHealth + boss.HpOffset, "display max health");
});

Run("existing math counters are rescanned whenever a map is loaded", () =>
{
    AssertBool(true, ShouldRescanExistingMathCounters(true, "ze_test"), "hot reload with map");
    AssertBool(true, ShouldRescanExistingMathCounters(false, "ze_test"), "cold load with map");
    AssertBool(false, ShouldRescanExistingMathCounters(true, ""), "hot reload without map");
    AssertBool(false, ShouldRescanExistingMathCounters(true, "   "), "hot reload with blank map");
});

Run("runtime auto segment learner learns subtract segment for newly detected boss only", () =>
{
    var learner = new RuntimeAutoSegmentLearner();
    learner.TrackNewMain("nut_boss_hp", 0.0);
    var mark = learner.MarkMainHitMin("nut_boss_hp", 1.0);

    var observation = learner.ObserveCounter("nut_boss_segment", 9, 10, 10, 1.2, NamesMatch);
    var decision = learner.Finalize("nut_boss_hp", mark.AttemptId, 1.5);

    AssertBool(true, observation.SuppressAutoCreate, "suppress segment auto create");
    AssertEqual((int)RuntimeAutoSegmentDecisionStatus.Learned, (int)decision.Status, "status");
    AssertString("nut_boss_hp", decision.MainCounter, "main");
    AssertString("nut_boss_segment", decision.SegmentCounter, "segment");
    AssertEqual(1, decision.Mode, "mode");
});

Run("runtime auto segment learner learns add-up segment when previous value is unknown", () =>
{
    var learner = new RuntimeAutoSegmentLearner();
    learner.TrackNewMain("boss_hp", 0.0);
    var mark = learner.MarkMainHitMin("boss_hp", 1.0);

    var observation = learner.ObserveCounter("boss_phase_counter", 1, null, 5, 1.2, NamesMatch);
    var decision = learner.Finalize("boss_hp", mark.AttemptId, 1.5);

    AssertBool(true, observation.SuppressAutoCreate, "suppress segment auto create");
    AssertEqual((int)RuntimeAutoSegmentDecisionStatus.Learned, (int)decision.Status, "status");
    AssertString("boss_phase_counter", decision.SegmentCounter, "segment");
    AssertEqual(2, decision.Mode, "mode");
});

Run("runtime auto segment learner does not learn without newly tracked main", () =>
{
    var learner = new RuntimeAutoSegmentLearner();

    var observation = learner.ObserveCounter("nut_boss_segment", 9, 10, 10, 1.2, NamesMatch);
    var decision = learner.Finalize("nut_boss_hp", 1, 1.5);

    AssertBool(false, observation.SuppressAutoCreate, "suppress segment auto create");
    AssertEqual((int)RuntimeAutoSegmentDecisionStatus.None, (int)decision.Status, "status");
});

Run("runtime auto segment learner skips ambiguous segment candidates", () =>
{
    var learner = new RuntimeAutoSegmentLearner();
    learner.TrackNewMain("boss_hp", 0.0);
    var mark = learner.MarkMainHitMin("boss_hp", 1.0);

    learner.ObserveCounter("segment_a", 9, 10, 10, 1.1, NamesMatch);
    learner.ObserveCounter("segment_b", 9, 10, 10, 1.2, NamesMatch);
    var decision = learner.Finalize("boss_hp", mark.AttemptId, 1.5);

    AssertEqual((int)RuntimeAutoSegmentDecisionStatus.Ambiguous, (int)decision.Status, "status");
    AssertString(null, decision.SegmentCounter, "segment");
});

Run("runtime auto segment learner ignores dangerous candidate names", () =>
{
    var learner = new RuntimeAutoSegmentLearner();
    learner.TrackNewMain("boss_hp", 0.0);
    var mark = learner.MarkMainHitMin("boss_hp", 1.0);

    var observation = learner.ObserveCounter("boss_attack_counter", 1, 0, 5, 1.2, NamesMatch);
    var decision = learner.Finalize("boss_hp", mark.AttemptId, 1.5);

    AssertBool(false, observation.SuppressAutoCreate, "suppress segment auto create");
    AssertEqual((int)RuntimeAutoSegmentDecisionStatus.None, (int)decision.Status, "status");
});

Run("segment counter helper creates disabled editable config entry", () =>
{
    var config = new BossConfig
    {
        MathCounterList =
        [
            new()
            {
                Name = "boss_hp",
                MathCounter = "boss_hp",
                HealthSegmentCounter = "boss_phase_counter",
                HealthSegmentCounterMode = 2
            }
        ]
    };

    var changed = SegmentCounterConfigHelper.EnsureDisabledMathCounterEntry(config, "boss_phase_counter", NamesMatch);

    AssertBool(true, changed, "changed");
    AssertEqual(2, config.MathCounterList.Count, "math counter count");
    AssertString("boss_phase_counter", config.MathCounterList[1].MathCounter, "segment mathcounter");
    AssertBool(false, config.MathCounterList[1].Enabled, "segment enabled");
});

Run("segment counter helper disables existing segment config entry without changing offset", () =>
{
    var config = new BossConfig
    {
        MathCounterList =
        [
            new()
            {
                Name = "boss_healthcount",
                MathCounter = "boss_healthcount",
                Enabled = true,
                HpOffset = -2
            }
        ]
    };

    var changed = SegmentCounterConfigHelper.EnsureDisabledMathCounterEntry(config, "boss_healthcount", NamesMatch);

    AssertBool(true, changed, "changed");
    AssertEqual(1, config.MathCounterList.Count, "math counter count");
    AssertBool(false, config.MathCounterList[0].Enabled, "segment enabled");
    AssertEqual(-2, config.MathCounterList[0].HpOffset, "offset");
});

Run("segment counter helper keeps editable entry offset for segment display", () =>
{
    var config = new BossConfig
    {
        MathCounterList =
        [
            new()
            {
                Name = "boss_hp",
                MathCounter = "boss_hp",
                HealthSegmentCounter = "boss_phase_counter",
                HealthSegmentCounterMode = 2
            },
            new()
            {
                Name = "boss_phase_counter",
                MathCounter = "boss_phase_counter",
                Enabled = false,
                HpOffset = -1
            }
        ]
    };

    var offset = SegmentCounterConfigHelper.GetSegmentCounterHpOffset(config, "boss_phase_counter", NamesMatch);
    var boss = new MathCounterBoss
    {
        TotalHealthSegments = 5,
        HealthSegmentCounterMode = 2,
        HealthSegmentCounterHpOffset = offset
    };

    UpdateSegmentHealthFromCounter(boss, 1);

    AssertEqual(-1, offset, "offset");
    AssertEqual(3, boss.HealthSegments, "health segments");
});

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine("All EntBossHP focused tests passed.");
return 0;

void Run(string name, Action test)
{
    try
    {
        test();
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
    }
}

static int ApplyMathCounterValue(MathCounterBoss boss, float counterValue)
{
    var method = typeof(global::EntBossHP.EntBossHP).GetMethod(
        "ApplyMathCounterValue",
        BindingFlags.NonPublic | BindingFlags.Static);

    if (method is null)
    {
        throw new InvalidOperationException("Expected private static ApplyMathCounterValue helper to exist.");
    }

    return (int)method.Invoke(null, [boss, counterValue])!;
}

static bool ShouldRescanExistingMathCounters(bool hotReload, string? mapName)
{
    var method = typeof(global::EntBossHP.EntBossHP).GetMethod(
        "ShouldRescanExistingMathCounters",
        BindingFlags.NonPublic | BindingFlags.Static);

    if (method is null)
    {
        throw new InvalidOperationException("Expected private static ShouldRescanExistingMathCounters helper to exist.");
    }

    return (bool)method.Invoke(null, [hotReload, mapName])!;
}

static void UpdateBreakableMaxHealth(BreakableBoss boss, int hp, int engineMaxHealth)
{
    var method = typeof(global::EntBossHP.EntBossHP).GetMethod(
        "UpdateBreakableMaxHealth",
        BindingFlags.NonPublic | BindingFlags.Static);

    if (method is null)
    {
        throw new InvalidOperationException("Expected private static UpdateBreakableMaxHealth helper to exist.");
    }

    method.Invoke(null, [boss, hp, engineMaxHealth]);
}

static void UpdateSegmentHealthFromCounter(SegmentedBossData boss, int counterValue)
{
    var method = typeof(global::EntBossHP.EntBossHP).GetMethod(
        "UpdateSegmentHealthFromCounter",
        BindingFlags.NonPublic | BindingFlags.Static);

    if (method is null)
    {
        throw new InvalidOperationException("Expected private static UpdateSegmentHealthFromCounter helper to exist.");
    }

    method.Invoke(null, [boss, counterValue]);
}

static void AssertEqual(int expected, int actual, string label)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}

static void AssertBool(bool expected, bool actual, string label)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}

static bool NamesMatch(string firstName, string secondName)
{
    if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(secondName)) return false;
    if (firstName.Equals(secondName, StringComparison.Ordinal)) return true;
    return Sanitize(firstName).Equals(secondName, StringComparison.Ordinal)
        || Sanitize(secondName).Equals(firstName, StringComparison.Ordinal);
}

static string Sanitize(string entityName)
{
    return System.Text.RegularExpressions.Regex.Replace(entityName, @"_\d{3,}$", "_");
}

static void AssertString(string? expected, string? actual, string label)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{label}: expected {expected ?? "<null>"}, got {actual ?? "<null>"}");
    }
}
