#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Cinemachine;
using Helpers;
using UI;
using UI.Notification;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CameraTools
{
    [DefaultExecutionOrder(-10000)]
    public sealed partial class CameraToolsRuntime : MonoBehaviour
    {
        private const float PitchStepPerMousePixel = 0.15f;
        private const float VehicleYawStepPerMousePixel = 0.2f;
        private const float VehicleYawSlowResetSpeedDegreesPerSecond = 40f;
        private const float VehicleYawResetSpeedDegreesPerSecond = 160f;
        private const float VehicleYawAutoResetHighSpeedThreshold = 12f;
        private const float VehicleYawAutoResetLowSpeedThreshold = VehicleYawAutoResetHighSpeedThreshold * 0.25f;
        private const float VehicleMinimumZoom = 1.5f;
        private const float VehicleZoomStepPerScrollTick = 4f;
        private const float VehicleFineZoomStepPerScrollTick = 0.5f;
        private const float VehicleFineZoomRange = 5f;
        private const float VehicleZoomSmoothTime = 0.18f;
        private const float VehicleChaseBaseHeightOffset = -0.5f;
        private const float VehicleBumperBaseHeightOffset = 0.2f;
        private const float VehicleBumperFovIncrease = 18f;
        private const float VehicleForcedZoomStep = 20f;
        private const float GameplayMinimumZoom = 2.75f;
        private const float GameplayEyeAlignmentRange = 2.5f;
        private const float GameplayRightMouseDragDeadZonePixels = 7f;
        private const float GameplayTrackedObjectOffsetY = 1.15f;
        private const float IndoorWallsPartlyHiddenPitchThreshold = 32.45f;
        private const float MapMinimumZoom = 120f;
        private const float MapScrollStepMultiplier = 7f;
        private const float MapScrollDeltaThreshold = 0.1f;
        private const float MapMinimumPitch = 45f;
        private const float MapMaximumPitch = 90f;
        private const float UiStateRefreshIntervalSeconds = 0.15f;
        private const float HiddenUiRefreshIntervalSeconds = 1f;
        private const float VehicleOverwriteThreshold = 0.5f;
        private const float VehicleSearchIntervalSeconds = 5f;
        private const string GameManagerTypeName = "GameManager";
        private const string CarControllerTypeName = "CarController";
        private const string VehicleControllerTypeName = "VehicleController";
        private const string PedestrianCamTypeName = "PedestrianCam";
        private const string CityMapCamTypeName = "CityMapCam";
        private const string CameraMouseDragTypeName = "CameraMouseDrag";
        private const string SaveGameManagerTypeName = "SaveGameManager";
        private static readonly string[] VehicleCameraMemberNames =
        {
            "vehicleCamera",
            "vehicleCameraReverse",
            "indoorVehicleCamera",
            "indoorVehicleCameraReverse"
        };
        private static readonly string[] VehicleControllerMemberNames =
        {
            "vehicleController",
            "_vehicleController",
            "VehicleController"
        };
        private static readonly string[] VehicleDistanceMemberNames =
        {
            "vehicleToCamDistance",
            "VehicleToCamDistance",
            "cameraDistance",
            "CameraDistance",
            "distance",
            "Distance"
        };
        private static readonly string[] VehicleMinDistanceMemberNames =
        {
            "minCameraDistance",
            "MinCameraDistance",
            "vehicleCameraMinDistance",
            "VehicleCameraMinDistance",
            "minDistance",
            "MinDistance"
        };
        private static readonly string[] VehicleMaxDistanceMemberNames =
        {
            "maxCameraDistance",
            "MaxCameraDistance",
            "vehicleCameraMaxDistance",
            "VehicleCameraMaxDistance",
            "maxDistance",
            "MaxDistance"
        };
        private static readonly string[] VehicleStateKeywords =
        {
            "player",
            "driver",
            "controlled",
            "active",
            "entered",
            "inside",
            "seated",
            "occupant",
            "controller",
            "camera",
            "vehicle"
        };
        private static readonly string[] VehicleCameraKeywords =
        {
            "cam",
            "camera",
            "distance",
            "zoom",
            "follow",
            "orbit",
            "chase",
            "view",
            "look",
            "third",
            "person",
            "vehicleToCam",
            "cameraDistance",
            "maxDistance",
            "minDistance"
        };
        private static readonly string[] GameplayCameraKeywords =
        {
            "cam",
            "camera",
            "distance",
            "zoom",
            "offset",
            "min",
            "max",
            "follow",
            "look"
        };
        private static readonly string[] HiddenUiIncludeKeywords =
        {
            "portrait",
            "avatar",
            "face",
            "objective",
            "objectives",
            "status",
            "topbar",
            "needs",
            "energy",
            "happiness",
            "hunger",
            "money",
            "day",
            "time",
            "filter",
            "filters",
            "search",
            "bizphone",
            "phone",
            "smartphone",
            "vehicle",
            "car",
            "steering",
            "fuel",
            "gas",
            "autopark",
            "auto_park",
            "park",
            "sell",
            "rent",
            "rented",
            "lease",
            "owned",
            "owner",
            "help",
            "bug",
            "option",
            "building",
            "business",
            "location"
        };
        private static readonly string[] HiddenUiMarkerKeywords =
        {
            "icon",
            "marker",
            "mapicon",
            "worldicon",
            "circle",
            "steering",
            "rent",
            "rented",
            "lease",
            "owned",
            "owner"
        };
        private static readonly string[] HiddenUiExcludeKeywords =
        {
            "menu",
            "dialog",
            "popup",
            "modal",
            "tooltip",
            "toast",
            "sleep",
            "bed",
            "timeskip",
            "time_skip",
            "skiptime",
            "fastforward",
            "fast_forward",
            "cancel",
            "dropdown",
            "scroll",
            "list",
            "phonecall",
            "notificationstack",
            "notificationitem"
        };

        private Camera? activeMapRenderCamera;
        private readonly CameraToolsUpdateNoticeUi updateNoticeUi = new CameraToolsUpdateNoticeUi();
        private CameraState activeMapRenderCameraState;
        private Transform? activeMapVcamTransform;
        private Component? activeVehicleCameraRoot;
        private Component[]? cachedVehicleCameras;
        private readonly Dictionary<int, Vector3> cachedVehicleFollowOffsets = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, object[]> cachedVehiclePipelineComponents = new Dictionary<int, object[]>();
        private readonly Dictionary<int, object[]> cachedVehicleZoomComponents = new Dictionary<int, object[]>();
        private MonoBehaviour? configuredGameplayController;
        private MonoBehaviour? configuredMapController;
        private ModContext? context;
        private float desiredVehicleDistance;
        private float displayedVehicleDistance;
        private float vehicleZoomVelocity;
        private Coroutine? vehicleZoomCoroutine;
        private Component? bumperFovCamera;
        private Component? bumperFovUnavailableCamera;
        private object? originalBumperLens;
        private int lastVehiclePoseFrame = -1;
        private bool wasVehicleBumperViewActive;
        private bool bumperPoseFailureLogged;
        private Transform? cachedBumperVehicle;
        private Component? cachedBumperCameraRoot;
        private bool cachedBumperReverse;
        private Vector3 cachedBumperLocalPosition;
        private MonoBehaviour? gameManagerController;
        private MonoBehaviour? gameplayController;
        private bool hasInitializedMapDistanceForCurrentOpen;
        private bool hasManualGameplayPitch;
        private bool hasManualMapPitch;
        private bool hasManualVehiclePitch;
        private bool hasManualVehicleYaw;
        private bool isVehicleRuntimeSuspendedForMap;
        private bool vehicleRuntimeHadCameraBeforeMap;
        private string? originalForcedIndoorWallsVisibility;
        private string? forcedIndoorWallsVisibility;
        private bool hasShownGameplayPitchHint;
        private bool isUiHidden;
        private bool isGameplayUiBlocked;
        private bool isTrackingMapRightMousePitch;
        private bool isTrackingRightMousePitch;
        private bool isGameplayRightMousePending;
        private bool isTrackingVehicleRightMousePitch;
        private int lastActiveVehicleCameraId;
        private int lastConfiguredGameplayControllerId;
        private int lastConfiguredMapControllerId;
        private int lastAppliedGameplayMaxZoom;
        private int lastAppliedMapMaxZoom;
        private float lastAppliedMapDistanceSetting;
        private float desiredMapDistance;
        private float lastAppliedVehicleMaxZoom;
        private float lastRightMouseY;
        private Vector2 gameplayRightMousePressPosition;
        private float lastVehicleRightMouseX;
        private float lastVehicleRightMouseY;
        private float lastVehicleControllerSearchTime;
        private MonoBehaviour? mapController;
        private float manualMapPitch;
        private float manualGameplayPitch;
        private float manualVehiclePitch;
        private float manualVehicleYaw;
        private bool needsVehicleDistanceReapply;
        private bool timeMachineActive;
        private float nextUiStateRefreshTime;
        private float nextHiddenUiRefreshTime;
        private float nextWorldTextOverlaySearchTime;
        private PendingVcamDiagnostic? pendingVcamDiagnostic;
        private RectTransform? producerOverlayRect;
        private RectTransform? worldTextSectionRect;
        private bool scenicViewEnabled;
        private RendererState[] scenicViewRendererStates = Array.Empty<RendererState>();
        private Component? scenicViewTargetRoot;
        private GameObjectActiveState[] hiddenUiStates = Array.Empty<GameObjectActiveState>();
        private CameraToolsSettings? settings;
        private bool showVehicleDebugOverlay;
        private bool wasCityMapOpen;
        private Vector3? lastAppliedGameplayOffset;
        private float mapDebugCurrentDistance;
        private float mapDebugDesiredDistance;
        private float mapDebugRawScrollDelta;
        private float mapDebugVanillaDelta;
        private string lastMapApplyLogSummary = string.Empty;
        private string lastMapLifecycleLogSummary = string.Empty;
        private string lastMapDebugLogSummary = string.Empty;
        private string lastIndoorCameraLogSignature = string.Empty;
        private VehicleDebugState vehicleDebug = new VehicleDebugState();
        private VehicleTarget? vehicleTarget;
        private MonoBehaviour? cachedDialogUiController;
        private static Type? cameraMouseDragType;
        private static Type? carControllerType;
        private static Type? cityMapType;
        private static Type? cityMapCamType;
        private static Type? cinemachineBrainType;
        private static Type? cinematachineVirtualCameraType;
        private static Type? dialogUiType;
        private static Type? fullMenuType;
        private static Type? gameManagerType;
        private static Type? interiorDesignerUiType;
        private static Type? miniMenuType;
        private static Type? optionsUiType;
        private static Type? pedestrianCamType;
        private static Type? placementSystemType;
        private static Type? saveGameManagerType;
        private static Type? vehicleControllerType;
        private static Type? wallsVisibilityHelperType;
        private static Type? wallsVisibilityType;
        private static readonly Dictionary<string, FieldInfo?> fieldCache = new Dictionary<string, FieldInfo?>();
        private static readonly Dictionary<string, PropertyInfo?> propertyCache = new Dictionary<string, PropertyInfo?>();
        private static readonly object memberCacheLock = new object();
        private static bool cameraToolsDebugEnabled;
        private static bool vehicleDebugLoggingEnabled;

        public static CameraToolsRuntime Initialize(ModContext context, CameraToolsSettings settings)
        {
            var runtime = FindObjectOfType<CameraToolsRuntime>();
            if (runtime == null)
            {
                var runtimeObject = new GameObject(nameof(CameraToolsRuntime));
                DontDestroyOnLoad(runtimeObject);
                runtime = runtimeObject.AddComponent<CameraToolsRuntime>();
            }

            runtime.RestoreCityFogState();
            runtime.StopVehicleZoomAnimation();
            runtime.RestoreBumperFieldOfView();
            runtime.RestoreForcedIndoorWallsVisibility();
            runtime.RestoreTrackedMemberStates();
            runtime.ExitCameraModes();
            runtime.context = context;
            runtime.settings = settings;
            runtime.updateNoticeUi.Initialize(context, settings);
            runtime.activeVehicleCameraRoot = null;
            runtime.cachedVehicleCameras = null;
            runtime.cachedVehicleFollowOffsets.Clear();
            runtime.cachedVehiclePipelineComponents.Clear();
            runtime.cachedVehicleZoomComponents.Clear();
            runtime.configuredGameplayController = null;
            runtime.configuredMapController = null;
            runtime.desiredVehicleDistance = float.NaN;
            runtime.displayedVehicleDistance = float.NaN;
            runtime.vehicleZoomVelocity = 0f;
            runtime.lastVehiclePoseFrame = -1;
            runtime.wasVehicleBumperViewActive = false;
            runtime.bumperPoseFailureLogged = false;
            runtime.cachedBumperVehicle = null;
            runtime.cachedBumperCameraRoot = null;
            runtime.gameManagerController = null;
            runtime.gameplayController = null;
            runtime.hasInitializedMapDistanceForCurrentOpen = false;
            runtime.hasManualGameplayPitch = false;
            runtime.wasNearEyeAlignmentActive = false;
            runtime.hasManualMapPitch = false;
            runtime.hasManualVehiclePitch = false;
            runtime.hasManualVehicleYaw = false;
            runtime.isVehicleRuntimeSuspendedForMap = false;
            runtime.vehicleRuntimeHadCameraBeforeMap = false;
            runtime.originalForcedIndoorWallsVisibility = null;
            runtime.forcedIndoorWallsVisibility = null;
            runtime.hasShownGameplayPitchHint = false;
            runtime.isUiHidden = false;
            runtime.isTrackingMapRightMousePitch = false;
            runtime.isTrackingRightMousePitch = false;
            runtime.isGameplayRightMousePending = false;
            runtime.isTrackingVehicleRightMousePitch = false;
            runtime.lastActiveVehicleCameraId = 0;
            runtime.lastAppliedGameplayMaxZoom = int.MinValue;
            runtime.lastAppliedMapMaxZoom = int.MinValue;
            runtime.lastAppliedMapDistanceSetting = float.NaN;
            runtime.desiredMapDistance = float.NaN;
            runtime.lastAppliedVehicleMaxZoom = float.NaN;
            runtime.lastConfiguredGameplayControllerId = 0;
            runtime.lastConfiguredMapControllerId = 0;
            runtime.lastAppliedGameplayOffset = null;
            runtime.lastRightMouseY = 0f;
            runtime.gameplayRightMousePressPosition = Vector2.zero;
            runtime.lastVehicleRightMouseX = 0f;
            runtime.lastVehicleRightMouseY = 0f;
            runtime.lastVehicleControllerSearchTime = float.NegativeInfinity;
            runtime.mapController = null;
            runtime.manualVehiclePitch = 0f;
            runtime.manualVehicleYaw = 0f;
            runtime.needsVehicleDistanceReapply = false;
            runtime.timeMachineActive = false;
            runtime.nextUiStateRefreshTime = 0f;
            runtime.nextHiddenUiRefreshTime = 0f;
            runtime.nextWorldTextOverlaySearchTime = 0f;
            runtime.pendingVcamDiagnostic = null;
            runtime.producerOverlayRect = null;
            runtime.worldTextSectionRect = null;
            runtime.scenicViewEnabled = false;
            runtime.scenicViewRendererStates = Array.Empty<RendererState>();
            runtime.scenicViewTargetRoot = null;
            runtime.hiddenUiStates = Array.Empty<GameObjectActiveState>();
            runtime.showVehicleDebugOverlay = settings.EnableCameraToolsDebug && settings.EnableVehicleDebugOverlay;
            runtime.vehicleTarget = null;
            runtime.vehicleDebug = new VehicleDebugState();
            runtime.wasCityMapOpen = false;
            runtime.cachedDialogUiController = null;
            runtime.isGameplayUiBlocked = false;
            runtime.lastMapApplyLogSummary = string.Empty;
            runtime.lastMapLifecycleLogSummary = string.Empty;
            runtime.lastMapDebugLogSummary = string.Empty;
            runtime.lastIndoorCameraLogSignature = string.Empty;
            cameraToolsDebugEnabled = settings.EnableCameraToolsDebug;
            vehicleDebugLoggingEnabled = settings.EnableCameraToolsDebug && settings.EnableVehicleDebugLogging;

            pedestrianCamType ??= FindType(PedestrianCamTypeName);
            cityMapType ??= FindType("CityMap");
            cityMapCamType ??= FindType(CityMapCamTypeName);
            carControllerType ??= FindType(CarControllerTypeName);
            vehicleControllerType ??= FindType(VehicleControllerTypeName);
            cameraMouseDragType ??= FindType(CameraMouseDragTypeName);
            cinemachineBrainType ??= FindType("CinemachineBrain");
            cinematachineVirtualCameraType ??= FindType("CinemachineVirtualCamera");
            miniMenuType ??= FindType("UI.MiniMenu.MiniMenu");
            fullMenuType ??= FindType("UI.Smartphone.FullMenu");
            optionsUiType ??= FindType("Scenes.MainMenu.Options");
            dialogUiType ??= FindType("UI.Dialog.DialogUI");
            gameManagerType ??= FindType(GameManagerTypeName);
            interiorDesignerUiType ??= FindType("UI.InteriorDesigner.InteriorDesignerUI");
            placementSystemType ??= FindType("BigAmbitions.PlacementSystem.PlacementSystem");
            saveGameManagerType ??= FindType(SaveGameManagerTypeName);
            wallsVisibilityHelperType ??= FindType("Buildings.Indoors.WallsVisibilityHelper");
            wallsVisibilityType ??= FindType("BigAmbitions.InteriorDesigner.WallsVisibility");

            runtime.RegisterTimeMachineHooks();
            LogVehicleDebug("CameraTools runtime initialized.");
            return runtime;
        }

        public void Shutdown()
        {
            StopVehicleZoomAnimation();
            RestoreBumperFieldOfView();
            ExitCameraModes();
            updateNoticeUi.Shutdown();
            RestoreCityFogState();
            RestoreForcedIndoorWallsVisibility();
            RestoreTrackedMemberStates();
            RestoreScenicView();
            RestoreHiddenUi();
            RestoreMapCameraState();
            LogVehicleDebug("CameraTools runtime shutting down.");
            Destroy(gameObject);
        }

        private void OnEnable()
        {
            RegisterTimeMachineHooks();
            GlobalEvents.RegisterOnGameLoadedLateCallback(RegisterTimeMachineHooks);
            Camera.onPreCull += HandleCameraPreCull;
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
            CinemachineCore.CameraUpdatedEvent.AddListener(HandleBrainCameraUpdated);
            SceneManager.sceneLoaded += HandleCityFogSceneLoaded;
        }

        private void OnDisable()
        {
            GlobalEvents.onTimeMachineStarted -= HandleTimeMachineStarted;
            GlobalEvents.onTimeMachineEnded -= HandleTimeMachineEnded;
            timeMachineActive = false;
            StopFirstPersonPoiDistanceFilter();
            StopVehicleZoomAnimation();
            RestoreBumperFieldOfView();
            RestoreFirstPersonMouseCapture();
            Camera.onPreCull -= HandleCameraPreCull;
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            CinemachineCore.CameraUpdatedEvent.RemoveListener(HandleBrainCameraUpdated);
            SceneManager.sceneLoaded -= HandleCityFogSceneLoaded;
            RestoreCityFogState();
            RestoreForcedIndoorWallsVisibility();
            RestoreTrackedMemberStates();
        }

        private void RegisterTimeMachineHooks()
        {
            if (this == null || !isActiveAndEnabled)
                return;

            GlobalEvents.onTimeMachineStarted -= HandleTimeMachineStarted;
            GlobalEvents.onTimeMachineStarted += HandleTimeMachineStarted;
            GlobalEvents.onTimeMachineEnded -= HandleTimeMachineEnded;
            GlobalEvents.onTimeMachineEnded += HandleTimeMachineEnded;

            if (InstanceBehavior<UIs>.Instance?.timeMachine?.isRunning == true)
                HandleTimeMachineStarted();
            else if (timeMachineActive)
                HandleTimeMachineEnded();
        }

        private void HandleTimeMachineStarted()
        {
            if (timeMachineActive)
                return;

            timeMachineActive = true;
            RestoreFirstPersonMouseCapture();
            updateNoticeUi.SetSuppressedByNativeUi(true, IsFreeCameraActive());
            LogTimeMachineDebug($"Camera Tools suspended for time machine: firstPerson={firstPersonViewActive}, uiHidden={isUiHidden}, vehicle={activeVehicleCameraRoot != null}.");
        }

        private void HandleTimeMachineEnded()
        {
            if (!timeMachineActive)
                return;

            timeMachineActive = false;
            if (isUiHidden && IsGameplayActive())
            {
                pendingHiddenUiRefreshFrames = HiddenUiRefreshBurstFrames;
                ApplyHiddenUi();
            }

            LogTimeMachineDebug($"Camera Tools resumed after time machine: firstPerson={firstPersonViewActive}, uiHidden={isUiHidden}.");
        }

        private void LogTimeMachineDebug(string message)
        {
            if (settings?.EnableCameraToolsDebug == true && settings.EnableTimeMachineDebugLogging)
                context?.Logger.Info($"CameraTools: {message}");
        }

        private void LateUpdate()
        {
            if (settings == null || timeMachineActive)
                return;

            cameraToolsDebugEnabled = settings.EnableCameraToolsDebug;
            vehicleDebugLoggingEnabled = settings.EnableCameraToolsDebug && settings.EnableVehicleDebugLogging;
            if (!cameraToolsDebugEnabled)
                showVehicleDebugOverlay = false;

            var cityMapOpen = IsCityMapOpen();
            EnsureControllers(cityMapOpen);
            UpdateCameraModes(cityMapOpen);
            updateNoticeUi.SetSuppressedByNativeUi(IsUpdateNoticeSuppressedByNativeUi(cityMapOpen), IsFreeCameraActive());
            HandleScenicViewHotkey();
            RefreshScenicViewState();
            HandleHideUiHotkey();
            RefreshHiddenUiState();
            KeepWorldTextOverlayOnScreen(cityMapOpen);
            HandleVehicleDebugHotkeys();
            var gameplayActive = IsGameplayActive();
            HandleIndoorCameraDebugHotkey(cityMapOpen, gameplayActive);
            UpdateIndoorWallsVisibility(cityMapOpen, gameplayActive);
            if (!cityMapOpen && !IsFreeCameraActive())
                ApplyGameplayTweaks();
            if (!cityMapOpen && !IsFreeCameraActive())
            {
                ApplyVehicleTweaks(gameplayActive);
            }
            else
                SuspendVehicleRuntimeStateForMap();
            ApplyMapTweaks(cityMapOpen);
            UpdateFirstPersonZoom(cityMapOpen);
            UpdateCityFogSuppression();
            if (cameraToolsDebugEnabled)
                ProcessPendingVcamDiagnostic();
        }

        private void OnGUI()
        {
            if (timeMachineActive)
                return;

            // IMGUI can process a click after LateUpdate's cached UI state. Never let
            // our update window receive an Options-menu event in that transition.
            if (IsOptionsMenuOpen())
                return;

            updateNoticeUi.OnGui();
        }

        private void EnsureControllers(bool cityMapOpen)
        {
            if (!cityMapOpen)
            {
                var liveGameplayController = ResolveLiveGameplayController();
                if (liveGameplayController != null && gameplayController != liveGameplayController)
                {
                    gameplayController = liveGameplayController;
                    if (gameplayController != configuredGameplayController)
                        configuredGameplayController = null;
                }
            }

            if (gameplayController == null || !gameplayController.isActiveAndEnabled)
            {
                gameplayController = FindFirstActiveController(pedestrianCamType, includeInactive: false);
                if (gameplayController != configuredGameplayController)
                    configuredGameplayController = null;
            }

            if (gameplayController != null && gameplayController != configuredGameplayController)
            {
                lastConfiguredGameplayControllerId = 0;
                lastAppliedGameplayOffset = null;
                ConfigureGameplayController();
                configuredGameplayController = gameplayController;
            }

            if (cityMapOpen && (mapController == null || !mapController.isActiveAndEnabled))
            {
                mapController = FindFirstActiveController(cityMapCamType, includeInactive: false) ??
                    FindFirstActiveController(cityMapCamType, includeInactive: true);
                if (mapController != configuredMapController)
                    configuredMapController = null;
            }

            if (mapController != null && mapController != configuredMapController)
            {
                lastConfiguredMapControllerId = 0;
                ConfigureMapController();
                configuredMapController = mapController;
            }

            if (gameManagerController == null || !gameManagerController.isActiveAndEnabled)
            {
                gameManagerController = FindFirstActiveController(gameManagerType, includeInactive: false) ??
                    FindFirstActiveController(gameManagerType, includeInactive: true);
                InvalidateVehicleCameraCaches();
            }
        }

        private MonoBehaviour? ResolveLiveGameplayController()
        {
            var liveVirtualCamera = GetLiveVirtualCameraComponent();
            if (liveVirtualCamera == null || pedestrianCamType == null)
                return null;

            return liveVirtualCamera.GetComponent(pedestrianCamType) as MonoBehaviour;
        }

        private static MonoBehaviour? FindFirstActiveController(Type? type, bool includeInactive)
        {
            if (type == null)
                return null;

            if (!includeInactive)
            {
                foreach (var obj in Object.FindObjectsOfType(type))
                {
                    var behaviour = obj as MonoBehaviour;
                    if (behaviour != null)
                        return behaviour;
                }

                return null;
            }

            foreach (var obj in Resources.FindObjectsOfTypeAll(type))
            {
                var behaviour = obj as MonoBehaviour;
                if (behaviour == null || behaviour.gameObject == null || behaviour.gameObject.hideFlags != HideFlags.None)
                    continue;

                return behaviour;
            }

            return null;
        }








































































        private static void LogVehicleDebug(string message)
        {
            if (!vehicleDebugLoggingEnabled)
                return;

            CameraToolsFileLogger.Log(message);
        }

        private static void LogIndoorCameraDebug(string message)
        {
            CameraToolsFileLogger.Log("indoor-camera-debug.log", "cameratools-indoor-camera-debug.log", message);
        }

        private static void LogJobBoardUiDebug(string message)
        {
            CameraToolsFileLogger.Log("jobboard-ui-debug.log", "cameratools-jobboard-ui-debug.log", message);
        }

        private static void LogGameplayZoomDebug(string message)
        {
            CameraToolsFileLogger.Log("gameplay-zoom-debug.log", "cameratools-gameplay-zoom-debug.log", message);
        }

        private static Array? GetCinemachinePipeline(Type virtualCameraType, object virtualCamera)
        {
            var getPipelineMethod = virtualCameraType.GetMethod("GetComponentPipeline", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return getPipelineMethod?.Invoke(virtualCamera, null) as Array;
        }

        private static Type? FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Exact lookup avoids enumerating most assemblies and works even when an
                // unrelated assembly cannot expose all of its types on the game's runtime.
                var exactType = assembly.GetType(typeName, false);
                if (exactType != null)
                    return exactType;

                Type[]? types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    // Big Ambitions 1.0's runtime does not expose
                    // ReflectionTypeLoadException.Types. Skipping a partially loadable
                    // assembly is safe because the remaining game assemblies are searched.
                    continue;
                }

                if (types == null)
                    continue;

                foreach (var type in types)
                {
                    if (type == null)
                        continue;

                    if (TypeNameMatches(type, typeName))
                        return type;
                }
            }

            return null;
        }

        private static bool TypeNameMatches(Type type, string typeName)
        {
            if (string.Equals(type.FullName, typeName, StringComparison.Ordinal) ||
                string.Equals(type.Name, typeName, StringComparison.Ordinal))
                return true;

            return type.FullName != null &&
                type.FullName.EndsWith("." + typeName, StringComparison.Ordinal);
        }

        private static bool IsCityMapOpen()
        {
            var cityMapType = CameraToolsRuntime.cityMapType;
            if (cityMapType == null)
                return false;

            var isOpenProperty = cityMapType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static);
            return isOpenProperty?.GetValue(null) as bool? ?? false;
        }

        private void HandleIndoorCameraDebugHotkey(bool cityMapOpen, bool gameplayActive)
        {
            if (settings == null)
                return;

            if (settings.EnableIndoorCameraDebugLogging && !cityMapOpen && gameplayActive)
                MaybeLogIndoorCameraState();

            if (Input.GetKeyDown(KeyCode.F4))
            {
                if (settings.EnableGameplayZoomDebugLogging)
                    DumpGameplayZoomDiagnostics("manual");
                else if (settings.EnableJobBoardUiDebugLogging)
                    DumpJobBoardUiDiagnostics("manual");
                else if (settings.EnableIndoorCameraDebugLogging)
                    DumpIndoorCameraDiagnostics("manual");
            }
        }

        private void MaybeLogIndoorCameraState()
        {
            var liveVirtualCamera = GetLiveVirtualCameraComponent();
            if (liveVirtualCamera == null)
                return;

            var path = GetHierarchyPath(liveVirtualCamera.transform);
            if (IsVehicleLikeCameraPath(path))
                return;

            var signature =
                path + "|" +
                liveVirtualCamera.GetType().FullName + "|" +
                (gameplayController == null ? "none" : GetHierarchyPath(gameplayController.transform));
            if (string.Equals(signature, lastIndoorCameraLogSignature, StringComparison.Ordinal))
                return;

            lastIndoorCameraLogSignature = signature;
            DumpIndoorCameraDiagnostics("active-camera-changed");
        }

        private void DumpIndoorCameraDiagnostics(string reason)
        {
            if (settings == null || !settings.EnableIndoorCameraDebugLogging)
                return;

            var liveVirtualCamera = GetLiveVirtualCameraComponent();
            LogIndoorCameraDebug("=== Indoor camera diagnostics start ===");
            LogIndoorCameraDebug($"reason={reason}, gameplayActive={IsGameplayActive()}, cityMapOpen={IsCityMapOpen()}, liveVcam={(liveVirtualCamera == null ? "none" : GetHierarchyPath(liveVirtualCamera.transform))}");
            LogIndoorCameraDebug($"gameplayPitch={GetCurrentGameplayPitchForLogging():0.##}, hasManualGameplayPitch={hasManualGameplayPitch}");
            LogIndoorCameraDebug($"wallsVisibility={GetCurrentWallsVisibilityName()}, forcedMode={forcedIndoorWallsVisibility ?? "none"}, firstPerson={firstPersonViewActive}, thresholdPitch={IndoorWallsPartlyHiddenPitchThreshold:0.##}");
            LogIndoorCameraDebug($"wallsHelperType={(wallsVisibilityHelperType == null ? "null" : wallsVisibilityHelperType.FullName)}, wallsEnumType={(wallsVisibilityType == null ? "null" : wallsVisibilityType.FullName)}");

            if (gameplayController != null)
            {
                LogIndoorCameraDebug(
                    $"gameplayController: type={gameplayController.GetType().FullName}, path={GetHierarchyPath(gameplayController.transform)}, enabled={gameplayController.isActiveAndEnabled}");
                foreach (var member in EnumerateInterestingMembers(gameplayController, GameplayCameraKeywords))
                {
                    LogIndoorCameraDebug(
                        $"  Gameplay member: {member.DeclaringType}.{member.Name} type={member.MemberType.Name} writable={member.Writable} value={FormatMemberValue(member.Value)}");
                }
            }
            else
            {
                LogIndoorCameraDebug("gameplayController: none");
            }

            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                var liveDistance = TryGetCameraDistanceToFollowTarget(liveVirtualCamera, mainCamera, out var distanceToFollow)
                    ? distanceToFollow.ToString("0.##")
                    : "n/a";
                LogIndoorCameraDebug(
                    $"Camera.main: path={GetHierarchyPath(mainCamera.transform)}, enabled={mainCamera.enabled}, fov={mainCamera.fieldOfView:0.##}, " +
                    $"clearFlags={mainCamera.clearFlags}, background={mainCamera.backgroundColor}, cullingMask={FormatLayerMask(mainCamera.cullingMask)}, " +
                    $"skyboxEnabled={FormatSkyboxEnabled(mainCamera)}, distanceToFollow={liveDistance}");
            }
            else
            {
                LogIndoorCameraDebug("Camera.main: none");
            }

            foreach (var camera in Camera.allCameras)
            {
                if (camera == null || !camera.enabled)
                    continue;

                LogIndoorCameraDebug(
                    $"Enabled camera: path={GetHierarchyPath(camera.transform)}, depth={camera.depth:0.##}, clearFlags={camera.clearFlags}, " +
                    $"background={camera.backgroundColor}, cullingMask={FormatLayerMask(camera.cullingMask)}, skyboxEnabled={FormatSkyboxEnabled(camera)}");
            }

            if (liveVirtualCamera == null)
            {
                LogIndoorCameraDebug("No live Cinemachine virtual camera was resolved.");
                LogIndoorCameraDebug("=== Indoor camera diagnostics end ===");
                return;
            }

            LogIndoorVirtualCameraDetails(liveVirtualCamera);
            LogIndoorCameraDebug("=== Indoor camera diagnostics end ===");
        }

        private void LogIndoorVirtualCameraDetails(Component liveVirtualCamera)
        {
            var follow = TryGetMemberValue(liveVirtualCamera, "Follow", out var followValue) ? followValue : null;
            var lookAt = TryGetMemberValue(liveVirtualCamera, "LookAt", out var lookAtValue) ? lookAtValue : null;
            var priority = TryGetMemberValue(liveVirtualCamera, "Priority", out var priorityValue) ? priorityValue : null;
            LogIndoorCameraDebug(
                $"Live VCAM: type={liveVirtualCamera.GetType().FullName}, path={GetHierarchyPath(liveVirtualCamera.transform)}, enabled={FormatEnabled(liveVirtualCamera)}, " +
                $"priority={FormatMemberValue(priority)}, follow={FormatMemberValue(follow)}, lookAt={FormatMemberValue(lookAt)}");

            foreach (var sameGoComponent in liveVirtualCamera.gameObject.GetComponents<Component>())
            {
                if (sameGoComponent == null)
                    continue;

                LogIndoorCameraDebug($"SameGO component: {sameGoComponent.GetType().FullName}");
            }

            var virtualCameraType = cinematachineVirtualCameraType;
            if (virtualCameraType == null)
                return;

            var pipeline = GetCinemachinePipeline(virtualCameraType, liveVirtualCamera);
            if (pipeline == null)
            {
                LogIndoorCameraDebug("Pipeline: none");
                return;
            }

            foreach (var pipelineComponent in pipeline)
            {
                if (pipelineComponent == null)
                    continue;

                LogIndoorCameraDebug($"Pipeline component: {pipelineComponent.GetType().FullName}");
                foreach (var member in EnumerateInterestingMembers(pipelineComponent, VehicleCameraKeywords))
                {
                    LogIndoorCameraDebug(
                        $"  {member.DeclaringType}.{member.Name} type={member.MemberType.Name} writable={member.Writable} value={FormatMemberValue(member.Value)}");
                }
            }
        }

        private static bool IsVehicleLikeCameraPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return path.IndexOf("VehicleCam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("IndoorVehicle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsIndoorGameplayCamera(Component? liveVirtualCamera)
        {
            if (liveVirtualCamera == null)
                return false;

            var path = GetHierarchyPath(liveVirtualCamera.transform);
            return path.IndexOf("IndoorCam", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatSkyboxEnabled(Camera camera)
        {
            // Unity 6 no longer exposes the legacy Skybox component through
            // UnityEngine.CoreModule. Keep this diagnostic compatibility-safe.
            return "unavailable";
        }

        private static bool TryGetCameraDistanceToFollowTarget(Component? liveVirtualCamera, Camera mainCamera, out float distance)
        {
            distance = 0f;
            if (liveVirtualCamera == null)
                return false;

            if (!TryGetMemberValue(liveVirtualCamera, "Follow", out var followValue) || followValue is not Transform followTarget)
                return false;

            distance = Vector3.Distance(mainCamera.transform.position, followTarget.position);
            return true;
        }

        private static string FormatLayerMask(int cullingMask)
        {
            if (cullingMask == 0)
                return "0";

            var names = new List<string>();
            for (var layer = 0; layer < 32; layer++)
            {
                if ((cullingMask & (1 << layer)) == 0)
                    continue;

                var layerName = LayerMask.LayerToName(layer);
                names.Add(string.IsNullOrWhiteSpace(layerName) ? layer.ToString() : layerName);
            }

            return string.Join(",", names);
        }






















        private static object? SafeRead(Func<object?> reader)
        {
            try
            {
                return reader();
            }
            catch (Exception exception)
            {
                return "<read failed: " + exception.GetType().Name + ">";
            }
        }

        private static string FormatMemberValue(object? value)
        {
            if (value == null)
                return "null";

            if (value is Component component)
                return GetHierarchyPath(component.transform);

            if (value is GameObject gameObject)
                return GetHierarchyPath(gameObject.transform);

            return value.ToString() ?? value.GetType().Name;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new List<string>();
            var current = transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static string FormatEnabled(Component component)
        {
            return component is Behaviour behaviour ? behaviour.enabled.ToString() : "n/a";
        }






        private static Transform? GetTransformMember(object target, string memberName)
        {
            if (!TryGetMemberValue(target, memberName, out var value))
                return null;

            return value as Transform;
        }

        private static Vector2 GetVector2Member(object target, string memberName)
        {
            if (!TryGetMemberValue(target, memberName, out var value) || value is not Vector2 vector)
                return default;

            return vector;
        }

        private static float GetFloatMember(object target, string memberName)
        {
            return TryGetFloatMember(target, memberName, out var value) ? value : 0f;
        }

        private static bool TryGetFloatMember(object target, string memberName, out float value)
        {
            value = 0f;
            if (!TryGetMemberValue(target, memberName, out var memberValue) || memberValue == null)
                return false;

            switch (memberValue)
            {
                case float floatValue:
                    value = floatValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetBoolMember(object target, string memberName, out bool value)
        {
            value = false;
            if (!TryGetMemberValue(target, memberName, out var memberValue) || memberValue == null || memberValue is not bool boolValue)
                return false;

            value = boolValue;
            return true;
        }

        private static bool TryGetFirstFloatMember(object target, string[] memberNames, out float value, out string foundMemberName)
        {
            foreach (var memberName in memberNames)
            {
                if (!TryGetFloatMember(target, memberName, out value))
                    continue;

                foundMemberName = memberName;
                return true;
            }

            value = 0f;
            foundMemberName = "none";
            return false;
        }

        private static bool SetFirstFloatMember(object target, string[] memberNames, float value)
        {
            foreach (var memberName in memberNames)
            {
                if (SetMemberValue(target, memberName, value))
                    return true;
            }

            return false;
        }

        private static bool TryGetMemberValue(object target, string memberName, out object? value)
        {
            value = null;
            if (target == null)
                return false;

            try
            {
                var type = target.GetType();
                var field = GetCachedField(type, memberName);
                if (field != null)
                {
                    value = field.GetValue(target);
                    return true;
                }

                var property = GetCachedProperty(type, memberName);
                if (property == null || !property.CanRead)
                    return false;

                value = property.GetValue(target, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetMemberValue(object target, string memberName, object value)
        {
            if (target == null)
                return false;

            try
            {
                var type = target.GetType();
                var field = GetCachedField(type, memberName);
                if (field != null)
                {
                    field.SetValue(target, ConvertValue(value, field.FieldType));
                    return true;
                }

                var property = GetCachedProperty(type, memberName);
                if (property == null || !property.CanWrite)
                    return false;

                property.SetValue(target, ConvertValue(value, property.PropertyType), null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static FieldInfo? GetCachedField(Type type, string memberName)
        {
            var key = type.AssemblyQualifiedName + "|F|" + memberName;
            lock (memberCacheLock)
            {
                if (fieldCache.TryGetValue(key, out var field))
                    return field;

                field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                fieldCache[key] = field;
                return field;
            }
        }

        private static PropertyInfo? GetCachedProperty(Type type, string memberName)
        {
            var key = type.AssemblyQualifiedName + "|P|" + memberName;
            lock (memberCacheLock)
            {
                if (propertyCache.TryGetValue(key, out var property))
                    return property;

                property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                propertyCache[key] = property;
                return property;
            }
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (targetType.IsInstanceOfType(value))
                return value;

            if (targetType == typeof(float))
                return Convert.ToSingle(value);

            if (targetType == typeof(int))
                return Convert.ToInt32(value);

            if (targetType == typeof(bool))
                return Convert.ToBoolean(value);

            return value;
        }

        private readonly struct CameraState
        {
            private readonly Color backgroundColor;
            private readonly CameraClearFlags clearFlags;
            private readonly float fieldOfView;
            private readonly bool orthographic;
            private readonly float orthographicSize;
            private readonly Vector3 position;
            private readonly Quaternion rotation;

            public CameraState(Camera camera)
            {
                backgroundColor = camera.backgroundColor;
                clearFlags = camera.clearFlags;
                orthographic = camera.orthographic;
                orthographicSize = camera.orthographicSize;
                fieldOfView = camera.fieldOfView;
                position = camera.transform.position;
                rotation = camera.transform.rotation;
            }

            public void Restore(Camera camera)
            {
                camera.transform.position = position;
                camera.transform.rotation = rotation;
                camera.backgroundColor = backgroundColor;
                camera.clearFlags = clearFlags;
                camera.orthographic = orthographic;
                camera.orthographicSize = orthographicSize;
                camera.fieldOfView = fieldOfView;
            }
        }

        private readonly struct RendererState
        {
            public RendererState(Renderer renderer, bool wasEnabled)
            {
                Renderer = renderer;
                WasEnabled = wasEnabled;
            }

            public Renderer Renderer { get; }

            public bool WasEnabled { get; }
        }

        private readonly struct GameObjectActiveState
        {
            public GameObjectActiveState(GameObject target, bool wasActive)
            {
                Target = target;
                WasActive = wasActive;
            }

            public GameObject Target { get; }

            public bool WasActive { get; }
        }

        private sealed class VehicleTarget
        {
            public VehicleTarget(MonoBehaviour? carController, object? vehicleController)
            {
                CarController = carController;
                VehicleController = vehicleController;
            }

            public MonoBehaviour? CarController { get; }

            public object? VehicleController { get; }
        }

        private sealed class VehicleDebugState
        {
            public bool ActiveCarFound;
            public float ActualDistance;
            public bool CameraObjectFound;
            public string CarControllerTypeName = "none";
            public bool CinemachineFound;
            public float CurrentDistance;
            public string CurrentFollowOffset = "n/a";
            public string DistanceMemberName = "none";
            public bool IsInsideVehicle;
            public bool IsVehicleMode;
            public float LastAppliedDistance;
            public string LastApplySummary = string.Empty;
            public string LastOverwriteSummary = string.Empty;
            public string OriginalFollowOffset = "n/a";
            public string LastResolutionSummary = string.Empty;
            public float ScrollDelta;
            public bool VehicleControllerFound;
            public string VehicleControllerTypeName = "none";
            public bool WasOverwritten;
        }

        private readonly struct InterestingMember
        {
            public InterestingMember(string declaringType, string name, Type memberType, bool writable, object? value)
            {
                DeclaringType = declaringType;
                Name = name;
                MemberType = memberType;
                Writable = writable;
                Value = value;
            }

            public string DeclaringType { get; }
            public string Name { get; }
            public Type MemberType { get; }
            public object? Value { get; }
            public bool Writable { get; }
        }

        private readonly struct PendingVcamDiagnostic
        {
            public PendingVcamDiagnostic(Component virtualCamera, string summary)
            {
                VirtualCamera = virtualCamera;
                Summary = summary;
            }

            public string Summary { get; }
            public Component VirtualCamera { get; }
        }
    }
}
