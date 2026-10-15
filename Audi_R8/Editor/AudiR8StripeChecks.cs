#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BAModAPI;
using Data.VehicleColors;
using UnityEditor;
using UnityEngine;
using UI.Elements;

public static class AudiR8StripeChecks
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const string VehicleTypeName = "audir8-vehicle:vehicletype_audir8";
    private const string FixtureId = "AudiR8StripeEditorFixture";

    public static void Author(GameObject prefab)
    {
        var body = prefab.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "AudiR8DamageBody");
        var bindings = prefab.GetComponent<AudiR8AppearanceBindings>() ?? prefab.AddComponent<AudiR8AppearanceBindings>();
        var data = new SerializedObject(bindings);
        data.FindProperty("bodyRenderer").objectReferenceValue = body;
        data.FindProperty("paintMaterialIndex").intValue = 0;
        data.FindProperty("stripeMaterialIndex").intValue = 1;
        data.ApplyModifiedPropertiesWithoutUndo();
        VerifyBindings(prefab);
    }

    private static void VerifyBindings(GameObject prefab)
    {
        var bindings = prefab.GetComponent<AudiR8AppearanceBindings>();
        if (bindings == null || bindings.BodyRenderer == null || bindings.PaintMaterialIndex != 0 || bindings.StripeMaterialIndex != 1)
            throw new InvalidOperationException("R8 paint/stripe serialized bindings are missing or invalid.");
        var body = bindings.BodyRenderer;
        var mesh = body.GetComponent<MeshFilter>().sharedMesh;
        if (body.name != "AudiR8DamageBody" || body.sharedMaterials.Length != 2 || mesh.subMeshCount != 2 ||
            mesh.GetIndexCount(bindings.StripeMaterialIndex) != 4467)
            throw new InvalidOperationException("R8 stripe binding no longer selects the authored 1489 body triangles.");
    }

    // Use the built bundle and real material initialization. Source-only fixtures
    // with invented runtime names previously missed the in-game mapping failure.
    public static void VerifyBundledPrefab(GameObject prefab)
    {
        VerifyBindings(prefab);
        prefab.SetActive(false);
        var first = UnityEngine.Object.Instantiate(prefab);
        var second = UnityEngine.Object.Instantiate(prefab);
        var options = typeof(AudiR8Mod).Assembly.GetType("AudiR8RacingStripes", true)!;
        var currentField = typeof(SaveGameManager).GetField("_current", StaticMembers)!;
        var dirtyField = typeof(SaveGameManager).GetField("_hasChangeSinceLastSave", StaticMembers);
        var previousSave = currentField.GetValue(null);
        var previousDirty = dirtyField?.GetValue(null);
        var save = new GameInstance();
        var firstCar = new VehicleInstance(VehicleTypeName) { id = "r8-stripe-fixture-one" };
        var secondCar = new VehicleInstance(VehicleTypeName) { id = "r8-stripe-fixture-two" };
        save.VehicleInstances.Add(firstCar); save.VehicleInstances.Add(secondCar);
        var light = ScriptableObject.CreateInstance<VehicleColor>(); light.name = "fixture_white"; light.tint = new Color32(255, 255, 255, 255);
        var dark = ScriptableObject.CreateInstance<VehicleColor>(); dark.name = "fixture_black"; dark.tint = new Color32(5, 5, 5, 255);
        var initialBlack = ScriptableObject.CreateInstance<VehicleColor>(); initialBlack.name = "fixture_native_default"; initialBlack.tint = new Color32(13, 13, 13, 255);
        var referencesType = typeof(CarController).Assembly.GetType("GlobalReferences", true)!;
        var lifecycle = referencesType.BaseType!.Assembly.GetType("Extensions.SingletonLifecycle", true)!;
        var getter = lifecycle.GetMethod("GetSingleton", StaticMembers)!.MakeGenericMethod(referencesType);
        var register = lifecycle.GetMethod("RegisterSingleton", StaticMembers)!;
        var unregister = lifecycle.GetMethod("UnregisterSingleton", StaticMembers)!;
        var previousReferences = getter.Invoke(null, null);
        var wasMissing = (bool)lifecycle.GetMethod("IsTypeMissing", StaticMembers)!.Invoke(null, new object[] { referencesType });
        var referencesHost = new GameObject("Audi R8PaintFixtureRegistry"); referencesHost.SetActive(false);
        Component? references = null;
        var context = new ModContext("Assets/Mods/Audi_R8", FixtureId, new UnityModLogger(FixtureId));
        var nativeClones = new List<Material>();
        try
        {
            currentField.SetValue(null, save);
            references = referencesHost.AddComponent(referencesType);
            referencesType.GetField("vehicleColors", Members)!.SetValue(references, new[] { light, dark, initialBlack });
            register.Invoke(null, new object[] { referencesType, references });
            lifecycle.GetMethod("UnmarkMissingType", StaticMembers)!.Invoke(null, new object[] { referencesType });
            firstCar.vehicleColorName = light.name; secondCar.vehicleColorName = dark.name;
            Invoke(options, "Initialize", context);
            var firstPaint = Prepare(first, firstCar, initialBlack, nativeClones);
            var secondPaint = Prepare(second, secondCar, initialBlack, nativeClones);
            var firstBody = first.GetComponent<AudiR8AppearanceBindings>().BodyRenderer;
            var secondBody = second.GetComponent<AudiR8AppearanceBindings>().BodyRenderer;
            var originalStripe = firstBody.sharedMaterials[1];
            var originalMesh = firstBody.GetComponent<MeshFilter>().sharedMesh;
            var triangles = originalMesh.GetTriangles(1);
            CheckColor(firstBody, 1, Color.black); CheckColor(secondBody, 1, Color.white);
            CheckColor(firstBody, 0, light.tint); CheckColor(secondBody, 0, dark.tint);
            Apply(firstPaint); Apply(secondPaint);
            CheckColor(firstBody, 0, light.tint); CheckColor(secondBody, 0, dark.tint);
            if (first.activeInHierarchy || second.activeInHierarchy || first.GetComponent<CarController>().controlledByPlayer || second.GetComponent<CarController>().controlledByPlayer)
                throw new InvalidOperationException("Cold-load fixture must remain inactive and unoccupied.");
            SetLiveColor(first, light); SetLiveColor(second, dark);
            if (ReferenceEquals(firstBody.sharedMaterials[1], secondBody.sharedMaterials[1]))
                throw new InvalidOperationException("R8 instances share a mutable stripe material.");
            var firstButton = WorkshopButton(save, firstCar.id, true, () => Invoke(options, "SetRacingStripes", save, firstCar.id, false));
            if (firstButton.key != "audir8_stripes_remove" || !firstButton.interactable || firstButton.playerAction != PlayerAction.Confirm)
                throw new InvalidOperationException("Workshop remove action metadata is incorrect.");
            VerifyComposedButtons(firstButton);
            VerifyWorkshopReturnLifecycle(firstButton);
            firstButton.onClick();
            CheckColor(firstBody, 1, light.tint); CheckColor(secondBody, 1, Color.white);
            if (firstBody.sharedMaterials[1] != firstBody.sharedMaterials[0] || !firstBody.enabled ||
                firstBody.GetComponent<MeshFilter>().sharedMesh != originalMesh || !originalMesh.GetTriangles(1).SequenceEqual(triangles))
                throw new InvalidOperationException("Disabled R8 stripes lost shell geometry or failed to use the body finish.");
            CheckColor(firstBody, 1, light.tint);
            var secondButton = WorkshopButton(save, secondCar.id, false, () => {});
            if (secondButton.interactable || secondButton.key != "audir8_stripes_remove")
                throw new InvalidOperationException("Moving vehicle's workshop action is not disabled.");
            if (WorkshopButton(save, firstCar.id, true, () => {}).key != "audir8_stripes_add")
                throw new InvalidOperationException("Workshop label did not switch to the add action.");
            CheckColor(secondBody, 1, Color.white);
            if ((bool)Invoke(options, "GetRacingStripes", save, firstCar.id)! ||
                !(bool)Invoke(options, "GetRacingStripes", save, secondCar.id)!)
                throw new InvalidOperationException("Per-vehicle stripe settings leaked between cars.");

            // Restore a separate save snapshot with independent per-car state.
            var restored = new GameInstance { modData = new Dictionary<string, string>(save.modData) };
            restored.VehicleInstances.Add(firstCar); restored.VehicleInstances.Add(secondCar);
            currentField.SetValue(null, restored);
            if ((bool)Invoke(options, "GetRacingStripes", restored, firstCar.id)!)
                throw new InvalidOperationException("Restoring the save lost the first car's disabled stripes.");
            CheckColor(firstBody, 1, light.tint); CheckColor(secondBody, 1, Color.white);
            WorkshopButton(restored, firstCar.id, true, () => Invoke(options, "SetRacingStripes", restored, firstCar.id, true)).onClick();
            CheckColor(firstBody, 1, Color.black);
            if (firstBody.sharedMaterials[1] != originalStripe) throw new InvalidOperationException("Enabling stripes did not restore the stripe finish.");
            light.tint = new Color32(0, 0, 0, 255); Apply(firstPaint);
            CheckColor(firstBody, 1, Color.white);
            dark.tint = new Color32(255, 255, 255, 255); Apply(secondPaint);
            CheckColor(secondBody, 1, Color.black);
            firstPaint.enabled = false;
            Invoke(options, "SetRacingStripes", restored, firstCar.id, false);
            firstPaint.enabled = true;
            Apply(firstPaint); // Inactive fixture: explicitly exercise the lifecycle reapplication.
            CheckColor(firstBody, 1, light.tint); CheckColor(secondBody, 1, Color.black);
            VerifySupplementalColorRestore(first, second, firstPaint, secondPaint, light, dark, initialBlack, references);
            if (!(bool)Invoke(options, "GetRacingStripes", restored, "new-r8-fixture")!)
                throw new InvalidOperationException("Unconfigured R8s do not default to stripes enabled.");
            if (typeof(AudiR8Mod).Assembly.GetReferencedAssemblies().Any(a => a.Name.IndexOf("Repainter", StringComparison.OrdinalIgnoreCase) >= 0))
                throw new InvalidOperationException("Audi R8 references another mod for its workshop button.");
            Debug.Log("AudiR8 bundled stripes verified: serialized body slot, real material clones, black/white contrast, independent two-car workshop actions, moving action disabled, add/remove labels, body finish and shell retained, repaint, save snapshot restoration, defaults on, no Repainter assembly reference.");
        }
        finally
        {
            Invoke(options, "Shutdown");
            if (references != null) unregister.Invoke(null, new object[] { referencesType, references });
            if (previousReferences != null) register.Invoke(null, new[] { (object)referencesType, previousReferences });
            if (wasMissing) lifecycle.GetMethod("MarkMissingType", StaticMembers)!.Invoke(null, new object[] { referencesType });
            UnityEngine.Object.DestroyImmediate(referencesHost);
            currentField.SetValue(null, previousSave);
            if (dirtyField != null) dirtyField.SetValue(null, previousDirty);
            Release(first); Release(second);
            foreach (var material in nativeClones) if (material != null) UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(light); UnityEngine.Object.DestroyImmediate(dark);
            UnityEngine.Object.DestroyImmediate(initialBlack);
        }
    }

    private static object? Invoke(Type type, string method, params object?[] args) => type.GetMethod(method, StaticMembers)!.Invoke(null, args);
    private static ButtonInfo WorkshopButton(GameInstance save, string id, bool stopped, Action onClick) =>
        (ButtonInfo)typeof(AudiR8WorkshopButtons).GetMethod("CreateButton", StaticMembers)!.Invoke(null, new object[] { save, id, stopped, onClick });

    private static void VerifyComposedButtons(ButtonInfo stripe)
    {
        var nativeCalls = 0;
        var bridges = 0;
        var native = new[]
        {
            new ButtonInfo("RepairVehicle", "fixture_repair", "blue", () => nativeCalls++, PlayerAction.Interact, false),
            new ButtonInfo("WashVehicle", "fixture_wash", "blue", () => nativeCalls++, PlayerAction.SecondaryInteract),
            new ButtonInfo("RepaintVehicle", "fixture_optional_mod", "blue", () => nativeCalls++, PlayerAction.SpecialInteract),
        };
        var compose = typeof(AudiR8WorkshopButtons).GetMethod("ComposeButtons", StaticMembers)!;
        Action<Action> bridge = action => { bridges++; action(); };
        // Repeated native button requests must produce one complete list without
        // executing actions, changing input arrays, or scheduling UI mutation.
        for (var pass = 0; pass < 100; pass++)
        {
            var list = (ButtonInfo[])compose.Invoke(null, new object[] { native, stripe, bridge });
            if (list.Length != 4 || list.Count(b => b.name == stripe.name) != 1 ||
                native.Length != 3 || nativeCalls != 0 || bridges != 0)
                throw new InvalidOperationException("Workshop list composition duplicates buttons or invokes callbacks during refresh.");
            for (var i = 0; i < native.Length; i++)
                if (list[i].name != native[i].name || list[i].key != native[i].key ||
                    list[i].playerAction != native[i].playerAction || list[i].interactable != native[i].interactable)
                    throw new InvalidOperationException("Workshop decoration changed an existing service button.");
        }
        var result = (ButtonInfo[])compose.Invoke(null, new object[] { native, stripe, bridge });
        result[1].onClick();
        if (nativeCalls != 1 || bridges != 1) throw new InvalidOperationException("Native workshop callback was not bridged exactly once.");
        Debug.Log("AudiR8 workshop button lists verified: 100 repeated requests, exactly one stripe action, Repair/Wash/optional Repaint metadata retained, no callback execution during composition, native callback bridged once.");
    }

    private static void VerifyWorkshopReturnLifecycle(ButtonInfo stripe)
    {
        var host = new GameObject("AudiR8WorkshopReturnFixture");
        host.SetActive(false);
        var listener = host.AddComponent<AudiR8WorkshopOverlayLifecycle>();
        var bind = typeof(AudiR8WorkshopOverlayLifecycle).GetMethod("Bind", Members)!;
        var unbind = typeof(AudiR8WorkshopOverlayLifecycle).GetMethod("Unbind", Members)!;
        var compose = typeof(AudiR8WorkshopButtons).GetMethod("ComposeButtons", StaticMembers)!;
        var calls = 0;
        var activations = 0;
        var native = new[] {
            new ButtonInfo("RepairVehicle", "fixture_repair", "blue", () => calls++, PlayerAction.Interact),
            new ButtonInfo("WashVehicle", "fixture_wash", "blue", () => calls++, PlayerAction.SecondaryInteract),
            new ButtonInfo("RepaintVehicle", "fixture_repaint", "blue", () => calls++, PlayerAction.SpecialInteract) };
        var displayed = native;
        Action restore = () => {
            activations++;
            displayed = (ButtonInfo[])compose.Invoke(null, new object[] { native, stripe, (Action<Action>)(action => action()) });
        };
        try
        {
            bind.Invoke(listener, new object[] { restore });
            // Mirror native OverlayUI.Show ordering: assign its original list,
            // then activate the UI root. Purchase and cancel use this same path.
            for (var pass = 1; pass <= 10; pass++)
            {
                host.SetActive(false);
                displayed = native;
                // Rebinding must replace, rather than duplicate, callbacks.
                bind.Invoke(listener, new object[] { restore });
                host.SetActive(true);
                if (activations != pass || displayed.Length != 4 ||
                    displayed.Count(b => b.name == stripe.name) != 1 || calls != 0)
                    throw new InvalidOperationException($"Workshop return did not restore one stripe action synchronously: pass={pass} activations={activations} buttons={displayed.Length} actions={calls}.");
                for (var frame = 0; frame < 100; frame++) host.SetActive(true);
                if (activations != pass)
                    throw new InvalidOperationException("Already visible workshop UI triggered repeated mutation.");
            }
            unbind.Invoke(listener, null);
            host.SetActive(false); host.SetActive(true);
            if (activations != 10 || typeof(AudiR8WorkshopOverlayLifecycle).GetMethod("Update", Members) != null ||
                typeof(AudiR8WorkshopOverlayLifecycle).GetMethod("LateUpdate", Members) != null ||
                typeof(AudiR8WorkshopOverlayLifecycle).GetMethod("OnTransformChildrenChanged", Members) != null)
                throw new InvalidOperationException("Workshop lifecycle observer retains callbacks or has a polling/hierarchy hook.");
            Debug.Log("AudiR8 workshop return verified: 10 immediate hide/show handoffs restore exactly one stripe action; 100 already-active requests per pass cause no UI mutation; rebind is idempotent, unbind releases callback, no polling or hierarchy watcher.");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    private static AudiR8PaintController Prepare(GameObject root, VehicleInstance instance, VehicleColor color, List<Material> nativeClones)
    {
        var car = root.GetComponent<CarController>(); car.vehicleInstance = instance;
        var features = root.GetComponentsInChildren<CarFeatures>(true).First();
        typeof(VehicleController).GetField("<CarFeatures>k__BackingField", Members)!.SetValue(car, features);
        typeof(CarFeatures).GetField("<VehicleColor>k__BackingField", Members)!.SetValue(features, color);
        var body = root.GetComponent<AudiR8AppearanceBindings>().BodyRenderer;
        // Exercise Unity's own material instancing before R8 normalization,
        // as native vehicle initialization can do. Do not invent clone names.
        nativeClones.AddRange(body.materials);
        var materialController = root.AddComponent<AudiR8MaterialController>();
        typeof(AudiR8MaterialController).GetMethod("Initialize", Members)!.Invoke(materialController, new object?[] { null });
        Debug.Log($"AudiR8 bundle stripe fixture renderer='{body.name}', material='{body.sharedMaterials[1].name}'.");
        var paint = root.AddComponent<AudiR8PaintController>();
        typeof(AudiR8PaintController).GetMethod("Initialize", Members)!.Invoke(paint, new object?[] { car, null });
        return paint;
    }

    private static void Apply(AudiR8PaintController paint) => typeof(AudiR8PaintController).GetMethod("ApplyCurrentColor", Members)!.Invoke(paint, new object[] { "stripe-bundle-fixture", true, false });
    private static void SetLiveColor(GameObject root, VehicleColor color) =>
        typeof(CarFeatures).GetField("<VehicleColor>k__BackingField", Members)!.SetValue(root.GetComponent<CarController>().CarFeatures, color);

    private static void VerifySupplementalColorRestore(GameObject first, GameObject second, AudiR8PaintController firstPaint,
        AudiR8PaintController secondPaint, VehicleColor firstColor, VehicleColor secondColor, VehicleColor initialBlack, Component references)
    {
        // Simulate a palette deliberately excluded from the native dealer list.
        references.GetType().GetField("vehicleColors", Members)!.SetValue(references, new[] { initialBlack });
        firstColor.tint = new Color32(0, 157, 154, 255); secondColor.tint = new Color32(120, 0, 0, 255);
        SetLiveColor(first, initialBlack); SetLiveColor(second, initialBlack);
        Apply(firstPaint); Apply(secondPaint);
        // The palette's exact restore notification must update both parked R8s,
        // even with no GameManager/selected vehicle and no player-entry event.
        SetLiveColor(first, firstColor); SetLiveColor(second, secondColor);
        GameEvent.Invoke("vehicle-repainter:color-restored");
        var firstBody = first.GetComponent<AudiR8AppearanceBindings>().BodyRenderer;
        var secondBody = second.GetComponent<AudiR8AppearanceBindings>().BodyRenderer;
        CheckColor(firstBody, 0, firstColor.tint); CheckColor(secondBody, 0, secondColor.tint);
        CheckColor(firstBody, 1, firstColor.tint); CheckColor(secondBody, 1, Color.white);
        // A native restore may clear a slot block without changing color names.
        firstBody.SetPropertyBlock(null, 0); secondBody.SetPropertyBlock(null, 0);
        GameEvent.Invoke("vehicle-repainter:color-restored");
        CheckColor(firstBody, 0, firstColor.tint); CheckColor(secondBody, 0, secondColor.tint);
        // Preserve live-only repaint previews until the saved choice is committed.
        SetLiveColor(first, secondColor); Apply(firstPaint);
        CheckColor(firstBody, 0, secondColor.tint);
        Debug.Log("AudiR8 paint reload verified: saved registered colors override provisional Black before entry; supplemental restore event updates two inactive unselected cars, forces cleared blocks, retains independent stripes and live repaint previews.");
    }
    private static void CheckColor(Renderer renderer, int slot, Color expected)
    {
        var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties, slot);
        var actual = properties.GetColor("_BaseColor");
        if (Vector4.Distance(actual, expected) > .001f)
            throw new InvalidOperationException($"R8 stripe color mismatch: expected={expected}, actual={actual}.");
    }

    private static void Release(GameObject root)
    {
        var paint = root.GetComponent<AudiR8PaintController>();
        if (paint != null)
            typeof(AudiR8PaintController).GetMethod("UnsubscribeAppearanceEvents", Members)!.Invoke(paint, null);
        var controller = root.GetComponent<AudiR8MaterialController>();
        if (controller != null)
        {
            var materials = (List<Material>)typeof(AudiR8MaterialController).GetField("ownedMaterials", Members)!.GetValue(controller);
            foreach (var material in materials) if (material != null) UnityEngine.Object.DestroyImmediate(material);
            materials.Clear();
        }
        UnityEngine.Object.DestroyImmediate(root);
    }
}
