# EntBossHP

A Counter-Strike 2 plugin for displaying boss health information to players during gameplay.

## Overview

EntBossHP tracks and displays the health of configured boss entities in CS2 maps through center screen messages. The plugin supports breakable entities and math counters. It also features a segmented boss system for bosses with multiple health phases.

## Features

- **Multiple Boss Types Support**:
  - Breakable entities (func_physbox, func_breakable, prop_dynamic)
  - Math counter entities

- **Real-time HP Display**:
  - Boss HP shown to the attacking player
  - Visual HP bar with filled/empty blocks
  - Optional HP offset per boss config

- **Segmented Boss Support**:
  - Bosses with multiple health segments
  - Remaining segment display
  - Uses health_segment_counter to track boss phases

- **Player Preferences**:
  - Players can toggle their own boss HP HUD via chat commands.
  - Integration with PlayerSettingsApi to save player preferences across sessions.

- **Auto-configuration**:
  - Automatically detects and adds new boss entities
  - Per-map configuration files
  - Dynamic boss discovery during gameplay

## Installation

1. Install CounterStrikeSharp on your CS2 server.
2. (Optional but Recommended) Install [PlayerSettingsApi](https://github.com/NickFox007/PlayerSettingsCS2) for saving player preferences.
3. Place the plugin files in your CounterStrikeSharp plugins directory.
4. Restart the server or load the plugin.

## Configuration

### Per-Map Configuration

The plugin creates configuration files for each map in JSON format:

```text
configs/plugins/EntBossHP/{mapname}.jsonc
```

### Config File Structure

```json
{
  "Breakable": [
    {
      "name": "Boss Name",
      "enabled": true,
      "breakable": "entity_name",
      "health_segment_counter": "counter_name",
      "health_segment_counter_mode": 1,
      "hp_offset": 0
    }
  ],
  "MathCounter": [
    {
      "name": "Math Boss",
      "enabled": true,
      "mathcounter": "counter_name",
      "mathcounter_mode": 1,
      "health_segment_counter": "segment_counter",
      "health_segment_counter_mode": 1,
      "hp_offset": 0
    }
  ]
}
```

> **Note:** The old HPBar configuration block is **deprecated**. Please use MathCounter in combination with health_segment_counter for multi-phase/segmented bosses instead.

## Boss Types

### 1. Breakable Boss

- Monitors breakable entities like func_physbox, func_breakable, and prop_dynamic
- Tracks entity health changes
- Supports segment counters for multi-phase bosses

### 2. Math Counter Boss

- Uses math_counter entities to track boss HP
- Supports different counter modes:
  - Mode 1: Direct counter value
  - Mode 2: Inverted counter value (max - current)
- Auto-detects counter min/max values
- Supports segment counters for multi-phase bosses

## Commands

- !bhud (or /bhud, css_bhud) - Toggles the Boss HP HUD on/off for the executing player. (Preferences are saved if PlayerSettingsApi is installed)
- boss_list - Display configured bosses and their settings (console only)

## Display Features

- **HP Bar Visualization**: filled/empty center HUD bar
- **Segmented Boss Display**: shows remaining segments
- **Auto-hide**: bosses are hidden when HP reaches 0

## Auto-Detection

The plugin automatically detects new boss entities during gameplay:

- Entities with HP > 10 are automatically added as enabled
- Entities with HP <= 10 are added as disabled
- Configuration is saved automatically

## Technical Details

- **Version**: 2.1.4
- **Author**: Oylsister, Credits to Kxrnl, DarkerZ [RUS] / modified by Tsukasa
- **Target Framework**: .NET 10
- **Dependencies**: CounterStrikeSharp.API 1.0.369, [PlayerSettingsApi](https://github.com/NickFox007/PlayerSettingsCS2) (Optional)
- **Supported Entities**: math_counter, func_physbox_multiplayer, func_physbox, func_breakable, prop_dynamic

## Troubleshooting

1. **Boss not appearing**: Check if the entity name matches the configuration.
2. **HP not updating**: Verify the entity is being damaged and outputs are firing.
3. **Configuration issues**: Check the JSON syntax in your map config file.

## License

This plugin is provided as-is for Counter-Strike 2 servers using CounterStrikeSharp.
