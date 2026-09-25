#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;

internal sealed class KoenigseggJeskoPaintController : MonoBehaviour
{
    private const int PaintSettlementAttempts = 20;
    private const float PaintSettlementDelay = 0.10f;
    private const string BodyMaterialMarker = "Exterior_mm_ext1";
    private const string CaliperMaterialMarker = "_Caliper";
    private const string InteriorAccentMaterialMarker = "Tela_Int";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");

    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private readonly Dictionary<Material, CaliperPaintTexture> caliperTextures =
        new Dictionary<Material, CaliperPaintTexture>();
    private readonly Dictionary<Material, ExteriorContrastTexture> bodyPaintTextures =
        new Dictionary<Material, ExteriorContrastTexture>();
    private readonly Dictionary<Material, ExteriorContrastTexture> exteriorContrastTextures =
        new Dictionary<Material, ExteriorContrastTexture>();
    private readonly Dictionary<Texture, CaliperPaintTexture.SharedData> sharedCaliperTextures =
        new Dictionary<Texture, CaliperPaintTexture.SharedData>();
    private readonly Dictionary<Texture, ExteriorContrastTexture.SharedData> sharedBodyTextures =
        new Dictionary<Texture, ExteriorContrastTexture.SharedData>();
    private readonly Dictionary<Texture, ExteriorContrastTexture.SharedData> sharedAccentTextures =
        new Dictionary<Texture, ExteriorContrastTexture.SharedData>();
    private readonly Dictionary<Texture, ExteriorContrastTexture.SharedData> sharedLetteringTextures =
        new Dictionary<Texture, ExteriorContrastTexture.SharedData>();
    private readonly HashSet<CaliperPaintTexture> updatedCaliperTextures =
        new HashSet<CaliperPaintTexture>();
    private readonly HashSet<ExteriorContrastTexture> updatedBodyPaintTextures =
        new HashSet<ExteriorContrastTexture>();
    private readonly HashSet<ExteriorContrastTexture> updatedExteriorContrastTextures =
        new HashSet<ExteriorContrastTexture>();
    private VehicleController? vehicle;
    private ModContext? context;
    private string? explicitVehicleColorName;
    private VehicleColor? explicitVehicleColor;
    private VehicleColor? appliedColor;
    private Color32 appliedTint;
    private bool hasAppliedTint;
    private Coroutine? settlementCoroutine;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        var tracePerformance = KoenigseggJeskoDiagnostics.DebugEnabled &&
                               KoenigseggJeskoDiagnostics.PerformanceDebugEnabled;
        var startedAt = tracePerformance ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        vehicle = controller;
        context = modContext;
        explicitVehicleColorName = null;
        explicitVehicleColor = null;
        FindPaintSlots();
        var slotsReadyAt = tracePerformance ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        ApplyCurrentColor("initialize");
        var colorReadyAt = tracePerformance ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        SchedulePaintSettlement("initialize");
        if (tracePerformance)
            KoenigseggJeskoDiagnostics.PerformanceInfo(context,
                $"Jesko performance paint vehicle={controller.GetInstanceID()} " +
                $"slots={slots.Count} mapping={KoenigseggJeskoDiagnostics.Milliseconds(startedAt, slotsReadyAt):0.0}ms " +
                $"apply={KoenigseggJeskoDiagnostics.Milliseconds(slotsReadyAt, colorReadyAt):0.0}ms " +
                $"materials={bodyPaintTextures.Count + exteriorContrastTextures.Count + caliperTextures.Count} " +
                $"uniqueTextures={sharedBodyTextures.Count + sharedAccentTextures.Count + sharedLetteringTextures.Count + sharedCaliperTextures.Count} " +
                $"total={KoenigseggJeskoDiagnostics.Milliseconds(startedAt, System.Diagnostics.Stopwatch.GetTimestamp()):0.0}ms.");
    }

    internal void InitializeForPrivateDriver(
        string? vehicleColorName,
        VehicleColor? vehicleColor)
    {
        vehicle = null;
        context = null;
        explicitVehicleColorName = vehicleColorName;
        explicitVehicleColor = vehicleColor;
        appliedColor = null;
        hasAppliedTint = false;
        FindPaintSlots();
        ApplyCurrentColor("private-driver");
    }

    internal void InitializeForAmbientTraffic(VehicleColor vehicleColor)
    {
        InitializeForPrivateDriver(vehicleColor.name, vehicleColor);
    }

    internal bool HasAppliedColor => hasAppliedTint;

    internal void RestoreAfterVehicleEntered() =>
        SchedulePaintSettlement("vehicle-entered");

    internal bool RefreshCurrentColor(string source, bool settleWhenUnchanged = true)
    {
        var changed = HasCurrentColorChanged();
        if (!ApplyCurrentColor(source))
            return false;
        if (changed || settleWhenUnchanged)
            SchedulePaintSettlement(source);
        return changed;
    }

    private bool HasCurrentColorChanged()
    {
        var selected = ResolveVehicleColor();
        if (selected == null)
            return false;
        var tint = (Color32)selected.tint;
        return !hasAppliedTint ||
               !ReferenceEquals(selected, appliedColor) ||
               !tint.Equals(appliedTint);
    }

    private void SchedulePaintSettlement(string source)
    {
        if (vehicle == null)
            return;
        if (settlementCoroutine != null)
            StopCoroutine(settlementCoroutine);
        settlementCoroutine = StartCoroutine(SettlePaintAfterLifecycle(source));
    }

    private IEnumerator SettlePaintAfterLifecycle(string source)
    {
        // Dealer and repaint transitions can update the selected color after
        // the triggering callback. Sample only this bounded transition window;
        // there is no permanent per-frame paint watcher.
        yield return null;
        yield return new WaitForEndOfFrame();
        for (var attempt = 1; attempt <= PaintSettlementAttempts; attempt++)
        {
            ApplyCurrentColor($"{source}-settle-{attempt}");
            if (attempt < PaintSettlementAttempts)
                yield return new WaitForSecondsRealtime(PaintSettlementDelay);
        }
        settlementCoroutine = null;
    }

    private void FindPaintSlots()
    {
        if (slots.Count > 0)
            return;

        ReleaseRuntimePaintTextures();
        slots.Clear();
        var bodySlots = 0;
        var caliperSlots = 0;
        var exteriorContrastSlots = 0;
        var letteringSlots = 0;
        var interiorAccentSlots = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;
                if (material.name.IndexOf(BodyMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var bodyTexture = GetOrCreateBodyPaintTexture(renderer, index, material);
                    slots.Add(new PaintSlot(
                        renderer,
                        bodyTexture?.Material ?? material,
                        index,
                        PaintCategory.Body,
                        bodyPaintTexture: bodyTexture));
                    bodySlots++;
                }
                else if (IsCaliperRenderer(renderer.transform))
                {
                    var caliperTexture = GetOrCreateCaliperPaintTexture(
                        renderer, index, material);
                    slots.Add(new PaintSlot(
                        renderer,
                        caliperTexture?.Material ?? material,
                        index,
                        PaintCategory.Caliper,
                        caliperTexture));
                    caliperSlots++;
                }
                else if (IsExteriorContrastAccentRenderer(renderer, material))
                {
                    var contrastTexture = GetOrCreateExteriorContrastTexture(
                        renderer, index, material);
                    if (contrastTexture != null)
                    {
                        slots.Add(new PaintSlot(
                            renderer,
                            contrastTexture.Material,
                            index,
                            PaintCategory.ExteriorContrastAccent,
                            exteriorContrastTexture: contrastTexture));
                        exteriorContrastSlots++;
                        if (IsAccentLetteringRenderer(renderer, material))
                            letteringSlots++;
                    }
                }
                else if (material.name.IndexOf(
                             InteriorAccentMaterialMarker,
                             StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    slots.Add(new PaintSlot(renderer, material, index, PaintCategory.InteriorAccent));
                    interiorAccentSlots++;
                }
            }
        }

        KoenigseggJeskoDiagnostics.PaintInfo(context,
            $"KoenigseggJesko paint vehicle={vehicle?.GetInstanceID()}: " +
            $"mapped bodySlots={bodySlots}, caliperSlots={caliperSlots}, " +
            $"exteriorContrastSlots={exteriorContrastSlots}, " +
            $"letteringSlots={letteringSlots} (BOOT sides, rear plate, wing 251), " +
            $"interiorAccentSlots={interiorAccentSlots}; " +
            "rims, tires, carbon, black trim, and glass remain factory materials.");
        if (bodySlots == 0 || caliperSlots != 4 || exteriorContrastSlots == 0 ||
            interiorAccentSlots == 0)
            context?.Logger.Warn(
                $"KoenigseggJesko paint mapping incomplete bodySlots={bodySlots}, " +
                $"caliperSlots={caliperSlots}, exteriorContrastSlots={exteriorContrastSlots}, " +
                $"interiorAccentSlots={interiorAccentSlots}.");
        if (letteringSlots != 3)
            context?.Logger.Warn(
                $"KoenigseggJesko lettering mapping incomplete vehicle={vehicle?.GetInstanceID()}: " +
                $"expected 3 badge renderers (two cabin sides, plate, two wing sides), found {letteringSlots}.");
    }

    private bool ApplyCurrentColor(string source)
    {
        var selected = ResolveVehicleColor();
        if (selected == null)
            return false;
        var tint = (Color32)selected.tint;
        if (hasAppliedTint && ReferenceEquals(selected, appliedColor) && tint.Equals(appliedTint))
            return true;

        var selectedColor = (Color)tint;
        selectedColor.a = 1f;
        updatedCaliperTextures.Clear();
        updatedBodyPaintTextures.Clear();
        updatedExteriorContrastTextures.Clear();
        foreach (var slot in slots)
        {
            if (slot.Category == PaintCategory.Body && slot.BodyPaintTexture != null)
            {
                slot.Renderer.SetPropertyBlock(null, slot.MaterialIndex);
                if (updatedBodyPaintTextures.Add(slot.BodyPaintTexture))
                    slot.BodyPaintTexture.Apply(selectedColor);
                continue;
            }
            if (slot.Category == PaintCategory.Caliper && slot.CaliperTexture != null)
            {
                slot.Renderer.SetPropertyBlock(null, slot.MaterialIndex);
                if (updatedCaliperTextures.Add(slot.CaliperTexture))
                    slot.CaliperTexture.Apply(selectedColor);
                continue;
            }
            if (slot.Category == PaintCategory.ExteriorContrastAccent &&
                slot.ExteriorContrastTexture != null)
            {
                slot.Renderer.SetPropertyBlock(null, slot.MaterialIndex);
                if (updatedExteriorContrastTextures.Add(slot.ExteriorContrastTexture))
                    slot.ExteriorContrastTexture.Apply(selectedColor);
                continue;
            }

            // Match the Bugatti's interior hierarchy: accent upholstery and trim
            // follow the selected paint while staying slightly lighter than the
            // body. Rims are intentionally not registered as paint slots.
            var color = slot.Category == PaintCategory.InteriorAccent
                ? Color.Lerp(selectedColor, Color.white, 0.12f)
                : selectedColor;
            color.a = 1f;
            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            if (slot.Material.HasProperty(BaseColor)) properties.SetColor(BaseColor, color);
            if (slot.Material.HasProperty(ColorProperty)) properties.SetColor(ColorProperty, color);
            if (slot.Material.HasProperty(BaseColorFactor)) properties.SetColor(BaseColorFactor, color);
            slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
        }

        appliedColor = selected;
        appliedTint = tint;
        hasAppliedTint = true;
        KoenigseggJeskoDiagnostics.PaintInfo(context,
            $"KoenigseggJesko paint vehicle={vehicle?.GetInstanceID()}: " +
            $"applied color='{((UnityEngine.Object)selected).name}' rgba={tint} " +
            $"to {slots.Count} body/caliper slots; exteriorContrast=" +
            $"{(UseDarkContrast(selectedColor) ? "50%-gray" : "white")}, source='{source}'.");
        return true;
    }

    private static bool UseDarkContrast(Color paint)
    {
        // VehicleColor tint values are stored in display (sRGB) space. Judging
        // their linearized values made even visibly light pinks and lavenders
        // select white. Use perceived display luminance, as the caliper lettering
        // and exterior accent are visual contrast decisions rather than lighting.
        var luminance = paint.r * 0.2126f + paint.g * 0.7152f + paint.b * 0.0722f;
        return luminance >= 0.40f;
    }

    private CaliperPaintTexture? GetOrCreateCaliperPaintTexture(
        Renderer renderer,
        int materialIndex,
        Material sourceMaterial)
    {
        if (!caliperTextures.TryGetValue(sourceMaterial, out var state))
        {
            state = CaliperPaintTexture.Create(sourceMaterial, sharedCaliperTextures);
            if (state == null)
            {
                context?.Logger.Warn(
                    $"KoenigseggJesko paint vehicle={vehicle?.GetInstanceID()}: caliper " +
                    $"texture unavailable material='{sourceMaterial.name}'; using flat paint fallback.");
                return null;
            }
            caliperTextures.Add(sourceMaterial, state);
        }

        var materials = renderer.sharedMaterials;
        materials[materialIndex] = state.Material;
        renderer.sharedMaterials = materials;
        return state;
    }

    private static bool IsCaliperRenderer(Transform transform)
    {
        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("KoenigseggFixedCaliper", StringComparison.Ordinal) ||
                current.name.IndexOf("BRAKE_CALIPER", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private ExteriorContrastTexture? GetOrCreateBodyPaintTexture(
        Renderer renderer,
        int materialIndex,
        Material sourceMaterial)
    {
        if (!bodyPaintTextures.TryGetValue(sourceMaterial, out var state))
        {
            state = ExteriorContrastTexture.Create(
                sourceMaterial,
                tintNonAccentPixels: true,
                recolorBadgeLettering: false,
                sharedBodyTextures);
            if (state == null)
            {
                context?.Logger.Warn(
                    $"KoenigseggJesko paint vehicle={vehicle?.GetInstanceID()}: body " +
                    $"texture unavailable material='{sourceMaterial.name}'; using property-block fallback.");
                return null;
            }
            bodyPaintTextures.Add(sourceMaterial, state);
        }

        var materials = renderer.sharedMaterials;
        materials[materialIndex] = state.Material;
        renderer.sharedMaterials = materials;
        return state;
    }

    private ExteriorContrastTexture? GetOrCreateExteriorContrastTexture(
        Renderer renderer,
        int materialIndex,
        Material sourceMaterial)
    {
        if (!exteriorContrastTextures.TryGetValue(sourceMaterial, out var state))
        {
            var lettering = IsAccentLetteringRenderer(renderer, sourceMaterial);
            state = ExteriorContrastTexture.Create(
                sourceMaterial,
                tintNonAccentPixels: false,
                recolorBadgeLettering: lettering,
                cache: lettering ? sharedLetteringTextures : sharedAccentTextures);
            if (state == null)
            {
                context?.Logger.Warn(
                    $"KoenigseggJesko paint vehicle={vehicle?.GetInstanceID()}: exterior " +
                    $"contrast texture unavailable material='{sourceMaterial.name}'.");
                return null;
            }
            exteriorContrastTextures.Add(sourceMaterial, state);
        }

        var materials = renderer.sharedMaterials;
        materials[materialIndex] = state.Material;
        renderer.sharedMaterials = materials;
        return state;
    }

    private static bool IsExteriorContrastAccentRenderer(Renderer renderer, Material material)
    {
        var rendererName = renderer.name;
        if (IsAccentLetteringRenderer(renderer, material))
            return true;

        var supportedMaterial =
            material.name.IndexOf("Exterior_mm_misc1", StringComparison.OrdinalIgnoreCase) >= 0 ||
            material.name.IndexOf("CARBON", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!supportedMaterial)
            return false;

        return rendererName.IndexOf("BODY_mm_misc", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("BOOT_mm_misc", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("HOOD_mm_misc", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("DOOR_LEFT_mm_misc", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("DOOR_RIGHT_mm_misc", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("FRONTBUMPER_mm_misc_CARBON", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("REARBUMPER_mm_misc_CARBON", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsAccentLetteringRenderer(Renderer renderer, Material material) =>
        material.name.IndexOf("Exterior_mm_badges1", StringComparison.OrdinalIgnoreCase) >= 0 &&
        (renderer.name.IndexOf("BOOT_mm_badges", StringComparison.OrdinalIgnoreCase) >= 0 ||
         renderer.name.IndexOf("REARBUMPER_mm_badges", StringComparison.OrdinalIgnoreCase) >= 0 ||
         renderer.name.IndexOf("WING_REAR_mm_badges", StringComparison.OrdinalIgnoreCase) >= 0);

    private void ReleaseRuntimePaintTextures()
    {
        var released = new HashSet<CaliperPaintTexture>();
        foreach (var state in caliperTextures.Values)
        {
            if (state != null && released.Add(state))
                state.Dispose();
        }
        caliperTextures.Clear();

        foreach (var state in bodyPaintTextures.Values)
            state?.Dispose();
        bodyPaintTextures.Clear();

        foreach (var state in exteriorContrastTextures.Values)
            state?.Dispose();
        exteriorContrastTextures.Clear();
        foreach (var state in sharedCaliperTextures.Values)
            state.Dispose();
        sharedCaliperTextures.Clear();
        foreach (var state in sharedBodyTextures.Values)
            state.Dispose();
        sharedBodyTextures.Clear();
        foreach (var state in sharedAccentTextures.Values)
            state.Dispose();
        sharedAccentTextures.Clear();
        foreach (var state in sharedLetteringTextures.Values)
            state.Dispose();
        sharedLetteringTextures.Clear();
    }

    private void OnDestroy()
    {
        if (settlementCoroutine != null)
            StopCoroutine(settlementCoroutine);
        settlementCoroutine = null;
        ReleaseRuntimePaintTextures();
    }

    private VehicleColor? ResolveVehicleColor()
    {
        if (explicitVehicleColor != null &&
            (string.IsNullOrEmpty(explicitVehicleColorName) ||
             string.Equals(explicitVehicleColor.name, explicitVehicleColorName,
                 StringComparison.Ordinal)))
            return explicitVehicleColor;
        var live = vehicle?.CarFeatures?.VehicleColor;
        if (live != null)
            return live;
        var colorName = vehicle?.vehicleInstance?.vehicleColorName;
        return !string.IsNullOrEmpty(colorName) && VehicleHelper.TryGetVehicleColor(colorName, out var saved)
            ? saved
            : null;
    }

    private readonly struct PaintSlot
    {
        internal PaintSlot(
            Renderer renderer,
            Material material,
            int materialIndex,
            PaintCategory category,
            CaliperPaintTexture? caliperTexture = null,
            ExteriorContrastTexture? bodyPaintTexture = null,
            ExteriorContrastTexture? exteriorContrastTexture = null)
        {
            Renderer = renderer;
            Material = material;
            MaterialIndex = materialIndex;
            Category = category;
            CaliperTexture = caliperTexture;
            BodyPaintTexture = bodyPaintTexture;
            ExteriorContrastTexture = exteriorContrastTexture;
        }

        internal readonly Renderer Renderer;
        internal readonly Material Material;
        internal readonly int MaterialIndex;
        internal readonly PaintCategory Category;
        internal readonly CaliperPaintTexture? CaliperTexture;
        internal readonly ExteriorContrastTexture? BodyPaintTexture;
        internal readonly ExteriorContrastTexture? ExteriorContrastTexture;
    }

    private enum PaintCategory
    {
        Body,
        Caliper,
        ExteriorContrastAccent,
        InteriorAccent,
    }

    private sealed class CaliperPaintTexture
    {
        private readonly SharedData shared;

        private CaliperPaintTexture(Material material, SharedData shared)
        {
            Material = material;
            this.shared = shared;
        }

        internal Material Material { get; }

        internal static CaliperPaintTexture? Create(
            Material sourceMaterial, Dictionary<Texture, SharedData> cache)
        {
            var sourceTexture = FindBaseTexture(sourceMaterial);
            if (sourceTexture == null)
                return null;

            if (!cache.TryGetValue(sourceTexture, out var shared))
            {
                shared = SharedData.Create(sourceTexture);
                cache.Add(sourceTexture, shared);
            }

            var runtimeMaterial = UnityEngine.Object.Instantiate(sourceMaterial);
            runtimeMaterial.name = sourceMaterial.name + "_RuntimeCaliperPaint";
            runtimeMaterial.hideFlags = HideFlags.DontSave;
            SetTexture(runtimeMaterial, "_BaseColorMap", shared.Texture);
            SetTexture(runtimeMaterial, "_MainTex", shared.Texture);
            SetTexture(runtimeMaterial, "baseColorTexture", shared.Texture);
            SetMaterialColor(runtimeMaterial, Color.white);
            return new CaliperPaintTexture(runtimeMaterial, shared);
        }

        internal void Apply(Color paint) => shared.Apply(paint);

        internal void Dispose()
        {
            if (Material != null) UnityEngine.Object.Destroy(Material);
        }

        internal sealed class SharedData
        {
            private readonly Color32[] sourcePixels;
            private Color32? appliedPaint;

            private SharedData(Texture2D texture, Color32[] sourcePixels)
            {
                Texture = texture;
                this.sourcePixels = sourcePixels;
            }

            internal Texture2D Texture { get; }

            internal static SharedData Create(Texture sourceTexture)
            {

            var tracePerformance = KoenigseggJeskoDiagnostics.DebugEnabled &&
                                   KoenigseggJeskoDiagnostics.PerformanceDebugEnabled;
            var startedAt = tracePerformance ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

            var width = sourceTexture.width;
            var height = sourceTexture.height;
            var temporary = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            Texture2D readable;
            try
            {
                Graphics.Blit(sourceTexture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, true, false)
                {
                    name = sourceTexture.name + "_RuntimeCaliperPaint",
                    hideFlags = HideFlags.DontSave,
                    filterMode = sourceTexture.filterMode,
                    wrapMode = sourceTexture.wrapMode,
                    anisoLevel = sourceTexture.anisoLevel,
                };
                readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                readable.Apply(true, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }

            var pixelsReadyAt = tracePerformance ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

            var sourcePixels = readable.GetPixels32();
            if (tracePerformance)
                KoenigseggJeskoDiagnostics.PerformanceInfo(
                    KoenigseggJeskoPrivateDriverSupport.Context,
                    $"Jesko performance paint-texture kind=caliper source='{sourceTexture.name}' " +
                    $"size={width}x{height} readable={(sourceTexture is Texture2D source2D && source2D.isReadable)} " +
                    $"readback={KoenigseggJeskoDiagnostics.Milliseconds(startedAt, pixelsReadyAt):0.0}ms " +
                    $"finish={KoenigseggJeskoDiagnostics.Milliseconds(pixelsReadyAt, System.Diagnostics.Stopwatch.GetTimestamp()):0.0}ms.");
            return new SharedData(readable, sourcePixels);
            }

            internal void Apply(Color paint)
            {
            var paintBytes = (Color32)paint;
            if (appliedPaint.HasValue && appliedPaint.Value.Equals(paintBytes))
                return;
            var text = UseDarkContrast(paint) ? Color.black : Color.white;
            var output = new Color32[sourcePixels.Length];
            for (var index = 0; index < sourcePixels.Length; index++)
            {
                var source = (Color)sourcePixels[index];
                if (source.a <= 0.01f)
                {
                    output[index] = sourcePixels[index];
                    continue;
                }

                Color.RGBToHSV(source, out _, out var saturation, out var value);
                var greenBody = source.g > source.r * 1.08f &&
                                source.g > source.b * 1.08f &&
                                saturation > 0.18f;
                var lettering = saturation < 0.22f && value > 0.38f;
                Color recolored;
                if (greenBody)
                {
                    var shade = Mathf.Lerp(0.38f, 1.12f, value);
                    recolored = new Color(
                        Mathf.Clamp01(paint.r * shade),
                        Mathf.Clamp01(paint.g * shade),
                        Mathf.Clamp01(paint.b * shade),
                        source.a);
                }
                else if (lettering)
                {
                    var shade = Mathf.Lerp(0.72f, 1f, value);
                    recolored = new Color(text.r * shade, text.g * shade, text.b * shade, source.a);
                }
                else
                {
                    recolored = source;
                }
                output[index] = recolored;
            }

            Texture.SetPixels32(output);
            Texture.Apply(true, false);
            appliedPaint = paintBytes;
            }

            internal void Dispose()
            {
                if (Texture != null) UnityEngine.Object.Destroy(Texture);
            }
        }

        private static Texture? FindBaseTexture(Material material)
        {
            if (material.HasProperty("_BaseColorMap") && material.GetTexture("_BaseColorMap") != null)
                return material.GetTexture("_BaseColorMap");
            if (material.HasProperty("baseColorTexture") && material.GetTexture("baseColorTexture") != null)
                return material.GetTexture("baseColorTexture");
            return material.mainTexture;
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property)) material.SetTexture(property, texture);
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, color);
            if (material.HasProperty(ColorProperty)) material.SetColor(ColorProperty, color);
            if (material.HasProperty(BaseColorFactor)) material.SetColor(BaseColorFactor, color);
        }
    }

    private sealed class ExteriorContrastTexture
    {
        private readonly SharedData shared;

        private ExteriorContrastTexture(Material material, SharedData shared)
        {
            Material = material;
            this.shared = shared;
        }

        internal Material Material { get; }

        internal static ExteriorContrastTexture? Create(
            Material sourceMaterial,
            bool tintNonAccentPixels,
            bool recolorBadgeLettering,
            Dictionary<Texture, SharedData> cache)
        {
            var sourceTexture = FindBaseTexture(sourceMaterial);
            if (sourceTexture == null)
                return null;

            if (!cache.TryGetValue(sourceTexture, out var shared))
            {
                shared = SharedData.Create(sourceTexture, tintNonAccentPixels, recolorBadgeLettering);
                cache.Add(sourceTexture, shared);
            }

            var runtimeMaterial = UnityEngine.Object.Instantiate(sourceMaterial);
            runtimeMaterial.name = sourceMaterial.name +
                                   (tintNonAccentPixels
                                       ? "_RuntimeBodyContrast"
                                       : "_RuntimeExteriorContrast");
            runtimeMaterial.hideFlags = HideFlags.DontSave;
            SetTexture(runtimeMaterial, "_BaseColorMap", shared.Texture);
            SetTexture(runtimeMaterial, "_MainTex", shared.Texture);
            SetTexture(runtimeMaterial, "baseColorTexture", shared.Texture);
            SetMaterialColor(runtimeMaterial, Color.white);
            return new ExteriorContrastTexture(runtimeMaterial, shared);
        }

        internal void Apply(Color paint) => shared.Apply(paint);

        internal void Dispose()
        {
            if (Material != null) UnityEngine.Object.Destroy(Material);
        }

        internal sealed class SharedData
        {
            private readonly Color32[] sourcePixels;
            private readonly Color32[] outputPixels;
            private readonly bool[] accentPixels;
            private readonly bool tintNonAccentPixels;
            private bool? appliedDarkContrast;
            private Color32? appliedPaint;

            private SharedData(Texture2D texture, Color32[] sourcePixels,
                Color32[] outputPixels, bool[] accentPixels, bool tintNonAccentPixels)
            {
                Texture = texture;
                this.sourcePixels = sourcePixels;
                this.outputPixels = outputPixels;
                this.accentPixels = accentPixels;
                this.tintNonAccentPixels = tintNonAccentPixels;
            }

            internal Texture2D Texture { get; }

            internal static SharedData Create(Texture sourceTexture, bool tintNonAccentPixels,
                bool recolorBadgeLettering)
            {

            var tracePerformance = KoenigseggJeskoDiagnostics.DebugEnabled &&
                                   KoenigseggJeskoDiagnostics.PerformanceDebugEnabled;
            var startedAt = tracePerformance ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

            var temporary = RenderTexture.GetTemporary(
                sourceTexture.width,
                sourceTexture.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            Texture2D readable;
            try
            {
                Graphics.Blit(sourceTexture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(
                    sourceTexture.width,
                    sourceTexture.height,
                    TextureFormat.RGBA32,
                    true,
                    false)
                {
                    name = sourceTexture.name +
                           (tintNonAccentPixels
                               ? "_RuntimeBodyContrast"
                               : "_RuntimeExteriorContrast"),
                    hideFlags = HideFlags.DontSave,
                    filterMode = sourceTexture.filterMode,
                    wrapMode = sourceTexture.wrapMode,
                    anisoLevel = sourceTexture.anisoLevel,
                };
                readable.ReadPixels(
                    new Rect(0f, 0f, sourceTexture.width, sourceTexture.height),
                    0,
                    0,
                    false);
                readable.Apply(true, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }

            var pixelsReadyAt = tracePerformance ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

            var sourcePixels = readable.GetPixels32();
            var accentPixels = new bool[sourcePixels.Length];
            for (var index = 0; index < sourcePixels.Length; index++)
            {
                var source = (Color)sourcePixels[index];
                Color.RGBToHSV(source, out var hue, out var saturation, out var value);
                var yellowGreenAccent = hue >= 0.14f && hue <= 0.38f &&
                                        saturation > 0.30f && value > 0.18f;
                // Authored BADGES atlas, not EXT/body paint. BOOT owns BOTH
                // cabin-side scripts; REARBUMPER owns the plate script; WING_REAR
                // owns both 251 decals. Keep the Swedish flag and other badges.
                // glTFast flips V on import, so convert the bottom-up readback
                // row to the original atlas's top-down coordinate here.
                var pixelX = index % sourceTexture.width;
                var pixelY = index / sourceTexture.width;
                var normalizedX = (pixelX + 0.5f) / sourceTexture.width;
                var atlasY = 1f - (pixelY + 0.5f) / sourceTexture.height;
                var scriptRegion = normalizedX >= 0.024f && normalizedX <= 0.438f &&
                                   atlasY >= 0.434f && atlasY <= 0.576f;
                var wingNumberRegion = normalizedX >= 0.424f && normalizedX <= 0.486f &&
                                       atlasY >= 0.061f && atlasY <= 0.335f;
                var badgeLettering = recolorBadgeLettering &&
                                     (scriptRegion || wingNumberRegion) &&
                                     saturation < 0.25f && value > 0.20f;
                accentPixels[index] = source.a > 0.01f &&
                                      (recolorBadgeLettering ? badgeLettering : yellowGreenAccent);
            }

            if (tracePerformance)
                KoenigseggJeskoDiagnostics.PerformanceInfo(
                    KoenigseggJeskoPrivateDriverSupport.Context,
                    $"Jesko performance paint-texture kind={(tintNonAccentPixels ? "body" : recolorBadgeLettering ? "lettering" : "accent")} " +
                    $"source='{sourceTexture.name}' size={sourceTexture.width}x{sourceTexture.height} " +
                    $"readable={(sourceTexture is Texture2D source2D && source2D.isReadable)} " +
                    $"readback={KoenigseggJeskoDiagnostics.Milliseconds(startedAt, pixelsReadyAt):0.0}ms " +
                    $"mask={KoenigseggJeskoDiagnostics.Milliseconds(pixelsReadyAt, System.Diagnostics.Stopwatch.GetTimestamp()):0.0}ms.");
            return new SharedData(
                readable,
                sourcePixels,
                new Color32[sourcePixels.Length],
                accentPixels,
                tintNonAccentPixels);
            }

            internal void Apply(Color paint)
            {
            var darkContrast = UseDarkContrast(paint);
            var paintBytes = (Color32)paint;
            if (tintNonAccentPixels ? appliedPaint.HasValue && appliedPaint.Value.Equals(paintBytes)
                                    : appliedDarkContrast == darkContrast)
                return;

            for (var index = 0; index < sourcePixels.Length; index++)
            {
                var source = sourcePixels[index];
                if (!accentPixels[index])
                {
                    outputPixels[index] = tintNonAccentPixels
                        ? new Color32(
                            (byte)(source.r * paintBytes.r / 255),
                            (byte)(source.g * paintBytes.g / 255),
                            (byte)(source.b * paintBytes.b / 255),
                            source.a)
                        : source;
                    continue;
                }

                var shade = darkContrast
                    ? (byte)128
                    : (byte)Mathf.RoundToInt(Mathf.Lerp(184f, 255f,
                        Mathf.Max(source.r, Mathf.Max(source.g, source.b)) / 255f));
                outputPixels[index] = new Color32(shade, shade, shade, source.a);
            }

            Texture.SetPixels32(outputPixels);
            Texture.Apply(true, false);
            appliedDarkContrast = darkContrast;
            appliedPaint = paintBytes;
            }

            internal void Dispose()
            {
                if (Texture != null) UnityEngine.Object.Destroy(Texture);
            }
        }

        private static Texture? FindBaseTexture(Material material)
        {
            if (material.HasProperty("_BaseColorMap") && material.GetTexture("_BaseColorMap") != null)
                return material.GetTexture("_BaseColorMap");
            if (material.HasProperty("baseColorTexture") && material.GetTexture("baseColorTexture") != null)
                return material.GetTexture("baseColorTexture");
            return material.mainTexture;
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property)) material.SetTexture(property, texture);
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, color);
            if (material.HasProperty(ColorProperty)) material.SetColor(ColorProperty, color);
            if (material.HasProperty(BaseColorFactor)) material.SetColor(BaseColorFactor, color);
        }
    }
}

