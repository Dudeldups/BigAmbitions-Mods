#nullable enable
using BAModAPI;
using System.Collections;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using PlayerActivity;
using UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigHax
{
    [DefaultExecutionOrder(-10000)]
    public sealed class BigHaxRuntime : MonoBehaviour
    {
        private const float ActiveVehiclePollIntervalSeconds = 0.1f;
        private const float CasinoBetLimitPollIntervalSeconds = 0.5f;
        private const float CustomerTrafficPollIntervalSeconds = 5f;
        private const float EmployeeTrainingPollIntervalSeconds = 0.25f;
        private const float LoanLimitPollIntervalSeconds = 0.5f;
        private static readonly MethodInfo? SetUpActivitySliderMethod = typeof(PlayerActivityUI).GetMethod(
            "SetUpSlider", BindingFlags.Instance | BindingFlags.NonPublic);

        private static BigHaxRuntime? instance;
        private readonly BigHaxBusinessCapacityService businessCapacityService = new BigHaxBusinessCapacityService();
        private readonly BigHaxCasinoBetLimitService casinoBetLimitService = new BigHaxCasinoBetLimitService();
        private readonly BigHaxBuildingCustomerCapacityService buildingCustomerCapacityService = new BigHaxBuildingCustomerCapacityService();
        private BigHaxCustomerTrafficService? customerTrafficService;
        private readonly BigHaxEmployeeDemandService employeeDemandService = new BigHaxEmployeeDemandService();
        private readonly BigHaxEmployeeTrainingService employeeTrainingService = new BigHaxEmployeeTrainingService();
        private readonly BigHaxIllegalParkingService illegalParkingService = new BigHaxIllegalParkingService();
        private readonly BigHaxInstallationFeeService installationFeeService = new BigHaxInstallationFeeService();
        private readonly BigHaxInstantDeliveryService instantDeliveryService = new BigHaxInstantDeliveryService();
        private readonly BigHaxInvestmentLimitService investmentLimitService = new BigHaxInvestmentLimitService();
        private readonly BigHaxItemCapacityService itemCapacityService = new BigHaxItemCapacityService();
        private readonly BigHaxLoanLimitService loanLimitService = new BigHaxLoanLimitService();
        private readonly BigHaxHeadhunterRpService headhunterRpService = new BigHaxHeadhunterRpService();
        private readonly BigHaxHrManagerCapacityService hrManagerCapacityService = new BigHaxHrManagerCapacityService();
        private readonly BigHaxRecruitmentCandidateService recruitmentCandidateService = new BigHaxRecruitmentCandidateService();
        private readonly BigHaxOverlayUi overlayUi = new BigHaxOverlayUi();
        private readonly BigHaxPlayerHaxService playerHaxService = new BigHaxPlayerHaxService();
        private readonly BigHaxPurchaseLimitService purchaseLimitService = new BigHaxPurchaseLimitService();
        private readonly BigHaxUnlockService unlockService = new BigHaxUnlockService();
        private readonly BigHaxSleepRestDurationService sleepRestDurationService = new BigHaxSleepRestDurationService();
        private readonly BigHaxExtendedBedSleepLabelService extendedBedSleepLabelService = new BigHaxExtendedBedSleepLabelService();
        private readonly BigHaxSleepTimeAccelerationService sleepTimeAccelerationService = new BigHaxSleepTimeAccelerationService();
        private readonly BigHaxUpdateNoticeUi updateNoticeUi = new BigHaxUpdateNoticeUi();
        private readonly BigHaxVehicleCapacityService vehicleCapacityService = new BigHaxVehicleCapacityService();
        private readonly BigHaxVehicleConditionService vehicleConditionService = new BigHaxVehicleConditionService();
        private readonly BigHaxTrafficService trafficService = new BigHaxTrafficService();

        private bool applyRequested;
        private bool sleepDurationApplyRequested;
        private bool parkingVehicleEventsSubscribed;
        private Coroutine? employeeDemandCleanupCoroutine;
        private Coroutine? employeeDemandMessageCleanupCoroutine;
        private Coroutine? optionsUiPrewarmCoroutine;
        private Coroutine? parkingExitCleanupCoroutine;
        private Coroutine? sleepDurationApplyCoroutine;
        private Coroutine? vehicleCollisionApplyCoroutine;
        private FieldInfo? onEnterVehicleField;
        private FieldInfo? onExitVehicleField;
        private Delegate? onEnterVehicleHandler;
        private Delegate? onExitVehicleHandler;
        private float nextCasinoBetLimitPollAt;
        private ModContext? context;
        private float nextActiveVehiclePollAt;
        private float nextCustomerTrafficPollAt;
        private float nextEmployeeTrainingPollAt;
        private float nextLoanLimitPollAt;
        private BigHaxSettings? settings;
        private SleepActivity? lastBedSleepActivity;

        public static BigHaxRuntime Initialize(ModContext context, BigHaxSettings settings)
        {
            var runtime = FindObjectOfType<BigHaxRuntime>();
            if (runtime == null)
            {
                var runtimeObject = new GameObject(nameof(BigHaxRuntime));
                DontDestroyOnLoad(runtimeObject);
                runtime = runtimeObject.AddComponent<BigHaxRuntime>();
            }

            runtime.context = context;
            runtime.settings = settings;
            runtime.applyRequested = true;
            runtime.sleepDurationApplyRequested = true;
            runtime.nextActiveVehiclePollAt = 0f;
            runtime.nextCasinoBetLimitPollAt = 0f;
            runtime.nextCustomerTrafficPollAt = 0f;
            runtime.nextEmployeeTrainingPollAt = 0f;
            runtime.nextLoanLimitPollAt = 0f;
            runtime.customerTrafficService = CreateCustomerTrafficService(context);
            runtime.employeeDemandService.SetDailyCleanupScheduler(runtime.ScheduleEmployeeDemandCleanup);
            runtime.installationFeeService.Initialize();
            runtime.headhunterRpService.Initialize();
            runtime.hrManagerCapacityService.Initialize();
            runtime.instantDeliveryService.Initialize(runtime.StartInstantDeliveryOperation);
            runtime.overlayUi.ConfigureUnlockActions(runtime.unlockService.ConfirmUnlockAllContacts, runtime.unlockService.ConfirmUnlockAllCourses);
            runtime.overlayUi.ConfigurePurchaseLimitOption(runtime.purchaseLimitService.AreLimitsDisabled, runtime.purchaseLimitService.SetLimitsDisabled);
            runtime.updateNoticeUi.Initialize(context.ModId);
            instance = runtime;
            runtime.ApplyIfRequested();
            GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
            return runtime;
        }

        public static void RequestImmediateApply()
        {
            if (instance != null)
            {
                instance.applyRequested = true;
                instance.sleepDurationApplyRequested = true;
            }
        }

        public void Shutdown()
        {
            if (employeeDemandCleanupCoroutine != null)
            {
                StopCoroutine(employeeDemandCleanupCoroutine);
                employeeDemandCleanupCoroutine = null;
            }

            if (employeeDemandMessageCleanupCoroutine != null)
            {
                StopCoroutine(employeeDemandMessageCleanupCoroutine);
                employeeDemandMessageCleanupCoroutine = null;
            }

            if (optionsUiPrewarmCoroutine != null)
            {
                StopCoroutine(optionsUiPrewarmCoroutine);
                optionsUiPrewarmCoroutine = null;
            }

            if (parkingExitCleanupCoroutine != null)
            {
                StopCoroutine(parkingExitCleanupCoroutine);
                parkingExitCleanupCoroutine = null;
            }

            if (sleepDurationApplyCoroutine != null)
            {
                StopCoroutine(sleepDurationApplyCoroutine);
                sleepDurationApplyCoroutine = null;
            }

            if (vehicleCollisionApplyCoroutine != null)
            {
                StopCoroutine(vehicleCollisionApplyCoroutine);
                vehicleCollisionApplyCoroutine = null;
            }

            TryRestoreCustomerTraffic();
            casinoBetLimitService.RestoreOriginalLimit();
            buildingCustomerCapacityService.RestoreOriginalCapacities();
            businessCapacityService.RestoreOriginalCapacities();
            illegalParkingService.RestoreOriginalState();
            installationFeeService.Shutdown();
            investmentLimitService.RestoreOriginalLimit();
            itemCapacityService.RestoreOriginalCapacities();
            loanLimitService.RestoreOriginalLimit();
            headhunterRpService.Shutdown();
            hrManagerCapacityService.Shutdown();
            recruitmentCandidateService.Unsubscribe();
            employeeDemandService.Unsubscribe();
            playerHaxService.Shutdown();
            instantDeliveryService.Shutdown();
            sleepRestDurationService.RestoreOriginalDurationsOnShutdown();
            extendedBedSleepLabelService.Detach();
            sleepTimeAccelerationService.RestoreOriginalCurve();
            vehicleCapacityService.RestoreOriginalCapacities();
            trafficService.Shutdown();
            updateNoticeUi.Shutdown();
            overlayUi.Shutdown();
            if (instance == this)
                instance = null;

            Destroy(gameObject);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            GlobalEvents.onVehicleVariablesChanged += HandleVehicleVariablesChanged;
            GlobalEvents.onTimeMachineStarted += HandleTimeMachineStarted;
            GlobalEvents.onTimeMachineEnded += HandleTimeMachineEnded;
            BigHaxVehicleCollisionGuard.CollisionDetected += HandleVehicleCollision;
            SubscribeParkingVehicleEvents();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
            GlobalEvents.onTimeMachineStarted -= HandleTimeMachineStarted;
            GlobalEvents.onTimeMachineEnded -= HandleTimeMachineEnded;
            BigHaxVehicleCollisionGuard.CollisionDetected -= HandleVehicleCollision;
            UnsubscribeParkingVehicleEvents();
        }

        private void Update()
        {
            if (settings == null)
                return;

            ApplyIfRequested();
            PollUiToggleHotkey();
            PollActiveVehicleChanges();
            PollCasinoBetLimitChanges();
            PollCustomerTrafficChanges();
            PollEmployeeTrainingChanges();
            RefreshActiveBedSleep();
            sleepTimeAccelerationService.RefreshActiveSleep(context, settings);
            PollLoanLimitChanges();
            updateNoticeUi.ConsumeGameplayInputIfNeeded();
            overlayUi.ConsumeGameplayInputIfNeeded();
        }

        private void ApplyIfRequested()
        {
            if (!applyRequested || context == null || settings == null)
                return;

            try
            {
                SafeApply("casino bet limit", () => casinoBetLimitService.ApplyConfiguredLimit(context, settings));
                SafeApply("building customer capacities", () => buildingCustomerCapacityService.ApplyConfiguredCapacities(context, settings));
                SafeApply("business capacities", () => businessCapacityService.ApplyConfiguredCapacities(context, settings));
                SafeApply("illegal parking", () => illegalParkingService.ApplyConfiguredBehavior(context, settings));
                SafeApply("installation firm fee", () => installationFeeService.ApplyConfiguredPercentage(settings));
                SafeApply("investment limit", () => investmentLimitService.ApplyConfiguredLimit(context, settings));
                SafeApply("customer traffic", () => TryApplyCustomerTraffic(context, settings, forceRefresh: true));
                SafeApply("item capacities", () => itemCapacityService.ApplyConfiguredCapacities(context, settings));
                SafeApply("loan limit", () => loanLimitService.ApplyConfiguredLimit(settings));
                SafeApply("headhunter recruitment points", () => headhunterRpService.ApplyConfiguredBehavior(context, settings));
                SafeApply("HR manager capacity", () => hrManagerCapacityService.ApplyConfiguredBehavior(settings));
                SafeApply("recruitment candidate maximum skill", () => recruitmentCandidateService.ApplyConfiguredMaximum(context, settings));
                SafeApply("AI vehicle traffic", () => trafficService.ApplyConfiguredBehavior(context, settings));
                SafeApply("employee demands", () => employeeDemandService.ApplyConfiguredBehavior(context, settings));
                SafeApply("player hax", () => playerHaxService.ApplyConfiguredBehavior(settings));
                SafeApply("instant deliveries", () => instantDeliveryService.ApplyConfiguredBehavior(settings));
                if (sleepDurationApplyRequested)
                {
                    if (sleepRestDurationService.NeedsSettingsApply(settings))
                        SafeApply("bench rest durations", () => sleepRestDurationService.ApplyConfiguredDurations(context, settings));
                    sleepDurationApplyRequested = false;
                }
                SafeApply("vehicle capacities", () => vehicleCapacityService.ApplyConfiguredCapacities(context, settings, forceRefresh: true));
                SafeApply("vehicle conditions", () => vehicleConditionService.ApplyConfiguredConditions(settings));
            }
            finally
            {
                applyRequested = false;
            }
        }

        private void PollActiveVehicleChanges()
        {
            if (context == null || settings == null || Time.unscaledTime < nextActiveVehiclePollAt)
                return;

            nextActiveVehiclePollAt = Time.unscaledTime + ActiveVehiclePollIntervalSeconds;
            vehicleCapacityService.ApplyConfiguredCapacities(context, settings, forceRefresh: false);
        }

        private void PollCustomerTrafficChanges()
        {
            if (context == null || settings == null || Time.unscaledTime < nextCustomerTrafficPollAt)
                return;

            nextCustomerTrafficPollAt = Time.unscaledTime + CustomerTrafficPollIntervalSeconds;
            TryApplyCustomerTraffic(context, settings, forceRefresh: false);
        }

        private void PollCasinoBetLimitChanges()
        {
            if (context == null || settings == null || Time.unscaledTime < nextCasinoBetLimitPollAt)
                return;

            nextCasinoBetLimitPollAt = Time.unscaledTime + CasinoBetLimitPollIntervalSeconds;
            casinoBetLimitService.ApplyConfiguredLimit(context, settings);
        }

        private void PollEmployeeTrainingChanges()
        {
            if (context == null || settings == null || Time.unscaledTime < nextEmployeeTrainingPollAt)
                return;

            nextEmployeeTrainingPollAt = Time.unscaledTime + EmployeeTrainingPollIntervalSeconds;
            employeeTrainingService.Update(context, settings);
        }

        private void PollLoanLimitChanges()
        {
            if (context == null || settings == null || Time.unscaledTime < nextLoanLimitPollAt)
                return;

            nextLoanLimitPollAt = Time.unscaledTime + LoanLimitPollIntervalSeconds;
            loanLimitService.ApplyConfiguredLimit(settings);
        }

        private void RefreshActiveBedSleep()
        {
            if (context == null || settings?.EnableExtendedBedSleep != true)
            {
                lastBedSleepActivity = null;
                return;
            }

            var activityUi = InstanceBehavior<UIs>.Instance?.playerActivityUI;
            if (activityUi == null || !PlayerActivityUI.IsPanelOpen ||
                activityUi.GetCurrentActivity is not SleepActivity activity)
            {
                lastBedSleepActivity = null;
                return;
            }

            if (ReferenceEquals(lastBedSleepActivity, activity))
                return;

            lastBedSleepActivity = activity;
            SafeApply("active bed sleep duration", () =>
            {
                if (sleepRestDurationService.PatchActiveBedSleep(activity, context))
                    SetUpActivitySliderMethod?.Invoke(activityUi, null);
            });
        }

        private void PollUiToggleHotkey()
        {
            if (settings == null || !Input.GetKeyDown(settings.UiHotkey))
                return;

            overlayUi.Toggle();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            overlayUi.Hide();
            updateNoticeUi.HandleSceneLoaded();
            UnsubscribeParkingVehicleEvents();
            SubscribeParkingVehicleEvents();
            TryInvalidateCustomerTrafficCache();
            casinoBetLimitService.InvalidateCache();
            employeeDemandService.InvalidateCache();
            employeeTrainingService.InvalidateCache();
            investmentLimitService.InvalidateCache();
            buildingCustomerCapacityService.InvalidateCache();
            businessCapacityService.InvalidateCache();
            illegalParkingService.InvalidateCache();
            itemCapacityService.InvalidateCache();
            instantDeliveryService.AttachUiHooks();
            headhunterRpService.AttachUiHooks();
            loanLimitService.InvalidateCache();
            sleepRestDurationService.InvalidateCache();
            if (sleepDurationApplyCoroutine == null && context != null && settings != null)
                sleepDurationApplyCoroutine = StartCoroutine(ApplySleepDurationsAfterGameLoad());
            vehicleCapacityService.InvalidateCache();
            trafficService.InvalidateCache();
            applyRequested = true;
        }

        private void OnGUI()
        {
            if (context == null || settings == null)
                return;

            overlayUi.OnGui(context, settings);
            updateNoticeUi.OnGui(context);
        }

        private void ScheduleEmployeeDemandCleanup()
        {
            if (employeeDemandCleanupCoroutine == null)
                employeeDemandCleanupCoroutine = StartCoroutine(ClearEmployeeDemandsAfterDailyUpdate());
        }

        private void ScheduleEmployeeDemandMessageCleanup()
        {
            if (employeeDemandMessageCleanupCoroutine == null)
                employeeDemandMessageCleanupCoroutine = StartCoroutine(RemoveSavedEmployeeDemandMessagesAfterLoad());
        }

        private void HandleGameLoadedLate()
        {
            if (context != null && settings != null)
            {
                SafeApply("employee events after game load", () => employeeDemandService.RebindAfterGameLoad(context, settings));
                SafeApply("recruitment events after game load", () => recruitmentCandidateService.RebindAfterGameLoad(context, settings));
            }
            playerHaxService.ApplyConfiguredBehavior(settings!);
            extendedBedSleepLabelService.Attach(settings!);
            // These callbacks are cleared while a save is loading and are required
            // by extended bed sleep once the loaded game is ready.
            GlobalEvents.onTimeMachineStarted -= HandleTimeMachineStarted;
            GlobalEvents.onTimeMachineStarted += HandleTimeMachineStarted;
            GlobalEvents.onTimeMachineEnded -= HandleTimeMachineEnded;
            GlobalEvents.onTimeMachineEnded += HandleTimeMachineEnded;
            instantDeliveryService.AttachUiHooks();
            headhunterRpService.AttachUiHooks();
            SafeApply("AI vehicle traffic after game load", () => trafficService.ApplyConfiguredBehavior(context!, settings!));
            ScheduleEmployeeDemandMessageCleanup();
            if (sleepDurationApplyCoroutine == null)
                sleepDurationApplyCoroutine = StartCoroutine(ApplySleepDurationsAfterGameLoad());
            if (optionsUiPrewarmCoroutine == null)
                optionsUiPrewarmCoroutine = StartCoroutine(PrewarmOptionsUiWhenGameIsReady());
        }

        private void StartInstantDeliveryOperation(IEnumerator operation)
        {
            StartCoroutine(operation);
        }

        private IEnumerator ApplySleepDurationsAfterGameLoad()
        {
            // Bed activity configurations are instantiated asynchronously after
            // the game-loaded callback. Keep this to two lightweight, bounded
            // startup passes; scanning all loaded objects repeatedly is costly.
            yield return new WaitForEndOfFrame();
            if (context != null && settings != null)
            {
                SafeApply("sleep durations after game load", () => sleepRestDurationService.ApplyConfiguredDurations(context, settings));
                extendedBedSleepLabelService.Attach(settings);
            }

            yield return new WaitForSecondsRealtime(1f);
            if (context != null && settings != null)
            {
                SafeApply("sleep durations after delayed game load", () => sleepRestDurationService.ApplyConfiguredDurations(context, settings));
                extendedBedSleepLabelService.Attach(settings);
            }

            sleepDurationApplyCoroutine = null;
        }

        private IEnumerator PrewarmOptionsUiWhenGameIsReady()
        {
            // The callback can run before City mods and the native Options
            // controller finish initialization. Poll only for prefab readiness;
            // Prewarm does not build anything until both vanilla rows exist.
            while (context != null && settings != null)
            {
                var completed = false;
                SafeApply("options UI prewarm", () => completed = overlayUi.Prewarm(context, settings));
                if (completed)
                    break;

                yield return new WaitForSecondsRealtime(0.1f);
            }

            optionsUiPrewarmCoroutine = null;
        }

        private IEnumerator ClearEmployeeDemandsAfterDailyUpdate()
        {
            yield return employeeDemandService.ClearNewDemandsAfterDailyUpdate();
            employeeDemandCleanupCoroutine = null;
        }

        private IEnumerator RemoveSavedEmployeeDemandMessagesAfterLoad()
        {
            yield return employeeDemandService.RemoveSavedDemandMessagesAfterLoad();
            employeeDemandMessageCleanupCoroutine = null;
        }

        private void HandleVehicleVariablesChanged()
        {
            if (context == null || settings == null)
                return;

            if (settings.DisableIllegalParkingPenalties)
                SafeApply("illegal parking onVehicleVariablesChanged", () => illegalParkingService.ApplyConfiguredBehavior(context, settings));

            SafeApply("vehicle conditions onVehicleVariablesChanged", () => vehicleConditionService.ApplyConfiguredConditions(settings));
        }

        private void HandleVehicleCollision()
        {
            if (settings == null || !vehicleConditionService.HasEnabledConditions(settings) || vehicleCollisionApplyCoroutine != null)
                return;

            vehicleCollisionApplyCoroutine = StartCoroutine(ApplyVehicleConditionsAfterCollision());
        }

        private IEnumerator ApplyVehicleConditionsAfterCollision()
        {
            yield return new WaitForEndOfFrame();
            if (settings != null)
                SafeApply("vehicle conditions after collision", () => vehicleConditionService.ApplyActiveVehicleConditions(settings));

            vehicleCollisionApplyCoroutine = null;
        }

        private void HandleTimeMachineStarted()
        {
            sleepTimeAccelerationService.HandleTimeMachineStarted(context, settings);
        }

        private void HandleTimeMachineEnded()
        {
            sleepTimeAccelerationService.HandleTimeMachineEnded();
        }

        private void HandleVehicleEntered()
        {
            if (context == null || settings == null)
                return;

            if (settings.DisableIllegalParkingPenalties)
                SafeApply("illegal parking onEnterVehicle", () => illegalParkingService.HandleVehicleEntered(context, settings));

            SafeApply("vehicle conditions onEnterVehicle", () => vehicleConditionService.ApplyConfiguredConditions(settings));
        }

        private void HandleVehicleExited()
        {
            if (context == null || settings == null || !settings.DisableIllegalParkingPenalties)
                return;

            if (parkingExitCleanupCoroutine != null)
                StopCoroutine(parkingExitCleanupCoroutine);

            parkingExitCleanupCoroutine = StartCoroutine(HandleVehicleExitedDeferred());
        }

        private IEnumerator HandleVehicleExitedDeferred()
        {
            for (var frame = 0; frame < 90; frame++)
            {
                yield return null;

                if (context == null || settings == null || !settings.DisableIllegalParkingPenalties)
                    yield break;

                SafeApply("illegal parking onExitVehicle", () => illegalParkingService.HandleVehicleExited(context, settings));
            }

            parkingExitCleanupCoroutine = null;
        }

        private void SubscribeParkingVehicleEvents()
        {
            if (parkingVehicleEventsSubscribed)
                return;

            try
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var globalEventsType = typeof(GlobalEvents);
                onEnterVehicleField = globalEventsType.GetField("onEnterVehicle", flags);
                onExitVehicleField = globalEventsType.GetField("onExitVehicle", flags);
                onEnterVehicleHandler = SubscribeGlobalEvent(onEnterVehicleField, nameof(HandleVehicleEntered));
                onExitVehicleHandler = SubscribeGlobalEvent(onExitVehicleField, nameof(HandleVehicleExited));
                parkingVehicleEventsSubscribed = onEnterVehicleHandler != null || onExitVehicleHandler != null;
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
            }
        }

        private void UnsubscribeParkingVehicleEvents()
        {
            TryUnsubscribeGlobalEvent(onEnterVehicleField, onEnterVehicleHandler);
            TryUnsubscribeGlobalEvent(onExitVehicleField, onExitVehicleHandler);
            onEnterVehicleHandler = null;
            onExitVehicleHandler = null;
            onEnterVehicleField = null;
            onExitVehicleField = null;
            parkingVehicleEventsSubscribed = false;
        }

        private Delegate? SubscribeGlobalEvent(FieldInfo? eventField, string handlerMethodName)
        {
            if (eventField == null)
                return null;

            var eventType = eventField.FieldType;
            if (eventType == null)
                return null;

            var handler = CreateCompatibleDelegate(eventType, handlerMethodName);
            if (handler == null)
                return null;

            var currentValue = eventField.GetValue(null) as Delegate;
            eventField.SetValue(null, Delegate.Combine(currentValue, handler));
            return handler;
        }

        private Delegate? CreateCompatibleDelegate(Type eventType, string handlerMethodName)
        {
            var invokeMethod = eventType.GetMethod("Invoke");
            var targetMethod = GetType().GetMethod(handlerMethodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (invokeMethod == null || targetMethod == null)
                return null;

            var parameters = invokeMethod.GetParameters()
                .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
                .ToArray();
            var call = Expression.Call(Expression.Constant(this), targetMethod);
            Expression body = call;
            if (invokeMethod.ReturnType != typeof(void))
            {
                body = Expression.Block(call, Expression.Default(invokeMethod.ReturnType));
            }

            return Expression.Lambda(eventType, body, parameters).Compile();
        }

        private static void TryUnsubscribeGlobalEvent(FieldInfo? eventField, Delegate? handler)
        {
            if (eventField == null || handler == null)
                return;

            try
            {
                var currentValue = eventField.GetValue(null) as Delegate;
                eventField.SetValue(null, Delegate.Remove(currentValue, handler));
            }
            catch
            {
            }
        }

        private void SafeApply(string scope, System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception exception)
            {
                context?.Logger.Error(exception);
            }
        }

        private static BigHaxCustomerTrafficService? CreateCustomerTrafficService(ModContext context)
        {
            try
            {
                return new BigHaxCustomerTrafficService();
            }
            catch (System.Exception exception)
            {
                context.Logger.Error(exception);
                return null;
            }
        }

        private void TryApplyCustomerTraffic(ModContext context, BigHaxSettings settings, bool forceRefresh)
        {
            if (customerTrafficService == null)
                return;

            try
            {
                customerTrafficService.ApplyConfiguredTraffic(context, settings, forceRefresh);
            }
            catch (System.Exception exception)
            {
                context.Logger.Error(exception);
                customerTrafficService = null;
            }
        }

        private void TryRestoreCustomerTraffic()
        {
            if (customerTrafficService == null)
                return;

            try
            {
                customerTrafficService.RestoreOriginalState(context);
            }
            catch (System.Exception exception)
            {
                context?.Logger.Error(exception);
            }
        }

        private void TryInvalidateCustomerTrafficCache()
        {
            if (customerTrafficService == null)
                return;

            try
            {
                customerTrafficService.InvalidateCache();
            }
            catch (System.Exception exception)
            {
                context?.Logger.Error(exception);
                customerTrafficService = null;
            }
        }
    }
}
