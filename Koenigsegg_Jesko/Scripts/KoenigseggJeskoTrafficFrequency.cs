#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;

[AddComponentMenu("")]
internal sealed class KoenigseggJeskoTrafficFrequency : MonoBehaviour
{
    private const int TargetSpawnsPerCar = 200;
    private static readonly BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo? TrafficVehiclesField =
        typeof(TrafficManager).GetField("trafficVehicles", PrivateInstance);
    private static readonly FieldInfo? IdleVehiclesField =
        typeof(TrafficVehicles).GetField("idleVehicles", PrivateInstance);

    private readonly List<VehicleComponent> ownedIdleVehicles = new List<VehicleComponent>(2);
    private ModContext? context;
    private Coroutine? rebalanceCoroutine;
    private bool missingFieldReported;
    private bool firstBalanceLogged;
    private int ownSpawns;
    private int totalSpawns;

    internal void Initialize(ModContext modContext)
    {
        context = modContext;
        ScheduleRebalance();
    }

    private void OnEnable()
    {
        DensityEvents.onVehicleAdded += HandleVehicleAdded;
        AIEvents.onVehicleChangedState += HandleVehicleChangedState;
        ScheduleRebalance();
    }

    private void OnDisable()
    {
        LogSummary("disabled");
        DensityEvents.onVehicleAdded -= HandleVehicleAdded;
        AIEvents.onVehicleChangedState -= HandleVehicleChangedState;
        if (rebalanceCoroutine != null)
            StopCoroutine(rebalanceCoroutine);
        rebalanceCoroutine = null;
    }

    private void ScheduleRebalance()
    {
        if (rebalanceCoroutine == null && isActiveAndEnabled)
            rebalanceCoroutine = StartCoroutine(RebalanceAfterTrafficUpdate());
    }

    private IEnumerator RebalanceAfterTrafficUpdate()
    {
        // Returned cars enter the idle list after the state-change event.
        yield return null;
        for (var frame = 0; frame < 120 && !TrafficManager.IsInitialized; frame++)
            yield return null;
        if (TrafficManager.IsInitialized)
            RebalanceIdleVehicles();
        rebalanceCoroutine = null;
    }

    private void HandleVehicleAdded(int vehicleIndex)
    {
        if (TryGetIdleVehicles(out _, out var trafficVehicles))
        {
            var all = trafficVehicles.GetVehicleList();
            if (vehicleIndex >= 0 && vehicleIndex < all.Count)
            {
                totalSpawns++;
                var prefab = KoenigseggJeskoPrivateDriverSupport.TrafficPrefab;
                if (prefab != null && all[vehicleIndex]?.prefab == prefab)
                    ownSpawns++;
                if (totalSpawns % 100 == 0)
                    LogSummary("periodic");
            }
        }

        // TrafficManager spawns its initial wave before IsInitialized becomes true.
        RebalanceIdleVehicles();
        ScheduleRebalance();
    }

    private void LogSummary(string reason)
    {
        if (totalSpawns == 0 || !KoenigseggJeskoDiagnostics.DebugEnabled ||
            !KoenigseggJeskoDiagnostics.NpcTrafficDebugEnabled)
            return;
        context?.Logger.Info(
            "KoenigseggJesko traffic frequency: " +
            $"reason={reason} own={ownSpawns} total={totalSpawns} " +
            $"share={100d * ownSpawns / totalSpawns:0.00}% " +
            $"target={100d / TargetSpawnsPerCar:0.00}%.");
    }

    private void HandleVehicleChangedState(
        int vehicleIndex, Collider vehicleCollider, SpecialDriveActionTypes action)
    {
        if ((int)action == 10000)
            ScheduleRebalance();
    }

    private bool TryGetIdleVehicles(
        out List<VehicleComponent>? idle, out TrafficVehicles trafficVehicles)
    {
        idle = null;
        trafficVehicles = null!;
        var manager = TrafficManager.Instance;
        if (manager == null)
            return false;

        if (TrafficVehiclesField?.GetValue(manager) is TrafficVehicles vehicles &&
            IdleVehiclesField?.GetValue(vehicles) is List<VehicleComponent> list)
        {
            trafficVehicles = vehicles;
            idle = list;
            return true;
        }

        if (!missingFieldReported)
        {
            missingFieldReported = true;
            context?.Logger.Warn(
                "KoenigseggJesko traffic frequency: idle list unavailable; NPC frequency unchanged.");
        }
        return false;
    }

    private void RebalanceIdleVehicles()
    {
        if (!TryGetIdleVehicles(out var idle, out _) || idle == null)
            return;
        var prefab = KoenigseggJeskoPrivateDriverSupport.TrafficPrefab;
        if (prefab == null)
            return;

        ownedIdleVehicles.Clear();
        for (var index = idle.Count - 1; index >= 0; index--)
        {
            var vehicle = idle[index];
            if (vehicle == null || vehicle.prefab != prefab)
                continue;
            ownedIdleVehicles.Insert(0, vehicle);
            idle.RemoveAt(index);
        }
        if (ownedIdleVehicles.Count == 0)
            return;

        // Gley picks the first idle car in a randomly selected vehicle group.
        // Compare only this car's observed spawns with all traffic spawns.
        var due = (long)ownSpawns * TargetSpawnsPerCar <= totalSpawns;
        if (due)
        {
            idle.Insert(0, ownedIdleVehicles[0]);
            for (var index = 1; index < ownedIdleVehicles.Count; index++)
                idle.Add(ownedIdleVehicles[index]);
        }
        else
        {
            idle.AddRange(ownedIdleVehicles);
        }

        if (!firstBalanceLogged && KoenigseggJeskoDiagnostics.DebugEnabled &&
            KoenigseggJeskoDiagnostics.NpcTrafficDebugEnabled)
        {
            firstBalanceLogged = true;
            context?.Logger.Info(
                "KoenigseggJesko traffic frequency: " +
                $"idle={ownedIdleVehicles.Count} due={due} " +
                $"own={ownSpawns} total={totalSpawns} target=1/{TargetSpawnsPerCar}.");
        }
    }
}
