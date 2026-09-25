#nullable enable

using BAModAPI;
using BusinessLayoutSets;
using UnityEngine;

/// <summary>
/// Starts native warehouse entry just before the Jesko's low front reaches the
/// physical door. A narrow centerline probe identifies the exact entrance, and
/// a bounded contact retry covers a native request rejected during the initial
/// physics bounce. It cannot span or select an adjacent garage door.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
internal sealed class KoenigseggJeskoWarehouseEntryController : MonoBehaviour
{
    private const float RetryDelaySeconds = 0.25f;
    private const float ContactWindowSeconds = 2.5f;
    private const float EntranceSelectionRadius = 8f;
    private const int MaximumAttemptsPerContact = 8;
    private static readonly Vector3 FrontProbePosition = new Vector3(0f, 0.55f, 2.48f);
    private static readonly Vector3 FrontProbeSize = new Vector3(0.40f, 0.75f, 0.65f);

    private VehicleController? vehicle;
    private ModContext? context;
    private BoxCollider? frontProbe;
    private DriveInEntrance? suppressedEntrance;
    private bool fallbackSuppressed;
    private DriveInEntrance? contactEntrance;
    private DriveInEntrance? acceptedEntrance;
    private float contactWindowEnd;
    private float acceptedUntil;
    private int contactAttempts;
    private float nextAttemptTime;
    private float nextSuppressionLogTime;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        EnsureFrontProbe();
    }

    private void EnsureFrontProbe()
    {
        if (frontProbe != null)
            return;

        var vehicleLayer = gameObject.layer;
        foreach (var child in GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(child.name, "BodyCollider", System.StringComparison.Ordinal))
                continue;
            vehicleLayer = child.gameObject.layer;
            break;
        }

        var probeObject = new GameObject("KoenigseggJesko_WarehouseFrontProbe")
        {
            hideFlags = HideFlags.DontSave,
            layer = vehicleLayer,
        };
        probeObject.transform.SetParent(transform, false);
        probeObject.transform.localPosition = FrontProbePosition;
        frontProbe = probeObject.AddComponent<BoxCollider>();
        frontProbe.isTrigger = true;
        frontProbe.size = FrontProbeSize;
    }

    internal void SuppressEntrance(DriveInEntrance entrance, string reason)
    {
        if (entrance == null)
            return;

        suppressedEntrance = entrance;
        fallbackSuppressed = true;
        ResetContact();
        KoenigseggJeskoDiagnostics.WarehouseInfo(
            context,
            $"KoenigseggJesko warehouse-entry: suppressing exact-door fallback for " +
            $"entrance='{entrance.name}', reason={reason}.");
    }

    internal void ClearSuppressedEntrance(DriveInEntrance? entrance, string reason)
    {
        if (suppressedEntrance == null ||
            (entrance != null && suppressedEntrance != entrance))
        {
            return;
        }

        KoenigseggJeskoDiagnostics.WarehouseInfo(
            context,
            $"KoenigseggJesko warehouse-entry: restoring exact-door fallback for " +
            $"entrance='{suppressedEntrance.name}', reason={reason}.");
        suppressedEntrance = null;
        fallbackSuppressed = false;
        ResetContact();
    }

    private void OnTriggerEnter(Collider other) => ObserveTrigger(other, "trigger-enter");
    private void OnTriggerStay(Collider other) => ObserveTrigger(other, "trigger-stay");

    private void ObserveTrigger(Collider other, string source)
    {
        if (other == null)
            return;
        var entrance = other.GetComponentInParent<DriveInEntrance>();
        if (entrance != null)
            TryEnter(entrance, other.name, source, Vector3.zero);
    }

    private void OnCollisionEnter(Collision collision) => ObserveCollision(collision, "collision-enter");
    private void OnCollisionStay(Collision collision) => ObserveCollision(collision, "collision-stay");

    private void ObserveCollision(Collision collision, string source)
    {
        if (vehicle == null || !vehicle.controlledByPlayer ||
            collision == null || collision.collider == null)
        {
            return;
        }

        var entrance = collision.collider.GetComponentInParent<DriveInEntrance>();
        if (entrance != null)
            TryEnter(
                entrance,
                collision.collider.name,
                source,
                collision.relativeVelocity);
    }

    private void TryEnter(
        DriveInEntrance entrance,
        string colliderName,
        string source,
        Vector3 relativeVelocity)
    {
        if (vehicle == null || !vehicle.controlledByPlayer)
            return;

        if (fallbackSuppressed)
        {
            if (Time.unscaledTime >= nextSuppressionLogTime)
            {
                nextSuppressionLogTime = Time.unscaledTime + RetryDelaySeconds;
                KoenigseggJeskoDiagnostics.WarehouseInfo(
                    context,
                    $"KoenigseggJesko warehouse-entry: fallback suppressed for all doors during " +
                    $"exit; vehicle={vehicle.GetInstanceID()}, guardedEntrance=" +
                    $"'{suppressedEntrance?.name ?? "unknown"}', touchedEntrance='{entrance.name}', " +
                    $"collider='{colliderName}', source={source}.");
            }
            return;
        }

        var now = Time.unscaledTime;
        if (acceptedEntrance != null && now < acceptedUntil)
            return;
        var touchedEntrance = entrance;
        entrance = FindAlignedEntrance(out var alignmentDistance) ?? entrance;
        if (contactEntrance != entrance || now > contactWindowEnd)
        {
            contactEntrance = entrance;
            contactWindowEnd = now + ContactWindowSeconds;
            contactAttempts = 0;
            nextAttemptTime = 0f;
        }
        if (now < nextAttemptTime || contactAttempts >= MaximumAttemptsPerContact)
            return;

        var car = vehicle as CarController ?? vehicle.GetComponent<CarController>();
        if (car == null || InstanceBehavior<GameManager>.Instance?.selectedVehicle != car)
            return;

        contactAttempts++;
        nextAttemptTime = now + RetryDelaySeconds;
        var accepted = entrance.TryToEnterWithCar(car);
        KoenigseggJeskoDiagnostics.WarehouseInfo(
            context,
            $"KoenigseggJesko warehouse-entry: exact entrance invokes native entry; " +
            $"vehicle={vehicle.GetInstanceID()}, touchedEntrance='{touchedEntrance.name}', " +
            $"selectedEntrance='{entrance.name}', alignmentDistance={alignmentDistance:0.00}m, " +
            $"collider='{colliderName}', source={source}, attempt={contactAttempts}, " +
            $"accepted={accepted}, relativeVelocity={relativeVelocity}.");
        if (accepted)
        {
            acceptedEntrance = entrance;
            acceptedUntil = now + 5f;
            contactAttempts = MaximumAttemptsPerContact;
        }
    }

    private DriveInEntrance? FindAlignedEntrance(out float distance)
    {
        var probePosition = frontProbe != null
            ? frontProbe.bounds.center
            : transform.TransformPoint(FrontProbePosition);
        DriveInEntrance? nearest = null;
        var nearestDistanceSquared = EntranceSelectionRadius * EntranceSelectionRadius;
        foreach (var entrance in FindObjectsOfType<DriveInEntrance>(true))
        {
            if (entrance == null)
                continue;
            var distanceSquared = (entrance.transform.position - probePosition).sqrMagnitude;
            if (distanceSquared >= nearestDistanceSquared)
                continue;
            nearest = entrance;
            nearestDistanceSquared = distanceSquared;
        }

        distance = nearest == null
            ? float.PositiveInfinity
            : Mathf.Sqrt(nearestDistanceSquared);
        return nearest;
    }

    private void ResetContact()
    {
        contactEntrance = null;
        contactWindowEnd = 0f;
        contactAttempts = 0;
        nextAttemptTime = 0f;
        acceptedEntrance = null;
        acceptedUntil = 0f;
    }

    private void OnDisable()
    {
        ResetContact();
        nextSuppressionLogTime = 0f;
        suppressedEntrance = null;
        fallbackSuppressed = false;
    }
}
