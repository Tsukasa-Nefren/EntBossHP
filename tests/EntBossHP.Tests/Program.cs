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

Run("runtime auto segment learner learns overlay-named segment from behavior", () =>
{
    var learner = new RuntimeAutoSegmentLearner();
    learner.TrackNewMain("Boss_Health", 0.0);
    var mark = learner.MarkMainHitMin("Boss_Health", 1.0);

    var observation = learner.ObserveCounter("Boss_Overlay_Counter", 8, 7, 7, 1.2, NamesMatch);
    var decision = learner.Finalize("Boss_Health", mark.AttemptId, 1.5);

    AssertBool(true, observation.SuppressAutoCreate, "suppress segment auto create");
    AssertEqual((int)RuntimeAutoSegmentDecisionStatus.Learned, (int)decision.Status, "status");
    AssertString("Boss_Overlay_Counter", decision.SegmentCounter, "segment");
    AssertEqual(2, decision.Mode, "mode");
});

Run("runtime auto segment learner prefers phase counter over visual hp bar", () =>
{
    var learner = new RuntimeAutoSegmentLearner();
    learner.TrackNewMain("Demonwal_attackhp", 0.0);
    var mark = learner.MarkMainHitMin("Demonwal_attackhp", 1.0);

    learner.ObserveCounter("Demonwal_shigehp", 9, 10, 10, 1.1, NamesMatch);
    learner.ObserveCounter("Demonwal_shigemathcounter", 1, 0, 11, 1.2, NamesMatch);
    var decision = learner.Finalize("Demonwal_attackhp", mark.AttemptId, 1.5);

    AssertEqual((int)RuntimeAutoSegmentDecisionStatus.Learned, (int)decision.Status, "status");
    AssertString("Demonwal_shigemathcounter", decision.SegmentCounter, "segment");
    AssertEqual(2, decision.Mode, "mode");
});

Run("runtime auto segment learner suppresses but does not learn low-confidence auxiliary candidates", () =>
{
    var learner = new RuntimeAutoSegmentLearner();
    learner.TrackNewMain("boss_hp", 0.0);
    var mark = learner.MarkMainHitMin("boss_hp", 1.0);

    var observation = learner.ObserveCounter("boss_attack_counter", 1, 0, 5, 1.2, NamesMatch);
    var decision = learner.Finalize("boss_hp", mark.AttemptId, 1.5);

    AssertBool(true, observation.SuppressAutoCreate, "suppress segment auto create");
    AssertEqual((int)RuntimeAutoSegmentDecisionStatus.None, (int)decision.Status, "status");
});

Run("new math counter auto enable is limited to likely boss hp counters", () =>
{
    AssertBool(true, global::EntBossHP.EntBossHP.ShouldAutoEnableNewMathCounter("nut_boss_hp", 300, 60000), "nut boss hp");
    AssertBool(false, global::EntBossHP.EntBossHP.ShouldAutoEnableNewMathCounter("Scathing_Counter", 800, 800), "scathing mechanic");
    AssertBool(false, global::EntBossHP.EntBossHP.ShouldAutoEnableNewMathCounter("laser_Exdeath_Hp", 55, 999999), "laser hp");
    AssertBool(false, global::EntBossHP.EntBossHP.ShouldAutoEnableNewMathCounter("Item_SGE_math", 4, 4), "item counter");
    AssertBool(false, global::EntBossHP.EntBossHP.ShouldAutoEnableNewMathCounter("Boss_S4_Hp_Counter", 11, 30), "small segment counter");
});

Run("generated numeric breakable suffixes share one configured family", () =>
{
    AssertBool(true, global::EntBossHP.EntBossHP.NamesMatchGeneratedNumericSuffixFamily("pillar_break_1", "pillar_break_2"), "pillar family");
    AssertBool(true, global::EntBossHP.EntBossHP.NamesMatchGeneratedNumericSuffixFamily("boss_block_14", "boss_block_2"), "boss block family");
    AssertBool(false, global::EntBossHP.EntBossHP.NamesMatchGeneratedNumericSuffixFamily("boss_1", "boss_2"), "generic numbered bosses");
    AssertBool(false, global::EntBossHP.EntBossHP.NamesMatchGeneratedNumericSuffixFamily("lv1_boss_hp", "lv3_boss_hp"), "stage hp");
    AssertBool(false, global::EntBossHP.EntBossHP.NamesMatchGeneratedNumericSuffixFamily("Boss_Health_Suzaku", "Boss_Health_Chaos"), "named bosses");
});

Run("new breakable auto enable requires bounded health and damage evidence", () =>
{
    AssertBool(true, global::EntBossHP.EntBossHP.ShouldAutoEnableNewBreakable("pillar_break_1", 3500, 3300, 3500, 0, true, 0), "pillar damage");
    AssertBool(true, global::EntBossHP.EntBossHP.ShouldAutoEnableNewBreakable("door12", 450, 450, 450, 500, false, 0), "engine max damage evidence");
    AssertBool(false, global::EntBossHP.EntBossHP.ShouldAutoEnableNewBreakable("glass", 80, 60, 80, 0, true, 0), "tiny breakable");
    AssertBool(false, global::EntBossHP.EntBossHP.ShouldAutoEnableNewBreakable("boss_hitbox", 9999999, 9999900, 9999999, 9999999, true, 0), "dummy health");
    AssertBool(false, global::EntBossHP.EntBossHP.ShouldAutoEnableNewBreakable("Item_Emiya_Box", 500, 450, 500, 0, true, 0), "item box");
    AssertBool(false, global::EntBossHP.EntBossHP.ShouldAutoEnableNewBreakable("laser_wall", 500, 450, 500, 0, true, 0), "laser wall");
    AssertBool(false, global::EntBossHP.EntBossHP.ShouldAutoEnableNewBreakable("box 2", 200, 150, 200, 0, true, 0), "generic box");
    AssertBool(false, global::EntBossHP.EntBossHP.ShouldAutoEnableNewBreakable("door12", 450, 450, 450, 0, false, 0), "no damage evidence");
    AssertBool(false, global::EntBossHP.EntBossHP.ShouldAutoEnableNewBreakable("pillar_break_1", 3500, 3300, 3500, 0, true, 8), "map cap reached");
});

Run("display name resolution uses exact match before longest prefix", () =>
{
    var config = new BossConfig
    {
        MathCounterList =
        [
            new() { Name = "Generic", MathCounter = "Boss_Health" },
            new() { Name = "Suzaku", MathCounter = "Boss_Health_Suzaku" }
        ]
    };

    AssertString("Suzaku", HitEventDisplay.ResolveDisplayName(config, "Boss_Health_Suzaku"), "exact specific");
    AssertString("Generic", HitEventDisplay.ResolveDisplayName(config, "Boss_Health_001"), "template prefix");
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
