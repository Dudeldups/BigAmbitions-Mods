#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

internal sealed class AudiRS6RRoadDamageGuard : MonoBehaviour
{
    private const float DamageTolerance = 0.0001f;
    private const float RoadDamageSuppressionWindow = 0.5f;
    private const float RoadSurfaceNormalThreshold = 0.65f;

    private readonly List<VehicleDeformationController.VehicleDeformation> approvedDeformations = new();
    private VehicleController? vehicleController;
    private VehicleDeformationController? deformationController;
    private FieldInfo? deformationQueueField;
    private bool initialized;
    private bool failureReported;
    private bool lastCollisionWasRoadSurface;
    private float approvedDamage;
    private float obstacleContactUntil;
    private float roadDamageSuppressionUntil;
    private int roadSurfaceContactFrame = -1;
    private int obstacleContactFrame = -1;

    public void Initialize(VehicleController controller)
    {
        if (initialized && vehicleController == controller)
            return;

        vehicleController = controller;
        deformationController = controller.GetComponentInChildren<VehicleDeformationController>(true);
        deformationQueueField = typeof(VehicleDeformationController).GetField(
            "_deformationQueue",
            BindingFlags.Instance | BindingFlags.NonPublic);
        approvedDamage = controller.vehicleInstance?.damage ?? 0f;
        SnapshotApprovedDeformations();
        initialized = true;
    }

    private void Update()
    {
        if (!initialized || vehicleController?.vehicleInstance == null)
            return;

        try
        {
            SuppressRoadSurfaceDamage();
        }
        catch (Exception ex)
        {
            ReportFailureOnce(nameof(Update), ex);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!initialized || collision == null)
            return;

        try
        {
            var otherCollider = collision.collider;
            var otherName = otherCollider != null ? otherCollider.name : string.Empty;
            var otherPath = otherCollider != null ? GetHierarchyPath(otherCollider.transform) : string.Empty;
            var tag = otherCollider != null ? otherCollider.tag : string.Empty;
            var layerName = otherCollider != null ? LayerMask.LayerToName(otherCollider.gameObject.layer) : string.Empty;
            var normal = collision.contactCount > 0 ? collision.GetContact(0).normal : Vector3.zero;
            var isRoadSurfaceContact = IsRoadLike(otherName, otherPath, tag, layerName) &&
                                       normal.y >= RoadSurfaceNormalThreshold &&
                                       Vector3.Dot(transform.up, Vector3.up) >= 0.7f;

            if (isRoadSurfaceContact)
            {
                roadSurfaceContactFrame = Time.frameCount;
                // A hard rear/front impact can pitch the car into the road on
                // the following physics step. That ground contact must not
                // roll back damage already earned from the obstacle impact.
                lastCollisionWasRoadSurface = Time.unscaledTime > obstacleContactUntil;
                if (lastCollisionWasRoadSurface)
                    roadDamageSuppressionUntil = Time.unscaledTime + RoadDamageSuppressionWindow;
            }
            else
            {
                lastCollisionWasRoadSurface = false;
                obstacleContactFrame = Time.frameCount;
                obstacleContactUntil = Time.unscaledTime + RoadDamageSuppressionWindow;
                roadDamageSuppressionUntil = 0f;
            }
        }
        catch (Exception ex)
        {
            ReportFailureOnce(nameof(OnCollisionEnter), ex);
        }
    }

    private void SuppressRoadSurfaceDamage()
    {
        if (vehicleController?.vehicleInstance == null)
            return;

        var instance = vehicleController.vehicleInstance;
        var roadOnlyThisFrame = roadSurfaceContactFrame == Time.frameCount &&
                                obstacleContactFrame != Time.frameCount &&
                                Time.unscaledTime > obstacleContactUntil;
        var roadDamageMayBePending = Time.unscaledTime <= roadDamageSuppressionUntil &&
                                     Time.unscaledTime > obstacleContactUntil &&
                                     lastCollisionWasRoadSurface;

        if (roadOnlyThisFrame)
        {
            ClearPendingDeformationQueue();
            if (!MatchesApprovedDeformations(instance.deformations))
            {
                instance.deformations.Clear();
                instance.deformations.AddRange(approvedDeformations);
            }
        }

        if (roadDamageMayBePending && instance.damage > approvedDamage + DamageTolerance)
        {
            if (vehicleController is CarController carController)
                carController.SetDamage(approvedDamage);
            else
                instance.damage = approvedDamage;
        }

        if (!roadOnlyThisFrame && !roadDamageMayBePending)
        {
            approvedDamage = instance.damage;
            SnapshotApprovedDeformations();
        }
    }

