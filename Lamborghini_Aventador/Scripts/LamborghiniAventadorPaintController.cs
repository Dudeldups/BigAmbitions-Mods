#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;

/// <summary>
/// Applies the saved Big Ambitions vehicle color to the GLB's explicitly authored
/// RED_PAINT slot and the masked accents in the InteriorA texture atlas.
/// </summary>
internal sealed class LamborghiniAventadorPaintController : MonoBehaviour
{
    private const string BodyPaintMaterialName = "RED_PAINT";
    private const string InteriorMaterialName = "AventadorInteriorAccent";
    // UV inventory of the 512x512 InteriorA atlas, in Unity's bottom-left
    // texel coordinates. Both headrests sample the same crest island.
    // The tricolore and seat-belt release retain their authored colors.
    private static readonly RectInt[] InteriorAccentRegions =
    {
        new RectInt(291, 274, 21, 45),
        new RectInt(60, 161, 222, 10),
        new RectInt(451, 49, 61, 11),
        new RectInt(282, 90, 108, 120) // Headrest crest
    };
    private static readonly int[] AtlasProperties =
    {
        Shader.PropertyToID("_BaseColorMap"),
        Shader.PropertyToID("baseColorTexture"),
        Shader.PropertyToID("_MainTex")
    };
    private static readonly Dictionary<(Texture2D, Color32), TintedAtlas> TintedAtlases =
        new Dictionary<(Texture2D, Color32), TintedAtlas>();
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");

    private static readonly int[] SvjColorProperties = { BaseColor, ColorProperty, BaseColorFactor };

    internal static bool AppearanceDebugEnabled { get; set; } = false;
    internal bool HasAppliedColor => hasAppliedTint;
    internal bool HasResolvedSavedColor => hasResolvedSavedColor;
    internal int AppliedRevision { get; private set; }

    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly HashSet<Material> ownedPaintMaterials = new HashSet<Material>();
    private readonly Dictionary<MeshFilter, Mesh> svjMeshes = new Dictionary<MeshFilter, Mesh>();
    private readonly List<PaintSlot> svjSlots = new List<PaintSlot>();
    private MaterialPropertyBlock? properties;
    private Texture2D? interiorSourceAtlas;
    private (Texture2D, Color32)? interiorAtlasKey;
    private bool warnedMissingInteriorAtlas;
    private VehicleController? vehicle;
    private ModContext? context;
    private VehicleColor? explicitAmbientColor;
    private bool hasAppliedTint;
    private bool hasResolvedSavedColor;
    private bool missingColorWarningLogged;
    private bool awaitingSavedColor;
    private bool hasLoggedAppearanceState;
    private string lastLoggedColorName = string.Empty;
    private Color32 lastLoggedTint;
    private int lastLoggedSuccessfulSlots = -1;
    private int lastLoggedSlotCount = -1;

    /// <summary>Gets or creates the per-vehicle paint adapter and applies its current saved color.</summary>
    internal static LamborghiniAventadorPaintController Attach(
        VehicleController controller,
        ModContext? modContext,
        bool preferSavedColor = true)
    {
        if (controller == null)
            throw new ArgumentNullException(nameof(controller));

        var paint = controller.GetComponent<LamborghiniAventadorPaintController>() ??
                    controller.gameObject.AddComponent<LamborghiniAventadorPaintController>();
        paint.Initialize(controller, modContext, preferSavedColor);
        return paint;
    }

    /// <summary>Refreshes paint only when the supplied vehicle already owns this adapter.</summary>
    internal static void RefreshIfOwned(VehicleController? controller, bool preferSavedColor = false)
    {
        if (controller == null)
            return;

        controller.GetComponent<LamborghiniAventadorPaintController>()?.RefreshColor(preferSavedColor);
    }

