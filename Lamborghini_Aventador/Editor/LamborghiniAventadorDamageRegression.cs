#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Exercises the live damage controller against the authored Aventador prefab,
/// including its dynamically generated front and rear lamp overlays.
/// </summary>
public static class LamborghiniAventadorDamageRegression
{
    private const string PrefabPath = "Assets/Mods/Lamborghini_Aventador/LamborghiniAventador.prefab";
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void VerifyDamageOwnershipAndRegions()
    {
        var first = LoadDamageFixture("AventadorDamageRegressionFirst");
        Fixture? second = null;
        try
        {
            second = LoadDamageFixture("AventadorDamageRegressionSecond");
            var secondFixture = second.Value;
            var firstStates = GetStates(first.Damage);
            foreach (var state in firstStates)
            {
                var stateType = state.GetType();
                var filter = (MeshFilter)stateType.GetField("Filter", InstanceMembers)!.GetValue(state)!;
                var bounds = (Bounds)stateType.GetField("VehicleBounds", InstanceMembers)!.GetValue(state)!;
                var vertices = (Vector3[])stateType.GetField("VehicleOriginalVertices", InstanceMembers)!.GetValue(state)!;
                var tolerantBounds = bounds;
                tolerantBounds.Expand(0.0001f);
                var indices = (int[]?)stateType.GetField("DeformableIndices", InstanceMembers)!.GetValue(state);
                for (var index = 0; index < (indices?.Length ?? vertices.Length); index++)
                    if (!tolerantBounds.Contains(vertices[indices == null ? index : indices[index]]))
                        throw new InvalidOperationException($"Damage broadphase bounds omit deformable vertices of '{filter.name}'.");
                Debug.Log($"Aventador damage inventory mesh={filter.name} material={MaterialName(filter)} bounds={bounds.ToString("F3")}.");
            }
            var secondStates = GetStates(secondFixture.Damage);
            if (firstStates.Count == 0 || secondStates.Count != firstStates.Count)
                throw new InvalidOperationException(
                    $"Damage ownership did not capture the full exterior inventory: first={firstStates.Count}, second={secondStates.Count}.");
            VerifySourceInventory(first.Root.transform.Find("AventadorBody"), firstStates);
            VerifyWheelAndCaliperExclusions(first.Root.transform, firstStates);
            VerifyReducedDisplacementCaps(first.Damage);

            var firstMeshes = GetRuntimeMeshesByName(firstStates);
            var secondMeshes = GetRuntimeMeshesByName(secondStates);
            VerifyLampStates(first.Lighting);
            AssertContains(firstMeshes, "Aventador_SideLeftIndicator", "left side Blender indicator");
            AssertContains(firstMeshes, "Aventador_SideRightIndicator", "right side Blender indicator");
            AssertContains(firstMeshes, "Aventador_DRL", "front DRL damage overlay");
            AssertContains(firstMeshes, "Aventador_FrontLeftIndicator", "front left indicator after body stance adjustment");
            AssertContains(firstMeshes, "Aventador_FrontRightIndicator", "front right indicator after body stance adjustment");
            AssertContains(firstMeshes, "Aventador_LowBeamEmitter", "low beam emitter after body stance adjustment");
            AssertContains(firstMeshes, "Aventador_HighBeamEmitter", "high beam emitter after body stance adjustment");
            AssertContains(firstMeshes, "Aventador_TailSignature", "rear lamp damage overlay");
            AssertContains(firstMeshes, "Object_14", "authored rear lamp lens");
            AssertContains(firstMeshes, "Object_42", "authored rear lamp lens");
            AssertContains(firstMeshes, "Object_25", "authored front/rear lamp emitter under Light_Geo_lodA_14");

            foreach (var pair in firstMeshes)
            {
                if (!secondMeshes.TryGetValue(pair.Key, out var other) ||
                    ReferenceEquals(pair.Value, other))
                    throw new InvalidOperationException(
                        $"Damage mesh '{pair.Key}' is not independently owned by each vehicle instance.");
            }

            var pristineSecond = CaptureVertices(secondMeshes.Values);
            var mixedMeshes = new[] { firstMeshes["Object_9"], firstMeshes["Object_12"], firstMeshes["Object_40"] };
            var pristineMixed = CaptureVertices(mixedMeshes);
            var frontLampOriginal = firstMeshes["Aventador_DRL"].vertices;
            var frontSourceFilter = GetFilter(firstStates, "Aventador_DRL").transform.parent.GetComponent<MeshFilter>();
            var frontSourceMesh = firstMeshes[frontSourceFilter.name];
            var frontSourceOriginal = frontSourceMesh.vertices;
            var frontGrille = FindMesh(firstMeshes, "Object_31");
            var frontGrilleOriginal = frontGrille.Value.vertices;
            var frontSource = frontSourceFilter;
            var frontOverlaySource = GetFilter(firstStates, "Aventador_DRL").transform.parent.GetComponent<MeshFilter>();
            if (frontOverlaySource != frontSourceFilter)
                throw new InvalidOperationException("Aventador front lamp overlay is not attached to a deformable source renderer.");
            Apply(first.Damage, new Vector3(-0.71f, 0.46f, 1.85f), Vector3.back,
                new Vector3(0.98f, 0.76f, 0.84f), "front impact");
            Flush(first.Damage);
            AssertChanged(firstMeshes["Aventador_DRL"], frontLampOriginal, "front lamp overlay");
            AssertChanged(frontSourceMesh, frontSourceOriginal,
                $"front source lamp '{frontSourceFilter.name}' material={MaterialName(frontSourceFilter)}");
            AssertChanged(frontGrille.Value, frontGrilleOriginal,
                $"front bumper insert '{frontGrille.Key}' material={MaterialName(GetFilter(firstStates, frontGrille.Key))}");
            AssertOverlayTracksSource(GetFilter(firstStates, "Aventador_DRL"), frontLampOriginal,
                frontSource, frontSourceOriginal, "front lamp overlay");
            AssertOnlyRegionChanged(mixedMeshes[0], pristineMixed[mixedMeshes[0]], mixedMeshes[0].name,
                vertex => vertex.z >= 1.365f);
            AssertOnlyRegionChanged(mixedMeshes[1], pristineMixed[mixedMeshes[1]], "front clear lamp cover",
                vertex => vertex.z >= 1.5f);
            AssertUnchanged(new[] { mixedMeshes[2] }, pristineMixed, "front impact changed inner rear lamp glazing");
            AssertUnchanged(secondMeshes.Values, pristineSecond,
                "front impact changed the second Aventador instance");

            var frontImpactMixed = CaptureVertices(mixedMeshes);
            var rearLampOriginal = firstMeshes["Aventador_TailSignature"].vertices;
            var rearSourceLens = firstMeshes["Object_42"];
            var rearSourceLensOriginal = rearSourceLens.vertices;
            var rearAmber = firstMeshes["Object_44"];
            var rearAmberOriginal = rearAmber.vertices;
            var rearGrilleOriginal = firstMeshes["Object_47"].vertices;
            var rearSource = GetFilter(firstStates, "Aventador_TailSignature").transform.parent.GetComponent<MeshFilter>();
            if (rearSource == null)
                throw new InvalidOperationException("Aventador rear lamp overlay is not attached to a deformable source renderer.");
            var rearSourceOriginal = rearSource.sharedMesh.vertices;
            Apply(first.Damage, new Vector3(0.65f, 0.65f, -2.12f), Vector3.forward,
                new Vector3(0.95f, 0.78f, 0.86f), "rear impact");
            Flush(first.Damage);
            AssertChanged(firstMeshes["Aventador_TailSignature"], rearLampOriginal, "rear lamp overlay");
            AssertChanged(rearSourceLens, rearSourceLensOriginal,
                $"rear lamp lens 'Object_42' material={MaterialName(GetFilter(firstStates, "Object_42"))}");
            AssertChanged(rearAmber, rearAmberOriginal,
                $"rear indicator source 'Object_44' material={MaterialName(GetFilter(firstStates, "Object_44"))}");
            AssertChanged(firstMeshes["Object_47"], rearGrilleOriginal, "rear grille Object_47");
            AssertOverlayTracksSource(GetFilter(firstStates, "Aventador_TailSignature"), rearLampOriginal,
                rearSource, rearSourceOriginal, "rear lamp overlay");

            AssertOnlyRegionChanged(mixedMeshes[1], frontImpactMixed[mixedMeshes[1]], "rear clear lamp cover",
                vertex => vertex.z <= -1.9f);
            AssertOnlyRegionChanged(mixedMeshes[2], frontImpactMixed[mixedMeshes[2]], "rear inner lamp glazing",
                vertex => vertex.z <= -1.9f);
            AssertUnchanged(new[] { mixedMeshes[0] }, frontImpactMixed, "rear impact changed the cabin or front luggage bay");

            Restore(first.Damage);
            AssertRestored(firstStates);
            AssertUnchanged(secondMeshes.Values, pristineSecond,
                "repair or first-instance impacts changed the second Aventador instance");
            Debug.Log(
                $"Aventador damage regression passed: source rendered meshes={GetBodyRendererCount(first.Root.transform.Find("AventadorBody"))}, " +
                $"runtime deformable meshes={firstStates.Count}, front lamp={frontSourceFilter.name} ({MaterialName(frontSourceFilter)}), " +
                $"front bumper insert={frontGrille.Key}, rear lens=Object_42 ({MaterialName(GetFilter(firstStates, "Object_42"))}), " +
                "front luggage bay and front/rear lamp covers deformed; fixed cabin glazing and interior preserved; wheels/calipers excluded; repair and instance isolation preserved.");
        }
        finally
        {
            CleanupFixture(first);
            if (second.HasValue)
                CleanupFixture(second.Value);
        }
    }

