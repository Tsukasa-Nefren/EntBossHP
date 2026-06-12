using System.Collections.Concurrent;
using CounterStrikeSharp.API.Core;
using PlayerSettings;

namespace EntBossHP
{
    public sealed class PlayerPreferenceService(
        Func<ISettingsApi?> settingsApiProvider,
        bool hudEnabledByDefault,
        Func<CCSPlayerController, ulong?>? steamIdProvider = null)
    {
        public const string HudEnabledSettingName = "entbosshp.hud_enabled";

        private const string EnabledValue = "1";
        private const string DisabledValue = "0";
        private readonly ConcurrentDictionary<(ulong SteamId, string SettingName), string> _runtimeValues = new();
        private readonly Func<CCSPlayerController, ulong?> _steamIdProvider = steamIdProvider ?? TryGetSteamId;

        public bool IsHudEnabled(CCSPlayerController player)
        {
            return GetBoolean(player, HudEnabledSettingName, hudEnabledByDefault);
        }

        public bool ToggleHud(CCSPlayerController player)
        {
            var enabled = !IsHudEnabled(player);
            SetHudEnabled(player, enabled);
            return enabled;
        }

        public void SetHudEnabled(CCSPlayerController player, bool enabled)
        {
            SetBoolean(player, HudEnabledSettingName, enabled);
        }

        private bool GetBoolean(CCSPlayerController player, string settingName, bool defaultValue)
        {
            if (TryGetRuntimeValue(player, settingName, out var runtimeValue))
                return ParseBoolean(runtimeValue, defaultValue);

            var settingsApi = GetSettingsApi();
            if (settingsApi is null)
                return defaultValue;

            try
            {
                var storedValue = settingsApi.GetPlayerSettingsValue(player, settingName, ToStoredValue(defaultValue));
                return ParseBoolean(storedValue, defaultValue);
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        private void SetBoolean(CCSPlayerController player, string settingName, bool enabled)
        {
            var storedValue = ToStoredValue(enabled);
            SetRuntimeValue(player, settingName, storedValue);

            try
            {
                GetSettingsApi()?.SetPlayerSettingsValue(player, settingName, storedValue);
            }
            catch (Exception)
            {
            }
        }

        private ISettingsApi? GetSettingsApi()
        {
            ISettingsApi? settingsApi;
            try
            {
                settingsApi = settingsApiProvider();
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            if (settingsApi is null)
                return null;

            return settingsApi;
        }

        private static string ToStoredValue(bool enabled)
        {
            return enabled ? EnabledValue : DisabledValue;
        }

        private bool TryGetRuntimeValue(CCSPlayerController player, string settingName, out string value)
        {
            var steamId = GetSteamId(player);
            if (steamId is not { } resolvedSteamId)
            {
                value = "";
                return false;
            }

            return _runtimeValues.TryGetValue((resolvedSteamId, settingName), out value!);
        }

        private void SetRuntimeValue(CCSPlayerController player, string settingName, string value)
        {
            var steamId = GetSteamId(player);
            if (steamId is { } resolvedSteamId)
                _runtimeValues[(resolvedSteamId, settingName)] = value;
        }

        private ulong? GetSteamId(CCSPlayerController player)
        {
            try
            {
                return _steamIdProvider(player);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static ulong? TryGetSteamId(CCSPlayerController player)
        {
            if (player is null || !player.IsValid)
                return null;

            var steamId64 = player.AuthorizedSteamID?.SteamId64;
            if (steamId64 is null or <= 76561197960265728UL) return null;
            return steamId64;
        }

        private static bool ParseBoolean(string? value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            return value.Trim().ToLowerInvariant() switch
            {
                "1" or "true" or "yes" or "on" => true,
                "0" or "false" or "no" or "off" => false,
                _ => defaultValue
            };
        }

        public void Remove(ulong steamId64)
        {
            _runtimeValues.TryRemove((steamId64, HudEnabledSettingName), out _);
        }
    }
}
