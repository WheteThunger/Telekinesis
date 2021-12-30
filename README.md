## Features

- Allows players with permission to move and rotate entities in place
- Allows restricting capabilities with configurable rulesets

## Abuse warning

This plugin can be abused if the permissions are granted to normal players, so it's recommended to grant the permissions to only trusted admins or moderators.

## Performance warning

This plugin can degrade server performance, especially when multiple players are moving entities simultaneously, so it's recommended that you use this plugin sparingly.

## Permissions

- `telekinesis.admin` -- Allows unrestricted usage of the `tls` command.
- `telekinesis.ruleset.restricted` -- Allows restricted usage of the `tls` command. The restrictions are controlled by the plugin configuration.

You may create additional rulesets in the plugin configuration, each of which will generate a separate permission of the format `telekinesis.ruleset.<suffix>`. Granting multiple rulesets to a player will cause only the last one to apply, based on the order in the config.

## Usage

1. Run the `tls` command to grab the entity you are looking at. The entity will automatically move wherever your player camera goes.
2. Press primary attack (`mouse1`) or secondary attack (`mouse2`) to move or rotate the entity (depending on which mode is active).
3. While moving or rotating the entity, hold `duck` (`Ctrl`) to move/rotate slowly, or `sprint` (`Shift`) to move/rotate quickly.
2. Press the `reload` key to switch between modes. Holding `sprint` while pressing `reload` will change modes in reverse order. See below for details about each mode.
3. Run the `tls` command again to release the entity.
4. Run the `tls undo` command to reset the position/rotation of the entity you are currently controlling, or to reset the last entity that you controlled.

#### Modes

- "Move away/toward" -- Moves the entity away from you or toward you, in the direction that you are looking.
- "Move up/down" -- Moves the entity up or down, using the world Y axis, regardless of where you are looking, and regardless of the entity's rotation.
- "Rotate around X axis (pitch)" -- Rotate the entity around its own X axis. This basically tilts it forward or backward.
- "Rotate around Y axis (yaw)" -- Rotate the entity around its own Y axis. This basically turns it left or right.
- "Rotate around Z axis (roll)" -- Rotate the entity around its own Z axis. This basically leans it left or right.

## Commands

- `tls` -- Grabs the entity you are looking at, or releases the entity you are currently controlling.
- `tls undo` -- Resets the most recently grabbed entity to its original position and rotation. This can only be done if you moved or rotated the entity in the past 5 minutes.

## Configuration

Default configuration:

```json
{
  "Enable message prefix": true,
  "Idle timeout (seconds)": 60.0,
  "Building privilege check frequency (seconds)": 0.25,
  "Move sensitivity": {
    "Slow": 0.2,
    "Normal": 1.0,
    "Fast": 5.0
  },
  "Rotate sensitivity": {
    "Slow": 0.2,
    "Normal": 1.0,
    "Fast": 5.0
  },
  "Rulesets": [
    {
      "Permission suffix": "restricted",
      "Can move players": false,
      "Can use while building blocked": false,
      "Requires ownership": true,
      "Max distance": 3.0
    }
  ]
}
```

- `Enable message prefix` -- Enable or disable the chat/console message prefix. The prefix itself can be configured in the localization.
- `Idle timeout (seconds)` -- Determines how long you can be inactive (no entity movement or rotation) until the entity you are controlling is automatically released.
- `Building privilege check frequency (seconds)` -- Determines how often to check for building privilege when using a ruleset that disallows usage while building blocked. Note: Building privilege checks are costly to server performance, so you should avoid setting this value very low.
- `Move sensitivity` -- Determines how quickly you can move the held entity when using the "Move away/toward" or "Move up/down" modes. These settings do not affect how quickly the entity moves when you move your player or change your view angles.
  - `Slow` -- Applies while holding the `duck` key (`Ctrl` by default).
  - `Normal` -- Applies while **not** holding the `sprint` or `duck` keys.
  - `Fast` -- Applies while holding the `sprint` key (`Shift` by default).
- `Rotate sensitivity` -- Determines how quickly you can rotate the held entity when using any of the rotate modes.
- `Rulesets` -- Rulesets can be assigned to players via permissions, in order to restrict their usage.
  - `Permission suffix` -- Determines the permission associated with the ruleset, which will be generated using the format `telekinesis.ruleset.<suffix>`.
  - `Can move players` (`true`/`false`) -- Determines whether players with this ruleset are allowed to move other players (sleeping or awake).
  - `Can use while building blocked` (`true`/`false`) -- Determines whether players with this ruleset are allowed to move entities while they are building blocked (i.e., while they are nearby an enemy TC).
  - `Requires ownership` (`true`/`false`) -- Determines whether players with this ruleset must own the entities they want to move.
  - `Max distance` -- Determines how far away players with this ruleset can pick up and move entities. This applies when trying to pick up the entity, as well as when using the "Move away/toward" and "Move up/down" modes.

