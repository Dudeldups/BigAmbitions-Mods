#nullable enable
using System;
using System.Collections;
using System.Reflection;
using BAModAPI;
using BusinessLayoutSets;
using Helpers;
using UI.Notification;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MootorVehicle
{
    public sealed class MootorVehicleRuntime : MonoBehaviour
    {
        private const int InitializationRetryCount = 24;
        private const int RequiredStablePasses = 5;
        private const float InitializationRetryDelay = 0.25f;
        private const string VeterinarianAssemblyName = "MobileVeterinarian";
        private const string MissingVeterinarianNotificationKey =
            "mootorvehicle:missing_mobile_veterinarian";
        private const string MissingVeterinarianNotificationId =
            "MootorVehicleMissingMobileVeterinarian";
        private const string TextMeshProEventManagerTypeName =
            "TMPro.TMPro_EventManager, Unity.TextMeshPro";
        private const string VanillaLegalParkingLocalizationKey =
            "itempanelui_parkingzone_legal";
        private const string SidewalkLayerName = "Sidewalk";
        private const string CrosswalkLayerName = "Crosswalk";
        private const int NpcNavMeshAgentTypeId = 0;
        private const float SidewalkProbeStartHeight = 0.5f;
        private const float SidewalkProbeDistance = 3f;
        private const float SidewalkNavMeshSampleDistance = 1.5f;
        private const float SidewalkHorizontalTolerance = 0.45f;
        private const float SidewalkVerticalTolerance = 0.75f;

        private Coroutine? initializationCoroutine;
        private Coroutine? parkingHudCorrectionCoroutine;
        private ModContext? context;
        private string vehicleTypeName = string.Empty;
        private bool missingVeterinarianNoticeShown;
        private bool correctingParkingHud;
        private object? textChangedEvent;
        private MethodInfo? removeTextChangedHandlerMethod;
        private Action<UnityEngine.Object>? textChangedHandler;
        private FieldInfo? parkingZoneValueField;
        private PropertyInfo? localizationKeyProperty;
        private UnityEngine.Object? parkingTextContainer;
        private UI.ItemPanel.VehicleInfoPanel? activeVehicleInfoPanel;

        public static MootorVehicleRuntime Initialize(ModContext context, string vehicleTypeName)
        {
            var runtime = FindObjectOfType<MootorVehicleRuntime>();
            if (runtime == null)
            {
                var runtimeObject = new GameObject(nameof(MootorVehicleRuntime));
                DontDestroyOnLoad(runtimeObject);
                runtime = runtimeObject.AddComponent<MootorVehicleRuntime>();
            }

            runtime.context = context;
            runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
            runtime.SubscribeGlobalEvents();
            GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
            runtime.ScheduleInitialization();
            return runtime;
        }

        public void Shutdown()
        {
            EndFreeParkingHudOverride();

            if (initializationCoroutine != null)
            {
                StopCoroutine(initializationCoroutine);
                initializationCoroutine = null;
            }

            foreach (var rider in FindObjectsOfType<MootorVehicleRiderController>(true))
                if (rider != null)
                    Destroy(rider);

            foreach (var fuelController in FindObjectsOfType<MootorVehicleFuelController>(true))
                if (fuelController != null)
                    Destroy(fuelController);

            foreach (var materialController in FindObjectsOfType<MootorVehicleMaterialController>(true))
                if (materialController != null)
                    Destroy(materialController);

            foreach (var ambientController in FindObjectsOfType<MootorVehicleAmbientMooController>(true))
                if (ambientController != null)
                    Destroy(ambientController);

            foreach (var impactController in FindObjectsOfType<MootorVehicleImpactController>(true))
                if (impactController != null)
                    Destroy(impactController);

            Destroy(gameObject);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SubscribeGlobalEvents();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnsubscribeGlobalEvents();
            EndFreeParkingHudOverride();
        }

        private void SubscribeGlobalEvents()
        {
            GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
            GlobalEvents.onEnterVehicle += HandleVehicleEntered;
            GlobalEvents.onExitVehicle -= HandleVehicleExited;
            GlobalEvents.onExitVehicle += HandleVehicleExited;
            GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
            GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
            GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
            GlobalEvents.onGameUnloaded += HandleGameUnloaded;
        }

        private void UnsubscribeGlobalEvents()
        {
            GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
            GlobalEvents.onExitVehicle -= HandleVehicleExited;
            GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
            GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SubscribeGlobalEvents();
            ScheduleInitialization();
        }

        private void HandleGameLoadedLate()
        {
            SubscribeGlobalEvents();
            ScheduleInitialization();
        }

        private void HandleGameUnloaded()
        {
            EndFreeParkingHudOverride();

            if (initializationCoroutine == null)
                return;

            StopCoroutine(initializationCoroutine);
            initializationCoroutine = null;
        }

        private void HandleVehicleEntered(VehicleController vehicleController)
        {
            if (!IsMootorVehicle(vehicleController))
            {
                EndFreeParkingHudOverride();
                return;
            }

            TryConfigureVehicle(vehicleController, "vehicle-entered");
            BeginFreeParkingHudOverride();
        }

        private void HandleVehicleExited(VehicleController vehicleController)
        {
            if (!IsMootorVehicle(vehicleController))
                return;

            var isOnSidewalk = IsOnSidewalk(vehicleController);
            EndFreeParkingHudOverride();
            if (isOnSidewalk)
                ApplyFreeParking(vehicleController);
        }

        private void HandleFullMenuToggle(bool isOpen)
        {
            if (!isOpen || BusinessLayoutSetHelper.loadingLayouts)
                return;

            if (!MootorVehicleDealerStock.EnsureVehicleAvailable(vehicleTypeName, context))
                context?.Logger.Warn("Moo-tor Vehicle: dealer catalog was not ready when the full menu opened.");
        }

        private void ScheduleInitialization()
        {
            if (initializationCoroutine != null)
                StopCoroutine(initializationCoroutine);

            initializationCoroutine = StartCoroutine(InitializeForLifecycle());
        }

        private IEnumerator InitializeForLifecycle()
        {
            while (BusinessLayoutSetHelper.loadingLayouts)
            {
                EnsureVehiclesConfigured(out _, out _);
                yield return new WaitForSecondsRealtime(InitializationRetryDelay);
            }

            var previousMatchedCount = -1;
            var stablePasses = 0;
            var dealerReady = false;
            for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
            {
                if (!dealerReady)
                    dealerReady = MootorVehicleDealerStock.EnsureVehicleAvailable(vehicleTypeName, context);
                EnsureVehiclesConfigured(out var matchedCount, out _);

                if (dealerReady && matchedCount == previousMatchedCount)
                    stablePasses++;
                else
                    stablePasses = 0;

                previousMatchedCount = matchedCount;
                if (dealerReady && stablePasses >= RequiredStablePasses)
                    break;

                if (attempt < InitializationRetryCount)
                    yield return new WaitForSecondsRealtime(InitializationRetryDelay);
            }

            initializationCoroutine = null;
            TryShowMissingVeterinarianNotice();
        }

        private void TryShowMissingVeterinarianNotice()
        {
            if (missingVeterinarianNoticeShown || SaveGameManager.Current == null)
                return;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(
                        assembly.GetName().Name,
                        VeterinarianAssemblyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

            try
            {
                Notifications.Show(
                    NotificationType.Info,
                    MissingVeterinarianNotificationKey,
                    null,
                    7f,
                    MissingVeterinarianNotificationId,
                    null,
                    true,
                    false);
                missingVeterinarianNoticeShown = true;
                MootorVehicleDiagnostics.Info(
                    context,
                    "Moo-tor Vehicle: Mobile Veterinarian was not loaded; displayed dependency notice.");
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    "Moo-tor Vehicle: could not display the missing Mobile Veterinarian notice: " +
                    exception.GetBaseException().Message);
            }
        }

        private void EnsureVehiclesConfigured(out int matchedCount, out int configuredCount)
        {
            matchedCount = 0;
            configuredCount = 0;

            var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
            if (allPlayerVehicles == null)
                return;

            foreach (var vehicleController in allPlayerVehicles)
            {
                if (vehicleController?.vehicleInstance == null ||
                    !string.Equals(
                        vehicleController.vehicleInstance.vehicleTypeName,
                        vehicleTypeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                matchedCount++;
                if (TryConfigureVehicle(vehicleController, "lifecycle-scan"))
                    configuredCount++;
            }
        }

        private bool TryConfigureVehicle(VehicleController? vehicleController, string source)
        {
            if (!IsMootorVehicle(vehicleController))
                return false;

            var configuredVehicle = vehicleController!;
            if (!configuredVehicle.controlledByPlayer && IsOnSidewalk(configuredVehicle))
                ApplyFreeParking(configuredVehicle);

            var riderController = configuredVehicle.GetComponent<MootorVehicleRiderController>();
            var added = riderController == null;
            if (added)
                riderController = configuredVehicle.gameObject.AddComponent<MootorVehicleRiderController>();

            var fuelController = configuredVehicle.GetComponent<MootorVehicleFuelController>();
            if (fuelController == null)
                fuelController = configuredVehicle.gameObject.AddComponent<MootorVehicleFuelController>();

            var materialController = configuredVehicle.GetComponent<MootorVehicleMaterialController>();
            if (materialController == null)
                materialController = configuredVehicle.gameObject.AddComponent<MootorVehicleMaterialController>();

            var ambientController = configuredVehicle.GetComponent<MootorVehicleAmbientMooController>();
            if (ambientController == null)
                ambientController = configuredVehicle.gameObject.AddComponent<MootorVehicleAmbientMooController>();

            var impactController = configuredVehicle.GetComponent<MootorVehicleImpactController>();
            if (impactController == null)
                impactController = configuredVehicle.gameObject.AddComponent<MootorVehicleImpactController>();

            materialController.Initialize(configuredVehicle, context);
            fuelController.Initialize(configuredVehicle, context);
            ambientController.Initialize(configuredVehicle, context);
            impactController.Initialize(configuredVehicle, context);
            riderController!.Initialize(configuredVehicle, context);
            if (source == "vehicle-entered")
                riderController.NotifyMounted();
            return added;
        }

        private bool IsMootorVehicle(VehicleController? vehicleController)
        {
            return vehicleController?.vehicleInstance != null &&
                   string.Equals(
                       vehicleController.vehicleInstance.vehicleTypeName,
                       vehicleTypeName,
                       StringComparison.Ordinal);
        }

        private void ApplyFreeParking(VehicleController vehicleController)
        {
            var instance = vehicleController.vehicleInstance;
            if (instance == null || instance.parkingState == ParkingState.Legal)
                return;

            var changed = instance.parkingState != ParkingState.NotAvailable ||
                          !string.IsNullOrEmpty(instance.parkingNeighbourhood) ||
                          instance.unpaidParkingAmount > 0f;
            if (!changed)
                return;

            // Native scooters never run CarController.UpdateParkingZone(), so their saved parking
            // state does not become Illegal or accrue meter fees. Apply that same exemption at the
            // standard vehicle-exit event while keeping the cow on the proven car controller.
            instance.parkingState = ParkingState.NotAvailable;
            instance.parkingNeighbourhood = string.Empty;
            instance.unpaidParkingAmount = 0f;
            SaveGameManager.MarkChange();
            GlobalEvents.onVehicleVariablesChanged?.Invoke();
            MootorVehicleDiagnostics.Info(
                context,
                $"Moo-tor Vehicle: applied free sidewalk parking to vehicle={vehicleController.GetInstanceID()}.");
        }

        private void BeginFreeParkingHudOverride()
        {
            EndFreeParkingHudOverride();

            activeVehicleInfoPanel = UI.UIs.Instance?.playerHUD?.itemPanelUI?.vehicleInfo;
            if (activeVehicleInfoPanel == null || !TrySubscribeToTextChanges())
                return;

            parkingHudCorrectionCoroutine = StartCoroutine(CorrectParkingHudAfterEnter());
        }

        private IEnumerator CorrectParkingHudAfterEnter()
        {
            // CarController writes the initial parking result after the global enter event.
            yield return null;
            parkingHudCorrectionCoroutine = null;
            CorrectFreeParkingHud();
        }

        private bool TrySubscribeToTextChanges()
        {
            try
            {
                var eventManagerType = Type.GetType(TextMeshProEventManagerTypeName, false);
                var eventField = eventManagerType?.GetField(
                    "TEXT_CHANGED_EVENT",
                    BindingFlags.Public | BindingFlags.Static);
                var eventInstance = eventField?.GetValue(null);
                if (eventInstance == null)
                    throw new MissingMemberException(TextMeshProEventManagerTypeName, "TEXT_CHANGED_EVENT");

                var handlerParameterType = typeof(Action<UnityEngine.Object>);
                var eventType = eventInstance.GetType();
                var addMethod = eventType.GetMethod(
                    "Add",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { handlerParameterType },
                    null);
                var removeMethod = eventType.GetMethod(
                    "Remove",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { handlerParameterType },
                    null);

                var parkingValueField = typeof(UI.ItemPanel.VehicleInfoPanel).GetField(
                    "parkingZoneValue",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var keyProperty = parkingValueField?.FieldType.GetProperty(
                    "Key",
                    BindingFlags.Public | BindingFlags.Instance);
                var textContainerProperty = parkingValueField?.FieldType.GetProperty(
                    "TextContainer",
                    BindingFlags.Public | BindingFlags.Instance);
                var localizationComponent = parkingValueField?.GetValue(activeVehicleInfoPanel);
                var textContainer = textContainerProperty?.GetValue(localizationComponent, null) as UnityEngine.Object;
                if (addMethod == null ||
                    removeMethod == null ||
                    parkingValueField == null ||
                    keyProperty == null ||
                    textContainer == null)
                {
                    throw new MissingMemberException("The parking HUD localization API was not available.");
                }

                var handler = new Action<UnityEngine.Object>(HandleTextChanged);
                addMethod.Invoke(eventInstance, new object[] { handler });

                textChangedEvent = eventInstance;
                removeTextChangedHandlerMethod = removeMethod;
                textChangedHandler = handler;
                parkingZoneValueField = parkingValueField;
                localizationKeyProperty = keyProperty;
                parkingTextContainer = textContainer;
                return true;
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    "Moo-tor Vehicle: could not enable the free-parking HUD label: " +
                    exception.GetBaseException().Message);
                EndFreeParkingHudOverride();
                return false;
            }
        }

        private void HandleTextChanged(UnityEngine.Object changedObject)
        {
            if (changedObject == null || changedObject != parkingTextContainer || correctingParkingHud)
                return;

            CorrectFreeParkingHud();
        }

        private void CorrectFreeParkingHud()
        {
            var selectedVehicle = GameManager.Instance?.selectedVehicle;
            var vehicleInfo = activeVehicleInfoPanel;
            if (!IsMootorVehicle(selectedVehicle) ||
                selectedVehicle == null ||
                !selectedVehicle.controlledByPlayer ||
                vehicleInfo == null ||
                vehicleInfo.currentParkingState == ParkingState.Legal ||
                !IsOnSidewalk(selectedVehicle) ||
                (vehicleInfo.currentParkingState == ParkingState.NotAvailable &&
                 string.IsNullOrEmpty(vehicleInfo.currentParkingNeighbourhood)))
            {
                return;
            }

            correctingParkingHud = true;
            try
            {
                // NotAvailable is the non-billable scooter-style state. Keep that state while
                // presenting the clearer vanilla "Legal" label to the player.
                vehicleInfo.SetParkingZone(ParkingState.NotAvailable, string.Empty);
                var localizationComponent = parkingZoneValueField?.GetValue(vehicleInfo);
                localizationKeyProperty?.SetValue(
                    localizationComponent,
                    VanillaLegalParkingLocalizationKey,
                    null);
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    "Moo-tor Vehicle: could not update the free-parking HUD label: " +
                    exception.GetBaseException().Message);
            }
            finally
            {
                correctingParkingHud = false;
            }
        }

        private static bool IsOnSidewalk(VehicleController vehicleController)
        {
            var sidewalkLayer = LayerMask.NameToLayer(SidewalkLayerName);
            var crosswalkLayer = LayerMask.NameToLayer(CrosswalkLayerName);
            if (sidewalkLayer < 0 || crosswalkLayer < 0)
                return false;

            var carController = vehicleController as CarController ??
                                vehicleController.GetComponent<CarController>();
            var wheels = carController?.vehicleController?.powertrain?.wheels;
            if (wheels == null || wheels.Count == 0)
                return false;

            var surfaceMask = 1 << sidewalkLayer;
            surfaceMask |= 1 << crosswalkLayer;
            surfaceMask |= 1 << LayerHelper.RoadsLayerIndex;
            surfaceMask |= 1 << LayerHelper.GroundLayerIndex;
            surfaceMask |= 1 << LayerHelper.GroundUnplacableLayerIndex;
            var navMeshFilter = new NavMeshQueryFilter
            {
                agentTypeID = NpcNavMeshAgentTypeId,
                areaMask = NavMesh.AllAreas
            };

            foreach (var wheel in wheels)
            {
                var wheelTransform = wheel?.wheelUAPI?.transform;
                if (wheelTransform == null)
                    return false;

                var probeOrigin = wheelTransform.position + Vector3.up * SidewalkProbeStartHeight;
                var probePosition = wheelTransform.position;
                if (Physics.Raycast(
                        probeOrigin,
                        Vector3.down,
                        out var surfaceHit,
                        SidewalkProbeDistance,
                        surfaceMask,
                        QueryTriggerInteraction.Collide))
                {
                    if (surfaceHit.collider != null &&
                        surfaceHit.collider.gameObject.layer == crosswalkLayer)
                    {
                        return false;
                    }

                    probePosition = surfaceHit.point;
                }

                if (!NavMesh.SamplePosition(
                        probePosition,
                        out var navMeshHit,
                        SidewalkNavMeshSampleDistance,
                        navMeshFilter))
                {
                    return false;
                }

                var horizontalDelta = navMeshHit.position - probePosition;
                horizontalDelta.y = 0f;
                if (horizontalDelta.sqrMagnitude >
                        SidewalkHorizontalTolerance * SidewalkHorizontalTolerance ||
                    Mathf.Abs(navMeshHit.position.y - probePosition.y) > SidewalkVerticalTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private void EndFreeParkingHudOverride()
        {
            if (parkingHudCorrectionCoroutine != null)
            {
                StopCoroutine(parkingHudCorrectionCoroutine);
                parkingHudCorrectionCoroutine = null;
            }

            if (textChangedEvent != null &&
                removeTextChangedHandlerMethod != null &&
                textChangedHandler != null)
            {
                try
                {
                    removeTextChangedHandlerMethod.Invoke(
                        textChangedEvent,
                        new object[] { textChangedHandler });
                }
                catch (Exception exception)
                {
                    context?.Logger.Warn(
                        "Moo-tor Vehicle: could not remove the free-parking HUD listener: " +
                        exception.GetBaseException().Message);
                }
            }

            textChangedEvent = null;
            removeTextChangedHandlerMethod = null;
            textChangedHandler = null;
            parkingZoneValueField = null;
            localizationKeyProperty = null;
            parkingTextContainer = null;
            activeVehicleInfoPanel = null;
            correctingParkingHud = false;
        }
    }
}
