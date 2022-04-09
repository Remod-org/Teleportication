## Teleportication
Nextgen Teleport plugin for Rust

Uses Friends, Clans, Rust teams

A lot of the familiar commands from older teleport plugins are still there, with some exceptions.  This is where the similarites end.

### Commands
    - /sethome NAME is an alias for /home set NAME.
    - /home or /home list will show your currently set home names, locations, and lastused.
    - /home list OTHERPLAYER will show a list of that player's currently set homes.
    - /home set NAME - Sets a home at player's current location
    - /home remove NAME - Removes the home named NAME
    - /home NAME - Teleports you to your home with that NAME.
    - /homeg - Show a GUI listing user homes (currently only the current user).
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
    - /tpadmin - Parent function for:
      - /tpadmin wipe - Wipe ALL home data and town, reset outpost and bandit.
      - /tpadmin backup - Backup database
      - /tpadmin import {r/n} {1/y/yes/true} - Import data from R/NTeleportation (homes only)
        If you specify 1 or y, etc. at the end it will actually do the import.  Otherwise, it will show what will be imported.
    - /tunnel -- List available tunnel entrances
      - /tunnel NAME OF TUNNEL -- Teleport to a tunnel entrance
    - /station -- List available tunnel stations
      - /tunnel NAME OF TUNNEL -- Teleport to a tunnel station (DANGEROUS: Procted by tunnel dwellers)

Note that "set" can be changed by config, e.g. for /home set, /town set...

### Permissions
    - teleportication.use     - /home
    - teleportication.tpb     - /tpb
    - teleportication.tpr     - /tpr
    - teleportication.town    - /town
    - teleportication.bandit  - /bandit
    - teleportication.outpost - /outpost
    - teleportication.station - /station
    - teleportication.tunnel  - /tunnel

### Configuration
```json
{
  "Options": {
    "debug": false,
    "logtofile": false,
    "useClans": false,
    "useFriends": false,
    "useTeams": false,
    "HomeRequireFoundation": true,
    "HomeMinimumDistance": 10f,
    "HomeRemoveInvalid": true,
    "StrictFoundationCheck": true,
    "HonorBuildingPrivilege": true,
    "HonorRelationships": false,
    "WipeOnNewSave": true,
    "AutoGenBandit": true,
    "AutoGenOutpost": true,
    "AutoGenTunnels": false,
    "DefaultMonumentSize": 120.0,
    "CaveDistanceSmall": 40.0,
    "CaveDistanceMedium": 60.0,
    "CaveDistanceLarge": 100.0,
    "MinimumTemp": 0.0,
    "MaximumTemp": 40.0,
    "SetCommand": "set"
    "ListCommand": "list",
    "RemoveCommand": "remove",
    "AddTownMapMarker": false,
    "TownZoneId": null,
    "TownZoneEnterMessage": "Welcome to Town",
    "TownZoneLeaveMessage": "Thanks for stopping by!",
    "TownZoneFlags": [
      "nodecay",
      "nohelitargeting"
    ]
  },
  "Types": {
    "Home": {
      "BlockOnHurt": false,
      "BlockOnCold": false,
      "BlockOnHot": false,
      "BlockOnCave": false,
      "BlockOnRig": false,
      "BlockOnMonuments": false,
      "BlockOnHostile": false,
      "BlockOnSafe": true,
      "BlockOnBalloon": false,
      "BlockOnCargo": false,
      "BlockOnExcavator": false,
      "BlockOnLift": false,
      "BlockOnMounted": true,
      "BlockOnSwimming": false,
      "BlockOnWater": false,
      "BlockInTunnel": true,
      "AutoAccept": false,
      "DailyLimit": 10.0,
      "CountDown": 5.0,
      "CoolDown": 30.0,
      "AllowBypass": false,
      "BypassAmount": 0.0,
      "VIPSettings": null
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
    - `WipeOnNewSave` -- If set, wipe home, town, bandit, outpost on a new map save.
    - `AutoGenBandit` -- Generate bandit location once per wipe.
    - `AutoGenOutpost` -- Generate outpost location once per wipe.
    - `AutoGenTunnels` -- Generate locations for tunnel stations and entrances once per wipe.
    - `DefaultMonumentSize` -- Most monuments do not contain a size parameter, so this would be the default in that case.
    - `CaveDistanceSmall` -- Small cave distance/size (no stored parameter)
    - `CaveDistanceMedium` -- Medium cave distance/size (no stored parameter)
    - `CaveDistanceLarge` -- Large cave distance/size (no stored parameter)
    - `MinimumTemp` -- Minimum player temperature to allow teleport, if BlockOnCold is set.
    - `MaximumTemp` -- Maximum player temperature to allow teleport, if BlockOnHot is set.
    - `SetCommand` -- For different languages to select something other than 'set' to set home, town, etc.
    - `ListCommand` -- For different languages to select something other than 'list' to list homes
    - `RemoveCommand` -- For different languages to select something other than 'set' to remove homes
    - `AddTownMapMarker` -- If true, adds a green dot at the location of town on player maps
    - `TownZoneId` -- If set to anything other than null, an attempt will be made to assign a zone to town using ZoneManager.  This can be a zone you have already set to your liking.  This is to avoid having to reset the zone location every time you move or set town.
    - `TownZoneEnterMessage` -- When entering the town zone, players will see this message.  Default is "Welcome to Town".  Leave empty if you already have a zone setup the way you like it.
    - `TownZoneLeaveMessage"` -- When leaving the town zone, players will see this message.  Default is  "Thanks for stopping by!".
    - `TownZoneFlags` -- The default values here prevent town decay and targeting by the heli within the town zone.  You can remove this if desired by setting this variable to [].  Or, edit the zone flags as you like.  See the documentation for ZoneManager.

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
    - `BlockInTunnel`: true -- Block if player is in a tunnel (height check). 
    - `AutoAccept`: false -- Only valid for TPR to automatically TPA.
    - `DailyLimit`: 0.0 -- (NOT YET WORKING) If set to other than 0, the limit for this action per day.
    - `CountDown`: 5.0 -- Waiting period for action on home, tpr, etc.
    - `CoolDown`: 120.0  -- Waiting period until next teleport of this type