## Localization

```json
{
  "Error.NoPermission": "You don't have permission to do that.",
  "Error.NoEntityFound": "No entity found.",
  "Error.AlreadyBeingControlled": "That entity is already being controlled.",
  "Error.AlreadyUsingTelekinesis": "You are already using telekinesis.",
  "Error.BlockedByPlugin": "Another plugin blocked telekinesis.",
  "Error.CannotMovePlayers": "You are not allowed to use telekinesis on players.",
  "Error.NotOwned": "That do not own that entity.",
  "Error.BuildingBlocked": "You are not allowed to use telekinesis while building blocked.",
  "Error.MaxDistance": "You are not allowed to use telekinesis that far away.",
  "MessagePrefix": "<color=#0ff>[Telekinesis]</color>: ",
  "Info.Enabled": "Telekinesis has been enabled.\n{0}",
  "Info.Disabled": "Telekinesis has been disabled.",
  "Info.Disabled.Inactivity": "Telekinesis has been disabled due to inactivity.",
  "Info.Disabled.BuildingBlocked": "Telekinesis has been disabled because you are building blocked.",
  "Undo.Error.NotFound": "No undo data found.",
  "Undo.Success": "Your last telekinesis movement was undone.",
  "Mode.Changed": "Current mode: {0}",
  "Mode.MovePlayerOffset": "Move away/toward",
  "Mode.OffsetY": "Move up/down",
  "Mode.RotateX": "Rotate around X axis (pitch)",
  "Mode.RotateY": "Rotate around Y axis (yaw)",
  "Mode.RotateZ": "Rotate around Z axis (roll)"
}
```

## Developer API

#### API_IsBeingControlled

```csharp
bool API_IsBeingControlled(BaseEntity entity)
```

Returns `true` if the entity is being controlled with Telekinesis, else returns `false`.

#### API_IsUsingTelekinesis

```csharp
bool API_IsUsingTelekinesis(BasePlayer player)
```

Returns `true` if the player is controlling an entiy with Telekinesis, else returns `false`.

#### API_StartAdminTelekinesis

```csharp
bool API_StartAdminTelekinesis(BasePlayer player, BaseEntity entity)
```

Starts a Telekinesis session for the specified player and entity, with no restrictions. This is the same as if the player had the `telekinesis.admin` and used the `tls` command on the entity. Returns `true` if the session was able to be started, else returns `false`.

Possible reasons for returning `false`:
- The entity was already being controlled by Telekinesis.
- The player was already controlling an entity with Telekinesis.
- Another plugin blocked it with the `CanStartTelekinesis` hook.

#### API_StopPlayerTelekinesis

```csharp
void API_StopPlayerTelekinesis(BasePlayer player)
```

Stops a Telekinesis session for the specified player. Has no effect if the player was not using Telekinesis.

#### API_StopTargetTelekinesis

```csharp
void API_StopTargetTelekinesis(BaseEntity target)
```

Stops a Telekinesis session for the specified target entity. Has no effect if the entity was not being controlled by Telekinesis.

## Developer Hooks

#### OnTelekinesisFindFailed

```csharp
BaseEntity OnTelekinesisFindFailed(BasePlayer player)
```

- Called after Telekinesis failed to find any entity where the player was looking.
- Returning a `BaseEntity` will cause Telekinesis to grab that entity.
- Returning `null` will result in the default behavior.
- This is useful for specific plugins to provide entities that have no collider.

#### OnTelekinesisStart

```csharp
BaseEntity OnTelekinesisStart(BasePlayer player, BaseEntity entity)
```

- Called after Telekinesis has found a target entity, before the session has started.
- Returning a `BaseEntity` will cause Telekinesis to grab that entity instead of the one that was found.
- Returning `null` will use the entity that Telekinesis found, or one that another plugin provided.
- This is useful to swap out which entity is going to be controlled. For example, to control a parent entity instead of the child.

#### CanStartTelekinesis

```csharp
object CanStartTelekinesis(BasePlayer player, BaseEntity entity)
```

- Called when a Telekinesis session is about to start.
- Returning `false` or a `string` will prevent the Telekinesis session from starting. If returning a `string`, the value will be sent to the player as a chat message.
- Returning `null` will allow the session to start, unless another plugin blocks it.

#### OnTelekinesisStarted

```csharp
void OnTelekinesisStarted(BasePlayer player, BaseEntity entity)
```

- Called after a Telekinesis session has started.
- No return behavior.

#### OnTelekinesisStopped

```csharp
void OnTelekinesisStopped(BasePlayer player, BaseEntity entity)
```

- Called after a Telekinesis session has ended.
- No return behavior.

## Credits

- **Bombardir**, the original author of this plugin
- **Fujikura**, for the active item removal code used in earlier versions of the plugin
- **redBDGR**, for maintaining the plugin
