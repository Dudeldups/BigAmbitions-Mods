#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;

[AddComponentMenu("")]
internal sealed class LamborghiniAventadorAutoParkingGuard : MonoBehaviour
{
    private const string HarmonyId = "Dudeldups.Lamborghini_Aventador.AutoParking";
    private const string DiagnosticsMarker = "auto-parking-diagnostics.enabled";
    private const string TeleportDiagnosticsMarker = "teleport-diagnostics.enabled";
    private const float MaximumCenteringCorrection = 0.25f;
    private const float MinimumCenteringCorrection = 0.02f;
    private const float MaximumAutoParkSpotDistance = 0.75f;
    private const float MaximumAutoParkSpotHeightDifference = 1.25f;
    private static readonly BindingFlags InstanceMethods =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags AnyMethods = InstanceMethods | BindingFlags.Static;
    private static readonly FieldInfo? HudParkingStateField =
        typeof(UI.ItemPanel.VehicleInfoPanel).GetField("currentParkingState",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? LastAutoParkPositionField =
        typeof(VehicleParkingHelper).GetField("_lastAutoParkPosition",
            BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? GroundLayerMaskField =
        typeof(LayerHelper).GetField("groundLayerMask",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    private const float MaximumNativeSpotPositionError = 0.05f;
    private static readonly MethodInfo? NativeAutoParkMethod =
        typeof(VehicleParkingHelper).GetMethod("ParkInClosestSpot", InstanceMethods,
            binder: null, types: Type.EmptyTypes, modifiers: null);
    private static readonly MethodInfo? NativeAutoParkPrefix =
        typeof(LamborghiniAventadorAutoParkingGuard).GetMethod(
            nameof(BeforeNativeAutoPark), BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo? NativeAutoParkPostfix =
        typeof(LamborghiniAventadorAutoParkingGuard).GetMethod(
            nameof(WrapNativeAutoPark), BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo? NativeTeleportMethod =
        typeof(VehicleHelper).GetMethod("TeleportVehicle", AnyMethods,
            binder: null,
            types: new[] { typeof(VehicleController), typeof(Vector3), typeof(Quaternion) },
            modifiers: null);
    private static readonly MethodInfo? NativeTeleportPostfix =
        typeof(LamborghiniAventadorAutoParkingGuard).GetMethod(
            nameof(AfterNativeTeleport), BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo? NativeDamperMethod =
        typeof(NWH.WheelController3D.Damper).GetMethod("CalculateDamperForce",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, types: new[] { typeof(float).MakeByRefType() }, modifiers: null);
    private static readonly MethodInfo? NativeDamperPrefix =
        typeof(LamborghiniAventadorAutoParkingGuard).GetMethod(
            nameof(BeforeNativeDamperForce), BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo? NativeWheelStepMethod =
        typeof(NWH.WheelController3D.WheelController).GetMethod("Step", InstanceMethods,
            binder: null, types: Type.EmptyTypes, modifiers: null);
    private static readonly MethodInfo? NativeWheelStepPostfix =
        typeof(LamborghiniAventadorAutoParkingGuard).GetMethod(
            nameof(AfterNativeWheelStep), BindingFlags.Static | BindingFlags.NonPublic);

    private static LamborghiniAventadorAutoParkingGuard? current;

    private readonly HashSet<int> activeHelpers = new();
    private System.Runtime.CompilerServices.ConditionalWeakTable<
        NWH.WheelController3D.Damper, NWH.WheelController3D.WheelController> pendingTeleportDampers = new();
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private object? harmony;
    private Type? harmonyType;

    private readonly struct ParkingProbeResult
    {
        internal readonly bool IsLegal;
        internal readonly bool HasHandicapLane;
        internal readonly string Neighbourhood;
        internal readonly string Details;

        internal ParkingProbeResult(bool isLegal, bool hasHandicapLane,
            string neighbourhood, string details)
        {
            IsLegal = isLegal;
            HasHandicapLane = hasHandicapLane;
            Neighbourhood = neighbourhood;
            Details = details;
        }
    }

    private readonly struct ParkingSpotSnapshot
    {
        // Native Auto-Park may destroy its selected spot while the iterator runs,
        // so all geometry and diagnostic data needed afterwards is copied here.
        internal readonly Vector3 Position;
        internal readonly Vector3 ParkingUp;
        internal readonly bool HasLane;
        internal readonly bool IsHandicap;
        internal readonly string Description;
        internal readonly string LaneName;
        internal readonly string Neighbourhood;
        internal readonly int LaneInstanceId;
        internal readonly bool HasGroundSurface;
        internal readonly Vector3 GroundPosition;
        internal readonly Vector3 GroundNormal;
        internal readonly string GroundDetails;

        internal ParkingSpotSnapshot(Vector3 position, Vector3 parkingUp,
            bool hasLane, bool isHandicap, string description,
            string laneName, string neighbourhood, int laneInstanceId, bool hasGroundSurface,
            Vector3 groundPosition, Vector3 groundNormal, string groundDetails)
        {
            Position = position;
            ParkingUp = parkingUp;
            HasLane = hasLane;
            IsHandicap = isHandicap;
            Description = description;
            LaneName = laneName;
            Neighbourhood = neighbourhood;
            LaneInstanceId = laneInstanceId;
            HasGroundSurface = hasGroundSurface;
            GroundPosition = groundPosition;
            GroundNormal = groundNormal;
            GroundDetails = groundDetails;
        }
    }

    internal static LamborghiniAventadorAutoParkingGuard Install(
        ModContext context, string vehicleTypeName)
    {
        var host = new GameObject(nameof(LamborghiniAventadorAutoParkingGuard));
        DontDestroyOnLoad(host);
        var guard = host.AddComponent<LamborghiniAventadorAutoParkingGuard>();
        guard.context = context;
        guard.vehicleTypeName = vehicleTypeName;
        current = guard;

        if (HudParkingStateField == null)
            context.Logger.Warn(
                "LamborghiniAventador auto-parking: VehicleInfoPanel.currentParkingState was not found; " +
                "native HUD result cannot be diagnosed.");
        if (LastAutoParkPositionField == null)
            context.Logger.Warn(
                "LamborghiniAventador auto-parking: VehicleParkingHelper._lastAutoParkPosition was not found; " +
                "native completion cannot be distinguished from an early iterator exit.");

        var marker = Path.Combine(Application.persistentDataPath,
            "ModsLocal", "Lamborghini_Aventador", "Config", DiagnosticsMarker);
        if (File.Exists(marker))
        {
            LamborghiniAventadorDiagnostics.DebugEnabled = true;
            LamborghiniAventadorDiagnostics.AutoParkingDebugEnabled = true;
            LamborghiniAventadorDiagnostics.TeleportDebugEnabled = true;
            context.Logger.Info("LamborghiniAventador auto-parking diagnostics enabled.");
        }
        var teleportMarker = Path.Combine(Application.persistentDataPath,
            "ModsLocal", "Lamborghini_Aventador", "Config", TeleportDiagnosticsMarker);
        if (File.Exists(teleportMarker))
        {
            LamborghiniAventadorDiagnostics.DebugEnabled = true;
            LamborghiniAventadorDiagnostics.TeleportDebugEnabled = true;
            context.Logger.Info("LamborghiniAventador teleport suspension diagnostics enabled.");
        }

        GlobalEvents.onExitVehicle -= guard.HandleVehicleExited;
        GlobalEvents.onExitVehicle += guard.HandleVehicleExited;
        guard.InstallNativeCompletionHook();
        return guard;
    }

    internal void Shutdown()
    {
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        UninstallNativeCompletionHook();
        activeHelpers.Clear();
        pendingTeleportDampers = new();
        if (current == this) current = null;
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        UninstallNativeCompletionHook();
        activeHelpers.Clear();
        pendingTeleportDampers = new();
        if (current == this) current = null;
    }

    private bool DiagnosticsEnabled =>
        LamborghiniAventadorDiagnostics.DebugEnabled &&
        LamborghiniAventadorDiagnostics.AutoParkingDebugEnabled;

    private bool IsTarget(VehicleController? vehicle) =>
        vehicle != null &&
        (string.Equals(vehicle.vehicleInstance?.vehicleTypeName,
             vehicleTypeName, StringComparison.Ordinal) ||
         string.Equals(vehicle.vehicleType?.vehicleTypeName,
             vehicleTypeName, StringComparison.Ordinal));

    private void InstallNativeCompletionHook()
    {
        var canHookAutoPark = NativeAutoParkMethod != null && NativeAutoParkPrefix != null &&
                              NativeAutoParkPostfix != null &&
                              NativeAutoParkMethod.ReturnType == typeof(IEnumerator);
        if (!canHookAutoPark)
        {
            context?.Logger.Warn(
                "LamborghiniAventador auto-parking: exact native ParkInClosestSpot iterator hook " +
                $"is unavailable (method={NativeAutoParkMethod != null}, " +
                $"prefix={NativeAutoParkPrefix != null}, postfix={NativeAutoParkPostfix != null}).");
        }

        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate =>
                               string.Equals(candidate.GetName().Name, "0Harmony",
                                   StringComparison.OrdinalIgnoreCase))
                           ?? Assembly.Load(new AssemblyName("0Harmony"));
            harmonyType = assembly.GetType("HarmonyLib.Harmony", throwOnError: true);
            var harmonyMethodType = assembly.GetType("HarmonyLib.HarmonyMethod", throwOnError: true);
            var harmonyConstructor = harmonyType!.GetConstructor(new[] { typeof(string) });
            var harmonyMethodConstructor = harmonyMethodType!.GetConstructor(new[] { typeof(MethodInfo) });
            var patchMethod = harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, "Patch", StringComparison.Ordinal)) return false;
                    var parameters = candidate.GetParameters();
                    return parameters.Length == 5 && parameters[0].ParameterType == typeof(MethodBase);
                });
            if (harmonyConstructor == null || harmonyMethodConstructor == null || patchMethod == null)
                throw new MissingMethodException("Harmony 2.x Harmony/Patch API signatures were not found.");

            harmony = harmonyConstructor.Invoke(new object[] { HarmonyId });
            if (canHookAutoPark)
            {
                var prefix = harmonyMethodConstructor.Invoke(new object[] { NativeAutoParkPrefix! });
                var postfix = harmonyMethodConstructor.Invoke(new object[] { NativeAutoParkPostfix! });
                patchMethod.Invoke(harmony,
                    new object?[] { NativeAutoParkMethod, prefix, postfix, null, null });
            }

            if (NativeTeleportMethod == null || NativeTeleportPostfix == null ||
                NativeDamperMethod == null || NativeDamperPrefix == null ||
                NativeWheelStepMethod == null || NativeWheelStepPostfix == null)
            {
                context?.Logger.Warn(
                    "LamborghiniAventador teleport suspension reset could not be installed; " +
                    $"teleport={NativeTeleportMethod != null}, teleportPostfix={NativeTeleportPostfix != null}, " +
                    $"damper={NativeDamperMethod != null}, damperPrefix={NativeDamperPrefix != null}, " +
                    $"wheelStep={NativeWheelStepMethod != null}, wheelStepPostfix={NativeWheelStepPostfix != null}.");
            }
            else
            {
                var teleportPostfix = harmonyMethodConstructor.Invoke(new object[] { NativeTeleportPostfix });
                var damperPrefix = harmonyMethodConstructor.Invoke(new object[] { NativeDamperPrefix });
                var wheelStepPostfix = harmonyMethodConstructor.Invoke(new object[] { NativeWheelStepPostfix });
                patchMethod.Invoke(harmony,
                    new object?[] { NativeTeleportMethod, null, teleportPostfix, null, null });
                patchMethod.Invoke(harmony,
                    new object?[] { NativeDamperMethod, damperPrefix, null, null, null });
                patchMethod.Invoke(harmony,
                    new object?[] { NativeWheelStepMethod, null, wheelStepPostfix, null, null });
                if (TeleportDiagnosticsEnabled)
                    LamborghiniAventadorDiagnostics.TeleportInfo(context,
                        "Aventador one-shot teleport suspension hook installed " +
                        $"teleport={NativeTeleportMethod.DeclaringType?.FullName}.{NativeTeleportMethod.Name}; " +
                        $"damper={NativeDamperMethod.DeclaringType?.FullName}.{NativeDamperMethod.Name}; " +
                        $"wheelStep={NativeWheelStepMethod.DeclaringType?.FullName}.{NativeWheelStepMethod.Name}.");
            }
            try
            {
                var damageMethod = typeof(NWH.VehiclePhysics2.Damage.DamageHandler).GetMethod(
                    "IsCollisionValid", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Collision) }, null);
                var warehouseDamagePrefix = typeof(LamborghiniAventadorWarehouseEntryController).GetMethod(
                    "FilterEntranceDamage", BindingFlags.Static | BindingFlags.NonPublic);
                if (damageMethod == null || damageMethod.ReturnType != typeof(bool) || warehouseDamagePrefix == null)
                    context?.Logger.Warn("LamborghiniAventador warehouse impact filter could not bind the native damage check.");
                else
                {
                    var damagePrefix = harmonyMethodConstructor.Invoke(new object[] { warehouseDamagePrefix });
                    patchMethod.Invoke(harmony, new object?[] { damageMethod, damagePrefix, null, null, null });
                }
            }
            catch (Exception exception)
            {
                context?.Logger.Warn($"LamborghiniAventador warehouse impact filter could not be installed. {exception.GetBaseException().Message}");
            }
            if (canHookAutoPark)
                LamborghiniAventadorDiagnostics.AutoParkingInfo(context,
                    "Aventador installed exact native completion hook " +
                    $"method={NativeAutoParkMethod!.DeclaringType?.FullName}.{NativeAutoParkMethod.Name}; " +
                    "prefix deduplicates active requests and postfix wraps the returned IEnumerator.");
        }
        catch (Exception exception)
        {
            try
            {
                if (harmony != null && harmonyType != null)
                {
                    var unpatchAll = harmonyType.GetMethod("UnpatchAll",
                        BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public,
                        binder: null, types: new[] { typeof(string) }, modifiers: null);
                    unpatchAll?.Invoke(unpatchAll.IsStatic ? null : harmony, new object[] { HarmonyId });
                }
            }
            catch
            {
                // Preserve the install failure below; cleanup is best-effort.
            }
            harmony = null;
            harmonyType = null;
            context?.Logger.Warn(
                "LamborghiniAventador native hook installation failed: " +
                $"{exception.GetBaseException().GetType().Name}: {exception.GetBaseException().Message}.");
        }
    }

    private void UninstallNativeCompletionHook()
    {
        try
        {
            if (harmony != null && harmonyType != null)
            {
                var unpatchAll = harmonyType.GetMethod("UnpatchAll",
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public,
                    binder: null, types: new[] { typeof(string) }, modifiers: null);
                if (unpatchAll == null)
                    throw new MissingMethodException("Harmony.UnpatchAll(string) is unavailable.");
                unpatchAll.Invoke(unpatchAll.IsStatic ? null : harmony, new object[] { HarmonyId });
            }
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                "LamborghiniAventador auto-parking hook cleanup failed: " +
                $"{exception.GetBaseException().GetType().Name}: {exception.GetBaseException().Message}.");
        }
        harmony = null;
        harmonyType = null;
    }

    private static void AfterNativeTeleport(VehicleController __0)
    {
        var guard = current;
        var vehicle = __0;
        if (guard == null || !guard.IsTarget(vehicle)) return;

        var wheels = vehicle!.GetComponent<NWH.VehiclePhysics2.VehicleController>()?.powertrain?.wheels;
        var marked = 0;
        if (wheels != null)
        {
            foreach (var component in wheels)
            {
                if (component?.wheelUAPI is not NWH.WheelController3D.WheelController wheel)
                    continue;
                MarkTeleportDamper(guard, wheel, ref marked);
            }
        }
        else
        {
            // This is a per-teleport fallback for early lifecycle calls, not a
            // recurring scene search. The initialized powertrain list is preferred.
            foreach (var wheel in vehicle.GetComponentsInChildren<NWH.WheelController3D.WheelController>(true))
                MarkTeleportDamper(guard, wheel, ref marked);
        }

        var result = marked == 4 ? "four wheel dampers armed" : wheels == null
            ? "powertrain wheel list unavailable; hierarchy fallback used"
            : "expected four wheel dampers";
        if (marked != 4)
            guard.context?.Logger.Warn(
                $"LamborghiniAventador teleport suspension reset armed {marked}/4 wheel dampers " +
                $"for vehicle={vehicle.GetInstanceID()} ({result}).");
        ContextLogTeleport(guard, vehicle, marked, result);
    }

    private static void MarkTeleportDamper(LamborghiniAventadorAutoParkingGuard guard,
        NWH.WheelController3D.WheelController wheel, ref int marked)
    {
        if (wheel == null || wheel.damper == null || wheel.spring == null) return;
        guard.pendingTeleportDampers.Remove(wheel.damper);
        guard.pendingTeleportDampers.Add(wheel.damper, wheel);
        marked++;
    }

    private static void BeforeNativeDamperForce(
        NWH.WheelController3D.Damper __instance, ref float __0)
    {
        var guard = current;
        if (guard == null || __instance == null ||
            !guard.pendingTeleportDampers.TryGetValue(__instance, out var wheel))
            return;

        guard.pendingTeleportDampers.Remove(__instance);
        var incomingVelocity = __0;
        var spring = wheel.spring;
        var previousLength = spring?.prevLength ?? 0f;
        var currentLength = spring?.length ?? 0f;
        if (spring != null)
        {
            // UpdateSpringAndDamper has already derived the new suspension length
            // from the post-teleport ground hit. The old-to-new pose delta is not
            // physical suspension velocity, so rebase only the damper history.
            spring.prevLength = spring.length;
            spring.prevVelocity = 0f;
        }
        __0 = 0f;

        if (guard.TeleportDiagnosticsEnabled)
            LamborghiniAventadorDiagnostics.TeleportInfo(guard.context,
                $"Aventador teleport suspension sample rebased wheel={wheel.GetInstanceID()} " +
                $"springLength={previousLength:0.000}->{currentLength:0.000} " +
                $"damperVelocity={incomingVelocity:0.000}->0.000; " +
                $"springForce={spring?.force.ToString("0.0") ?? "<unavailable>"}N retained.");
    }

    private static void AfterNativeWheelStep(NWH.WheelController3D.WheelController __instance)
    {
        var guard = current;
        var damper = __instance?.damper;
        if (guard == null || damper == null ||
            !guard.pendingTeleportDampers.TryGetValue(damper, out var wheel))
            return;

        guard.pendingTeleportDampers.Remove(damper);
        var spring = wheel.spring;
        var previousLength = spring?.prevLength ?? 0f;
        var currentLength = spring?.length ?? 0f;
        if (spring != null)
        {
            // An airborne first step never calls CalculateDamperForce. Expire the
            // marker here so the later real landing keeps its physical damping.
            spring.prevLength = spring.length;
            spring.compressionVelocity = 0f;
            spring.prevVelocity = 0f;
        }

        if (guard.TeleportDiagnosticsEnabled)
            LamborghiniAventadorDiagnostics.TeleportInfo(guard.context,
                $"Aventador teleport suspension marker expired after first wheel step " +
                $"wheel={wheel.GetInstanceID()} " +
                $"springLength={previousLength:0.000}->{currentLength:0.000}; " +
                "no grounded damper sample occurred; later landing damping remains enabled.");
    }

    private bool TeleportDiagnosticsEnabled =>
        LamborghiniAventadorDiagnostics.DebugEnabled &&
        LamborghiniAventadorDiagnostics.TeleportDebugEnabled;

    private static void ContextLogTeleport(
        LamborghiniAventadorAutoParkingGuard guard, VehicleController vehicle,
        int marked, string result)
    {
        if (guard.TeleportDiagnosticsEnabled)
            LamborghiniAventadorDiagnostics.TeleportInfo(guard.context,
                $"Aventador native teleport suspension reset armed vehicle={vehicle.GetInstanceID()} " +
                $"wheels={marked}/4 result={result}.");
    }

    private static bool BeforeNativeAutoPark(
        VehicleParkingHelper __instance, out bool __state, ref IEnumerator __result)
    {
        __state = false;
        var guard = current;
        var car = __instance != null ? __instance.GetComponentInParent<CarController>(true) : null;
        if (guard == null || !guard.IsTarget(car)) return true;

        var helperId = __instance!.GetInstanceID();
        if (!guard.activeHelpers.Add(helperId))
        {
            __result = EmptyEnumerator();
            if (guard.DiagnosticsEnabled)
                LamborghiniAventadorDiagnostics.AutoParkingInfo(guard.context,
                    $"Aventador duplicate native Auto-Park request suppressed " +
                    $"vehicle={car!.GetInstanceID()} helper={helperId}; one native routine is already active.");
            return false;
        }

        __state = true;
        if (guard.DiagnosticsEnabled)
        {
            var spot = __instance.availableAutoParkSpot;
            LamborghiniAventadorDiagnostics.AutoParkingInfo(guard.context,
                $"Aventador native Auto-Park entered vehicle={car!.GetInstanceID()} helper={helperId} " +
                $"spot={DescribeSpot(spot)}, root={car.transform.position}, " +
                $"saved={car.vehicleInstance?.parkingState.ToString() ?? "<none>"}, " +
                $"hud={ReadHudState(UI.UIs.Instance?.playerHUD?.itemPanelUI?.vehicleInfo)?.ToString() ?? "<unavailable>"}.");
        }
        return true;
    }

    private static void WrapNativeAutoPark(
        VehicleParkingHelper __instance, ref IEnumerator __result, bool __state)
    {
        if (!__state) return;
        var guard = current;
        if (guard == null) return;

        var car = __instance != null ? __instance.GetComponentInParent<CarController>(true) : null;
        var spot = __instance != null ? __instance.availableAutoParkSpot : null;
        if (!guard.IsTarget(car) || car == null || __result == null || spot == null)
        {
            if (__instance != null) guard.activeHelpers.Remove(__instance.GetInstanceID());
            if (spot == null && car != null && guard.IsTarget(car) && guard.DiagnosticsEnabled)
                LamborghiniAventadorDiagnostics.AutoParkingInfo(guard.context,
                    $"Aventador native Auto-Park returned without an available spot " +
                    $"vehicle={car.GetInstanceID()}; vanilla no-op path left untouched.");
            return;
        }

        var nativeRoutine = __result;
        try
        {
            var spotSnapshot = CaptureSpot(spot!, car!);
            __result = guard.ReconcileAfterNativeAutoPark(
                nativeRoutine, __instance!, car!, spotSnapshot);
            if (guard.DiagnosticsEnabled)
                LamborghiniAventadorDiagnostics.AutoParkingInfo(guard.context,
                    $"Aventador wrapped native Auto-Park vehicle={car.GetInstanceID()} " +
                    $"helper={__instance!.GetInstanceID()} spot={spotSnapshot.Description}; " +
                    $"preNativeGround={(spotSnapshot.HasGroundSurface ? spotSnapshot.GroundPosition.ToString() : "<unavailable>")} " +
                    $"normal={spotSnapshot.GroundNormal}; {spotSnapshot.GroundDetails}; " +
                    "completion will be observed after the native iterator finishes.");
        }
        catch (Exception exception)
        {
            guard.activeHelpers.Remove(__instance!.GetInstanceID());
            __result = nativeRoutine;
            guard.context?.Logger.Warn(
                $"LamborghiniAventador auto-parking could not snapshot/wrap native result " +
                $"vehicle={car!.GetInstanceID()} helper={__instance.GetInstanceID()}; " +
                $"native iterator was returned unchanged: " +
                $"{exception.GetBaseException().GetType().Name}: " +
                $"{exception.GetBaseException().Message}.");
        }
    }

    private static IEnumerator EmptyEnumerator()
    {
        yield break;
    }

    private IEnumerator ReconcileAfterNativeAutoPark(
        IEnumerator nativeRoutine, VehicleParkingHelper helper,
        CarController car, ParkingSpotSnapshot capturedSpot)
    {
        var helperId = helper.GetInstanceID();
        var vehicleId = car.GetInstanceID();
        try
        {
            while (true)
            {
                var hasNext = false;
                object? currentValue = null;
                Exception? iteratorException = null;
                try
                {
                    hasNext = nativeRoutine.MoveNext();
                    if (hasNext) currentValue = nativeRoutine.Current;
                }
                catch (Exception exception)
                {
                    iteratorException = exception;
                }

                if (iteratorException != null)
                {
                    context?.Logger.Warn(
                        $"LamborghiniAventador auto-parking native iterator failed " +
                        $"vehicle={vehicleId} helper={helperId} spot={capturedSpot.Description}; " +
                        $"saved-state reconciliation was skipped: " +
                        $"{iteratorException.GetBaseException().GetType().Name}: " +
                        $"{iteratorException.GetBaseException().Message}.");
                    yield break;
                }
                if (!hasNext) break;
                yield return currentValue;
            }

            // The native iterator has completed its fade, teleport and saved-state
            // work. Its ground teleport synchronizes transforms before returning, so
            // inspect that committed pose immediately without waiting on another tick.
            if (car == null || helper == null || !IsTarget(car) ||
                car.vehicleInstance == null)
                yield break;

            try
            {
                ReconcileCompletedNativeAutoPark(helper, car, capturedSpot);
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"LamborghiniAventador auto-parking reconciliation failed " +
                    $"vehicle={vehicleId} helper={helperId} spot={capturedSpot.Description}; " +
                    $"the guard did not write saved parking state: " +
                    $"{exception.GetBaseException().GetType().Name}: " +
                    $"{exception.GetBaseException().Message}.");
            }
        }
        finally
        {
            try
            {
                (nativeRoutine as IDisposable)?.Dispose();
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"LamborghiniAventador auto-parking iterator cleanup failed " +
                    $"vehicle={vehicleId} helper={helperId}: " +
                    $"{exception.GetBaseException().GetType().Name}: " +
                    $"{exception.GetBaseException().Message}.");
            }
            activeHelpers.Remove(helperId);
        }
    }

    private void ReconcileCompletedNativeAutoPark(
        VehicleParkingHelper helper, CarController car,
        ParkingSpotSnapshot capturedSpot)
    {
        // Native parking clears availableAutoParkSpot before its occupancy checks.
        // Its last-position field is written only after both checks pass and just
        // before TeleportVehicleToGround, so it distinguishes a parked result from
        // a completed early-exit iterator.
        if (LastAutoParkPositionField?.GetValue(helper) is not Vector3 lastAutoParkPosition)
        {
            context?.Logger.Warn(
                $"LamborghiniAventador auto-parking vehicle={car.GetInstanceID()} " +
                $"native iterator ended without confirming teleport to {capturedSpot.Description}; " +
                $"lastAutoParkPosition={LastAutoParkPositionField?.GetValue(helper)?.ToString() ?? "<unavailable>"}; " +
                "native state and vehicle position were left untouched.");
            return;
        }
        var nativeMarkerDistance = Vector3.Distance(lastAutoParkPosition, capturedSpot.Position);
        if (nativeMarkerDistance > MaximumNativeSpotPositionError)
        {
            context?.Logger.Warn(
                $"LamborghiniAventador auto-parking vehicle={car.GetInstanceID()} " +
                $"native iterator ended without confirming teleport to {capturedSpot.Description}; " +
                $"lastAutoParkPosition={lastAutoParkPosition}, " +
                $"markerDistance={nativeMarkerDistance:0.000}m; " +
                "native state and vehicle position were left untouched.");
            return;
        }

        // In the installed game, successful ParkInClosestSpot sets the saved state
        // to Legal before UpdateParkingZone updates only the HUD. ExitVehicle later
        // copies the current HUD result back into the vehicle instance. Require the
        // native saved write before any geometry movement so the HUD and save cannot
        // diverge if the iterator completed through an early-return path.
        var nativeSavedState = car.vehicleInstance.parkingState;
        if (nativeSavedState != ParkingState.Legal)
        {
            context?.Logger.Warn(
                $"LamborghiniAventador auto-parking vehicle={car.GetInstanceID()} " +
                $"native teleport marker matched {capturedSpot.Description} but saved state is " +
                $"{nativeSavedState}; geometry reconciliation was skipped to avoid diverging " +
                "HUD and saved parking state.");
            return;
        }

        Physics.SyncTransforms();
        if (capturedSpot.HasGroundSurface)
        {
            var hasGroundCorrection = TryGetGroundedCandidate(car.transform.position,
                capturedSpot.GroundPosition, capturedSpot.ParkingUp,
                out var groundedPosition, out var nativeGroundError);
            if (hasGroundCorrection)
            {
                if (Vector3.Distance(groundedPosition, capturedSpot.Position) >
                    MaximumAutoParkSpotHeightDifference)
                {
                    context?.Logger.Warn(
                        $"LamborghiniAventador auto-parking vehicle={car.GetInstanceID()} " +
                        $"native ground pose differs from the filtered spot ground hit by " +
                        $"{nativeGroundError:0.000}m, but the grounded target is outside the " +
                        $"{MaximumAutoParkSpotHeightDifference:0.00}m spot bound; " +
                        "native pose and parking state were left untouched.");
                    return;
                }

                if (!TryValidateParkingCandidate(car, groundedPosition, capturedSpot,
                        out var groundCandidateDetails))
                {
                    context?.Logger.Warn(
                        $"LamborghiniAventador auto-parking vehicle={car.GetInstanceID()} " +
                        $"filtered ground candidate {groundedPosition} was rejected before " +
                        $"movement: {groundCandidateDetails}; native pose was preserved.");
                    return;
                }

                if (HasCandidateCollision(car, groundedPosition - car.transform.position,
                        out var groundCollision))
                {
                    context?.Logger.Warn(
                        $"LamborghiniAventador auto-parking vehicle={car.GetInstanceID()} " +
                        $"filtered ground candidate {groundedPosition} was rejected because " +
                        $"it collides with {groundCollision}; native pose was preserved.");
                    return;
                }

                var beforeGroundCorrection = car.transform.position;
                if (!TryCommitRootPosition(car, groundedPosition))
                {
                    context?.Logger.Warn(
                        $"LamborghiniAventador auto-parking vehicle={car.GetInstanceID()} " +
                        $"could not commit the filtered ground position {groundedPosition}; " +
                        $"root remains {car.transform.position}, expected {beforeGroundCorrection}; " +
                        "parking evaluation was skipped.");
                    return;
                }

                if (DiagnosticsEnabled)
                    LamborghiniAventadorDiagnostics.AutoParkingInfo(context,
                        $"Aventador corrected native ground snap vehicle={car.GetInstanceID()} " +
                        $"from={beforeGroundCorrection} to={car.transform.position} " +
                        $"groundHit={capturedSpot.GroundPosition} " +
                        $"normal={capturedSpot.GroundNormal} " +
                        $"nativeGroundError={nativeGroundError:0.000}m " +
                        $"probe={capturedSpot.GroundDetails}.");
            }
        }

        var rootToSpot = car.transform.position - capturedSpot.Position;
        var rootToSpotDistance = Vector3.ProjectOnPlane(
            rootToSpot, capturedSpot.ParkingUp).magnitude;
        var rootToSpotHeight = Mathf.Abs(
            Vector3.Dot(rootToSpot, capturedSpot.ParkingUp));
        if (rootToSpotDistance > MaximumAutoParkSpotDistance ||
            rootToSpotHeight > MaximumAutoParkSpotHeightDifference)
        {
            context?.Logger.Warn(
                $"LamborghiniAventador auto-parking vehicle={car.GetInstanceID()} " +
                $"completed native routine but moved away from its captured spot " +
                $"{capturedSpot.Description}: root={car.transform.position}, " +
                $"spot={capturedSpot.Position}, " +
                $"planarDistance={rootToSpotDistance:0.000}m, " +
                $"heightDifference={rootToSpotHeight:0.000}m; " +
                "no geometry correction was applied.");
            return;
        }

        var panel = UI.UIs.Instance?.playerHUD?.itemPanelUI?.vehicleInfo;
        var hudBeforeRefresh = ReadHudState(panel);
        var savedAfterNative = nativeSavedState.ToString();
        car.UpdateParkingZone();
        car.isCheckedForParkingZone = true;
        var hudAfterNativeRefresh = ReadHudState(panel);
        var probeAfterNativeRefresh = GetParkingWheelState(car);
        var correction = Vector3.zero;
        var correctionAttempted = false;

        // Native passes spotPosition as the car root target. Center the four-wheel
        // footprint only when it is offset a small amount, and never set parking state.
        if (hudAfterNativeRefresh != ParkingState.Legal &&
            !probeAfterNativeRefresh.IsLegal &&
            !probeAfterNativeRefresh.HasHandicapLane &&
            !capturedSpot.IsHandicap &&
            TryGetWheelbaseCenterOffset(car, out var wheelbaseOffset))
        {
            correction = -wheelbaseOffset;
            var correctionMagnitude = correction.magnitude;
            if (correctionMagnitude >= MinimumCenteringCorrection &&
                correctionMagnitude <= MaximumCenteringCorrection)
            {
                var oldPosition = car.transform.position;
                var newPosition = oldPosition + correction;
                if (DiagnosticsEnabled)
                    LamborghiniAventadorDiagnostics.AutoParkingInfo(context,
                        $"Aventador auto-parking geometry correction vehicle={car.GetInstanceID()} " +
                        $"spot={capturedSpot.Description} rootBefore={oldPosition}, " +
                        $"wheelbaseOffset={wheelbaseOffset}, rootTarget={newPosition}, " +
                        $"nativeHud={hudAfterNativeRefresh}, " +
                        $"probes=[{probeAfterNativeRefresh.Details}].");

                if (!TryValidateParkingCandidate(car, newPosition, capturedSpot,
                        out var centerCandidateDetails))
                {
                    correction = Vector3.zero;
                    if (DiagnosticsEnabled)
                        LamborghiniAventadorDiagnostics.AutoParkingInfo(context,
                            $"Aventador centering candidate rejected before movement " +
                            $"vehicle={car.GetInstanceID()}: {centerCandidateDetails}.");
                }
                else if (HasCandidateCollision(car, correction, out var centerCollision))
                {
                    correction = Vector3.zero;
                    if (DiagnosticsEnabled)
                        LamborghiniAventadorDiagnostics.AutoParkingInfo(context,
                            $"Aventador centering candidate rejected before movement " +
                            $"vehicle={car.GetInstanceID()}: collision={centerCollision}.");
                }
                else if (!TryCommitRootPosition(car, newPosition))
                {
                    correction = Vector3.zero;
                    if (DiagnosticsEnabled)
                        LamborghiniAventadorDiagnostics.AutoParkingInfo(context,
                            $"Aventador auto-parking centering correction did not commit " +
                            $"vehicle={car.GetInstanceID()} target={newPosition} " +
                            $"actual={car.transform.position}; no follow-up parking evaluation " +
                            "was run for that correction.");
                }
                else
                {
                    correctionAttempted = true;
                    car.UpdateParkingZone();
                    car.isCheckedForParkingZone = true;
                }
            }
        }
        else if (hudAfterNativeRefresh != ParkingState.Legal &&
                 (probeAfterNativeRefresh.HasHandicapLane || capturedSpot.IsHandicap))
        {
            if (DiagnosticsEnabled)
                LamborghiniAventadorDiagnostics.AutoParkingInfo(context,
                    $"Aventador auto-parking geometry correction refused vehicle={car.GetInstanceID()} " +
                    $"spotHandicap={capturedSpot.IsHandicap}, " +
                    $"wheelHandicap={probeAfterNativeRefresh.HasHandicapLane}; " +
                    "no position/state override applied.");
        }

        var hudAfter = ReadHudState(panel);
        var finalProbes = correctionAttempted
            ? GetParkingWheelState(car) : probeAfterNativeRefresh;
        var rootPosition = car.transform.position;
        var rootToTarget = Vector3.ProjectOnPlane(
            rootPosition - capturedSpot.Position, capturedSpot.ParkingUp).magnitude;
        var finalSaved = car.vehicleInstance.parkingState.ToString();
        var details =
            $"vehicle={car.GetInstanceID()} helper={helper.GetInstanceID()} " +
            $"spot={capturedSpot.Description} targetLane={capturedSpot.LaneName} " +
            $"targetNeighbourhood='{capturedSpot.Neighbourhood}' " +
            $"preNativeGround={(capturedSpot.HasGroundSurface ? capturedSpot.GroundPosition.ToString() : "<unavailable>")} " +
            $"preNativeGroundNormal={capturedSpot.GroundNormal} " +
            $"groundRay=[{capturedSpot.GroundDetails}] " +
            $"targetHandicap={(capturedSpot.HasLane ? capturedSpot.IsHandicap.ToString() : "<unknown>")} " +
            $"nativeSpotMarker={lastAutoParkPosition} markerDistance={nativeMarkerDistance:0.000}m " +
            $"root={rootPosition} rootToSpot={rootToTarget:0.000}m " +
            $"spotPlanarLimit={MaximumAutoParkSpotDistance:0.00}m " +
            $"savedNative={savedAfterNative} savedFinal={finalSaved} " +
            $"hudBeforeRefresh={hudBeforeRefresh?.ToString() ?? "<unavailable>"} " +
            $"hudAfterNativeRefresh={hudAfterNativeRefresh?.ToString() ?? "<unavailable>"} " +
            $"hudAfter={hudAfter?.ToString() ?? "<unavailable>"} " +
            $"centerCorrection={correction} correctionApplied={correctionAttempted} " +
            $"wheelNeighbourhood='{finalProbes.Neighbourhood}' " +
            $"wheels=[{finalProbes.Details}].";

        if (hudAfter == ParkingState.Legal)
            LamborghiniAventadorDiagnostics.AutoParkingInfo(context,
                "Aventador native Auto-Park completed with vanilla Legal result; " + details);
        else
            context?.Logger.Warn(
                "LamborghiniAventador auto-parking: native geometry still evaluates Illegal " +
                "after final native parking evaluation; no ParkingState was forced. " + details);
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsTarget(vehicle) || vehicle.vehicleInstance == null) return;
        var panel = UI.UIs.Instance?.playerHUD?.itemPanelUI?.vehicleInfo;
        var hud = ReadHudState(panel);
        var saved = vehicle.vehicleInstance.parkingState;
        var neighbourhood = vehicle.vehicleInstance.parkingNeighbourhood;
        var message =
            $"Aventador exit parking persistence vehicle={vehicle.GetInstanceID()} " +
            $"saved={saved} savedNeighbourhood='{neighbourhood}' " +
            $"hud={hud?.ToString() ?? "<unavailable>"} " +
            $"position={vehicle.transform.position}.";
        if (saved == ParkingState.Illegal || hud == ParkingState.Illegal)
            context?.Logger.Warn("LamborghiniAventador auto-parking: " + message);
        else if (DiagnosticsEnabled)
            LamborghiniAventadorDiagnostics.AutoParkingInfo(context, message);
    }

    private static ParkingProbeResult GetParkingWheelState(CarController car)
    {
        if (BuildingManager.IsInsideBuilding)
            return new ParkingProbeResult(true, false, string.Empty,
                "building-context: vanilla skips parking-lane wheel tests");

        var checks = new List<string>();
        var wheels = car.vehicleController?.powertrain?.wheels;
        var legal = wheels != null && wheels.Count == 4;
        var hasHandicap = false;
        var neighbourhood = string.Empty;
        if (wheels != null)
        {
            for (var index = 0; index < wheels.Count; index++)
            {
                var wheel = wheels[index]?.wheelUAPI;
                if (wheel == null)
                {
                    legal = false;
                    checks.Add($"{index}:missing-wheel");
                    continue;
                }

                var transform = wheel.transform;
                var probeCenter = transform.position + transform.up * -0.1f;
                // Keep every argument identical to CarController.UpdateParkingZone:
                // same center, half-width, radius-derived height, wheel rotation,
                // layer mask, and Physics.queriesHitTriggers global behavior.
                var hits = Physics.OverlapBox(probeCenter,
                    new Vector3(0.1f, wheel.Radius * 2f, 0.1f),
                    transform.rotation, LayerHelper.parkingAreaLayerMask);
                var first = hits.Length > 0 ? hits[0] : null;
                // Match CarController.UpdateParkingZone: it resolves the first
                // overlap through the collider's parent chain.
                var lane = first != null
                    ? first.GetComponentInParent<ParkingLaneGenerator>() : null;
                var hitHasHandicapLane = hits.Any(hit =>
                {
                    var directLane = hit.GetComponent<ParkingLaneGenerator>();
                    var parentLane = directLane == null
                        ? hit.GetComponentInParent<ParkingLaneGenerator>(true)
                        : null;
                    return directLane?.isHandicapParking == true ||
                           parentLane?.isHandicapParking == true;
                });
                if (hits.Length == 0 || lane == null || lane.isHandicapParking)
                    legal = false;
                hasHandicap |= hitHasHandicapLane;
                if (lane != null)
                {
                    neighbourhood = lane.neighbourhood;
                }

                checks.Add(
                    $"{index}:{transform.name}@{transform.position},probe={probeCenter}," +
                    $"radius={wheel.Radius:0.000},hits={DescribeHits(hits)}," +
                    $"nativeFirstLane={lane?.name ?? "<none>"}," +
                    $"neighbourhood='{lane?.neighbourhood ?? "<none>"}'," +
                    $"handicap={lane?.isHandicapParking.ToString() ?? "<none>"}");
            }
        }

        if (wheels == null || wheels.Count != 4)
            checks.Add($"wheelCount={wheels?.Count ?? 0}; vanilla expects four probes");
        return new ParkingProbeResult(legal, hasHandicap, neighbourhood,
            string.Join("; ", checks));
    }

    private static bool TryGetWheelbaseCenterOffset(CarController car, out Vector3 offset)
    {
        offset = Vector3.zero;
        var wheels = car.vehicleController?.powertrain?.wheels;
        if (wheels == null || wheels.Count != 4) return false;

        var center = Vector3.zero;
        foreach (var component in wheels)
        {
            var wheel = component?.wheelUAPI;
            if (wheel == null) return false;
            center += wheel.transform.position;
        }
        center /= wheels.Count;
        offset = Vector3.ProjectOnPlane(center - car.transform.position, car.transform.up);
        return true;
    }

    private static ParkingSpotSnapshot CaptureSpot(AutoParkSpot spot, VehicleController vehicle)
    {
        var lane = spot.GetComponentInParent<ParkingLaneGenerator>(true);
        var parent = spot.transform.parent;
        var hasGroundSurface = TryFindGroundSurface(spot, vehicle,
            out var groundPosition, out var groundNormal, out var groundDetails);
        var laneColliders = lane == null
            ? "<no-lane>"
            : string.Join("; ", lane.GetComponentsInChildren<Collider>(true).Select(collider =>
                $"{collider.name}(layer={collider.gameObject.layer}:" +
                $"{LayerMask.LayerToName(collider.gameObject.layer)},enabled={collider.enabled}," +
                $"trigger={collider.isTrigger},center={collider.bounds.center}," +
                $"size={collider.bounds.size})"));
        return new ParkingSpotSnapshot(
            spot.transform.position,
            parent != null ? parent.up : Vector3.up,
            lane != null,
            lane != null && lane.isHandicapParking,
            DescribeSpot(spot) + $",nativeRotation={(parent != null ? parent.rotation.eulerAngles.ToString() : "<world>")}",
            lane?.name ?? "<none>",
            lane?.neighbourhood ?? "<none>",
            lane != null ? lane.GetInstanceID() : 0,
            hasGroundSurface, groundPosition, groundNormal,
            $"groundMask={groundDetails}; laneColliders=[{laneColliders}]");
    }

    private static bool TryFindGroundSurface(AutoParkSpot spot, VehicleController vehicle,
        out Vector3 groundPosition, out Vector3 groundNormal, out string details)
    {
        groundPosition = Vector3.zero;
        groundNormal = Vector3.up;
        details = "unavailable";
        LayerMask groundMask;
        try
        {
            if (GroundLayerMaskField?.GetValue(null) is not LayerMask resolvedMask)
            {
                details = "groundLayerMask-unavailable";
                return false;
            }
            groundMask = resolvedMask;
        }
        catch (Exception exception)
        {
            details = $"groundLayerMask-failed:{exception.GetBaseException().GetType().Name}";
            return false;
        }

        var origin = spot.transform.position + Vector3.up * 2f;
        var hits = Physics.RaycastAll(origin, Vector3.down, 2.5f, groundMask,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        var hitDetails = new List<string>();
        foreach (var hit in hits)
        {
            var collider = hit.collider;
            if (collider == null) continue;
            var owner = collider.GetComponentInParent<VehicleController>(true);
            var isVehicleCollider = owner != null || IsVehicleLayer(collider.gameObject.layer);
            var ownedByAutoParkCar = owner == vehicle ||
                                     collider.transform == vehicle.transform ||
                                     collider.transform.IsChildOf(vehicle.transform);
            var upward = Vector3.Dot(hit.normal, Vector3.up);
            hitDetails.Add(
                $"{collider.name}(layer={collider.gameObject.layer}:" +
                $"{LayerMask.LayerToName(collider.gameObject.layer)},trigger={collider.isTrigger}," +
                $"vehicle={isVehicleCollider},owned={ownedByAutoParkCar}," +
                $"point={hit.point},normal={hit.normal},distance={hit.distance:0.000})");
            if (ownedByAutoParkCar || isVehicleCollider || upward < 0.5f)
                continue;

            groundPosition = hit.point;
            groundNormal = hit.normal;
            details = $"mask={groundMask.value},origin={origin},hits=[{string.Join("; ", hitDetails)}]";
            return true;
        }

        details = $"mask={groundMask.value},origin={origin},hits=[{string.Join("; ", hitDetails)}]";
        return false;
    }

    private static bool IsVehicleLayer(int layer)
    {
        return layer == LayerMask.NameToLayer("Vehicles") ||
               layer == LayerMask.NameToLayer("PlayerVehicles") ||
               layer == LayerMask.NameToLayer("ParkedVehicles") ||
               layer == LayerMask.NameToLayer("AiVehicles");
    }

    private static bool TryValidateParkingCandidate(CarController car,
        Vector3 candidateRootPosition, ParkingSpotSnapshot capturedSpot, out string details)
    {
        var wheels = car.vehicleController?.powertrain?.wheels;
        var hitLaneIds = new List<int>(4);
        var neighbourhoods = new List<string>(4);
        var handicaps = new List<bool>(4);
        var hasHits = new List<bool>(4);
        var failures = new List<string>();
        var delta = candidateRootPosition - car.transform.position;
        if (wheels == null || wheels.Count != 4)
        {
            details = $"wheelCount={wheels?.Count ?? 0}; expected four native wheel probes";
            return false;
        }

        if (!capturedSpot.HasLane || capturedSpot.LaneInstanceId == 0 ||
            capturedSpot.IsHandicap)
        {
            details = $"capturedLane={capturedSpot.LaneName}#{capturedSpot.LaneInstanceId}, " +
                      $"handicap={capturedSpot.IsHandicap}; target is not a captured regular lane";
            return false;
        }

        for (var index = 0; index < wheels.Count; index++)
        {
            var wheel = wheels[index]?.wheelUAPI;
            if (wheel == null)
            {
                hasHits.Add(false);
                hitLaneIds.Add(0);
                neighbourhoods.Add(string.Empty);
                handicaps.Add(false);
                failures.Add($"{index}:missing-wheel");
                continue;
            }

            var transform = wheel.transform;
            var probeCenter = transform.position + delta + transform.up * -0.1f;
            var hits = Physics.OverlapBox(probeCenter,
                new Vector3(0.1f, wheel.Radius * 2f, 0.1f),
                transform.rotation, LayerHelper.parkingAreaLayerMask);
            var first = hits.Length > 0 ? hits[0] : null;
            var lane = first != null
                ? first.GetComponentInParent<ParkingLaneGenerator>() : null;
            var laneId = lane != null ? lane.GetInstanceID() : 0;
            var neighbourhood = lane?.neighbourhood ?? string.Empty;
            var handicap = hits.Any(hit =>
            {
                var directLane = hit.GetComponent<ParkingLaneGenerator>();
                var parentLane = directLane == null
                    ? hit.GetComponentInParent<ParkingLaneGenerator>(true)
                    : null;
                return directLane?.isHandicapParking == true ||
                       parentLane?.isHandicapParking == true;
            });
            var hasHit = hits.Length > 0 && lane != null;
            hasHits.Add(hasHit);
            hitLaneIds.Add(laneId);
            neighbourhoods.Add(neighbourhood);
            handicaps.Add(handicap || lane?.isHandicapParking == true);
            if (!hasHit || laneId != capturedSpot.LaneInstanceId ||
                !string.Equals(neighbourhood, capturedSpot.Neighbourhood, StringComparison.Ordinal) ||
                handicap || lane?.isHandicapParking == true)
            {
                failures.Add($"{index}:center={probeCenter},hits={DescribeHits(hits)}," +
                             $"lane={lane?.name ?? "<none>"}#{laneId}," +
                             $"neighbourhood='{neighbourhood}',handicap={handicap}");
            }
        }

        var legal = IsLegalParkingCandidate(hasHits.ToArray(), hitLaneIds.ToArray(),
            neighbourhoods.ToArray(), handicaps.ToArray(), capturedSpot.LaneInstanceId,
            capturedSpot.Neighbourhood, hasCollision: false);
        details = legal
            ? $"four native wheel OBBs overlap captured lane {capturedSpot.LaneName}#" +
              $"{capturedSpot.LaneInstanceId} in '{capturedSpot.Neighbourhood}'"
            : string.Join("; ", failures);
        return legal;
    }

    internal static bool IsLegalParkingCandidate(bool[] hasLaneHit, int[] laneIds,
        string[] neighbourhoods, bool[] hasHandicapHit, int expectedLaneId,
        string expectedNeighbourhood, bool hasCollision)
    {
        if (hasCollision || expectedLaneId == 0 || hasLaneHit == null || laneIds == null ||
            neighbourhoods == null || hasHandicapHit == null || hasLaneHit.Length != 4 ||
            laneIds.Length != 4 || neighbourhoods.Length != 4 || hasHandicapHit.Length != 4)
            return false;

        for (var index = 0; index < 4; index++)
        {
            if (!hasLaneHit[index] || laneIds[index] != expectedLaneId ||
                !string.Equals(neighbourhoods[index], expectedNeighbourhood,
                    StringComparison.Ordinal) || hasHandicapHit[index])
                return false;
        }
        return true;
    }

    internal static bool TryGetGroundedCandidate(Vector3 currentRoot,
        Vector3 groundPosition, Vector3 groundUp, out Vector3 candidate,
        out float heightError)
    {
        heightError = Vector3.Dot(currentRoot - groundPosition, groundUp);
        candidate = currentRoot - groundUp * heightError;
        return Mathf.Abs(heightError) > MaximumNativeSpotPositionError;
    }

    private static bool HasCandidateCollision(CarController vehicle, Vector3 delta,
        out string details)
    {
        details = string.Empty;
        var body = vehicle.vehicleCollider;
        if (body == null || !body.enabled || body.isTrigger)
        {
            details = "native solid vehicleCollider unavailable";
            return true;
        }

        var bounds = body.bounds;
        var candidateCenter = bounds.center + delta;
        var candidates = Physics.OverlapBox(candidateCenter, bounds.extents,
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        foreach (var other in candidates)
        {
            if (other == null || other == body || other.isTrigger ||
                other.transform == vehicle.transform || other.transform.IsChildOf(vehicle.transform) ||
                (body.attachedRigidbody != null &&
                 other.attachedRigidbody == body.attachedRigidbody))
                continue;

            if (Physics.ComputePenetration(body, body.transform.position + delta,
                    body.transform.rotation, other, other.transform.position,
                    other.transform.rotation, out var direction, out var distance) && distance > 0.001f)
            {
                details = $"{other.name}(layer={other.gameObject.layer}:" +
                          $"{LayerMask.LayerToName(other.gameObject.layer)}," +
                          $"penetration={distance:0.000}m,direction={direction})";
                return true;
            }
        }
        return false;
    }

    private static bool TryCommitRootPosition(CarController vehicle, Vector3 target)
    {
        var root = vehicle.transform;
        var rotation = root.rotation;
        var rigidbody = vehicle.vehicleController?.vehicleRigidbody;
        var bodyLocalPosition = rigidbody != null
            ? root.InverseTransformPoint(rigidbody.transform.position)
            : Vector3.zero;
        var bodyLocalRotation = rigidbody != null
            ? Quaternion.Inverse(root.rotation) * rigidbody.transform.rotation
            : Quaternion.identity;

        // Keep the game's teleport reset semantics, then write both representations
        // of the vehicle pose in the same synchronous operation. The native helper
        // writes the Rigidbody, while this mod's root transform can otherwise retain
        // the pre-teleport pose until Unity's next physics synchronization.
        VehicleHelper.TeleportVehicle(vehicle, target, rotation);
        root.SetPositionAndRotation(target, rotation);
        if (rigidbody != null)
        {
            rigidbody.position = root.TransformPoint(bodyLocalPosition);
            rigidbody.rotation = root.rotation * bodyLocalRotation;
        }
        Physics.SyncTransforms();
        return Vector3.Distance(root.position, target) <= MaximumNativeSpotPositionError &&
               (rigidbody == null || Vector3.Distance(rigidbody.position,
                   root.TransformPoint(bodyLocalPosition)) <= MaximumNativeSpotPositionError);
    }

    private static string DescribeSpot(AutoParkSpot? spot)
    {
        if (spot == null) return "<none>";
        var box = spot.boxCollider;
        return $"'{spot.name}'@{spot.transform.position}/rot={spot.transform.rotation.eulerAngles}," +
               $"maxLength={spot.maxVehicleLength:0.000}," +
               $"boxCenter={box?.bounds.center.ToString() ?? "<none>"}," +
               $"boxSize={box?.bounds.size.ToString() ?? "<none>"}";
    }

    private static string DescribeHits(Collider[] hits)
    {
        if (hits.Length == 0) return "<none>";
        return string.Join(",", hits.Select(hit =>
        {
            var direct = hit.GetComponent<ParkingLaneGenerator>();
            var parent = direct == null ? hit.GetComponentInParent<ParkingLaneGenerator>(true) : null;
            return $"{hit.name}(layer={hit.gameObject.layer},trigger={hit.isTrigger}," +
                   $"directLane={direct?.name ?? "<none>"}," +
                   $"parentLane={parent?.name ?? "<none>"})";
        }));
    }

    private static ParkingState? ReadHudState(UI.ItemPanel.VehicleInfoPanel? panel) =>
        panel != null && HudParkingStateField?.GetValue(panel) is ParkingState state
            ? state : null;
}