    /// <summary>
    /// Binds this adapter to one vehicle instance. Safe to call again when a
    /// lifecycle callback reconfigures an already initialized vehicle.
    /// </summary>
    internal void Initialize(
        VehicleController controller, ModContext? modContext, bool preferSavedColor = true)
    {
        vehicle = controller != null ? controller : throw new ArgumentNullException(nameof(controller));
        context = modContext;
        explicitAmbientColor = null;
        awaitingSavedColor |= preferSavedColor;
        FindPaintSlots();
        RefreshColor(preferSavedColor);
    }

    /// <summary>Applies one chosen Gley ambient color without changing saved player color state.</summary>
    internal void InitializeForAmbientTraffic(VehicleController controller, VehicleColor color, ModContext? modContext)
    {
        vehicle = controller != null ? controller : throw new ArgumentNullException(nameof(controller));
        explicitAmbientColor = color ?? throw new ArgumentNullException(nameof(color));
        context = modContext;
        awaitingSavedColor = false;
        FindPaintSlots();
        RefreshColor(preferSavedColor: true);
    }

    /// <summary>Applies weighted ambient paint to a native Gley prefab without a player vehicle controller.</summary>
    internal void InitializeForAmbientTraffic(VehicleColor color, ModContext? modContext)
    {
        vehicle = null;
        explicitAmbientColor = color ?? throw new ArgumentNullException(nameof(color));
        context = modContext;
        awaitingSavedColor = false;
        FindPaintSlots();
        RefreshColor(preferSavedColor: true);
    }

