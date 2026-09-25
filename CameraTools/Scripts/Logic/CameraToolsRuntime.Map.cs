#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Helpers;
using UI.Notification;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime : MonoBehaviour
    {
        private void ConfigureMapController()
        {
            if (settings == null || mapController == null || !settings.EnableMapTopDown)
                return;

            var controllerId = mapController.GetInstanceID();
            var bounds = GetVector2Member(mapController, "minMaxDistance");
            if (lastAppliedMapMaxZoom != settings.MapDistance || controllerId != lastConfiguredMapControllerId)
            {
                bounds.x = Mathf.Min(bounds.x, MapMinimumZoom);
                bounds.y = Mathf.Max(bounds.y, settings.MapDistance);
                SetTrackedMemberValue(mapController, "minMaxDistance", bounds);
                lastAppliedMapMaxZoom = settings.MapDistance;
                lastConfiguredMapControllerId = controllerId;
            }

            var desiredDistance = Mathf.Clamp(settings.MapDistance, bounds.x, bounds.y);
            var shouldSeedDistance =
                !hasInitializedMapDistanceForCurrentOpen ||
                !Mathf.Approximately(lastAppliedMapDistanceSetting, desiredDistance);

            if (!shouldSeedDistance)
                return;

            var currentDistance = Mathf.Clamp(GetFloatMember(mapController, "distance"), bounds.x, bounds.y);
            if (currentDistance < desiredDistance)
            {
                SetTrackedMemberValue(mapController, "distance", desiredDistance);
                currentDistance = desiredDistance;
            }

            SetSavedMapZoom(currentDistance);
            hasInitializedMapDistanceForCurrentOpen = true;
            lastAppliedMapDistanceSetting = desiredDistance;
        }

        private void ApplyMapTweaks(bool cityMapOpen)
        {
            if (cameraToolsDebugEnabled)
            {
                var lifecycleSummary = $"Map state: cityMapOpen={cityMapOpen}, mapController={(mapController == null ? "null" : mapController.GetType().FullName)}, activeMapRenderCamera={(activeMapRenderCamera == null ? "null" : activeMapRenderCamera.name)}, activeMapVcam={(activeMapVcamTransform == null ? "null" : activeMapVcamTransform.name)}";
                if (lastMapLifecycleLogSummary != lifecycleSummary)
                {
                    lastMapLifecycleLogSummary = lifecycleSummary;
                    LogVehicleDebug(lifecycleSummary);
                }
            }

            if (cityMapOpen && !wasCityMapOpen)
            {
                StopFirstPersonPoiDistanceFilter();
                hasInitializedMapDistanceForCurrentOpen = false;
                hasManualMapPitch = false;
                isTrackingMapRightMousePitch = false;
                lastConfiguredMapControllerId = 0;
                desiredMapDistance = float.NaN;
                if (cameraToolsDebugEnabled)
                    LogVehicleDebug("Map opened: resetting desiredMapDistance.");
            }

            if (!cityMapOpen)
            {
                hasInitializedMapDistanceForCurrentOpen = false;
                desiredMapDistance = float.NaN;
                isTrackingMapRightMousePitch = false;
                activeMapVcamTransform = null;
                RestoreMapCameraState();
                if (cameraToolsDebugEnabled && wasCityMapOpen)
                    LogVehicleDebug("Map closed: clearing desiredMapDistance.");
                if (wasCityMapOpen && firstPersonViewActive && PlayerHelper.PlayerController is { } player)
                    StartFirstPersonPoiDistanceFilter(player.transform);
                wasCityMapOpen = false;
                return;
            }

            wasCityMapOpen = cityMapOpen;

            if (settings == null || mapController == null || !settings.EnableMapTopDown)
            {
                RestoreMapCameraState();
                if (cameraToolsDebugEnabled && cityMapOpen)
                    LogVehicleDebug($"Map early exit: settingsNull={settings == null}, mapControllerNull={mapController == null}, mapTopDownEnabled={(settings != null && settings.EnableMapTopDown)}");
                return;
            }

            ConfigureMapController();

            var mapVcamTransform = ResolveMapVcamTransform(mapController);
            var mapRenderCamera = GetLiveMainCamera();
            if (mapVcamTransform == null && mapRenderCamera == null)
            {
                if (cameraToolsDebugEnabled)
                    LogVehicleDebug("Map early exit: ResolveMapVcamTransform and render camera both returned null.");
                return;
            }

            if (activeMapRenderCamera != mapRenderCamera)
            {
                RestoreMapCameraState();
                activeMapRenderCamera = mapRenderCamera;
                if (mapRenderCamera != null)
                    activeMapRenderCameraState = new CameraState(mapRenderCamera);
            }

            activeMapVcamTransform = mapVcamTransform;

            var bounds = GetVector2Member(mapController, "minMaxDistance");
            var currentDistance = Mathf.Clamp(GetFloatMember(mapController, "distance"), bounds.x, bounds.y);
            if (float.IsNaN(desiredMapDistance))
                desiredMapDistance = Mathf.Clamp(currentDistance, bounds.x, bounds.y);

            if (!hasManualMapPitch)
                manualMapPitch = Mathf.Clamp(settings.MapPitch, MapMinimumPitch, MapMaximumPitch);

            if (Input.GetMouseButtonDown(1))
            {
                isTrackingMapRightMousePitch = true;
                lastRightMouseY = Input.mousePosition.y;
            }

            if (Input.GetMouseButton(1) && isTrackingMapRightMousePitch)
            {
                var currentMouseY = Input.mousePosition.y;
                var deltaY = currentMouseY - lastRightMouseY;
                lastRightMouseY = currentMouseY;

                if (Mathf.Abs(deltaY) > Mathf.Epsilon)
                {
                    manualMapPitch = Mathf.Clamp(manualMapPitch - deltaY * PitchStepPerMousePixel, MapMinimumPitch, MapMaximumPitch);
                    hasManualMapPitch = true;
                }
            }

            if (Input.GetMouseButtonUp(1))
                isTrackingMapRightMousePitch = false;

            var rawScrollDelta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(rawScrollDelta) <= Mathf.Epsilon)
                rawScrollDelta = Input.GetAxis("Mouse ScrollWheel") * 120f;

            var vanillaDelta = currentDistance - desiredMapDistance;
            if (Mathf.Abs(vanillaDelta) > MapScrollDeltaThreshold)
            {
                desiredMapDistance = Mathf.Clamp(
                    desiredMapDistance + (vanillaDelta * MapScrollStepMultiplier),
                    bounds.x,
                    bounds.y);
            }

            desiredMapDistance = Mathf.Clamp(desiredMapDistance, bounds.x, bounds.y);
            if (cameraToolsDebugEnabled)
            {
                mapDebugCurrentDistance = currentDistance;
                mapDebugDesiredDistance = desiredMapDistance;
                mapDebugRawScrollDelta = rawScrollDelta;
                mapDebugVanillaDelta = vanillaDelta;

                var mapSummary =
                    $"Map zoom input: currentDistance={mapDebugCurrentDistance:0.##}, desiredMapDistance={mapDebugDesiredDistance:0.##}, " +
                    $"vanillaDelta={mapDebugVanillaDelta:0.##}, rawScrollDelta={mapDebugRawScrollDelta:0.##}";
                if (lastMapDebugLogSummary != mapSummary &&
                    (Mathf.Abs(mapDebugVanillaDelta) > MapScrollDeltaThreshold || Mathf.Abs(mapDebugRawScrollDelta) > Mathf.Epsilon))
                {
                    lastMapDebugLogSummary = mapSummary;
                    LogVehicleDebug(mapSummary);
                }
            }
            ApplyMapCameraState();
        }

        private void ApplyMapCameraState()
        {
            if (settings == null || mapController == null || float.IsNaN(desiredMapDistance))
                return;

            var bounds = GetVector2Member(mapController, "minMaxDistance");
            var distance = Mathf.Clamp(desiredMapDistance, bounds.x, bounds.y);
            desiredMapDistance = distance;
            SetTrackedMemberValue(mapController, "distance", distance);
            SetSavedMapZoom(distance);
            if (cameraToolsDebugEnabled)
            {
                var readBackDistance = Mathf.Clamp(GetFloatMember(mapController, "distance"), bounds.x, bounds.y);
                var applySummary =
                    $"Map zoom apply: desiredMapDistance={desiredMapDistance:0.##}, readBackDistance={readBackDistance:0.##}, activeMapRenderCamera={(activeMapRenderCamera == null ? "null" : activeMapRenderCamera.name)}, activeMapVcam={(activeMapVcamTransform == null ? "null" : activeMapVcamTransform.name)}";
                if (lastMapApplyLogSummary != applySummary)
                {
                    lastMapApplyLogSummary = applySummary;
                    LogVehicleDebug(applySummary);
                }
            }

            var currentAngle = GetFloatMember(mapController, "_currentAngle");
            var pitch = Mathf.Clamp(hasManualMapPitch ? manualMapPitch : settings.MapPitch, MapMinimumPitch, MapMaximumPitch);
            var pitchRadians = pitch * Mathf.Deg2Rad;
            var height = distance * Mathf.Sin(pitchRadians);
            var horizontalRadius = Mathf.Max(0.05f, distance * Mathf.Cos(pitchRadians));
            var orbit = Quaternion.Euler(0f, currentAngle, 0f) * new Vector3(0f, height, -horizontalRadius);
            var rootPosition = mapController.transform.position;
            var targetPosition = rootPosition + orbit;
            var upAxis = Quaternion.Euler(0f, currentAngle, 0f) * Vector3.forward;
            var targetRotation = Quaternion.LookRotation(rootPosition - targetPosition, upAxis);

            var vCamTransform = activeMapVcamTransform ?? ResolveMapVcamTransform(mapController);
            if (vCamTransform != null)
            {
                activeMapVcamTransform = vCamTransform;
                vCamTransform.position = targetPosition;
                vCamTransform.rotation = targetRotation;
            }

            if (activeMapRenderCamera != null)
            {
                activeMapRenderCamera.transform.position = targetPosition;
                activeMapRenderCamera.transform.rotation = targetRotation;
            }
        }

        private void RestoreMapCameraState()
        {
            if (activeMapRenderCamera != null)
            {
                activeMapRenderCameraState.Restore(activeMapRenderCamera);
                activeMapRenderCamera = null;
            }

            activeMapVcamTransform = null;
        }

        private Transform? ResolveMapVcamTransform(Component controller)
        {
            var vCamTransform = GetTransformMember(controller, "_vCam");
            if (vCamTransform != null)
                return vCamTransform;

            if (TryGetMemberValue(controller, "_vCam", out var vCamValue) && vCamValue != null)
            {
                if (vCamValue is Transform transform)
                    return transform;

                if (vCamValue is Component component)
                    return component.transform;

                if (vCamValue is GameObject gameObject)
                    return gameObject.transform;
            }

            if (gameManagerController != null &&
                TryGetMemberValue(gameManagerController, "citymapCamera", out var cityMapCameraValue) &&
                cityMapCameraValue != null)
            {
                if (cityMapCameraValue is Transform cityMapTransform)
                    return cityMapTransform;

                if (cityMapCameraValue is Component cityMapComponent)
                    return cityMapComponent.transform;

                if (cityMapCameraValue is GameObject cityMapObject)
                    return cityMapObject.transform;
            }

            return null;
        }

        private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            HandleCameraPreCull(camera);
        }

        private void HandleCameraPreCull(Camera camera)
        {
            if (settings == null || timeMachineActive)
                return;

            ApplyFirstPersonCameraPose(camera);
            ApplyVehicleCameraPose(camera);

            var cityMapOpen = IsCityMapOpen();
            if (cityMapOpen && activeMapRenderCamera != null && camera == activeMapRenderCamera)
            {
                ApplyMapCameraState();
            }

            if (!cameraToolsDebugEnabled ||
                !needsVehicleDistanceReapply ||
                float.IsNaN(desiredVehicleDistance) ||
                cachedVehicleCameras == null ||
                !vehicleDebug.IsVehicleMode)
                return;

            foreach (var vehicleCamera in cachedVehicleCameras)
            {
                if (vehicleCamera == null || !vehicleCamera.gameObject.activeInHierarchy)
                    continue;

                var vehicleCameraComponent = vehicleCamera.GetComponent<Camera>();
                var nestedCamera = vehicleCamera.GetComponentInChildren<Camera>(true);
                if (vehicleCameraComponent != camera && nestedCamera != camera)
                    continue;

                ApplyVehicleCameraDistance(vehicleCamera.gameObject, desiredVehicleDistance, settings.VehicleMaxZoom);
            }
        }

        private static Camera? GetLiveMainCamera()
        {
            if (gameManagerType != null)
            {
                var getMainCameraMethod = gameManagerType.GetMethod("GetMainCamera", BindingFlags.Public | BindingFlags.Static);
                if (getMainCameraMethod?.Invoke(null, null) is Camera gameManagerCamera)
                    return gameManagerCamera;
            }

            return Camera.main;
        }

        private static void SetSavedMapZoom(float zoom)
        {
            var type = saveGameManagerType;
            if (type == null)
                return;

            var currentProperty = type.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
            var current = currentProperty?.GetValue(null);
            if (current == null)
                return;

            SetMemberValue(current, "cityMapZoom", zoom);
        }

    }
}
