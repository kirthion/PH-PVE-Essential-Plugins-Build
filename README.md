# PH PVE Essential Plugins

Custom all-in-one RocketMod plugin for the PH PVE Unturned server.

## Build Target

- RocketModFix 4.23.1
- Current Unturned server API
- Compatible with OpenMod 3.8.10 running alongside RocketMod

## Core Requirements

- Preserve RocketMod/OpenMod as the default owners of their native commands.
- Do not register duplicate or overlapping commands.
- Consolidate the required functionality into PHPVEEssentialPlugins.dll.
- Editable XML configuration and translations.
- AdvancedGodVanish remains a separate plugin.

## PH PVE Features

### Back
- `/back`
- 3600-second cooldown after each successful use.
- Failed attempts do not consume the cooldown.
- Store valid previous locations for configured teleport/death events.

### TPA
- CEO can initiate teleport requests.
- All players can accept or deny requests.
- `/tpa <player>`
- `/tpa accept`
- `/tpa deny`
- `/tpaccept`
- `/tpdeny`

### Administration
Separate permissions for:
- Freecam
- Editor
- Spectate
- Editor Other Objects

Spectate access:
- CEO
- Elite
- Moders
- Staff

Editor Other Objects:
- Staff

### Eagle Eye Surveillance System
When a CEO or Elite player activates spectator mode with Shift+F7, publicly announce:

`[player] has activated the Eagle Eye Surveillance System`

- Activation only.
- No public message when disabling it.
- No private activation notification.
- Moders and Staff retain spectate access without triggering this announcement.
