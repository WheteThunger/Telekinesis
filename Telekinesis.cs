using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Telekinesis", "WhiteThunder", "3.2.1")]
    [Description("Allows players to move and rotate objects in place.")]
    internal class Telekinesis : CovalencePlugin
    {
        #region Fields

        private static Telekinesis _pluginInstance;
        private static Configuration _pluginConfig;

        private const string PermissionAdmin = "telekinesis.admin";
        private const string PermissionRulesetFormat = "telekinesis.ruleset.{0}";

        private TelekinesisManager _telekinesisManager;
        private UndoManager _undoManager;

        public Telekinesis()
        {
            _undoManager = new UndoManager(timer);
            _telekinesisManager = new TelekinesisManager(_undoManager);
        }

        #endregion

        #region Hooks

        private void Init()
        {
            _pluginInstance = this;

            permission.RegisterPermission(PermissionAdmin, this);

            foreach (var ruleset in _pluginConfig.Rulesets)
            {
                if (ruleset.Permission != null)
                {
                    permission.RegisterPermission(ruleset.Permission, this);
                }
            }
        }

        private void Unload()
        {
            _telekinesisManager.StopAll();

            _pluginConfig = null;
            _pluginInstance = null;
        }

        #endregion

        #region API

        private bool API_IsBeingControlled(Component component)
        {
            return _telekinesisManager.IsBeingControlled(component);
        }

        private bool API_IsUsingTelekinesis(BasePlayer player)
        {
            return _telekinesisManager.IsUsingTelekinesis(player);
        }

        private bool API_StartAdminTelekinesis(BasePlayer player, Component component)
        {
            return _telekinesisManager.TryStartTelekinesis(player, component, PlayerRuleset.AdminRuleset);
        }

        private void API_StopPlayerTelekinesis(BasePlayer player)
        {
            _telekinesisManager.StopPlayerTelekinesis(player);
        }

        private void API_StopTargetTelekinesis(Component target)
        {
            _telekinesisManager.StopTargetTelekinesis(target);
        }

        #endregion

        #region Exposed Hooks

        private static class ExposedHooks
        {
            // Allow plugins to provide an entity that doesn't have a collider.
            public static Component OnTelekinesisFindFailed(BasePlayer player)
            {
                return Interface.CallHook("OnTelekinesisFindFailed", player) as Component;
            }

            // Allow plugins to replace the target entity with a more suitable one (e.g., the parent entity).
            public static Tuple<Component, Component> OnTelekinesisStart(BasePlayer player, Component component)
            {
                var result = Interface.CallHook("OnTelekinesisStart", player, component);
                if (result is Tuple<BaseEntity, BaseEntity> entityTuple)
                    return new Tuple<Component, Component>(entityTuple.Item1, entityTuple.Item2);

                if (result is Tuple<Component, Component> componentTuple)
                    return componentTuple;

                var resultEntity = result as BaseEntity;
                if (resultEntity != null)
                    return new Tuple<Component, Component>(resultEntity, resultEntity);

                return new Tuple<Component, Component>(component, component);
            }

            // Allow plugins to prevent telekinesis based on arbitrary circumstances.
            public static bool CanStartTelekinesis(BasePlayer player, Component moveComponent, Component rotateComponent, out string errorMessage)
            {
                errorMessage = null;

                object hookResult = Interface.CallHook("CanStartTelekinesis", player, moveComponent, rotateComponent);
                if (hookResult is bool && (bool)hookResult == false)
                    return false;

                errorMessage = hookResult as string;
                if (errorMessage != null)
                    return false;

                return true;
            }

            // Notify plugins that telekinesis started.
            public static void OnTelekinesisStarted(BasePlayer player, Component moveComponent, Component rotateComponent)
            {
                Interface.CallHook("OnTelekinesisStarted", player, moveComponent, rotateComponent);
            }

            // Notify plugins that telekinesis stopped.
            public static void OnTelekinesisStopped(BasePlayer player, Component moveComponent, Component rotateComponent)
            {
                Interface.CallHook("OnTelekinesisStopped", player, moveComponent, rotateComponent);
            }
        }

        #endregion

        #region Commands

        [Command("telekinesis", "tls")]
        private void CommandTelekinesis(IPlayer player, string cmd, string[] args)
        {
            if (player.IsServer)
                return;

            var ruleset = _pluginConfig.GetPlayerRuleset(permission, player.Id);
            if (ruleset == null)
            {
                ReplyToPlayer(player, Lang.ErrorNoPermission);
                return;
            }

            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null)
                return;

            if (args.Length > 0 && args[0] == "undo")
            {
                _telekinesisManager.StopPlayerTelekinesis(basePlayer);

                if (_undoManager.TryUndo(basePlayer.userID, out var previousMoveComponent, out var previousRotateComponent))
                {
                    ReplyToPlayer(player, Lang.UndoSuccess);
                    ExposedHooks.OnTelekinesisStopped(basePlayer, previousMoveComponent, previousRotateComponent);
                }
                else
                {
                    ReplyToPlayer(player, Lang.ErrorUndoNotFound);
                }

                return;
            }

            if (_telekinesisManager.IsUsingTelekinesis(basePlayer))
            {
                _telekinesisManager.StopPlayerTelekinesis(basePlayer);
                return;
            }

            Component component = GetLookEntity(basePlayer);
            if (component == null)
            {
                component = ExposedHooks.OnTelekinesisFindFailed(basePlayer);
                if (component == null)
                {
                    ReplyToPlayer(player, Lang.ErrorNoEntityFound);
                    return;
                }
            }

            if (!ruleset.CanMovePlayers && component is BasePlayer)
            {
                ChatMessageWithPrefix(basePlayer, Lang.ErrorCannotMovePlayers);
                return;
            }

            if (ruleset.MaxDistance > 0 && Vector3.Distance(basePlayer.eyes.position, component.transform.position) > ruleset.MaxDistance)
            {
                ChatMessageWithPrefix(basePlayer, Lang.ErrorMaxDistance);
                return;
            }

            if (ruleset.RequiresOwnership && component is BaseEntity entity && entity.OwnerID != basePlayer.userID)
            {
                ChatMessageWithPrefix(basePlayer, Lang.ErrorNotOwned);
                return;
            }

            if (!ruleset.CanUseWhileBuildingBlocked && IsBuildingBlocked(basePlayer, component))
            {
                ChatMessageWithPrefix(basePlayer, Lang.ErrorBuildingBlocked);
                return;
            }

            if (component is BaseVehicleModule vehicleModule)
            {
                if (vehicleModule.Vehicle != null)
                {
                    component = vehicleModule.Vehicle;
                }
            }

            _telekinesisManager.TryStartTelekinesis(basePlayer, component, ruleset);
        }

        #endregion

        #region Helper Methods

        private static Vector3 TransformPoint(Vector3 origin, Vector3 localPosition, Quaternion rotation)
        {
            return origin + rotation * localPosition;
        }

        private static Vector3 InverseTransformPoint(Vector3 origin, Vector3 worldPosition, Quaternion rotation)
        {
            return Quaternion.Inverse(rotation) * (worldPosition - origin);
        }

        private static BaseEntity GetLookEntity(BasePlayer player, int layerMask = Physics.DefaultRaycastLayers, float maxDistance = 15)
        {
            return Physics.Raycast(player.eyes.HeadRay(), out var hit, maxDistance, layerMask, QueryTriggerInteraction.Ignore)
                ? hit.GetEntity()
                : null;
        }

        private static void BroadcastEntityTransformChange(BaseEntity entity, Transform transform = null)
        {
            transform ??= entity.transform;

            var wasSyncPosition = entity.syncPosition;
            entity.syncPosition = true;
            entity.TransformChanged();
            entity.syncPosition = wasSyncPosition;

            transform.hasChanged = false;

            if (entity is StabilityEntity)
            {
                // Not great for performance, but can be optimized later.
                entity.TerminateOnClient(BaseNetworkable.DestroyMode.None);
                entity.SendNetworkUpdateImmediate();

                foreach (var child in entity.children)
                {
                    child.SendNetworkUpdateImmediate();
                }
            }
        }

        private static void RemoveActiveItem(BasePlayer player)
        {
            var activeItem = player.GetActiveItem();
            if (activeItem == null)
                return;

            var slot = activeItem.position;
            activeItem.RemoveFromContainer();
            player.inventory.SendUpdatedInventory(PlayerInventory.Type.Belt, player.inventory.containerBelt);

            var playerPosition = player.transform.position;

            // Use server manager to ensure the invoke isn't canceled (player invoke or oxide timer could be).
            ServerMgr.Instance.Invoke(() =>
            {
                if (!activeItem.MoveToContainer(player.inventory.containerBelt, slot)
                    && !player.inventory.GiveItem(activeItem))
                {
                    activeItem.DropAndTossUpwards(playerPosition);
                }
            }, 0.2f);
        }

        private static bool IsBuildingBlocked(BasePlayer player, Component component)
        {
            if (component is BaseEntity entity)
            {
                return player.IsBuildingBlocked(entity.WorldSpaceBounds());
            }

            return player.IsBuildingBlocked(component.transform.position, Quaternion.identity, new Bounds());
        }

        #endregion

        #region TelekinesisManager

        private class TelekinesisManager
        {
            private UndoManager _undoManager;
            private Dictionary<BasePlayer, TelekinesisComponent> _playerComponents = new();

            public TelekinesisManager(UndoManager undoManager)
            {
                _undoManager = undoManager;
            }

            public void Register(TelekinesisComponent component)
            {
                _playerComponents[component.Player] = component;
                ExposedHooks.OnTelekinesisStarted(component.Player, component.MoveComponent, component.RotateComponent);
            }

            public void Unregister(TelekinesisComponent component)
            {
                _playerComponents.Remove(component.Player);
                ExposedHooks.OnTelekinesisStopped(component.Player, component.MoveComponent, component.RotateComponent);
            }

            public bool IsBeingControlled(Component component)
            {
                if (component == null || component is BaseEntity { IsDestroyed: true })
                    return false;

                return TelekinesisComponent.GetForComponent(component) != null;
            }

            public bool IsUsingTelekinesis(BasePlayer player)
            {
                return GetPlayerTelekinesisTarget(player) != null;
            }

            public bool TryStartTelekinesis(BasePlayer player, Component component, PlayerRuleset ruleset)
            {
                // Prevent multiple players from simultaneously controlling the entity.
                if (IsBeingControlled(component))
                {
                    _pluginInstance.ChatMessageWithPrefix(player, Lang.ErrorAlreadyBeingControlled);
                    return false;
                }

                // Prevent the player from simultaneously controlling multiple entities.
                if (IsUsingTelekinesis(player))
                {
                    _pluginInstance.ChatMessageWithPrefix(player, Lang.ErrorAlreadyUsingTelekinesis);
                    return false;
                }

                // Allow plugins to swap out the entities.
                var entitiesToTransform = ExposedHooks.OnTelekinesisStart(player, component);
                var moveComponent = entitiesToTransform.Item1;
                var rotateComponent = entitiesToTransform.Item2;

                // Allow plugins to prevent telekinesis on specific entities.
                if (!ExposedHooks.CanStartTelekinesis(player, moveComponent, rotateComponent, out string errorMessage))
                {
                    if (errorMessage != null)
                    {
                        player.ChatMessage(errorMessage);
                    }
                    else
                    {
                        _pluginInstance.ChatMessageWithPrefix(player, Lang.ErrorBlockedByPlugin);
                    }

                    return false;
                }

                var restorePoint = _undoManager.SavePosition(player.userID, moveComponent, rotateComponent);
                TelekinesisComponent.AddToComponent(moveComponent, rotateComponent, this, player, ruleset, restorePoint);
                RemoveActiveItem(player);

                var modeMessage = _pluginInstance.GetModeMessage(player, TelekinesisMode.MovePlayerOffset);
                _pluginInstance.ChatMessageWithPrefix(player, Lang.InfoEnabled, modeMessage);

                return true;
            }

            public void StopPlayerTelekinesis(BasePlayer player)
            {
                GetPlayerTelekinesisTarget(player)?.DestroyImmediate();
            }

            public void StopTargetTelekinesis(Component component)
            {
                TelekinesisComponent.GetForComponent(component)?.DestroyImmediate();
            }

            public void StopAll()
            {
                foreach (var component in _playerComponents.Values.ToArray())
                {
                    component.DestroyImmediate();
                }
            }

            private TelekinesisComponent GetPlayerTelekinesisTarget(BasePlayer player)
            {
                return _playerComponents.GetValueOrDefault(player);
            }
        }

        #endregion

        #region Undo Manager

        private class RestorePoint
        {
            private static bool IsComponentValid(Component component)
            {
                return component != null && component is not BaseEntity { IsDestroyed: true };
            }

            private const float ExpirationSeconds = 300;

            public bool IsValid => IsComponentValid(_moveComponent) && IsComponentValid(_rotateComponent);

            private PluginTimers _pluginTimers;
            private Component _moveComponent;
            private Component _rotateComponent;
            private Vector3 _localPosition;
            private Quaternion _localRotation;
            private Action _cleanup;
            private Timer _timer;

            public RestorePoint(PluginTimers pluginTimers, Component moveComponent, Component rotateComponent, Action cleanup)
            {
                _pluginTimers = pluginTimers;

                _moveComponent = moveComponent;
                _rotateComponent = rotateComponent;
                _localPosition = moveComponent.transform.localPosition;
                _localRotation = rotateComponent.transform.localRotation;
                _cleanup = cleanup;
            }

            public bool TryRestore(out Component moveComponent, out Component rotateComponent)
            {
                moveComponent = _moveComponent;
                rotateComponent = _rotateComponent;

                if (!IsValid)
                {
                    Destroy();
                    return false;
                }

                _moveComponent.transform.localPosition = _localPosition;
                _rotateComponent.transform.localRotation = _localRotation;

                if (_moveComponent is BaseEntity moveEntity)
                {
                    BroadcastEntityTransformChange(moveEntity);
                }

                if (_rotateComponent != _moveComponent && _rotateComponent is BaseEntity rotateEntity)
                {
                    BroadcastEntityTransformChange(rotateEntity);
                }

                Destroy();
                return true;
            }

            public void StartExpirationTimer()
            {
                if (!IsValid)
                    return;

                _timer = _pluginTimers.Once(ExpirationSeconds, _cleanup);
            }

            public void Destroy()
            {
                _timer?.Destroy();
                _cleanup();
            }
        }

        private class UndoManager
        {
            private Dictionary<ulong, RestorePoint> _playerRestorePoints = new();
            private PluginTimers _pluginTimers;

            public UndoManager(PluginTimers pluginTimers)
            {
                _pluginTimers = pluginTimers;
            }

            public RestorePoint SavePosition(ulong userId, Component moveComponent, Component rotateComponent)
            {
                GetRestorePoint(userId)?.Destroy();

                var restorePoint = new RestorePoint(_pluginTimers, moveComponent, rotateComponent, () => _playerRestorePoints.Remove(userId));
                _playerRestorePoints[userId] = restorePoint;
                return restorePoint;
            }

            public bool TryUndo(ulong userId, out Component moveComponent, out Component rotateComponent)
            {
                var restorePoint = GetRestorePoint(userId);
                if (restorePoint == null)
                {
                    moveComponent = null;
                    rotateComponent = null;
                    return false;
                }

                return restorePoint.TryRestore(out moveComponent, out rotateComponent);
            }

            private RestorePoint GetRestorePoint(ulong userId)
            {
                return _playerRestorePoints.GetValueOrDefault(userId);
            }
        }

        #endregion

        #region Telekinesis Component

        private enum TelekinesisMode
        {
            MovePlayerOffset,
            MoveY,
            RotateX,
            RotateY,
            RotateZ,
        }

        private class TelekinesisComponent : FacepunchBehaviour
        {
            private class RigidbodyRestorePoint
            {
                private Rigidbody _rigidBody;
                private bool _useGravity;
                private bool _isKinematic;

                public static RigidbodyRestorePoint CreateRestore(Rigidbody rigidbody)
                {
                    if (rigidbody == null)
                        return null;

                    if (!rigidbody.useGravity && rigidbody.isKinematic)
                        return null;

                    var restore = new RigidbodyRestorePoint
                    {
                        _rigidBody = rigidbody,
                        _useGravity = rigidbody.useGravity,
                        _isKinematic = rigidbody.isKinematic,
                    };

                    rigidbody.useGravity = false;
                    rigidbody.isKinematic = true;

                    return restore;
                }

                public void Restore()
                {
                    if (_rigidBody == null)
                        return;

                    _rigidBody.useGravity = _useGravity;
                    _rigidBody.isKinematic = _isKinematic;
                }
            }

            private const float ModeChangeDelay = 0.25f;

            public static void AddToComponent(Component moveComponent, Component rotateComponent, TelekinesisManager manager, BasePlayer player, PlayerRuleset ruleset, RestorePoint restorePoint) =>
                moveComponent.gameObject.AddComponent<TelekinesisComponent>().Init(moveComponent, rotateComponent, manager, player, ruleset, restorePoint);

            public static TelekinesisComponent GetForComponent(Component component) =>
                component.gameObject.GetComponent<TelekinesisComponent>();

            public Component MoveComponent { get; private set; }
            public Component RotateComponent { get; private set; }
            public BasePlayer Player { get; private set; }

            private Transform _moveTransform;
            private Transform _rotateTransform;
            private PlayerRuleset _ruleset;
            private TelekinesisManager _manager;
            private RestorePoint _restorePoint;
            private float _maxDistanceSquared;

            private TelekinesisMode _mode = TelekinesisMode.MovePlayerOffset;
            private float _lastBuildingBlockCheck = UnityEngine.Time.time;

            // Keep track of when the component is destroyed for an explicit reason, to avoid sending an extra notification.
            private bool _wasDestroyedForExplicitReason;

            // Keep track of where the entity is relative to the player eyes.
            // This precise offset is maintained throughout the movement session.
            private Vector3 _headOffset;

            // Keep track of last time the entity moved in order to time it out.
            private float _lastMoved;

            // Keep track of the last mode change to avoid changing mode too rapidly.
            private float _lastChangedMode = UnityEngine.Time.time;

            // Keep track of original rigid body settings so they can be restored.
            private RigidbodyRestorePoint _rigidbodyRestore;

            public TelekinesisComponent Init(Component moveComponent, Component rotateComponent, TelekinesisManager manager, BasePlayer player, PlayerRuleset ruleset, RestorePoint restorePoint)
            {
                MoveComponent = moveComponent;
                RotateComponent = rotateComponent;
                _moveTransform = MoveComponent.transform;
                _rotateTransform = RotateComponent.transform;
                Player = player;

                _ruleset = ruleset;
                _maxDistanceSquared = Mathf.Pow(ruleset.MaxDistance, 2);
                _manager = manager;
                _manager.Register(this);
                _restorePoint = restorePoint;

                _lastMoved = UnityEngine.Time.realtimeSinceStartup;
                _headOffset = InverseTransformPoint(player.eyes.position, _moveTransform.position, player.eyes.rotation);

                _rigidbodyRestore = RigidbodyRestorePoint.CreateRestore(GetComponent<Rigidbody>());

                // Use facepunch invoke handler instead of Update() to avoid overhead incurred by calling from native.
                InvokeRepeating(TrackedUpdate, 0, 0);

                return this;
            }

            public void DestroyImmediate(string reason = null)
            {
                if (reason != null && Player != null)
                {
                    Player.ChatMessage(reason);
                    _wasDestroyedForExplicitReason = true;
                }

                DestroyImmediate(this);
            }

            private void MaybeSwitchMode(float now)
            {
                if (_lastChangedMode + ModeChangeDelay > now
                    || !Player.serverInput.IsDown(BUTTON.RELOAD))
                    return;

                _lastChangedMode = now;

                if (Player.serverInput.IsDown(BUTTON.SPRINT))
                {
                    switch (_mode)
                    {
                        case TelekinesisMode.RotateZ:
                            _mode = TelekinesisMode.RotateY;
                            break;
                        case TelekinesisMode.RotateY:
                            _mode = TelekinesisMode.RotateX;
                            break;
                        case TelekinesisMode.RotateX:
                            _mode = TelekinesisMode.MoveY;
                            break;
                        case TelekinesisMode.MoveY:
                            _mode = TelekinesisMode.MovePlayerOffset;
                            break;
                        case TelekinesisMode.MovePlayerOffset:
                            _mode = TelekinesisMode.RotateZ;
                            break;
                    }
                }
                else
                {
                    switch (_mode)
                    {
                        case TelekinesisMode.MovePlayerOffset:
                            _mode = TelekinesisMode.MoveY;
                            break;
                        case TelekinesisMode.MoveY:
                            _mode = TelekinesisMode.RotateX;
                            break;
                        case TelekinesisMode.RotateX:
                            _mode = TelekinesisMode.RotateY;
                            break;
                        case TelekinesisMode.RotateY:
                            _mode = TelekinesisMode.RotateZ;
                            break;
                        case TelekinesisMode.RotateZ:
                            _mode = TelekinesisMode.MovePlayerOffset;
                            break;
                    }
                }

                _pluginInstance.SendModeChatMessage(Player, _mode);
            }

            private float GetSensitivityMultiplier(SpeedSettings speedSettings)
            {
                if (Player.serverInput.IsDown(BUTTON.SPRINT))
                    return speedSettings.Fast;

                if (Player.serverInput.IsDown(BUTTON.DUCK))
                    return speedSettings.Slow;

                return speedSettings.Normal;
            }

            private void SetHeadOffset(Vector3 newHeadOffset)
            {
                // Verify max distance isn't being exceeded.
                if (_maxDistanceSquared > 0 && newHeadOffset.sqrMagnitude > _maxDistanceSquared)
                    return;

                _headOffset = newHeadOffset;
            }

            private void MaybeMoveOrRotate(float now)
            {
                var direction = Player.serverInput.IsDown(BUTTON.FIRE_PRIMARY)
                    ? 1 : Player.serverInput.IsDown(BUTTON.FIRE_SECONDARY)
                    ? -1 : 0;

                var eyeRotation = Player.eyes.rotation;

                if (direction != 0)
                {
                    var deltaTimeAndDirection = UnityEngine.Time.deltaTime * direction;

                    switch (_mode)
                    {
                        case TelekinesisMode.MovePlayerOffset:
                            SetHeadOffset(_headOffset + new Vector3(0, 0, deltaTimeAndDirection * GetSensitivityMultiplier(_pluginConfig.MoveSensitivity)));
                            break;

                        case TelekinesisMode.MoveY:
                            SetHeadOffset(_headOffset + Quaternion.Inverse(eyeRotation) * new Vector3(0, deltaTimeAndDirection * GetSensitivityMultiplier(_pluginConfig.MoveSensitivity), 0));
                            break;

                        case TelekinesisMode.RotateX:
                            _rotateTransform.Rotate(50f * deltaTimeAndDirection * GetSensitivityMultiplier(_pluginConfig.RotateSensitivity), 0, 0);
                            break;

                        case TelekinesisMode.RotateY:
                            _rotateTransform.Rotate(0, -50f * deltaTimeAndDirection * GetSensitivityMultiplier(_pluginConfig.RotateSensitivity), 0);
                            break;

                        case TelekinesisMode.RotateZ:
                            _rotateTransform.Rotate(0, 0, 50f * deltaTimeAndDirection * GetSensitivityMultiplier(_pluginConfig.RotateSensitivity));
                            break;
                    }
                }

                var eyePosition = Player.eyes.position;
                var desiredPosition = TransformPoint(eyePosition, _headOffset, eyeRotation);

                if (!_ruleset.CanUseWhileBuildingBlocked && _lastBuildingBlockCheck + _pluginConfig.BuildingBlockedCheckFrequency < now)
                {
                    _lastBuildingBlockCheck = now;

                    var bounds = MoveComponent is BaseEntity moveEntity
                        ? moveEntity.bounds
                        : new Bounds();

                    // Perform the building block check at the entity location.
                    if (Player.IsBuildingBlocked(new OBB(desiredPosition, _moveTransform.lossyScale, _moveTransform.rotation, bounds)))
                    {
                        DestroyImmediate(_pluginInstance?.GetMessageWithPrefix(Player, Lang.InfoDisableBuildingBlocked));
                        return;
                    }
                }

                if (_moveTransform.position != desiredPosition)
                {
                    if ((desiredPosition - _moveTransform.position).sqrMagnitude > 0.0001f)
                    {
                        // Interpolate over longer distances (> 0.01) so the movement is less jumpy.
                        _moveTransform.position = Vector3.Lerp(_moveTransform.position, desiredPosition, UnityEngine.Time.deltaTime * 15);
                    }
                    else
                    {
                        // Don't interpolate when really close, so that the position eventually matches.
                        _moveTransform.position = desiredPosition;
                    }
                }

                var hasChanged = false;

                if (_moveTransform.hasChanged)
                {
                    if (MoveComponent is BaseEntity moveEntity)
                    {
                        BroadcastEntityTransformChange(moveEntity, _moveTransform);
                    }

                    hasChanged = true;
                }

                if (_rotateTransform.hasChanged)
                {
                    if (RotateComponent is BaseEntity rotateEntity)
                    {
                        BroadcastEntityTransformChange(rotateEntity, _rotateTransform);
                    }

                    hasChanged = true;
                }

                if (hasChanged)
                {
                    _lastMoved = UnityEngine.Time.realtimeSinceStartup;
                }
                else if (_lastMoved + _pluginConfig.IdleTimeout < UnityEngine.Time.realtimeSinceStartup)
                {
                    DestroyImmediate(_pluginInstance?.GetMessageWithPrefix(Player, Lang.InfoDisableInactivity));
                    return;
                }
            }

            private void DoUpdate()
            {
                if (Player == null
                    || Player.IsDestroyed
                    || Player.IsDead()
                    || !Player.IsConnected
                    || (RotateComponent != MoveComponent && RotateComponent == null))
                {
                    DestroyImmediate();
                    return;
                }

                var now = UnityEngine.Time.time;

                MaybeSwitchMode(now);
                MaybeMoveOrRotate(now);
            }

            private void TrackedUpdate()
            {
                _pluginInstance?.TrackStart();
                DoUpdate();
                _pluginInstance?.TrackEnd();
            }

            private void OnDestroy()
            {
                _rigidbodyRestore?.Restore();
                _restorePoint?.StartExpirationTimer();
                MoveComponent.GetComponent<Buoyancy>()?.Wake();
                _manager.Unregister(this);

                if (!_wasDestroyedForExplicitReason && Player != null)
                {
                    _pluginInstance?.ChatMessageWithPrefix(Player, Lang.InfoDisabled);
                }
            }
        }

        #endregion

        #region Configuration

        private class PlayerRuleset
        {
            public static PlayerRuleset AdminRuleset = new()
            {
                CanMovePlayers = true,
                CanUseWhileBuildingBlocked = true,
                RequiresOwnership = false,
                MaxDistance = 0,
            };

            [JsonProperty("Permission suffix")]
            public string PermissionSuffix;

            [JsonProperty("Can move players")]
            public bool CanMovePlayers;

            [JsonProperty("Can use while building blocked")]
            public bool CanUseWhileBuildingBlocked;

            [JsonProperty("Requires ownership")]
            public bool RequiresOwnership;

            [JsonProperty("Max distance")]
            public float MaxDistance;

            private string _cachedPermission;

            [JsonIgnore]
            public string Permission
            {
                get
                {
                    if (_cachedPermission == null && !string.IsNullOrWhiteSpace(PermissionSuffix))
                    {
                        _cachedPermission = string.Format(PermissionRulesetFormat, PermissionSuffix);
                    }

                    return _cachedPermission;
                }
            }
        }

        private class SpeedSettings
        {
            [JsonProperty("Slow")]
            public float Slow = 0.2f;

            [JsonProperty("Normal")]
            public float Normal = 1;

            [JsonProperty("Fast")]
            public float Fast = 5;
        }

        private class Configuration : SerializableConfiguration
        {
            [JsonProperty("Enable message prefix")]
            public bool EnableMessagePrefix = true;

            [JsonProperty("Idle timeout (seconds)")]
            public float IdleTimeout = 60;

            [JsonProperty("Building privilege check frequency (seconds)")]
            public float BuildingBlockedCheckFrequency = 0.25f;

            [JsonProperty("Move sensitivity")]
            public SpeedSettings MoveSensitivity = new();

            [JsonProperty("Rotate sensitivity")]
            public SpeedSettings RotateSensitivity = new();

            [JsonProperty("Rulesets")]
            public PlayerRuleset[] Rulesets =
            {
                new()
                {
                    PermissionSuffix = "restricted",
                    CanMovePlayers = false,
                    CanUseWhileBuildingBlocked = false,
                    RequiresOwnership = true,
                    MaxDistance = 3,
                },
            };

            public PlayerRuleset GetPlayerRuleset(Permission permission, string userIdString)
            {
                if (permission.UserHasPermission(userIdString, PermissionAdmin))
                    return PlayerRuleset.AdminRuleset;

                if (Rulesets == null)
                    return null;

                for (var i = Rulesets.Length - 1; i >= 0; i--)
                {
                    var ruleset = Rulesets[i];
                    var perm = ruleset.Permission;
                    if (perm != null && permission.UserHasPermission(userIdString, perm))
                        return ruleset;
                }

                return null;
            }
        }

        private Configuration GetDefaultConfig() => new();

        #endregion

        #region Configuration Helpers

        private class SerializableConfiguration
        {
            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(SerializableConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            var changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                if (currentRaw.TryGetValue(key, out var currentRawValue))
                {
                    var defaultDictValue = currentWithDefaults[key] as Dictionary<string, object>;
                    var currentDictValue = currentRawValue as Dictionary<string, object>;

                    if (defaultDictValue != null)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigDict(defaultDictValue, currentDictValue))
                        {
                            changed = true;
                        }
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig() => _pluginConfig = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _pluginConfig = Config.ReadObject<Configuration>();
                if (_pluginConfig == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_pluginConfig))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_pluginConfig, true);
        }

        #endregion

        #region Localization

        private string GetMessage(string playerId, string messageName, params object[] args)
        {
            var message = lang.GetMessage(messageName, this, playerId);
            return args.Length > 0 ? string.Format(message, args) : message;
        }

        private string GetMessageWithPrefix(string playerId, string messageName, params object[] args)
        {
            var message = GetMessage(playerId, messageName, args);

            if (_pluginConfig.EnableMessagePrefix)
            {
                message = GetMessage(playerId, Lang.MessagePrefix) + message;
            }

            return message;
        }

        private string GetMessageWithPrefix(BasePlayer player, string messageName, params object[] args) =>
            GetMessageWithPrefix(player.UserIDString, messageName, args);

        private void ReplyToPlayer(IPlayer player, string messageName, params object[] args) =>
            player.Reply(GetMessageWithPrefix(player.Id, messageName, args));

        private void ChatMessageWithPrefix(BasePlayer player, string messageName, params object[] args) =>
            player.ChatMessage(GetMessageWithPrefix(player.UserIDString, messageName, args));

        private string GetModeLangKey(TelekinesisMode mode)
        {
            switch (mode)
            {
                case TelekinesisMode.MovePlayerOffset:
                    return Lang.MovePlayerOffset;

                case TelekinesisMode.MoveY:
                    return Lang.ModeMoveY;

                case TelekinesisMode.RotateX:
                    return Lang.ModeRotateX;

                case TelekinesisMode.RotateY:
                    return Lang.ModeRotateY;

                case TelekinesisMode.RotateZ:
                    return Lang.ModeRotateZ;

                default:
                    return Enum.GetName(typeof(TelekinesisMode), mode);
            }
        }

        private string GetModeName(BasePlayer player, TelekinesisMode mode) =>
            GetMessage(player.UserIDString, GetModeLangKey(mode));

        private string GetModeMessage(BasePlayer player, TelekinesisMode mode) =>
            GetMessage(player.UserIDString, Lang.ModeChanged, GetModeName(player, mode));

        private void SendModeChatMessage(BasePlayer player, TelekinesisMode mode) =>
            ChatMessageWithPrefix(player, GetModeMessage(player, mode));

        private class Lang
        {
            public const string ErrorNoPermission = "Error.NoPermission";
            public const string ErrorNoEntityFound = "Error.NoEntityFound";
            public const string ErrorAlreadyBeingControlled = "Error.AlreadyBeingControlled";
            public const string ErrorAlreadyUsingTelekinesis = "Error.AlreadyUsingTelekinesis";
            public const string ErrorBlockedByPlugin = "Error.BlockedByPlugin";
            public const string ErrorCannotMovePlayers = "Error.CannotMovePlayers";
            public const string ErrorNotOwned = "Error.NotOwned";
            public const string ErrorBuildingBlocked = "Error.BuildingBlocked";
            public const string ErrorMaxDistance = "Error.MaxDistance";

            public const string MessagePrefix = "MessagePrefix";
            public const string InfoEnabled = "Info.Enabled";
            public const string InfoDisabled = "Info.Disabled";
            public const string InfoDisableInactivity = "Info.Disabled.Inactivity";
            public const string InfoDisableBuildingBlocked = "Info.Disabled.BuildingBlocked";

            public const string ErrorUndoNotFound = "Undo.Error.NotFound";
            public const string UndoSuccess = "Undo.Success";

            public const string ModeChanged = "Mode.Changed";
            public const string MovePlayerOffset = "Mode.MovePlayerOffset";
            public const string ModeMoveY = "Mode.OffsetY";
            public const string ModeRotateX = "Mode.RotateX";
            public const string ModeRotateY = "Mode.RotateY";
            public const string ModeRotateZ = "Mode.RotateZ";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.ErrorNoPermission] = "You don't have permission to do that.",
                [Lang.ErrorNoEntityFound] = "No entity found.",
                [Lang.ErrorAlreadyBeingControlled] = "That entity is already being controlled.",
                [Lang.ErrorAlreadyUsingTelekinesis] = "You are already using telekinesis.",
                [Lang.ErrorBlockedByPlugin] = "Another plugin blocked telekinesis.",
                [Lang.ErrorCannotMovePlayers] = "You are not allowed to use telekinesis on players.",
                [Lang.ErrorNotOwned] = "That do not own that entity.",
                [Lang.ErrorBuildingBlocked] = "You are not allowed to use telekinesis while building blocked.",
                [Lang.ErrorMaxDistance] = "You are not allowed to use telekinesis that far away.",

                [Lang.MessagePrefix] = "<color=#0ff>[Telekinesis]</color>: ",
                [Lang.InfoEnabled] = "Telekinesis has been enabled.\n{0}",
                [Lang.InfoDisabled] = "Telekinesis has been disabled.",
                [Lang.InfoDisableInactivity] = "Telekinesis has been disabled due to inactivity.",
                [Lang.InfoDisableBuildingBlocked] = "Telekinesis has been disabled because you are building blocked.",

                [Lang.ErrorUndoNotFound] = "No undo data found.",
                [Lang.UndoSuccess] = "Your last telekinesis movement was undone.",

                [Lang.ModeChanged] = "Current mode: {0}",
                [Lang.MovePlayerOffset] = "Move away/toward",
                [Lang.ModeMoveY] = "Move up/down",
                [Lang.ModeRotateX] = "Rotate around X axis (pitch)",
                [Lang.ModeRotateY] = "Rotate around Y axis (yaw)",
                [Lang.ModeRotateZ] = "Rotate around Z axis (roll)",
            }, this, "en");
        }

        #endregion
    }
}