    /// <summary>
    /// Reapplies the authoritative live/saved color after a paint UI event,
    /// vehicle entry, repair, or save/load lifecycle callback.
    /// </summary>
    internal void RefreshColor(bool preferSavedColor = false)
    {
        var preferSaved = preferSavedColor || awaitingSavedColor;
        var savedName = vehicle?.vehicleInstance?.vehicleColorName;
        var liveColor = vehicle?.CarFeatures?.VehicleColor;
        var savedColorResolved = !string.IsNullOrEmpty(savedName) &&
            (VehicleHelper.TryGetVehicleColor(savedName, out _) ||
             liveColor != null && string.Equals(liveColor.name, savedName, StringComparison.Ordinal));
        var selected = ResolveVehicleColor(preferSaved);
        if (selected == null)
        {
            hasAppliedTint = false;
            hasResolvedSavedColor = false;
            hasLoggedAppearanceState = false;
            if (!missingColorWarningLogged)
            {
                context?.Logger.Warn(
                    $"LamborghiniAventador paint owner={GetPaintOwnerId()}: " +
                    "no live or saved vehicle color was available; will retry on the next appearance lifecycle callback.");
                missingColorWarningLogged = true;
            }
            return;
        }

        missingColorWarningLogged = false;
        var tint = (Color32)selected.tint;

        var color = (Color)tint;
        color.a = 1f;
        var block = properties ??= new MaterialPropertyBlock();
        var successfulSlots = 0;
        var changedSlots = 0;
        var interiorTexture = GetInteriorTexture(tint);
        ApplySvjContrast(selected.tint);
        var successfulInteriorSlots = 0;
        foreach (var slot in slots)
        {
            block.Clear();
            slot.Renderer.GetPropertyBlock(block, slot.MaterialIndex);
            if (slot.IsInterior)
            {
                if (interiorTexture != null && ApplyInteriorTexture(slot, block, interiorTexture, out var interiorChanged))
                {
                    successfulInteriorSlots++;
                    if (interiorChanged) changedSlots++;
                }
                continue;
            }

            var supportedProperties = 0;
            var changed = false;
            if (slot.Material.HasProperty(BaseColor))
            {
                if (block.GetColor(BaseColor) != color)
                {
                    block.SetColor(BaseColor, color);
                    changed = true;
                }
                supportedProperties++;
            }
            if (slot.Material.HasProperty(ColorProperty))
            {
                if (block.GetColor(ColorProperty) != color)
                {
                    block.SetColor(ColorProperty, color);
                    changed = true;
                }
                supportedProperties++;
            }
            if (slot.Material.HasProperty(BaseColorFactor))
            {
                if (block.GetColor(BaseColorFactor) != color)
                {
                    block.SetColor(BaseColorFactor, color);
                    changed = true;
                }
                supportedProperties++;
            }
            if (supportedProperties == 0)
            {
                context?.Logger.Warn(
                    $"LamborghiniAventador paint owner={GetPaintOwnerId()}: " +
                    $"exact {BodyPaintMaterialName} slot on renderer '{slot.Renderer.name}' " +
                    "has no supported color property; selected paint could not be applied.");
                continue;
            }

            // Repainter broadcasts restoration even when the tint is unchanged.
            // Still inspect the actual block: native repair/repaint may clear it
            // without changing the saved color identifier.
            if (changed)
            {
                slot.Renderer.SetPropertyBlock(block, slot.MaterialIndex);
                changedSlots++;
            }
            successfulSlots++;
        }

        if (changedSlots > 0)
            AppliedRevision++;

        hasAppliedTint = successfulSlots > 0;
        hasResolvedSavedColor = hasAppliedTint && savedColorResolved;
        // A transient live/default tint is not proof that the dealer or save
        // system has assigned the requested paint. Keep waiting for the saved
        // color through the next authoritative vehicle lifecycle event.
        if (hasResolvedSavedColor && preferSaved)
            awaitingSavedColor = false;
        var shouldLogAppearance = !hasLoggedAppearanceState ||
                                  !string.Equals(lastLoggedColorName, selected.name, StringComparison.Ordinal) ||
                                  !lastLoggedTint.Equals(tint) ||
                                  lastLoggedSuccessfulSlots != successfulSlots ||
                                  lastLoggedSlotCount != slots.Count;
        if (successfulSlots == 0)
            hasLoggedAppearanceState = false;
        if (successfulSlots > 0 && shouldLogAppearance &&
            LamborghiniAventadorDiagnostics.DebugEnabled && AppearanceDebugEnabled)
        {
            var material = slots.Find(slot => !slot.IsInterior).Material;
            var surface = material != null && material.HasProperty("_SurfaceType")
                ? material.GetFloat("_SurfaceType") : float.NaN;
            var zWrite = material != null && material.HasProperty("_ZWrite")
                ? material.GetFloat("_ZWrite") : float.NaN;
            context?.Logger.Info(
                $"LamborghiniAventador paint applied vehicle={vehicle?.GetInstanceID()} " +
                $"color='{selected.name}' bodySlots={successfulSlots} interiorSlots={successfulInteriorSlots} rgb=({tint.r},{tint.g},{tint.b}) " +
                $"material='{material?.name ?? "<none>"}' shader='{material?.shader?.name ?? "<none>"}' " +
                $"surface={surface:0.##} zWrite={zWrite:0.##} " +
                $"queue={material?.renderQueue ?? -1} tintApplied={hasAppliedTint}.");

            hasLoggedAppearanceState = true;
            lastLoggedColorName = selected.name;
            lastLoggedTint = tint;
            lastLoggedSuccessfulSlots = successfulSlots;
            lastLoggedSlotCount = slots.Count;
        }
    }

