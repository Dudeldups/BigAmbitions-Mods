#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Helpers;
using UI.Notification;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime : MonoBehaviour
    {
        private bool wasNearEyeAlignmentActive;

        private static readonly string[] GameplayDistanceMemberNames =
        {
            "distance",
            "_distance",
            "currentDistance",
            "_currentDistance",
            "camDistance",
            "_camDistance",
            "cameraDistance",
            "_cameraDistance"
        };

        private void ConfigureGameplayController()
        {
            if (settings == null || gameplayController == null || !settings.EnableGameplayTweaks)
                return;

            var controllerId = gameplayController.GetInstanceID();
            var currentBounds = GetVector2Member(gameplayController, "minMaxDistance");
            var desiredBounds = currentBounds;
            desiredBounds.x = GameplayMinimumZoom;
            desiredBounds.y = settings.GameplayMaxZoom;

            var boundsDiffer =
                Mathf.Abs(currentBounds.x - desiredBounds.x) > 0.01f ||
                Mathf.Abs(currentBounds.y - desiredBounds.y) > 0.01f;
            var needsReconfigure =
                boundsDiffer ||
                lastAppliedGameplayMaxZoom != settings.GameplayMaxZoom ||
                controllerId != lastConfiguredGameplayControllerId;

            if (needsReconfigure)
            {
                SetTrackedMemberValue(gameplayController, "minMaxDistance", desiredBounds);
                SetTrackedMemberValue(gameplayController, "blockCameraZoom", false);
                lastAppliedGameplayMaxZoom = settings.GameplayMaxZoom;
                lastConfiguredGameplayControllerId = controllerId;
            }

            if (!hasManualGameplayPitch)
                manualGameplayPitch = Mathf.Clamp(settings.GameplayDefaultPitch, settings.GameplayMinPitch, settings.GameplayMaxPitch);

            ClampGameplayDistance(desiredBounds);
            ApplyGameplayOffset(manualGameplayPitch);
            ApplyGameplayTrackedObjectOffset();
        }

        private void ApplyGameplayTweaks()
        {
            if (settings == null || gameplayController == null || !settings.EnableGameplayTweaks)
                return;

            ConfigureGameplayController();

            if (firstPersonViewActive)
            {
                ResetGameplayRightMouseGesture();
                return;
            }

            var minPitch = Mathf.Min(settings.GameplayMinPitch, settings.GameplayMaxPitch);
            var maxPitch = Mathf.Max(settings.GameplayMinPitch, settings.GameplayMaxPitch);

            if (!hasShownGameplayPitchHint && context != null)
            {
                context.Logger.Info("CameraTools: hold right mouse and move up or down to tilt the gameplay camera.");
                hasShownGameplayPitchHint = true;
            }

            if (Input.GetMouseButtonDown(1))
            {
                if (IsGameplayInputBlockedByUi(forceRefresh: true))
                {
                    ResetGameplayRightMouseGesture();
                }
                else
                {
                    isGameplayRightMousePending = true;
                    isTrackingRightMousePitch = false;
                    gameplayRightMousePressPosition = Input.mousePosition;
                }
                lastRightMouseY = Input.mousePosition.y;
            }

            if (Input.GetMouseButton(1) && (isGameplayRightMousePending || isTrackingRightMousePitch))
            {
                var currentMouseY = Input.mousePosition.y;
                if (IsGameplayInputBlockedByUi())
                {
                    ResetGameplayRightMouseGesture();
                }
                else if (isGameplayRightMousePending)
                {
                    var dragDelta = (Vector2)Input.mousePosition - gameplayRightMousePressPosition;
                    if (dragDelta.sqrMagnitude > GameplayRightMouseDragDeadZonePixels * GameplayRightMouseDragDeadZonePixels)
                    {
                        isGameplayRightMousePending = false;
                        isTrackingRightMousePitch = true;
                        lastRightMouseY = currentMouseY;
                    }
                }
                else
                {
                    var deltaY = currentMouseY - lastRightMouseY;
                    lastRightMouseY = currentMouseY;

                    if (Mathf.Abs(deltaY) > Mathf.Epsilon)
                    {
                        manualGameplayPitch = Mathf.Clamp(manualGameplayPitch - deltaY * PitchStepPerMousePixel, minPitch, maxPitch);
                        hasManualGameplayPitch = true;
                    }
                }
            }

            if (Input.GetMouseButtonUp(1) || !Input.GetMouseButton(1))
                ResetGameplayRightMouseGesture();

            if (Input.GetKeyDown(KeyCode.Home))
            {
                manualGameplayPitch = Mathf.Clamp(settings.GameplayDefaultPitch, minPitch, maxPitch);
                hasManualGameplayPitch = true;
            }

            var bounds = GetVector2Member(gameplayController, "minMaxDistance");
            ClampGameplayDistance(bounds);
            ApplyGameplayOffset(hasManualGameplayPitch ? manualGameplayPitch : settings.GameplayDefaultPitch);
            ApplyGameplayTrackedObjectOffset();
        }

        private void ResetGameplayRightMouseGesture()
        {
            isGameplayRightMousePending = false;
            isTrackingRightMousePitch = false;
        }

        private void ClampGameplayDistance(Vector2 bounds)
        {
            if (gameplayController == null)
                return;

            var foundAny = false;
            for (var i = 0; i < GameplayDistanceMemberNames.Length; i++)
            {
                var memberName = GameplayDistanceMemberNames[i];
                if (!TryGetFloatMember(gameplayController, memberName, out var currentDistance))
                    continue;

                foundAny = true;
                var clampedDistance = Mathf.Clamp(currentDistance, bounds.x, bounds.y);
                if (Mathf.Abs(clampedDistance - currentDistance) <= 0.01f)
                    continue;

                SetTrackedMemberValue(gameplayController, memberName, clampedDistance);
            }

            if (!foundAny && TryGetCameraDistanceToFollowTarget(GetLiveVirtualCameraComponent(), Camera.main, out var actualDistance))
            {
                var clampedActualDistance = Mathf.Clamp(actualDistance, bounds.x, bounds.y);
                if (Mathf.Abs(clampedActualDistance - actualDistance) > 0.01f)
                {
                    SetTrackedMemberValue(gameplayController, "distance", clampedActualDistance);
                    SetTrackedMemberValue(gameplayController, "_currentDistance", clampedActualDistance);
                }
            }
        }

        private bool TryGetPrimaryGameplayDistance(out float currentDistance, out string activeMemberName)
        {
            currentDistance = 0f;
            activeMemberName = GameplayDistanceMemberNames[0];

            if (gameplayController == null)
                return false;

            for (var i = 0; i < GameplayDistanceMemberNames.Length; i++)
            {
                var memberName = GameplayDistanceMemberNames[i];
                if (!TryGetFloatMember(gameplayController, memberName, out currentDistance))
                    continue;

                activeMemberName = memberName;
                return true;
            }

            return false;
        }

        private void ApplyGameplayOffset(float pitchDegrees)
        {
            if (gameplayController == null)
                return;

            var effectivePitch = Mathf.Clamp(pitchDegrees, 1f, 89f);
            var nearEyeAlignmentActive = false;
            var player = PlayerHelper.PlayerController;
            if (player != null && !IsPlayerInVehicle() &&
                TryGetPrimaryGameplayDistance(out var distance, out _))
            {
                var alignedDistance = GameplayMinimumZoom + FirstPersonZoomThreshold;
                var alignment = 1f - Mathf.Clamp01(
                    (distance - alignedDistance) / GameplayEyeAlignmentRange);
                if (alignment > 0f)
                {
                    nearEyeAlignmentActive = true;
                    var eyeHeight = GetPlayerEyeHeight(player);
                    var followHeight = 0f;
                    var liveCamera = GetCurrentPedestrianCamera();
                    if (liveCamera != null &&
                        TryGetMemberValue(liveCamera, "Follow", out var followValue) &&
                        followValue is Transform followTarget)
                        followHeight = followTarget.position.y - player.transform.position.y;
                    var alignedPitch = Mathf.Asin(
                        Mathf.Clamp((eyeHeight - followHeight) / Mathf.Max(distance, 0.1f), -0.98f, 0.98f)) *
                        Mathf.Rad2Deg;
                    // Keep the player's right-drag pitch adjustment even at the closest zoom.
                    alignedPitch = Mathf.Clamp(alignedPitch + pitchDegrees -
                        (settings?.GameplayDefaultPitch ?? 35f), 1f, 89f);
                    effectivePitch = Mathf.Lerp(
                        effectivePitch,
                        alignedPitch,
                        Mathf.SmoothStep(0f, 1f, alignment));
                    if (!wasNearEyeAlignmentActive)
                        LogCameraModes($"near zoom eye alignment entered, distance={distance:0.##}, eyeHeight={eyeHeight:0.##}, followHeight={followHeight:0.##}, pitch={effectivePitch:0.##}");
                }
            }

            if (!nearEyeAlignmentActive && wasNearEyeAlignmentActive)
                LogCameraModes("near zoom eye alignment ended");
            wasNearEyeAlignmentActive = nearEyeAlignmentActive;

            var radians = Mathf.Deg2Rad * effectivePitch;
            var offset = new Vector3(0f, Mathf.Sin(radians), -Mathf.Cos(radians));
            if (!lastAppliedGameplayOffset.HasValue || lastAppliedGameplayOffset.Value != offset)
            {
                SetTrackedMemberValue(gameplayController, "offset", offset);
                lastAppliedGameplayOffset = offset;
            }
        }

        private void ApplyGameplayTrackedObjectOffset()
        {
            var liveVirtualCamera = GetLiveVirtualCameraComponent();
            var virtualCameraType = cinematachineVirtualCameraType;
            if (liveVirtualCamera == null || virtualCameraType == null)
                return;

            var trackedOffsetY = eyeHeightPlayer != null ? cachedEyeHeight : GameplayTrackedObjectOffsetY;
            var player = PlayerHelper.PlayerController;
            if (player != null && !IsPlayerInVehicle())
                trackedOffsetY = GetPlayerEyeHeight(player);

            var pipeline = GetCinemachinePipeline(virtualCameraType, liveVirtualCamera);
            if (pipeline == null)
                return;

            foreach (var pipelineComponent in pipeline)
            {
                if (pipelineComponent == null)
                    continue;

                var typeName = pipelineComponent.GetType().Name;
                if (!string.Equals(typeName, "CinemachineComposer", StringComparison.Ordinal))
                    continue;

                if (TryGetMemberValue(pipelineComponent, "m_TrackedObjectOffset", out var trackedOffsetValue) &&
                    trackedOffsetValue is Vector3 trackedOffset)
                {
                    var desiredOffset = trackedOffset;
                    desiredOffset.y = trackedOffsetY;
                    if (desiredOffset != trackedOffset)
                        SetTrackedMemberValue(pipelineComponent, "m_TrackedObjectOffset", desiredOffset);
                    return;
                }

                if (TryGetMemberValue(pipelineComponent, "TrackedObjectOffset", out var publicTrackedOffsetValue) &&
                    publicTrackedOffsetValue is Vector3 publicTrackedOffset)
                {
                    var desiredOffset = publicTrackedOffset;
                    desiredOffset.y = trackedOffsetY;
                    if (desiredOffset != publicTrackedOffset)
                        SetTrackedMemberValue(pipelineComponent, "TrackedObjectOffset", desiredOffset);
                    return;
                }
            }
        }

        private float GetCurrentGameplayPitchForLogging()
        {
            if (settings == null)
                return manualGameplayPitch;

            var minPitch = Mathf.Min(settings.GameplayMinPitch, settings.GameplayMaxPitch);
            var maxPitch = Mathf.Max(settings.GameplayMinPitch, settings.GameplayMaxPitch);
            var currentPitch = hasManualGameplayPitch ? manualGameplayPitch : settings.GameplayDefaultPitch;
            return Mathf.Clamp(currentPitch, minPitch, maxPitch);
        }

        private void UpdateIndoorWallsVisibility(bool cityMapOpen, bool gameplayActive)
        {
            if (cityMapOpen || !gameplayActive || !IsIndoorGameplayCamera(GetLiveVirtualCameraComponent()))
            {
                RestoreForcedIndoorWallsVisibility();
                return;
            }

            var currentWallMode = GetCurrentWallsVisibilityName();
            if (currentWallMode != "AllHidden" && currentWallMode != "PartlyHidden" &&
                currentWallMode != "AllVisible")
                return;

            var currentPitch = GetCurrentGameplayPitchForLogging();
            var desiredMode = firstPersonViewActive
                ? "AllVisible"
                : currentPitch <= IndoorWallsPartlyHiddenPitchThreshold ? "PartlyHidden" : null;
            if (desiredMode == null)
            {
                RestoreForcedIndoorWallsVisibility();
                return;
            }

            if (!firstPersonViewActive && forcedIndoorWallsVisibility == null &&
                string.Equals(currentWallMode, "AllVisible", StringComparison.Ordinal))
                return;

            if (string.Equals(currentWallMode, desiredMode, StringComparison.Ordinal))
                return;

            if (forcedIndoorWallsVisibility == null)
                originalForcedIndoorWallsVisibility = currentWallMode;

            if (TrySetWallsVisibility(desiredMode))
            {
                forcedIndoorWallsVisibility = desiredMode;
                LogIndoorCameraDebug($"walls visibility forced to {desiredMode}, original={originalForcedIndoorWallsVisibility}, firstPerson={firstPersonViewActive}");
            }
        }

        private void RestoreForcedIndoorWallsVisibility()
        {
            if (forcedIndoorWallsVisibility == null)
                return;

            var currentWallMode = GetCurrentWallsVisibilityName();
            if (string.Equals(currentWallMode, forcedIndoorWallsVisibility, StringComparison.Ordinal) &&
                originalForcedIndoorWallsVisibility != null &&
                !string.Equals(currentWallMode, originalForcedIndoorWallsVisibility, StringComparison.Ordinal))
                TrySetWallsVisibility(originalForcedIndoorWallsVisibility);

            LogIndoorCameraDebug($"walls visibility force ended, restored={originalForcedIndoorWallsVisibility ?? "none"}, current={currentWallMode}");
            originalForcedIndoorWallsVisibility = null;
            forcedIndoorWallsVisibility = null;
        }

        private static string GetCurrentWallsVisibilityName()
        {
            var helperType = ResolveWallsVisibilityHelperType();
            if (helperType == null)
                return "helper-missing";

            var field = GetCachedField(helperType, "currentWallsVisibility");
            if (field == null)
                return "field-missing";

            if (field.GetValue(null) is object enumValue)
                return enumValue.ToString() ?? "value-null-name";

            return "value-null";
        }

        private static bool TrySetWallsVisibility(string enumFieldName)
        {
            var helperType = ResolveWallsVisibilityHelperType();
            var enumType = ResolveWallsVisibilityEnumType();
            if (helperType == null || enumType == null)
                return false;

            var enumField = GetCachedField(enumType, enumFieldName);
            var enumValue = enumField?.GetValue(null);
            if (enumValue == null)
                return false;

            var toggleMethod = helperType.GetMethod(
                "ToggleWalls",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { enumType },
                null);
            if (toggleMethod == null)
                return false;

            try
            {
                toggleMethod.Invoke(null, new[] { enumValue });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Type? ResolveWallsVisibilityHelperType()
        {
            return wallsVisibilityHelperType ??= FindType("Buildings.Indoors.WallsVisibilityHelper");
        }

        private static Type? ResolveWallsVisibilityEnumType()
        {
            return wallsVisibilityType ??= FindType("BigAmbitions.InteriorDesigner.WallsVisibility");
        }

        private void HandleScenicViewHotkey()
        {
            if (settings == null || !Input.GetKeyDown(settings.ScenicViewHotkey))
                return;

            if (firstPersonViewActive)
                ExitFirstPersonView("hide-character-hotkey");

            scenicViewEnabled = !scenicViewEnabled;
            if (scenicViewEnabled)
            {
                ApplyScenicView();
            }
            else
            {
                RestoreScenicView();
            }
        }

        private void RefreshScenicViewState()
        {
            if (!scenicViewEnabled)
            {
                if (scenicViewRendererStates.Length > 0)
                    RestoreScenicView();

                return;
            }

            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
                return;

            if (scenicViewTargetRoot == null || scenicViewTargetRoot != playerController)
            {
                ApplyScenicView();
                return;
            }

            var currentRenderers = playerController.GetComponentsInChildren<Renderer>(true);
            if (currentRenderers.Length != scenicViewRendererStates.Length)
            {
                ApplyScenicView();
                return;
            }

            foreach (var state in scenicViewRendererStates)
            {
                if (state.Renderer != null && state.Renderer.enabled)
                    state.Renderer.enabled = false;
            }
        }

        private void ApplyScenicView()
        {
            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
                return;

            RestoreScenicView();

            var renderers = playerController.GetComponentsInChildren<Renderer>(true);
            var states = new List<RendererState>(renderers.Length);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;

                states.Add(new RendererState(renderer, renderer.enabled));
                renderer.enabled = false;
            }

            scenicViewTargetRoot = playerController;
            scenicViewRendererStates = states.ToArray();
        }

        private void RestoreScenicView()
        {
            foreach (var state in scenicViewRendererStates)
            {
                if (state.Renderer != null)
                    state.Renderer.enabled = state.WasEnabled;
            }

            scenicViewRendererStates = Array.Empty<RendererState>();
            scenicViewTargetRoot = null;
        }

        private bool IsGameplayActive()
        {
            if (gameplayController != null && gameplayController.isActiveAndEnabled)
                return true;

            if (gameManagerController != null && gameManagerController.isActiveAndEnabled)
                return true;

            return false;
        }

    }
}
