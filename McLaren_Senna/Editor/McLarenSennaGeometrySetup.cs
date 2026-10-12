#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Physical lamp map: Senna owner's handbook, exterior lighting (US p.1.27).
// UV windows identify the emitter islands in the supplied LightA atlas, not the housing.
public static class McLarenSennaGeometrySetup
{
    private const string Root = "Assets/Mods/McLaren_Senna";
    private const string MeshPath = Root + "/Models/GeneratedMeshes/McLarenGeometry.asset";
    private static readonly string[] LampNames = { "FrontLeft", "FrontRight", "HeadLeft", "HeadRight", "RearLeft", "RearRight", "CenterBrake", "Reverse" };

    public static void Author(GameObject root)
    {
        var source = root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.sharedMesh != null && f.name.Contains("Kit2_Light_Geo"));
        var mesh = source.sharedMesh;
        var vertices = mesh.vertices;
        var uv = mesh.uv;
        var triangles = mesh.triangles;
        var sets = LampNames.Select(_ => new List<int>()).ToArray();
        for (var i = 0; i < triangles.Length; i += 3)
        {
            var a = triangles[i]; var b = triangles[i + 1]; var c = triangles[i + 2];
            var p = root.transform.InverseTransformPoint(source.transform.TransformPoint((vertices[a] + vertices[b] + vertices[c]) / 3f));
            var u = (uv[a] + uv[b] + uv[c]) / 3f;
            var side = p.x < 0f ? 0 : 1;
            var group = -1;
            if (p.z > 1f && u.x > .115f && u.y > .715f && u.y < .825f) group = side;
            else if (p.z > 1f && u.x > .13f && u.x < .735f && u.y > .03f && u.y < .455f) group = 2 + side;
            else if (p.z < -1f && Mathf.Abs(p.x) > .25f && u.x < .115f) group = 4 + side;
            else if (p.z < -.8f && Mathf.Abs(p.x) < .14f && p.y > .7f && u.x > .12f && u.x < .94f && u.y > .615f && u.y < .71f) group = 6;
            else if (p.z < -1.8f && Mathf.Abs(p.x) < .05f && p.y < .4f && u.x > .12f && u.x < .94f && u.y > .48f && u.y < .605f) group = 7;
            if (group >= 0) { sets[group].Add(a); sets[group].Add(b); sets[group].Add(c); }
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Models/GeneratedMaterials/McLarenLamp.mat");
        if (material == null)
        {
            material = new Material(Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color")) { name = "McLarenLamp" };
            AssetDatabase.CreateAsset(material, Root + "/Models/GeneratedMaterials/McLarenLamp.mat");
        }
        material.SetColor("_UnlitColor", Color.white);
        for (var i = 0; i < sets.Length; i++)
        {
            if (sets[i].Count == 0) throw new InvalidOperationException("Missing Senna emitter: " + LampNames[i]);
            var host = new GameObject("McLarenLamp_" + LampNames[i]);
            host.transform.SetParent(source.transform, false);
            host.layer = source.gameObject.layer;
            var unique = sets[i].Distinct().ToArray();
            var remap = unique.Select((index, newIndex) => new { index, newIndex }).ToDictionary(x => x.index, x => x.newIndex);
            var normals = mesh.normals;
            var overlay = new Mesh { name = host.name };
            overlay.vertices = unique.Select(index => vertices[index] + source.transform.InverseTransformVector(source.transform.TransformDirection(normals[index]).normalized * .0004f)).ToArray();
            overlay.normals = unique.Select(index => normals[index]).ToArray();
            overlay.triangles = sets[i].Select(index => remap[index]).ToArray();
            overlay.RecalculateBounds();
            host.AddComponent<MeshFilter>().sharedMesh = overlay;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            renderer.renderingLayerMask = source.GetComponent<Renderer>().renderingLayerMask;
            Debug.Log($"Senna emitter {LampNames[i]}: {sets[i].Count / 3} triangles.");
        }

        // Measured LHD wheel/seat in the source model. Derive the wheel center from
        // its welded connected component after normalization, rather than donor offsets.
        var beamTemplate = root.GetComponentsInChildren<Light>(true).FirstOrDefault(l => l.name == "Spotlights");
        if (beamTemplate == null) throw new InvalidOperationException("Native HDRP beam template is missing.");
        var beamRoot = new GameObject("McLarenLightBeams");
        beamRoot.transform.SetParent(root.transform, false);
        for (var side = 0; side < 2; side++)
        {
            var emitter = root.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "McLarenLamp_Head" + (side == 0 ? "Left" : "Right"));
            var beam = UnityEngine.Object.Instantiate(beamTemplate.gameObject, beamRoot.transform, false);
            beam.name = "McLarenBeam_" + (side == 0 ? "Left" : "Right");
            beam.transform.position = emitter.bounds.center + root.transform.forward * McLarenSennaPowertrainCalibration.HeadlightForwardOffset;
            beam.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
            var light = beam.GetComponent<Light>();
            light.enabled = false; light.cookie = null; light.range = 50f;
            light.spotAngle = 62f; light.innerSpotAngle = 38f;
            light.colorTemperature = 5600f; light.useColorTemperature = true;
        }
        beamTemplate.enabled = false;
        var interior = root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.sharedMesh != null && f.name.Contains("Kit2_Interior_Geo"));
        var wheel = FindSteeringBounds(root, interior);
        Anchor(root, "McLarenSteeringAnchor", wheel.center);
        Anchor(root, "McLarenDriverSeat", new Vector3(wheel.center.x, wheel.min.y - .18f, wheel.center.z - .39f));
    }

    public static void RepairHeadlightBeams(GameObject root)
    {
        foreach (var side in new[] { "Left", "Right" })
        {
            var emitter = root.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "McLarenLamp_Head" + side);
            var beam = root.GetComponentsInChildren<Light>(true).Single(l => l.name == "McLarenBeam_" + side);
            beam.transform.position = emitter.bounds.center + root.transform.forward * McLarenSennaPowertrainCalibration.HeadlightForwardOffset;
        }
    }

    public static void VerifyHeadlightBeams(GameObject root)
    {
        foreach (var side in new[] { "Left", "Right" })
        {
            var emitter = root.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "McLarenLamp_Head" + side);
            var beam = root.GetComponentsInChildren<Light>(true).Single(l => l.name == "McLarenBeam_" + side);
            var offset = root.transform.InverseTransformVector(beam.transform.position - emitter.bounds.center);
            if (Vector3.Distance(offset, Vector3.forward * McLarenSennaPowertrainCalibration.HeadlightForwardOffset) > .0001f)
                throw new InvalidOperationException("Senna headlight beam is not 12 cm ahead of its emitter: " + side);
        }
    }

    public static void RepairDriverAnchors(GameObject root)
    {
        var interior = root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.sharedMesh != null && f.name.Contains("Kit2_Interior_Geo"));
        var wheel = FindSteeringBounds(root, interior);
        var seat = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "McLarenDriverSeat");
        seat.localPosition = new Vector3(wheel.center.x, wheel.min.y - .18f, wheel.center.z - .39f);
    }

    private static Bounds FindSteeringBounds(GameObject root, MeshFilter interior)
    {
        var vertices = interior.sharedMesh.vertices.Select(v => root.transform.InverseTransformPoint(interior.transform.TransformPoint(v))).ToArray();
        var parent = Enumerable.Range(0, vertices.Length).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        var weld = new Dictionary<Vector3Int, int>();
        for (var i = 0; i < vertices.Length; i++)
        {
            var key = Vector3Int.RoundToInt(vertices[i] * 100000f);
            if (weld.TryGetValue(key, out var other)) parent[Find(i)] = Find(other); else weld[key] = i;
        }
        var tris = interior.sharedMesh.triangles;
        for (var i = 0; i < tris.Length; i += 3) { parent[Find(tris[i + 1])] = Find(tris[i]); parent[Find(tris[i + 2])] = Find(tris[i]); }
        var bounds = new Dictionary<int, Bounds>();
        for (var i = 0; i < vertices.Length; i++)
        {
            var key = Find(i);
            if (!bounds.TryGetValue(key, out var b)) b = new Bounds(vertices[i], Vector3.zero); else b.Encapsulate(vertices[i]);
            bounds[key] = b;
        }
        var candidates = bounds.Values.Where(b => b.center.x < -.15f && b.center.x > -.6f && b.center.z > .1f && b.center.z < .8f && b.size.x > .28f && b.size.x < .42f && b.size.y > .26f && b.size.y < .4f && b.size.z > .1f && b.size.z < .22f).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException($"Expected one Senna steering wheel component, found {candidates.Length}.");
        return candidates[0];
    }

    private static void Anchor(GameObject root, string name, Vector3 position)
    {
        var anchor = new GameObject(name);
        anchor.transform.SetParent(root.transform, false);
        anchor.transform.localPosition = position;
        Debug.Log($"Senna anchor {name}: {position:F4}");
    }

    // Imported GLB subasset identifiers may change after a fresh import. Persist the
    // actual production meshes/textures, and keep their local IDs on subsequent runs.
    public static void Persist(GameObject root)
    {
        var container = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (container == null) { container = new Mesh { name = "McLarenGeometry" }; AssetDatabase.CreateAsset(container, MeshPath); }
        var existing = AssetDatabase.LoadAllAssetsAtPath(MeshPath).ToDictionary(o => o.name, o => o);
        var meshes = new Dictionary<Mesh, Mesh>();
        var index = 0;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            var source = filter.sharedMesh;
            if (!meshes.TryGetValue(source, out var target))
            {
                var name = $"SennaMesh_{index++:D4}";
                if (existing.TryGetValue(name, out var found)) { target = (Mesh)found; EditorUtility.CopySerialized(source, target); }
                else { target = UnityEngine.Object.Instantiate(source); AssetDatabase.AddObjectToAsset(target, container); }
                target.name = name;
                EditorUtility.SetDirty(target);
                meshes[source] = target;
            }
            filter.sharedMesh = target;
        }
        var textures = new Dictionary<Texture, Texture>();
        index = 0;
        foreach (var material in root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
        {
            foreach (var property in material.GetTexturePropertyNames())
            {
                var source = material.GetTexture(property);
                if (source == null || !AssetDatabase.GetAssetPath(source).EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) continue;
                if (!textures.TryGetValue(source, out var target))
                {
                    var name = $"SennaTexture_{index++:D3}";
                    if (existing.TryGetValue(name, out var found)) { target = (Texture)found; EditorUtility.CopySerialized(source, target); }
                    else { target = UnityEngine.Object.Instantiate(source); AssetDatabase.AddObjectToAsset(target, container); }
                    target.name = name; EditorUtility.SetDirty(target); textures[source] = target;
                }
                material.SetTexture(property, target);
            }
            EditorUtility.SetDirty(material);
        }
        Debug.Log($"Senna persisted {meshes.Count} meshes and {textures.Count} textures.");
    }

    public static void VerifyStoredAndBuilt()
    {
        McLarenSennaTrafficSetup.VerifyTrafficLifecycle();
        McLarenSennaPaintSetup.VerifyParkedSpawnPaint();
        McLarenSennaWashSetup.VerifyNativeWashLifecycle();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/McLarenSenna.prefab");
        Verify(prefab);
        VerifyLightStateBindings(prefab);
        VerifyPurchaseLookup();
        VerifyDriverOccupancy();
        VerifyHandAdjustment();
        VerifyExteriorDeformation();
        McLarenSennaSetup.VerifyFeedbackConfiguration(prefab);
        McLarenSennaSetup.VerifyBuiltBundle();
        McLarenSennaSetup.VerifyPlatformBundleManifests();
    }

    public static void VerifyExteriorDeformation()
    {
        foreach (var excess in new[] { 0f, 1f, 20f, 90f })
        {
            var front = McLarenSennaVisualDamageController.ComputeDentDepth(excess, true, true);
            var rear = McLarenSennaVisualDamageController.ComputeDentDepth(excess, true, false);
            var side = McLarenSennaVisualDamageController.ComputeDentDepth(excess, false, false);
            if (Mathf.Abs(front - Mathf.Clamp(excess*.012f, .04f, .36f)*.90f) > .000001f ||
                Mathf.Abs(rear - Mathf.Clamp(excess*.013f, .04f, .42f)*.85f) > .000001f ||
                Mathf.Abs(side - Mathf.Clamp(excess*.011f, .025f, .34f)) > .000001f)
                throw new InvalidOperationException("Senna visual dent ratios/floor/cap are incorrect.");
        }
        Debug.Log("Senna regional visual depth verified: front 90%, rear 85%, side unchanged; floor and cap included.");
        var root = PrefabUtility.LoadPrefabContents(Root + "/McLarenSenna.prefab");
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var classifier = typeof(McLarenSennaRuntime).GetMethod("IsDeformableExterior", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var all = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToArray();
        var originals = all.ToDictionary(f => f, f => f.sharedMesh);
        var vertices = all.ToDictionary(f => f, f => f.sharedMesh.vertices);
        var sourceNormals = all.ToDictionary(f => f, f => f.sharedMesh.normals);
        var tangents = all.ToDictionary(f => f, f => f.sharedMesh.tangents);
        var filters = all.Where(f => (bool)classifier.Invoke(null, new object[] { f })).ToArray();
        var cloned = new List<Mesh>();
        root.transform.SetPositionAndRotation(new Vector3(70f, 0f, -45f), Quaternion.Euler(0f, 37f, 0f));
        var detailClassifier = typeof(McLarenSennaVisualDamageController).GetMethod("IsAttachedExteriorDetail", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var expectedDetails = filters.ToDictionary(f => f, f => (bool)detailClassifier.Invoke(null, new object[] { f, f.sharedMesh.vertexCount }));
        McLarenSennaVisualDamageController? damage = null;
        try
        {
            damage = root.AddComponent<McLarenSennaVisualDamageController>();
            typeof(McLarenSennaVisualDamageController).GetMethod("Initialize", flags).Invoke(damage,
                new object?[] { root.GetComponents<MonoBehaviour>().Single(c => c != null && c.GetType().Name == "CarController"), root.GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true), null, filters, 5f });
            var cachedDetails = (Dictionary<MeshFilter, bool>)typeof(McLarenSennaVisualDamageController).GetField("attachedDetails", flags).GetValue(damage);
            foreach (var filter in filters)
            {
                if (cachedDetails[filter] != expectedDetails[filter]) throw new InvalidOperationException("Conservative bounds changed attachment classification: " + filter.name);
                if (filter.sharedMesh == originals[filter]) throw new InvalidOperationException("Damage shares the prefab mesh: " + filter.name);
                cloned.Add(filter.sharedMesh);
            }
            var impact = typeof(McLarenSennaVisualDamageController).GetMethod("ApplyImpact", flags);
            foreach (var end in new[] { 1f, -1f })
            {
                var points = new[] { new Vector3(-.5f, end > 0f ? .95f : .49f, end * 2.22f), new Vector3(.5f, end > 0f ? .95f : .49f, end * 2.22f) }
                    .Select(root.transform.TransformPoint).ToArray();
                var normals = new[] { -root.transform.forward * end, -root.transform.forward * end };
                impact.Invoke(damage, new object[] { points, normals, 25f, "editor-fixture" });
            }
            var clearFront = 0; var clearRear = 0; var redRear = 0; var wing = 0; var splitter = 0; var frontSupports = 0; var rearSupports = 0;
            foreach (var filter in all)
            {
                var before = vertices[filter]; var after = filter.sharedMesh.vertices;
                if (filters.Contains(filter) && (!filter.sharedMesh.normals.SequenceEqual(sourceNormals[filter]) || !filter.sharedMesh.tangents.SequenceEqual(tangents[filter])))
                    throw new InvalidOperationException("Crash safeguard changed authored hard-edge shading: " + filter.name);
                if (filters.Contains(filter) && after.Any(p => !filter.sharedMesh.bounds.Contains(p)))
                    throw new InvalidOperationException("Conservative damage bounds exclude a moved vertex: " + filter.name);
                if (!originals[filter].vertices.SequenceEqual(before) || !originals[filter].normals.SequenceEqual(sourceNormals[filter])) throw new InvalidOperationException("Damage mutated shared source: " + filter.name);
                if (!filters.Contains(filter) && !after.SequenceEqual(before)) throw new InvalidOperationException("Damage affected cabin/wheel/interior: " + filter.name);
                for (var i = 0; i < before.Length; i++)
                {
                    if (Vector3.Distance(before[i], after[i]) < .00001f) continue;
                    var point = root.transform.InverseTransformPoint(filter.transform.TransformPoint(before[i]));
                    if (filter.name.StartsWith("McLarenLampCovers")) { if (point.z > 1.45f) clearFront++; if (point.z < -1.8f) clearRear++; }
                    if (filter.name.EndsWith("_red_glass_0", StringComparison.Ordinal)) redRear++;
                    if (filter.name == "McLarenDamageSupports_Front") frontSupports++;
                    if (filter.name == "McLarenDamageSupports_Rear") rearSupports++;
                    if (point.y < .3f && point.z > 1.8f && filter.name.Contains("Carbon1")) splitter++;
                    if (point.y > .98f && point.z < -1.6f && !filter.name.Contains("Badge")) wing++;
                }
            }
            if (clearFront == 0 || clearRear == 0 || redRear == 0 || wing == 0 || splitter == 0 || frontSupports == 0 || rearSupports == 0)
                throw new InvalidOperationException($"Exterior deformation is incomplete: clearFront={clearFront} clearRear={clearRear} redRear={redRear} wing={wing} splitter={splitter} frontSupports={frontSupports} rearSupports={rearSupports}.");
            // Repeated severe impacts cannot accumulate unbounded panel/support displacement.
            for (var pass = 0; pass < 6; pass++)
                foreach (var end in new[] { 1f, -1f })
                    impact.Invoke(damage, new object[] { new[] { root.transform.TransformPoint(new Vector3(0f, .5f, end*2.1f)) }, new[] { -root.transform.forward*end }, 90f, "editor-repeat-fixture" });
            foreach (var filter in filters)
            {
                var after = filter.sharedMesh.vertices;
                for (var i = 0; i < after.Length; i++)
                    if (filter.transform.TransformVector(after[i] - vertices[filter][i]).magnitude > .5001f)
                        throw new InvalidOperationException("Repeated damage exceeded cumulative bound: " + filter.name);
            }
            // Reproduce the native repair mesh swap, then prove owned clone/binding restoration.
            filters[0].sharedMesh = originals[filters[0]];
            typeof(McLarenSennaVisualDamageController).GetMethod("RestoreMeshes", flags).Invoke(damage, null);
            foreach (var filter in filters)
                if (filter.sharedMesh == originals[filter] || !filter.sharedMesh.vertices.SequenceEqual(vertices[filter]) ||
                    !filter.sharedMesh.normals.SequenceEqual(sourceNormals[filter]) || !filter.sharedMesh.tangents.SequenceEqual(tangents[filter]))
                    throw new InvalidOperationException("Damage repair failed to restore an instance-owned mesh: " + filter.name);
            Debug.Log($"Senna damage fixture verified: front clear lens={clearFront}, rear clear lens={clearRear}, red rear lens={redRear}, spoiler vertices={wing}, low front splitter vertices={splitter}, front supports={frontSupports}, rear supports={rearSupports}; repeated severe dents <=0.5m, engine core unchanged; cabin/wheels/shared meshes unchanged, repair restored owned bindings.");
        }
        finally
        {
            foreach (var pair in originals) if (pair.Key != null) pair.Key.sharedMesh = pair.Value;
            foreach (var mesh in cloned) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            if (damage != null) UnityEngine.Object.DestroyImmediate(damage);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // Exercises the real native lookup and our ownership lifecycle without a
    // dealer purchase, vehicle instantiation, money changes or save mutations.
    public static void VerifyPurchaseLookup()
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var registration = typeof(McLarenSennaRuntime).Assembly.GetType("McLarenSennaPlayerPrefabRegistration", true);
        var bind = registration.GetMethod("Bind", flags);
        var release = registration.GetMethod("Release", flags);
        var helper = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Helpers.PrefabHelper")).First(t => t != null);
        var nativeLookup = helper.GetMethod("LoadPrefabAssetByName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        var cache = (System.Collections.IDictionary)helper.GetField("PrefabCache", flags).GetValue(null);
        const string key = "Prefabs/Vehicles/PlayerVehicles/mclarensenna.prefab";
        const string lookup = "Vehicles/PlayerVehicles/mclarensenna";
        var hadPrevious = cache.Contains(key);
        var previous = cache[key];
        var sentinel = new GameObject("McLarenPrefabVerificationSentinel");
        var foreignKey = "McLarenPrefabVerification/" + Guid.NewGuid().ToString("N");
        cache[foreignKey] = sentinel;
        void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        void Bind(GameObject prefab, string source)
        {
            Require((bool)bind.Invoke(null, new object?[] { null, prefab, source }), "McLaren prefab binding failed: " + source);
            Require(ReferenceEquals(nativeLookup.Invoke(null, new object[] { lookup }), prefab), "Native purchase lookup mismatch: " + source);
            Require(ReferenceEquals(cache[foreignKey], sentinel), "Unrelated native cache entry was modified.");
        }
        void Check(GameObject prefab, string source)
        {
            cache[key] = sentinel;
            Bind(prefab, source);
            Bind(prefab, source + "-repeat");
            // Replacing an owned asset must still restore the external owner,
            // rather than leaving the previous McLaren asset in the native cache.
            var replacement = new GameObject("McLarenPrefabVerificationReplacement");
            try { Bind(replacement, source + "-replacement"); release.Invoke(null, null); }
            finally { UnityEngine.Object.DestroyImmediate(replacement); }
            Require(ReferenceEquals(cache[key], sentinel), "Previous cache entry was not restored.");
            Bind(prefab, source + "-before-save-unload");
            // Native PrefabHelper.OnGameUnloaded clears all entries. Remove only
            // ours in this editor check, preserving every unrelated cached asset.
            cache.Remove(key);
            Bind(prefab, source + "-after-save-unload");
            release.Invoke(null, null);
            Require(!cache.Contains(key), "Binding restored an entry from an old save cache generation.");
            cache[key] = sentinel;
            Bind(prefab, source + "-before-other-owner");
            cache[key] = sentinel;
            release.Invoke(null, null);
            Require(ReferenceEquals(cache[key], sentinel), "Unload overwrote another cache owner.");
            Debug.Log("Senna native purchase prefab lookup verified: " + source + ", repeat, save-unload/rebind and ownership-safe release.");
        }
        AssetBundle? bundle = null;
        try
        {
            var type = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Root + "/McLarenSenna.asset");
            Require((string)type.GetType().GetField("vehicleTypeName").GetValue(type) == "mclarensenna-vehicle:vehicletype_mclarensenna",
                "Vehicle type ID and purchase prefab key differ.");
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/McLarenSenna.prefab"), "source");
            bundle = AssetBundle.LoadFromFile(Path.GetFullPath(Root + "/AssetBundles/Windows/mclarensenna.unity3d"));
            Require(bundle != null, "Built McLaren bundle is unavailable.");
            var bundledPrefab = bundle.LoadAsset<GameObject>(Root + "/McLarenSenna.prefab");
            Require(bundledPrefab != null && bundledPrefab.GetComponent(helper.Assembly.GetType("VehicleController", true)) != null,
                "Built McLaren player prefab/controller is unavailable.");
            Check(bundledPrefab, "built-bundle");
        }
        finally
        {
            release.Invoke(null, null);
            if (hadPrevious) cache[key] = previous; else cache.Remove(key);
            cache.Remove(foreignKey);
            UnityEngine.Object.DestroyImmediate(sentinel);
            if (bundle != null) bundle.Unload(true);
        }
    }

    public static void VerifyDriverOccupancy()
    {
        var root = PrefabUtility.LoadPrefabContents(Root + "/McLarenSenna.prefab");
        try
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var driverType = typeof(McLarenSennaRuntime).Assembly.GetType("McLarenSennaDriverController", true);
            var vehicle = root.GetComponents<MonoBehaviour>().First(c => c.GetType().Name == "CarController");
            var driver = (Behaviour)root.AddComponent(driverType);
            driver.enabled = false; // Test transitions without running character animation in edit mode.
            driverType.GetMethod("Initialize", flags).Invoke(driver, new object?[] { vehicle, null });
            var controlled = vehicle.GetType().GetField("controlledByPlayer", flags);
            var occupied = driverType.GetField("occupied", flags);
            var setOccupied = driverType.GetMethod("SetOccupied", flags);
            void Check(bool nativeControl, bool request, bool expected)
            {
                controlled.SetValue(vehicle, nativeControl);
                setOccupied.Invoke(driver, new object[] { request });
                if ((bool)occupied.GetValue(driver) != expected)
                    throw new InvalidOperationException("Driver occupancy accepted a creation callback without native player control.");
            }
            Check(false, true, false); // DeveloperTools creation callback.
            Check(true, true, true);   // Native entry.
            Check(false, false, false); // Exit.
            Check(false, true, false); // A second parked spawn cannot resurrect the clone.
            if (root.GetComponentsInChildren<Transform>(true).Any(t => t.name == "McLarenSenna_SeatedPlayer"))
                throw new InvalidOperationException("Parked McLaren contains a seated player visual.");
            Debug.Log("Senna driver occupancy verified: parked spawn callback, native entry, exit and repeated parked callback.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void VerifyHandAdjustment()
    {
        var root = new GameObject("SennaHandFixture");
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var driverType = typeof(McLarenSennaRuntime).Assembly.GetType("McLarenSennaDriverController", true);
        var limbType = driverType.GetNestedType("SeatedLimb", System.Reflection.BindingFlags.NonPublic);
        try
        {
            var driver = root.AddComponent(driverType);
            driverType.GetField("vehicleRoot", flags).SetValue(driver, root.transform);
            var anchor = new GameObject("SteeringAnchor").transform; anchor.SetParent(root.transform, false); anchor.localPosition = new Vector3(0f, .5f, .58f);
            driverType.GetField("steeringWheel", flags).SetValue(driver, anchor);
            var arms = new List<object>(); var hands = new List<Transform>();
            foreach (var side in new[] { -1f, 1f })
            {
                Transform Bone(string name, Transform parent, Vector3 point) { var t = new GameObject(name).transform; t.SetParent(parent, false); t.position = point; t.rotation = Quaternion.identity; return t; }
                var upper = Bone("Upper", root.transform, new Vector3(side*.2f, .6f, 0f));
                var lower = Bone("Lower", upper, new Vector3(side*.22f, .48f, .22f));
                var hand = Bone("Hand", lower, new Vector3(side*.19f, .5f, .5f));
                var arm = Activator.CreateInstance(limbType, new object[] { upper, lower, hand }); arms.Add(arm); hands.Add(hand);
                driverType.GetField(side < 0f ? "leftArm" : "rightArm", flags).SetValue(driver, arm);
            }
            for (var frame = 0; frame < 30; frame++)
            {
                foreach (var arm in arms) limbType.GetMethod("RestoreAnimationPose", flags).Invoke(arm, null);
                driverType.GetMethod("AlignHandsWithWheel", flags).Invoke(driver, null);
                for (var i = 0; i < hands.Count; i++)
                {
                    var side = i == 0 ? -1f : 1f;
                    if (Vector3.Distance(hands[i].position, new Vector3(side*.19f, .5f, .48f)) > .0001f ||
                        Quaternion.Angle(hands[i].rotation, Quaternion.AngleAxis(side * 5f, Vector3.up)) > .001f)
                        throw new InvalidOperationException("Hand target/grip accumulated rotation or did not move 2 cm rearward.");
                }
            }
            foreach (var arm in arms) limbType.GetMethod("RestoreAnimationPose", flags).Invoke(arm, null);
            if (hands.Any(h => Quaternion.Angle(h.rotation, Quaternion.identity) > .001f)) throw new InvalidOperationException("Hand animation pose did not restore.");
            Debug.Log("Senna hand fixture verified: 2 cm rearward, opposite 5-degree outward grips, 30 frames without accumulation, original animation pose restored.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void VerifyLightStateBindings(GameObject prefab)
    {
        var assembly = typeof(McLarenSennaRuntime).Assembly;
        var lighting = assembly.GetType("McLarenSennaLightingController", true);
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var accessor = lighting.GetMethod("Accessor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        object? Member(object instance, string name) => instance.GetType().GetField(name, flags)?.GetValue(instance) ?? instance.GetType().GetProperty(name, flags)?.GetValue(instance);
        void Check(object? instance, string name, Type result) => accessor.MakeGenericMethod(result).Invoke(null, new[] { instance, name });
        var components = prefab.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null).ToArray();
        var vehicle = components.First(c => c.GetType().Name == "CarController");
        var physics = components.First(c => c.GetType().FullName == "NWH.VehiclePhysics2.VehicleController");
        var blinkers = components.First(c => c.GetType().FullName == "Vehicles.Components.VehicleBlinker");
        Check(vehicle, "ShouldLightsBeOn", typeof(bool));
        Check(Member(physics, "brakes"), "IsBraking", typeof(bool));
        Check(blinkers, "_isLeftBlinkerOn", typeof(bool));
        Check(blinkers, "_isRightBlinkerOn", typeof(bool));
        Check(Member(Member(physics, "powertrain")!, "transmission"), "Gear", typeof(int));
        Debug.Log("Senna cached light state accessors verified against imported game components.");
    }

    public static void Verify(GameObject prefab)
    {
        var classifier = typeof(McLarenSennaRuntime).GetMethod("IsDeformableExterior", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (classifier == null) throw new InvalidOperationException("Missing damage ownership classifier.");
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            var exteriorDetail = filter.name.Contains("Kit2_Light_Geo") || filter.name.StartsWith("McLarenLampCovers") || filter.name.EndsWith("_red_glass_0", StringComparison.Ordinal) || filter.name.StartsWith("McLarenLamp_") || filter.name.Contains("Kit2_Grille") || filter.name.Contains("Kit2_Badge");
            if (exteriorDetail && !(bool)classifier.Invoke(null, new object[] { filter })) throw new InvalidOperationException("Missing damage ownership: " + filter.name);
        }
        foreach (var name in LampNames)
        {
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name == "McLarenLamp_" + name).ToArray();
            if (filters.Length != 1 || filters[0].sharedMesh == null || filters[0].sharedMesh.triangles.Length == 0) throw new InvalidOperationException("Invalid emitter " + name);
        }
        foreach (var name in new[] { "McLarenSteeringAnchor", "McLarenDriverSeat", "McLarenBeam_Left", "McLarenBeam_Right" })
            if (prefab.GetComponentsInChildren<Transform>(true).Count(t => t.name == name) != 1) throw new InvalidOperationException("Missing/duplicate Senna anchor " + name);
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!McLarenSennaMaterials.IsMcLarenRenderer(filter.transform)) continue;
            if (filter.sharedMesh == null) throw new InvalidOperationException("Missing production mesh " + filter.name);
            var renderer = filter.GetComponent<Renderer>();
            if (renderer == null || renderer.sharedMaterials.Length == 0 || renderer.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name.Contains("InternalError"))) throw new InvalidOperationException("Missing production material " + filter.name);
            foreach (var material in renderer.sharedMaterials)
            {
                if (!material.shader.name.StartsWith("HDRP/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Non-HDRP production material: " + filter.name + " / " + material.name + " / " + material.shader.name);
                if (material.name.Contains("Paint") &&
                    (material.shader.name != "HDRP/Lit" || material.GetFloat("_SurfaceType") != 0f ||
                     material.GetColor("_BaseColor").a != 1f || material.GetFloat("_ZWrite") != 1f))
                    throw new InvalidOperationException("Paint is not an opaque HDRP surface: " + material.name);
            }
        }
        var drl = prefab.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "McLarenLamp_FrontLeft").bounds.center;
        var head = prefab.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "McLarenLamp_HeadLeft").bounds.center;
        if (head.y <= drl.y || drl.z < 1f) throw new InvalidOperationException("Senna model orientation is incorrect.");
        Debug.Log("Senna geometry verified: eight emitters, driver/steering anchors, upright model and persistent meshes/materials.");
    }
}
