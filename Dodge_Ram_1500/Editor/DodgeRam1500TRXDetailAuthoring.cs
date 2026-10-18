#nullable enable
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Selections are authored once from the normalized supplied GLB. Runtime only
// switches these exact renderers; it never reconstructs geometry or scales lenses.
public static class DodgeRam1500TRXDetailAuthoring
{
    private const string Root = "Assets/Mods/Dodge_Ram_1500/Models/";
    public static readonly string[] LampNames = {
        "DrlLeft", "DrlRight", "DrlIndicatorLeft", "DrlIndicatorRight", "HeadLeft", "HeadRight", "FogLeft", "FogRight",
        "TailLeft", "TailRight", "BrakeLeft", "BrakeRight", "ThirdBrake",
        "ReverseLeft", "ReverseRight", "SignalLeft", "SignalRight",
        "RearSignalLeft", "RearSignalRight", "MirrorLeft", "MirrorRight",
        "FrontMarkers", "RearMarkers", "Plate" };

    public static void Configure(GameObject root, GameObject model)
    {
        var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        var light = renderers.Single(r => r.name.Contains("Light_Geo"));
        var orange = renderers.Single(r => r.name.Contains("ORANGE_GLASS"));
        var white = new Color(.90f, .95f, 1f);
        var amber = new Color(1f, .48f, .008f);
        var crimson = new Color(1f, .008f, .002f);
        // Preserve supported lamps for which the user supplied no replacement.
        for (var side = 0; side < 2; side++)
        {
            var left = side == 0;
            AddLamp(root, light, "Fog" + (left ? "Left" : "Right"),
                p => (left ? p.x < 0f : p.x > 0f) && p.z > 2.56f && p.y > .691f && p.y < .731f && Mathf.Abs(p.x) > .75f, white, 3f);
        }
        AddLamp(root, orange, "FrontMarkers", p => true, amber, 1.6f);
        AddLamp(root, light, "RearMarkers", p => p.z < -2.7f && Mathf.Abs(p.x) < .19f && p.y > .81f && p.y < .86f, crimson, .75f);
        AddLamp(root, light, "Plate", p => p.z < -2.84f && Mathf.Abs(p.x) > .30f && Mathf.Abs(p.x) < .33f && p.y > .70f && p.y < .73f, white, 1.5f);
        DodgeRam1500TRXBlenderLights.Configure(root);
        ConfigureSteering(root, model);
        ConfigureDamageCoverage(root, model);
        ConfigureRearCabinGlass(root, model);
    }

    public static void RebuildRearLightingAndBuild() => DodgeRam1500TRXBlenderLights.RebuildAndBuild();

    private static void AddLamp(GameObject root, MeshRenderer source, string name, Func<Vector3, bool> predicate, Color color, float intensity)
    {
        var original = source.GetComponent<MeshFilter>().sharedMesh;
        var vertices = original.vertices;
        var chosen = new List<int>();
        var triangles = original.triangles;
        for (var i = 0; i < triangles.Length; i += 3)
        {
            var center = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3f;
            if (!predicate(root.transform.InverseTransformPoint(source.transform.TransformPoint(center)))) continue;
            chosen.Add(triangles[i]); chosen.Add(triangles[i + 1]); chosen.Add(triangles[i + 2]);
        }
        AddSelectedLamp(root, source, name, chosen, color, intensity, false);
    }

