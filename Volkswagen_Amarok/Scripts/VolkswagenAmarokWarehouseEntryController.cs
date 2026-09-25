#nullable enable

using UnityEngine;
using BAModAPI;

/// <summary>
/// Invokes the native warehouse-entry path if the physical garage door is
/// reached before the vehicle-layer trigger can receive the Amarok collider.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
internal sealed class VolkswagenAmarokWarehouseEntryController : MonoBehaviour
{
    private const float RetryDelaySeconds = .5f;

    private VehicleController? vehicle;
    private ModContext? context;
    private BoxCollider? approachTrigger;
    private DriveInEntrance? suppressedEntrance;
    private float nextAttemptTime;
    private float nextSuppressionLogTime;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        EnsureApproachTrigger();
    }

    private void EnsureApproachTrigger()
    {
        if (approachTrigger != null)
            return;

        var vehicleLayer = gameObject.layer;
        foreach (var child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name != "BodyCollider")
                continue;

            vehicleLayer = child.gameObject.layer;
            break;
        }

        var triggerObject = new GameObject("VolkswagenAmarok_WarehouseApproachTrigger")
        {
            hideFlags = HideFlags.DontSave,
            layer = vehicleLayer,
        };
        triggerObject.transform.SetParent(transform, false);
        approachTrigger = triggerObject.AddComponent<BoxCollider>();
        approachTrigger.isTrigger = true;
        // Reach the closed door slightly ahead of the bumper boxes from
        // either driving direction. This is event-driven, not polling.
        approachTrigger.center = new Vector3(0f, 0.60f, 0f);
        approachTrigger.size = new Vector3(1.78f, 1.10f, 6.20f);
    }

    internal void SuppressEntrance(DriveInEntrance entrance, string reason)
    {
        if (entrance == null)
            return;

        suppressedEntrance = entrance;
        VolkswagenAmarokDiagnostics.WarehouseExitInfo(
            context,
            $"VolkswagenAmarok warehouse-entry: suppressing physical-door fallback for " +
            $"entrance='{entrance.name}', reason={reason}.");
    }

    internal void ClearSuppressedEntrance(DriveInEntrance? entrance, string reason)
    {
        if (suppressedEntrance == null ||
            (entrance != null && suppressedEntrance != entrance))
        {
            return;
        }

        VolkswagenAmarokDiagnostics.WarehouseExitInfo(
            context,
            $"VolkswagenAmarok warehouse-entry: restoring physical-door fallback for " +
            $"entrance='{suppressedEntrance.name}', reason={reason}.");
        suppressedEntrance = null;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (vehicle == null || !vehicle.controlledByPlayer ||
            Time.unscaledTime < nextAttemptTime || collision.collider == null)
        {
            return;
        }

        var entrance = collision.collider.GetComponentInParent<DriveInEntrance>();
        if (entrance == null)
            return;

        if (entrance == suppressedEntrance)
        {
            LogSuppressedCollision(entrance, collision.collider, collision.relativeVelocity);
            return;
        }

        var car = vehicle as CarController ?? vehicle.GetComponent<CarController>();
        if (car == null || InstanceBehavior<GameManager>.Instance?.selectedVehicle != car)
            return;

        VolkswagenAmarokDiagnostics.WarehouseExitInfo(
            context,
            $"VolkswagenAmarok warehouse-entry: physical door collision invokes native entry; " +
            $"instance={vehicle.GetInstanceID()}, entrance='{entrance.name}', " +
            $"vehiclePosition={vehicle.transform.position}, collider='{collision.collider.name}', " +
            $"relativeVelocity={collision.relativeVelocity}.");
        nextAttemptTime = Time.unscaledTime + RetryDelaySeconds;
        entrance.TryToEnterWithCar(car);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (vehicle == null || !vehicle.controlledByPlayer || other == null)
            return;

        var entrance = other.GetComponentInParent<DriveInEntrance>();
        if (entrance == null)
            return;

        if (entrance == suppressedEntrance)
        {
            if (Time.unscaledTime >= nextSuppressionLogTime)
            {
                nextSuppressionLogTime = Time.unscaledTime + RetryDelaySeconds;
                VolkswagenAmarokDiagnostics.WarehouseExitInfo(
                    context,
                    $"VolkswagenAmarok warehouse-entry: approach trigger suppressed during warehouse " +
                    $"exit; instance={vehicle.GetInstanceID()}, entrance='{entrance.name}', " +
                    $"vehiclePosition={vehicle.transform.position}, collider='{other.name}'.");
            }
            return;
        }

        if (Time.unscaledTime < nextAttemptTime)
            return;

        var car = vehicle as CarController ?? vehicle.GetComponent<CarController>();
        if (car == null || InstanceBehavior<GameManager>.Instance?.selectedVehicle != car)
            return;

        VolkswagenAmarokDiagnostics.WarehouseExitInfo(
            context,
            $"VolkswagenAmarok warehouse-entry: approach trigger invokes native entry; " +
            $"instance={vehicle.GetInstanceID()}, entrance='{entrance.name}', " +
            $"collider='{other.name}', vehiclePosition={vehicle.transform.position}.");
        nextAttemptTime = Time.unscaledTime + RetryDelaySeconds;
        entrance.TryToEnterWithCar(car);
    }

    private void LogSuppressedCollision(
        DriveInEntrance entrance,
        Collider collider,
        Vector3 relativeVelocity)
    {
        if (Time.unscaledTime < nextSuppressionLogTime)
            return;

        nextSuppressionLogTime = Time.unscaledTime + RetryDelaySeconds;
        VolkswagenAmarokDiagnostics.WarehouseExitInfo(
            context,
            $"VolkswagenAmarok warehouse-entry: physical-door fallback suppressed during " +
            $"warehouse exit; instance={vehicle!.GetInstanceID()}, entrance='{entrance.name}', " +
            $"vehiclePosition={vehicle.transform.position}, collider='{collider.name}', " +
            $"relativeVelocity={relativeVelocity}.");
    }

    private void OnDisable()
    {
        nextAttemptTime = 0f;
        nextSuppressionLogTime = 0f;
        suppressedEntrance = null;
    }
}
