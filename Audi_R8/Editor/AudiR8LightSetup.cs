#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class AudiR8LightSetup
{
    private const string OverlayPath = "Assets/Mods/Audi_R8/Models/AudiR8LightOverlays.glb";
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [Serializable] private sealed class Manifest { public Record[] groups = Array.Empty<Record>(); }
    [Serializable] private sealed class Record
    {
        public string group = "", source = "", mesh = "";
        public int sourceVertices, faces;
    }

    public static void Author(GameObject prefab)
    {
        AssetDatabase.ImportAsset(OverlayPath, ImportAssetOptions.ForceSynchronousImport);
        var imported = AssetDatabase.LoadAssetAtPath<GameObject>(OverlayPath);
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.ChangeExtension(OverlayPath, ".json")));
        if (imported == null || manifest.groups.Length != 11)
            throw new InvalidOperationException("R8 authored lamp asset or 11-group manifest is missing.");
        var generated = typeof(AudiR8Mod).Assembly.GetType("AudiR8AuthoredLights", true)!
            .GetField("Groups", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) as Array;
        if (generated == null || generated.Length != manifest.groups.Length)
            throw new InvalidOperationException("R8 compiled lamp definitions differ from the export manifest.");
        var sources = prefab.GetComponentsInChildren<MeshRenderer>(true);
        var meshes = imported.GetComponentsInChildren<MeshFilter>(true);
        var bindings = prefab.GetComponent<AudiR8LightBindings>() ?? prefab.AddComponent<AudiR8LightBindings>();
        var data = new SerializedObject(bindings);
        var entries = data.FindProperty("lamps"); entries.arraySize = manifest.groups.Length;
        for (var i = 0; i < manifest.groups.Length; i++)
        {
            var record = manifest.groups[i];
            var definition = generated.GetValue(i)!;
            foreach (var field in new[] { "group", "source", "mesh", "sourceVertices", "faces" })
                if (!Equals(typeof(Record).GetField(field)!.GetValue(record),
                    definition.GetType().GetField(field, Members)!.GetValue(definition)))
                    throw new InvalidOperationException($"R8 compiled lamp definition differs from Blender export: {record.group}/{field}.");
            var source = sources.Single(r => r.name == record.source);
            var mesh = meshes.Single(f => f.name == record.mesh).sharedMesh;
            if (source.GetComponent<MeshFilter>().sharedMesh.vertexCount != record.sourceVertices ||
                mesh.triangles.Length != record.faces * 3)
                throw new InvalidOperationException($"R8 topology/face count mismatch: {record.group}.");
            var entry = entries.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("group").stringValue = record.group;
            entry.FindPropertyRelative("source").objectReferenceValue = source;
            entry.FindPropertyRelative("mesh").objectReferenceValue = mesh;
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        VerifyBindings(prefab);
    }

    private static void VerifyBindings(GameObject prefab)
    {
        var bindings = prefab.GetComponent<AudiR8LightBindings>();
        if (bindings == null || bindings.Lamps.Length != 11 ||
            bindings.Lamps.Select(l => l.group).Distinct().Count() != 11)
            throw new InvalidOperationException("R8 authored lamp bindings are incomplete or duplicate.");
        foreach (var lamp in bindings.Lamps)
        {
            if (lamp.source == null || lamp.mesh == null || lamp.mesh.triangles.Length == 0)
                throw new InvalidOperationException($"R8 missing lamp source/mesh: {lamp.group}.");
            var original = lamp.source.GetComponent<MeshFilter>().sharedMesh.vertices;
            var toVehicle = prefab.transform.worldToLocalMatrix * lamp.source.transform.localToWorldMatrix;
            var bounds = new Bounds(toVehicle.MultiplyPoint3x4(lamp.mesh.vertices[0]), Vector3.zero);
            foreach (var point in lamp.mesh.vertices)
            {
                if (!original.Any(p => (p - point).sqrMagnitude < 0.00000001f))
                    throw new InvalidOperationException($"R8 lamp {lamp.group} is not in production mesh coordinates: {point}.");
                var p = toVehicle.MultiplyPoint3x4(point); bounds.Encapsulate(p);
                var front = lamp.group == "Headlamps" || lamp.group.StartsWith("DRL", StringComparison.Ordinal);
                var mirror = lamp.group.StartsWith("Indicator_Mirror", StringComparison.Ordinal);
                if (front ? p.z < 1.48f : mirror ? p.z < .35f || p.z > .80f || Math.Abs(p.x) < .80f : p.z > -.5f)
                    throw new InvalidOperationException($"R8 lamp {lamp.group} has incorrect front/rear/mirror bounds: {bounds}.");
                if ((lamp.group.EndsWith("left", StringComparison.Ordinal) || lamp.group == "Indicator_RL") && p.x >= 0f ||
                    (lamp.group.EndsWith("right", StringComparison.Ordinal) || lamp.group == "Indicator_RR") && p.x <= 0f)
                    throw new InvalidOperationException($"R8 lamp {lamp.group} is on the wrong side: {p}.");
            }
            Debug.Log($"AudiR8 authored lamp group={lamp.group} faces={lamp.mesh.triangles.Length / 3} vertices={lamp.mesh.vertexCount} vehicleBounds={bounds}; production local coordinates verified.");
        }
    }

    public static void VerifyBundledPrefab(GameObject prefab, AssetBundle bundle)
    {
        VerifyBindings(prefab);
        var assets = typeof(AudiR8Mod).Assembly.GetType("AudiR8LightAssets", true)!;
        AudiR8HeadlightSetup.Verify(prefab);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        if (!(bool)assets.GetMethod("Load", flags)!.Invoke(null, new object?[] { bundle, null }))
            throw new InvalidOperationException("R8 runtime lamp asset catalog could not load from the built bundle.");
        try
        {
            VerifyRuntimeInstance(prefab, false);
            VerifyRuntimeInstance(prefab, true);
        }
        finally { assets.GetMethod("Clear", flags)!.Invoke(null, null); }
    }

    private static void VerifyRuntimeInstance(GameObject prefab, bool removeSerializedBindings)
    {
        prefab.SetActive(false);
        var fixture = UnityEngine.Object.Instantiate(prefab);
        if (removeSerializedBindings)
            UnityEngine.Object.DestroyImmediate(fixture.GetComponent<AudiR8LightBindings>());
        var type = typeof(AudiR8Mod).Assembly.GetType("AudiR8LightingController", true)!;
        var controller = fixture.AddComponent(type);
        try
        {
            type.GetField("vehicle", Members)!.SetValue(controller, fixture.GetComponent<VehicleController>());
            type.GetField("vehicleRoot", Members)!.SetValue(controller, fixture.transform);
            type.GetMethod("InitializeVisuals", Members)!.Invoke(controller, new object[] { fixture });
            AudiR8HeadlightSetup.Verify(fixture);
            var renderers = fixture.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.name.StartsWith("AudiR8_", StringComparison.Ordinal)).ToArray();
            if (renderers.Length != 14)
                throw new InvalidOperationException($"R8 expected 14 runtime lamp overlays, got {renderers.Length}.");
            var damage = typeof(AudiR8Runtime).GetMethod("IsDeformableExterior", BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (var renderer in renderers)
            {
                var t = renderer.transform;
                if (t.localPosition != Vector3.zero || t.localRotation != Quaternion.identity || t.localScale != Vector3.one ||
                    t.parent.GetComponent<MeshRenderer>() == null ||
                    !(bool)damage.Invoke(null, new object[] { renderer.GetComponent<MeshFilter>() }))
                    throw new InvalidOperationException($"R8 overlay alignment/damage ownership failed: {renderer.name}.");
            }
            CheckState(type, controller, false, false, false, false, false, false);
            CheckState(type, controller, true, false, false, false, false, false);
            CheckState(type, controller, true, true, true, true, false, false);
            CheckState(type, controller, true, true, true, false, true, false);
            CheckState(type, controller, true, true, false, true, false, true);
            CheckState(type, controller, true, true, true, true, true, true);
            var reverse = renderers.Where(r => r.name.StartsWith("AudiR8_ReverseLights", StringComparison.Ordinal)).ToArray();
            if (reverse.Length != 2 || reverse.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length) != 12 ||
                reverse.Any(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length != 6))
                throw new InvalidOperationException("R8 reverse split lost or duplicated authored faces.");
            Debug.Log($"AudiR8 authored lamp states verified: serializedBindingsRemoved={removeSerializedBindings}; active-bundle runtime asset load, 14 compact overlays and two road beams; permanent DRL stays on, side DRL stays off through both blinker phases; independent rear driving/brake/reverse, amber priority on shared reverse faces, left/right/hazards, identity parents and damage ownership.");
        }
        finally
        {
            type.GetMethod("OnDisable", Members)!.Invoke(controller, null);
            // Runtime owns these clones, but an Editor fixture must release
            // them synchronously before its OnDestroy callback.
            foreach (var field in new[] { "generatedObjects", "generatedMaterials", "generatedMeshes" })
            {
                var list = type.GetField(field, Members)!.GetValue(controller)!;
                foreach (var item in ((System.Collections.IEnumerable)list).Cast<UnityEngine.Object>().ToArray())
                    if (item != null) UnityEngine.Object.DestroyImmediate(item);
                list.GetType().GetMethod("Clear")!.Invoke(list, null);
            }
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void CheckState(Type type, Component controller, bool controlled, bool lights,
        bool brake, bool reverse, bool left, bool right)
    {
        var values = new Dictionary<string, bool> { ["controlled"] = controlled, ["lightsOn"] = lights,
            ["braking"] = brake, ["reversing"] = reverse, ["leftBlinker"] = left, ["rightBlinker"] = right };
        foreach (var value in values) type.GetField(value.Key, Members)!.SetValue(controller, value.Value);
        foreach (var flash in new[] { true, false })
        {
            type.GetField("wasBlinking", Members)!.SetValue(controller, left || right);
            type.GetField("blinkerPhaseStartedAt", Members)!.SetValue(controller, Time.unscaledTime - (flash ? 0f : .43f));
            type.GetMethod("ApplyState", Members)!.Invoke(controller, null);
            var expected = new Dictionary<string, bool> {
                ["permanentDaylightOverlay"] = controlled,
                ["daylightOverlay"] = controlled && !left, ["daylightOverlayRight"] = controlled && !right,
                ["headlampOverlay"] = lights, ["rearTailOverlay"] = lights,
                ["rearBrakeOverlay"] = brake, ["reverseOverlay"] = reverse && !(left && flash),
                ["reverseOverlayRight"] = reverse && !(right && flash),
                ["leftBlinkerOverlay"] = left && flash, ["rightBlinkerOverlay"] = right && flash,
                ["rearLeftBlinkerOverlay"] = left && flash, ["rearRightBlinkerOverlay"] = right && flash,
                ["mirrorLeftBlinkerOverlay"] = left && flash, ["mirrorRightBlinkerOverlay"] = right && flash,
                ["leftBeam"] = controlled && lights, ["rightBeam"] = controlled && lights };
            foreach (var pair in expected)
            {
                var target = type.GetField(pair.Key, Members)!.GetValue(controller);
                var enabled = target is Renderer r ? r.enabled : target is Light l && l.enabled;
                if (enabled != pair.Value)
                    throw new InvalidOperationException($"R8 state failed: {pair.Key} expected={pair.Value} flash={flash} left/right={left}/{right}.");
            }
        }
    }
}
