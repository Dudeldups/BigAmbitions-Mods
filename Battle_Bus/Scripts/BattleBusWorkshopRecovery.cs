#nullable enable
using System;
using System.Linq;
using BAModAPI;
using Helpers;
using UnityEngine;

[DefaultExecutionOrder(20000)]
[DisallowMultipleComponent]
public sealed class BattleBusWorkshopRecovery : MonoBehaviour
{
    private Rigidbody? body;
    private VehicleController? vehicle;
    private NWH.VehiclePhysics2.VehicleController? physics;
    private BattleBusFlightController? flight;
    private BoxCollider? chassis;
    private ModContext? context;
    private Vector3 previous;
    private bool sampled;
    private bool guarding;
    private float guardUntil;
    private float stopUntil;
    private RigidbodyConstraints savedConstraints;
    internal void Initialize(ModContext? value) => context = value;

    private void Awake()
    {
        body = GetComponent<Rigidbody>(); vehicle = GetComponent<VehicleController>();
        physics = GetComponent<NWH.VehiclePhysics2.VehicleController>();
        flight = GetComponent<BattleBusFlightController>();
        foreach (var box in GetComponentsInChildren<BoxCollider>(true))
            if (box.enabled && !box.isTrigger && box.name == "BodyCollider") { chassis = box; break; }
        previous = transform.position;
    }

    private void FixedUpdate() => UpdateRecovery();
    private void LateUpdate() => UpdateRecovery();

    private void UpdateRecovery()
    {
        if (body == null || vehicle == null || physics == null || flight == null || chassis == null) return;
        // Observe only this instance's discontinuous relocation. Scene queries run
        // once at a teleport destination, never as a global discovery poll.
        var distance = Vector3.Distance(previous, body.position);
        if (sampled && !guarding && distance > Mathf.Max(2f, body.velocity.magnitude * Time.deltaTime + 1f))
        {
            var station = Physics.OverlapSphere(body.position, 8f, ~0, QueryTriggerInteraction.Collide)
                .Select(hit => hit.GetComponentInParent<GasStationTrigger>())
                .Where(s => s != null && s.stationCollider != null)
                .Distinct().OrderBy(s => s.stationCollider.bounds.SqrDistance(body.position))
                .ThenByDescending(s => s.isRepairStation).FirstOrDefault();
            if (station != null) Recover(station);

        }
        sampled = true; previous = body.position;
        if (!guarding) return;
        if (Time.unscaledTime >= guardUntil) { Release(); return; }
        physics.input.Throttle = 0f; physics.input.Brakes = 1f; physics.input.Steering = 0f;
        if (!body.isKinematic)
        {
            body.angularVelocity = Vector3.zero;
            if (Time.unscaledTime < stopUntil) body.velocity = Vector3.zero;
        }
    }

    private void Recover(GasStationTrigger station)
    {
        var start = body!.position;
        var upright = Quaternion.Euler(0f, body.rotation.eulerAngles.y, 0f);
        var found = TryPlacement(station, start, upright, out var destination);
        if (!found)
        {
            // Never leave momentum driving the bus deeper into the failed bay.
            if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            physics!.input.Throttle = 0f; physics.input.Brakes = 1f;
            Debug.LogWarning($"Battle Bus workshop recovery: no clear supported repair placement near {start}; movement stopped.");
            return;
        }
        body.position = destination; body.rotation = upright;
        if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        Physics.SyncTransforms();
        foreach (var wheel in physics!.powertrain.wheels)
            if (wheel.wheelUAPI is NWH.WheelController3D.WheelController controller) controller.ResetSimulationState();
        flight!.BeginWorkshopRecovery(2f);
        savedConstraints = body.constraints;
        body.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        guardUntil = Time.unscaledTime + 2f; stopUntil = Time.unscaledTime + .35f; guarding = true;
        vehicle!.SavePosition();
        if (BattleBusDiagnostics.RecoveryDebugEnabled)
            BattleBusDiagnostics.Info(context, $"Battle Bus workshop recovery vehicle={GetInstanceID()} from={start} to={destination}; settled with brakes for 2s.");
    }

    private bool TryPlacement(GasStationTrigger station, Vector3 origin, Quaternion rotation, out Vector3 destination)
    {
        destination = origin;
        if (station.stationCollider == null) return false;
        var center = transform.InverseTransformPoint(chassis!.transform.TransformPoint(chassis.center));
        var pivot = transform.InverseTransformPoint(chassis.transform.position);
        var localRotation = Quaternion.Inverse(transform.rotation) * chassis.transform.rotation;
        var scale = chassis.transform.lossyScale;
        var half = Vector3.Scale(chassis.size, new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z))) * .5f;
        var axisX = rotation * Vector3.right; var axisZ = rotation * Vector3.forward;
        var worldHalf = new Vector3(Mathf.Abs(axisX.x)*half.x + Mathf.Abs(axisZ.x)*half.z,
            half.y, Mathf.Abs(axisX.z)*half.x + Mathf.Abs(axisZ.z)*half.z);
        var supportRejected = 0; var zoneRejected = 0; var blocked = 0; var blocker = "none";
        foreach (var lateral in new[] { 0f, -.5f, .5f, -1f, 1f })
        for (var step = 0; step <= 48; step++)
        {
            var candidate = origin - axisZ * (step*.25f) + axisX*lateral;
            if (!flight!.TryWorkshopGround(candidate, rotation, out candidate)) { supportRejected++; continue; }
            if (!station.stationCollider.bounds.Intersects(new Bounds(candidate + rotation*center, worldHalf*2f)))
            { zoneRejected++; continue; }
            var clear = true;
            foreach (var obstacle in Physics.OverlapBox(candidate + rotation*center,
                         half + new Vector3(.04f,0f,.08f), rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (obstacle.attachedRigidbody == body || obstacle.transform.IsChildOf(transform)) continue;
                // Confirm penetration against the actual collider, not just its
                // broad-phase bounds (large workshop floor/wall meshes overlap).
                if (!Physics.ComputePenetration(chassis, candidate + rotation*pivot, rotation*localRotation,
                        obstacle, obstacle.transform.position, obstacle.transform.rotation, out var direction, out var depth)
                    || depth <= .005f) continue;
                clear = false; blocked++; blocker = obstacle.name; break;
            }
            if (clear) { destination = candidate; return true; }
        }
        if (BattleBusDiagnostics.RecoveryDebugEnabled)
            BattleBusDiagnostics.Info(context, $"Battle Bus bay rejected={station.name} ground={supportRejected} zone={zoneRejected} blocked={blocked} lastCollider={blocker}.");
        return false;
    }

    private void Release()
    {
        if (guarding && body != null)
        {
            var owned = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.constraints = (body.constraints & ~owned) | (savedConstraints & owned);
        }
        guarding = false;
    }
    private void OnDisable() { Release(); sampled = false; }
}
