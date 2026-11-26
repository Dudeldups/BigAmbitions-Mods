#nullable enable
using System;
using System.Reflection;
using BAModAPI;
using UnityEngine;

[AddComponentMenu("")]
[DisallowMultipleComponent]
internal sealed class LamborghiniAventadorWarehouseEntryController : MonoBehaviour
{
    private const string WarehouseType = "ba:buildingtype_warehouse";
    private const BindingFlags DoorFields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo? ClosedDoorField = typeof(DriveInEntrance).GetField("garageDoorClosedCollider", DoorFields);
    private static readonly FieldInfo? BuildingField = typeof(DriveInEntrance).GetField("cityBuildingController", DoorFields);
    private static readonly FieldInfo? EntryTriggerField = typeof(DriveInEntranceEnterTrigger).GetField("driveInEntrance", DoorFields);

    private CarController? car;
    private NWH.VehiclePhysics2.VehicleController? physicsVehicle;
    private BoxCollider? rearProbe;
    private bool diagnostics;
    private int lastAttemptFrame = -1;

    internal void Initialize(CarController controller, NWH.VehiclePhysics2.VehicleController physics,
        ModContext modContext, bool enableDiagnostics)
    {
        car = controller;
        physicsVehicle = physics;
        diagnostics = enableDiagnostics;
        if (ClosedDoorField == null || BuildingField == null || EntryTriggerField == null)
            modContext.Logger.Warn("LamborghiniAventador warehouse reverse entry: native entrance fields are unavailable.");

        var body = transform.Find("CarHolder/Colliders/BodyCollider");
        if (body == null)
        {
            modContext.Logger.Warn("LamborghiniAventador warehouse reverse entry: native body collider is unavailable.");
            return;
        }

        // Same vehicle layer as the fitted body so the native door collider
        // can overlap the probe. The stock entry trigger ignores this tag.
        var holder = new GameObject("AventadorWarehouseRearProbe")
        {
            hideFlags = HideFlags.DontSave,
            layer = body.gameObject.layer,
            tag = "AiVehicleHalt"
        };
        holder.transform.SetParent(transform, false);
        rearProbe = holder.AddComponent<BoxCollider>();
        rearProbe.isTrigger = true;
        rearProbe.center = new Vector3(0f, .48f, -2.54f);
        rearProbe.size = new Vector3(.42f, .68f, .70f);
    }

    internal void SetOccupied(bool occupied)
    {
        if (rearProbe != null)
            rearProbe.enabled = occupied;
    }

    private void OnDestroy()
    {
        if (rearProbe != null)
            Destroy(rearProbe.gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        // This Rigidbody also receives events from its stock colliders.
        if (rearProbe != null && rearProbe.enabled && other != null && rearProbe.bounds.Intersects(other.bounds))
            TryEnter(other, null, "rear-probe");
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.contactCount > 0)
            TryEnter(collision.collider, collision.GetContact(0).point, "solid-contact");
    }

    private void TryEnter(Collider other, Vector3? contact, string source)
    {
        var context = LamborghiniAventadorVehicleRuntime.SpawnContext;
        var gear = physicsVehicle?.powertrain.transmission.Gear ?? 0;
        if (context == null || car == null || gear >= 0 || !car.controlledByPlayer ||
            lastAttemptFrame == Time.frameCount || GameManager.Instance?.selectedVehicle != car)
            return;

        var manager = BuildingManager.Instance;
        if (manager == null || BuildingManager.IsInsideBuilding || manager.enteringBuilding || manager.exitingBuilding)
            return;

        var entrance = FindEntrance(other);
        if (entrance == null || !entrance.isActiveAndEnabled || !IsWarehouse(entrance) ||
            ClosedDoorField?.GetValue(entrance) is not BoxCollider barrier ||
            !IsReverseApproach(gear, car.transform, barrier) ||
            (contact.HasValue && Vector3.Dot(contact.Value - car.transform.position, car.transform.forward) >= 0f))
            return;

        lastAttemptFrame = Time.frameCount;
        var accepted = entrance.TryToEnterWithCar(car);
        if (diagnostics && LamborghiniAventadorDiagnostics.DebugEnabled)
            context.Logger.Info($"LamborghiniAventador warehouse reverse entry vehicle={car.GetInstanceID()} " +
                $"door={entrance.doorID} collider='{other.name}' source={source} accepted={accepted} " +
                $"vehiclePosition={car.transform.position} gatePosition={barrier.transform.TransformPoint(barrier.center)}.");
    }

    private static bool IsWarehouse(DriveInEntrance entrance) =>
        BuildingField?.GetValue(entrance) is CityBuildingController city &&
        string.Equals(city.building?.BuildingType, WarehouseType, StringComparison.Ordinal);

    internal static DriveInEntrance? FindEntrance(Collider collider)
    {
        if (collider == null)
            return null;

        if (collider.TryGetComponent<DriveInEntranceEnterTrigger>(out var trigger))
            return EntryTriggerField?.GetValue(trigger) as DriveInEntrance;

        var entrance = collider.GetComponentInParent<DriveInEntrance>(true);
        return entrance != null && ClosedDoorField?.GetValue(entrance) as Collider == collider
            ? entrance : null;
    }

    internal static bool IsReverseApproach(int gear, Transform vehicle, BoxCollider barrier)
    {
        // The warehouse pivot can be several metres outside the physical gate.
        // Use the interior face of the serialized barrier, even while it is open.
        var insideFace = barrier.transform.TransformPoint(barrier.center - Vector3.forward * (barrier.size.z * .5f));
        var outward = barrier.transform.forward;
        return gear < 0 &&
            Vector3.Dot(vehicle.forward, outward) > 0f &&
            Vector3.Dot(vehicle.position - insideFace, outward) > 0f;
    }

    internal static bool IsEntranceBarrier(Collider collider) =>
        collider != null && !collider.isTrigger && FindEntrance(collider) is { } entrance && IsWarehouse(entrance);

    // The same impact can still be delivered after native proximity opens the
    // gate. The serialized collider references remain valid when disabled.
    internal static bool FilterEntranceDamage(Collision __0, ref bool __result)
    {
        if (__0 == null || __0.contactCount == 0 || !IsEntranceBarrier(__0.collider))
            return true;
        var owner = __0.GetContact(0).thisCollider?.GetComponentInParent<VehicleController>();
        if (!IsAventador(owner?.vehicleInstance?.vehicleTypeName ?? owner?.vehicleType?.vehicleTypeName))
            return true;
        __result = false;
        return false;
    }

    private static bool IsAventador(string? type) =>
        string.Equals(type, LamborghiniAventadorMod.VehicleTypeName, StringComparison.Ordinal);
}