    private void FindPaintSlots()
    {
        slots.Clear();
        svjSlots.Clear();
        interiorSourceAtlas = null;
        var bodySlots = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;

            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;

                if (index == 0 && renderer.name == "Object_58" && material.name.StartsWith("PAINT_BLACK", StringComparison.Ordinal))
                {
                    PrepareSvjGeometry(renderer, index);
                    materials = renderer.sharedMaterials;
                }
                // The dealership accesses Renderer.materials and Unity
                // replaces the names with instance names. The atlas is stable.
                var atlas = FindInteriorAtlas(material);
                if (atlas != null)
                {
                    interiorSourceAtlas = atlas;
                    slots.Add(new PaintSlot(renderer, material, index, isInterior: true));
                    continue;
                }
                if (material.name.StartsWith(InteriorMaterialName, StringComparison.Ordinal) && !warnedMissingInteriorAtlas)
                {
                    warnedMissingInteriorAtlas = true;
                    context?.Logger.Warn($"LamborghiniAventador paint owner={GetPaintOwnerId()}: expected the readable 512x512 interior accent atlas.");
                }

                if (!ContainsIgnoreCase(material.name, BodyPaintMaterialName) &&
                    !IsInsideBodyPaintNode(renderer.transform))
                    continue;

                bodySlots++;
                if (ownedPaintMaterials.Contains(material))
                {
                    slots.Add(new PaintSlot(renderer, material, index));
                    continue;
                }

                // GLB importer shaders may not be present in the game player.
                // Keep the paint material instance-local and normalize it before
                // applying the selected vehicle color through a property block.
                var sourceMaterial = material;
                material = Instantiate(sourceMaterial);
                material.name = sourceMaterial.name + "_RuntimeBodyPaint";
                ownedPaintMaterials.Add(material);
                LamborghiniAventadorGlassController.NormalizeRuntimeMaterial(
                    material, LamborghiniAventadorGlassController.MaterialRole.BodyPaint);
                materials[index] = material;
                renderer.sharedMaterials = materials;

                slots.Add(new PaintSlot(renderer, material, index));
            }
        }

        if (bodySlots == 0)
        {
            context?.Logger.Warn(
                $"LamborghiniAventador paint owner={GetPaintOwnerId()}: " +
                $"no renderer material slot or imported node marked '{BodyPaintMaterialName}' was found; vehicle paint remains unchanged.");
            return;
        }

        if (bodySlots != 1)
        {
            context?.Logger.Warn(
                $"LamborghiniAventador paint owner={GetPaintOwnerId()}: " +
                $"expected one '{BodyPaintMaterialName}' source slot but found {bodySlots}; " +
                "all matched body-paint slots will receive the selected color.");
        }
        else if (LamborghiniAventadorDiagnostics.DebugEnabled && AppearanceDebugEnabled)
        {
            var body = slots.Find(slot => !slot.IsInterior);
            context?.Logger.Info(
                $"LamborghiniAventador paint mapping vehicle={vehicle?.GetInstanceID()}: " +
                $"renderer='{body.Renderer.name}', material='{body.Material.name}', " +
                $"slot={body.MaterialIndex}, importedNode={IsInsideBodyPaintNode(body.Renderer.transform)}, " +
                $"interiorSlots={slots.Count - bodySlots}.");
        }
    }

    private void PrepareSvjGeometry(Renderer renderer, int sourceSlot)
    {
        if (!renderer.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null)
            return;

        if (!svjMeshes.ContainsKey(filter))
        {
            var source = filter.sharedMesh;
            if (source.subMeshCount != 1 || sourceSlot != 0)
            {
                context?.Logger.Warn("LamborghiniAventador SVJ lettering mesh has an unexpected submesh layout.");
                return;
            }
            var vertices = source.vertices;
            var triangles = source.GetTriangles(0);
            var lettering = new List<int>();
            var remainder = new List<int>();
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var target = vertices[triangles[i]].z < -.3f &&
                             vertices[triangles[i + 1]].z < -.3f &&
                             vertices[triangles[i + 2]].z < -.3f ? lettering : remainder;
                target.Add(triangles[i]);
                target.Add(triangles[i + 1]);
                target.Add(triangles[i + 2]);
            }
            if (lettering.Count != 562 * 3 || remainder.Count == 0)
            {
                context?.Logger.Warn($"LamborghiniAventador SVJ lettering geometry mismatch: triangles={lettering.Count / 3}.");
                return;
            }

            var mesh = Instantiate(source);
            mesh.name = source.name + "_SVJContrast";
            mesh.subMeshCount = 2;
            mesh.SetTriangles(remainder, 0);
            mesh.SetTriangles(lettering, 1);
            filter.sharedMesh = mesh;
            svjMeshes.Add(filter, mesh);

            var materials = renderer.sharedMaterials;
            renderer.sharedMaterials = new[] { materials[0], materials[0] };
            if (LamborghiniAventadorDiagnostics.DebugEnabled && AppearanceDebugEnabled)
                context?.Logger.Info($"LamborghiniAventador SVJ mesh vehicle={GetPaintOwnerId()} renderer='{renderer.name}' triangles={lettering.Count / 3} fixedTriangles={remainder.Count / 3}.");
        }

        var currentMaterials = renderer.sharedMaterials;
        if (currentMaterials.Length > 1 && currentMaterials[1] != null)
            svjSlots.Add(new PaintSlot(renderer, currentMaterials[1], 1));
    }

    private void ApplySvjContrast(Color paint)
    {
        var luminance = paint.r * .2126f + paint.g * .7152f + paint.b * .0722f;
        var dark = luminance >= .46f;
        Color shade = dark ? new Color32(18, 18, 18, 255) : new Color32(245, 245, 245, 255);
        var block = properties ??= new MaterialPropertyBlock();
        foreach (var slot in svjSlots)
        {
            block.Clear();
            slot.Renderer.GetPropertyBlock(block, slot.MaterialIndex);
            var supported = false;
            var changed = false;
            foreach (var property in SvjColorProperties)
            {
                if (!slot.Material.HasProperty(property)) continue;
                supported = true;
                if (block.GetColor(property) == shade) continue;
                block.SetColor(property, shade);
                changed = true;
            }
            if (!supported)
                context?.Logger.Warn($"LamborghiniAventador SVJ lettering renderer='{slot.Renderer.name}' has no color property.");
            else if (changed)
            {
                slot.Renderer.SetPropertyBlock(block, slot.MaterialIndex);
                if (LamborghiniAventadorDiagnostics.DebugEnabled && AppearanceDebugEnabled)
                    context?.Logger.Info($"LamborghiniAventador SVJ color vehicle={GetPaintOwnerId()} renderer='{slot.Renderer.name}' mesh='{slot.Renderer.GetComponent<MeshFilter>()?.sharedMesh?.name}' slot={slot.MaterialIndex} luminance={luminance:0.000} ink={(dark ? "dark" : "light")}.");
            }
        }
    }
    private Texture2D? GetInteriorTexture(Color32 tint)
    {
        var source = interiorSourceAtlas;
        if (source == null)
        {
            ReleaseInteriorAtlas();
            return null;
        }

        // Like the Revuelto, keep the cabin accents slightly brighter than the
        // body. Share finished atlases only between instances using the same tint.
        Color32 accent = Color.Lerp((Color)tint, Color.white, 0.12f);
        var key = (source, accent);
        if (interiorAtlasKey is { } current && current.Equals(key))
            return TintedAtlases[key].Texture;

        ReleaseInteriorAtlas();
        if (!TintedAtlases.TryGetValue(key, out var entry))
        {
            var pixels = source.GetPixels32();
            foreach (var region in InteriorAccentRegions)
            {
                for (var y = region.yMin; y < region.yMax; y++)
                {
                    for (var x = region.xMin; x < region.xMax; x++)
                    {
                        var index = y * source.width + x;
                        var pixel = pixels[index];
                        var blend = Mathf.Clamp01((pixel.r - Mathf.Max(pixel.g, pixel.b)) / 165f);
                        if (blend == 0f)
                            continue;

                        var shade = Mathf.Min(pixel.r / 210f, 1f);
                        pixels[index] = new Color32(
                            (byte)Mathf.RoundToInt(Mathf.Lerp(pixel.r, accent.r * shade, blend)),
                            (byte)Mathf.RoundToInt(Mathf.Lerp(pixel.g, accent.g * shade, blend)),
                            (byte)Mathf.RoundToInt(Mathf.Lerp(pixel.b, accent.b * shade, blend)), pixel.a);
                    }
                }
            }
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true)
            {
                name = InteriorMaterialName + "_TintedAtlas",
                filterMode = source.filterMode,
                anisoLevel = source.anisoLevel,
                wrapMode = source.wrapMode
            };
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: Application.isPlaying);
            entry = new TintedAtlas(texture);
            TintedAtlases.Add(key, entry);
        }

        entry.Users++;
        interiorAtlasKey = key;
        return entry.Texture;
    }

    private static bool ApplyInteriorTexture(PaintSlot slot, MaterialPropertyBlock block, Texture2D texture, out bool changed)
    {
        changed = false;
        var supported = false;
        foreach (var property in AtlasProperties)
        {
            if (!slot.Material.HasProperty(property))
                continue;
            supported = true;
            if (block.GetTexture(property) == texture)
                continue;
            block.SetTexture(property, texture);
            changed = true;
        }

        if (changed)
            slot.Renderer.SetPropertyBlock(block, slot.MaterialIndex);
        return supported;
    }

    private void ReleaseInteriorAtlas()
    {
        if (interiorAtlasKey is not { } key)
            return;

        var entry = TintedAtlases[key];
        if (--entry.Users == 0)
        {
            TintedAtlases.Remove(key);
            if (Application.isPlaying) Destroy(entry.Texture);
            else DestroyImmediate(entry.Texture);
        }
        interiorAtlasKey = null;
    }

    private sealed class TintedAtlas
    {
        internal readonly Texture2D Texture;
        internal int Users;

        internal TintedAtlas(Texture2D texture) => Texture = texture;
    }

    private static Texture2D? FindInteriorAtlas(Material material)
    {
        foreach (var property in AtlasProperties)
            if (material.HasProperty(property) && material.GetTexture(property) is Texture2D atlas &&
                atlas.name == "AventadorInteriorAccentAtlas" && atlas.isReadable &&
                atlas.width == 512 && atlas.height == 512)
                return atlas;
        return null;
    }

    private bool IsInsideBodyPaintNode(Transform transform)
    {
        var root = vehicle != null ? vehicle.transform : this.transform;
        for (var current = transform; current != null; current = current.parent)
        {
            if (ContainsIgnoreCase(current.name, BodyPaintMaterialName))
                return true;
            if (current == root)
                break;
        }
        return false;
    }

    private int GetPaintOwnerId() => vehicle != null ? vehicle.GetInstanceID() : gameObject.GetInstanceID();

    private static bool ContainsIgnoreCase(string value, string marker) =>
        value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;

    private VehicleColor? ResolveVehicleColor(bool preferSavedColor)
    {
        if (explicitAmbientColor != null)
            return explicitAmbientColor;

        var live = vehicle?.CarFeatures?.VehicleColor;
        var colorName = vehicle?.vehicleInstance?.vehicleColorName;
        if (preferSavedColor)
        {
            if (string.IsNullOrEmpty(colorName))
                return live;
            // Some paint mods keep their custom palette outside the native
            // VehicleHelper registry and restore it directly to CarFeatures.
            // The live object matching the saved identifier is then the exact
            // authoritative color for this vehicle instance.
            if (live != null && string.Equals(live.name, colorName, StringComparison.Ordinal))
                return live;
            return VehicleHelper.TryGetVehicleColor(colorName, out var registeredColor) ? registeredColor : null;
        }
        if (live != null)
            return live;

        return !string.IsNullOrEmpty(colorName) && VehicleHelper.TryGetVehicleColor(colorName, out var saved)
            ? saved
            : null;
    }

    private readonly struct PaintSlot
    {
        internal PaintSlot(Renderer renderer, Material material, int materialIndex, bool isInterior = false)
        {
            Renderer = renderer;
            Material = material;
            MaterialIndex = materialIndex;
            IsInterior = isInterior;
        }

        internal readonly Renderer Renderer;
        internal readonly Material Material;
        internal readonly int MaterialIndex;
        internal readonly bool IsInterior;
    }

    private void OnDestroy()
    {
        ReleaseInteriorAtlas();
        foreach (var mesh in svjMeshes.Values)
            if (mesh != null) Destroy(mesh);
        svjMeshes.Clear();
        foreach (var material in ownedPaintMaterials)
            if (material != null) Destroy(material);
        ownedPaintMaterials.Clear();
    }
}
