#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;

internal sealed class BigfootMonsterTruckPaintController : MonoBehaviour
{
    private const string BodyRendererName = "Object_5";
    private const string StructureRendererName = "Object_4";
    private VehicleController? vehicle;
    private MeshRenderer? bodyRenderer;
    private Material? originalMaterial;
    private Material? paintMaterial;
    private Material? grillePanelMaterial;
    private Material? originalWindowMaterial;
    private Material? paintWindowMaterial;
    private Texture2D? sourceTexture;
    private Color32[]? sourcePixels;
    private Texture2D? paintedTexture;
    private Texture2D? paintedWindowTexture;
    private MeshRenderer? structureRenderer;
    private MeshFilter? structureFilter;
    private Mesh? originalStructureMesh;
    private Mesh? paintedStructureMesh;
    private Material[]? originalStructureMaterials;
    private Material? pillarMaterial;
    private MeshFilter? bodyFilter;
    private Mesh? originalBodyMesh;
    private Mesh? paintedBodyMesh;
    private Material[]? originalBodyMaterials;
    private ModContext? context;
    private VehicleColor? appliedColor;
    private Color32 appliedTint;
    private bool hasAppliedColor;
    private bool unresolvedSavedColorLogged;
    private bool configured;
    private bool failed;
    private int attempts;
    private float nextAttempt;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
    }

    internal bool RefreshSavedColor(string source, bool force = false)
    {
        if (!configured)
            return false;

        return ApplySelectedColor(source, force, false);
    }

    internal bool RefreshPreviewColor() =>
        configured && ApplySelectedColor("repaint-preview", false, true);

    private void Update()
    {
        if (failed || vehicle == null)
            return;
        if (!configured)
        {
            if (attempts >= 20 || Time.unscaledTime < nextAttempt)
                return;
            attempts++;
            nextAttempt = Time.unscaledTime + 0.25f;
            if (!TryConfigure() && attempts == 20)
            {
                failed = true;
                Warn("paint adapter could not prepare the main body texture.");
            }
            return;
        }
    }

    private bool TryConfigure()
    {
        foreach (var renderer in vehicle!.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!string.Equals(renderer.name, BodyRendererName, StringComparison.Ordinal))
                continue;
            bodyRenderer = renderer;
            break;
        }
        if (bodyRenderer == null)
            return false;

        var materials = bodyRenderer.sharedMaterials;
        originalBodyMaterials = materials;
        if (materials.Length == 0 || materials[0] == null)
            return false;
        originalMaterial = materials[0];
        sourceTexture = GetBaseTexture(originalMaterial) as Texture2D;
        if (sourceTexture == null)
            return false;

        try
        {
            sourcePixels = ReadSourcePixels(sourceTexture);
        }
        catch (Exception exception)
        {
            failed = true;
            Warn($"body texture readback failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }

        paintMaterial = new Material(originalMaterial)
        {
            name = "Bigfoot Repaintable Body",
            hideFlags = HideFlags.DontSave,
        };
        grillePanelMaterial = new Material(originalMaterial)
        {
            name = "Bigfoot Repaintable Native Grille Panel",
            hideFlags = HideFlags.DontSave,
        };
        SetBaseTexture(grillePanelMaterial, Texture2D.whiteTexture);
        ClearTexture(grillePanelMaterial, "_NormalMap");
        ClearTexture(grillePanelMaterial, "_BumpMap");
        ClearTexture(grillePanelMaterial, "normalTexture");
        ClearTexture(grillePanelMaterial, "_MaskMap");
        ClearTexture(grillePanelMaterial, "_MetallicGlossMap");
        ClearTexture(grillePanelMaterial, "_OcclusionMap");
        ClearTexture(grillePanelMaterial, "_DetailMap");
        materials[0] = paintMaterial;
        if (materials.Length > 1 && materials[1] != null)
        {
            originalWindowMaterial = materials[1];
            paintWindowMaterial = new Material(originalWindowMaterial)
            {
                name = "Bigfoot Repaintable Windshield Trim",
                hideFlags = HideFlags.DontSave,
            };
            materials[1] = paintWindowMaterial;
        }
        var detailSplit = ConfigureFactoryDetails(materials);
        if (!detailSplit)
            bodyRenderer.sharedMaterials = materials;
        ConfigurePaintedPillars();
        configured = true;
        ApplySelectedColor("initialization", true, false);
        return true;
    }

    private bool ApplySelectedColor(
        string source,
        bool force,
        bool preferLiveColor)
    {
        if (bodyRenderer == null || paintMaterial == null || sourceTexture == null ||
            sourcePixels == null)
            return false;

        var selected = ResolveVehicleColor(preferLiveColor);
        if (selected == null)
        {
            Warn($"saved vehicle color was unavailable source='{source}'.");
            return false;
        }

        var tint = (Color)selected.tint;
        tint.a = 1f;
        var tint32 = (Color32)tint;
        if (!force && hasAppliedColor && ReferenceEquals(selected, appliedColor) &&
            tint32.Equals(appliedTint))
            return true;

        Texture2D? replacement = null;
        Texture2D? windowReplacement = null;
        try
        {
            replacement = CreatePaintedTexture(
                sourceTexture,
                sourcePixels,
                tint);
            SetBaseTexture(paintMaterial, replacement);
            if (paintWindowMaterial != null)
            {
                windowReplacement = CreateWindshieldTexture(replacement);
                SetBaseTexture(paintWindowMaterial, windowReplacement);
                SetColor(
                    paintWindowMaterial,
                    "_Color",
                    new Color(0.78f, 0.84f, 0.88f, 0.11f));
            }
            SetPillarColor(tint);
            SetGrillePanelColor(tint);
            if (paintedTexture != null)
                Destroy(paintedTexture);
            if (paintedWindowTexture != null)
                Destroy(paintedWindowTexture);
            paintedTexture = replacement;
            paintedWindowTexture = windowReplacement;
            replacement = null;
            windowReplacement = null;
            appliedColor = selected;
            appliedTint = tint32;
            hasAppliedColor = true;
            context?.Logger.Info(
                $"BigfootMonsterTruck paint vehicle={vehicle?.GetInstanceID()}: applied " +
                $"color='{selected.name}' rgba={tint32} source='{source}'.");
            return true;
        }
        catch (Exception exception)
        {
            if (replacement != null)
                Destroy(replacement);
            if (windowReplacement != null)
                Destroy(windowReplacement);
            failed = true;
            Warn($"paint update failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private VehicleColor? ResolveVehicleColor(bool preferLiveColor)
    {
        var live = vehicle?.CarFeatures?.VehicleColor;
        if (preferLiveColor && live != null)
            return live;

        // On a cold load, CarFeatures can still contain the prefab's black
        // fallback until the player enters the truck. The serialized instance
        // name is the durable repaint value and must win during restoration.
        var colorName = vehicle?.vehicleInstance?.vehicleColorName;
        if (!string.IsNullOrEmpty(colorName) &&
            VehicleHelper.TryGetVehicleColor(colorName, out var saved))
            return saved;

        if (!string.IsNullOrEmpty(colorName) && !unresolvedSavedColorLogged)
        {
            unresolvedSavedColorLogged = true;
            Warn($"saved color name '{colorName}' could not be resolved; " +
                 "falling back to the runtime color.");
        }

        return live;
    }

    private static Color32[] ReadSourcePixels(Texture2D source)
    {
        if (source.isReadable)
            return source.GetPixels32();

        var previous = RenderTexture.active;
        var temporary = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);
        Texture2D? readable = null;
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            readable = new Texture2D(
                source.width,
                source.height,
                TextureFormat.RGBA32,
                false,
                false);
            readable.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
            readable.Apply(false, false);
            return readable.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            if (readable != null)
                Destroy(readable);
        }
    }

    private static Texture2D CreatePaintedTexture(
        Texture2D source,
        Color32[] originalPixels,
        Color tint)
    {
        var pixels = (Color32[])originalPixels.Clone();
        var flameColor = GetContrastingFlameColor(tint);
        for (var index = 0; index < pixels.Length; index++)
        {
            var sourceColor = pixels[index];
            var red = sourceColor.r / 255f;
            var green = sourceColor.g / 255f;
            var blue = sourceColor.b / 255f;
            var maximum = Mathf.Max(red, Mathf.Max(green, blue));
            var minimum = Mathf.Min(red, Mathf.Min(green, blue));
            var chroma = maximum - minimum;
            // The atlas uses near-black for the base body and strongly colored or
            // bright pixels for flames, sponsor decals, lamps, grille and bumper.
            // Keep the transition deliberately narrow so those authored graphics
            // cannot inherit the selected vehicle color.
            var darkMask = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.075f, 0.17f, maximum));
            var neutralMask = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.028f, 0.085f, chroma));
            var paintMask = darkMask * neutralMask;
            // Blue is the atlas' complete secondary paint layer: both the light
            // flames and their darker edging. Recolor it from the selected base
            // color instead of relying on the finite vanilla paint palette.
            var blueDominance = blue - Mathf.Max(red, green);
            var flameMask = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.07f, 0.20f, blueDominance)) *
                Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.16f, 0.42f, maximum));
            if (paintMask <= 0f && flameMask <= 0f)
                continue;

            var original = new Color(red, green, blue, 1f);
            var painted = original;
            if (paintMask > 0f)
            {
                var shade = Mathf.Lerp(0.58f, 1f, Mathf.Clamp01(maximum / 0.24f));
                var target = new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);
                painted = Color.Lerp(painted, target, paintMask);
            }
            if (flameMask > 0f)
            {
                var flameShade = Mathf.Lerp(0.62f, 1f, Mathf.InverseLerp(0.25f, 0.98f, maximum));
                var target = new Color(
                    flameColor.r * flameShade,
                    flameColor.g * flameShade,
                    flameColor.b * flameShade,
                    1f);
                painted = Color.Lerp(painted, target, flameMask);
            }
            pixels[index] = new Color32(
                (byte)Mathf.RoundToInt(painted.r * 255f),
                (byte)Mathf.RoundToInt(painted.g * 255f),
                (byte)Mathf.RoundToInt(painted.b * 255f),
                sourceColor.a);
        }

        var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, false)
        {
            name = $"Bigfoot body paint {tint.r:F2},{tint.g:F2},{tint.b:F2}",
            hideFlags = HideFlags.DontSave,
            filterMode = source.filterMode,
            wrapMode = source.wrapMode,
            anisoLevel = source.anisoLevel,
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    private static Texture2D CreateWindshieldTexture(Texture2D paintedBodyTexture)
    {
        var pixels = paintedBodyTexture.GetPixels32();
        for (var index = 0; index < pixels.Length; index++)
        {
            var color = pixels[index];
            var red = color.r / 255f;
            var green = color.g / 255f;
            var blue = color.b / 255f;
            var maximum = Mathf.Max(red, Mathf.Max(green, blue));
            var minimum = Mathf.Min(red, Mathf.Min(green, blue));

            // The bundled glass shader derives opacity from atlas brightness.
            // Suppress only its neutral light-grey glass field. Bright white
            // lettering and saturated repaint trim retain their authored color
            // and remain opaque.
            if (maximum >= 0.32f && maximum < 0.86f && maximum - minimum < 0.10f)
                pixels[index] = new Color32(32, 38, 42, color.a);
        }

        var texture = new Texture2D(
            paintedBodyTexture.width,
            paintedBodyTexture.height,
            TextureFormat.RGBA32,
            true,
            false)
        {
            name = paintedBodyTexture.name + " windshield",
            hideFlags = HideFlags.DontSave,
            filterMode = paintedBodyTexture.filterMode,
            wrapMode = paintedBodyTexture.wrapMode,
            anisoLevel = paintedBodyTexture.anisoLevel,
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    private bool ConfigureFactoryDetails(Material[] configuredMaterials)
    {
        bodyFilter = bodyRenderer?.GetComponent<MeshFilter>();
        var sourceMesh = bodyFilter?.sharedMesh;
        if (bodyRenderer == null || sourceMesh == null || originalMaterial == null ||
            sourceMesh.subMeshCount == 0)
            return false;

        var vertices = sourceMesh.vertices;
        var uv = sourceMesh.uv;
        if (uv.Length != vertices.Length)
            return false;
        var sourceTriangles = sourceMesh.GetTriangles(0);
        var headlightCandidates = new List<Vector3Int>();
        for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
        {
            var a = sourceTriangles[index];
            var b = sourceTriangles[index + 1];
            var c = sourceTriangles[index + 2];
            if (IsHeadlightUv(uv[a]) || IsHeadlightUv(uv[b]) || IsHeadlightUv(uv[c]))
                headlightCandidates.Add(new Vector3Int(a, b, c));
        }
        var selectedHeadlightIndices = SelectLargestConnectedGroups(headlightCandidates, 2);
        var headlightTriangles = new HashSet<Vector3Int>();
        for (var index = 0; index + 2 < selectedHeadlightIndices.Count; index += 3)
            headlightTriangles.Add(new Vector3Int(
                selectedHeadlightIndices[index],
                selectedHeadlightIndices[index + 1],
                selectedHeadlightIndices[index + 2]));

        var paintTriangles = new List<int>(sourceTriangles.Length);
        var factoryTriangles = new List<int>();
        var grilleTriangles = new List<int>();
        for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
        {
            var a = sourceTriangles[index];
            var b = sourceTriangles[index + 1];
            var c = sourceTriangles[index + 2];
            var triangle = new Vector3Int(a, b, c);
            var center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            var isBadge =
                Mathf.Abs(center.x) <= 0.18f &&
                center.y >= 3.68f && center.y <= 3.78f &&
                center.z >= -0.36f && center.z <= -0.20f;
            var isHeadlight = headlightTriangles.Contains(triangle);
            var minX = Mathf.Min(vertices[a].x, Mathf.Min(vertices[b].x, vertices[c].x));
            var maxX = Mathf.Max(vertices[a].x, Mathf.Max(vertices[b].x, vertices[c].x));
            var minZ = Mathf.Min(vertices[a].z, Mathf.Min(vertices[b].z, vertices[c].z));
            var maxZ = Mathf.Max(vertices[a].z, Mathf.Max(vertices[b].z, vertices[c].z));
            var isNativeGrillePanel =
                center.y >= 3.64f && center.y <= 3.71f &&
                minX >= -0.59f && maxX <= 0f && maxX - minX >= 0.52f &&
                minZ >= -0.51f && maxZ <= -0.05f && maxZ - minZ >= 0.38f;
            var destination = isNativeGrillePanel
                ? grilleTriangles
                : isBadge || isHeadlight
                    ? factoryTriangles
                    : paintTriangles;
            destination.Add(a);
            destination.Add(b);
            destination.Add(c);
        }
        if (factoryTriangles.Count == 0 || grilleTriangles.Count == 0 ||
            grillePanelMaterial == null)
            return false;

        originalBodyMesh = sourceMesh;
        paintedBodyMesh = Instantiate(sourceMesh);
        paintedBodyMesh.name = sourceMesh.name + "_RepaintableFactoryDetails";
        paintedBodyMesh.hideFlags = HideFlags.DontSave;
        var factorySubMesh = sourceMesh.subMeshCount;
        var grilleSubMesh = factorySubMesh + 1;
        paintedBodyMesh.subMeshCount = factorySubMesh + 2;
        paintedBodyMesh.SetTriangles(paintTriangles, 0, true);
        for (var subMesh = 1; subMesh < sourceMesh.subMeshCount; subMesh++)
            paintedBodyMesh.SetTriangles(sourceMesh.GetTriangles(subMesh), subMesh, true);
        paintedBodyMesh.SetTriangles(factoryTriangles, factorySubMesh, true);
        paintedBodyMesh.SetTriangles(grilleTriangles, grilleSubMesh, true);
        paintedBodyMesh.RecalculateBounds();
        bodyFilter!.sharedMesh = paintedBodyMesh;

        var splitMaterials = new Material[factorySubMesh + 2];
        for (var index = 0; index < factorySubMesh; index++)
            splitMaterials[index] = configuredMaterials[Mathf.Min(
                index,
                configuredMaterials.Length - 1)];
        splitMaterials[factorySubMesh] = originalMaterial;
        splitMaterials[grilleSubMesh] = grillePanelMaterial;
        bodyRenderer.sharedMaterials = splitMaterials;
        return true;
    }

    private static bool IsHeadlightUv(Vector2 uv) =>
        uv.y >= 0.55f && uv.y <= 0.64f &&
        ((uv.x >= 0.06f && uv.x <= 0.15f) ||
         (uv.x >= 0.40f && uv.x <= 0.51f));

    private static List<int> SelectLargestConnectedGroups(
        List<Vector3Int> triangles,
        int groupCount)
    {
        var parents = new int[triangles.Count];
        var vertexOwner = new Dictionary<int, int>();
        for (var index = 0; index < triangles.Count; index++)
        {
            parents[index] = index;
            ConnectVertex(triangles[index].x, index, parents, vertexOwner);
            ConnectVertex(triangles[index].y, index, parents, vertexOwner);
            ConnectVertex(triangles[index].z, index, parents, vertexOwner);
        }

        var groups = new Dictionary<int, List<Vector3Int>>();
        for (var index = 0; index < triangles.Count; index++)
        {
            var root = FindRoot(index, parents);
            if (!groups.TryGetValue(root, out var group))
            {
                group = new List<Vector3Int>();
                groups[root] = group;
            }
            group.Add(triangles[index]);
        }

        var ordered = new List<List<Vector3Int>>(groups.Values);
        ordered.Sort((left, right) => right.Count.CompareTo(left.Count));
        var selected = new List<int>();
        for (var groupIndex = 0; groupIndex < Mathf.Min(groupCount, ordered.Count); groupIndex++)
            foreach (var triangle in ordered[groupIndex])
            {
                selected.Add(triangle.x);
                selected.Add(triangle.y);
                selected.Add(triangle.z);
            }
        return selected;
    }

    private static void ConnectVertex(
        int vertex,
        int triangle,
        int[] parents,
        Dictionary<int, int> vertexOwner)
    {
        if (vertexOwner.TryGetValue(vertex, out var owner))
            Union(triangle, owner, parents);
        else
            vertexOwner[vertex] = triangle;
    }

    private static int FindRoot(int value, int[] parents)
    {
        while (parents[value] != value)
        {
            parents[value] = parents[parents[value]];
            value = parents[value];
        }
        return value;
    }

    private static void Union(int left, int right, int[] parents)
    {
        var leftRoot = FindRoot(left, parents);
        var rightRoot = FindRoot(right, parents);
        if (leftRoot != rightRoot)
            parents[rightRoot] = leftRoot;
    }

    private static Color GetContrastingFlameColor(Color tint)
    {
        Color.RGBToHSV(tint, out var hue, out var saturation, out var value);
        var luminance = 0.2126f * tint.r + 0.7152f * tint.g + 0.0722f * tint.b;

        // Preserve the familiar blue-on-black stock livery. Neutral colors need
        // an intentional accent because hue rotation is undefined for greys.
        if (saturation < 0.16f)
        {
            if (luminance < 0.20f)
                return new Color(0.02f, 0.41f, 0.97f, 1f);
            if (luminance > 0.68f)
                return new Color(0.04f, 0.30f, 0.95f, 1f);
            return new Color(1f, 0.38f, 0.03f, 1f);
        }

        var contrastHue = Mathf.Repeat(hue + 0.5f, 1f);
        var contrastSaturation = Mathf.Max(0.78f, saturation);
        var contrastValue = luminance < 0.42f
            ? 1f
            : luminance > 0.72f
                ? 0.86f
                : Mathf.Clamp(1.08f - value * 0.22f, 0.82f, 0.96f);
        return Color.HSVToRGB(contrastHue, contrastSaturation, contrastValue);
    }

    private int ConfigurePaintedPillars()
    {
        foreach (var renderer in vehicle!.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!string.Equals(renderer.name, StructureRendererName, StringComparison.Ordinal))
                continue;
            structureRenderer = renderer;
            structureFilter = renderer.GetComponent<MeshFilter>();
            break;
        }
        var sourceMesh = structureFilter?.sharedMesh;
        if (structureRenderer == null || sourceMesh == null || sourceMesh.subMeshCount != 1)
            return 0;

        var sourceTriangles = sourceMesh.GetTriangles(0);
        var vertices = sourceMesh.vertices;
        var regularTriangles = new List<int>(sourceTriangles.Length);
        var pillarTriangles = new List<int>();
        for (var index = 0; index < sourceTriangles.Length; index += 3)
        {
            var a = sourceTriangles[index];
            var b = sourceTriangles[index + 1];
            var c = sourceTriangles[index + 2];
            var center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            var absX = Mathf.Abs(center.x);
            var isCabinPillar =
                absX >= 0.82f && absX <= 1.45f &&
                center.y >= 0.65f && center.y <= 2.15f &&
                center.z >= -0.05f && center.z <= 1.10f;
            var destination = isCabinPillar ? pillarTriangles : regularTriangles;
            destination.Add(a);
            destination.Add(b);
            destination.Add(c);
        }
        if (pillarTriangles.Count == 0)
            return 0;

        originalStructureMesh = sourceMesh;
        originalStructureMaterials = structureRenderer.sharedMaterials;
        paintedStructureMesh = Instantiate(sourceMesh);
        paintedStructureMesh.name = "Bigfoot structure with repaintable A-pillars";
        paintedStructureMesh.hideFlags = HideFlags.DontSave;
        paintedStructureMesh.subMeshCount = 2;
        paintedStructureMesh.SetTriangles(regularTriangles, 0);
        paintedStructureMesh.SetTriangles(pillarTriangles, 1);
        structureFilter!.sharedMesh = paintedStructureMesh;

        var sourceMaterial = originalStructureMaterials.Length > 0
            ? originalStructureMaterials[0]
            : originalMaterial;
        if (sourceMaterial == null)
            return 0;
        pillarMaterial = new Material(sourceMaterial)
        {
            name = "Bigfoot Repaintable A-Pillars",
            hideFlags = HideFlags.DontSave,
        };
        SetBaseTexture(pillarMaterial, Texture2D.whiteTexture);
        SetFloat(pillarMaterial, "_Metallic", 0.18f);
        SetFloat(pillarMaterial, "_Smoothness", 0.48f);
        structureRenderer.sharedMaterials = new[] { sourceMaterial, pillarMaterial };
        return pillarTriangles.Count / 3;
    }

    private void SetPillarColor(Color tint)
    {
        if (pillarMaterial == null)
            return;
        var color = new Color(
            Mathf.Max(0.035f, tint.r * 0.82f),
            Mathf.Max(0.035f, tint.g * 0.82f),
            Mathf.Max(0.035f, tint.b * 0.82f),
            1f);
        SetColor(pillarMaterial, "_BaseColor", color);
        SetColor(pillarMaterial, "_Color", color);
        SetColor(pillarMaterial, "baseColorFactor", color);
    }

    private void SetGrillePanelColor(Color tint)
    {
        if (grillePanelMaterial == null)
            return;
        var color = new Color(
            Mathf.Max(0.025f, tint.r),
            Mathf.Max(0.025f, tint.g),
            Mathf.Max(0.025f, tint.b),
            1f);
        SetColor(grillePanelMaterial, "_BaseColor", color);
        SetColor(grillePanelMaterial, "_Color", color);
        SetColor(grillePanelMaterial, "baseColorFactor", color);
    }

    private static Texture? GetBaseTexture(Material material)
    {
        foreach (var property in new[] { "_BaseColorMap", "_MainTex", "baseColorTexture" })
            if (material.HasProperty(property) && material.GetTexture(property) != null)
                return material.GetTexture(property);
        return null;
    }

    private static void SetBaseTexture(Material material, Texture texture)
    {
        foreach (var property in new[] { "_BaseColorMap", "_MainTex", "baseColorTexture" })
            if (material.HasProperty(property))
                material.SetTexture(property, texture);
    }

    private static void SetColor(Material material, string property, Color color)
    {
        if (material.HasProperty(property))
            material.SetColor(property, color);
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
            material.SetFloat(property, value);
    }

    private static void ClearTexture(Material material, string property)
    {
        if (material.HasProperty(property))
            material.SetTexture(property, null);
    }

    private void Warn(string message) => context?.Logger.Warn(
        $"BigfootMonsterTruck paint vehicle={vehicle?.GetInstanceID()}: {message}");

    private void OnDestroy()
    {
        if (bodyFilter != null && originalBodyMesh != null)
            bodyFilter.sharedMesh = originalBodyMesh;
        if (bodyRenderer != null && originalBodyMaterials != null)
        {
            bodyRenderer.sharedMaterials = originalBodyMaterials;
        }
        else if (bodyRenderer != null && originalMaterial != null && paintMaterial != null)
        {
            var materials = bodyRenderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                if (materials[index] == paintMaterial)
                    materials[index] = originalMaterial;
                else if (paintWindowMaterial != null && originalWindowMaterial != null &&
                         materials[index] == paintWindowMaterial)
                    materials[index] = originalWindowMaterial;
            }
            bodyRenderer.sharedMaterials = materials;
        }
        if (paintedTexture != null)
            Destroy(paintedTexture);
        if (paintedWindowTexture != null)
            Destroy(paintedWindowTexture);
        if (paintMaterial != null)
            Destroy(paintMaterial);
        if (paintWindowMaterial != null)
            Destroy(paintWindowMaterial);
        if (grillePanelMaterial != null)
            Destroy(grillePanelMaterial);
        if (paintedBodyMesh != null)
            Destroy(paintedBodyMesh);
        if (structureFilter != null && originalStructureMesh != null)
            structureFilter.sharedMesh = originalStructureMesh;
        if (structureRenderer != null && originalStructureMaterials != null)
            structureRenderer.sharedMaterials = originalStructureMaterials;
        if (paintedStructureMesh != null)
            Destroy(paintedStructureMesh);
        if (pillarMaterial != null)
            Destroy(pillarMaterial);
        paintedTexture = null;
        paintedWindowTexture = null;
        paintMaterial = null;
        grillePanelMaterial = null;
        originalWindowMaterial = null;
        paintWindowMaterial = null;
        sourceTexture = null;
        sourcePixels = null;
        bodyRenderer = null;
        bodyFilter = null;
        originalBodyMesh = null;
        paintedBodyMesh = null;
        originalBodyMaterials = null;
        structureRenderer = null;
        structureFilter = null;
        originalStructureMesh = null;
        paintedStructureMesh = null;
        originalStructureMaterials = null;
        pillarMaterial = null;
    }
}