    internal static void VerifyLightingFromBundle(GameObject bundledPrefab)
    {
        var host = new GameObject("AventadorBundledLightingFixture");
        host.SetActive(false);
        Component? lighting = null;
        try
        {
            var instance = UnityEngine.Object.Instantiate(bundledPrefab, host.transform);
            var vehicle = instance.GetComponent<VehicleController>();
            var body = instance.transform.Find("AventadorBody");
            var holders = Array.FindAll(body.GetComponentsInChildren<MeshFilter>(true),
                filter => filter.name.StartsWith(LamborghiniAventadorLampGeometry.SourcePrefix, StringComparison.Ordinal));
            if (holders.Length != 11)
                throw new InvalidOperationException($"Aventador bundled prefab has {holders.Length}/11 Blender lamp sources.");
            foreach (var holder in holders)
            {
                if (holder.sharedMesh == null || holder.sharedMesh.vertexCount == 0 ||
                    holder.transform.parent.GetComponent<MeshRenderer>() == null ||
                    holder.GetComponent<Renderer>() != null || holder.transform.localPosition != Vector3.zero ||
                    holder.transform.localRotation != Quaternion.identity || holder.transform.localScale != Vector3.one)
                    throw new InvalidOperationException($"Aventador bundled lamp source '{holder.name}' is invalid.");
            }

            var type = FindRuntimeType(AppDomain.CurrentDomain.GetAssemblies(), "LamborghiniAventadorLightingController") ??
                throw new InvalidOperationException("Could not resolve the Aventador lighting controller.");
            lighting = instance.AddComponent(type);
            Invoke(lighting, "Initialize", vehicle, null);
            VerifyLampStates(lighting);
            Debug.Log("Aventador packaged Windows prefab: all 11 native mesh references and 13 runtime lamp outputs verified.");
        }
        finally
        {
            if (lighting != null) CleanupLightingResources(lighting);
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static Fixture LoadDamageFixture(string name)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
            throw new InvalidOperationException("Aventador damage regression could not load the authored vehicle prefab.");
        try
        {
            root.name = name;
            root.SetActive(false);
            var vehicle = root.GetComponent<VehicleController>();
            if (vehicle == null)
                throw new InvalidOperationException($"Damage regression fixture '{name}' has no native VehicleController.");

            var runtimeAssembly = AppDomain.CurrentDomain.GetAssemblies();
            Type? controllerType = null;
            foreach (var candidate in runtimeAssembly)
            {
                controllerType = candidate.GetType("LamborghiniAventadorVisualDamageController", false);
                if (controllerType != null) break;
            }
            if (controllerType == null)
                throw new InvalidOperationException("Aventador damage regression could not resolve the runtime damage controller.");

            var lightingType = Array.Find(runtimeAssembly,
                candidate => candidate.GetType("LamborghiniAventadorLightingController", false) != null)?
                .GetType("LamborghiniAventadorLightingController", false);
            if (lightingType == null)
                throw new InvalidOperationException("Aventador damage regression could not resolve the runtime lighting controller.");
            var lighting = root.GetComponent(lightingType) ?? root.AddComponent(lightingType);
            Invoke(lighting, "Initialize", vehicle, null);

            var damage = root.GetComponent(controllerType) ?? root.AddComponent(controllerType);
            Invoke(damage, "Initialize", vehicle, null);
            return new Fixture(root, damage, lighting);
        }
        catch
        {
            CleanupPrefabContents(root);
            PrefabUtility.UnloadPrefabContents(root);
            throw;
        }
    }

    private static void CleanupFixture(Fixture fixture)
    {
        CleanupPrefabContents(fixture.Root);
        PrefabUtility.UnloadPrefabContents(fixture.Root);
    }

    private static void CleanupPrefabContents(GameObject root)
    {
        var runtimeAssembly = AppDomain.CurrentDomain.GetAssemblies();
        var damageType = FindRuntimeType(runtimeAssembly, "LamborghiniAventadorVisualDamageController");
        var lightingType = FindRuntimeType(runtimeAssembly, "LamborghiniAventadorLightingController");
        if (damageType != null && root.GetComponent(damageType) is Component damage)
            CleanupDamageMeshes(damage);
        if (lightingType != null && root.GetComponent(lightingType) is Component lighting)
            CleanupLightingResources(lighting);
    }

    private static Type? FindRuntimeType(Assembly[] assemblies, string name)
    {
        foreach (var candidate in assemblies)
        {
            var type = candidate.GetType(name, false);
            if (type != null) return type;
        }
        return null;
    }

    private static void CleanupDamageMeshes(Component controller)
    {
        // Repair before discarding the fixture, then detach and immediately
        // destroy its per-instance clones. Clearing owners keeps their normal
        // runtime OnDestroy methods from calling deferred Destroy in edit mode.
        Invoke(controller, "RestoreMeshes", "editor-fixture-cleanup");
        var states = GetStates(controller);
        var destroyed = new HashSet<Mesh>();
        foreach (var state in states)
        {
            var type = state.GetType();
            var filter = (MeshFilter?)type.GetField("Filter", InstanceMembers)?.GetValue(state);
            var runtimeMesh = (Mesh?)type.GetField("RuntimeMesh", InstanceMembers)?.GetValue(state);
            var originalMesh = (Mesh?)type.GetField("OriginalMesh", InstanceMembers)?.GetValue(state);
            if (filter != null && runtimeMesh != null && filter.sharedMesh == runtimeMesh && originalMesh != null)
                filter.sharedMesh = originalMesh;
            if (runtimeMesh != null && destroyed.Add(runtimeMesh))
                UnityEngine.Object.DestroyImmediate(runtimeMesh);
        }
        ClearCollection(controller, "meshes");
        ClearCollection(controller, "ownedMeshes");
        ClearCollection(controller, "modifiedMeshes");
    }

    private static void CleanupLightingResources(Component controller)
    {
        DestroyOwnedObjects(controller, "ownedObjects");
        DestroyOwnedAssets<Material>(controller, "ownedMaterials");
        DestroyOwnedAssets<Mesh>(controller, "ownedMeshes");
    }

    private static void DestroyOwnedObjects(Component controller, string fieldName)
    {
        var items = GetCollection(controller, fieldName);
        if (items == null) return;
        var objects = new List<GameObject>();
        foreach (var item in items)
            if (item is GameObject gameObject && gameObject != null) objects.Add(gameObject);
        items.Clear();
        foreach (var gameObject in objects)
            if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
    }

    private static void DestroyOwnedAssets<T>(Component controller, string fieldName)
        where T : UnityEngine.Object
    {
        var items = GetCollection(controller, fieldName);
        if (items == null) return;
        var assets = new List<T>();
        foreach (var item in items)
            if (item is T asset && asset != null) assets.Add(asset);
        items.Clear();
        foreach (var asset in assets)
            if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
    }

    private static IList? GetCollection(Component controller, string fieldName)
    {
        var field = controller.GetType().GetField(fieldName, InstanceMembers);
        return field?.GetValue(controller) as IList;
    }

    private static void ClearCollection(Component controller, string fieldName) =>
        GetCollection(controller, fieldName)?.Clear();

    private static List<object> GetStates(Component controller)
    {
        var field = controller.GetType().GetField("meshes", InstanceMembers) ??
            throw new InvalidOperationException("Aventador damage controller has no captured mesh inventory.");
        var result = new List<object>();
        foreach (var state in (IEnumerable)(field.GetValue(controller) ?? Array.Empty<object>()))
            result.Add(state);
        return result;
    }

    private static Dictionary<string, Mesh> GetRuntimeMeshesByName(IEnumerable<object> states)
    {
        var result = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            var type = state.GetType();
            var filter = (MeshFilter?)type.GetField("Filter", InstanceMembers)?.GetValue(state);
            var mesh = (Mesh?)type.GetField("RuntimeMesh", InstanceMembers)?.GetValue(state);
            if (filter == null || mesh == null)
                throw new InvalidOperationException("Aventador damage captured an invalid mesh state.");
            result[filter.name] = mesh;
        }
        return result;
    }

