#nullable enable
using System;
using System.Collections.Generic;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Final wheel-authoring pass for the isolated Challenger build. Reads the
/// original GLB (not the preceding PCA-baked meshes), preserves their asset GUIDs,
/// and validates multiple circular tire sections before building the bundle.
/// No player/runtime hooks and no changes to damage, suspension, audio or paint.
/// </summary>
public static class DodgeChallenger2018MeasuredWheelBuild
{
    private const string Root = "Assets/Mods/Dodge_Challenger_2018";
    private const string ModelPath = Root + "/Models/2018_dodge_challenger_srt_demon_hpe1200.glb";
    private const string PrefabPath = Root + "/DodgeChallenger2018.prefab";
    private const string MeshFolder = Root + "/Models/GeneratedMeshes";
    private static readonly string[] Corners = { "FrontLeft", "FrontRight", "RearLeft", "RearRight" };
    private static readonly string[] Sides = { "LF", "RF", "LR", "RR" };

    [MenuItem("Tools/Big Ambitions/Dodge Challenger/Generate and build measured wheels")]
    public static void GenerateAndBuild()
    {
        try
        {
            Debug.Log("DodgeChallenger2018 BUILD_STAGE: Generate");
            DodgeChallenger2018Setup.Generate();
            Debug.Log("DodgeChallenger2018 BUILD_STAGE: MeasuredWheelGeometry");
            RepairGeneratedWheels();
            Debug.Log("DodgeChallenger2018 BUILD_STAGE: BuildForMod");
            ModAssetBundleCli.BuildForMod();
            Debug.Log("DodgeChallenger2018 BUILD_STAGE: VerifyBuiltBundle");
            DodgeChallenger2018Setup.VerifyBuiltBundle();
            Debug.Log("DodgeChallenger2018 BUILD_STAGE: Complete");
        }
        catch (Exception exception)
        {
            Debug.LogError("DodgeChallenger2018 BUILD_FAILURE: " + exception);
            throw;
        }
    }