    internal static void AddSelectedLamp(GameObject root, MeshRenderer source, string name,
        List<int> chosen, Color color, float intensity, bool rear, Mesh? selectedSource = null)
    {
        if (chosen.Count < 3) throw new InvalidOperationException("TRX lamp has no faces: " + name);
        var original = selectedSource ?? source.GetComponent<MeshFilter>().sharedMesh;
        var mesh = Compact(original, chosen, "DodgeRam1500TRXLamp" + name);
        var positions = mesh.vertices;
        var normals = mesh.normals;
        for (var i = 0; i < positions.Length; i++)
        {
            var normal = source.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(normals[i]).normalized;
            var offset = rear ? root.transform.TransformVector(Vector3.back * .0005f) : normal * .0005f;
            positions[i] += source.transform.InverseTransformVector(offset);
        }
        mesh.vertices = positions; mesh.RecalculateBounds();
        var existing = root.GetComponentsInChildren<MeshRenderer>(true).SingleOrDefault(r => r.name == "DodgeRam1500TRX_Lamp_" + name);
        var host = existing != null ? existing.gameObject : new GameObject("DodgeRam1500TRX_Lamp_" + name);
        host.transform.SetParent(source.transform, false);
        host.transform.localPosition = Vector3.zero;
        host.transform.localRotation = Quaternion.identity;
        host.transform.localScale = Vector3.one;
        host.layer = source.gameObject.layer;
        mesh = Save(mesh, Root + "GeneratedMeshes/Lamp" + name + ".asset");
        var filter = host.GetComponent<MeshFilter>();
        if (filter == null) filter = host.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var material = new Material(Shader.Find("HDRP/Unlit")) { name = "DodgeRam1500TRXLamp" + name };
        var emission = color * intensity; emission.a = 1f;
        material.SetColor("_UnlitColor", emission);
        material.SetColor("_EmissiveColor", Color.black);
        material.SetFloat("_SurfaceType", 0f);
        material.SetFloat("_CullMode", 0f);
        material.renderQueue = 2450;
        var renderer = existing != null ? existing : host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Save(material, Root + "GeneratedMaterials/Lamp" + name + ".mat");
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        Debug.Log($"TRX authored lamp {name}: {chosen.Count / 3} faces, bounds={mesh.bounds.size}.");
    }

