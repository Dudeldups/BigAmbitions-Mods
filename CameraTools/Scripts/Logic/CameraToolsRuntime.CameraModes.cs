#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BigAmbitions.InputSystem;
using CameraControllers;
using Cinemachine;
using Helpers;
using UI;
using UnityEngine;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime : MonoBehaviour
    {
        private const float FirstPersonZoomThreshold = 0.12f;
        private const float FirstPersonMinimumEyeHeight = 1.55f;
        private const float FirstPersonMaximumEyeHeight = 2.1f;
        private const float FirstPersonLookPitchLimit = 80f;
        private const float FirstPersonLookSensitivityMultiplier = 1f;
        private const float FreeCameraLookSensitivityPerStep = 0.2f;
        private const float NativeFreeCameraLookSensitivity = 0.2f;
        private const float FreeCameraMaxDistance = 75f;
        private const float FreeCameraVerticalSpeed = 2f;
        private const float FreeCameraFastVerticalMultiplier = 5f;
        private const float FirstPersonBuildingTransitionGraceSeconds = 3f;
        private const float FirstPersonPlayerTransitionGraceSeconds = 3f;
        private bool freeCameraOwned;
        private bool freeCameraDistanceLimitActive;
        private int freeCameraEnteredFrame = -1;
        private Transform? freeCameraMovementSnapshotTransform;
        private Vector3 freeCameraMovementSnapshotPosition;
        private Vector3 freeCameraMovementSnapshotUp;
        private bool freeCameraAutoHidUi;
        private bool freeCameraCursorHidden;
        private Texture2D? freeCameraTransparentCursor;
        private readonly Dictionary<CursorData, (Texture2D? Texture, Vector2 Hotspot)> firstPersonNativeCursorTextures = new();
        private int freeCameraEscapeFrame = -1;
        private bool firstPersonViewActive;
        private float firstPersonLookPitch;
        private bool restoreFirstPersonAfterVehicle;
        private float firstPersonPitchBeforeVehicle;
        private float firstPersonPlayerUnavailableUntil;
        private bool loggedFirstPersonVehicleRestoreWait;
        private bool firstPersonMouseCaptured;
        private int firstPersonMouseCaptureFrame = -1;
        private PedestrianCam? firstPersonMouseCaptureCamera;
        private float firstPersonMouseCaptureAngle;
        private bool firstPersonCursorVisibleByUser;
        private bool suppressFirstPersonRightClickUntilRelease;
        private bool wasFirstPersonUiOpen;
        private PedestrianCam? lastUncapturedFirstPersonCamera;
        private float lastUncapturedFirstPersonAngle;
        private CursorLockMode firstPersonPreviousCursorLockMode;
        private bool firstPersonPreviousCursorVisible;
        private bool firstPersonPoseLogged;
        private float firstPersonBuildingTransitionGraceUntil;
        private Component? followOverrideCamera;
        private PedestrianCam? followAutoRotateCamera;
        private bool originalFollowAutoRotate;
        private Transform? originalFollowTarget;
        private Vector3 lastFollowPlayerPosition;
        private bool hasLastFollowPlayerPosition;
        private Transform? firstPersonPlayer;
        private Transform? eyeHeightPlayer;
        private float cachedEyeHeight = FirstPersonMinimumEyeHeight;
        private RendererState[] firstPersonRendererStates = Array.Empty<RendererState>();
        private float lastFirstPersonDistance = float.NaN;

        private bool IsFreeCameraActive()
        {
            return ScreenshotController.isInFreeLookMode;
        }

        private void Update()
        {
            if (timeMachineActive)
                return;

            UpdateFirstPersonMouseCapture();

            if (!freeCameraOwned || !IsFreeCameraActive())
                return;

            if (PlayerAction.OpenMap.Pressed())
            {
                ToggleFreeCamera("map-opening");
                return;
            }

            if (!Input.GetKeyDown(KeyCode.Escape))
            {
                CaptureFreeCameraMovement();
                return;
            }

            PlayerAction.Cancel.Reset();
            PlayerAction.Menu.Reset();
            Input.ResetInputAxes();
            freeCameraEscapeFrame = Time.frameCount;
            ToggleFreeCamera("escape");
        }

        private void UpdateCameraModes(bool cityMapOpen)
        {
            if (settings == null)
                return;

            if (freeCameraEscapeFrame == Time.frameCount)
            {
                var menu = InstanceBehavior<UIs>.Instance?.miniMenuUI;
                if (menu != null && menu.panel.gameObject.activeSelf)
                {
                    menu.Toggle(false);
                    LogCameraModes("suppressed Escape menu after free-camera exit");
                }
            }

            var nativeUiOpen = IsUpdateNoticeSuppressedByNativeUi(cityMapOpen);
            if (freeCameraOwned && (SaveGameManager.Current == null || nativeUiOpen))
                ToggleFreeCamera("native-ui-or-save-unloaded");

            if (Input.GetKeyDown(settings.FreeCameraHotkey) && !nativeUiOpen && SaveGameManager.Current != null)
                ToggleFreeCamera("hotkey");

            if (freeCameraOwned && IsFreeCameraActive())
            {
                ApplyFreeCameraVerticalMotion();
                ApplyFreeCameraLookSensitivity();
                KeepFreeCameraNearPlayer();
            }

            UpdatePlayerFollowCamera(cityMapOpen || nativeUiOpen || IsFreeCameraActive());
        }

        private void ToggleFreeCamera(string reason)
        {
            var screenshot = InstanceBehavior<UIs>.Instance?.screenshot;
            if (screenshot == null || gameManagerController == null)
            {
                context?.Logger.Warn($"CameraTools: free camera unavailable ({reason}); screenshot controller or game manager missing.");
                return;
            }

            if (!ScreenshotController.isInFreeLookMode)
            {
                if (firstPersonViewActive)
                {
                    StopFirstPersonPoiDistanceFilter();
                    RestoreFirstPersonMouseCapture();
                    firstPersonCursorVisibleByUser = false;
                    RestoreFirstPersonRenderers();
                    LogCameraModes("FP cursor state reset for free-camera entry");
                }
                RestorePlayerFollowCamera();
                screenshot.ToggleFreeLookCamera();
                freeCameraOwned = ScreenshotController.isInFreeLookMode;
                if (freeCameraOwned)
                    freeCameraEnteredFrame = Time.frameCount;
                if (freeCameraOwned)
                    HideFreeCameraCursor();
                if (freeCameraOwned)
                    ShowModeHint("cameratools_free_camera_controls_hint");
                if (freeCameraOwned)
                    LogCameraModes($"free-camera movement enabled, verticalSpeed={FreeCameraVerticalSpeed:0.##}m/s, lookSensitivity={settings?.FreeCameraMouseSensitivity ?? 5}");
                if (freeCameraOwned && !isUiHidden)
                {
                    freeCameraAutoHidUi = true;
                    isUiHidden = true;
                    pendingHiddenUiRefreshFrames = HiddenUiRefreshBurstFrames;
                    ApplyHiddenUi();
                }
            }
            else
            {
                freeCameraMovementSnapshotTransform = null;
                screenshot.ToggleFreeLookCamera();
                freeCameraOwned = false;
                freeCameraDistanceLimitActive = false;
                freeCameraEnteredFrame = -1;
                RestoreFreeCameraCursor();
                updateNoticeUi.ClearModeHint();
                if (firstPersonViewActive && PlayerHelper.PlayerController is { } player)
                {
                    BindFirstPersonPlayer(player);
                    if (reason != "map-opening" && reason != "native-ui-or-save-unloaded" &&
                        !IsUpdateNoticeSuppressedByNativeUi(IsCityMapOpen()))
                    {
                        CaptureFirstPersonMouse();
                        ShowModeHint("cameratools_first_person_controls_hint");
                        LogCameraModes("FP mouse look restored after free-camera exit");
                    }
                }
                if (freeCameraAutoHidUi)
                {
                    isUiHidden = false;
                    pendingHiddenUiRefreshFrames = 0;
                    RestoreHiddenUi();
                }
                freeCameraAutoHidUi = false;
            }

            LogCameraModes($"freeCamera={(ScreenshotController.isInFreeLookMode ? "on" : "off")}, reason={reason}");
        }

        private void UpdatePlayerFollowCamera(bool unavailable)
        {
            if (settings == null || !settings.FollowPlayerOnFoot || unavailable || IsPlayerInVehicle())
            {
                RestorePlayerFollowCamera();
                return;
            }

            var player = PlayerHelper.PlayerController;
            var liveCamera = GetCurrentPedestrianCamera();
            if (player == null || liveCamera == null || pedestrianCamType == null ||
                liveCamera.GetComponent(pedestrianCamType) == null)
            {
                RestorePlayerFollowCamera();
                return;
            }

            if (followOverrideCamera == liveCamera)
            {
                SetFollowAutoRotateSuspended(liveCamera.GetComponent<PedestrianCam>(),
                    firstPersonViewActive || Input.GetMouseButton(1));
                if (firstPersonViewActive || Input.GetMouseButton(1))
                {
                    lastFollowPlayerPosition = player.transform.position;
                    hasLastFollowPlayerPosition = true;
                }
                else
                    UpdateFollowYaw(player, liveCamera);
                return;
            }

            RestorePlayerFollowCamera();
            if (!TryGetMemberValue(liveCamera, "Follow", out var currentFollow) || currentFollow is not Transform follow)
            {
                context?.Logger.Warn("CameraTools: player-follow camera has no valid Follow target.");
                return;
            }

            if (follow != player.transform && !SetMemberValue(liveCamera, "Follow", player.transform))
            {
                context?.Logger.Warn("CameraTools: could not set the pedestrian camera Follow target.");
                return;
            }

            followOverrideCamera = liveCamera;
            originalFollowTarget = follow;
            hasLastFollowPlayerPosition = false;
            SetFollowAutoRotateSuspended(liveCamera.GetComponent<PedestrianCam>(),
                firstPersonViewActive || Input.GetMouseButton(1));
            LogCameraModes($"player-follow enabled, camera={liveCamera.name}, originalTarget={follow.name}");
            if (!firstPersonViewActive && !Input.GetMouseButton(1))
                UpdateFollowYaw(player, liveCamera);
        }

        private void UpdateFollowYaw(PlayerController player, Component liveCamera)
        {
            var position = player.transform.position;
            var moved = hasLastFollowPlayerPosition &&
                (position - lastFollowPlayerPosition).sqrMagnitude > 0.0001f;
            lastFollowPlayerPosition = position;
            hasLastFollowPlayerPosition = true;
            if (!moved || liveCamera.GetComponent<PedestrianCam>() is not { } pedestrianCamera)
                return;

            var cameraOffset = liveCamera.transform.position - position;
            cameraOffset.y = 0f;
            var behind = -player.transform.forward;
            behind.y = 0f;
            if (cameraOffset.sqrMagnitude < 0.01f || behind.sqrMagnitude < 0.01f)
                return;

            var currentYaw = Mathf.Atan2(cameraOffset.x, cameraOffset.z) * Mathf.Rad2Deg;
            var targetYaw = Mathf.Atan2(behind.x, behind.z) * Mathf.Rad2Deg;
            pedestrianCamera.angle += Mathf.DeltaAngle(currentYaw, targetYaw) *
                Mathf.Clamp01(Time.unscaledDeltaTime * 3f);
        }

        private void RestorePlayerFollowCamera()
        {
            SetFollowAutoRotateSuspended(null, false);
            if (followOverrideCamera != null && originalFollowTarget != null &&
                !SetMemberValue(followOverrideCamera, "Follow", originalFollowTarget))
                context?.Logger.Warn("CameraTools: could not restore the pedestrian camera Follow target.");

            if (followOverrideCamera != null)
                LogCameraModes("player-follow disabled");

            followOverrideCamera = null;
            originalFollowTarget = null;
            hasLastFollowPlayerPosition = false;
        }

        private void SetFollowAutoRotateSuspended(PedestrianCam? pedestrianCamera, bool suspended)
        {
            if (followAutoRotateCamera != null && (followAutoRotateCamera != pedestrianCamera || !suspended))
            {
                followAutoRotateCamera.autoRotate = originalFollowAutoRotate;
                followAutoRotateCamera = null;
                LogCameraModes("player-follow native auto-rotation restored");
            }

            if (!suspended || pedestrianCamera == null || followAutoRotateCamera == pedestrianCamera)
                return;

            followAutoRotateCamera = pedestrianCamera;
            originalFollowAutoRotate = pedestrianCamera.autoRotate;
            pedestrianCamera.autoRotate = false;
            LogCameraModes("player-follow rotation paused for right-mouse camera control");
        }

        private void KeepFreeCameraNearPlayer()
        {
            var player = PlayerHelper.PlayerController;
            if (player == null || gameManagerController == null ||
                !TryGetMemberValue(gameManagerController, "freeLookCamera", out var cameraValue) ||
                cameraValue is not Component freeCamera)
                return;

            var cameraTransform = freeCamera.transform;
            var playerPosition = player.transform.position;
            if (Input.GetKeyDown(KeyCode.R))
            {
                freeCameraDistanceLimitActive = false;
                cameraTransform.position = playerPosition + Vector3.up * 2f - player.transform.forward * 4f;
                cameraTransform.LookAt(playerPosition + Vector3.up * 1.8f);
                LogCameraModes("free camera returned to player");
                return;
            }

            var offset = cameraTransform.position - playerPosition;
            if (offset.sqrMagnitude > FreeCameraMaxDistance * FreeCameraMaxDistance)
            {
                cameraTransform.position = playerPosition + offset.normalized * FreeCameraMaxDistance;
                if (!freeCameraDistanceLimitActive)
                    LogCameraModes($"free-camera player-distance limit reached, maximum={FreeCameraMaxDistance:0.##}m");
                freeCameraDistanceLimitActive = true;
            }
            else
                freeCameraDistanceLimitActive = false;
        }

        private void CaptureFreeCameraMovement()
        {
            freeCameraMovementSnapshotTransform = null;
            if (gameManagerController == null ||
                !TryGetMemberValue(gameManagerController, "freeLookCamera", out var cameraValue) ||
                cameraValue is not Component freeCamera)
                return;

            freeCameraMovementSnapshotTransform = freeCamera.transform;
            freeCameraMovementSnapshotPosition = freeCamera.transform.position;
            freeCameraMovementSnapshotUp = freeCamera.transform.up;
        }

        private void ApplyFreeCameraVerticalMotion()
        {
            var cameraTransform = freeCameraMovementSnapshotTransform;
            freeCameraMovementSnapshotTransform = null;
            if (cameraTransform == null)
                return;

            // Remove only the native Q/E component; preserve WASD movement and mouse rotation.
            var nativeMovement = cameraTransform.position - freeCameraMovementSnapshotPosition;
            var nativeVerticalMovement = Vector3.Dot(nativeMovement, freeCameraMovementSnapshotUp);
            cameraTransform.position -= freeCameraMovementSnapshotUp * nativeVerticalMovement;

            var direction = (Input.GetKey(KeyCode.Q) ? 1f : 0f) -
                (Input.GetKey(KeyCode.E) ? 1f : 0f);
            if (Mathf.Approximately(direction, 0f))
                return;

            var speed = FreeCameraVerticalSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                speed *= FreeCameraFastVerticalMultiplier;
            cameraTransform.position += Vector3.up * (direction * speed * Time.unscaledDeltaTime);
        }

        private void ApplyFreeCameraLookSensitivity()
        {
            if (Time.frameCount <= freeCameraEnteredFrame + 1)
                return;

            if (gameManagerController == null ||
                !TryGetMemberValue(gameManagerController, "freeLookCamera", out var cameraValue) ||
                cameraValue is not Component freeCamera)
                return;

            var extraSensitivity = (settings?.FreeCameraMouseSensitivity ?? 5) *
                FreeCameraLookSensitivityPerStep - NativeFreeCameraLookSensitivity;
            var mouseX = Input.GetAxis("Mouse X");
            var mouseY = Input.GetAxis("Mouse Y");
            if (Mathf.Abs(mouseX) <= Mathf.Epsilon && Mathf.Abs(mouseY) <= Mathf.Epsilon)
                return;

            var angles = freeCamera.transform.eulerAngles;
            var pitch = Mathf.Clamp(
                Mathf.DeltaAngle(0f, angles.x) - mouseY * extraSensitivity,
                -80f,
                80f);
            freeCamera.transform.rotation = Quaternion.Euler(
                pitch,
                angles.y + mouseX * extraSensitivity,
                0f);
        }

        private void HideFreeCameraCursor()
        {
            EnsureTransparentCursor();
            MouseController.SetRestrictedObjectTypes();
            MouseController.SetCursor(null);
            Cursor.SetCursor(freeCameraTransparentCursor, Vector2.zero, CursorMode.Auto);
            freeCameraCursorHidden = true;
            LogCameraModes("free-camera cursor hidden on entry");
        }

        private void RestoreFreeCameraCursor()
        {
            if (!freeCameraCursorHidden)
                return;

            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            MouseController.Reset();
            freeCameraCursorHidden = false;
            LogCameraModes("free-camera cursor restored on exit");
        }

        private void EnsureTransparentCursor()
        {
            if (freeCameraTransparentCursor != null)
                return;

            freeCameraTransparentCursor = new Texture2D(16, 16, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
            freeCameraTransparentCursor.SetPixels(new Color[16 * 16]);
            freeCameraTransparentCursor.Apply();
        }

        private void HideFirstPersonCursor()
        {
            EnsureTransparentCursor();
            // Unlike free camera, First person must keep native world interactions active.
            // Native MouseController can choose a new hover cursor after a vehicle/HUD
            // transition. Make those cursor choices transparent for this capture only.
            HideNativeFirstPersonCursorTextures();
            MouseController.SetCursor(null);
            Cursor.SetCursor(freeCameraTransparentCursor, Vector2.zero, CursorMode.Auto);
        }

        private void RestoreFirstPersonCursor()
        {
            RestoreNativeFirstPersonCursorTextures();
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            MouseController.Reset();
        }

        private void HideNativeFirstPersonCursorTextures()
        {
            if (firstPersonNativeCursorTextures.Count != 0)
                return;

            var field = typeof(MouseController).GetField("CursorDataDict", BindingFlags.Static | BindingFlags.NonPublic);
            if (field?.GetValue(null) is not Dictionary<CursorType, CursorData> cursorData ||
                freeCameraTransparentCursor == null)
            {
                context?.Logger.Warn("CameraTools: native cursor data unavailable; first-person hover cursor may remain visible.");
                return;
            }

            foreach (var entry in cursorData)
            {
                if (entry.Value == null)
                    continue;

                firstPersonNativeCursorTextures[entry.Value] = (entry.Value.cursorTexture, entry.Value.hotspot);
                entry.Value.cursorTexture = freeCameraTransparentCursor;
                entry.Value.hotspot = Vector2.zero;
            }
            LogCameraModes($"FP native hover cursors hidden for capture: count={firstPersonNativeCursorTextures.Count}");
        }

        private void RestoreNativeFirstPersonCursorTextures()
        {
            if (firstPersonNativeCursorTextures.Count == 0)
                return;

            foreach (var entry in firstPersonNativeCursorTextures)
            {
                if (entry.Key != null)
                {
                    entry.Key.cursorTexture = entry.Value.Texture;
                    entry.Key.hotspot = entry.Value.Hotspot;
                }
            }
            LogCameraModes($"FP native hover cursors restored: count={firstPersonNativeCursorTextures.Count}");
            firstPersonNativeCursorTextures.Clear();
        }

        private bool IsPlayerInVehicle()
        {
            return gameManagerController != null && IsDrivingVehicleSafe() &&
                activeVehicleCameraRoot != null;
        }

        private static bool IsDrivingVehicleSafe()
        {
            return InstanceBehavior<GameManager>.Instance != null && GameManager.IsDrivingVehicle();
        }

        private void UpdateFirstPersonZoom(bool cityMapOpen)
        {
            var enteringBuilding = InstanceBehavior<BuildingManager>.Instance?.enteringBuilding == true;
            if (firstPersonViewActive && enteringBuilding)
                firstPersonBuildingTransitionGraceUntil = Time.unscaledTime + FirstPersonBuildingTransitionGraceSeconds;

            var preservingBuildingTransition = firstPersonViewActive &&
                (enteringBuilding || Time.unscaledTime < firstPersonBuildingTransitionGraceUntil);
            if (settings == null || !settings.EnableFirstPersonZoom || SaveGameManager.Current == null ||
                InstanceBehavior<GameManager>.Instance == null)
            {
                restoreFirstPersonAfterVehicle = false;
                loggedFirstPersonVehicleRestoreWait = false;
                ExitFirstPersonView("unavailable");
                return;
            }

            if (IsDrivingVehicleSafe())
            {
                if (firstPersonViewActive)
                {
                    firstPersonPitchBeforeVehicle = firstPersonLookPitch;
                    restoreFirstPersonAfterVehicle = true;
                    loggedFirstPersonVehicleRestoreWait = false;
                    ExitFirstPersonView("vehicle-entered");
                    LogCameraModes($"FP suspended for vehicle, pitch={firstPersonPitchBeforeVehicle:0.##}");
                }
                return;
            }

            if (IsFreeCameraActive())
                return;

            var player = PlayerHelper.PlayerController;
            if (player == null)
            {
                if (firstPersonViewActive && firstPersonPlayerUnavailableUntil <= 0f)
                    firstPersonPlayerUnavailableUntil = Time.unscaledTime + FirstPersonPlayerTransitionGraceSeconds;
                if (restoreFirstPersonAfterVehicle || preservingBuildingTransition ||
                    (firstPersonViewActive && Time.unscaledTime < firstPersonPlayerUnavailableUntil))
                {
                    if (restoreFirstPersonAfterVehicle && !loggedFirstPersonVehicleRestoreWait)
                    {
                        LogCameraModes("FP vehicle restoration waiting for player controller");
                        loggedFirstPersonVehicleRestoreWait = true;
                    }
                    return;
                }

                ExitFirstPersonView("save-or-player-unavailable");
                return;
            }
            firstPersonPlayerUnavailableUntil = 0f;

            if (restoreFirstPersonAfterVehicle)
            {
                if (enteringBuilding || cityMapOpen || activeVehicleCameraRoot != null ||
                    IsUpdateNoticeSuppressedByNativeUi(cityMapOpen) ||
                    GetCurrentPedestrianCamera()?.GetComponent<PedestrianCam>() is not { isActiveAndEnabled: true })
                {
                    if (!loggedFirstPersonVehicleRestoreWait)
                    {
                        LogCameraModes("FP vehicle restoration waiting for pedestrian camera or native UI");
                        loggedFirstPersonVehicleRestoreWait = true;
                    }
                    return;
                }

                firstPersonViewActive = true;
                restoreFirstPersonAfterVehicle = false;
                loggedFirstPersonVehicleRestoreWait = false;
                firstPersonCursorVisibleByUser = false;
                firstPersonLookPitch = firstPersonPitchBeforeVehicle;
                lastFirstPersonDistance = GameplayMinimumZoom;
                BindFirstPersonPlayer(player);
                CaptureFirstPersonMouse();
                ShowModeHint("cameratools_first_person_controls_hint");
                LogCameraModes($"FP restored after vehicle exit, player={player.name}, pitch={firstPersonLookPitch:0.##}");
            }

            if (firstPersonViewActive)
            {
                if (firstPersonPlayer != player.transform || firstPersonRendererStates.Length == 0)
                    BindFirstPersonPlayer(player);
            }

            if (enteringBuilding || cityMapOpen || IsUpdateNoticeSuppressedByNativeUi(cityMapOpen) ||
                (!firstPersonMouseCaptured && IsGameplayInputBlockedByUi(forceRefresh: true)))
                return;

            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) <= Mathf.Epsilon)
                scroll = Input.GetAxis("Mouse ScrollWheel") * 120f;

            if (firstPersonViewActive)
            {
                if (scroll < -Mathf.Epsilon)
                {
                    ExitFirstPersonView("zoom-out");
                    var exitDistance = GameplayMinimumZoom + FirstPersonZoomThreshold;
                    if (gameplayController != null)
                        SetTrackedMemberValue(gameplayController, "distance", exitDistance);
                    lastFirstPersonDistance = exitDistance;
                }
                else
                    UpdateFirstPersonLook();
                return;
            }

            var liveCamera = GetCurrentPedestrianCamera();
            if (liveCamera == null || pedestrianCamType == null ||
                liveCamera.GetComponent(pedestrianCamType) == null ||
                !TryGetPrimaryGameplayDistance(out var distance, out _))
                return;

            var wasAlreadyAtClosestZoom = !float.IsNaN(lastFirstPersonDistance) &&
                lastFirstPersonDistance <= GameplayMinimumZoom + FirstPersonZoomThreshold;
            lastFirstPersonDistance = distance;
            if (scroll > Mathf.Epsilon && wasAlreadyAtClosestZoom &&
                distance <= GameplayMinimumZoom + FirstPersonZoomThreshold)
            {
                firstPersonViewActive = true;
                firstPersonLookPitch = 0f;
                firstPersonBuildingTransitionGraceUntil = 0f;
                BindFirstPersonPlayer(player);
                ShowModeHint("cameratools_first_person_controls_hint");
                LogCameraModes($"first-person entered, distance={distance:0.##}");
            }
        }

        private void BindFirstPersonPlayer(PlayerController player)
        {
            RestoreFirstPersonRenderers();
            firstPersonPoseLogged = false;
            firstPersonPlayer = player.transform;
            eyeHeightPlayer = null;
            var eyeHeight = GetPlayerEyeHeight(player);
            HidePlayerForFirstPerson(player);
            StartFirstPersonPoiDistanceFilter(player.transform);
            LogCameraModes($"first-person bound to player={player.name}, eyeHeight={eyeHeight:0.##}");
        }

        private float GetPlayerEyeHeight(PlayerController player)
        {
            if (eyeHeightPlayer == player.transform)
                return cachedEyeHeight;

            eyeHeightPlayer = player.transform;
            var rootPosition = player.transform.position;
            var eyeHeight = FirstPersonMinimumEyeHeight;
            foreach (var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == null || renderer.bounds.size.y < 1f)
                    continue;

                var top = renderer.bounds.max.y - rootPosition.y - 1.05f;
                eyeHeight = Mathf.Max(eyeHeight, top);
            }

            cachedEyeHeight = Mathf.Clamp(eyeHeight, FirstPersonMinimumEyeHeight, FirstPersonMaximumEyeHeight);
            return cachedEyeHeight;
        }

        private void UpdateFirstPersonLook()
        {
            if (!firstPersonMouseCaptured)
                return;

            if (Time.frameCount <= firstPersonMouseCaptureFrame + 1)
            {
                if (firstPersonMouseCaptureCamera != null)
                    firstPersonMouseCaptureCamera.angle = firstPersonMouseCaptureAngle;
                return;
            }

            var sensitivity = (settings?.FirstPersonMouseSensitivity ?? 5) * FirstPersonLookSensitivityMultiplier;
            firstPersonLookPitch = Mathf.Clamp(
                firstPersonLookPitch - Input.GetAxis("Mouse Y") * PitchStepPerMousePixel * sensitivity,
                -FirstPersonLookPitchLimit,
                FirstPersonLookPitchLimit);

            if (GetCurrentPedestrianCamera()?.GetComponent<PedestrianCam>() is { } pedestrianCamera)
            {
                var mouseX = Input.GetAxis("Mouse X");
                pedestrianCamera.angle +=
                    (PedestrianCam.invertRotation ? mouseX : -mouseX) *
                    pedestrianCamera.mouseSensitivity * sensitivity;
            }
        }

        private void UpdateFirstPersonMouseCapture()
        {
            if (!firstPersonViewActive || SaveGameManager.Current == null ||
                InstanceBehavior<GameManager>.Instance == null || IsDrivingVehicleSafe())
            {
                RestoreFirstPersonMouseCapture();
                firstPersonCursorVisibleByUser = false;
                lastUncapturedFirstPersonCamera = null;
                wasFirstPersonUiOpen = false;
                suppressFirstPersonRightClickUntilRelease = false;
                return;
            }

            var cityMapOpen = IsCityMapOpen();
            var nativeUiOpen = IsUpdateNoticeSuppressedByNativeUi(cityMapOpen);
            if (IsFreeCameraActive() || nativeUiOpen)
            {
                if (nativeUiOpen && !wasFirstPersonUiOpen)
                    LogCameraModes($"FP mouse capture suspended for native UI: options={IsOptionsMenuOpen()}, cityMap={cityMapOpen}");
                suppressFirstPersonRightClickUntilRelease = false;
                RestoreFirstPersonMouseCapture();
                if (nativeUiOpen && !wasFirstPersonUiOpen)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                wasFirstPersonUiOpen = nativeUiOpen;
                RememberUncapturedFirstPersonAngle();
                return;
            }
            wasFirstPersonUiOpen = false;

            if (suppressFirstPersonRightClickUntilRelease)
            {
                var rightMouseStillDown = Input.GetMouseButton(1);
                var rightMouseReleased = Input.GetMouseButtonUp(1);
                Input.ResetInputAxes();
                if (!rightMouseStillDown || rightMouseReleased)
                    suppressFirstPersonRightClickUntilRelease = false;
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                firstPersonCursorVisibleByUser = !firstPersonCursorVisibleByUser;
                suppressFirstPersonRightClickUntilRelease = true;
                Input.ResetInputAxes();
                if (firstPersonCursorVisibleByUser)
                {
                    RestoreFirstPersonMouseCapture();
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    RememberUncapturedFirstPersonAngle();
                }
                else
                    CaptureFirstPersonMouse();
                LogCameraModes($"FP cursor toggled, visible={firstPersonCursorVisibleByUser}, native right-click suppressed until release");
                return;
            }

            if (firstPersonCursorVisibleByUser)
            {
                RememberUncapturedFirstPersonAngle();
                return;
            }

            if (firstPersonMouseCaptured)
                return;

            CaptureFirstPersonMouse();
        }

        private void RememberUncapturedFirstPersonAngle()
        {
            var camera = GetCurrentPedestrianCamera()?.GetComponent<PedestrianCam>();
            lastUncapturedFirstPersonCamera = camera;
            if (camera != null)
                lastUncapturedFirstPersonAngle = camera.angle;
        }

        private void CaptureFirstPersonMouse()
        {
            firstPersonPreviousCursorLockMode = Cursor.lockState;
            firstPersonPreviousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            HideFirstPersonCursor();
            firstPersonMouseCaptured = true;
            firstPersonMouseCaptureFrame = Time.frameCount;
            firstPersonMouseCaptureCamera = GetCurrentPedestrianCamera()?.GetComponent<PedestrianCam>();
            firstPersonMouseCaptureAngle = firstPersonMouseCaptureCamera != null &&
                firstPersonMouseCaptureCamera == lastUncapturedFirstPersonCamera
                    ? lastUncapturedFirstPersonAngle
                    : firstPersonMouseCaptureCamera != null ? firstPersonMouseCaptureCamera.angle : 0f;
            LogCameraModes($"FP mouse look captured, sensitivity={settings?.FirstPersonMouseSensitivity ?? 5}, initialMouseDeltaSuppressed=true");
        }

        private void RestoreFirstPersonMouseCapture()
        {
            if (!firstPersonMouseCaptured)
                return;

            RestoreFirstPersonCursor();
            Cursor.lockState = firstPersonPreviousCursorLockMode;
            Cursor.visible = firstPersonPreviousCursorVisible;
            firstPersonMouseCaptured = false;
            firstPersonMouseCaptureCamera = null;
            LogCameraModes("FP mouse look released");
        }

        private void HidePlayerForFirstPerson(PlayerController player)
        {
            var renderers = player.GetComponentsInChildren<Renderer>(true);
            var states = new List<RendererState>(renderers.Length);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;

                states.Add(new RendererState(renderer, renderer.enabled));
                renderer.enabled = false;
            }

            firstPersonRendererStates = states.ToArray();
        }

        private void RestoreFirstPersonRenderers()
        {
            foreach (var state in firstPersonRendererStates)
            {
                if (state.Renderer != null)
                    state.Renderer.enabled = state.WasEnabled;
            }

            firstPersonRendererStates = Array.Empty<RendererState>();
        }

        private void HandleBrainCameraUpdated(CinemachineBrain brain)
        {
            if (timeMachineActive)
                return;

            if (brain != null && brain.OutputCamera != null)
            {
                ApplyFirstPersonCameraPose(brain.OutputCamera, "cinemachine-brain");
                ApplyVehicleCameraPose(brain.OutputCamera);
            }
        }

        private void ApplyFirstPersonCameraPose(Camera camera, string source = "render")
        {
            if (!firstPersonViewActive || firstPersonPlayer == null ||
                (camera != GetLiveMainCamera() && camera != Camera.main) ||
                SaveGameManager.Current == null || IsDrivingVehicleSafe() ||
                IsCityMapOpen() || IsFreeCameraActive())
                return;

            var player = PlayerHelper.PlayerController;
            var eyeHeight = player != null ? GetPlayerEyeHeight(player) : FirstPersonMinimumEyeHeight;
            var rootEyePosition = firstPersonPlayer.position + Vector3.up * eyeHeight;
            camera.transform.position = rootEyePosition;
            camera.transform.rotation = Quaternion.Euler(firstPersonLookPitch, camera.transform.eulerAngles.y, 0f);
            if (!firstPersonPoseLogged)
            {
                firstPersonPoseLogged = true;
                LogCameraModes($"first-person pose applied, source={source}, camera={camera.name}, player={firstPersonPlayer.name}, eyeHeight={eyeHeight:0.##}, position={camera.transform.position}, rotation={camera.transform.eulerAngles}");
            }
        }

        private void ExitFirstPersonView(string reason)
        {
            if (!firstPersonViewActive)
                return;

            StopFirstPersonPoiDistanceFilter();
            firstPersonViewActive = false;
            updateNoticeUi.ClearModeHint();
            RestoreFirstPersonMouseCapture();
            firstPersonLookPitch = 0f;
            firstPersonBuildingTransitionGraceUntil = 0f;
            firstPersonPlayer = null;
            lastFirstPersonDistance = float.NaN;
            RestoreFirstPersonRenderers();
            LogCameraModes($"first-person exited, reason={reason}");
        }

        private void ExitCameraModes()
        {
            restoreFirstPersonAfterVehicle = false;
            loggedFirstPersonVehicleRestoreWait = false;
            ExitFirstPersonView("shutdown");
            RestorePlayerFollowCamera();
            if (freeCameraOwned && ScreenshotController.isInFreeLookMode)
                ToggleFreeCamera("shutdown");
            RestoreFreeCameraCursor();
            if (freeCameraTransparentCursor != null)
                Destroy(freeCameraTransparentCursor);
            freeCameraTransparentCursor = null;
            updateNoticeUi.ClearModeHint();
            freeCameraOwned = false;
            freeCameraAutoHidUi = false;
        }

        private Component? GetCurrentPedestrianCamera()
        {
            if (gameManagerController == null)
                return null;

            var memberName = BuildingManager.IsInsideBuilding ? "indoorCamera" : "pedestrianCamera";
            if (TryGetMemberValue(gameManagerController, memberName, out var cameraValue) &&
                cameraValue is Component pedestrianCamera && pedestrianCamType != null &&
                pedestrianCamera.GetComponent(pedestrianCamType) != null)
                return pedestrianCamera;

            return null;
        }

        private void ShowModeHint(string localizationKey)
        {
            var titleKey = localizationKey == "cameratools_free_camera_controls_hint"
                ? "cameratools_free_camera_controls_title"
                : "cameratools_first_person_controls_title";
            updateNoticeUi.ShowModeHint(titleKey, localizationKey);
            LogCameraModes($"control hint shown, key={localizationKey}");
        }

        private void LogCameraModes(string message)
        {
            if (settings?.EnableCameraToolsDebug == true && settings.EnableCameraModesDebugLogging)
                context?.Logger.Info($"CameraTools camera modes: {message}");
        }
    }
}