    private void ClearPendingDeformationQueue()
    {
        if (deformationController == null || deformationQueueField == null)
            return;

        var queue = deformationQueueField.GetValue(deformationController);
        queue?.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)?.Invoke(queue, null);
    }

    private void SnapshotApprovedDeformations()
    {
        approvedDeformations.Clear();
        var deformations = vehicleController?.vehicleInstance?.deformations;
        if (deformations != null)
            approvedDeformations.AddRange(deformations);
    }

    private bool MatchesApprovedDeformations(List<VehicleDeformationController.VehicleDeformation> deformations)
    {
        if (deformations.Count != approvedDeformations.Count)
            return false;

        for (var index = 0; index < deformations.Count; index++)
        {
            if (!ReferenceEquals(deformations[index], approvedDeformations[index]))
                return false;
        }

        return true;
    }

    private void ReportFailureOnce(string scope, Exception exception)
    {
        if (failureReported)
            return;

        failureReported = true;
        Debug.LogWarning($"AudiRS6R road damage guard {scope} failed: {exception.GetType().Name}: {exception.Message}");
    }

    private static bool IsRoadLike(string name, string path, string tag, string layerName)
    {
        var objectName = name.ToLowerInvariant();
        var objectIdentity = $"{name} {path} {tag}".ToLowerInvariant();
        var normalizedLayerName = layerName.ToLowerInvariant();

        if (objectName.Contains("tree") || objectName.Contains("pine") || objectName.Contains("trunk") ||
            objectName.Contains("fence") || objectName.Contains("wall") || objectName.Contains("building") ||
            objectName.Contains("pole") || objectName.Contains("lamp") || objectName.Contains("hydrant") ||
            objectName.Contains("barricade") || objectName.Contains("delimiter") || objectName.Contains("barrier") ||
            objectName.Contains("bollard") || objectName.Contains("curb") || objectName.Contains("kerb") ||
            objectName.Contains("sidewalk") || objectName.Contains("pavement"))
        {
            return false;
        }

        if (objectName.Contains("road"))
            return true;
        if (normalizedLayerName.Contains("prop"))
            return false;
        if (normalizedLayerName == "ground" || normalizedLayerName == "terrain" ||
            normalizedLayerName == "road" || normalizedLayerName == "roads")
        {
            return true;
        }

        return objectIdentity.Contains("groundplane") || objectIdentity.Contains("ground_plane") ||
               objectIdentity.Contains("roadmesh") || objectIdentity.Contains("road_mesh") ||
               objectIdentity.Contains("roadsurface") || objectIdentity.Contains("road_surface") ||
               objectIdentity.Contains("asphalt") || objectIdentity.Contains("terrain");
    }

    internal bool IsRoadSurfaceCollision(Collision collision)
    {
        if (collision == null || collision.contactCount == 0)
            return false;

        var otherCollider = collision.collider;
        var otherName = otherCollider != null ? otherCollider.name : string.Empty;
        var otherPath = otherCollider != null ? GetHierarchyPath(otherCollider.transform) : string.Empty;
        var tag = otherCollider != null ? otherCollider.tag : string.Empty;
        var layerName = otherCollider != null
            ? LayerMask.LayerToName(otherCollider.gameObject.layer)
            : string.Empty;
        return IsRoadLike(otherName, otherPath, tag, layerName) &&
               collision.GetContact(0).normal.y >= RoadSurfaceNormalThreshold &&
               Vector3.Dot(transform.up, Vector3.up) >= 0.7f;
    }

    private static string GetHierarchyPath(Transform target)
    {
        var path = target.name;
        var current = target.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
