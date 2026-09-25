#nullable enable
using System;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Items;
using Buildings.Indoors.InteriorDesign;
using CameraControllers;
using GamePrompt.Runtime.Scripts;
using Helpers;
using UnityEngine;

// Build 3675 can run the Steam valuation goal in GameManager.Awake before
// initialization-scope mods register saved vehicle types. The exception aborts
// Awake; registering the types later does not execute its remaining statements.
// Resume only that verified tail, once, after the game's load event.
internal static class VolkswagenAmarokLoadRecovery
{
    private static readonly MethodInfo? ExitBuildingMethod = typeof(GameManager).GetMethod(
        "OnExitBuilding", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? RefreshAchievementsMethod = typeof(GameManager).GetMethod(
        "ForceUpdateAchievementsOnSteam", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? PersonalGoalsField = typeof(GameManager).GetField(
        "personalGoals", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? IndoorPlacementCameraField = typeof(GameManager).GetField(
        "indoorPlacementCamera", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static GameManager? attemptedManager;
    private static GameManager? recoveredManager;

    internal static bool CompleteInterruptedLoad(ModContext? context)
    {
        var manager = InstanceBehavior<GameManager>.Instance;
        var save = SaveGameManager.Current;
        if (manager == null || save?.privateDriverVehicleInstances == null || context == null ||
            ReferenceEquals(attemptedManager, manager))
            return false;

        var hasAmarok = false;
        foreach (var vehicle in save.privateDriverVehicleInstances)
            hasAmarok |= vehicle != null && string.Equals(vehicle.vehicleTypeName,
                VolkswagenAmarokMod.VehicleTypeName, StringComparison.Ordinal);
        if (!hasAmarok)
            return false;

        attemptedManager = manager;
        var exitRegistered = HasNativeExitCallback(manager);
        VolkswagenAmarokDiagnostics.LoadRecoveryInfo(context, $"VolkswagenAmarok: chauffeur save startup check: " +
            $"managerEnabled={manager.enabled}, inputInitialized={InputHelper.IsInitialized()}, " +
            $"nativeAwakeTail={exitRegistered}, interiorReady={InteriorDesignerHelper.TimeOfDayController != null}, " +
            $"playerRecords={save.VehicleInstances?.Count ?? 0}, chauffeurRecords={save.privateDriverVehicleInstances.Count}.");
        if (exitRegistered)
            return false;

        // Multiple sentinels keep this from replaying an earlier, unrelated Awake
        // failure or overwriting a successfully initialized city.
        var indoorPlacementCamera = IndoorPlacementCameraField?.GetValue(manager) as Component;
        if (ExitBuildingMethod == null || RefreshAchievementsMethod == null ||
            PersonalGoalsField?.GetValue(manager) == null ||
            InteriorDesignerHelper.TimeOfDayController != null ||
            indoorPlacementCamera == null || manager.timeOfDayController == null ||
            !manager.gameObject.activeInHierarchy || save.VehicleInstances == null)
        {
            context.Logger.Warn("VolkswagenAmarok: startup recovery skipped: native initialization state differs from the supported interrupted-Awake path.");
            return false;
        }

        foreach (var vehicle in save.VehicleInstances)
        {
            if (vehicle == null || vehicle.VehicleType == null)
            {
                context.Logger.Warn($"VolkswagenAmarok: startup recovery stopped: player vehicle type still unresolved: '{vehicle?.vehicleTypeName ?? "<null record>"}'.");
                return false;
            }
        }
        foreach (var vehicle in save.privateDriverVehicleInstances)
        {
            if (vehicle == null || vehicle.VehicleType == null)
            {
                context.Logger.Warn($"VolkswagenAmarok: startup recovery stopped: chauffeur vehicle type still unresolved: '{vehicle?.vehicleTypeName ?? "<null record>"}'.");
                return false;
            }
        }

        try
        {
            var exitCallback = (Action<Address>)Delegate.CreateDelegate(
                typeof(Action<Address>), manager, ExitBuildingMethod);
            var placement = indoorPlacementCamera.GetComponentInParent<PlacementCam>();
            if (placement == null)
                throw new InvalidOperationException("Native placement camera is unavailable.");

            // Let the real valuation run with the now-resolved types. No fabricated
            // vehicle prices, removed save records, or swallowed valuation errors.
            RefreshAchievementsMethod.Invoke(manager, null);
            if (manager.ForceLOD0)
            {
                foreach (var group in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
                    group.ForceLOD(0);
            }
            GlobalEvents.onExitBuilding += exitCallback;
            GamePromptManager.StartCollecting();
            InteriorDesignerHelper.Init(manager.timeOfDayController, placement, false);
            TutorialHelper.Init();

            // Unity disables a MonoBehaviour whose Awake throws. Completing its
            // fields alone leaves Update (clock, Escape and mouse interaction)
            // stopped. Re-enable only after the guarded recovery fully succeeds.
            // Unity runs any pending Start itself; never invoke Start manually or
            // replay Awake, which would duplicate native initialization/listeners.
            var wasDisabled = !manager.enabled;
            manager.enabled = true;
            recoveredManager = manager;
            context.Logger.Warn($"VolkswagenAmarok: recovered interrupted city startup after vehicle registration; " +
                $"managerWasDisabled={wasDisabled}, managerEnabled={manager.enabled}. Native Start/Update can resume.");
            return true;
        }
        catch (Exception exception)
        {
            var failure = exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException : exception;
            context.Logger.Warn($"VolkswagenAmarok: interrupted-load recovery failed: {failure}");
            return false;
        }
    }

    internal static void ReportInputState(ModContext? context)
    {
        if (context == null || recoveredManager == null ||
            recoveredManager != InstanceBehavior<GameManager>.Instance)
            return;
        if (!recoveredManager.isActiveAndEnabled || !InputHelper.IsInitialized())
            context.Logger.Warn($"VolkswagenAmarok: recovered city is not ready for input: " +
                $"managerActive={recoveredManager.isActiveAndEnabled}, inputInitialized={InputHelper.IsInitialized()}.");
    }

    private static bool HasNativeExitCallback(GameManager manager)
    {
        if (ExitBuildingMethod == null)
            return false;
        var callbacks = GlobalEvents.onExitBuilding?.GetInvocationList();
        if (callbacks == null)
            return false;
        foreach (var callback in callbacks)
            if (ReferenceEquals(callback.Target, manager) && callback.Method == ExitBuildingMethod)
                return true;
        return false;
    }
}
