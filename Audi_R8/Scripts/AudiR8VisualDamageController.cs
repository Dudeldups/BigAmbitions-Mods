#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using BAModAPI;
using BusinessLayoutSets;
using NWH.VehiclePhysics2.Damage;
using UnityEngine;

[AddComponentMenu("")]
public sealed class AudiR8VisualDamageController : MonoBehaviour
{
    private const float MaximumDisplacement = .20f;
    private const int VertexBudgetPerFrame = 4096;
    private const double MillisecondBudget = 2d;
    private const int MaximumPendingImpacts = 4;
    internal static float RegionScale(bool end, bool front) => end ? (front ? .80f : .90f) : 1f;
    private sealed class Surface
    {
        internal MeshFilter Filter = null!;
        internal Mesh Mesh = null!;
        internal Vector3[] Original = null!, Working = null!;
        internal int[] Indices = null!;
        internal Matrix4x4 ToVehicle, FromVehicle;
        internal Bounds SafeBounds, VehicleBounds;
    }
    private readonly struct Contact
    {
        internal readonly Vector3 Point, Direction;
        internal readonly bool End, Front;
        internal Contact(Vector3 point, Vector3 direction, bool end, bool front)
        { Point = point; Direction = direction; End = end; Front = front; }
    }
    private sealed class Impact
    {
        internal Contact[] Contacts = null!;
        internal float Depth, Closing;
        internal string Other = "";
    }
    private readonly List<Surface> surfaces = new();
    private readonly Queue<Impact> pending = new();
    private readonly List<VehicleDeformationController.VehicleDeformation> savedImpacts = new();
    private VehicleController? vehicle;
    private DamageHandler? handler;
    private ModContext? context;
    private Coroutine? worker, repair, saveRecords;
    private float threshold, nextImpactAt, previousDamage;
    private bool initialized, failureReported;

    internal void Initialize(VehicleController controller, DamageHandler damageHandler,
        ModContext? modContext, IReadOnlyList<MeshFilter> filters, float thresholdMps)
    {
        if (initialized) return;
        vehicle = controller; handler = damageHandler; context = modContext;
        threshold = thresholdMps; previousDamage = damageHandler.Damage;
        foreach (var filter in filters)
        {
            if (filter == null || filter.sharedMesh == null) continue;
            var toVehicle = controller.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var baseline = filter.sharedMesh.vertices;
            var restricted = RestrictToExteriorEnds(filter);
            var used = new HashSet<int>();
            foreach (var index in filter.sharedMesh.triangles)
                if (!restricted || IsExteriorEnd(toVehicle.MultiplyPoint3x4(baseline[index]))) used.Add(index);
            if (used.Count == 0) continue;
            var indices = new int[used.Count]; used.CopyTo(indices); Array.Sort(indices);
            var mesh = Instantiate(filter.sharedMesh); mesh.name += "_RuntimeDamage"; mesh.MarkDynamic();
            var bounds = mesh.bounds; bounds.Expand(.45f);
            var vehicleBounds = new Bounds(toVehicle.MultiplyPoint3x4(baseline[indices[0]]), Vector3.zero);
            foreach (var index in indices) vehicleBounds.Encapsulate(toVehicle.MultiplyPoint3x4(baseline[index]));
            vehicleBounds.Expand(MaximumDisplacement * 2f);
            surfaces.Add(new Surface { Filter = filter, Mesh = mesh, Original = baseline,
                Working = (Vector3[])baseline.Clone(), Indices = indices, ToVehicle = toVehicle,
                FromVehicle = toVehicle.inverse, SafeBounds = bounds, VehicleBounds = vehicleBounds });
            filter.sharedMesh = mesh; mesh.bounds = bounds;
            AudiR8Diagnostics.DamageInfo(context,
                $"AudiR8 damage ownership vehicle={controller.GetInstanceID()} mesh='{filter.name}' eligible={indices.Length}/{baseline.Length} restrictedExteriorEnds={restricted}.");
        }
        GlobalEvents.onVehicleVariablesChanged += OnVariablesChanged;
        initialized = true;
        ReplaySavedImpacts();
    }

