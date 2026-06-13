using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using System.Globalization;
using PlayerSettings;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Core.Capabilities;
using static CounterStrikeSharp.API.Core.Listeners;

namespace EntBossHP
{
    [MinimumApiVersion(369)]
    public partial class EntBossHP : BasePlugin
    {
        private const ulong SteamId64Base = 76561197960265728UL;
        private const uint InvalidAccountId = uint.MaxValue;
        private const float MathCounterDefeatConfirmationDelay = 1.35f;
        private const float BreakableDefeatConfirmationDelay = 0.3f;
        private const float AutoSegmentLearningWindow = 1.0f;

        [GeneratedRegex(@"_\d{3,}$")]
        private static partial Regex BossNameSuffixRegex();

        public override string ModuleName => "EntBossHP";
        public override string ModuleVersion => "2.1.13";
        public override string ModuleAuthor => "Oylsister, Credits to Kxrnl, DarkerZ [RUS] / modified by Tsukasa";
        
        public string PluginConfigDirectory => Path.Combine(ModuleDirectory, "..", "..", "configs", "plugins", ModuleName);
        private string PlayerSettingsPath => Path.Combine(PluginConfigDirectory, "player_settings.json");

        private readonly List<BreakableBoss> _breakableBosses = [];
        private readonly List<MathCounterBoss> _mathCounterBosses = [];
        private readonly Dictionary<string, float> _mathCounterValuesByName = new(StringComparer.Ordinal);
        private readonly RuntimeAutoSegmentLearner _autoSegmentLearner = new(AutoSegmentLearningWindow);

        private static readonly System.Threading.SemaphoreSlim SaveLock = new(1, 1);
        private static long SaveGeneration;
        private CCSGameRulesProxy? _gameRulesProxy;
        private bool configLoaded = false;

        private static readonly PluginCapability<ISettingsApi?> SettingsCapability = new("settings:nfcore");
        private PlayerPreferenceService _playerPreferenceService = null!;

        internal BossConfig BossConfigs { get; private set; } = new();

        private HitEventDisplay HitEventDisplay { get; set; } = null!;

        // Store delegates using the correct EntityOutputHandler type to prevent GC collection
        private EntityIO.EntityOutputHandler? _counterOutDelegate;
        private EntityIO.EntityOutputHandler? _physboxMultiplayerDamagedDelegate;
        private EntityIO.EntityOutputHandler? _physboxHealthChangedDelegate;
        private EntityIO.EntityOutputHandler? _breakableHealthChangedDelegate;
        private EntityIO.EntityOutputHandler? _hitboxHookDelegate;

        public override void Load(bool hotReload)
        {
            _playerPreferenceService = new PlayerPreferenceService(() => SettingsCapability.Get(), true);
            HitEventDisplay = new(this);

            _counterOutDelegate = CounterOut;
            _physboxMultiplayerDamagedDelegate = BreakableOut_PhysboxMultiplayerOnDamaged;
            _physboxHealthChangedDelegate = BreakableOut_PhysboxOnHealthChanged;
            _breakableHealthChangedDelegate = BreakableOut_FuncBreakableOnHealthChanged;
            _hitboxHookDelegate = Hitbox_Hook;

            HookEntityOutput("math_counter", "OutValue", _counterOutDelegate);
            HookEntityOutput("func_physbox_multiplayer", "OnDamaged", _physboxMultiplayerDamagedDelegate);
            HookEntityOutput("func_physbox", "OnHealthChanged", _physboxHealthChangedDelegate);
            HookEntityOutput("func_breakable", "OnHealthChanged", _breakableHealthChangedDelegate);
            HookEntityOutput("prop_dynamic", "OnHealthChanged", _hitboxHookDelegate);

            RegisterEventHandler<EventRoundStart>(OnRoundStart);
            RegisterListener<OnMapStart>(MapStart);
            RegisterListener<OnMapEnd>(MapEnd);
            RegisterListener<OnEntityCreated>(OnEntityCreated);
            RegisterListener<OnTick>(OnTick);
            RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

            AddCommand("boss_list", "", CommandBossList);
            AddCommand("css_bhud", "Toggle boss HP HUD", CommandBossHud);

            if (hotReload)
            {
                if (ShouldRescanExistingMathCounters(hotReload, Server.MapName))
                {
                    MapStart(Server.MapName);
                }
            }
        }

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            if (!configLoaded && !string.IsNullOrWhiteSpace(Server.MapName))
            {
                MapStart(Server.MapName);
            }
        }

