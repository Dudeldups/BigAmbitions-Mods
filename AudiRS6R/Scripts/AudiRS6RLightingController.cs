#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class AudiRS6RLightingController : MonoBehaviour
{
    private const string FrontLampRendererName = "B:Light_Geo_lodA_B:Light_Geo_lodASG1_0";
    private const string InnerWindowRendererName = "B:WindowInside_Geo_lodA_B:Window_Geo_lodASG1_0";
    private const string OuterWindowRendererName = "B:Window_Geo_lodA_B:Window_Geo_lodASG1_0";
    private const string RearLampRendererName = "B:Window_Geo_lodA_red_glass_0";
    private const float BlinkerHalfPeriod = 0.42f;

    private static readonly BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly List<GameObject> generatedObjects = new();
    private readonly List<Material> generatedMaterials = new();
    private readonly List<Mesh> generatedMeshes = new();
    private readonly List<OverlayMeshBinding> deformableOverlayBindings = new();

    private VehicleController? vehicleController;
    private Transform? vehicleRoot;
    private VehicleLightsComponent? trafficLights;
    private static readonly FieldInfo? TrafficMain = typeof(VehicleLightsComponent).GetField("_main", InstanceFields);
    private static readonly FieldInfo? TrafficBrake = typeof(VehicleLightsComponent).GetField("_brake", InstanceFields);
    private static readonly FieldInfo? TrafficLeftBlinker = typeof(VehicleLightsComponent).GetField("_leftBlinker", InstanceFields);
    private static readonly FieldInfo? TrafficRightBlinker = typeof(VehicleLightsComponent).GetField("_rightBlinker", InstanceFields);
    private static readonly FieldInfo? TrafficBlinkerPhase = typeof(VehicleLightsComponent).GetField("_lastBlinkerState", InstanceFields);
    private ModContext? context;
    private object? brakes;
    private object? blinkers;
    private Light? headlightBeam;
    private Light? leftHeadlightBeam;
    private Light? rightHeadlightBeam;
    private MeshRenderer? leftHeadlightOverlay;
    private MeshRenderer? rightHeadlightOverlay;
    private MeshRenderer? rearWindowTintOverlay;
    private MeshRenderer? leftTailLightOverlay;
    private MeshRenderer? rightTailLightOverlay;
    private MeshRenderer? leftBrakeLightOverlay;
    private MeshRenderer? rightBrakeLightOverlay;
    private MeshRenderer? centerBrakeLightOverlay;
    private MeshRenderer? leftFrontBlinkerOverlay;
    private MeshRenderer? rightFrontBlinkerOverlay;
    private MeshRenderer? leftRearBlinkerOverlay;
    private MeshRenderer? rightRearBlinkerOverlay;
    private bool initialized;
    private bool updateFailureReported;
    private int lastNpcLightState = -1;
    private bool wasBlinking;
    private float blinkerPhaseStartedAt;
    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (initialized && vehicleController == controller)
            return;

        vehicleController = controller;
        vehicleRoot = controller.transform;
        context = modContext;
        LocateVehicleStateSources(controller);

        InitializeVisuals(controller.gameObject);
    }

    internal void InitializeForAmbientTraffic(ModContext? modContext)
    {
        if (initialized)
            return;

        context = modContext;
        vehicleRoot = transform;
        trafficLights = GetComponentInChildren<VehicleLightsComponent>(true);
        if (trafficLights == null || TrafficMain == null || TrafficBrake == null ||
            TrafficLeftBlinker == null || TrafficRightBlinker == null || TrafficBlinkerPhase == null)
        {
            LogWarning("NPC light state source is unavailable.");
            return;
        }

        InitializeVisuals(gameObject);
    }

    private void InitializeVisuals(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        var frontLampRenderer = FindRenderer(renderers, FrontLampRendererName);
        var rearLampRenderer = FindRenderer(renderers, RearLampRendererName);
        var outerWindowRenderer = FindRenderer(renderers, OuterWindowRendererName);
        var innerWindowRenderer = FindRenderer(renderers, InnerWindowRendererName);
        var glassCount = ConfigureGlass(outerWindowRenderer, innerWindowRenderer);
        rearWindowTintOverlay = CreateRearWindowTintOverlay(outerWindowRenderer);
        // Keep the imported housing shader, textures and roughness visible in daylight.
        LogSurface("front-lamp-preserved", frontLampRenderer);
        ConfigureRearLampGlass(rearLampRenderer);
        var beamCount = trafficLights == null ? ConfigureHeadlightBeams() : 1;

        leftHeadlightOverlay = CreateFunctionalOverlay(
            frontLampRenderer,
            position => position.z >= 1.80f && position.y >= 0.55f && position.x <= 0f,
            "LeftHeadlight",
            new Color(0.78f, 0.82f, 0.90f, 1f),
            intensity: 5.0f,
            copyBaseTexture: false,
            overlayScale: 1.0015f,
            selectHeadlightSignatureComponents: true);
        rightHeadlightOverlay = CreateFunctionalOverlay(
            frontLampRenderer,
            position => position.z >= 1.80f && position.y >= 0.55f && position.x > 0f,
            "RightHeadlight",
            new Color(0.78f, 0.82f, 0.90f, 1f),
            intensity: 5.0f,
            copyBaseTexture: false,
            overlayScale: 1.0015f,
            selectHeadlightSignatureComponents: true);
        leftTailLightOverlay = CreateFunctionalOverlay(
            frontLampRenderer, position => position.x <= 0f,
            "LeftTailLight", new Color(0.78f, 0.006f, 0.002f, 1f),
            intensity: 2.7f,
            copyBaseTexture: false,
            selectRearLampSignatureComponents: true);
        rightTailLightOverlay = CreateFunctionalOverlay(
            frontLampRenderer, position => position.x > 0f,
            "RightTailLight", new Color(0.78f, 0.006f, 0.002f, 1f),
            intensity: 2.7f,
            copyBaseTexture: false,
            selectRearLampSignatureComponents: true);
        leftBrakeLightOverlay = CreateFunctionalOverlay(
            frontLampRenderer, position => position.x <= 0f,
            "LeftBrakeLight", new Color(1f, 0.008f, 0.001f, 1f),
            intensity: 5.0f,
            copyBaseTexture: false,
            selectRearLampSignatureComponents: true);
        rightBrakeLightOverlay = CreateFunctionalOverlay(
            frontLampRenderer, position => position.x > 0f,
            "RightBrakeLight", new Color(1f, 0.008f, 0.001f, 1f),
            intensity: 5.0f,
            copyBaseTexture: false,
            selectRearLampSignatureComponents: true);
        centerBrakeLightOverlay = CreateFunctionalOverlay(
            rearLampRenderer, position => position.y >= 1.10f,
            "CenterBrakeLight", new Color(1f, 0.008f, 0.001f, 1f), intensity: 5.0f);
        leftFrontBlinkerOverlay = CreateFunctionalOverlay(
            frontLampRenderer,
            position => position.z >= 1.80f && position.y >= 0.55f && position.x <= 0f,
            "FrontLeftBlinker", new Color(1f, 0.42f, 0f, 1f),
            intensity: 3.0f,
            copyBaseTexture: false,
            overlayScale: 1.004f,
            selectHeadlightSignatureComponents: true);
        rightFrontBlinkerOverlay = CreateFunctionalOverlay(
            frontLampRenderer,
            position => position.z >= 1.80f && position.y >= 0.55f && position.x > 0f,
            "FrontRightBlinker", new Color(1f, 0.42f, 0f, 1f),
            intensity: 3.0f,
            copyBaseTexture: false,
            overlayScale: 1.004f,
            selectHeadlightSignatureComponents: true);
        leftRearBlinkerOverlay = CreateFunctionalOverlay(
            frontLampRenderer, position => position.z <= -2.0f && position.x <= 0f,
            "RearLeftBlinker", new Color(1f, 0.42f, 0f, 1f), intensity: 3.0f,
            copyBaseTexture: false, overlayScale: 1.004f,
            selectRearIndicatorComponents: true);
        rightRearBlinkerOverlay = CreateFunctionalOverlay(
            frontLampRenderer, position => position.z <= -2.0f && position.x > 0f,
            "RearRightBlinker", new Color(1f, 0.42f, 0f, 1f), intensity: 3.0f,
            copyBaseTexture: false, overlayScale: 1.004f,
            selectRearIndicatorComponents: true);

        initialized = true;
        var lightOverlayCount = CountLightOverlays();
        if (AudiRS6RDiagnostics.DebugEnabled && AudiRS6RDiagnostics.NpcLightingDebugEnabled)
            context?.Logger.Info(
                $"AudiRS6R lighting vehicle={root.GetInstanceID()} npc={trafficLights != null}: " +
                $"glassRenderers={glassCount}/2, emissiveLayers={lightOverlayCount}/11.");
        if (glassCount != 2 || rearWindowTintOverlay == null ||
            (trafficLights == null && beamCount != 2) || lightOverlayCount != 11)
            LogWarning("Lighting setup is incomplete; inspect the preceding material/overlay diagnostics.");
        if (vehicleController != null &&
            vehicleController.GetType().GetProperty("ShouldLightsBeOn", InstanceFields) == null)
            LogWarning("ShouldLightsBeOn is unavailable; automatic headlights cannot be read.");
        ApplyLightState();
    }

    private void Update()
    {
        if (!initialized)
            return;

        try
        {
            ApplyLightState();
        }
        catch (Exception ex)
        {
            if (!updateFailureReported)
            {
                updateFailureReported = true;
                LogWarning($"update failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void LocateVehicleStateSources(VehicleController controller)
    {
        foreach (var component in controller.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;

            var componentType = component.GetType();
            if (brakes == null)
                brakes = componentType.GetField("brakes", InstanceFields)?.GetValue(component);
            if (componentType.FullName == "Vehicles.Components.VehicleBlinker")
                blinkers = component;
        }

        foreach (var light in controller.GetComponentsInChildren<Light>(true))
        {
            if (light != null && string.Equals(light.name, "Spotlights", StringComparison.Ordinal))
            {
                headlightBeam = light;
                break;
            }
        }
    }

    private int ConfigureGlass(MeshRenderer? outerWindowRenderer, MeshRenderer? innerWindowRenderer)
    {
        var configuredCount = 0;
        LogSurface("outer-glass-source", outerWindowRenderer);
        LogSurface("inner-glass-source", innerWindowRenderer);
        if (outerWindowRenderer != null)
        {
            outerWindowRenderer.sharedMaterial = CloneAndConfigureGlass(
                FirstMaterial(outerWindowRenderer), "AudiRS6R Corrected Outer Glass",
                new Color(0.14f, 0.14f, 0.14f, 0.20f));
            outerWindowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            outerWindowRenderer.enabled = true;
            LogSurface("outer-glass-configured", outerWindowRenderer);
            configuredCount++;
        }

        if (innerWindowRenderer != null)
        {
            innerWindowRenderer.sharedMaterial = CloneAndConfigureGlass(
                FirstMaterial(innerWindowRenderer) ?? FirstMaterial(outerWindowRenderer),
                "AudiRS6R Corrected Inner Glass", new Color(0.10f, 0.10f, 0.10f, 0.10f));
            innerWindowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            innerWindowRenderer.enabled = true;
            LogSurface("inner-glass-configured", innerWindowRenderer);
            configuredCount++;
        }

        return configuredCount;
    }

    private void ConfigureRearLampGlass(MeshRenderer? rearLampRenderer)
    {
        LogSurface("rear-lamp-glass-source", rearLampRenderer);
        if (rearLampRenderer == null)
            return;

        var source = FirstMaterial(rearLampRenderer);
        if (source == null)
            return;

        rearLampRenderer.sharedMaterial = CloneAndConfigureGlass(
            source,
            "AudiRS6R Transparent Rear Lamp Glass",
            new Color(0.45f, 0.025f, 0.015f, 0.30f));
        LogSurface("rear-lamp-glass-configured", rearLampRenderer);
    }

    private MeshRenderer? CreateRearWindowTintOverlay(MeshRenderer? sourceRenderer)
    {
        if (sourceRenderer == null)
        {
            LogWarning("rear-window tint has no outer-glass source renderer.");
            return null;
        }

        var sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
        if (sourceFilter?.sharedMesh == null)
        {
            LogWarning($"rear-window tint source '{sourceRenderer.name}' has no mesh.");
            return null;
        }

        try
        {
            var tintMesh = CreateFilteredMesh(
                sourceRenderer,
                sourceFilter.sharedMesh,
                position => position.y >= 0.95f && position.y <= 1.36f && position.z <= -0.28f &&
                            (position.y <= 1.32f || Mathf.Abs(position.x) >= 0.30f),
                "RearWindowTint");
            if (tintMesh == null)
            {
                LogWarning($"rear-window tint selected no triangles on '{sourceRenderer.name}'.");
                return null;
            }

            var tintObject = new GameObject("AudiRS6R_RearWindowTint");
            tintObject.transform.SetParent(sourceRenderer.transform, false);
            tintObject.transform.localScale = Vector3.one * 1.0005f;
            tintObject.layer = sourceRenderer.gameObject.layer;
            tintObject.AddComponent<MeshFilter>().sharedMesh = tintMesh;

            var tintRenderer = tintObject.AddComponent<MeshRenderer>();
            var tintMaterial = CloneAndConfigureGlass(
                FirstMaterial(sourceRenderer),
                "AudiRS6R Rear Window Tint",
                new Color(0.03f, 0.03f, 0.03f, 0.58f));
            tintMaterial.renderQueue = (int)RenderQueue.Transparent + 10;
            tintRenderer.sharedMaterial = tintMaterial;
            tintRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
            tintRenderer.shadowCastingMode = ShadowCastingMode.Off;
            tintRenderer.receiveShadows = false;

            generatedObjects.Add(tintObject);
            generatedMeshes.Add(tintMesh);
            return tintRenderer;
        }
        catch (Exception ex)
        {
            LogWarning($"could not create rear-window tint: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private Material CloneAndConfigureGlass(Material? source, string materialName, Color tint)
    {
        if (source == null)
            throw new InvalidOperationException("Audi glass source material is missing.");

        var material = new Material(source) { name = materialName };
        generatedMaterials.Add(material);
        SetColorIfPresent(material, "_BaseColor", tint);
        SetColorIfPresent(material, "_Color", tint);
        SetColorIfPresent(material, "baseColorFactor", tint);
        // HDRP requires blend/depth state as well as alpha. Use ordinary alpha
        // transparency so glass does not depend on screen-space refraction.
        SetFloatIfPresent(material, "transmissionFactor", 0f);
        SetFloatIfPresent(material, "_SurfaceType", 1f);
        SetFloatIfPresent(material, "_BlendMode", 0f);
        // HDRP applies the source alpha inside the shader.
        SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.One);
        SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloatIfPresent(material, "_AlphaSrcBlend", (float)BlendMode.One);
        SetFloatIfPresent(material, "_AlphaDstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloatIfPresent(material, "_ZWrite", 0f);
        SetFloatIfPresent(material, "_TransparentZWrite", 0f);
        SetFloatIfPresent(material, "_ZTestDepthEqualForOpaque", (float)CompareFunction.LessEqual);
        SetFloatIfPresent(material, "_AlphaCutoffEnable", 0f);
        SetFloatIfPresent(material, "_SupportDecals", 0f);
        SetFloatIfPresent(material, "_EnableBlendModePreserveSpecularLighting", 0f);
        SetFloatIfPresent(material, "_TransparentDepthPrepassEnable", 0f);
        SetFloatIfPresent(material, "_TransparentDepthPostpassEnable", 0f);
        SetFloatIfPresent(material, "_TransparentBackfaceEnable", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("TransparentDepthPrepass", false);
        material.SetShaderPassEnabled("TransparentDepthPostpass", false);
        material.SetShaderPassEnabled("TransparentBackface", false);
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("ShadowCaster", false);
        SetFloatIfPresent(material, "_Cull", 0f);
        SetFloatIfPresent(material, "_CullMode", 0f);
        SetFloatIfPresent(material, "_CullModeForward", 0f);
        SetFloatIfPresent(material, "_TransparentCullMode", 0f);
        SetFloatIfPresent(material, "_DoubleSidedEnable", 1f);
        material.EnableKeyword("_DOUBLESIDED_ON");
        material.EnableKeyword("_DISABLE_DECALS");
        if (!material.HasProperty("_SurfaceType") || !material.HasProperty("_SrcBlend") ||
            !material.HasProperty("_DstBlend") || !material.HasProperty("_ZWrite"))
            LogWarning($"Glass material '{materialName}' uses unexpected shader '{material.shader.name}'; " +
                       "transparent rendering controls are missing.");
        return material;
    }

    private int ConfigureHeadlightBeams()
    {
        if (headlightBeam == null)
            return 0;

        headlightBeam.enabled = false;
        leftHeadlightBeam = CloneHeadlightBeam(headlightBeam, -0.82f, "AudiRS6R_LeftHeadlightBeam");
        rightHeadlightBeam = CloneHeadlightBeam(headlightBeam, 0.82f, "AudiRS6R_RightHeadlightBeam");
        return (leftHeadlightBeam != null ? 1 : 0) + (rightHeadlightBeam != null ? 1 : 0);
    }

    private Light? CloneHeadlightBeam(Light source, float localX, string objectName)
    {
        try
        {
            var clone = Instantiate(source.gameObject, source.transform.parent, false);
            clone.name = objectName;
            clone.transform.localPosition = new Vector3(localX, 0.88f, 2.38f);
            clone.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);
            clone.layer = source.gameObject.layer;

            var light = clone.GetComponent<Light>();
            if (light == null)
            {
                Destroy(clone);
                return null;
            }

            light.enabled = false;
            light.cookie = null;
            light.range = 45f;
            light.spotAngle = 68f;
            light.innerSpotAngle = 42f;
            light.colorTemperature = 5000f;
            light.useColorTemperature = true;
            generatedObjects.Add(clone);
            return light;
        }
        catch (Exception ex)
        {
            LogWarning($"could not clone headlight beam '{objectName}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private MeshRenderer? CreateFunctionalOverlay(
        MeshRenderer? sourceRenderer,
        Func<Vector3, bool> includeTriangleCenter,
        string suffix,
        Color activeColor,
        float intensity = 3.5f,
        bool copyBaseTexture = true,
        float overlayScale = 1.0015f,
        bool selectHeadlightSignatureComponents = false,
        bool selectRearLampSignatureComponents = false,
        bool selectRearIndicatorComponents = false)
    {
        if (sourceRenderer == null)
        {
            LogWarning($"overlay '{suffix}' has no source renderer.");
            return null;
        }

        var sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
        if (sourceFilter?.sharedMesh == null)
        {
            LogWarning($"overlay '{suffix}' has no source mesh on '{sourceRenderer.name}'.");
            return null;
        }

        try
        {
            var overlayMesh = selectHeadlightSignatureComponents
                ? CreateHeadlightSignatureMesh(
                    sourceRenderer, sourceFilter.sharedMesh, includeTriangleCenter, suffix)
                : selectRearLampSignatureComponents
                    ? CreateRearLampSignatureMesh(
                        sourceRenderer, sourceFilter.sharedMesh, includeTriangleCenter, suffix, false)
                    : selectRearIndicatorComponents
                        ? CreateRearLampSignatureMesh(
                            sourceRenderer, sourceFilter.sharedMesh, includeTriangleCenter, suffix, true)
                    : CreateFilteredMesh(
                        sourceRenderer, sourceFilter.sharedMesh, includeTriangleCenter, suffix);
            if (overlayMesh == null)
            {
                LogWarning($"overlay '{suffix}' selected no triangles on '{sourceRenderer.name}'.");
                return null;
            }

            var overlayObject = new GameObject("AudiRS6R_" + suffix);
            overlayObject.transform.SetParent(sourceRenderer.transform, false);
            overlayObject.transform.localScale = Vector3.one * overlayScale;
            overlayObject.layer = sourceRenderer.gameObject.layer;
            overlayObject.AddComponent<MeshFilter>().sharedMesh = overlayMesh;
            var overlayRenderer = overlayObject.AddComponent<MeshRenderer>();
            overlayRenderer.sharedMaterial = CreateUnlitMaterial(
                FirstMaterial(sourceRenderer), "AudiRS6R " + suffix, activeColor, intensity,
                copyBaseTexture);
            overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            overlayRenderer.enabled = false;

            generatedObjects.Add(overlayObject);
            generatedMeshes.Add(overlayMesh);
            deformableOverlayBindings.Add(new OverlayMeshBinding(sourceFilter, overlayMesh));
            return overlayRenderer;
        }
        catch (Exception ex)
        {
            LogWarning($"could not create light overlay '{suffix}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private Mesh? CreateFilteredMesh(
        MeshRenderer sourceRenderer,
        Mesh source,
        Func<Vector3, bool> includeTriangleCenter,
        string suffix)
    {
        var vertices = source.vertices;
        if (vertices.Length == 0 || vehicleRoot == null)
            return null;

        var vehicleTransform = vehicleRoot;
        var vehiclePositions = new Vector3[vertices.Length];
        for (var index = 0; index < vertices.Length; index++)
        {
            vehiclePositions[index] = vehicleTransform.InverseTransformPoint(
                sourceRenderer.transform.TransformPoint(vertices[index]));
        }

        var triangles = new List<int>();
        for (var subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            var sourceTriangles = source.GetTriangles(subMesh);
            for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
            {
                var a = sourceTriangles[index];
                var b = sourceTriangles[index + 1];
                var c = sourceTriangles[index + 2];
                var center = (vehiclePositions[a] + vehiclePositions[b] + vehiclePositions[c]) / 3f;
                if (!includeTriangleCenter(center))
                    continue;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
        }

        if (triangles.Count == 0)
            return null;

        var mesh = new Mesh
        {
            name = source.name + "_AudiRS6R_" + suffix,
            indexFormat = source.indexFormat,
            vertices = vertices,
            normals = source.normals,
            tangents = source.tangents,
            colors32 = source.colors32,
            uv = source.uv,
            uv2 = source.uv2
        };
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh? CreateHeadlightSignatureMesh(
        MeshRenderer sourceRenderer,
        Mesh source,
        Func<Vector3, bool> includeTriangleCenter,
        string suffix)
    {
        var vertices = source.vertices;
        var uvs = source.uv;
        if (vertices.Length == 0 || uvs.Length != vertices.Length || vehicleRoot == null)
            return null;

        var vehicleTransform = vehicleRoot;
        var bounds = source.bounds;
        var selectedTriangles = new List<int>();
        var atlasBandTriangles = 0;
        for (var subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            var subMeshTriangles = source.GetTriangles(subMesh);
            for (var index = 0; index + 2 < subMeshTriangles.Length; index += 3)
            {
                var a = subMeshTriangles[index];
                var b = subMeshTriangles[index + 1];
                var c = subMeshTriangles[index + 2];
                var uvCenter = (uvs[a] + uvs[b] + uvs[c]) / 3f;
                if (uvCenter.x < 0.50f || uvCenter.x > 0.60f)
                    continue;

                atlasBandTriangles++;
                var localCenter = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                var normalizedHeight = Mathf.InverseLerp(bounds.min.y, bounds.max.y, localCenter.y);
                if (normalizedHeight < 0.25f)
                    continue;

                var vehicleCenter = vehicleTransform.InverseTransformPoint(
                    sourceRenderer.transform.TransformPoint(localCenter));
                if (!includeTriangleCenter(vehicleCenter))
                    continue;

                selectedTriangles.Add(a);
                selectedTriangles.Add(b);
                selectedTriangles.Add(c);
            }
        }

        if (selectedTriangles.Count == 0)
        {
            LogWarning($"headlight-signature suffix='{suffix}' selected=0 atlasBand={atlasBandTriangles} " +
                       $"vertices={vertices.Length} bounds={bounds}.");
            return null;
        }

        var mesh = new Mesh
        {
            name = source.name + "_AudiRS6R_" + suffix,
            indexFormat = source.indexFormat,
            vertices = vertices,
            normals = source.normals,
            tangents = source.tangents,
            colors32 = source.colors32,
            uv = uvs,
            uv2 = source.uv2
        };
        mesh.SetTriangles(selectedTriangles, 0, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh? CreateRearLampSignatureMesh(
        MeshRenderer sourceRenderer,
        Mesh source,
        Func<Vector3, bool> includeComponentCenter,
        string suffix,
        bool indicatorOnly)
    {
        var vertices = source.vertices;
        if (vertices.Length == 0 || vehicleRoot == null)
            return null;

        var sourceNormals = source.normals;

        var parents = new int[vertices.Length];
        for (var index = 0; index < parents.Length; index++)
            parents[index] = index;

        var allTriangles = new List<int>();
        for (var subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            var subMeshTriangles = source.GetTriangles(subMesh);
            for (var index = 0; index + 2 < subMeshTriangles.Length; index += 3)
            {
                var a = subMeshTriangles[index];
                var b = subMeshTriangles[index + 1];
                var c = subMeshTriangles[index + 2];
                UnionVertices(parents, a, b);
                UnionVertices(parents, a, c);
                allTriangles.Add(a);
                allTriangles.Add(b);
                allTriangles.Add(c);
            }
        }

        var components = new Dictionary<int, List<int>>();
        for (var index = 0; index + 2 < allTriangles.Count; index += 3)
        {
            var root = FindVertexRoot(parents, allTriangles[index]);
            if (!components.TryGetValue(root, out var componentTriangles))
            {
                componentTriangles = new List<int>();
                components.Add(root, componentTriangles);
            }

            componentTriangles.Add(allTriangles[index]);
            componentTriangles.Add(allTriangles[index + 1]);
            componentTriangles.Add(allTriangles[index + 2]);
        }

        var selectedTriangles = new List<int>();
        var selectedBars = 0;
        var selectedTeeth = 0;
        foreach (var component in components.Values)
        {
            var componentVertices = new HashSet<int>(component);
            using var vertexEnumerator = componentVertices.GetEnumerator();
            if (!vertexEnumerator.MoveNext())
                continue;

            var componentBounds = new Bounds(vertices[vertexEnumerator.Current], Vector3.zero);
            var normalSum = Vector3.zero;
            foreach (var vertexIndex in componentVertices)
            {
                componentBounds.Encapsulate(vertices[vertexIndex]);
                if (sourceNormals.Length == vertices.Length)
                    normalSum += sourceNormals[vertexIndex];
            }

            var center = componentBounds.center;
            var size = componentBounds.size;
            var absMinX = Mathf.Min(Mathf.Abs(componentBounds.min.x), Mathf.Abs(componentBounds.max.x));
            var absMaxX = Mathf.Max(Mathf.Abs(componentBounds.min.x), Mathf.Abs(componentBounds.max.x));
            var averageNormal = normalSum.sqrMagnitude > 0f ? normalSum.normalized : Vector3.zero;
            var isRearward = averageNormal.z <= -0.60f;
            var isStraightBar =
                absMinX >= 0.30f && absMaxX <= 0.77f &&
                componentBounds.min.z >= -2.40f && componentBounds.max.z <= -2.15f &&
                componentBounds.min.y >= 0.848f && componentBounds.max.y <= 0.890f &&
                size.y >= 0.015f && size.y <= 0.035f && size.x >= 0.10f;
            var isTooth =
                absMinX >= 0.40f && absMaxX <= 0.76f && componentBounds.max.z < -2.10f &&
                size.x <= 0.035f && size.y >= 0.018f && size.y <= 0.040f &&
                componentBounds.min.y >= 0.795f &&
                componentBounds.max.y <= 0.838f;

            var isSelectedSignature = indicatorOnly
                ? isStraightBar
                : isStraightBar || (isTooth && isRearward);
            if (!includeComponentCenter(center) || !isSelectedSignature)
            {
                continue;
            }

            selectedTriangles.AddRange(component);
            if (isStraightBar)
                selectedBars++;
            else
                selectedTeeth++;
        }

        if (selectedTriangles.Count == 0)
        {
            LogWarning($"rear-lamp-signature suffix='{suffix}' selected=0 " +
                       $"components={components.Count} vertices={vertices.Length}.");
            return null;
        }

        if (indicatorOnly && selectedBars == 0)
        {
            LogWarning($"rear-indicator-signature suffix='{suffix}' selected no horizontal indicator bars.");
        }
        else if (!indicatorOnly &&
                 (selectedBars != 2 || selectedTeeth != 50 || selectedTriangles.Count / 3 != 546))
        {
            LogWarning($"rear-lamp-signature suffix='{suffix}' expected bars=2 teeth=50 triangles=546 " +
                       $"but selected bars={selectedBars} teeth={selectedTeeth} " +
                       $"triangles={selectedTriangles.Count / 3}.");
        }
        var mesh = new Mesh
        {
            name = source.name + "_AudiRS6R_" + suffix,
            indexFormat = source.indexFormat,
            vertices = vertices,
            normals = source.normals,
            tangents = source.tangents,
            colors32 = source.colors32,
            uv = source.uv,
            uv2 = source.uv2
        };
        mesh.SetTriangles(selectedTriangles, 0, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static int FindVertexRoot(int[] parents, int vertex)
    {
        while (parents[vertex] != vertex)
        {
            parents[vertex] = parents[parents[vertex]];
            vertex = parents[vertex];
        }

        return vertex;
    }

    private static void UnionVertices(int[] parents, int first, int second)
    {
        var firstRoot = FindVertexRoot(parents, first);
        var secondRoot = FindVertexRoot(parents, second);
        if (firstRoot != secondRoot)
            parents[secondRoot] = firstRoot;
    }

    private Material CreateUnlitMaterial(
        Material? source,
        string materialName,
        Color color,
        float intensity,
        bool copyBaseTexture)
    {
        var shader = Shader.Find("HDRP/Unlit") ??
                     Shader.Find("High Definition Render Pipeline/Unlit") ??
                     Shader.Find("Unlit/Texture") ?? source?.shader;
        if (shader == null)
            throw new InvalidOperationException("No compatible unlit shader is available.");

        var material = new Material(shader) { name = materialName };
        generatedMaterials.Add(material);
        if (copyBaseTexture)
        {
            CopyBaseTexture(source, material);
        }

        var hdrColor = color * intensity;
        hdrColor.a = 1f;
        SetColorIfPresent(material, "_UnlitColor", hdrColor);
        SetColorIfPresent(material, "_BaseColor", hdrColor);
        SetColorIfPresent(material, "_Color", hdrColor);
        SetColorIfPresent(material, "baseColorFactor", hdrColor);
        SetColorIfPresent(material, "_EmissiveColor", hdrColor);
        SetColorIfPresent(material, "_EmissionColor", hdrColor);
        SetFloatIfPresent(material, "_SurfaceType", 0f);
        SetFloatIfPresent(material, "_ZWrite", 1f);
        SetFloatIfPresent(material, "_Cull", 0f);
        SetFloatIfPresent(material, "_CullMode", 0f);
        material.EnableKeyword("_EMISSION");
        material.renderQueue = Mathf.Clamp((source?.renderQueue ?? 2450) + 1, 2001, 2499);
        return material;
    }

    private static void CopyBaseTexture(Material? source, Material destination)
    {
        AssignBaseTexture(destination, FindBaseTexture(source));
    }

    private static Texture? FindBaseTexture(Material? source)
    {
        if (source == null)
            return null;

        Texture? baseTexture = null;
        if (source.HasProperty("_BaseColorMap"))
            baseTexture = source.GetTexture("_BaseColorMap");
        if (baseTexture == null && source.HasProperty("_BaseMap"))
            baseTexture = source.GetTexture("_BaseMap");
        if (baseTexture == null && source.HasProperty("_MainTex"))
            baseTexture = source.GetTexture("_MainTex");
        if (baseTexture == null && source.HasProperty("baseColorTexture"))
            baseTexture = source.GetTexture("baseColorTexture");
        return baseTexture;
    }

    private static void AssignBaseTexture(Material destination, Texture? baseTexture)
    {
        SetTextureIfPresent(destination, "_UnlitColorMap", baseTexture);
        SetTextureIfPresent(destination, "_BaseColorMap", baseTexture);
        SetTextureIfPresent(destination, "_BaseMap", baseTexture);
        SetTextureIfPresent(destination, "_MainTex", baseTexture);
        SetTextureIfPresent(destination, "baseColorTexture", baseTexture);
        SetTextureIfPresent(destination, "_EmissiveColorMap", baseTexture);
        SetTextureIfPresent(destination, "_EmissionMap", baseTexture);
    }

    private void ApplyLightState()
    {
        var npc = trafficLights != null;
        var controlledByPlayer = vehicleController != null && vehicleController.controlledByPlayer;
        var automaticHeadlights = npc ? ReadTrafficBool(TrafficMain) :
            GetBoolProperty(vehicleController, "ShouldLightsBeOn");
        var headlights = (npc || controlledByPlayer) && automaticHeadlights;
        var tailLights = headlights;
        var rawBraking = npc ? ReadTrafficBool(TrafficBrake) :
            GetBoolProperty(brakes, "IsBraking") || GetBoolMethod(brakes, "IsBraking");
        var braking = (npc || controlledByPlayer) && rawBraking;
        var leftBlinker = npc ? ReadTrafficBool(TrafficLeftBlinker) :
            controlledByPlayer && GetBoolField(blinkers, "_isLeftBlinkerOn");
        var rightBlinker = npc ? ReadTrafficBool(TrafficRightBlinker) :
            controlledByPlayer && GetBoolField(blinkers, "_isRightBlinkerOn");
        var isBlinking = leftBlinker || rightBlinker;

        if (!npc && isBlinking && !wasBlinking)
            blinkerPhaseStartedAt = Time.unscaledTime;
        var blinkerFlash = npc
            ? isBlinking && TrafficBlinkerPhase?.GetValue(trafficLights) is int phase && phase != 0
            : isBlinking && Mathf.Repeat(Time.unscaledTime - blinkerPhaseStartedAt,
                BlinkerHalfPeriod * 2f) < BlinkerHalfPeriod;
        wasBlinking = isBlinking;

        SetRendererState(leftHeadlightOverlay, headlights && !(leftBlinker && blinkerFlash));
        SetRendererState(rightHeadlightOverlay, headlights && !(rightBlinker && blinkerFlash));
        if (headlightBeam != null)
            headlightBeam.enabled = false;
        SetLightState(leftHeadlightBeam, controlledByPlayer && automaticHeadlights);
        SetLightState(rightHeadlightBeam, controlledByPlayer && automaticHeadlights);
        SetRendererState(leftTailLightOverlay, tailLights && !braking && !(leftBlinker && blinkerFlash));
        SetRendererState(rightTailLightOverlay, tailLights && !braking && !(rightBlinker && blinkerFlash));
        SetRendererState(leftBrakeLightOverlay, braking && !(leftBlinker && blinkerFlash));
        SetRendererState(rightBrakeLightOverlay, braking && !(rightBlinker && blinkerFlash));
        SetRendererState(centerBrakeLightOverlay, braking);
        SetRendererState(leftFrontBlinkerOverlay, leftBlinker && blinkerFlash);
        SetRendererState(leftRearBlinkerOverlay, leftBlinker && blinkerFlash);
        SetRendererState(rightFrontBlinkerOverlay, rightBlinker && blinkerFlash);
        SetRendererState(rightRearBlinkerOverlay, rightBlinker && blinkerFlash);

        if (npc && AudiRS6RDiagnostics.DebugEnabled &&
            AudiRS6RDiagnostics.NpcLightingDebugEnabled)
        {
            var state = (headlights ? 1 : 0) | (braking ? 2 : 0) |
                (leftBlinker ? 4 : 0) | (rightBlinker ? 8 : 0) |
                (blinkerFlash ? 16 : 0);
            if (state != lastNpcLightState)
            {
                lastNpcLightState = state;
                context?.Logger.Info(
                    $"AudiRS6R NPC lighting instance={GetInstanceID()} " +
                    $"headlights={headlights} brake={braking} left={leftBlinker} " +
                    $"right={rightBlinker} flash={blinkerFlash}.");
            }
        }
    }

    private bool ReadTrafficBool(FieldInfo? field) =>
        trafficLights != null && field?.GetValue(trafficLights) is bool value && value;

    private int CountLightOverlays() =>
        (leftHeadlightOverlay != null ? 1 : 0) +
        (rightHeadlightOverlay != null ? 1 : 0) +
        (leftTailLightOverlay != null ? 1 : 0) +
        (rightTailLightOverlay != null ? 1 : 0) +
        (leftBrakeLightOverlay != null ? 1 : 0) +
        (rightBrakeLightOverlay != null ? 1 : 0) +
        (centerBrakeLightOverlay != null ? 1 : 0) +
        (leftFrontBlinkerOverlay != null ? 1 : 0) +
        (rightFrontBlinkerOverlay != null ? 1 : 0) +
        (leftRearBlinkerOverlay != null ? 1 : 0) +
        (rightRearBlinkerOverlay != null ? 1 : 0);

    internal void SynchronizeDeformedSource(MeshFilter sourceFilter, Mesh sourceMesh)
    {
        if (sourceFilter == null || sourceMesh == null)
            return;

        foreach (var binding in deformableOverlayBindings)
        {
            if (binding.Source != sourceFilter || binding.Overlay == null ||
                binding.Overlay.vertexCount != sourceMesh.vertexCount)
            {
                continue;
            }

            binding.Overlay.vertices = sourceMesh.vertices;
            binding.Overlay.RecalculateBounds();
        }
    }

    private sealed class OverlayMeshBinding
    {
        internal readonly MeshFilter Source;
        internal readonly Mesh Overlay;

        internal OverlayMeshBinding(MeshFilter source, Mesh overlay)
        {
            Source = source;
            Overlay = overlay;
        }
    }

    private void LogSurface(string operation, MeshRenderer? renderer)
    {
        var material = FirstMaterial(renderer);
        if (renderer == null || material == null)
        {
            LogWarning($"surface operation='{operation}' renderer='{renderer?.name ?? "missing"}' material=missing.");
            return;
        }

        var texture = material.HasProperty("baseColorTexture") ? material.GetTexture("baseColorTexture") :
            material.HasProperty("_BaseColorMap") ? material.GetTexture("_BaseColorMap") : null;
        if (!material.shader.isSupported)
            LogWarning($"surface operation='{operation}' shader '{material.shader.name}' is unsupported.");
        if (operation == "front-lamp-preserved" && texture == null)
            LogWarning("The imported front lamp material has no base texture; model detail may be missing.");
    }

    private void LogWarning(string message) =>
        context?.Logger.Warn($"AudiRS6R lighting vehicle='{vehicleRoot?.name}' " +
                             $"instance={vehicleRoot?.GetInstanceID()}: {message}");

    private static void SetRendererState(Renderer? renderer, bool active)
    {
        if (renderer != null && renderer.enabled != active)
            renderer.enabled = active;
    }

    private static void SetLightState(Light? light, bool active)
    {
        if (light != null && light.enabled != active)
            light.enabled = active;
    }

    private static MeshRenderer? FindRenderer(IEnumerable<MeshRenderer> renderers, string objectName)
    {
        foreach (var renderer in renderers)
        {
            if (renderer != null && string.Equals(renderer.name, objectName, StringComparison.Ordinal))
                return renderer;
        }
        return null;
    }

    private static Material? FirstMaterial(Renderer? renderer)
    {
        if (renderer == null)
            return null;
        var materials = renderer.sharedMaterials;
        return materials.Length > 0 ? materials[0] : null;
    }

    private static bool GetBoolField(object? target, string fieldName) =>
        target != null && target.GetType().GetField(fieldName, InstanceFields)?.GetValue(target) is bool value && value;

    private static bool GetBoolProperty(object? target, string propertyName) =>
        target != null && target.GetType().GetProperty(propertyName, InstanceFields)?.GetValue(target) is bool value && value;

    private static bool GetBoolMethod(object? target, string methodName) =>
        target != null &&
        target.GetType().GetMethod(methodName, InstanceFields, null, Type.EmptyTypes, null)?.Invoke(target, null) is bool value && value;

    private static void SetColorIfPresent(Material material, string propertyName, Color value)
    {
        if (material.HasProperty(propertyName)) material.SetColor(propertyName, value);
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName)) material.SetFloat(propertyName, value);
    }

    private static void SetTextureIfPresent(Material material, string propertyName, Texture? value)
    {
        if (value != null && material.HasProperty(propertyName)) material.SetTexture(propertyName, value);
    }


    private void OnDestroy()
    {
        foreach (var generatedObject in generatedObjects)
            if (generatedObject != null) Destroy(generatedObject);
        foreach (var material in generatedMaterials)
            if (material != null) Destroy(material);
        foreach (var mesh in generatedMeshes)
            if (mesh != null) Destroy(mesh);
    }
}