    private void ReplaySavedImpacts()
    {
        var records = vehicle?.vehicleInstance?.deformations;
        if (records == null) return;
        foreach (var record in records)
        {
            if (record.points == null || record.decelerationMagnitude / 100f < threshold) continue;
            var contacts = new List<Contact>(4);
            foreach (var saved in record.points)
            {
                Vector3 point = saved.point; Vector3 normal = saved.normal;
                if (normal.y > .65f || contacts.Count >= 4) continue;
                var end = Mathf.Abs(point.z) >= 1.35f && Mathf.Abs(point.z) > Mathf.Abs(point.x);
                var front = point.z >= 0f;
                var direction = end ? (front ? Vector3.back : Vector3.forward) : normal.normalized;
                if (!end && Vector3.Dot(direction, -point) < 0f) direction = -direction;
                contacts.Add(new Contact(point, direction, end, front));
            }
            if (contacts.Count == 0) continue;
            savedImpacts.Add(record);
            pending.Enqueue(new Impact { Contacts = contacts.ToArray(), Other = "saved impact",
                Closing = record.decelerationMagnitude / 100f,
                Depth = Mathf.Clamp((record.decelerationMagnitude / 100f - threshold) * .006f, .02f, MaximumDisplacement) });
        }
        if (pending.Count > 0) worker = StartCoroutine(ProcessImpacts());
    }

    private void StoreImpact(Impact impact)
    {
        savedImpacts.Add(new VehicleDeformationController.VehicleDeformation
        {
            decelerationMagnitude = impact.Closing * 100f,
            points = Array.ConvertAll(impact.Contacts, contact => new VehicleDeformationController.VehicleDeformation.VehicleDeformationPoint
            { point = contact.Point, normal = contact.Direction, deformationRandomness = 0f })
        });
        if (savedImpacts.Count > 20) savedImpacts.RemoveAt(0);
        if (saveRecords != null) StopCoroutine(saveRecords);
        saveRecords = StartCoroutine(StoreAfterNativeCallbacks());
    }

    private IEnumerator StoreAfterNativeCallbacks()
    {
        // The disabled native receiver can still append a raw collision record.
        // Replace it with our approved, normal-speed record after that callback.
        yield return null;
        FlushSavedImpacts();
        saveRecords = null;
    }

    private void FlushSavedImpacts()
    {
        var records = vehicle?.vehicleInstance?.deformations;
        if (records != null) { records.Clear(); records.AddRange(savedImpacts); }
    }