        public override void Unload(bool hotReload)
        {
            SafeUnhookEntityOutput("math_counter", "OutValue", _counterOutDelegate);
            SafeUnhookEntityOutput("func_physbox_multiplayer", "OnDamaged", _physboxMultiplayerDamagedDelegate);
            SafeUnhookEntityOutput("func_physbox", "OnHealthChanged", _physboxHealthChangedDelegate);
            SafeUnhookEntityOutput("func_breakable", "OnHealthChanged", _breakableHealthChangedDelegate);
            SafeUnhookEntityOutput("prop_dynamic", "OnHealthChanged", _hitboxHookDelegate);
            SafeDeregisterEventHandler<EventRoundStart>(OnRoundStart);
            SafeDeregisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
            SafeRemoveListener<OnMapStart>(MapStart);
            SafeRemoveListener<OnMapEnd>(MapEnd);
            SafeRemoveListener<OnEntityCreated>(OnEntityCreated);
            SafeRemoveListener<OnTick>(OnTick);
            ClearRuntimeState();
        }

        private void MapEnd() => ClearRuntimeState();

        private static bool ShouldRescanExistingMathCounters(bool hotReload, string? mapName)
        {
            return !string.IsNullOrWhiteSpace(mapName);
        }

        private void ScheduleExistingMathCounterRescan()
        {
            AddTimer(0.1f, RescanExistingMathCounters, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void RescanExistingMathCounters()
        {
            if (!configLoaded) return;

            try
            {
                foreach (var counter in Utilities.FindAllEntitiesByDesignerName<CMathCounter>("math_counter"))
                {
                    if (counter is not { IsValid: true }) continue;
                    if (!string.Equals(counter.DesignerName, "math_counter", StringComparison.Ordinal)) continue;

                    Timer_MathCounterInitial(counter);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error while rescanning existing math_counter entities");
            }
        }

        private void ClearRuntimeState()
        {
            ResetBossHP();
            _breakableBosses.Clear();
            _mathCounterBosses.Clear();
            _mathCounterValuesByName.Clear();
            _autoSegmentLearner.Clear();
            _gameRulesProxy = null;
            configLoaded = false;
        }

        private void SafeUnhookEntityOutput(string classname, string outputName, EntityIO.EntityOutputHandler? handler)
        {
            if (handler is null) return;
            try { UnhookEntityOutput(classname, outputName, handler); }
            catch (Exception ex) { Logger.LogDebug(ex, "Failed to unhook {Class}.{Output}", classname, outputName); }
        }

        private void SafeDeregisterEventHandler<T>(BasePlugin.GameEventHandler<T> handler) where T : GameEvent
        {
            try { DeregisterEventHandler(handler); }
            catch (Exception ex) { Logger.LogDebug(ex, "Failed to deregister event handler {Event}", typeof(T).Name); }
        }

        private void SafeRemoveListener<T>(T handler) where T : Delegate
        {
            try { RemoveListener(handler); }
            catch (Exception ex) { Logger.LogDebug(ex, "Failed to remove listener {Listener}", typeof(T).Name); }
        }

        private HookResult BreakableOut_PhysboxMultiplayerOnDamaged(CEntityIOOutput o, string n, CEntityInstance a, CEntityInstance c, CVariant v, float d)
            => BreakableOut(o, n, a, c, v, d);

        private HookResult BreakableOut_PhysboxOnHealthChanged(CEntityIOOutput o, string n, CEntityInstance a, CEntityInstance c, CVariant v, float d)
            => BreakableOut(o, n, a, c, v, d);

        private HookResult BreakableOut_FuncBreakableOnHealthChanged(CEntityIOOutput o, string n, CEntityInstance a, CEntityInstance c, CVariant v, float d)
            => BreakableOut(o, n, a, c, v, d);
        
        private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            if (@event.Userid is { IsValid: true } player && player.AuthorizedSteamID?.SteamId64 is > 76561197960265728UL)
            {
                _playerPreferenceService.Remove(player.AuthorizedSteamID.SteamId64);
            }
            return HookResult.Continue;
        }
        
        private string SanitizeBossName(string entityName)
        {
            if (string.IsNullOrEmpty(entityName)) return string.Empty;
            return BossNameSuffixRegex().Replace(entityName, "_");
        }

        private void MapStart(string mapname)
        {
            _gameRulesProxy = null;
            LoadConfigBasedMap(mapname);
            if (ShouldRescanExistingMathCounters(false, mapname))
            {
                ScheduleExistingMathCounterRescan();
            }
        }

        private void OnTick()
        {
            try
            {
                if (_gameRulesProxy is not { IsValid: true })
                {
                    _gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
                }

                var gameRules = _gameRulesProxy?.GameRules;
                if (gameRules != null)
                {
                    gameRules.GameRestart = gameRules.RestartRoundTime < Server.CurrentTime;
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to update game restart state");
            }
        }

        private void LoadConfigBasedMap(string mapname)
        {
            var configPath = Path.Combine(PluginConfigDirectory, $"{mapname}.jsonc");
            var configDirectory = Path.GetDirectoryName(configPath);
            if (configDirectory != null && !Directory.Exists(configDirectory))
            {
                Directory.CreateDirectory(configDirectory);
            }

            if (!File.Exists(configPath))
            {
                Logger.LogInformation($"Couldn't Find {configPath}, creating new config.");
                BossConfigs = new();
            }
            else
            {
                try
                {
                    BossConfigs = JsonSerializer.Deserialize<BossConfig>(File.ReadAllText(configPath), new JsonSerializerOptions
                    {
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    }) ?? new();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to load boss config from {ConfigPath}, using default", configPath);
                    BossConfigs = new();
                }
            }
            Logger.LogInformation($"Loaded Boss Config {configPath}");
            configLoaded = true;
            if (EnsureConfiguredSegmentCounterEntries())
            {
                SaveChanges();
            }

            BossDataLoading();
        }

        private bool EnsureConfiguredSegmentCounterEntries()
        {
            var changed = false;

            var segmentCounters = BossConfigs.MathCounterList
                         .Select(entry => entry.HealthSegmentCounter)
                         .Concat(BossConfigs.BreakableList.Select(entry => entry.HealthSegmentCounter))
                         .Where(segmentCounter => !string.IsNullOrWhiteSpace(segmentCounter))
                         .Distinct(StringComparer.Ordinal)
                         .ToList();

            foreach (var segmentCounter in segmentCounters)
            {
                changed |= SegmentCounterConfigHelper.EnsureDisabledMathCounterEntry(
                    BossConfigs,
                    segmentCounter!,
                    NamesMatchEitherDirection);
            }

            return changed;
        }

        private void BossDataLoading()
        {
            _breakableBosses.Clear();
            _mathCounterBosses.Clear();

            foreach (var breakable in BossConfigs.BreakableList)
            {
                var boss = new BreakableBoss
                {
                    BossName = breakable.Name,
                    Enabled = breakable.Enabled,
                    BreakableEntityName = breakable.Breakable,
                    HpOffset = breakable.HpOffset,
                };
                if (!string.IsNullOrEmpty(breakable.HealthSegmentCounter))
                {
                    boss.IsSegmented = true;
                    boss.HealthSegmentCounterName = breakable.HealthSegmentCounter;
                    boss.HealthSegmentCounterMode = breakable.HealthSegmentCounterMode;
                }
                _breakableBosses.Add(boss);
            }

            foreach (var mathcounter in BossConfigs.MathCounterList)
            {
                _mathCounterBosses.Add(CreateLiveMathCounterBoss(mathcounter));
            }
        }

        private MathCounterBoss CreateLiveMathCounterBoss(MathCounterConfig config)
        {
            var boss = new MathCounterBoss
            {
                BossName = config.Name,
                Enabled = config.Enabled,
                MathCounterHitMode = config.MathCounterMode,
                MathCounterName = config.MathCounter,
                HpOffset = config.HpOffset,
            };
            ApplySegmentConfigToBoss(config, boss);
            return boss;
        }

        private void ApplySegmentConfigToBoss(MathCounterConfig config, MathCounterBoss boss)
        {
            if (string.IsNullOrWhiteSpace(config.HealthSegmentCounter)) return;

            boss.IsSegmented = true;
            boss.HealthSegmentCounterName = config.HealthSegmentCounter;
            boss.HealthSegmentCounterMode = config.HealthSegmentCounterMode;
            boss.HealthSegmentCounterHpOffset = SegmentCounterConfigHelper.GetSegmentCounterHpOffset(
                BossConfigs,
                config.HealthSegmentCounter,
                NamesMatchEitherDirection);
        }

        private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            if (configLoaded)
            {
                Server.PrintToChatAll($" {ChatColors.Olive}[{ChatColors.Lime}EntBossHP{ChatColors.Olive}] {ChatColors.White}The current map is supported by this plugin.");
                ResetBossHP();
            }
            return HookResult.Continue;
        }

        private void ResetBossHP()
        {
            _mathCounterValuesByName.Clear();

            foreach (var boss in _breakableBosses)
            {
                boss.Health = 0;
                boss.MaxHealth = 0;
                boss.BreakableEntity = null;
                ResetSegmentRuntime(boss);
                boss.DefeatPending = false;
                boss.Defeated = false;
            }
            foreach (var boss in _mathCounterBosses)
            {
                boss.Health = 0;
                boss.MaxHealth = 0;
                boss.MathCounterEntity = null;
                ResetSegmentRuntime(boss);
                boss.DefeatPending = false;
                boss.Defeated = false;
            }
        }

        private static void ResetSegmentRuntime(SegmentedBossData boss)
        {
            boss.HealthSegmentCounterEntity = null;
            boss.HealthSegments = 0;
            boss.TotalHealthSegments = 0;
        }

        private void OnEntityCreated(CEntityInstance entity)
        {
            if (!configLoaded || entity == null || !entity.IsValid || entity.DesignerName != "math_counter") return;
            AddTimer(0.1f, () => {
                if (entity.IsValid) Timer_MathCounterInitial(entity);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void CommandBossList(CCSPlayerController? client, CommandInfo info)
        {
            if (client == null || !client.IsValid) return;
            foreach (var boss in BossConfigs.MathCounterList) client.PrintToConsole($"Name: {boss.Name} | Counter: {boss.MathCounter} | Mode: {boss.MathCounterMode}");
            foreach (var boss in _mathCounterBosses) client.PrintToConsole($"Name: {boss.BossName} | Counter: {boss.MathCounterName} | Mode: {boss.MathCounterHitMode}");
        }

        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        private void CommandBossHud(CCSPlayerController? client, CommandInfo info)
        {
            if (client == null || !client.IsValid) return;

            var enabled = _playerPreferenceService.ToggleHud(client);

            var state = enabled
                ? $"{ChatColors.Lime}enabled"
                : $"{ChatColors.Red}disabled";
            info.ReplyToCommand($" {ChatColors.Olive}[{ChatColors.Lime}EntBossHP{ChatColors.Olive}] {ChatColors.White}Boss HP HUD is now {state}{ChatColors.White}.");
        }
        
        private void Timer_MathCounterInitial(CEntityInstance entity)
        {
            try
            {
                var entityName = GetEntityName(entity);
                if (string.IsNullOrWhiteSpace(entityName)) return;
                foreach (var boss in _mathCounterBosses.Where(b => b.IsSegmented && MatchesEntityName(entityName, b.HealthSegmentCounterName))) InitializeSegmentCounter(boss, entity, entityName);
                foreach (var boss in _breakableBosses.Where(b => b.IsSegmented && MatchesEntityName(entityName, b.HealthSegmentCounterName))) InitializeSegmentCounter(boss, entity, entityName);
                foreach (var boss in _mathCounterBosses.Where(b => MatchesEntityName(entityName, b.MathCounterName))) InitializeMainCounter(boss, entity, entityName);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in Timer_MathCounterInitial");
            }
        }

        private void InitializeSegmentCounter(SegmentedBossData boss, CEntityInstance entity, string entityName)
        {
            boss.HealthSegmentCounterEntity = entity;
            var segmentCounter = new CMathCounter(entity.Handle);
            boss.TotalHealthSegments = Math.Max(0, (int)Math.Round(segmentCounter.Max));

            if (TryGetRememberedMathCounterValue(entityName, out var counterValue))
            {
                UpdateSegmentHealthFromCounter(boss, (int)counterValue);
            }
            else
            {
                boss.HealthSegments = Math.Max(0, boss.TotalHealthSegments + boss.HealthSegmentCounterHpOffset);
            }
        }

        private void InitializeMainCounter(MathCounterBoss boss, CEntityInstance entity, string entityName)
        {
            boss.MathCounterEntity = entity;
            var counter = new CMathCounter(entity.Handle);
            boss.MathCounterMaxValue = (int)Math.Round(counter.Max);
            if (boss.MathCounterHitMode == 0) boss.MathCounterHitMode = 1;

            if (TryGetRememberedMathCounterValue(entityName, out var counterValue))
            {
                var currentHp = ApplyMathCounterValue(boss, counterValue);
                if (currentHp > 0) UpdateMaxHealth(boss, currentHp);
            }
        }
        
        private HookResult CounterOut(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
        {
            try
            {
                if (caller == null || activator == null || activator.DesignerName != "player") return HookResult.Continue;
                var client = GetPlayerFromEntity(activator);
                if (client == null) return HookResult.Continue;

                var entityname = GetEntityName(caller);
                if (string.IsNullOrWhiteSpace(entityname)) return HookResult.Continue;
                var counterValue = value.Get<float>();
                var hadPreviousValue = TryGetRememberedMathCounterValue(entityname, out var previousCounterValue);
                var counterMaxValue = TryGetMathCounterMax(caller);
                RememberMathCounterValue(entityname, counterValue);
                var counterValueInt = (int)counterValue;
                var isSegmentCounter = configLoaded && IsConfiguredSegmentCounter(entityname);
                var autoSegmentObservation = configLoaded && !isSegmentCounter
                    ? _autoSegmentLearner.ObserveCounter(
                        entityname,
                        counterValue,
                        hadPreviousValue ? previousCounterValue : null,
                        counterMaxValue,
                        Server.CurrentTime,
                        NamesMatchEitherDirection)
                    : new RuntimeAutoSegmentObservation(false);
                UpdateSegmentCounters(caller, entityname, counterValueInt, client);

                if (isSegmentCounter || autoSegmentObservation.SuppressAutoCreate) return HookResult.Continue;

                if (configLoaded)
                {
                    if (!BossConfigs.MathCounterList.Any(b => MatchesEntityName(entityname, b.MathCounter)))
                    {
                        var sanitizedName = SanitizeBossName(entityname);
                        if (!BossConfigs.MathCounterList.Any(b => b.MathCounter == sanitizedName))
                        {
                            var newBossConfig = new MathCounterConfig { Name = sanitizedName, MathCounter = sanitizedName, MathCounterMode = 1, Enabled = (counterValue > 10), HpOffset = 0 };
                            BossConfigs.MathCounterList.Add(newBossConfig);
                            if (newBossConfig.Enabled)
                            {
                                _autoSegmentLearner.TrackNewMain(newBossConfig.MathCounter, Server.CurrentTime);
                            }
                            SaveChanges();
                            var newLiveBoss = CreateLiveMathCounterBoss(newBossConfig);
                            InitializeMainCounter(newLiveBoss, caller, entityname);
                            _mathCounterBosses.Add(newLiveBoss);
                            if (newLiveBoss.Enabled)
                            {
                                ProcessMathCounterBoss(newLiveBoss, counterValue, client);
                            }
                        }
                    }
                }

                foreach (var boss in _mathCounterBosses.Where(b => MatchesEntityName(entityname, b.MathCounterName)))
                {
                    ProcessMathCounterBoss(boss, counterValue, client);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in CounterOut");
            }
            return HookResult.Continue;
        }

        private HookResult BreakableOut(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
        {
            try
            {
                if (caller == null || activator == null || activator.DesignerName != "player") return HookResult.Continue;
                var client = GetPlayerFromEntity(activator);
                if (client == null) return HookResult.Continue;

                var prop = new CBreakable(caller.Handle);
                if (!prop.IsValid) return HookResult.Continue;

                var hp = prop.Health;
                var entityname = GetEntityName(caller);
                if (string.IsNullOrWhiteSpace(entityname)) return HookResult.Continue;
                var engineMaxHealth = 0;
                try
                {
                    engineMaxHealth = prop.MaxHealth;
                }
                catch
                {
                }

                if (configLoaded)
                {
                     var isSegmentCounter =
                         BossConfigs.MathCounterList.Any(b => !string.IsNullOrWhiteSpace(b.HealthSegmentCounter) && MatchesEntityName(entityname, b.HealthSegmentCounter)) ||
                         BossConfigs.BreakableList.Any(b => !string.IsNullOrWhiteSpace(b.HealthSegmentCounter) && MatchesEntityName(entityname, b.HealthSegmentCounter));
                     if (!isSegmentCounter && !BossConfigs.BreakableList.Any(b => MatchesEntityName(entityname, b.Breakable)))
                     {
                        var sanitizedName = SanitizeBossName(entityname);
                        if (!BossConfigs.BreakableList.Any(b => b.Breakable == sanitizedName))
                        {
                            var newBossConfig = new BreakableConfig { Name = sanitizedName, Breakable = sanitizedName, Enabled = false, HpOffset = 0 };
                            BossConfigs.BreakableList.Add(newBossConfig);
                            SaveChanges();
                            var newLiveBoss = new BreakableBoss { BossName = newBossConfig.Name, Enabled = newBossConfig.Enabled, BreakableEntityName = newBossConfig.Breakable, BreakableEntity = caller, HpOffset = newBossConfig.HpOffset };
                            newLiveBoss.Health = hp;
                            UpdateBreakableMaxHealth(newLiveBoss, hp, engineMaxHealth);
                            _breakableBosses.Add(newLiveBoss);
                            if (newLiveBoss.Enabled)
                            {
                                UpdateAndDisplayBoss(newLiveBoss, client);
                            }
                        }
                     }
                }

                foreach (var boss in _breakableBosses.Where(b => MatchesEntityName(entityname, b.BreakableEntityName)))
                {
                    if (boss.Defeated) continue;

                    boss.BreakableEntity = caller;
                    if (boss.IsSegmented && hp <= 0)
                    {
                        if (boss.HealthSegmentCounterEntity is { IsValid: true })
                        {
                            AddTimer(0.1f, () => {
                                if (boss.HealthSegmentCounterEntity is not { IsValid: true }) return;
                                HandleSegmentEnd(boss, () => {
                                    boss.MaxHealth = 0;
                                    boss.Health = 0;
                                });
                            }, TimerFlags.STOP_ON_MAPCHANGE);
                        }
                        else
                        {
                            boss.MaxHealth = 0;
                            boss.Health = 0;
                            boss.DefeatPending = false;
                        }
                        continue;
                    }
                    if (hp <= 0)
                    {
                        ScheduleDefeatConfirmationBreakable(boss);
                        continue;
                    }
                    boss.DefeatPending = false;
                    boss.Health = hp;
                    UpdateBreakableMaxHealth(boss, hp, engineMaxHealth);

                    if (boss.Enabled)
                    {
                        UpdateAndDisplayBoss(boss, client);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in BreakableOut");
            }
            return HookResult.Continue;
        }

        private void ProcessMathCounterBoss(MathCounterBoss boss, float counterValue, CCSPlayerController client)
        {
            if (boss.Defeated) return;

            var currentHp = ApplyMathCounterValue(boss, counterValue);
            if (boss.IsSegmented && boss.HealthSegmentCounterEntity is { IsValid: true } && boss.HealthSegments <= 0)
            {
                NotifyBossDefeated(boss);
                return;
            }

            if (boss.IsSegmented && currentHp <= 0)
            {
                if (boss.HealthSegmentCounterEntity is { IsValid: true })
                {
                    AddTimer(0.1f, () => {
                        if (boss.HealthSegmentCounterEntity is not { IsValid: true }) return;
                        HandleSegmentEnd(boss, () => {
                            boss.MaxHealth = 0;
                            boss.Health = 0;
                        });
                    }, TimerFlags.STOP_ON_MAPCHANGE);
                }
                else
                {
                    boss.MaxHealth = 0;
                    boss.Health = 0;
                    boss.DefeatPending = false;
                }
                return;
            }

            if (currentHp <= 0)
            {
                boss.MaxHealth = 0;
                var mark = _autoSegmentLearner.MarkMainHitMin(boss.MathCounterName, Server.CurrentTime);
                if (mark.Started)
                {
                    ScheduleAutoSegmentLearningFinalization(mark.MainCounter, mark.AttemptId);
                }
                ScheduleDefeatConfirmation(boss);
                return;
            }

            boss.DefeatPending = false;
            UpdateMaxHealth(boss, currentHp);

            if (boss.Enabled)
            {
                UpdateAndDisplayBoss(boss, client);
            }
        }

        private void UpdateSegmentCounters(CEntityInstance entity, string entityName, int counterValue, CCSPlayerController client)
        {
            foreach (var boss in _mathCounterBosses) UpdateSegmentCounter(boss, entity, entityName, counterValue, client);
            foreach (var boss in _breakableBosses) UpdateSegmentCounter(boss, entity, entityName, counterValue, client);
        }

        private void UpdateSegmentCounter(SegmentedBossData boss, CEntityInstance entity, string entityName, int counterValue, CCSPlayerController client)
        {
            if (boss.Defeated) return;
            if (!boss.IsSegmented) return;
            if (!MatchesEntityName(entityName, boss.HealthSegmentCounterName)) return;

            if (boss.HealthSegmentCounterEntity is not { IsValid: true })
            {
                InitializeSegmentCounter(boss, entity, entityName);
            }

            UpdateSegmentHealthFromCounter(boss, counterValue);
            if (boss.HealthSegments <= 0)
            {
                boss.Health = 0;
                boss.MaxHealth = 0;
                NotifyBossDefeated(boss);
                return;
            }

            if (boss.Enabled)
            {
                UpdateAndDisplayBoss(boss, client);
            }
        }

        private void HandleSegmentEnd(SegmentedBossData boss, Action resetAction)
        {
            if (boss.HealthSegmentCounterEntity is not { IsValid: true } segmentCounterEntity) return;

            if (boss.HealthSegments <= 0)
            {
                NotifyBossDefeated(boss);
                return;
            }
            resetAction.Invoke();
            
        }

        private void ScheduleDefeatConfirmation(BossData boss)
        {
            if (boss.DefeatPending) return;
            boss.DefeatPending = true;

            AddTimer(MathCounterDefeatConfirmationDelay, () => {
                if (!boss.DefeatPending) return;
                boss.DefeatPending = false;

                if (boss is MathCounterBoss { Health: > 0 }) return;

                NotifyBossDefeated(boss);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void ScheduleAutoSegmentLearningFinalization(string mainCounter, int attemptId)
        {
            AddTimer(AutoSegmentLearningWindow, () => {
                var decision = _autoSegmentLearner.Finalize(mainCounter, attemptId, Server.CurrentTime);
                ApplyRuntimeAutoSegmentDecision(decision);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void ApplyRuntimeAutoSegmentDecision(RuntimeAutoSegmentDecision decision)
        {
            if (decision.Status == RuntimeAutoSegmentDecisionStatus.None) return;

            if (decision.Status == RuntimeAutoSegmentDecisionStatus.Ambiguous)
            {
                Logger.LogWarning(
                    "Skipped runtime auto segment learning for {MainCounter}: {Reason}",
                    decision.MainCounter,
                    decision.Reason);
                return;
            }

            if (decision.Status != RuntimeAutoSegmentDecisionStatus.Learned
                || string.IsNullOrWhiteSpace(decision.MainCounter)
                || string.IsNullOrWhiteSpace(decision.SegmentCounter)
                || decision.Mode is not (1 or 2))
            {
                return;
            }

            var config = BossConfigs.MathCounterList.FirstOrDefault(b => MatchesEntityName(decision.MainCounter, b.MathCounter));
            if (config == null || !string.IsNullOrWhiteSpace(config.HealthSegmentCounter)) return;

            config.HealthSegmentCounter = decision.SegmentCounter;
            config.HealthSegmentCounterMode = decision.Mode;
            SegmentCounterConfigHelper.EnsureDisabledMathCounterEntry(
                BossConfigs,
                decision.SegmentCounter,
                NamesMatchEitherDirection);

            var liveBoss = _mathCounterBosses.FirstOrDefault(b => MatchesEntityName(decision.MainCounter, b.MathCounterName));
            if (liveBoss != null)
            {
                liveBoss.IsSegmented = true;
                liveBoss.HealthSegmentCounterName = decision.SegmentCounter;
                liveBoss.HealthSegmentCounterMode = decision.Mode;
                liveBoss.HealthSegmentCounterHpOffset = SegmentCounterConfigHelper.GetSegmentCounterHpOffset(
                    BossConfigs,
                    decision.SegmentCounter,
                    NamesMatchEitherDirection);
                TryInitializeLearnedSegmentCounter(liveBoss);

                if (!liveBoss.Defeated)
                {
                    if (liveBoss.HealthSegmentCounterEntity is { IsValid: true } && liveBoss.HealthSegments <= 0)
                    {
                        NotifyBossDefeated(liveBoss);
                    }
                    else if (liveBoss.HealthSegmentCounterEntity is { IsValid: true })
                    {
                        liveBoss.DefeatPending = false;
                    }
                }
            }

            SaveChanges();
            Logger.LogInformation(
                "Runtime auto-filled health_segment_counter for newly detected {MainCounter}: {SegmentCounter} mode {Mode}",
                decision.MainCounter,
                decision.SegmentCounter,
                decision.Mode);
        }

        private void TryInitializeLearnedSegmentCounter(MathCounterBoss boss)
        {
            try
            {
                foreach (var counter in Utilities.FindAllEntitiesByDesignerName<CMathCounter>("math_counter"))
                {
                    if (counter is not { IsValid: true }) continue;

                    var entityName = GetEntityName(counter);
                    if (!MatchesEntityName(entityName, boss.HealthSegmentCounterName)) continue;

                    InitializeSegmentCounter(boss, counter, entityName);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to initialize learned segment counter for {Boss}", boss.BossName);
            }
        }

        private void ScheduleDefeatConfirmationBreakable(BreakableBoss boss)
        {
            if (boss.DefeatPending) return;
            boss.DefeatPending = true;

            AddTimer(BreakableDefeatConfirmationDelay, () => {
                if (!boss.DefeatPending) return;
                boss.DefeatPending = false;

                if (boss.BreakableEntity is not { IsValid: true } entity)
                {
                    NotifyBossDefeated(boss);
                    return;
                }

                try
                {
                    var breakable = new CBreakable(entity.Handle);
                    if (breakable.IsValid && breakable.Health > 0) return;
                }
                catch
                {
                }

                NotifyBossDefeated(boss);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private static void UpdateBreakableMaxHealth(BreakableBoss boss, int hp, int engineMaxHealth)
        {
            if (boss.HpOffset < 0)
            {
                if (boss.MaxHealth <= 0 || hp > boss.MaxHealth)
                {
                    boss.MaxHealth = hp;
                }
                return;
            }

            if (engineMaxHealth > 0) boss.MaxHealth = engineMaxHealth;
            else if (boss.MaxHealth <= 0) boss.MaxHealth = hp;
            if (hp > boss.MaxHealth) boss.MaxHealth = hp;
        }

        private static void UpdateMaxHealth(BossData boss, int currentHp)
        {
            if (boss.MaxHealth <= 0)
            {
                boss.MaxHealth = Math.Max(currentHp, 1);
                return;
            }

            if (currentHp > boss.MaxHealth)
            {
                boss.MaxHealth = currentHp;
            }
        }

        private void NotifyBossDefeated(BossData boss)
        {
            if (boss.Defeated) return;

            boss.Defeated = true;
            boss.DefeatPending = false;
            boss.Health = 0;
            boss.MaxHealth = 0;

            if (!boss.Enabled) return;

            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid) continue;
                if (!IsBossHudEnabled(player)) continue;
                HitEventDisplay.ShowBossDefeated(player, boss);
            }
        }

        private HookResult Hitbox_Hook(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
        {
            return BreakableOut(output, name, activator, caller, value, delay);
        }

        private void UpdateAndDisplayBoss(BossData boss, CCSPlayerController client)
        {
            if (boss.Defeated) return;

            if (boss.Enabled && IsBossHudEnabled(client))
            {
                HitEventDisplay.ShowHitEvent(client, boss, boss.Health);
            }
        }

        private bool IsBossHudEnabled(CCSPlayerController client)
        {
            return _playerPreferenceService.IsHudEnabled(client);
        }


        private static CCSPlayerController? GetPlayerFromEntity(CEntityInstance? instance)
        {
            if (instance == null || instance.DesignerName != "player") return null;
            var p = instance.As<CCSPlayerPawn>();
            return (p != null && p.IsValid && p.OriginalController.Value != null && p.OriginalController.Value.IsValid) ? p.OriginalController.Value : null;
        }

        private static string GetEntityName(CEntityInstance? entity)
        {
            if (entity == null || !entity.IsValid) return string.Empty;

            try
            {
                return entity.Entity?.Name?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static float? TryGetMathCounterMax(CEntityInstance entity)
        {
            try
            {
                var counter = new CMathCounter(entity.Handle);
                return counter.IsValid ? counter.Max : null;
            }
            catch
            {
                return null;
            }
        }

        private bool MatchesEntityName(string entityName, string configuredName)
        {
            if (string.IsNullOrWhiteSpace(entityName) || string.IsNullOrWhiteSpace(configuredName)) return false;
            if (entityName.Equals(configuredName, StringComparison.Ordinal)) return true;
            return SanitizeBossName(entityName).Equals(configuredName, StringComparison.Ordinal);
        }

        private bool NamesMatchEitherDirection(string firstName, string secondName)
        {
            return MatchesEntityName(firstName, secondName) || MatchesEntityName(secondName, firstName);
        }

        private bool IsConfiguredSegmentCounter(string entityName)
        {
            return BossConfigs.MathCounterList.Any(b => !string.IsNullOrWhiteSpace(b.HealthSegmentCounter) && MatchesEntityName(entityName, b.HealthSegmentCounter)) ||
                   BossConfigs.BreakableList.Any(b => !string.IsNullOrWhiteSpace(b.HealthSegmentCounter) && MatchesEntityName(entityName, b.HealthSegmentCounter));
        }

        private void RememberMathCounterValue(string entityName, float value)
        {
            if (string.IsNullOrWhiteSpace(entityName)) return;
            _mathCounterValuesByName[entityName] = value;
            _mathCounterValuesByName[SanitizeBossName(entityName)] = value;
        }

        private bool TryGetRememberedMathCounterValue(string entityName, out float value)
        {
            if (_mathCounterValuesByName.TryGetValue(entityName, out value)) return true;

            var sanitizedName = SanitizeBossName(entityName);
            if (!sanitizedName.Equals(entityName, StringComparison.Ordinal)
                && _mathCounterValuesByName.TryGetValue(sanitizedName, out value))
            {
                return true;
            }

            value = 0;
            return false;
        }

        private static int ApplyMathCounterValue(MathCounterBoss boss, float counterValue)
        {
            var currentHp = boss.MathCounterHitMode == 1
                ? (int)counterValue
                : boss.MathCounterMaxValue - (int)counterValue;

            boss.Health = Math.Max(0, currentHp);
            return currentHp;
        }

        private static void UpdateSegmentHealthFromCounter(SegmentedBossData boss, int counterValue)
        {
            var rawHealthSegments = boss.HealthSegmentCounterMode == 2
                ? Math.Max(0, boss.TotalHealthSegments - counterValue)
                : counterValue;

            boss.HealthSegments = Math.Max(0, rawHealthSegments + boss.HealthSegmentCounterHpOffset);
        }

        private void SaveChanges()
        {
            string configPath;
            string? configDirectory;
            string json;
            long saveGeneration;

            try
            {
                configPath = Path.Combine(PluginConfigDirectory, $"{Server.MapName}.jsonc");
                configDirectory = Path.GetDirectoryName(configPath);

#pragma warning disable CS0618
                if (BossConfigs.HPBarList is { Count: 0 })
                {
                    BossConfigs.HPBarList = null;
                }
#pragma warning restore CS0618

                json = JsonSerializer.Serialize(BossConfigs, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                saveGeneration = System.Threading.Interlocked.Increment(ref SaveGeneration);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to snapshot boss config");
                return;
            }

            _ = Task.Run(async () =>
            {
                await SaveLock.WaitAsync();
                try
                {
                    if (saveGeneration != System.Threading.Volatile.Read(ref SaveGeneration))
                    {
                        Logger.LogDebug("Skipped stale boss config save for {ConfigPath}", configPath);
                        return;
                    }

                    if (configDirectory != null && !Directory.Exists(configDirectory)) Directory.CreateDirectory(configDirectory);
                    await File.WriteAllTextAsync(configPath, json);
                    Logger.LogInformation($"Saved updated boss config to {configPath}");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to save boss config");
                }
                finally
                {
                    SaveLock.Release();
                }
            });
        }
    }
}