#### For each of home, town, bandit, outpost, tpr, VIP settings can be added as follows:

The default is "VIPSettings": null, ...  Change them as needed, creating your own permission name, e.g. teleportication.vip1, and settings:

```json
      "VIPSettings": {
        "teleportication.vip1": {
          "VIPDailyLimit": 20.0,
          "VIPCountDown": 5.0,
          "VIPCoolDown": 10.0,
          "VIPAllowBypass": true,
          "VIPBypassAmount": 1.0
        },
        "teleportication.vip2": {
          "VIPDailyLimit": 30.0,
          "VIPCountDown": 3.0,
          "VIPCoolDown": 5.0,
          "VIPAllowBypass": true,
          "VIPBypassAmount": 1.0
        }
      }
```

### Details

Despite some similarites, the configuration and data files from other teleport plugins are NOT compatible.

Teleportication uses SQLite for home, town, bandit, and outpost storage.  The file is saved in {oxidedata}/Teleportication/teleportication.db.

In-memory objects keep track of previous location for tpb, pending tpr/tpa, etc.  This could change as development progresses.

### For Developers
    The following can be used for example as follows:

```cs
    [PluginReference]
    private readonly Plugin Teleportication;

    bool success = (bool) Teleportication.CallHook("SetServerTp", "mytarget", Vector3 OBJECT);
    bool success = (bool) Teleportication.CallHook("UnsetServerTp", "mytarget");
    object x = Teleportication.CallHook("GetServerTp");
    object x = Teleportication.CallHook("GetServerTp", "mytarget");
    bool success = (bool) Teleportication.CallHook("ResetServerTp");
```

#### Hooks:

```cs
bool SetServerTp(string name, Vector3 location) or AddServerTp(string name, Vector3 location);
```

    Adds a server target using name and location.


```cs
bool UnsetServerTp(string name) or RemoveServerTp(name);
```

    Removes a server target by name other than bandit, outpost, and town.


```cs
bool GetServerTp(string name = "");
```

    Gets either the Vector3 location of a named server target or, if name is not specified, a Dictionary<string, Vector3> object of all server targets, or null if nothing is found by name or at all.


```cs
bool ResetServerTp();
```

    Resets/removes all server targets other than bandit, outpost, and town.


### Status

  1. Economics is a pending feature (for bypassing CoolDown, etc.)