    private static void ConfigureDamageCoverage(GameObject root, GameObject model)
    {
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            var name = filter.name;
            var clear = (name.Contains("Window_Geo") || name.Contains("WindowInside_Geo")) && name.Contains("Window_Material");
            var mixed = name.Contains("Base_Geo") || name.Contains("Coloured_Geo") ||
                name.Contains("Engine_Geo") || name.Contains("Grille") ||
                name.Contains("Badge_Geo") || name.Contains("Textured_Geo") ||
                name.Contains("ManufacturerPlate_Geo");
            if (!clear && !mixed) continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || filter.sharedMesh == null) continue;
            var source = filter.sharedMesh;
            var positions = source.vertices.Select(v => root.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
            var exterior = new List<int>(); var retained = new List<int>();
            var triangles = source.triangles;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                // Work in the normalized, unlowered model frame. Lamps/mirrors
                // are separated from cabin glazing. Mixed opaque groups retain
                // everything inside the cabin envelope, including seat belts.
                var center = (positions[triangles[i]] + positions[triangles[i + 1]] + positions[triangles[i + 2]]) / 3f;
                var outside = clear
                    ? Mathf.Abs(center.z) > 2.15f || Mathf.Abs(center.x) > 1.075f
                    : center.z < -1.04f || center.z > 1.65f || Mathf.Abs(center.x) > .94f || center.y < .62f || center.y > 1.96f;
                var target = outside ? exterior : retained;
                target.Add(triangles[i]); target.Add(triangles[i + 1]); target.Add(triangles[i + 2]);
            }
            if (exterior.Count == 0) continue;
            var assetName = name.Replace(':', '_');
            var host = new GameObject("DodgeRam1500TRXExterior_" + name);
            host.transform.SetParent(filter.transform, false); host.layer = filter.gameObject.layer;
            host.AddComponent<MeshFilter>().sharedMesh = Save(Compact(source, exterior, host.name), Root + "GeneratedMeshes/Exterior_" + assetName + ".asset");
            var owned = host.AddComponent<MeshRenderer>();
            owned.sharedMaterials = renderer.sharedMaterials; owned.renderingLayerMask = renderer.renderingLayerMask;
            owned.shadowCastingMode = renderer.shadowCastingMode; owned.receiveShadows = renderer.receiveShadows;
            filter.sharedMesh = Save(Compact(source, retained, "DodgeRam1500TRXRetained_" + name), Root + "GeneratedMeshes/Retained_" + assetName + ".asset");
            if (retained.Count == 0) renderer.enabled = false;
            Debug.Log($"TRX exterior ownership {name}: exteriorFaces={exterior.Count / 3}, retainedFaces={retained.Count / 3}.");
        }
    }

    private static void ConfigureRearCabinGlass(GameObject root, GameObject model)
    {
        Material? rearMaterial = null;
        var panes = 0;
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            // These retained original groups contain cabin glazing only; exterior lenses were split above.
            if (!(filter.name.StartsWith("Window_Geo", StringComparison.Ordinal) ||
                  filter.name.StartsWith("WindowInside_Geo", StringComparison.Ordinal)) ||
                !filter.name.Contains("Window_Material")) continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            var source = filter.sharedMesh;
            if (source == null || renderer == null || renderer.sharedMaterials.Length != 1)
                throw new InvalidOperationException("TRX cabin pane source is invalid: " + filter.name);
            var positions = source.vertices.Select(v => root.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
            var rear = new List<int>(); var front = new List<int>();
            var triangles = source.triangles;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = positions[triangles[i]].z < .20f;
                var b = positions[triangles[i + 1]].z < .20f;
                var c = positions[triangles[i + 2]].z < .20f;
                if (a != b || a != c) throw new InvalidOperationException("TRX tint boundary intersects a pane: " + filter.name);
                var selected = a ? rear : front;
                selected.Add(triangles[i]); selected.Add(triangles[i + 1]); selected.Add(triangles[i + 2]);
            }
            if (rear.Count == 0 || front.Count == 0) throw new InvalidOperationException("TRX front/rear cabin pane partition is empty.");
            if (rearMaterial == null)
            {
                rearMaterial = new Material(renderer.sharedMaterial) { name = DodgeRam1500TRXMaterials.RearCabinGlassMarker };
                DodgeRam1500TRXMaterials.PrepareRearCabinGlassMaterial(rearMaterial);
                rearMaterial = Save(rearMaterial, Root + "GeneratedMaterials/RearCabinGlass.mat");
            }
            var assetName = filter.name.Replace(':', '_');
            var host = new GameObject(DodgeRam1500TRXMaterials.RearCabinGlassMarker + "_" + filter.name);
            host.transform.SetParent(filter.transform, false); host.layer = filter.gameObject.layer;
            host.AddComponent<MeshFilter>().sharedMesh = Save(Compact(source, rear, host.name), Root + "GeneratedMeshes/RearCabinGlass_" + assetName + ".asset");
            var tinted = host.AddComponent<MeshRenderer>();
            tinted.sharedMaterial = rearMaterial; tinted.renderingLayerMask = renderer.renderingLayerMask;
            tinted.shadowCastingMode = renderer.shadowCastingMode; tinted.receiveShadows = renderer.receiveShadows;
            filter.sharedMesh = Save(Compact(source, front, "DodgeRam1500TRXFrontCabinGlass_" + filter.name), Root + "GeneratedMeshes/FrontCabinGlass_" + assetName + ".asset");
            panes++;
            Debug.Log($"TRX cabin tint {filter.name}: frontFaces={front.Count / 3}, rearFaces={rear.Count / 3}, whole-pane partition.");
        }
        if (panes != 2) throw new InvalidOperationException("TRX tint requires both inner and outer cabin glazing.");
    }

    private static void ConfigureSteering(GameObject root, GameObject model)
    {
        var pivot = new GameObject("DodgeRam1500TRXSteeringWheel");
        pivot.transform.SetParent(model.transform, false);
        pivot.transform.position = root.transform.TransformPoint(new Vector3(-.479f, 1.377f, .90f));
        pivot.transform.rotation = root.transform.rotation * Quaternion.Euler(24f, 0f, 0f);
        pivot.transform.localScale = Vector3.one;
        var part = 0;
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.name.Contains("Interior")) continue;
            var original = filter.sharedMesh;
            var vertices = original.vertices;
            var moved = new List<int>(); var kept = new List<int>();
            var triangles = original.triangles;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[i]]));
                var b = root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[i + 1]]));
                var c = root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[i + 2]]));
                bool InWheel(Vector3 p) => p.x < -.27f && p.x > -.69f && p.y > 1.19f && p.y < 1.56f && p.z > .785f && p.z < 1.008f;
                var target = InWheel(a) && InWheel(b) && InWheel(c) ? moved : kept;
                target.Add(triangles[i]); target.Add(triangles[i + 1]); target.Add(triangles[i + 2]);
            }
            if (moved.Count == 0) continue;
            var mesh = Compact(original, moved, "DodgeRam1500TRXSteering" + part);
            var positions = mesh.vertices; var normals = mesh.normals; var uvTangents = mesh.tangents;
            var matrix = pivot.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (var i = 0; i < positions.Length; i++)
            {
                positions[i] = matrix.MultiplyPoint3x4(positions[i]);
                normals[i] = matrix.inverse.transpose.MultiplyVector(normals[i]).normalized;
                if (uvTangents.Length == positions.Length)
                {
                    var t = uvTangents[i]; var d = matrix.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                    uvTangents[i] = new Vector4(d.x, d.y, d.z, t.w);
                }
            }
            mesh.vertices = positions; mesh.normals = normals; mesh.tangents = uvTangents; mesh.RecalculateBounds();
            var host = new GameObject(mesh.name); host.transform.SetParent(pivot.transform, false);
            host.AddComponent<MeshFilter>().sharedMesh = Save(mesh, Root + "GeneratedMeshes/Steering" + part + ".asset");
            host.AddComponent<MeshRenderer>().sharedMaterials = filter.GetComponent<MeshRenderer>().sharedMaterials;
            filter.sharedMesh = Save(Compact(original, kept, "DodgeRam1500TRXInterior" + part), Root + "GeneratedMeshes/Interior" + part + ".asset");
            part++;
        }
        if (part < 2) throw new InvalidOperationException("TRX steering wheel and grips were not extracted.");
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || component.GetType().FullName != "NWH.VehiclePhysics2.VehicleController") continue;
            var data = new SerializedObject(component);
            var wheel = data.FindProperty("steering")?.FindPropertyRelative("steeringWheel");
            if (wheel == null) throw new InvalidOperationException("Native steering wheel binding is missing.");
            // Retain the driver pose anchor, but do not animate the interior steering wheel.
            wheel.objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static Mesh Compact(Mesh source, List<int> selected, string name)
    {
        var mapping = new Dictionary<int, int>(); var ids = new List<int>();
        var triangles = new int[selected.Count];
        for (var i = 0; i < selected.Count; i++)
        {
            if (!mapping.TryGetValue(selected[i], out var id)) { id = ids.Count; mapping.Add(selected[i], id); ids.Add(selected[i]); }
            triangles[i] = id;
        }
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        var v = source.vertices; mesh.vertices = ids.Select(i => v[i]).ToArray();
        var n = source.normals; if (n.Length == v.Length) mesh.normals = ids.Select(i => n[i]).ToArray();
        var t = source.tangents; if (t.Length == v.Length) mesh.tangents = ids.Select(i => t[i]).ToArray();
        var uv = source.uv; if (uv.Length == v.Length) mesh.uv = ids.Select(i => uv[i]).ToArray();
        mesh.triangles = triangles; mesh.RecalculateBounds();
        return mesh;
    }
    private static T Save<T>(T value, string path) where T : UnityEngine.Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing == null)
        {
            var authoredName = value.name;
            AssetDatabase.CreateAsset(value, path);
            value.name = authoredName; // Unity otherwise replaces stable markers with the file stem.
            EditorUtility.SetDirty(value);
            return value;
        }
        EditorUtility.CopySerialized(value, existing); EditorUtility.SetDirty(existing);
        UnityEngine.Object.DestroyImmediate(value); return existing;
    }
}
