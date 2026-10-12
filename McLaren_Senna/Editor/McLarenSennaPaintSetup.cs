#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Data.VehicleColors;
using UnityEditor;
using UnityEngine;

public static class McLarenSennaPaintSetup
{
    private const string PrefabPath = "Assets/Mods/McLaren_Senna/McLarenSenna.prefab";
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static void VerifyParkedSpawnPaint()
    {
        var roots = new[] { PrefabUtility.LoadPrefabContents(PrefabPath), PrefabUtility.LoadPrefabContents(PrefabPath) };
        var host = new GameObject("SennaPaintFixtureReferences");
        var referencesType = typeof(VehicleController).Assembly.GetType("GlobalReferences", true)!;
        var lifecycle = referencesType.BaseType!.Assembly.GetType("Extensions.SingletonLifecycle", true)!;
        var statics = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        var previous = lifecycle.GetMethod("GetSingleton", statics)!.MakeGenericMethod(referencesType).Invoke(null, null);
        var wasMissing = (bool)lifecycle.GetMethod("IsTypeMissing", statics)!.Invoke(null, new object[] { referencesType });
        Component? references = null;
        var colors = new[] { new Color32(18, 230, 55, 255), new Color32(190, 25, 230, 255) }
            .Select((tint, index) => { var color = ScriptableObject.CreateInstance<VehicleColor>(); color.name = "SennaPaintFixture" + index; color.tint = tint; return color; }).ToArray();
        try
        {
            references = host.AddComponent(referencesType);
            referencesType.GetField("vehicleColors", Members)!.SetValue(references, colors);
            lifecycle.GetMethod("RegisterSingleton", statics)!.Invoke(null, new object[] { referencesType, references });
            lifecycle.GetMethod("UnmarkMissingType", statics)!.Invoke(null, new object[] { referencesType });
            var start = typeof(McLarenSennaSpawnAppearance).GetMethod("Start", Members)!;
            var apply = typeof(McLarenSennaPaintController).GetMethod("ApplyCurrentColor", Members)!;
            for (var index = 0; index < roots.Length; index++)
            {
                var root = roots[index];
                var car = root.GetComponent<CarController>();
                var features = root.GetComponent<CarFeatures>();
                typeof(VehicleController).GetProperty("CarFeatures", Members)!.SetValue(car, features);
                car.vehicleInstance = new VehicleInstance("mclarensenna-vehicle:vehicletype_mclarensenna") { vehicleColorName = colors[index].name };
                car.controlledByPlayer = false;
                // Native preview color is deliberately stale. Ordered saved color must win.
                features.SetColor(colors[1-index]);
                var spawn = root.GetComponent<McLarenSennaSpawnAppearance>();
                if (spawn == null) throw new InvalidOperationException("Missing serialized parked spawn appearance component.");
                var trim = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null && !m.name.Contains("Paint")).Distinct().ToDictionary(m => m, m => m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.clear);
                start.Invoke(spawn, null);
                CheckPaint(root, colors[index].tint);
                var materials = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).ToArray();
                start.Invoke(spawn, null);
                if (!materials.SequenceEqual(root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)))
                    throw new InvalidOperationException("Repeated spawn initialization replaced instance materials.");
                foreach (var pair in trim)
                    if (pair.Key.HasProperty("_BaseColor") && pair.Key.GetColor("_BaseColor") != pair.Value)
                        throw new InvalidOperationException("Spawn paint mutated a shared trim material.");
                if (car.controlledByPlayer || root.GetComponents<McLarenSennaPaintController>().Length != 1)
                    throw new InvalidOperationException("Spawn paint changed occupancy or duplicated controllers.");
            }
            CheckPaint(roots[0], colors[0].tint); // Second vehicle cannot repaint the first.
            var first = roots[0].GetComponent<CarController>();
            first.vehicleInstance.vehicleColorName = colors[1].name;
            first.CarFeatures.SetColor(colors[1]);
            apply.Invoke(roots[0].GetComponent<McLarenSennaPaintController>(), new object[] { "fixture-repaint" });
            CheckPaint(roots[0], colors[1].tint);
            apply.Invoke(roots[0].GetComponent<McLarenSennaPaintController>(), new object[] { "fixture-repair-reentry" });
            CheckPaint(roots[0], colors[1].tint);
            Debug.Log("Senna parked paint fixture verified: Start before entry, two ordered colors override stale native preview, instance isolation, shared trim unchanged, repeat setup and repaint/repair/reentry preserve paint.");
        }
        finally
        {
            if (references != null) lifecycle.GetMethod("UnregisterSingleton", statics)!.Invoke(null, new object[] { referencesType, references });
            if (previous != null) lifecycle.GetMethod("RegisterSingleton", statics)!.Invoke(null, new[] { (object)referencesType, previous });
            if (wasMissing) lifecycle.GetMethod("MarkMissingType", statics)!.Invoke(null, new object[] { referencesType });
            UnityEngine.Object.DestroyImmediate(host);
            foreach (var root in roots) PrefabUtility.UnloadPrefabContents(root);
            foreach (var color in colors) UnityEngine.Object.DestroyImmediate(color);
        }
    }

    private static void CheckPaint(GameObject root, Color32 tint)
    {
        var block = new MaterialPropertyBlock();
        var count = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            for (var index = 0; index < renderer.sharedMaterials.Length; index++)
            {
                var material = renderer.sharedMaterials[index];
                if (material == null || !material.name.Contains("Paint")) continue;
                renderer.GetPropertyBlock(block, index);
                if (Vector4.Distance(block.GetColor("_BaseColor"), (Color)tint) > .001f)
                    throw new InvalidOperationException("Parked ordered paint mismatch: " + renderer.name);
                count++;
            }
        if (count == 0) throw new InvalidOperationException("Paint fixture found no body slots.");
    }
}