    internal static bool IsExteriorEnd(Vector3 point) => point.z < -1.70f || point.z > 1.70f;
    internal static bool RestrictToExteriorEnds(MeshFilter filter)
    {
        var renderer = filter.GetComponent<Renderer>();
        if (renderer != null)
            foreach (var material in renderer.sharedMaterials)
                if (material != null && material.name.IndexOf("red_glass", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false; // Rear lamp lenses and centre brake lens, not cabin glazing.
        for (var node = filter.transform; node != null; node = node.parent)
            if (node.name.IndexOf("Kit1_Interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                node.name.IndexOf("Kit1_Window", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private void OnVariablesChanged()
    {
        if (!initialized || handler == null) return;
        var damage = Mathf.Max(handler.Damage, vehicle?.vehicleInstance?.damage ?? 0f);
        if (previousDamage > .001f && damage <= .001f)
        {
            pending.Clear();
            savedImpacts.Clear();
            if (saveRecords != null) StopCoroutine(saveRecords); saveRecords = null;
            if (worker != null) StopCoroutine(worker); worker = null;
            if (repair != null) StopCoroutine(repair);
            repair = StartCoroutine(RestoreAfterNativeRepair());
        }
        previousDamage = damage;
    }

    private IEnumerator RestoreAfterNativeRepair()
    {
        // Native Repair resets its original mesh bindings before this one-shot
        // lifecycle callback reattaches and restores the per-instance meshes.
        yield return null;
        foreach (var surface in surfaces)
        {
            if (surface.Filter == null || surface.Mesh == null) continue;
            Array.Copy(surface.Original, surface.Working, surface.Original.Length);
            surface.Filter.sharedMesh = surface.Mesh;
            surface.Mesh.vertices = surface.Working; surface.Mesh.bounds = surface.SafeBounds;
        }
        yield return new WaitForFixedUpdate();
        if (vehicle != null && vehicle.controlledByPlayer)
        {
            vehicle.SetFreeze(false);
            var physics = vehicle.GetComponent<NWH.VehiclePhysics2.VehicleController>();
            if (physics != null)
            {
                physics.enabled = true;
                if (!physics.powertrain.engine.IsRunning) physics.powertrain.engine.StartEngine();
                if (physics.powertrain.transmission.Gear == 0) physics.powertrain.transmission.ShiftInto(1, true);
            }
            foreach (var wheel in vehicle.GetComponentsInChildren<NWH.WheelController3D.WheelController>(true)) wheel.enabled = true;
            var body = vehicle.GetComponent<Rigidbody>();
            if (body != null) { body.isKinematic = false; body.WakeUp(); }
        }
        repair = null;
        AudiR8Diagnostics.DamageInfo(context, $"AudiR8 damage vehicle={vehicle?.GetInstanceID()}: instance meshes repaired.");
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!initialized || collision.contactCount == 0) return;
        // Also remove raw records for contacts rejected as support/tangential
        // or as sibling contacts in the same impact cooldown.
        if (saveRecords != null) StopCoroutine(saveRecords);
        saveRecords = StartCoroutine(StoreAfterNativeCallbacks());
        if (Time.unscaledTime < nextImpactAt ||
            (SaveGameManager.Current != null && SaveGameManager.Current.gameVariables.disableVehicleDamage)) return;
        var closing = AudiR8ImpactDamageController.ClosingSpeed(collision);
        if (closing < threshold) return;
        nextImpactAt = Time.unscaledTime + .5f;
        var contacts = new List<Contact>(4);
        for (var i = 0; i < collision.contactCount && contacts.Count < 4; i++)
        {
            var contact = collision.GetContact(i);
            if (Vector3.Dot(collision.relativeVelocity, contact.normal) < threshold) continue;
            var point = transform.InverseTransformPoint(contact.point);
            var duplicate = false;
            foreach (var selected in contacts) if ((selected.Point - point).sqrMagnitude < .0225f) duplicate = true;
            if (duplicate) continue;
            var end = Mathf.Abs(point.z) >= 1.35f && Mathf.Abs(point.z) > Mathf.Abs(point.x);
            var front = point.z >= 0f;
            var direction = end ? (front ? Vector3.back : Vector3.forward) :
                transform.InverseTransformDirection(contact.normal).normalized;
            if (!end && Vector3.Dot(direction, -point) < 0f) direction = -direction;
            contacts.Add(new Contact(point, direction, end, front));
        }
        if (contacts.Count == 0) return;
        if (pending.Count >= MaximumPendingImpacts)
        {
            // Score is handled independently, so queue pressure cannot erase
            // legitimate condition damage. Retain the most recent visual region.
            pending.Dequeue();
            AudiR8Diagnostics.DamageInfo(context, "AudiR8 deformation queue coalesced oldest visual impact.");
        }
        var impact = new Impact { Contacts = contacts.ToArray(), Closing = closing,
            Depth = Mathf.Clamp((closing - threshold) * .006f, .02f, MaximumDisplacement),
            Other = collision.collider != null ? collision.collider.name : "unknown" };
        StoreImpact(impact);
        pending.Enqueue(impact);
        if (worker == null) worker = StartCoroutine(ProcessImpacts());
    }

    private static float Influence(Vector3 point, Contact contact)
    {
        var delta = point - contact.Point;
        var distanceSquared = contact.End
            ? delta.x * delta.x / (contact.Front ? 1f : 1.44f) +
              delta.y * delta.y / (contact.Front ? 1.3924f : 2.89f) +
              delta.z * delta.z / (contact.Front ? 2.0164f : 3.0625f)
            : delta.sqrMagnitude / .9025f;
        if (distanceSquared >= 1f) return 0f;
        var influence = 1f - Mathf.Sqrt(distanceSquared);
        return contact.End ? Mathf.Pow(influence, 1.35f) : influence * influence;
    }

    private IEnumerator ProcessImpacts()
    {
        // Always defer the first slice so collision callbacks remain cheap and
        // the worker handle is assigned even when every surface is rejected.
        yield return null;
        var work = DeformQueuedImpacts();
        while (true)
        {
            bool more; object? next;
            try { more = work.MoveNext(); next = more ? work.Current : null; }
            catch (Exception exception)
            {
                if (!failureReported)
                {
                    failureReported = true;
                    context?.Logger.Warn($"AudiR8 deformation vehicle={vehicle?.GetInstanceID()} failed: {exception.GetType().Name}: {exception.Message}");
                }
                pending.Clear(); break;
            }
            if (!more) break;
            yield return next;
        }
        worker = null;
    }

    private IEnumerator DeformQueuedImpacts()
    {
        var inspected = 0;
        var sliceStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        while (pending.Count > 0)
        {
            var impact = pending.Dequeue();
            var changed = 0; var skipped = 0; var frames = 0;
            foreach (var surface in surfaces)
            {
                if (surface.Filter == null || surface.Mesh == null) continue;
                var reachable = false;
                foreach (var contact in impact.Contacts)
                    if (surface.VehicleBounds.SqrDistance(contact.Point) <= 3.0625f) reachable = true;
                if (!reachable) { skipped++; continue; }
                var dirty = false;
                foreach (var index in surface.Indices)
                {
                    var point = surface.ToVehicle.MultiplyPoint3x4(surface.Working[index]);
                    var strongest = 0f; var direction = Vector3.zero; var regionScale = 1f;
                    foreach (var contact in impact.Contacts)
                    {
                        var scale = RegionScale(contact.End, contact.Front);
                        var influence = Influence(point, contact) * scale;
                        if (influence <= strongest) continue;
                        strongest = influence; direction = contact.Direction; regionScale = scale;
                    }
                    if (strongest > 0f)
                    {
                        var original = surface.ToVehicle.MultiplyPoint3x4(surface.Original[index]);
                        var requested = point + direction * (impact.Depth * strongest);
                        var displaced = original + Vector3.ClampMagnitude(requested - original, MaximumDisplacement * regionScale);
                        if ((displaced - point).sqrMagnitude > .00000001f)
                        { surface.Working[index] = surface.FromVehicle.MultiplyPoint3x4(displaced); dirty = true; }
                    }
                    inspected++;
                    if (inspected >= VertexBudgetPerFrame || (inspected % 64 == 0 &&
                        (System.Diagnostics.Stopwatch.GetTimestamp() - sliceStarted) * 1000d /
                        System.Diagnostics.Stopwatch.Frequency >= MillisecondBudget))
                    {
                        frames++; yield return null;
                        inspected = 0; sliceStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    }
                }
                if (!dirty) continue;
                // Amarok safeguard: cached vertex arrays/matrices, broad-phase
                // rejection and conservative bounds; no full-mesh bounds scan.
                surface.Filter.sharedMesh = surface.Mesh;
                surface.Mesh.vertices = surface.Working; surface.Mesh.bounds = surface.SafeBounds;
                changed++;
            }
            AudiR8Diagnostics.DamageInfo(context,
                $"AudiR8 deformation vehicle={vehicle?.GetInstanceID()} other='{impact.Other}' closing={impact.Closing:0.00}mps meshes={changed} skipped={skipped} yieldedFrames={frames} vertexBudget={VertexBudgetPerFrame} timeBudget={MillisecondBudget:0.0}ms.");
        }
    }

    private void OnDisable()
    {
        if (saveRecords != null) FlushSavedImpacts();
        StopAllCoroutines(); worker = repair = saveRecords = null; pending.Clear();
    }

    private void OnDestroy()
    {
        GlobalEvents.onVehicleVariablesChanged -= OnVariablesChanged;
        StopAllCoroutines();
        foreach (var surface in surfaces) if (surface.Mesh != null) Destroy(surface.Mesh);
        surfaces.Clear(); pending.Clear();
    }
}
