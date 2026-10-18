#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// Adapted from the confirmed Dodge Ram reload-paint regression fixture.
public static class KiaCeedSportswagonReloadPaintVerification
{
    private const BindingFlags AllMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags AllStatics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly List<Material> detachedMaterials = new List<Material>();
    private static Type NativeType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null)!;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void DetachMaterials(Component owner, string field)
    {
        if (owner.GetType().GetField(field, AllMembers)?.GetValue(owner) is List<Material> owned)
        { detachedMaterials.AddRange(owned); owned.Clear(); }
    }

    public static void Verify()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "-kiaBundleRoot");
        Require(index >= 0, "Missing -kiaBundleRoot.");
        var bundle = AssetBundle.LoadFromFile(System.IO.Path.Combine(args[index+1], "kiaceedsportswagon.unity3d"));
        GameObject? first = null, second = null;
        try
        {
            var prefab = bundle.LoadAsset<GameObject>("Assets/Mods/Kia_Ceed_Sportswagon/KiaCeedSportswagon.prefab");
            first = UnityEngine.Object.Instantiate(prefab); first.SetActive(false);
            second = UnityEngine.Object.Instantiate(prefab); second.SetActive(false);
            ValidateReloadPaint(first, second);
        }
        finally
        {
            if (first != null) UnityEngine.Object.DestroyImmediate(first);
            if (second != null) UnityEngine.Object.DestroyImmediate(second);
            bundle.Unload(true);
        }
        KiaCeedSportswagonDamageVerification.Verify();
    }

    private static void ValidateReloadPaint(GameObject first, GameObject second)
    {
        var paintType = typeof(KiaCeedSportswagonPaintController);
        var vehicleType = NativeType("VehicleController"); var carType = NativeType("CarController");
        var featuresType = NativeType("CarFeatures"); var colorType = NativeType("Data.VehicleColors.VehicleColor");
        var referencesType = NativeType("GlobalReferences");
        var lifecycle = referencesType.BaseType!.Assembly.GetType("Extensions.SingletonLifecycle", true)!;
        var register = lifecycle.GetMethod("RegisterSingleton", AllStatics)!;
        var unregister = lifecycle.GetMethod("UnregisterSingleton", AllStatics)!;
        var oldReferences = lifecycle.GetMethod("GetSingleton", AllStatics)!.MakeGenericMethod(referencesType).Invoke(null, null);
        var wasMissing = (bool)lifecycle.GetMethod("IsTypeMissing", AllStatics)!.Invoke(null, new object[] { referencesType });
        var host = new GameObject("KIA paint registry fixture"); host.SetActive(false);
        var registry = host.AddComponent(referencesType);
        var registryColors = referencesType.GetField("vehicleColors", AllMembers)!;
        var tint = colorType.GetField("tint", AllMembers)!;
        ScriptableObject MakeColor(string name, Color32 value)
        {
            var c = ScriptableObject.CreateInstance(colorType); c.name = name; tint.SetValue(c, value); return c;
        }
        var red = MakeColor("fixture_red", new Color32(225, 25, 15, 255));
        var blue = MakeColor("fixture_blue", new Color32(10, 70, 225, 255));
        var black = MakeColor("fixture_provisional_black", new Color32(13, 13, 13, 255));
        void Palette(params ScriptableObject[] colors)
        {
            var array = Array.CreateInstance(colorType, colors.Length);
            for (var i = 0; i < colors.Length; i++) array.SetValue(colors[i], i);
            registryColors.SetValue(registry, array);
        }
        var instanceField = vehicleType.GetField("vehicleInstance", AllMembers)!;
        var featuresField = vehicleType.GetField("<CarFeatures>k__BackingField", AllMembers)!;
        var liveField = featuresType.GetField("<VehicleColor>k__BackingField", AllMembers)!;
        void SetLive(GameObject root, ScriptableObject color) => liveField.SetValue(featuresField.GetValue(root.GetComponent(carType)), color);
        var applied = new List<KiaCeedSportswagonPaintController>();
        Component Prepare(GameObject root, ScriptableObject saved)
        {
            var car = root.GetComponent(carType);
            var data = Activator.CreateInstance(instanceField.FieldType, new object[] { "kiaceedsportswagon-vehicle:vehicletype_kiaceedsportswagon" })!;
            instanceField.FieldType.GetField("vehicleColorName", AllMembers)!.SetValue(data, saved.name);
            instanceField.SetValue(car, data);
            featuresField.SetValue(car, root.GetComponentsInChildren(featuresType, true).First());
            SetLive(root, black);
            return car;
        }
        KiaCeedSportswagonPaintController Bind(GameObject root, ScriptableObject saved)
        {
            var car = Prepare(root, saved);
            var component = root.AddComponent<KiaCeedSportswagonPaintController>(); applied.Add(component);
            paintType.GetMethod("Initialize", AllMembers)!.Invoke(component, new object?[] { car, null });
            return component;
        }
        (Renderer renderer, int slot)[] Slots(GameObject root) => root.GetComponentsInChildren<Renderer>(true)
            .SelectMany(r => r.sharedMaterials.Select((m, i) => (r, m, i)))
            .Where(x => x.m != null && x.m.name.IndexOf("carpaint", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(x => (x.r, x.i)).ToArray();
        void Check(GameObject root, ScriptableObject color)
        {
            var bindings = Slots(root); Require(bindings.Length > 0, "actual bundled Kia paint bindings");
            var expected = (Color)(Color32)tint.GetValue(color)!; expected.a = 1f;
            foreach (var binding in bindings)
            {
                var block = new MaterialPropertyBlock(); binding.renderer.GetPropertyBlock(block, binding.slot);
                var actual = block.GetColor("_BaseColor");
                Require(Mathf.Abs(actual.r - expected.r) < .00001f && Mathf.Abs(actual.g - expected.g) < .00001f &&
                    Mathf.Abs(actual.b - expected.b) < .00001f && actual.a == 1f, "saved/preview tint on every actual paint slot");
            }
        }
        void Clear(GameObject root) { foreach (var slot in Slots(root)) slot.renderer.SetPropertyBlock(null, slot.slot); }
        void Apply(KiaCeedSportswagonPaintController p) => paintType.GetMethod("ApplyCurrentColor", AllMembers)!.Invoke(p, new object[] { "reload-fixture", false, false });
        var gameEvent = NativeType("GameEvent"); var notify = gameEvent.GetMethod("Invoke", AllStatics)!;
        void Restored() => notify.Invoke(null, new object[] { "vehicle-repainter:color-restored" });
        var sourceMaterials = first.GetComponentsInChildren<Renderer>(true).Concat(second.GetComponentsInChildren<Renderer>(true))
            .SelectMany(r => r.sharedMaterials).Where(m => m != null && m.HasProperty("_BaseColor")).Distinct().ToDictionary(m => m, m => m.GetColor("_BaseColor"));
        try
        {
            Palette(red, blue, black); register.Invoke(null, new object[] { referencesType, registry });
            lifecycle.GetMethod("UnmarkMissingType", AllStatics)!.Invoke(null, new object[] { referencesType });
            // The previous fixture pre-created paint controllers and missed
            // the failing real load order. Start with neither controller and
            // drive the actual runtime/prefab event receivers instead.
            var runtimeHost = new GameObject("KIA cold-load appearance dispatcher"); runtimeHost.SetActive(false);
            var runtime = runtimeHost.AddComponent<KiaCeedSportswagonRuntime>();
            typeof(KiaCeedSportswagonRuntime).GetField("vehicleTypeName", AllMembers)!.SetValue(runtime, "kiaceedsportswagon-vehicle:vehicletype_kiaceedsportswagon");
            var playerVehicles = (IList)NativeType("Helpers.VehicleHelper").GetField("AllPlayerVehicles", AllStatics)!.GetValue(null);
            var firstCar = Prepare(first, red); var secondCar = Prepare(second, blue);
            var loadingLayouts = NativeType("BusinessLayoutSets.BusinessLayoutSetHelper").GetField("loadingLayouts", AllStatics)!;
            var oldLoadingLayouts = loadingLayouts.GetValue(null);
            loadingLayouts.SetValue(null, true);
            void RemovePaint(GameObject root)
            {
                var old = root.GetComponent<KiaCeedSportswagonPaintController>();
                if (old == null) return;
                paintType.GetMethod("UnsubscribeAppearanceEvents", AllMembers)!.Invoke(old, null);
                DetachMaterials(old, "ownedPaintMaterials"); UnityEngine.Object.DestroyImmediate(old); Clear(root);
            }
            try
            {
                typeof(KiaCeedSportswagonRuntime).GetMethod("SubscribeEvents", AllMembers)!.Invoke(runtime, null);
                playerVehicles.Add(firstCar); playerVehicles.Add(secondCar);
                Palette(black); SetLive(first, red); SetLive(second, blue);
                Require(first.GetComponent<KiaCeedSportswagonPaintController>() == null && second.GetComponent<KiaCeedSportswagonPaintController>() == null, "cold load begins without any paint controller");
                Restored(); Check(first, red); Check(second, blue);
                Require(first.GetComponent<KiaCeedSportswagonPaintController>() != null && second.GetComponent<KiaCeedSportswagonPaintController>() != null, "unselected restore event creates both missing paint controllers");
                var ownedA = first.GetComponent<KiaCeedSportswagonPaintController>(); Restored();
                Require(first.GetComponent<KiaCeedSportswagonPaintController>() == ownedA && first.GetComponents<KiaCeedSportswagonPaintController>().Length == 1, "restore bootstrap is idempotent");
                RemovePaint(first); RemovePaint(second); playerVehicles.Remove(firstCar); playerVehicles.Remove(secondCar);
                Palette(red, blue, black); SetLive(first, black); SetLive(second, black);
                foreach (var root in new[] { first, second })
                {
                    var relay = root.GetComponent<KiaCeedSportswagonCreationNotifier>() ?? root.AddComponent<KiaCeedSportswagonCreationNotifier>();
                    Require(relay != null, "owned native-ready relay exists in built prefab");
                    var ready = (IEnumerator)typeof(KiaCeedSportswagonCreationNotifier).GetMethod("NotifyAppearanceReady", AllMembers)!.Invoke(relay, new object[] { root.GetComponent(carType) });
                    Require(ready.MoveNext() && ready.Current == null, "spawn handoff defers exactly one native initialization frame");
                    Require(!ready.MoveNext(), "spawn handoff completes without polling");
                }
                Check(first, red); Check(second, blue); // No selection, restore event or registered-list membership.
                RemovePaint(first); RemovePaint(second);
                playerVehicles.Add(firstCar); playerVehicles.Add(secondCar);
                typeof(KiaCeedSportswagonRuntime).GetMethod("ConfigureExistingAppearance", AllMembers)!.Invoke(runtime, new object[] { "fixture-scene-layout-still-loading" });
                Check(first, red); Check(second, blue);
                Require((int)typeof(KiaCeedSportswagonRuntime).GetField("configuredVehicleIds", AllMembers)!.FieldType.GetProperty("Count")!.GetValue(
                    typeof(KiaCeedSportswagonRuntime).GetField("configuredVehicleIds", AllMembers)!.GetValue(runtime))! == 0, "appearance readiness does not prematurely configure physics or dealer state");
                RemovePaint(first); RemovePaint(second);
                Debug.Log("KIA cold-load bootstrap passed: restore event before paint controllers exist creates two unselected owners; repeated event is idempotent; runtime prefab relay initializes even before player-list registration; scene appearance pass works while layout/dealer/physics setup stays deferred.");
            }
            finally
            {
                typeof(KiaCeedSportswagonRuntime).GetMethod("UnsubscribeEvents", AllMembers)!.Invoke(runtime, null);
                playerVehicles.Remove(firstCar); playerVehicles.Remove(secondCar);
                loadingLayouts.SetValue(null, oldLoadingLayouts);
                RemovePaint(first); RemovePaint(second); UnityEngine.Object.DestroyImmediate(runtimeHost);
            }
            Palette(red, blue, black);
            var a = Bind(first, red); var b = Bind(second, blue);
            Require(!first.activeInHierarchy && !second.activeInHierarchy &&
                !(bool)vehicleType.GetField("controlledByPlayer", AllMembers)!.GetValue(first.GetComponent(carType))! &&
                !(bool)vehicleType.GetField("controlledByPlayer", AllMembers)!.GetValue(second.GetComponent(carType))!, "two parked unoccupied cold-load fixtures");
            Check(first, red); Check(second, blue); Apply(a); Apply(b); Check(first, red); Check(second, blue);
            var finishSlot = Slots(first).First();
            var nativeOverride = new MaterialPropertyBlock();
            nativeOverride.SetColor("_BaseColor", Color.black);
            nativeOverride.SetFloat("_Smoothness", 0.137f);
            finishSlot.renderer.SetPropertyBlock(nativeOverride, finishSlot.slot);
            Restored(); Check(first, red);
            finishSlot.renderer.GetPropertyBlock(nativeOverride, finishSlot.slot);
            Require(Mathf.Abs(nativeOverride.GetFloat("_Smoothness") - 0.137f) < 0.00001f,
                "Color restore discarded repainter finish override.");
            // Optional saved colors absent from the native dealer palette.
            Palette(black); Clear(first); Clear(second);
            SetLive(first, red); SetLive(second, blue); Restored(); Check(first, red); Check(second, blue);
            Clear(first); Clear(second); Restored(); Check(first, red); Check(second, blue);
            SetLive(first, blue); Apply(a); Check(first, blue); Check(second, blue); // Live repaint preview wins after native restore.
            paintType.GetMethod("OnDisable", AllMembers)!.Invoke(a, null); Clear(first); Restored();
            Require(!Slots(first).First().renderer.HasPropertyBlock(), "disabled controller releases color-restore subscription");
            SetLive(first, black); Palette(red, blue, black);
            paintType.GetMethod("OnEnable", AllMembers)!.Invoke(a, null); Check(first, red);
            SetLive(first, red); Apply(a);
            // Legitimately saved black must remain black, not be treated as missing.
            instanceField.FieldType.GetField("vehicleColorName", AllMembers)!.SetValue(instanceField.GetValue(second.GetComponent(carType)), black.name);
            SetLive(second, black); paintType.GetMethod("OnEnable", AllMembers)!.Invoke(b, null); Check(second, black);
            paintType.GetMethod("InitializeForPrivateDriver", AllMembers)!.Invoke(b, new object[] { blue.name, blue });
            Restored(); Check(second, blue); // Explicit traffic/chauffeur color preserved.
            Require(sourceMaterials.All(x => x.Key.GetColor("_BaseColor") == x.Value), "shared/trim materials unchanged by reload paint");
            Require(!typeof(KiaCeedSportswagonMod).Assembly.GetReferencedAssemblies().Any(x => x.Name.IndexOf("Repainter", StringComparison.OrdinalIgnoreCase) >= 0), "no repainter mod assembly dependency");
            Debug.Log("KIA reload paint matrix passed: two inactive unoccupied cars override provisional Black from saved colors; optional palette event without selection, cleared blocks, live preview, disable/re-enable, legitimate saved black, explicit traffic/chauffeur paint and shared-material preservation.");
        }
        finally
        {
            foreach (var root in new[] { first, second })
                foreach (var owner in root.GetComponents<MonoBehaviour>())
                    if (owner != null) { DetachMaterials(owner, "ownedPaintMaterials"); DetachMaterials(owner, "ownedMaterials"); }
            foreach (var component in applied)
            {
                paintType.GetMethod("UnsubscribeAppearanceEvents", AllMembers)!.Invoke(component, null);
                UnityEngine.Object.DestroyImmediate(component);
            }
            unregister.Invoke(null, new object[] { referencesType, registry });
            if (oldReferences != null) register.Invoke(null, new[] { (object)referencesType, oldReferences });
            if (wasMissing) lifecycle.GetMethod("MarkMissingType", AllStatics)!.Invoke(null, new object[] { referencesType });
            foreach (var c in new[] { red, blue, black }) UnityEngine.Object.DestroyImmediate(c);
            UnityEngine.Object.DestroyImmediate(host);
            foreach (var material in detachedMaterials.Distinct()) if (material != null) UnityEngine.Object.DestroyImmediate(material); detachedMaterials.Clear();
        }
    }



}
