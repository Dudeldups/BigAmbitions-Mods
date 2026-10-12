#nullable enable
using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class McLarenSennaTemplateSeed
{
    private const string DonorRoot = "Assets/Mods/AudiRS6R/";
    private const string DonorPrefabPath = DonorRoot + "AudiRS6R.prefab";
    private const string DonorVehicleTypePath = DonorRoot + "AudiRS6R.asset";
    private const string ModRoot = "Assets/Mods/McLaren_Senna";
    private const string TemplatePath = ModRoot + "/McLarenSennaTemplate.prefab";
    private const string VehicleTypePath = ModRoot + "/McLarenSenna.asset";
    private const string VehicleTypeName =
        "mclarensenna-vehicle:vehicletype_mclarensenna";

    [MenuItem("Big Ambitions Mods/Seed McLaren Senna Template")]
    public static void Seed()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        CreateVehicleTypeAsset();
        CreateSanitizedTemplatePrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        VerifyNoDonorDependencies(TemplatePath);
        VerifyNoDonorDependencies(VehicleTypePath);
        Debug.Log(
            "McLarenSenna template seeded from a sanitized common vehicle rig; " +
            "all Audi source assets and component references were removed.");
    }

    private static void CreateVehicleTypeAsset()
    {
        var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DonorVehicleTypePath);
        if (source == null)
            throw new InvalidOperationException($"Donor vehicle type is missing: {DonorVehicleTypePath}.");
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleTypePath) != null)
            throw new InvalidOperationException($"Refusing to overwrite '{VehicleTypePath}'.");

        var target = UnityEngine.Object.Instantiate(source);
        target.name = "McLarenSenna";
        SanitizeObject(target, null);
        AssetDatabase.CreateAsset(target, VehicleTypePath);
        EditorUtility.SetDirty(target);
    }

    private static void CreateSanitizedTemplatePrefab()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPrefabPath);
        if (source == null)
            throw new InvalidOperationException($"Donor prefab is missing: {DonorPrefabPath}.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath) != null)
            throw new InvalidOperationException($"Refusing to overwrite '{TemplatePath}'.");

        var root = PrefabUtility.InstantiatePrefab(source) as GameObject ??
                   throw new InvalidOperationException("Could not instantiate the common vehicle rig.");
        try
        {
            PrefabUtility.UnpackPrefabInstance(
                root,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            root.name = "McLarenSennaTemplate";

            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                    continue;
                var script = MonoScript.FromMonoBehaviour(component);
                var scriptPath = script == null ? string.Empty : AssetDatabase.GetAssetPath(script);
                if (scriptPath.StartsWith(DonorRoot, StringComparison.OrdinalIgnoreCase))
                    UnityEngine.Object.DestroyImmediate(component, true);
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;
                renderer.enabled = false;
                renderer.forceRenderingOff = true;
                renderer.sharedMaterials = Array.Empty<Material>();
            }
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter != null)
                    filter.sharedMesh = null;
            foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true))
                if (collider != null)
                {
                    collider.enabled = false;
                    collider.sharedMesh = null;
                }

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                transform.name = SanitizeIdentityString(transform.name);
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
                foreach (var component in transform.GetComponents<Component>())
                {
                    if (component != null)
                        SanitizeObject(component, AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleTypePath));
                }
            }

            EnsureRequiredTemplateParts(root);
            if (PrefabUtility.SaveAsPrefabAsset(root, TemplatePath) == null)
                throw new InvalidOperationException($"Could not save the target-owned template at '{TemplatePath}'.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void EnsureRequiredTemplateParts(GameObject root)
    {
        var requiredNames = new[]
        {
            "FrontLeft_WheelController",
            "FrontRight_WheelController",
            "RearLeft_WheelController",
            "RearRight_WheelController",
            "BodyCollider",
            "Driverside",
            "Passengerside",
            "Spotlights",
        };
        foreach (var name in requiredNames)
        {
            if (FindTransform(root.transform, name) == null)
                throw new InvalidOperationException($"Sanitized vehicle rig is missing '{name}'.");
        }

        if (root.GetComponent<Rigidbody>() == null)
            throw new InvalidOperationException("Sanitized vehicle rig is missing its Rigidbody.");
        var wheelControllerCount = 0;
        var hasVehicleController = false;
        var hasDeformationController = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var type = component.GetType();
            if (string.Equals(type.Name, "WheelController", StringComparison.Ordinal))
                wheelControllerCount++;
            if (string.Equals(type.FullName, "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
                hasVehicleController = true;
            if (string.Equals(type.Name, "VehicleDeformationController", StringComparison.Ordinal))
                hasDeformationController = true;
        }

        if (wheelControllerCount != 4 || !hasVehicleController || !hasDeformationController)
            throw new InvalidOperationException(
                $"Sanitized rig is incomplete: wheelControllers={wheelControllerCount}, " +
                $"vehicleController={hasVehicleController}, deformation={hasDeformationController}.");
    }

    private static void SanitizeObject(
        UnityEngine.Object target,
        UnityEngine.Object? vehicleType)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.GetIterator();
        while (property.Next(true))
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference)
            {
                var referenced = property.objectReferenceValue;
                if (vehicleType != null && property.name == "vehicleType")
                {
                    property.objectReferenceValue = vehicleType;
                }
                else if (referenced != null &&
                    AssetDatabase.GetAssetPath(referenced).StartsWith(
                        DonorRoot, StringComparison.OrdinalIgnoreCase))
                {
                    property.objectReferenceValue = null;
                }
            }
            else if (property.propertyType == SerializedPropertyType.String)
            {
                if (property.name == "vehicleTypeName")
                    property.stringValue = VehicleTypeName;
                else
                    property.stringValue = SanitizeIdentityString(property.stringValue);
            }
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static string SanitizeIdentityString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        var cleaned = Regex.Replace(value, "Audi\\s*RS6R", "McLaren Senna", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, "Audi", "McLaren", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, "Porsche", "McLaren", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, "BMW[_\\s-]*M4[_\\s-]*G82", "McLaren Senna", RegexOptions.IgnoreCase);
        return cleaned;
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        return null;
    }

    private static void VerifyNoDonorDependencies(string assetPath)
    {
        var forbiddenRoots = new[]
        {
            DonorRoot,
            "Assets/Mods/Porsche_911_GT3_RS/",
            "Assets/Mods/BMW_M4_G82/",
        };
        foreach (var dependency in AssetDatabase.GetDependencies(assetPath, true))
        {
            foreach (var forbiddenRoot in forbiddenRoots)
            {
                if (dependency.StartsWith(forbiddenRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Foreign vehicle dependency remains in '{assetPath}': '{dependency}'.");
            }
        }
    }
}
