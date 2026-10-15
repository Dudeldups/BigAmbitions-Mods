#nullable enable
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class AudiR8HeadlightSetup
{
    private const string HdrpTypeName = "UnityEngine.Rendering.HighDefinition.HDAdditionalLightData";
    private const float LuxPerBeam = 2500f;
    private const float LuxDistance = 6f;
    private const float SpotAngle = 62f;
    private const float InnerAngle = 38f;

    public static void Author(GameObject prefab)
    {
        var type = Type.GetType(HdrpTypeName + ", Unity.RenderPipelines.HighDefinition.Runtime", true)!;
        foreach (var side in new[] { -1, 1 })
        {
            var name = "AudiR8_" + (side < 0 ? "Left" : "Right") + "HeadlightBeam";
            var existing = prefab.transform.Find(name);
            var host = existing != null ? existing.gameObject : new GameObject(name);
            host.transform.SetParent(prefab.transform, false);
            host.transform.localPosition = new Vector3(side * .62f, .60f, 1.96f);
            host.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
            host.transform.localScale = Vector3.one;
            host.layer = 22; // The native Spotlights light layer; no donor object or asset reference.
            var light = host.GetComponent<Light>();
            if (light == null) light = host.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = 50f;
            light.cookie = null;
            light.cullingMask = -1;
            light.renderingLayerMask = 1;
            light.shadows = LightShadows.None;
            light.color = Color.white;
            light.useColorTemperature = true;
            light.colorTemperature = 5600f;
            light.bounceIntensity = 0f;
            var data = host.GetComponent(type);
            if (data == null) data = host.AddComponent(type);
            ((Behaviour)data).enabled = true;
            type.GetProperty("affectDiffuse")!.SetValue(data, true);
            type.GetProperty("affectSpecular")!.SetValue(data, true);
            type.GetProperty("lightDimmer")!.SetValue(data, 1f);
            type.GetMethod("SetSpotAngle")!.Invoke(data, new object[] { SpotAngle, InnerAngle / SpotAngle * 100f });
            // The native template uses 5000 lux at 6m. Split that total across
            // two R8 projectors and let HDRP convert the physical unit correctly.
            type.GetMethod("SetSpotLightLuxAt")!.Invoke(data, new object[] { LuxPerBeam, LuxDistance });
            light.enabled = false;
            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(light);
        }
        Verify(prefab);
    }

    public static void Verify(GameObject prefab)
    {
        foreach (var side in new[] { "Left", "Right" })
        {
            var host = prefab.transform.Find("AudiR8_" + side + "HeadlightBeam");
            var light = host != null ? host.GetComponent<Light>() : null;
            var data = host != null ? host.GetComponents<Component>().SingleOrDefault(c => c != null && c.GetType().FullName == HdrpTypeName) : null;
            if (light == null || data == null)
                throw new InvalidOperationException($"R8 {side} road beam lacks its native HDRP light component.");
            var serialized = new SerializedObject(data);
            if (serialized.FindProperty("m_LightUnit").intValue != 2 ||
                Math.Abs(serialized.FindProperty("m_Intensity").floatValue - LuxPerBeam) > .01f ||
                Math.Abs(serialized.FindProperty("m_LuxAtDistance").floatValue - LuxDistance) > .01f ||
                !serialized.FindProperty("m_AffectDiffuse").boolValue ||
                serialized.FindProperty("m_LightDimmer").floatValue <= 0f ||
                !((Behaviour)data).enabled || light.intensity < 80000f ||
                light.type != LightType.Spot || light.cookie != null || light.cullingMask != -1 ||
                light.renderingLayerMask != 1 || light.range < 40f || host!.gameObject.layer != 22)
                throw new InvalidOperationException($"R8 {side} HDRP road illumination configuration is invalid.");
            var origin = prefab.transform.InverseTransformPoint(host.position);
            var direction = prefab.transform.InverseTransformDirection(host.forward);
            if (direction.z < .98f || direction.y >= -.05f)
                throw new InvalidOperationException($"R8 {side} beam does not aim forward and down: {direction}.");
            var distance = -origin.y / direction.y;
            var ground = origin + direction * distance;
            if (distance <= 0f || distance >= light.range || ground.z < 4f || ground.z > 12f)
                throw new InvalidOperationException($"R8 {side} beam does not intersect the road ahead: {ground}.");
            Debug.Log($"AudiR8 HDRP road beam verified side={side} lux={LuxPerBeam}@{LuxDistance}m candela={light.intensity} groundCenter={ground} range={light.range} angle={light.spotAngle}; native HDRP data and road layers present.");
        }
    }
}
