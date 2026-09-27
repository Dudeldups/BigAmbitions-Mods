#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BattleBusVisualDamageController : MonoBehaviour
{
    private const float MinimumImpactSpeed = 3.5f;
    private const float CollisionCooldown = 0.35f;
    private const float MaximumDentDepth = 0.28f;
    private const float SideRadius = 0.78f;
    private const float EndLateralRadius = 0.95f;
    private const float EndVerticalRadius = 0.88f;
    private const float EndLongitudinalRadius = 1.28f;

    [SerializeField] private MeshFilter[] bodyMeshes = Array.Empty<MeshFilter>();

    private readonly Dictionary<MeshFilter, Mesh> sourceMeshes = new();
    private readonly Dictionary<MeshFilter, Vector3[]> sourceVertices = new();
    private NWH.VehiclePhysics2.Damage.DamageHandler? damageHandler;
    private VehicleController? vehicle;
    private ModContext? context;
    private float previousDamage;
    private float nextCollisionTime;
    private bool failureReported;

    private void Awake()
    {
        vehicle = GetComponent<VehicleController>();
        damageHandler = GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true);
        foreach (var filter in bodyMeshes)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;
            sourceMeshes[filter] = filter.sharedMesh;
            sourceVertices[filter] = filter.sharedMesh.vertices;
        }
        previousDamage = damageHandler != null ? damageHandler.Damage : 0f;
    }

    internal void Initialize(ModContext? modContext)
    {
        context = modContext;
        if (bodyMeshes.Length != 17 || sourceMeshes.Count != 17)
            context?.Logger.Warn($"Battle Bus deformation vehicle={vehicle?.GetInstanceID()}: " +
                                 $"expected seventeen body and exterior meshes, found {sourceMeshes.Count}.");
        if (damageHandler == null)
            context?.Logger.Warn($"Battle Bus deformation vehicle={vehicle?.GetInstanceID()}: damage handler missing.");
        BattleBusDiagnostics.DamageInfo(context,
            $"Battle Bus deformation initialized vehicle={vehicle?.GetInstanceID()}, meshes={sourceMeshes.Count}.");
    }

    private void Update()
    {
        if (damageHandler == null)
            return;
        var damage = damageHandler.Damage;
        if (previousDamage > 0.001f && damage <= 0.001f)
        {
            foreach (var pair in sourceMeshes)
                if (pair.Key != null && pair.Value != null)
                    pair.Key.sharedMesh = pair.Value;
            BattleBusDiagnostics.DamageInfo(context,
                $"Battle Bus deformation restored after repair vehicle={vehicle?.GetInstanceID()}.");
        }
        previousDamage = damage;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null || collision.contactCount == 0 ||
            Time.unscaledTime < nextCollisionTime ||
            collision.relativeVelocity.magnitude < MinimumImpactSpeed ||
            !NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision))
            return;

        nextCollisionTime = Time.unscaledTime + CollisionCooldown;
        try
        {
            var impactSpeed = collision.relativeVelocity.magnitude;
            var depth = Mathf.Clamp((impactSpeed - MinimumImpactSpeed) * 0.014f,
                0.035f, MaximumDentDepth);
            var contacts = collision.contacts;
            var changedMeshes = 0;
            foreach (var pair in sourceMeshes)
            {
                var filter = pair.Key;
                if (filter == null || filter.sharedMesh == null)
                    continue;
                var mesh = filter.sharedMesh == pair.Value
                    ? Instantiate(pair.Value)
                    : filter.sharedMesh;
                if (filter.sharedMesh == pair.Value)
                    filter.sharedMesh = mesh;
                var vertices = mesh.vertices;
                var originals = sourceVertices[filter];
                var changed = false;
                for (var index = 0; index < vertices.Length; index++)
                {
                    var worldVertex = filter.transform.TransformPoint(vertices[index]);
                    var bestWeight = 0f;
                    var inward = Vector3.zero;
                    foreach (var contact in contacts)
                    {
                        var localContact = transform.InverseTransformPoint(contact.point);
                        var delta = transform.InverseTransformVector(worldVertex - contact.point);
                        var endImpact = Mathf.Abs(localContact.z) > 3.0f &&
                                        Mathf.Abs(localContact.z) > Mathf.Abs(localContact.x);
                        var distance = endImpact
                            ? Mathf.Sqrt(delta.x * delta.x / (EndLateralRadius * EndLateralRadius) +
                                         delta.y * delta.y / (EndVerticalRadius * EndVerticalRadius) +
                                         delta.z * delta.z / (EndLongitudinalRadius * EndLongitudinalRadius))
                            : delta.magnitude / SideRadius;
                        var weight = Mathf.Clamp01(1f - distance);
                        if (weight <= bestWeight)
                            continue;
                        bestWeight = weight;
                        inward = endImpact
                            ? (localContact.z >= 0f ? -transform.forward : transform.forward)
                            : (transform.position - contact.point).normalized;
                    }
                    if (bestWeight <= 0f || inward.sqrMagnitude < 0.5f)
                        continue;
                    var originalWorld = filter.transform.TransformPoint(originals[index]);
                    worldVertex += inward * (depth * bestWeight * bestWeight);
                    var displacement = worldVertex - originalWorld;
                    if (displacement.sqrMagnitude > MaximumDentDepth * MaximumDentDepth)
                        worldVertex = originalWorld + displacement.normalized * MaximumDentDepth;
                    vertices[index] = filter.transform.InverseTransformPoint(worldVertex);
                    changed = true;
                }
                if (!changed)
                    continue;
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                changedMeshes++;
            }
            BattleBusDiagnostics.DamageInfo(context,
                $"Battle Bus deformation impact vehicle={vehicle?.GetInstanceID()}, " +
                $"speed={impactSpeed:F1}m/s, contacts={contacts.Length}, meshes={changedMeshes}.");
        }
        catch (Exception exception)
        {
            if (failureReported)
                return;
            failureReported = true;
            context?.Logger.Warn($"Battle Bus deformation vehicle={vehicle?.GetInstanceID()} failed: " +
                                 $"{exception.GetType().Name}: {exception.Message}");
        }
    }
}
