#nullable enable
using System.Collections.Generic;
using BAModAPI;
using BusinessLayoutSets;
using NWH.VehiclePhysics2.Damage;
using UnityEngine;

// Observe contacts independently of the visual-deformation cooldown. Native NWH
// damage accepts only the first Enter in 0.8 seconds and measures its impulse;
// a splitter/bridge scrape can consume that entry before the main impact.
[DisallowMultipleComponent]
internal sealed class KoenigseggJeskoImpactDamageController : MonoBehaviour
{
    private const float Tolerance = 0.0001f;
    private readonly HashSet<Collider> impactColliders = new HashSet<Collider>();
    private VehicleController? vehicle;
    private DamageHandler? handler;
    private Rigidbody? body;
    private ModContext? context;
    private float preStepDamage;
    private float lastObservedDamage;
    private float windowEnd = -1f;
    private float windowBaseline;
    private float strongestImpact;
    private bool pending;
    private int correctionLogs;
    private string contactName = "";
    private float contactSpeed;
    private float contactImpulse;
    private bool warehouseDamageRestorePending;
    private float warehouseDamageBaseline;
    private string warehouseContactName = "";
    private int collisionDebugEvents;
    private int playerDebugEvents;
    private int groundDebugEvents;

    internal void Initialize(VehicleController owner, DamageHandler damage, ModContext? modContext)
    {
        vehicle = owner;
        handler = damage;
        body = owner.GetComponent<Rigidbody>();
        context = modContext;
        preStepDamage = lastObservedDamage = damage.Damage;
        ClearImpact();
        LogColliderLayout();
    }

    private bool CanApplyDamage => vehicle?.vehicleInstance != null &&
        handler != null && handler.isActiveAndEnabled && !handler.visualOnly &&
        handler.damageIntensity > 0f && body != null && !body.isKinematic &&
        !(SaveGameManager.Current != null && SaveGameManager.Current.gameVariables.disableVehicleDamage);

    private void FixedUpdate()
    {
        if (handler == null) return;
        // A repair or reload must discard the old impact target immediately.
        if (!CanApplyDamage || handler.Damage + Tolerance < lastObservedDamage)
            ClearImpact();
        preStepDamage = lastObservedDamage = handler.Damage;
    }

    private void OnCollisionEnter(Collision collision)
    {
        LogCollision(collision);
        Observe(collision, true);
    }
    private void OnCollisionStay(Collision collision) => Observe(collision, false);
    private void OnDisable()
    {
        RestoreWarehouseEntranceDamage();
        ClearImpact();
    }

    private void Observe(Collision collision, bool entering)
    {
        if (!CanApplyDamage || collision.contactCount == 0)
            return;
        if (collision.collider.GetComponentInParent<DriveInEntrance>() != null)
        {
            // The Jesko's splitter can touch the physical warehouse entrance
            // before its native entry trigger finishes the transition. Undo
            // only damage from that exact entrance-owned contact; all other
            // collision damage remains authoritative.
            warehouseDamageBaseline = warehouseDamageRestorePending
                ? Mathf.Min(warehouseDamageBaseline, preStepDamage)
                : Mathf.Min(preStepDamage, handler!.Damage);
            warehouseContactName = collision.collider.name;
            warehouseDamageRestorePending = true;
            ClearImpact();
            return;
        }
        if (!DamageHandler.IsCollisionValid(collision)) return;
        foreach (var tag in handler!.collisionIgnoreTags)
            if (collision.collider.CompareTag(tag)) return;

        var now = Time.realtimeSinceStartup;
        if (now > windowEnd)
        {
            // Do not turn resting interpenetration or sustained pushing into
            // repeated damage. Stay can only complete a real Enter's short window.
            if (!entering) return;
            ClearImpact();
            windowBaseline = Mathf.Min(preStepDamage, handler.Damage);
            windowEnd = now + handler.collisionTimeout;
        }
        if (entering) impactColliders.Add(collision.collider);
        else if (!impactColliders.Contains(collision.collider)) return;

        var closingSpeed = 0f;
        for (var i = 0; i < collision.contactCount; i++)
            closingSpeed = Mathf.Max(closingSpeed,
                ClosingSpeed(collision.relativeVelocity, collision.GetContact(i).normal));
        var other = collision.rigidbody;
        var expected = ImpactDamage(closingSpeed, collision.impulse.magnitude,
            body!.mass, other != null && !other.isKinematic ? other.mass : 0f,
            Time.fixedDeltaTime, handler.damageIntensity, handler.decelerationThreshold / 100f);
        if (expected <= strongestImpact) return;
        strongestImpact = expected;
        pending = true;
        contactName = collision.collider.name;
        contactSpeed = closingSpeed;
        contactImpulse = collision.impulse.magnitude;
    }

