#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Extensions;
using Helpers;
using UI;
using UnityEngine;

internal sealed class BigfootMonsterTruckParkingController : MonoBehaviour
{
    private const float EvaluationInterval = 0.2f;
    private const float FootprintTolerance = 0.25f;
    private const float MaximumAlignmentAngle = 20f;

    private static readonly BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? SpotDistanceField =
        typeof(ParkingLaneGenerator).GetField("parkingSpotDistance", InstanceFields);
    private static readonly FieldInfo? SideBySideField =
        typeof(ParkingLaneGenerator).GetField("sideBySideParkingSpots", InstanceFields);
    private static readonly FieldInfo? SideBySideAngleField =
        typeof(ParkingLaneGenerator).GetField("sideBySideAngle", InstanceFields);

    private readonly List<Transform> wheels = new(4);
    private VehicleController? vehicle;
    private ModContext? context;
    private float nextEvaluationTime;
    private bool evaluatedOnce;
    private bool validTwoSpaceParking;
    private string parkingNeighbourhood = string.Empty;
    private bool failureReported;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        foreach (var child in controller.GetComponentsInChildren<Transform>(true))
            if (child.name.EndsWith("_WheelController", StringComparison.Ordinal))
                wheels.Add(child);
    }

    private void LateUpdate()
    {
        if (vehicle == null || Time.unscaledTime < nextEvaluationTime ||
            (evaluatedOnce && !vehicle.controlledByPlayer))
            return;

        nextEvaluationTime = Time.unscaledTime + EvaluationInterval;
        evaluatedOnce = true;
        try
        {
            validTwoSpaceParking = TryFindTwoSpaceParking(out parkingNeighbourhood);
            if (vehicle.controlledByPlayer && validTwoSpaceParking)
                ApplyLegalParkingToHud();
            else if (!vehicle.controlledByPlayer && validTwoSpaceParking)
                ApplyLegalParkingToVehicle();
        }
        catch (Exception exception)
        {
            ReportFailureOnce("evaluation", exception);
        }
    }

    public void HandleVehicleEntered()
    {
        validTwoSpaceParking = false;
        parkingNeighbourhood = string.Empty;
        evaluatedOnce = false;
        nextEvaluationTime = 0f;
    }

    public void HandleVehicleExited()
    {
        if (vehicle == null)
            return;
        try
        {
            validTwoSpaceParking = TryFindTwoSpaceParking(out parkingNeighbourhood);
            evaluatedOnce = true;
            if (!validTwoSpaceParking)
                return;
            ApplyLegalParkingToVehicle();
        }
        catch (Exception exception)
        {
            ReportFailureOnce("vehicle-exit", exception);
        }
    }

    public void AddSecondSpaceHourlyFee()
    {
        if (vehicle?.vehicleInstance == null || vehicle.controlledByPlayer ||
            !validTwoSpaceParking || vehicle.vehicleInstance.parkingState != ParkingState.Legal)
            return;
        try
        {
            var fee = NeighborhoodHelper.GetData(parkingNeighbourhood).parkingPrice;
            vehicle.vehicleInstance.unpaidParkingAmount += fee;
        }
        catch (Exception exception)
        {
            ReportFailureOnce("hourly-fee", exception);
        }
    }

    private bool TryFindTwoSpaceParking(out string neighbourhood)
    {
        neighbourhood = string.Empty;
        if (vehicle == null || wheels.Count != 4)
            return false;

        var seen = new HashSet<ParkingLaneGenerator>();
        var nearby = Physics.OverlapBox(
            vehicle.transform.position + Vector3.up,
            new Vector3(2.25f, 2.5f, 3.25f),
            vehicle.transform.rotation,
            LayerHelper.parkingAreaLayerMask,
            QueryTriggerInteraction.Collide);
        foreach (var overlap in nearby)
        {
            var lane = overlap.GetComponentInParent<ParkingLaneGenerator>();
            if (lane == null || !seen.Add(lane) || lane.isHandicapParking ||
                SideBySideField?.GetValue(lane) is not bool sideBySide || !sideBySide ||
                SpotDistanceField?.GetValue(lane) is not float spotDistance || spotDistance <= 0f ||
                SideBySideAngleField?.GetValue(lane) is not float sideBySideAngle)
                continue;

            var laneCollider = lane.GetComponent<BoxCollider>();
            if (laneCollider == null)
                continue;
            var expectedForward = lane.transform.rotation *
                                  Quaternion.Euler(0f, sideBySideAngle, 0f) * Vector3.forward;
            if (Mathf.Abs(Vector3.Dot(vehicle.transform.forward, expectedForward)) <
                Mathf.Cos(MaximumAlignmentAngle * Mathf.Deg2Rad))
                continue;

            var spotCount = Mathf.FloorToInt(laneCollider.size.x / spotDistance);
            var occupiedSpots = new HashSet<int>();
            var allInside = true;
            foreach (var wheel in wheels)
            {
                var local = lane.transform.InverseTransformPoint(wheel.position);
                var minimumX = laneCollider.center.x - laneCollider.size.x * 0.5f;
                var maximumX = laneCollider.center.x + laneCollider.size.x * 0.5f;
                var minimumZ = laneCollider.center.z - laneCollider.size.z * 0.5f;
                var maximumZ = laneCollider.center.z + laneCollider.size.z * 0.5f;
                if (local.x < minimumX - FootprintTolerance || local.x > maximumX + FootprintTolerance ||
                    local.z < minimumZ - FootprintTolerance || local.z > maximumZ + FootprintTolerance)
                {
                    allInside = false;
                    break;
                }

                var spot = Mathf.FloorToInt((maximumX - local.x) / spotDistance);
                if (spot < 0 || spot >= spotCount)
                {
                    allInside = false;
                    break;
                }
                occupiedSpots.Add(spot);
            }

            if (!allInside || occupiedSpots.Count != 2)
                continue;
            var minimumSpot = int.MaxValue;
            var maximumSpot = int.MinValue;
            foreach (var spot in occupiedSpots)
            {
                minimumSpot = Mathf.Min(minimumSpot, spot);
                maximumSpot = Mathf.Max(maximumSpot, spot);
            }
            if (maximumSpot - minimumSpot != 1)
                continue;

            neighbourhood = lane.neighbourhood;
            return !string.IsNullOrWhiteSpace(neighbourhood);
        }
        return false;
    }

    private void ApplyLegalParkingToHud()
    {
        var panel = InstanceBehavior<UIs>.Instance?.playerHUD?.itemPanelUI?.vehicleInfo;
        if (panel == null)
            return;
        panel.SetParkingZone(ParkingState.Legal, parkingNeighbourhood);

        var parkingValue = panel.GetType().GetField("parkingZoneValue", InstanceFields)?.GetValue(panel);
        var arguments = parkingValue?.GetType().GetProperty("Arguments", InstanceFields);
        var fee = NeighborhoodHelper.GetData(parkingNeighbourhood).parkingPrice * 2f;
        if (arguments?.CanWrite == true && fee > 0f)
            arguments.SetValue(parkingValue, new { price = fee.ToShortCurrencyFormat(false) });
    }

    private void ApplyLegalParkingToVehicle()
    {
        if (vehicle?.vehicleInstance == null)
            return;
        vehicle.vehicleInstance.parkingState = ParkingState.Legal;
        vehicle.vehicleInstance.parkingNeighbourhood = parkingNeighbourhood;
        if (vehicle.poi != null)
            vehicle.poi.SetBackground(InstanceBehavior<GlobalReferences>.Instance.vehiclePOIBackgroundColor);
    }

    private void ReportFailureOnce(string operation, Exception exception)
    {
        if (failureReported)
            return;
        failureReported = true;
        context?.Logger.Warn(
            $"BigfootMonsterTruck: two-space parking {operation} failed: " +
            $"{exception.GetType().Name}: {exception.Message}");
    }
}