    private static MeshFilter GetFilter(IEnumerable<object> states, string name)
    {
        foreach (var state in states)
        {
            var filter = (MeshFilter?)state.GetType().GetField("Filter", InstanceMembers)?.GetValue(state);
            if (filter != null && filter.name == name) return filter;
        }
        throw new InvalidOperationException($"Aventador damage regression could not find filter '{name}'.");
    }

    private static KeyValuePair<string, Mesh> FindMesh(Dictionary<string, Mesh> meshes,
        params string[] candidates)
    {
        foreach (var candidate in candidates)
            if (meshes.TryGetValue(candidate, out var mesh))
                return new KeyValuePair<string, Mesh>(candidate, mesh);
        throw new InvalidOperationException(
            $"Aventador damage regression could not find any authored front bumper mesh: {string.Join(", ", candidates)}.");
    }

    private static int GetBodyRendererCount(Transform? body)
    {
        if (body == null) return 0;
        var count = 0;
        foreach (var filter in body.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh != null && filter.GetComponent<Renderer>() != null) count++;
        return count;
    }

    private static string MaterialName(MeshFilter filter)
    {
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null) return "no-renderer";
        foreach (var material in renderer.sharedMaterials)
            if (material != null) return material.name;
        return "no-material";
    }

    private static void VerifySourceInventory(Transform? body, IEnumerable<object> states)
    {
        if (body == null)
            throw new InvalidOperationException("Aventador damage regression could not find the source body.");
        var owned = new HashSet<MeshFilter>();
        foreach (var state in states)
        {
            var filter = (MeshFilter?)state.GetType().GetField("Filter", InstanceMembers)?.GetValue(state);
            if (filter != null) owned.Add(filter);
        }
        var renderedSourceMeshes = 0;
        foreach (var filter in body.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null)
                continue;
            renderedSourceMeshes++;
            if (!owned.Contains(filter) && !IsExcluded(filter))
                throw new InvalidOperationException(
                    $"Authored exterior mesh '{filter.name}' has no damage owner or explicit exclusion.");
        }
        if (renderedSourceMeshes == 0)
            throw new InvalidOperationException($"Damage regression found only {renderedSourceMeshes} rendered source meshes.");
    }

    private static void VerifyLampStates(Component lighting)
    {
        var switchFields = new[] { "controlled", "lightsOn", "highBeam", "braking", "reversing", "leftIndicator", "rightIndicator" };
        var outputFields = new[] { "drl", "lowBeamEmitter", "highBeamEmitter", "tail", "brake", "centerBrake", "reverse",
            "frontLeftIndicator", "sideLeftIndicator", "rearLeftIndicator", "frontRightIndicator", "sideRightIndicator", "rearRightIndicator" };
        var type = lighting.GetType();
        void Check(bool[] state, params string[] active)
        {
            for (var index = 0; index < switchFields.Length; index++)
                type.GetField(switchFields[index], InstanceMembers)!.SetValue(lighting, state[index]);
            type.GetField("wasBlinking", InstanceMembers)!.SetValue(lighting, false);
            Invoke(lighting, "ApplyState");
            foreach (var name in outputFields)
            {
                var renderer = type.GetField(name, InstanceMembers)!.GetValue(lighting) as MeshRenderer;
                if (renderer == null || renderer.enabled != (Array.IndexOf(active, name) >= 0))
                    throw new InvalidOperationException($"Aventador Blender lamp '{name}' has an incorrect state.");
            }
        }

        Check(new bool[7]);
        Check(new[] { true, false, false, false, false, false, false }, "drl");
        Check(new[] { true, true, false, false, false, false, false }, "drl", "lowBeamEmitter", "tail");
        Check(new[] { true, true, true, true, true, true, false }, "drl", "highBeamEmitter", "brake", "centerBrake",
            "reverse", "frontLeftIndicator", "sideLeftIndicator", "rearLeftIndicator");
        Check(new[] { true, false, false, false, false, true, true }, "drl", "frontLeftIndicator", "sideLeftIndicator",
            "rearLeftIndicator", "frontRightIndicator", "sideRightIndicator", "rearRightIndicator");
        Check(new bool[7]);
        Debug.Log("Aventador Blender lamp state regression passed for all 13 outputs and combined states.");
    }

    private static void VerifyReducedDisplacementCaps(Component controller)
    {
        var type = controller.GetType();
        var dentCap = (float)(type.GetField("MaximumDentDepth", InstanceMembers | BindingFlags.Static)?.GetRawConstantValue() ?? 1f);
        var cumulativeCap = (float)(type.GetField("MaximumCumulativeDisplacement", InstanceMembers | BindingFlags.Static)?.GetRawConstantValue() ?? 1f);
        if (dentCap > 0.12f || cumulativeCap > 0.20f)
            throw new InvalidOperationException(
                $"Aventador damage caps exceed the moderated range: impact={dentCap:0.000}m cumulative={cumulativeCap:0.000}m.");
    }

    private static Dictionary<Mesh, Vector3[]> CaptureVertices(IEnumerable<Mesh> meshes)
    {
        var result = new Dictionary<Mesh, Vector3[]>();
        foreach (var mesh in meshes) result.Add(mesh, mesh.vertices);
        return result;
    }

    private static void Apply(Component controller, Vector3 point, Vector3 inward,
        Vector3 radii, string label)
    {
        var apply = controller.GetType().GetMethod("ApplyImpact", InstanceMembers) ??
            throw new InvalidOperationException("Aventador damage controller has no production impact helper.");
        object?[] arguments = { point, inward, 0.08f, radii, 0 };
        Invoke(apply, controller, arguments);
        if ((int)arguments[4]! == 0)
            throw new InvalidOperationException($"Aventador {label} changed no mesh vertices.");
    }

    private static void Flush(Component controller) => Invoke(controller, "FlushModifiedMeshes");

    private static void Restore(Component controller) => Invoke(controller, "RestoreMeshes", "editor-regression");

    private static void AssertRestored(IEnumerable<object> states)
    {
        foreach (var state in states)
        {
            var type = state.GetType();
            var filter = (MeshFilter?)type.GetField("Filter", InstanceMembers)?.GetValue(state);
            var mesh = (Mesh?)type.GetField("RuntimeMesh", InstanceMembers)?.GetValue(state);
            var original = (Vector3[]?)type.GetField("OriginalVertices", InstanceMembers)?.GetValue(state);
            if (filter == null || mesh == null || original == null || filter.sharedMesh != mesh ||
                !VerticesEqual(mesh.vertices, original))
                throw new InvalidOperationException("Aventador repair did not restore every per-instance mesh exactly.");
        }
    }

    private static void AssertChanged(Mesh mesh, Vector3[] original, string label)
    {
        if (VerticesEqual(mesh.vertices, original))
            throw new InvalidOperationException($"Aventador {label} did not follow its local panel deformation.");
    }

    private static void AssertOverlayTracksSource(MeshFilter overlay, Vector3[] overlayOriginal,
        MeshFilter source, Vector3[] sourceOriginal, string label)
    {
        var sourceCurrent = source.sharedMesh.vertices;
        var overlayCurrent = overlay.sharedMesh.vertices;
        if (overlayOriginal.Length != overlayCurrent.Length || sourceOriginal.Length != sourceCurrent.Length ||
            overlay.transform.parent != source.transform || overlay.transform.localPosition != Vector3.zero ||
            overlay.transform.localRotation != Quaternion.identity || overlay.transform.localScale != Vector3.one)
            throw new InvalidOperationException($"Aventador {label} lost identity alignment with its source.");
        var moved = 0;
        var sourceByPosition = new Dictionary<Vector3Int, int>();
        Vector3Int PositionKey(Vector3 position) => Vector3Int.RoundToInt(position * 100000f);
        for (var index = 0; index < sourceOriginal.Length; index++)
            sourceByPosition[PositionKey(sourceOriginal[index])] = index;
        for (var index = 0; index < overlayCurrent.Length; index++)
        {
            if (!sourceByPosition.TryGetValue(PositionKey(overlayOriginal[index]), out var sourceIndex))
                throw new InvalidOperationException($"Aventador {label} has a vertex outside the source geometry.");
            var sourceDelta = sourceCurrent[sourceIndex] - sourceOriginal[sourceIndex];
            var overlayDelta = overlayCurrent[index] - overlayOriginal[index];
            if (sourceDelta.sqrMagnitude > 0.0000001f || overlayDelta.sqrMagnitude > 0.0000001f)
            {
                moved++;
                if ((sourceDelta - overlayDelta).sqrMagnitude > 0.00000001f)
                    throw new InvalidOperationException($"Aventador {label} detached from its source mesh during deformation.");
            }
        }
        if (moved == 0)
            throw new InvalidOperationException($"Aventador {label} impact did not move any corresponding source vertices.");
    }

    private static void AssertUnchanged(IEnumerable<Mesh> meshes, Dictionary<Mesh, Vector3[]> pristine,
        string failure)
    {
        foreach (var mesh in meshes)
            if (!VerticesEqual(mesh.vertices, pristine[mesh]))
                throw new InvalidOperationException(failure + $" (mesh '{mesh.name}').");
    }

    private static void AssertOnlyRegionChanged(Mesh mesh, Vector3[] original, string label, Func<Vector3, bool> eligible)
    {
        var current = mesh.vertices;
        var moved = 0;
        for (var index = 0; index < current.Length; index++)
        {
            if ((current[index] - original[index]).sqrMagnitude <= 0.00000001f)
                continue;
            if (!eligible(original[index]))
                throw new InvalidOperationException($"Aventador {label} changed fixed cabin or opposite-end geometry.");
            moved++;
        }
        if (moved == 0)
            throw new InvalidOperationException($"Aventador {label} did not move with the local impact.");
        Debug.Log($"Aventador damage regression {label}: moved {moved} of {current.Length} vertices.");
    }

    private static void VerifyWheelAndCaliperExclusions(Transform vehicleRoot, IEnumerable<object> states)
    {
        var owned = new HashSet<MeshFilter>();
        foreach (var state in states)
        {
            var filter = (MeshFilter?)state.GetType().GetField("Filter", InstanceMembers)?.GetValue(state);
            if (filter != null) owned.Add(filter);
        }
        var wheelFilters = 0;
        var caliperFilters = 0;
        foreach (var filter in vehicleRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null)
                continue;
            if (HasAncestor(filter.transform, "Wheel_"))
            {
                wheelFilters++;
                if (owned.Contains(filter) || !IsExcluded(filter))
                    throw new InvalidOperationException($"Rolling wheel mesh '{filter.name}' entered the damage owner.");
            }
            if (HasAncestor(filter.transform, "Caliper_"))
            {
                caliperFilters++;
                if (owned.Contains(filter) || !IsExcluded(filter))
                    throw new InvalidOperationException($"Brake caliper mesh '{filter.name}' entered the damage owner.");
            }
        }
        if (wheelFilters == 0 || caliperFilters == 0)
            throw new InvalidOperationException(
                $"Damage regression could not verify wheel/caliper exclusions: wheels={wheelFilters} calipers={caliperFilters}.");
    }

    private static bool IsExcluded(MeshFilter filter)
    {
        var runtimeAssembly = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var candidate in runtimeAssembly)
        {
            var damageType = candidate.GetType("LamborghiniAventadorVisualDamageController", false);
            var exclude = damageType?.GetMethod("IsExcluded", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (exclude != null) return (bool)exclude.Invoke(null, new object[] { filter })!;
        }
        throw new InvalidOperationException("Aventador damage regression could not resolve the production mesh exclusion check.");
    }

    private static bool HasAncestor(Transform transform, string marker)
    {
        for (var node = transform; node != null; node = node.parent)
            if (node.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static bool VerticesEqual(Vector3[] first, Vector3[] second)
    {
        if (first.Length != second.Length) return false;
        for (var index = 0; index < first.Length; index++)
            if ((first[index] - second[index]).sqrMagnitude > 0.00000001f)
                return false;
        return true;
    }

    private static void AssertContains(Dictionary<string, Mesh> meshes, string name, string label)
    {
        if (!meshes.ContainsKey(name))
            throw new InvalidOperationException($"Aventador damage regression did not capture the {label} '{name}'.");
    }

    private static void Invoke(Component target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, InstanceMembers) ??
            throw new InvalidOperationException($"Aventador regression could not resolve '{methodName}'.");
        Invoke(method, target, arguments);
    }

    private static void Invoke(MethodInfo method, object target, object?[] arguments)
    {
        try
        {
            method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw new InvalidOperationException("Aventador damage regression invocation failed.", exception.InnerException);
        }
    }

    private readonly struct Fixture
    {
        internal readonly GameObject Root;
        internal readonly Component Damage;
        internal readonly Component Lighting;
        internal Fixture(GameObject root, Component damage, Component lighting)
        { Root = root; Damage = damage; Lighting = lighting; }
    }
}