    private void LateUpdate()
    {
        RestoreWarehouseEntranceDamage();
        if (!pending || handler == null) return;
        pending = false;
        if (!CanApplyDamage || handler.Damage + Tolerance < lastObservedDamage)
        {
            ClearImpact();
            return;
        }
        // Native callbacks have now run. Credit ALL damage they already applied,
        // regardless of callback order, and never add the whole impact again.
        var current = handler.Damage;
        var target = ReconcileDamage(current, windowBaseline, strongestImpact);
        if (target > current + Tolerance)
        {
            if (vehicle is CarController car) car.SetDamage(target);
            else
            {
                handler.SetDamage(target);
                vehicle!.vehicleInstance.damage = target;
            }
            GlobalEvents.onVehicleVariablesChanged?.Invoke();
            if (correctionLogs++ < 6)
                context?.Logger.Warn($"KoenigseggJesko impact damage recovered vehicle={vehicle!.GetInstanceID()} " +
                    $"contact='{contactName}' closingSpeed={contactSpeed:0.00}mps impulse={contactImpulse:0.0}Ns " +
                    $"baseline={windowBaseline:0.0000} native={current:0.0000} corrected={target:0.0000}; " +
                    "strongest contact in native cooldown, existing damage credited.");
        }
        lastObservedDamage = handler.Damage;
    }

    private void RestoreWarehouseEntranceDamage()
    {
        if (!warehouseDamageRestorePending || handler == null || vehicle == null)
            return;

        warehouseDamageRestorePending = false;
        var observedDamage = handler.Damage;
        if (observedDamage <= warehouseDamageBaseline + Tolerance)
            return;

        if (vehicle is CarController car)
            car.SetDamage(warehouseDamageBaseline);
        else
        {
            handler.SetDamage(warehouseDamageBaseline);
            if (vehicle.vehicleInstance != null)
                vehicle.vehicleInstance.damage = warehouseDamageBaseline;
        }

        lastObservedDamage = handler.Damage;
        GlobalEvents.onVehicleVariablesChanged?.Invoke();
        KoenigseggJeskoDiagnostics.WarehouseInfo(
            context,
            $"KoenigseggJesko warehouse-entry: ignored entrance contact damage " +
            $"vehicle={vehicle.GetInstanceID()}, collider='{warehouseContactName}', " +
            $"observed={observedDamage:0.0000}, restored={warehouseDamageBaseline:0.0000}.");
    }

    // Same native impulse-to-condition scale and the existing 0.8 intensity.
    // When overlap recovery yields a tiny solver impulse, use normal closing
    // speed and reduced mass as a conservative, inelastic impact estimate.
    // Tangential scraping and separating motion do not contribute to that floor.
    internal static float ClosingSpeed(Vector3 relativeVelocity, Vector3 normal) =>
        // Unity's Collision reports other-relative-to-self velocity: approach
        // projects positively onto the receiving collider's contact normal.
        Mathf.Max(0f, Vector3.Dot(relativeVelocity, normal.normalized));

    internal static float ImpactDamage(float closingSpeed, float impulse, float mass,
        float otherDynamicMass, float fixedStep, float intensity, float threshold)
    {
        if (mass <= 0f || fixedStep <= 0f || intensity <= 0f) return 0f;
        var impulseDeltaV = Mathf.Max(0f, impulse) / mass;
        if (closingSpeed < threshold && impulseDeltaV < threshold) return 0f;
        var massFraction = otherDynamicMass > 0f ? otherDynamicMass / (mass + otherDynamicMass) : 1f;
        var deltaV = Mathf.Max(impulseDeltaV, Mathf.Max(0f, closingSpeed) * massFraction);
        return Mathf.Clamp01(deltaV / (fixedStep * 10f) * Mathf.Clamp(intensity, 0f, 0.99f) * 0.005f);
    }

