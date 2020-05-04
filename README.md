## RealTeleport (Work in progress)
Nextgen Teleport plugin for Rust

Uses Friends, Clans, teams, RustIO

A lot of the familiar commands from older teleport plugins are still there, with some exceptions.

### Commands
    - /sethome NAME is gone in favor of /home set NAME.
    - /home or /home list will show your currently set home names, locations, and lastused.
    - /home set NAME - Sets a home at player's current location
    - /home NAME - Teleports you to your home with that NAME.
    - /town set - Sets town at the current location
    - /bandit set - Sets bandit at the current location
    - /outpost set - Sets outpost at the current location
    - /town - Takes you to town
    - /bandit - Takes you to Bandit Town
    - /outpost - Takes you to the Outpost
    - /tpr PLAYER - Request teleport to PLAYER
    - /tpa - Accept teleport request
    - /tpc - Cancel a teleport
    - /tpb - Takes you back to your previous location

### Permissions
    - realteleport.use     - /home
    - realteleport.tpb     - /tpb
    - realteleport.tpr     - /tpr
    - realteleport.town    - /town
    - realteleport.bandit  - /bandit
    - realteleport.outpost - /outpost

### Configuration
```json
{
  "Options": {
    "useClans": false,
    "useFriends": false,
    "useTeams": false,
    "HomeRequireFoundation": true,
    "StrictFoundationCheck": true,
    "HomeRemoveInvalid": true,
    "HonorBuildingPrivilege": true,
    "HonorRelationships": false,
    "AutoGenBandit": true,
    "AutoGenOutpost": true,
    "DefaultMonumentSize": 120.0,
    "CaveDistanceSmall": 40.0,
    "CaveDistanceMedium": 60.0,
    "CaveDistanceLarge": 100.0,
    "MinimumTemp": 0.0,
    "MaximumTemp": 40.0,
    "SetCommand": "set"
  },
  "Home": {
    "BlockOnHurt": false,
    "BlockOnCold": false,
    "BlockOnHot": false,
    "BlockOnCave": false,
    "BlockOnRig": false,
    "BlockOnMonuments": false,
    "BlockOnHostile": false,
    "BlockOnSafe": false,
    "BlockOnBalloon": false,
    "BlockOnCargo": false,
    "BlockOnExcavator": false,
    "BlockOnLift": false,
    "BlockOnMounted": false,
    "BlockOnSwimming": false,
    "BlockOnWater": false,
    "AutoAccept": false,
    "DailyLimit": 0.0,
    "CountDown": 5.0,
    "CoolDown": 120.0
  },
  ...
}
```
#### Global Options
    - `useClans` -- Use various Clans plugins for determining relationships
    - `useFriends` -- Use various Friends plugins for determining relationships
    - `useTeams` -- Use Rust native teams for determining relationships
    - `HomeRequireFoundation` -- Require a foundation to set or use a home
    - `StrictFoundationCheck` -- Require centering on a foundation block to set a home
    - `HomeRemoveInvalid` -- If the home is no longer valid due to building privilege, destruction, etc., remove it.
    - `HonorBuildingPrivilege` -- If set, require building privilege to use a home.
    - `HonorRelationships` -- If set, honor any of the useXXX features to determine ability to teleport to a home.
    - `AutoGenBandit` -- Generate bandit location once per wipe.
    - `AutoGenOutpost` -- Generate outpost location once per wipe.
    - `DefaultMonumentSize` -- Most monuments do not contain a size parameter, so this would be the default in that case.
    - `CaveDistanceSmall` -- Small cave distance/size (no stored parameter)
    - `CaveDistanceMedium` -- Medium cave distance/size (no stored parameter)
    - `CaveDistanceLarge` -- Large cave distance/size (no stored parameter)
    - `MinimumTemp` -- Minimum player temperature to allow teleport, if BlockOnCold is set.
    - `MaximumTemp` -- Maximum player temperature to allow teleport, if BlockOnHot is set.
    - `SetCommand` -- For different languages to select something other than 'set' to set home, town, etc.

#### For each of home, town, bandit, outpost, tpr, flags may be set as follows:
    - `BlockOnHurt`: false -- Block if player is injured (bleeding, etc.).
    - `BlockOnCold`: false -- Block if player is too cold.
    - `BlockOnHot`: false -- Block if player is too hot.
    - `BlockOnCave`: false -- Block if player is in or near a cave.
    - `BlockOnRig`: false -- Block if player is on one of the oil rigs.
    - `BlockOnMonuments`: false -- Block if player is to close to any monument.
    - `BlockOnHostile`: false -- Block if player is hostile (for bandit/outpost only).
    - `BlockOnSafe`: false -- Block if player is in a safe area.
    - `BlockOnBalloon`: false -- Block if player is on a hot air baloon.
    - `BlockOnCargo`: false -- Block if player is on the cargo ship.
    - `BlockOnExcavator`: false -- Block if player is on the excavator monument.
    - `BlockOnLift`: false -- Block if player is on a lift.
    - `BlockOnMounted`: false -- Block if player is mounted to a chair, etc.
    - `BlockOnSwimming`: false -- Block if player is swimming.
    - `BlockOnWater`: false -- Block if player is above water. 
    - `AutoAccept`: false -- Only valid for TPR to automatically TPA.
    - `DailyLimit`: 0.0 -- If set to other than 0, the limit for this action per day.
    - `CountDown`: 5.0 -- Waiting period for action on home, tpr, etc.
    - `CoolDown`: 120.0  -- Waiting period until next teleport of this type

### Details

Data files from other teleport plugins are NOT compatible.

RealTeleport uses SQLite for home, town, bandit, and outpost storage.  The file is saved in {oxidedata}/RealTeleport/realteleport.db.

In-memory objects keep track of previous location for tpb, pending tpr/tpa, etc.  This could change as development progresses.
