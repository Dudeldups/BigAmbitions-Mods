#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;

// Configure before entry/washing, including Awake before vehicleInstance exists.
internal static class BMWM4G82NativeActivities
{
    private const string DonorPath = "Vehicles/PlayerVehicles/HonzaMimic";
    private static ModContext? context;
    private static bool failureLogged;
    private static VehicleController? cachedDonor;
    private static readonly List<(CarController Car, GameObject Effect)> ownedEffects =
        new List<(CarController, GameObject)>();
    private static readonly List<BMWM4G82ActivityTrace> ownedTraces =
        new List<BMWM4G82ActivityTrace>();

    internal static void Initialize(ModContext modContext)
    {
        context = modContext;
        failureLogged = false;
        var marker = Path.Combine(Application.persistentDataPath,
            "ModsLocal", "BMW_M4_G82", "Config", "native-activities-diagnostics.enabled");
        if (File.Exists(marker))
        {
            BMWM4G82Diagnostics.DebugEnabled = true;
            BMWM4G82Diagnostics.NativeActivitiesDebugEnabled = true;
            Info("opt-in diagnostics enabled.");
        }
    }

    internal static void Shutdown()
    {
        var washVfxField = FindField(typeof(CarController), "washVfx");
        foreach (var entry in ownedEffects)
        {
            if (entry.Car != null && ReferenceEquals(washVfxField?.GetValue(entry.Car), entry.Effect))
                washVfxField?.SetValue(entry.Car, null);
            if (entry.Effect != null)
                UnityEngine.Object.Destroy(entry.Effect);
        }
        ownedEffects.Clear();
        foreach (var trace in ownedTraces)
            if (trace != null) UnityEngine.Object.Destroy(trace);
        ownedTraces.Clear();
        cachedDonor = null;
        context = null;
        BMWM4G82Diagnostics.NativeActivitiesDebugEnabled = false;
        failureLogged = false;
    }

    internal static bool Configure(VehicleController vehicle, bool preparePrefab = false)
    {
        try
        {
            if (cachedDonor == null)
            {
                var donorObject = PrefabHelper.LoadPrefabAssetByName(DonorPath);
                cachedDonor = donorObject == null ? null :
                    donorObject.GetComponent<VehicleController>() ??
                    donorObject.GetComponentInChildren<VehicleController>(true);
            }
            var donor = cachedDonor;
            if (donor == null)
                throw new InvalidOperationException("native HonzaMimic donor is unavailable.");

            var environment = vehicle.sleepEnvironment;
            var configField = FindField(environment.GetType(), "config");
            var config = configField?.GetValue(environment) as UnityEngine.Object;
            if (config == null)
            {
                var donorConfig = configField?.GetValue(donor.sleepEnvironment) as UnityEngine.Object;
                var type = donorConfig == null ? null :
                    FindField(donorConfig.GetType(), "sleepEnvironmentType")?.GetValue(donorConfig);
                if (donorConfig == null || type == null || Convert.ToInt32(type) != 1)
                    throw new InvalidOperationException("native donor has no Car sleep configuration.");
                configField!.SetValue(environment, donorConfig);
                vehicle.sleepEnvironment = environment;
                config = donorConfig;
                Info($"sleep configuration assigned vehicle={vehicle.GetInstanceID()} donor=HonzaMimic.");
            }

            if (preparePrefab)
                return true;

            if (vehicle is CarController car)
            {
                // Current native washing dereferences washVfx after SetFreeze(true).
                // Supply the missing dependency; native code still owns all blockers.
                var washVfxField = FindField(typeof(CarController), "washVfx");
                if (washVfxField == null)
                    throw new MissingFieldException("CarController.washVfx");
                if (!(washVfxField.GetValue(car) is GameObject currentEffect) || currentEffect == null)
                {
                    var donorEffect = washVfxField.GetValue(donor) as GameObject;
                    if (donorEffect == null)
                        throw new InvalidOperationException("native donor has no wash VFX.");
                    var effect = UnityEngine.Object.Instantiate(donorEffect, car.transform, false);
                    effect.transform.localPosition = donor.transform.InverseTransformPoint(donorEffect.transform.position);
                    effect.transform.localRotation = Quaternion.Inverse(donor.transform.rotation) * donorEffect.transform.rotation;
                    effect.name = "BMWM4G82_NativeWashVfx";
                    effect.SetActive(false);
                    effect.hideFlags = HideFlags.DontSave;
                    washVfxField.SetValue(car, effect);
                    ownedEffects.Add((car, effect));
                    Info($"wash VFX assigned vehicle={car.GetInstanceID()} donor=HonzaMimic.");
                }
                if (!HasReference(car, "washAudioSource") || !HasReference(car, "washSoundLoop") ||
                    !HasReference(car, "washSoundEnd"))
                    throw new InvalidOperationException("wash audio references are incomplete.");
                if (BMWM4G82Diagnostics.DebugEnabled &&
                    BMWM4G82Diagnostics.NativeActivitiesDebugEnabled &&
                    car.GetComponent<BMWM4G82ActivityTrace>() == null)
                    ownedTraces.Add(car.gameObject.AddComponent<BMWM4G82ActivityTrace>());
            }
            return config != null;
        }
        catch (Exception exception)
        {
            if (!failureLogged)
            {
                failureLogged = true;
                context?.Logger.Warn(
                    $"BMWM4G82 native activities unavailable vehicle={vehicle.GetInstanceID()}: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
            return false;
        }
    }

    internal static bool HasSleepConfig(VehicleController vehicle) =>
        FindField(vehicle.sleepEnvironment.GetType(), "config")?.GetValue(vehicle.sleepEnvironment)
            is UnityEngine.Object value && value != null;

    internal static bool HasReference(CarController car, string field) =>
        FindField(typeof(CarController), field)?.GetValue(car) is UnityEngine.Object value && value != null;

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) return field;
        }
        return null;
    }

    internal static void Info(string message)
    {
        if (BMWM4G82Diagnostics.DebugEnabled && BMWM4G82Diagnostics.NativeActivitiesDebugEnabled)
            context?.Logger.Info("BMWM4G82 native activities: " + message);
    }
}

// Purely observational, opt-in, per-instance state-change trace.
public sealed class BMWM4G82ActivityTrace : MonoBehaviour
{
    private IEnumerator Start()
    {
        var car = GetComponent<CarController>();
        var body = GetComponent<Rigidbody>();
        var physics = GetComponent<NWH.VehiclePhysics2.VehicleController>();
        string previous = string.Empty;
        while (car != null && BMWM4G82Diagnostics.DebugEnabled &&
               BMWM4G82Diagnostics.NativeActivitiesDebugEnabled)
        {
            var state = $"controlled={car.controlledByPlayer} washing={car.isWashing} " +
                        $"kinematic={body?.isKinematic} constraints={body?.constraints} " +
                        $"stateSettings={physics?.stateSettings != null} " +
                        $"running={physics?.powertrain.engine.IsRunning} " +
                        $"sleep={BMWM4G82NativeActivities.HasSleepConfig(car)} washVfx={BMWM4G82NativeActivities.HasReference(car, "washVfx")}";
            if (!string.Equals(previous, state, StringComparison.Ordinal))
            {
                previous = state;
                BMWM4G82NativeActivities.Info($"vehicle={car.GetInstanceID()} {state}");
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }
}