    internal static float ReconcileDamage(float current, float baseline, float strongest) =>
        Mathf.Clamp01(Mathf.Max(current, baseline + strongest));

    private void ClearImpact()
    {
        pending = false;
        windowEnd = -1f;
        strongestImpact = 0f;
        impactColliders.Clear();
    }

    private void LogColliderLayout()
    {
        if (!KoenigseggJeskoDiagnostics.DebugEnabled ||
            !KoenigseggJeskoDiagnostics.CollisionDebugEnabled || vehicle == null)
            return;

        var root = vehicle.transform;
        var renderFront = float.NegativeInfinity;
        var renderFrontSource = "none";
        var ignoredRenderers = 0;
        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled) continue;
            var bounds = renderer.bounds;
            // Some helpers have enormous renderer bounds unrelated to bodywork.
            // Keep the comparison within the car's plausible local footprint.
            var rendererFront = float.NegativeInfinity;
            var rendererRear = float.PositiveInfinity;
            for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                    for (var z = -1; z <= 1; z += 2)
                    {
                        var localZ = root.InverseTransformPoint(
                            bounds.center + Vector3.Scale(bounds.extents,
                                new Vector3(x, y, z))).z;
                        rendererFront = Mathf.Max(rendererFront, localZ);
                        rendererRear = Mathf.Min(rendererRear, localZ);
                    }
            if (rendererFront > 10f || rendererRear < -10f)
            {
                ignoredRenderers++;
                continue;
            }
            if (rendererFront <= renderFront) continue;
            renderFront = rendererFront;
            renderFrontSource = renderer.name;
        }

        KoenigseggJeskoDiagnostics.CollisionInfo(context,
            $"KoenigseggJesko collision layout vehicle={vehicle.GetInstanceID()} " +
            $"visualFrontLocalZ={renderFront:0.000} visualSource='{renderFrontSource}' " +
            $"ignoredVisualRenderers={ignoredRenderers} rootScale={root.lossyScale}.");
        foreach (var collider in vehicle.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger) continue;
            var localCenter = root.InverseTransformPoint(collider.bounds.center);
            var box = collider as BoxCollider;
            KoenigseggJeskoDiagnostics.CollisionInfo(context,
                $"KoenigseggJesko collider vehicle={vehicle.GetInstanceID()} " +
                $"id={collider.GetInstanceID()} name='{collider.name}' type={collider.GetType().Name} " +
                $"localCenter={localCenter} worldSize={collider.bounds.size} " +
                $"boxCenter={(box != null ? box.center.ToString() : "n/a")} " +
                $"boxSize={(box != null ? box.size.ToString() : "n/a")}.");
        }
    }

    private void LogCollision(Collision collision)
    {
        if (!KoenigseggJeskoDiagnostics.DebugEnabled ||
            !KoenigseggJeskoDiagnostics.CollisionDebugEnabled ||
            vehicle == null || collision.contactCount == 0)
            return;

        var contact = collision.GetContact(0);
        if (collision.collider.name == "Player")
        {
            if (playerDebugEvents++ >= 12) return;
        }
        else if (collision.collider.name == "GroundPlane")
        {
            if (groundDebugEvents++ >= 4) return;
        }
        else if (collisionDebugEvents++ >= 24)
            return;
        var localPoint = vehicle.transform.InverseTransformPoint(contact.point);
        KoenigseggJeskoDiagnostics.CollisionInfo(context,
            $"KoenigseggJesko collision enter vehicle={vehicle.GetInstanceID()} " +
            $"self='{contact.thisCollider?.name}' selfId={contact.thisCollider?.GetInstanceID()} " +
            $"other='{contact.otherCollider?.name}' " +
            $"otherLayer={collision.collider.gameObject.layer} " +
            $"localPoint={localPoint} normal={contact.normal} " +
            $"relativeSpeed={collision.relativeVelocity.magnitude:0.00}mps " +
            $"impulse={collision.impulse.magnitude:0.0}Ns " +
            $"contacts={collision.contactCount}.");
    }
}
