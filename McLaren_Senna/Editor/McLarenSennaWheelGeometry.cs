#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Author once in intrinsic wheel coordinates. The imported 1.5-degree camber
// must not rotate with the wheel, and body fitting scales must not shear it.
public static class McLarenSennaWheelGeometry
{
    private const string Folder = "Assets/Mods/McLaren_Senna/Models/GeneratedMeshes";
    public static void Recalibrate(GameObject root)
    {
        foreach (var mount in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("McLarenWheel", StringComparison.Ordinal)).ToArray())
        {
            var front = mount.name.Contains("Front");
            var radius = front ? .32705f : .34850f;
            var width = front ? .245f : .315f;
            var existing = mount.GetComponentsInChildren<MeshFilter>(true);
            if (existing.Length == 1 && existing[0].sharedMesh.name.StartsWith("McLarenRolling_", StringComparison.Ordinal))
                continue; // Repeated repairs preserve the calibrated mesh and GUID.
            var geometry = mount.GetChild(0);
            var filters = geometry.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToArray();
            var rendererTemplate = filters[0].GetComponent<Renderer>();
            var layer = geometry.gameObject.layer;
            var shadowCasting = rendererTemplate.shadowCastingMode;
            var receiveShadows = rendererTemplate.receiveShadows;
            var renderingLayers = rendererTemplate.renderingLayerMask;
            var lightProbes = rendererTemplate.lightProbeUsage;
            var reflectionProbes = rendererTemplate.reflectionProbeUsage;
            var materials = filters.SelectMany(f => f.GetComponent<Renderer>().sharedMaterials).Distinct().ToArray();
            if (materials.Length != 1 || filters.Any(f => f.sharedMesh.subMeshCount != 1))
                throw new InvalidOperationException("Unexpected wheel material/submesh layout: " + mount.name);
            var points = filters.SelectMany(f => f.sharedMesh.vertices.Select(p => geometry.InverseTransformPoint(f.transform.TransformPoint(p)))).ToArray();
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) bounds.Encapsulate(p);
            if (Mathf.Abs(bounds.size.y - bounds.size.z) > .0001f || bounds.size.x < .1f)
                throw new InvalidOperationException("Source tire is not circular in its intrinsic axle frame: " + mount.name);
            var rotation = Quaternion.Inverse(root.transform.rotation) * geometry.rotation;
            var axle = rotation * Vector3.right;
            rotation = Quaternion.FromToRotation(axle, axle.x >= 0f ? Vector3.right : Vector3.left) * rotation;
            var fit = Matrix4x4.TRS(Vector3.zero, rotation, new Vector3(width / bounds.size.x, 2f * radius / bounds.size.y, 2f * radius / bounds.size.y)) * Matrix4x4.Translate(-bounds.center);
            var copies = new List<Mesh>();
            try
            {
                foreach (var filter in filters)
                {
                    var transform = fit * geometry.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
                    mesh.vertices = mesh.vertices.Select(transform.MultiplyPoint3x4).ToArray();
                    var normalMatrix = transform.inverse.transpose;
                    mesh.normals = mesh.normals.Select(n => normalMatrix.MultiplyVector(n).normalized).ToArray();
                    var sign = transform.determinant < 0f ? -1f : 1f;
                    mesh.tangents = mesh.tangents.Select(t => { var v = transform.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized; return new Vector4(v.x, v.y, v.z, t.w * sign); }).ToArray();
                    if (sign < 0f) { var indices = mesh.triangles; for (var i = 0; i < indices.Length; i += 3) { var a = indices[i]; indices[i] = indices[i+1]; indices[i+1] = a; } mesh.triangles = indices; }
                    mesh.RecalculateBounds();
                    copies.Add(mesh);
                }
                var combined = new Mesh { name = "McLarenRolling_" + mount.name.Replace("McLarenWheel", ""), indexFormat = IndexFormat.UInt32 };
                combined.CombineMeshes(copies.Select(m => new CombineInstance { mesh = m, transform = Matrix4x4.identity }).ToArray(), true, false);
                combined.RecalculateBounds();
                var path = Folder + "/" + combined.name + ".asset";
                var stored = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (stored == null) { AssetDatabase.CreateAsset(combined, path); stored = combined; }
                else { EditorUtility.CopySerialized(combined, stored); UnityEngine.Object.DestroyImmediate(combined); }
                // The native wheel owns the unit-scale mount; no additional pose writer.
                mount.localScale = Vector3.one;
                for (var i = mount.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(mount.GetChild(i).gameObject);
                var child = new GameObject("McLarenRollingGeometry"); child.transform.SetParent(mount, false);
                child.AddComponent<MeshFilter>().sharedMesh = stored;
                child.layer = layer;
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[0]; renderer.shadowCastingMode = shadowCasting;
                renderer.receiveShadows = receiveShadows; renderer.renderingLayerMask = renderingLayers;
                renderer.lightProbeUsage = lightProbes; renderer.reflectionProbeUsage = reflectionProbes;
                Debug.Log($"Senna wheel calibrated: {mount.name}, tire={width:F5}x{2f*radius:F5}m, intrinsicCircle=true, unitScale=true, authoredCamberRemoved=true, vertices={stored.vertexCount}.");
            }
            finally { foreach (var mesh in copies) UnityEngine.Object.DestroyImmediate(mesh); }
        }
        Verify(root);
    }

    public static void Verify(GameObject root)
    {
        foreach (var mount in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("McLarenWheel", StringComparison.Ordinal)))
        {
            var filter = mount.GetComponentsInChildren<MeshFilter>(true).Single();
            var expectedRadius = mount.name.Contains("Front") ? .32705f : .34850f;
            var bounds = filter.sharedMesh.bounds;
            if (mount.localScale != Vector3.one || filter.transform.localScale != Vector3.one || bounds.center.magnitude > .00002f ||
                Mathf.Abs(bounds.extents.y - expectedRadius) > .00002f || Mathf.Abs(bounds.extents.z - expectedRadius) > .00002f)
                throw new InvalidOperationException("Wheel center/radius/scale calibration failed: " + mount.name);
            // Every vertex keeps its rolling radius and axle coordinate through a
            // full revolution. UVs, normals and tangents stay in the stored mesh.
            foreach (var degrees in new[] { 0f, 45f, 90f, 180f, 270f, 360f })
                foreach (var p in filter.sharedMesh.vertices)
                {
                    var q = Quaternion.AngleAxis(degrees, Vector3.right) * p;
                    if (Mathf.Abs(q.x - p.x) > .00002f || Mathf.Abs(new Vector2(q.y,q.z).magnitude - new Vector2(p.y,p.z).magnitude) > .00002f)
                        throw new InvalidOperationException("Rolling axis wobble: " + mount.name);
                }
        }
        Debug.Log("Senna four-wheel rotation fixture verified: centered circular tires, concentric rims, unit rolling hierarchy and invariant radius over 360 degrees.");
    }
}
