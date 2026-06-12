using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
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

        [GeneratedRegex(@"_\d{3,}$")]
        private static partial Regex BossNameSuffixRegex();

        public override string ModuleName => "EntBossHP";
        public override string ModuleVersion => "2.1.1";
        public override string ModuleAuthor => "Oylsister, Credits to Kxrnl, DarkerZ [RUS] / modified by Tsukasa";
        
        public string PluginConfigDirectory => Path.Combine(ModuleDirectory, "..", "..", "configs", "plugins", ModuleName);
        private string PlayerSettingsPath => Path.Combine(PluginConfigDirectory, "player_settings.json");

        private readonly List<BreakableBoss> _breakableBosses = [];
        private readonly List<MathCounterBoss> _mathCounterBosses = [];

        private static readonly System.Threading.SemaphoreSlim SaveLock = new(1, 1);
        private CCSGameRulesProxy? _gameRulesProxy;
        private bool configLoaded = false;

        private static readonly PluginCapability<ISettingsApi?> SettingsCapability = new("settings:nfcore");
        private PlayerPreferenceService _playerPreferenceService = null!;

        internal BossConfig BossConfigs { get; private set; } = new();

        private HitEventDisplay HitEventDisplay { get; set; } = null!;

        public override void Load(bool hotReload)
        {
            _playerPreferenceService = new PlayerPreferenceService(() => SettingsCapability.Get(), true);
            HitEventDisplay = new(this);

            HookEntityOutput("math_counter", "OutValue", CounterOut);
            HookEntityOutput("func_physbox_multiplayer", "OnDamaged", BreakableOut);
            HookEntityOutput("func_physbox", "OnHealthChanged", BreakableOut);
            HookEntityOutput("func_breakable", "OnHealthChanged", BreakableOut);
            HookEntityOutput("prop_dynamic", "OnHealthChanged", Hitbox_Hook);

            RegisterEventHandler<EventRoundStart>(OnRoundStart);
            RegisterListener<OnMapStart>(MapStart);
            RegisterListener<OnEntityCreated>(OnEntityCreated);
            RegisterListener<OnTick>(OnTick);
            RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

            AddCommand("boss_list", "", CommandBossList);
            AddCommand("css_bhud", "Toggle boss HP HUD", CommandBossHud);

            if (hotReload)
            {
                MapStart(Server.MapName);
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
            RemoveListener<OnTick>(OnTick);
            _gameRulesProxy = null;
        }
        
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

            BossDataLoading();
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
                var boss = new MathCounterBoss
                {
                    BossName = mathcounter.Name,
                    Enabled = mathcounter.Enabled,
                    MathCounterHitMode = mathcounter.MathCounterMode,
                    MathCounterName = mathcounter.MathCounter,
                    HpOffset = mathcounter.HpOffset,
                };
                if (!string.IsNullOrEmpty(mathcounter.HealthSegmentCounter))
                {
                    boss.IsSegmented = true;
                    boss.HealthSegmentCounterName = mathcounter.HealthSegmentCounter;
                    boss.HealthSegmentCounterMode = mathcounter.HealthSegmentCounterMode;
                }
                _mathCounterBosses.Add(boss);
            }
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
            foreach (var boss in _breakableBosses)
            {
                boss.Health = 0;
                boss.MaxHealth = 0;
                boss.BreakableEntity = null;
                boss.DefeatPending = false;
            }
            foreach (var boss in _mathCounterBosses)
            {
                boss.Health = 0;
                boss.MaxHealth = 0;
                boss.MathCounterEntity = null;
                boss.DefeatPending = false;
            }
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
                foreach (var boss in _mathCounterBosses.Where(b => b.IsSegmented && MatchesEntityName(entityName, b.HealthSegmentCounterName))) InitializeSegmentCounter(boss, entity);
                foreach (var boss in _breakableBosses.Where(b => b.IsSegmented && MatchesEntityName(entityName, b.HealthSegmentCounterName))) InitializeSegmentCounter(boss, entity);
                foreach (var boss in _mathCounterBosses.Where(b => MatchesEntityName(entityName, b.MathCounterName))) InitializeMainCounter(boss, entity);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in Timer_MathCounterInitial");
            }
        }

        private void InitializeSegmentCounter(SegmentedBossData boss, CEntityInstance entity)
        {
            boss.HealthSegmentCounterEntity = entity;
            var segmentCounter = new CMathCounter(entity.Handle);
            boss.TotalHealthSegments = (int)Math.Round(segmentCounter.Max);
            if (boss.HealthSegmentCounterMode == 2)
            {
                var destroyedSegments = (int)GetMathCounterValue(entity.Handle);
                boss.HealthSegments = Math.Max(0, boss.TotalHealthSegments - destroyedSegments);
            }
            else boss.HealthSegments = (int)GetMathCounterValue(entity.Handle);
        }

        private void InitializeMainCounter(MathCounterBoss boss, CEntityInstance entity)
        {
            boss.MathCounterEntity = entity;
            var counter = new CMathCounter(entity.Handle);
            boss.MathCounterMaxValue = (int)Math.Round(counter.Max);
            if (boss.MathCounterHitMode == 0) boss.MathCounterHitMode = 1;
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
                var counterValueInt = (int)counterValue;
                UpdateSegmentCounters(entityname, counterValueInt, client);

                if (configLoaded)
                {
                    if (!BossConfigs.MathCounterList.Any(b => MatchesEntityName(entityname, b.MathCounter)))
                    {
                        var sanitizedName = SanitizeBossName(entityname);
                        if (!BossConfigs.MathCounterList.Any(b => b.MathCounter == sanitizedName))
                        {
                            var newBossConfig = new MathCounterConfig { Name = sanitizedName, MathCounter = sanitizedName, MathCounterMode = 1, Enabled = (counterValue > 10), HpOffset = 0 };
                            BossConfigs.MathCounterList.Add(newBossConfig);
                            SaveChanges();
                            var newLiveBoss = new MathCounterBoss { BossName = newBossConfig.Name, Enabled = newBossConfig.Enabled, MathCounterHitMode = newBossConfig.MathCounterMode, MathCounterName = newBossConfig.MathCounter, HpOffset = newBossConfig.HpOffset };
                            InitializeMainCounter(newLiveBoss, caller);
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
                     if (!BossConfigs.BreakableList.Any(b => MatchesEntityName(entityname, b.Breakable)))
                     {
                        var sanitizedName = SanitizeBossName(entityname);
                        if (!BossConfigs.BreakableList.Any(b => b.Breakable == sanitizedName))
                        {
                            var newBossConfig = new BreakableConfig { Name = sanitizedName, Breakable = sanitizedName, Enabled = false, HpOffset = 0 };
                            BossConfigs.BreakableList.Add(newBossConfig);
                            SaveChanges();
                            var newLiveBoss = new BreakableBoss { BossName = newBossConfig.Name, Enabled = newBossConfig.Enabled, BreakableEntityName = newBossConfig.Breakable, BreakableEntity = caller, HpOffset = newBossConfig.HpOffset };
                            newLiveBoss.Health = hp;
                            newLiveBoss.MaxHealth = engineMaxHealth > 0 ? engineMaxHealth : hp;
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
                    boss.BreakableEntity = caller;
                    if (boss.IsSegmented && hp <= 0 && boss.HealthSegmentCounterEntity is { IsValid: true })
                    {
                        AddTimer(0.1f, () => {
                            if (boss.HealthSegmentCounterEntity is not { IsValid: true }) return;
                            HandleSegmentEnd(boss, () => {
                                boss.MaxHealth = 0;
                                boss.Health = 0;
                            });
                        }, TimerFlags.STOP_ON_MAPCHANGE);
                        continue;
                    }
                    if (hp <= 0)
                    {
                        ScheduleDefeatConfirmationBreakable(boss);
                        continue;
                    }
                    boss.DefeatPending = false;
                    boss.Health = hp;
                    if (engineMaxHealth > 0) boss.MaxHealth = engineMaxHealth;
                    else if (boss.MaxHealth <= 0) boss.MaxHealth = hp;
                    if (hp > boss.MaxHealth) boss.MaxHealth = hp;

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
            var currentHp = boss.MathCounterHitMode == 1 ? (int)counterValue : boss.MathCounterMaxValue - (int)counterValue;
            if (boss.IsSegmented && currentHp <= 0 && boss.HealthSegmentCounterEntity is { IsValid: true })
            {
                AddTimer(0.1f, () => {
                    if (boss.HealthSegmentCounterEntity is not { IsValid: true }) return;
                    HandleSegmentEnd(boss, () => {
                        boss.MaxHealth = 0;
                        boss.Health = 0;
                    });
                }, TimerFlags.STOP_ON_MAPCHANGE);
                return;
            }

            if (currentHp <= 0)
            {
                ScheduleDefeatConfirmation(boss);
                return;
            }

            boss.DefeatPending = false;
            boss.Health = currentHp;
            UpdateMaxHealth(boss, currentHp);

            if (boss.Enabled)
            {
                UpdateAndDisplayBoss(boss, client);
            }
        }

        private void UpdateSegmentCounters(string entityName, int counterValue, CCSPlayerController client)
        {
            foreach (var boss in _mathCounterBosses) UpdateSegmentCounter(boss, entityName, counterValue, client);
            foreach (var boss in _breakableBosses) UpdateSegmentCounter(boss, entityName, counterValue, client);
        }

        private void UpdateSegmentCounter(SegmentedBossData boss, string entityName, int counterValue, CCSPlayerController client)
        {
            if (!boss.IsSegmented) return;
            if (boss.HealthSegmentCounterEntity is not { IsValid: true }) return;
            if (!MatchesEntityName(entityName, boss.HealthSegmentCounterName)) return;

            boss.HealthSegments = boss.HealthSegmentCounterMode == 2
                ? Math.Max(0, boss.TotalHealthSegments - counterValue)
                : counterValue;

            if (boss.Enabled)
            {
                UpdateAndDisplayBoss(boss, client);
            }
        }

        private void HandleSegmentEnd(SegmentedBossData boss, Action resetAction)
        {
            if (boss.HealthSegmentCounterEntity is not { IsValid: true } segmentCounterEntity) return;

            if (boss.HealthSegmentCounterMode == 2)
            {
                var destroyedSegments = (int)GetMathCounterValue(segmentCounterEntity.Handle);
                boss.HealthSegments = Math.Max(0, boss.TotalHealthSegments - destroyedSegments);
            }
            else boss.HealthSegments = (int)GetMathCounterValue(segmentCounterEntity.Handle);
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

            AddTimer(0.3f, () => {
                if (!boss.DefeatPending) return;
                boss.DefeatPending = false;

                if (boss is MathCounterBoss mathCounterBoss && mathCounterBoss.MathCounterEntity is { IsValid: true } entity)
                {
                    var counterValue = GetMathCounterValue(entity.Handle);
                    var currentHp = mathCounterBoss.MathCounterHitMode == 1
                        ? (int)counterValue
                        : mathCounterBoss.MathCounterMaxValue - (int)counterValue;
                    if (currentHp > 0) return;
                }

                NotifyBossDefeated(boss);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void ScheduleDefeatConfirmationBreakable(BreakableBoss boss)
        {
            if (boss.DefeatPending) return;
            boss.DefeatPending = true;

            AddTimer(0.3f, () => {
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

        private static bool MatchesEntityName(string entityName, string configuredName)
        {
            return !string.IsNullOrWhiteSpace(entityName)
                && !string.IsNullOrWhiteSpace(configuredName)
                && entityName.StartsWith(configuredName, StringComparison.Ordinal);
        }

        private unsafe float GetMathCounterValue(nint handle)
        {
            if (handle == IntPtr.Zero) return 0;
            try
            {
                var offset = Schema.GetSchemaOffset("CMathCounter", "m_OutValue");
                return *(float*)IntPtr.Add(handle, offset + 24);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to read math_counter value");
                return 0;
            }
        }

        private void SaveChanges()
        {
            string configPath;
            string? configDirectory;
            string json;

            try
            {
                configPath = Path.Combine(PluginConfigDirectory, $"{Server.MapName}.jsonc");
                configDirectory = Path.GetDirectoryName(configPath);
                json = JsonSerializer.Serialize(BossConfigs, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });
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