    public static void RepairGeneratedWheels()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null)
            throw new InvalidOperationException("Original Challenger GLB is unavailable.");
        var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        GameObject? sourceInstance = null;
        var pending = new List<Mesh>();
        try
        {
            var authoredVisual = Find(prefab.transform, "DodgeVisual");
            sourceInstance = UnityEngine.Object.Instantiate(source, prefab.transform);
            sourceInstance.name = "MeasuredWheelSource_TEMP";
            sourceInstance.transform.localPosition = authoredVisual.localPosition;
            sourceInstance.transform.localRotation = authoredVisual.localRotation;
            sourceInstance.transform.localScale = authoredVisual.localScale;
            var targets = new List<MeshFilter>();
            var originals = new List<Mesh>();
            var reports = new List<string>();
            for (var cornerIndex = 0; cornerIndex < Corners.Length; cornerIndex++)
            {
                var corner = Corners[cornerIndex];
                var name = "Dodge_HPE1200Demon_2018_Modified_CSB:Wheel_01_" +
                    Sides[cornerIndex] + "_Dodge_HPE1200Demon_2018_Modified_CSB:Wheel2Mtl1_0";
                MeshFilter? reference = null;
                foreach (var filter in sourceInstance.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null ||
                        (!string.Equals(filter.name, name, StringComparison.Ordinal) &&
                         !string.Equals(filter.sharedMesh.name, name, StringComparison.Ordinal)))
                        continue;
                    if (reference != null)
                        throw new InvalidOperationException("Duplicate wheel source: " + name);
                    reference = filter;
                }
                if (reference == null || reference.sharedMesh == null)
                    throw new InvalidOperationException("Missing wheel source: " + name);
                var mount = Find(prefab.transform, "DodgeWheel" + corner);
                var target = Find(mount, "Geometry_DodgeWheel_" + corner).GetComponent<MeshFilter>();
                if (target == null || target.sharedMesh == null ||
                    AssetDatabase.GetAssetPath(target.sharedMesh) != MeshFolder + "/DodgeWheel" + corner + ".asset")
                    throw new InvalidOperationException("Unexpected generated wheel binding: " + corner);
                if (mount.parent != prefab.transform ||
                    Quaternion.Angle(mount.localRotation, Quaternion.identity) > 0.001f ||
                    (mount.localScale - Vector3.one).sqrMagnitude > 1e-10f ||
                    target.transform.localPosition.sqrMagnitude > 1e-10f ||
                    Quaternion.Angle(target.transform.localRotation, Quaternion.identity) > 0.001f ||
                    (target.transform.localScale - Vector3.one).sqrMagnitude > 1e-10f)
                    throw new InvalidOperationException("Wheel requires an identity visual mount: " + corner);
                var sourceToRoot = prefab.transform.worldToLocalMatrix * reference.transform.localToWorldMatrix;
                var replacement = Bake(reference.sharedMesh, sourceToRoot, 0.315f, 0.3546f, out var report);
                replacement.name = "DodgeWheel" + corner;
                pending.Add(replacement);
                targets.Add(target);
                originals.Add(target.sharedMesh);
                reports.Add(corner + ": " + report);
            }
            // Only publish meshes after ALL four originals passed the measurements.
            for (var index = 0; index < pending.Count; index++)
            {
                EditorUtility.CopySerialized(pending[index], originals[index]);
                EditorUtility.SetDirty(originals[index]);
                targets[index].sharedMesh = originals[index];
                Debug.Log("DodgeChallenger2018 MEASURED_WHEEL_V1 " + reports[index]);
            }
            UnityEngine.Object.DestroyImmediate(sourceInstance);
            sourceInstance = null;
            if (PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath) == null)
                throw new InvalidOperationException("Could not save the measured-wheel prefab.");
            AssetDatabase.SaveAssets();
        }
        finally
        {
            foreach (var mesh in pending)
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            if (sourceInstance != null) UnityEngine.Object.DestroyImmediate(sourceInstance);
            PrefabUtility.UnloadPrefabContents(prefab);
        }
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
        throw new InvalidOperationException("Missing transform: " + name);
    }

    private struct Face
    {
        public Vector3 Normal;
        public float Area;
    }
    private sealed class Ring
    {
        public int[] Indices = Array.Empty<int>();
        public Vector2 Center;
        public float Radius;
    }

    private static Vector3 MeasureAxle(Vector3[] points, int[] triangles)
    {
        var faces = new List<Face>();
        for (var index = 0; index < triangles.Length; index += 3)
        {
            var cross = Vector3.Cross(points[triangles[index + 1]] - points[triangles[index]],
                                      points[triangles[index + 2]] - points[triangles[index]]);
            var area = cross.magnitude;
            if (area <= 1e-9f) continue;
            var normal = cross / area;
            if (normal.x < 0f) normal = -normal;
            if (normal.x >= 0.9396926f) faces.Add(new Face { Normal = normal, Area = area });
        }
        if (faces.Count < 20) throw new InvalidOperationException("Insufficient axial wheel surfaces.");
        faces.Sort((a, b) => b.Area.CompareTo(a.Area));
        // The planar disc/rim surfaces agree on their normal. A centroid/PCA of
        // every vertex does not: valve and lug/spoke topology bias the latter.
        var tolerance = Mathf.Cos(0.15f * Mathf.Deg2Rad);
        var best = Vector3.right;
        var bestArea = -1f;
        for (var index = 0; index < Math.Min(64, faces.Count); index++)
        {
            var candidate = faces[index].Normal;
            var support = 0f;
            foreach (var face in faces)
                if (Vector3.Dot(face.Normal, candidate) >= tolerance) support += face.Area;
            if (support > bestArea) { bestArea = support; best = candidate; }
        }
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var sum = Vector3.zero;
            var count = 0;
            foreach (var face in faces)
                if (Vector3.Dot(face.Normal, best) >= tolerance)
                { sum += face.Normal * face.Area; count++; }
            if (count < 20 || sum.sqrMagnitude < 1e-12f)
                throw new InvalidOperationException("Wheel face-normal consensus failed.");
            best = sum.normalized;
        }
        return best;
    }

    private static List<Ring> FindCircularSections(Vector3[] points)
    {
        var bounds = BoundsOf(points);
        var diameter = Mathf.Max(bounds.size.y, bounds.size.z);
        var planeTolerance = diameter * 0.00006f;
        var order = new int[points.Length];
        for (var i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) => points[a].x.CompareTo(points[b].x));
        var rings = new List<Ring>();
        var start = 0;
        while (start < order.Length)
        {
            var end = start + 1;
            while (end < order.Length && points[order[end]].x - points[order[start]].x <= planeTolerance) end++;
            if (end - start >= 20)
            {
                var lo = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                var hi = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                for (var k = start; k < end; k++)
                {
                    var p = points[order[k]];
                    lo = Vector2.Min(lo, new Vector2(p.y, p.z));
                    hi = Vector2.Max(hi, new Vector2(p.y, p.z));
                }
                var rough = (lo + hi) * 0.5f;
                var largest = 0f;
                for (var k = start; k < end; k++)
                {
                    var p = points[order[k]];
                    largest = Mathf.Max(largest, Vector2.Distance(rough, new Vector2(p.y, p.z)));
                }
                var indices = new List<int>();
                for (var k = start; k < end; k++)
                {
                    var p = points[order[k]];
                    if (Vector2.Distance(rough, new Vector2(p.y, p.z)) >= largest * 0.985f) indices.Add(order[k]);
                }
                if (indices.Count >= 20 && FitCircle(points, indices, out var center, out var radius, out var spread))
                {
                    var sectors = new bool[24];
                    foreach (var index in indices)
                    {
                        var p = points[index];
                        var angle = Math.Atan2(p.z - center.y, p.y - center.x);
                        var sector = (int)Math.Floor((angle + Math.PI) * 24d / (2d * Math.PI));
                        sectors[(sector + 24) % 24] = true;
                    }
                    var coverage = 0;
                    foreach (var present in sectors) if (present) coverage++;
                    if (coverage >= 18 && radius >= diameter * 0.20f && spread <= diameter * 0.00014f)
                        rings.Add(new Ring { Indices = indices.ToArray(), Center = center, Radius = radius });
                }
            }
            start = end;
        }
        if (rings.Count < 4) throw new InvalidOperationException("Could not validate four circular wheel sections.");
        return rings;
    }

    private static bool FitCircle(Vector3[] points, IList<int> indices,
        out Vector2 center, out float radius, out float spread)
    {
        center = Vector2.zero; radius = spread = 0f;
        if (indices.Count < 3) return false;
        var lo = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var hi = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var index in indices)
        {
            var p = points[index];
            lo = Vector2.Min(lo, new Vector2(p.y, p.z));
            hi = Vector2.Max(hi, new Vector2(p.y, p.z));
        }
        var origin = (lo + hi) * 0.5f;
        var m = new double[3, 4];
        foreach (var index in indices)
        {
            double y = (double)points[index].y - origin.x, z = (double)points[index].z - origin.y;
            var q = y * y + z * z;
            m[0, 0] += y * y; m[0, 1] += y * z; m[0, 2] += y; m[0, 3] += y * q;
            m[1, 0] += y * z; m[1, 1] += z * z; m[1, 2] += z; m[1, 3] += z * q;
            m[2, 0] += y; m[2, 1] += z; m[2, 2] += 1d; m[2, 3] += q;
        }
        for (var col = 0; col < 3; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < 3; row++)
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col])) pivot = row;
            if (Math.Abs(m[pivot, col]) < 1e-16d) return false;
            for (var j = col; j < 4; j++) { var t = m[col, j]; m[col, j] = m[pivot, j]; m[pivot, j] = t; }
            var divisor = m[col, col];
            for (var j = col; j < 4; j++) m[col, j] /= divisor;
            for (var row = 0; row < 3; row++)
            {
                if (row == col) continue;
                var factor = m[row, col];
                for (var j = col; j < 4; j++) m[row, j] -= factor * m[col, j];
            }
        }
        center = origin + new Vector2((float)(m[0, 3] * 0.5d), (float)(m[1, 3] * 0.5d));
        var minimum = float.PositiveInfinity; var maximum = 0f;
        foreach (var index in indices)
        {
            var p = points[index];
            var r = Vector2.Distance(center, new Vector2(p.y, p.z));
            minimum = Mathf.Min(minimum, r); maximum = Mathf.Max(maximum, r); radius += r;
        }
        radius /= indices.Count; spread = maximum - minimum;
        return !(float.IsNaN(radius) || float.IsInfinity(radius));
    }

    private static Bounds BoundsOf(Vector3[] points)
    {
        if (points.Length == 0) throw new InvalidOperationException("Empty wheel mesh.");
        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (var point in points) bounds.Encapsulate(point);
        return bounds;
    }

    private static float Median(List<float> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) * 0.5f : values[middle];
    }

    private static Mesh Bake(Mesh source, Matrix4x4 sourceToRoot, float width, float radius, out string report)
    {
        var originalVertices = source.vertices;
        var points = new Vector3[originalVertices.Length];
        for (var i = 0; i < points.Length; i++) points[i] = sourceToRoot.MultiplyPoint3x4(originalVertices[i]);
        var axle = MeasureAxle(points, source.triangles);
        var align = Quaternion.FromToRotation(axle, Vector3.right);
        for (var i = 0; i < points.Length; i++) points[i] = align * points[i];
        var rings = FindCircularSections(points);
        var largestRing = 0f;
        foreach (var ring in rings) largestRing = Mathf.Max(largestRing, ring.Radius);
        var ys = new List<float>(); var zs = new List<float>();
        foreach (var ring in rings)
            if (ring.Radius >= largestRing * 0.8f) { ys.Add(ring.Center.x); zs.Add(ring.Center.y); }
        if (ys.Count < 4) throw new InvalidOperationException("Too few independent outer tire sections.");
        var bounds = BoundsOf(points);
        var center = new Vector3(bounds.center.x, Median(ys), Median(zs));
        var maximumRadius = 0f;
        foreach (var point in points)
            maximumRadius = Mathf.Max(maximumRadius, new Vector2(point.y - center.y, point.z - center.z).magnitude);
        if (bounds.size.x <= 0.001f || maximumRadius <= 0.1f)
            throw new InvalidOperationException("Invalid wheel dimensions.");
        var scale = new Vector3(width / bounds.size.x, radius / maximumRadius, radius / maximumRadius);
        var transform = Matrix4x4.Scale(scale) * Matrix4x4.Translate(-center) * Matrix4x4.Rotate(align) * sourceToRoot;
        var baked = UnityEngine.Object.Instantiate(source);
        try
        {
            for (var i = 0; i < points.Length; i++) points[i] = transform.MultiplyPoint3x4(originalVertices[i]);
            var worstSide = 0f; var worstOrbit = 0f; var worstRoundness = 0f;
            var checkedSections = 0;
            // These independent cross-sections test tilt AND eccentricity. The
            // former validation only re-fitted its own chosen center and could
            // pass even with a tilted wheel. Do not re-center to force a pass.
            foreach (var ring in rings)
            {
                if (ring.Radius < largestRing * 0.8f) continue;
                if (!FitCircle(points, ring.Indices, out var c, out _, out var roundness))
                    throw new InvalidOperationException("Post-bake ring measurement failed.");
                var low = float.PositiveInfinity; var high = float.NegativeInfinity;
                foreach (var index in ring.Indices) { low = Mathf.Min(low, points[index].x); high = Mathf.Max(high, points[index].x); }
                worstSide = Mathf.Max(worstSide, high - low);
                worstOrbit = Mathf.Max(worstOrbit, 2f * c.magnitude);
                worstRoundness = Mathf.Max(worstRoundness, roundness);
                checkedSections++;
            }
            if (worstSide > 0.0001f || worstOrbit > 0.0001f || worstRoundness > 0.0001f)
                throw new InvalidOperationException($"Measured wheel validation failed: lateral={worstSide * 1000f:F4}mm, orbit={worstOrbit * 1000f:F4}mm, roundness={worstRoundness * 1000f:F4}mm.");
            baked.vertices = points;
            var normals = source.normals;
            var normalTransform = transform.inverse.transpose;
            for (var i = 0; i < normals.Length; i++) normals[i] = normalTransform.MultiplyVector(normals[i]).normalized;
            if (normals.Length == points.Length) baked.normals = normals;
            var tangents = source.tangents;
            var mirrored = transform.determinant < 0f;
            for (var i = 0; i < tangents.Length; i++)
            {
                var t = tangents[i];
                var direction = transform.MultiplyVector(new Vector3(t.x, t.y, t.z));
                if (normals.Length == points.Length) direction -= normals[i] * Vector3.Dot(normals[i], direction);
                direction.Normalize();
                tangents[i] = new Vector4(direction.x, direction.y, direction.z, mirrored ? -t.w : t.w);
            }
            if (tangents.Length == points.Length) baked.tangents = tangents;
            if (mirrored)
                for (var sub = 0; sub < baked.subMeshCount; sub++)
                {
                    var triangles = baked.GetTriangles(sub);
                    for (var i = 0; i < triangles.Length; i += 3)
                    { var t = triangles[i + 1]; triangles[i + 1] = triangles[i + 2]; triangles[i + 2] = t; }
                    baked.SetTriangles(triangles, sub, false);
                }
            baked.RecalculateBounds();
            baked.UploadMeshData(false);
            report = $"faceAxis={axle}, sourceTilt={Vector3.Angle(axle, Vector3.right):F4}deg, sections={checkedSections}, lateral={worstSide * 1000f:F4}mm, orbitDiameter={worstOrbit * 1000f:F4}mm, radialRange={worstRoundness * 1000f:F4}mm, scale={scale}; original topology/UV/material slots retained";
            return baked;
        }
        catch { UnityEngine.Object.DestroyImmediate(baked); throw; }
    }
}
