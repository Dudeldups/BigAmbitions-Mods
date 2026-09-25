#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace MootorVehicle.Editor
{
    public static class MootorVehicleAssetBuilder
    {
        private const string ModRoot = "Assets/Mods/MootorVehicle";
        private const string VehicleTypePath = ModRoot + "/MootorVehicle.asset";
        private const string VehiclePrefabPath = ModRoot + "/MootorVehicle.prefab";
        private const string ManifestPath = ModRoot + "/ModManifest.asset";
        private const string CowModelPath = ModRoot + "/Models/cow.glb";
        private const string CowGaitMeshPath = ModRoot + "/Models/MootorVehicleCowGait.asset";
        private const string MooAudioPath = ModRoot + "/Audio/Moo.mp3";
        private const string ThumbnailPath = ModRoot + "/thumbnail.png";
        private const string TemplateVehicleTypePath = "Assets/Mods/Example-Vehicle/TurboHonza.asset";
        private const string TemplateVehiclePrefabPath = "Assets/Mods/Example-Vehicle/TurboHonza.prefab";
        private const string TemplateManifestPath = "Assets/Mods/Example-Vehicle/ModManifest.asset";
        private const string VehicleTypeName = "mootorvehicle:vehicletype_mootorvehicle";
        private const string OldVehicleTypeName = "example-vehicle:vehicletype_turbohonza";
        private const string ScooterVehicleTag = "ba:vehicletag_isscooter";
        private const string BundleName = "mootorvehicle";
        private const string BundleVariant = "unity3d";
        private const string GaitPoseAName = "MootorGaitA";
        private const string GaitPoseBName = "MootorGaitB";
        private const string LeftEarFlapName = "MootorEarLeft";
        private const string RightEarFlapName = "MootorEarRight";
        private const float TargetCowLength = 3.1f;
        private const float TargetCowGroundOffset = 0.04f;
        private const float BodyColliderGroundClearance = 0.08f;
        private const float GaitLegSwingDegrees = 17f;
        private const float EarFlapDegrees = 24f;

        [MenuItem("Big Ambitions/Moo-tor Vehicle/Build First Version")]
        public static void BuildAll()
        {
            AssetDatabase.ImportAsset(CowModelPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(MooAudioPath, ImportAssetOptions.ForceSynchronousImport);

            var vehicleType = CreateOrUpdateVehicleType();
            CreateOrUpdateVehiclePrefab(vehicleType);
            CreateOrUpdateManifest();
            CreateThumbnail();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            BuildWindowsAssetBundle();
            ValidateWindowsAssetBundle();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Debug.Log(
                "Moo-tor Vehicle: source assets and Windows AssetBundle built successfully. " +
                "Vehicle type, four invisible wheel controllers, cow body, rider seat and moo horn are configured.");
        }

        private static Object CreateOrUpdateVehicleType()
        {
            if (AssetDatabase.LoadMainAssetAtPath(VehicleTypePath) == null &&
                !AssetDatabase.CopyAsset(TemplateVehicleTypePath, VehicleTypePath))
            {
                throw new InvalidOperationException("Could not copy the official TurboHonza VehicleType template.");
            }

            var vehicleType = AssetDatabase.LoadMainAssetAtPath(VehicleTypePath)
                ?? throw new InvalidOperationException("Moo-tor VehicleType asset did not load.");
            vehicleType.name = "MootorVehicle";

            var serialized = new SerializedObject(vehicleType);
            SetString(serialized, "vehicleTypeName", VehicleTypeName);
            SetStringArray(serialized, "tags", new[] { ScooterVehicleTag });
            SetFloat(serialized, "price", 1800f);
            SetBool(serialized, "isATruck", false);
            SetBool(serialized, "isHandVehicle", false);
            SetFloat(serialized, "maxFuel", 100f);
            SetInt(serialized, "maxCargoCapacity", 0);
            SetInt(serialized, "maxSpeed", 25);
            SetFloat(serialized, "enginePower", 18f);
            SetFloat(serialized, "brakeForce", 3500f);
            SetFloat(serialized, "turnRadius", 15f);
            SetFloat(serialized, "damageIntensity", 0.2f);
            SetBool(serialized, "fitsHandTruck", false);
            SetBool(serialized, "fitsFlatbed", false);
            SetBool(serialized, "autoParkSupported", false);
            SetBool(serialized, "taxDeductible", false);
            SetBool(serialized, "hasRadio", false);
            SetInt(serialized, "requiredDeliveryDriverSkillValue", 0);
            SetInt(serialized, "destinationsThatCanDeliver", 1);
            SetBool(serialized, "countsForPersonalGoals", true);
            SetBool(serialized, "spawnInPlayerObject", false);
            SetBool(serialized, "usePedestrianCam", false);
            SetFloat(serialized, "autoDestroyAfterMinutes", -1f);
            // The game's enclosed vehicle entry path hides the original character. The runtime
            // then creates the visible, seated copy used by AudiRS6R and anchors it on the cow.
            SetBool(serialized, "enclosed", true);
            SetBool(serialized, "canGetDirty", false);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(vehicleType);
            return vehicleType;
        }

        private static void CreateOrUpdateVehiclePrefab(Object vehicleType)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(VehiclePrefabPath) == null &&
                !AssetDatabase.CopyAsset(TemplateVehiclePrefabPath, VehiclePrefabPath))
            {
                throw new InvalidOperationException("Could not copy the official TurboHonza vehicle prefab.");
            }

            var templateType = AssetDatabase.LoadMainAssetAtPath(TemplateVehicleTypePath)
                ?? throw new InvalidOperationException("TurboHonza VehicleType template did not load.");
            var cowSource = AssetDatabase.LoadAssetAtPath<GameObject>(CowModelPath)
                ?? throw new InvalidOperationException("Cow GLB did not import as a GameObject.");
            var mooClip = AssetDatabase.LoadAssetAtPath<AudioClip>(MooAudioPath)
                ?? throw new InvalidOperationException("Moo MP3 did not import as an AudioClip.");

            var root = PrefabUtility.LoadPrefabContents(VehiclePrefabPath);
            try
            {
                root.name = "MootorVehicle";
                root.layer = 19;

                var previousVisual = FindDirectOrNested(root.transform, "MootorVehicle_CowVisual");
                if (previousVisual != null)
                    Object.DestroyImmediate(previousVisual.gameObject);

                var previousSeat = FindDirectOrNested(root.transform, "MootorVehicle_RiderSeat");
                if (previousSeat != null)
                    Object.DestroyImmediate(previousSeat.gameObject);

                var originalRenderers = root.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in originalRenderers)
                {
                    renderer.enabled = false;
                    renderer.sharedMaterials = Array.Empty<Material>();
                    if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
                        skinnedMeshRenderer.sharedMesh = null;
                    var meshFilter = renderer.GetComponent<MeshFilter>();
                    if (meshFilter != null)
                        meshFilter.sharedMesh = null;
                }

                foreach (var light in root.GetComponentsInChildren<Light>(true))
                    light.enabled = false;

                var spoiler = FindDirectOrNested(root.transform, "Spoiler");
                if (spoiler != null)
                    spoiler.gameObject.SetActive(false);

                var cowVisual = PrefabUtility.InstantiatePrefab(cowSource, root.transform) as GameObject;
                if (cowVisual == null)
                {
                    cowVisual = Object.Instantiate(cowSource, root.transform);
                    cowVisual.name = cowSource.name;
                }

                if (PrefabUtility.IsPartOfPrefabInstance(cowVisual))
                {
                    PrefabUtility.UnpackPrefabInstance(
                        cowVisual,
                        PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);
                }

                cowVisual.name = "MootorVehicle_CowVisual";
                SetLayerRecursively(cowVisual, 19);
                var cowBounds = NormalizeCowVisual(cowVisual);
                ConfigureCowGait(cowVisual, root.transform, cowBounds);
                var cowRenderers = cowVisual
                    .GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => renderer.enabled)
                    .ToArray();
                var materialFix = FixSolidCowMaterialsForBundle(cowVisual);
                foreach (var renderer in cowRenderers)
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }

                Debug.Log(
                    $"Moo-tor Vehicle: prepared solid cow materials renderers={materialFix.RendererCount} " +
                    $"decalMasksCleared={materialFix.DecalMasksCleared} " +
                    $"hdrpFixed={materialFix.HdrpMaterialsFixed} " +
                    $"hdrpValidated={materialFix.HdrpMaterialsValidated} " +
                    $"shaders='{materialFix.ShaderNames}'.");

                ReplaceSerializedReferences(root, templateType, vehicleType);
                ReplaceSerializedString(root, OldVehicleTypeName, VehicleTypeName);
                ReplaceRendererArrays(root, cowRenderers);
                ConfigurePhysics(root, cowBounds, mooClip);
                CreateRiderAndExitAnchors(root, cowBounds);

                PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigurePhysics(GameObject root, Bounds cowBounds, AudioClip mooClip)
        {
            var rigidbody = root.GetComponent<Rigidbody>()
                ?? throw new InvalidOperationException("TurboHonza template has no Rigidbody.");
            rigidbody.mass = 700f;
            rigidbody.drag = 0.06f;
            rigidbody.angularDrag = 2.4f;
            rigidbody.centerOfMass = new Vector3(0f, 0.35f, 0f);
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            ConfigureBodyCollider(root, cowBounds);

            var obstacle = root.GetComponentInChildren<NavMeshObstacle>(true);
            if (obstacle != null)
            {
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = new Vector3(0f, cowBounds.center.y, 0f);
                obstacle.size = new Vector3(
                    Mathf.Max(1f, cowBounds.size.x * 1.05f),
                    Mathf.Max(1.1f, cowBounds.size.y),
                    Mathf.Max(2.6f, cowBounds.size.z * 0.92f));
                obstacle.carving = true;
            }

            foreach (var audioSource in root.GetComponentsInChildren<AudioSource>(true))
            {
                audioSource.playOnAwake = false;
                audioSource.loop = false;
            }

            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                    continue;

                var serialized = new SerializedObject(component);
                var changed = false;
                changed |= SetFloat(serialized, "brakes.maxTorque", 3500f);
                changed |= SetFloat(serialized, "brakes.actuationTime", 0.12f);
                changed |= SetFloat(serialized, "powertrain.engine.maxPower", 18f);
                changed |= SetFloat(serialized, "powertrain.engine.idleRPM", 600f);
                changed |= SetFloat(serialized, "powertrain.engine.revLimiterRPM", 3000f);
                changed |= SetFloat(serialized, "powertrain.engine.startDuration", 0.12f);
                changed |= SetBool(serialized, "powertrain.engine.stallingEnabled", false);
                changed |= SetFloat(serialized, "powertrain.transmission.finalGearRatio", 10f);
                changed |= SetInt(serialized, "powertrain.transmission.forwardGearCount", 4);
                changed |= SetInt(serialized, "powertrain.transmission.reverseGearCount", 1);
                changed |= SetFloat(serialized, "powertrain.transmission._downshiftRPM", 1200f);
                changed |= SetFloat(serialized, "powertrain.transmission._upshiftRPM", 2500f);
                changed |= SetFloat(serialized, "powertrain.wheelGroups.Array.data[0].antiRollBarForce", 5200f);
                changed |= SetFloat(serialized, "powertrain.wheelGroups.Array.data[1].antiRollBarForce", 5200f);
                changed |= SetFloat(serialized, "powertrain.wheelGroups.Array.data[1].brakeCoefficient", 0.6f);
                changed |= SetFloat(serialized, "wheelbase", 2.1f);
                changed |= SetFloat(serialized, "steering.maximumSteerAngle", 55f);
                changed |= SetFloat(serialized, "steering.degreesPerSecondLimit", 180f);
                changed |= SetAnimationCurve(
                    serialized,
                    "steering.speedSensitiveSteeringCurve",
                    new AnimationCurve(
                        new Keyframe(0f, 1f),
                        new Keyframe(0.3f, 0.8f),
                        new Keyframe(1f, 0.55f)));

                changed |= SetFloat(serialized, "soundManager.engineRunningComponent.baseVolume", 0f);
                changed |= ClearArray(serialized, "soundManager.engineRunningComponent.clips");
                changed |= SetFloat(serialized, "soundManager.engineStartStopComponent.baseVolume", 0f);
                changed |= ClearArray(serialized, "soundManager.engineStartStopComponent.clips");
                changed |= SetFloat(serialized, "soundManager.engineFanComponent.baseVolume", 0f);
                changed |= SetFloat(serialized, "soundManager.exhaustPopComponent.baseVolume", 0f);
                changed |= SetFloat(serialized, "soundManager.gearChangeComponent.baseVolume", 0f);
                changed |= SetFloat(serialized, "soundManager.transmissionWhineComponent.baseVolume", 0f);
                changed |= SetFloat(serialized, "soundManager.turboFlutterComponent.baseVolume", 0f);
                changed |= SetFloat(serialized, "soundManager.turboWhistleComponent.baseVolume", 0f);
                changed |= SetFloat(serialized, "soundManager.wheelSkidComponent.baseVolume", 0f);
                changed |= SetFloat(serialized, "soundManager.wheelTireNoiseComponent.baseVolume", 0f);
                changed |= SetFloat(serialized, "soundManager.suspensionBumpComponent.baseVolume", 0f);
                changed |= SetFloat(serialized, "soundManager.blinkerComponent.baseVolume", 0f);
                changed |= SetFloat(serialized, "soundManager.airBrakeComponent.baseVolume", 0f);
                changed |= SetFloat(serialized, "soundManager.reverseBeepComponent.baseVolume", 0f);
                changed |= SetFloat(serialized, "soundManager.hornComponent.baseVolume", 1f);
                changed |= SetObjectArray(serialized, "soundManager.hornComponent.clips", new Object[] { mooClip });

                changed |= SetBool(serialized, "module.active", true, "module.speedLimit");
                changed |= SetFloat(serialized, "module.speedLimit", 25f);
                changed |= SetBool(serialized, "module.useFuel", true);
                changed |= SetFloat(serialized, "module.amount", 100f);
                changed |= SetFloat(serialized, "module.capacity", 100f);
                changed |= SetFloat(serialized, "module.consumptionMultiplier", 400f);
                changed |= SetFloat(serialized, "module.idleConsumption", 0.0666667f);
                changed |= SetFloat(serialized, "module.maxConsumptionPerHour", 80f);

                changed |= SetBool(serialized, "useDefaultMass", false);
                changed |= SetFloat(serialized, "baseMass", 700f);
                changed |= SetFloat(serialized, "combinedMass", 700f);
                changed |= SetBool(serialized, "useDefaultCenterOfMass", false);
                changed |= SetVector3(serialized, "centerOfMass", new Vector3(0f, 0.35f, 0f));
                changed |= SetVector3(serialized, "combinedCenterOfMass", new Vector3(0f, 0.35f, 0f));
                changed |= SetFloat(serialized, "damageIntensity", 0.2f);
                changed |= SetBool(serialized, "meshDeform", false);
                changed |= ClearArray(serialized, "meshFilters");
                changed |= SetObject(serialized, "washSoundLoop", null);
                changed |= SetObject(serialized, "washSoundEnd", null);

                if (changed)
                    serialized.ApplyModifiedPropertiesWithoutUndo();

                if (string.Equals(
                        component.GetType().FullName,
                        "NWH.WheelController3D.WheelController",
                        StringComparison.Ordinal))
                {
                    ConfigureWheelController(component);
                }
            }
        }

        private static void ConfigureBodyCollider(GameObject root, Bounds cowBounds)
        {
            var colliderTransform = FindDirectOrNested(root.transform, "BodyCollider")
                ?? throw new InvalidOperationException("TurboHonza template BodyCollider was not found.");
            var oldCollider = colliderTransform.GetComponent<MeshCollider>();
            var boxCollider = colliderTransform.GetComponent<BoxCollider>();
            if (boxCollider == null)
                boxCollider = colliderTransform.gameObject.AddComponent<BoxCollider>();

            boxCollider.isTrigger = false;
            var colliderSize = new Vector3(
                Mathf.Max(0.9f, cowBounds.size.x * 0.88f),
                Mathf.Max(1f, cowBounds.size.y * 0.78f),
                Mathf.Max(2.4f, cowBounds.size.z * 0.82f));
            boxCollider.size = colliderSize;
            boxCollider.center = new Vector3(
                0f,
                BodyColliderGroundClearance + colliderSize.y * 0.5f,
                0f);
            colliderTransform.gameObject.layer = 12;
            colliderTransform.gameObject.tag = "Vehicle";

            if (oldCollider != null)
            {
                ReplaceSerializedReferences(root, oldCollider, boxCollider);
                Object.DestroyImmediate(oldCollider);
            }
        }

        private static void ConfigureWheelController(MonoBehaviour component)
        {
            var name = component.gameObject.name;
            var isFront = name.IndexOf("Front", StringComparison.OrdinalIgnoreCase) >= 0;
            var isLeft = name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0;
            component.transform.localPosition = new Vector3(
                isLeft ? -0.53f : 0.53f,
                0.3f,
                isFront ? 1.05f : -1.05f);

            var serialized = new SerializedObject(component);
            SetFloat(serialized, "spring.maxForce", 9000f);
            SetFloat(serialized, "spring.maxLength", 0.12f);
            SetFloat(serialized, "damper.bumpRate", 4200f);
            SetFloat(serialized, "damper.reboundRate", 4800f);
            SetFloat(serialized, "wheel.mass", 10f);
            SetFloat(serialized, "wheel.radius", 0.32f);
            SetFloat(serialized, "wheel.width", 0.16f);
            SetFloat(serialized, "sideFriction.grip", 1.15f);
            SetFloat(serialized, "forwardFriction.grip", 0.95f);
            SetFloat(serialized, "loadRating", 4000f);
            SetFloat(serialized, "rollingResistanceTorque", 45f);
            SetFloat(serialized, "forceApplicationPointDistance", 0.12f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateRiderAndExitAnchors(GameObject root, Bounds cowBounds)
        {
            var seat = new GameObject("MootorVehicle_RiderSeat");
            seat.layer = 19;
            seat.transform.SetParent(root.transform, false);
            seat.transform.localPosition = new Vector3(
                0f,
                cowBounds.min.y + cowBounds.size.y * 0.95f,
                -0.18f);
            seat.transform.localRotation = Quaternion.identity;

            CreateOrMoveAnchor(root.transform, "Driverside", new Vector3(-1.05f, 0.1f, -0.1f));
            CreateOrMoveAnchor(root.transform, "Passengerside", new Vector3(1.05f, 0.1f, -0.1f));
            CreateOrMoveAnchor(root.transform, "LoadingPosition", new Vector3(0f, 0f, -1.8f));
            CreateOrMoveAnchor(root.transform, "RefuelingPosition", new Vector3(-0.8f, 0.5f, -0.7f));
        }

        private static void CreateOrMoveAnchor(Transform root, string name, Vector3 localPosition)
        {
            var anchor = FindDirectOrNested(root, name);
            if (anchor == null)
            {
                anchor = new GameObject(name).transform;
                anchor.SetParent(root, false);
            }

            anchor.localPosition = localPosition;
            anchor.localRotation = Quaternion.identity;
        }

        private static Bounds NormalizeCowVisual(GameObject cowVisual)
        {
            cowVisual.transform.localPosition = Vector3.zero;
            cowVisual.transform.localRotation = Quaternion.identity;
            cowVisual.transform.localScale = Vector3.one;

            var rawBounds = CalculateWorldBounds(cowVisual);
            var dimensions = new[] { rawBounds.size.x, rawBounds.size.y, rawBounds.size.z };
            var lengthAxis = Array.IndexOf(dimensions, dimensions.Max());
            var widthAxis = Array.IndexOf(dimensions, dimensions.Min());
            var heightAxis = Enumerable.Range(0, 3).First(index => index != lengthAxis && index != widthAxis);

            var sourceForward = AxisVector(lengthAxis);
            var sourceUp = AxisVector(heightAxis);
            cowVisual.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f) *
                Quaternion.Inverse(Quaternion.LookRotation(sourceForward, sourceUp));
            var scale = TargetCowLength / dimensions[lengthAxis];
            cowVisual.transform.localScale = Vector3.one * scale;

            var orientedBounds = CalculateWorldBounds(cowVisual);
            var desiredCenter = new Vector3(0f, TargetCowGroundOffset + orientedBounds.extents.y, 0f);
            cowVisual.transform.localPosition += desiredCenter - orientedBounds.center;

            var finalBounds = CalculateWorldBounds(cowVisual);
            Debug.Log(
                $"Moo-tor Vehicle: normalized cow rawSize={rawBounds.size:F3} " +
                $"axes(width={widthAxis},height={heightAxis},length={lengthAxis}) " +
                $"scale={scale:F4} finalCenter={finalBounds.center:F3} finalSize={finalBounds.size:F3}.");
            return finalBounds;
        }

        private static void ConfigureCowGait(GameObject cowVisual, Transform vehicleRoot, Bounds cowBounds)
        {
            var meshFilter = cowVisual
                .GetComponentsInChildren<MeshFilter>(true)
                .FirstOrDefault(candidate => candidate.sharedMesh != null);
            var sourceRenderer = meshFilter != null ? meshFilter.GetComponent<MeshRenderer>() : null;
            if (meshFilter == null || sourceRenderer == null)
                throw new InvalidOperationException("Cow model has no static mesh renderer for gait generation.");

            var generatedMesh = Object.Instantiate(meshFilter.sharedMesh);
            generatedMesh.name = "MootorVehicleCowGait";
            generatedMesh.ClearBlendShapes();

            var vertices = generatedMesh.vertices;
            var poseADeltas = new Vector3[vertices.Length];
            var poseBDeltas = new Vector3[vertices.Length];
            var leftEarDeltas = new Vector3[vertices.Length];
            var rightEarDeltas = new Vector3[vertices.Length];
            var zeroNormals = new Vector3[vertices.Length];
            var zeroTangents = new Vector3[vertices.Length];
            var vehiclePositions = new Vector3[vertices.Length];
            var hoofSeedGroups = Enumerable.Repeat(-1, vertices.Length).ToArray();
            var seedPositionSums = new Vector2[4];
            var seedVertexCounts = new int[4];
            var animatedGroupCounts = new int[4];
            var localCowCenter = vehicleRoot.InverseTransformPoint(cowBounds.center);
            var localCowMin = vehicleRoot.InverseTransformPoint(cowBounds.min);
            var hipHeight = localCowMin.y + cowBounds.size.y * 0.43f;
            var hoofSeedHeight = localCowMin.y + cowBounds.size.y * 0.15f;
            var sideThreshold = cowBounds.extents.x * 0.2f;
            var animatedVertices = 0;

            for (var index = 0; index < vertices.Length; index++)
            {
                var worldPosition = meshFilter.transform.TransformPoint(vertices[index]);
                var vehiclePosition = vehicleRoot.InverseTransformPoint(worldPosition);
                vehiclePositions[index] = vehiclePosition;
                if (vehiclePosition.y >= hoofSeedHeight ||
                    Mathf.Abs(vehiclePosition.x - localCowCenter.x) <= sideThreshold)
                {
                    continue;
                }

                var isLeft = vehiclePosition.x < localCowCenter.x;
                var isFront = vehiclePosition.z > localCowCenter.z;
                var group = (isLeft ? 0 : 1) + (isFront ? 0 : 2);
                hoofSeedGroups[index] = group;
                seedPositionSums[group] += new Vector2(vehiclePosition.x, vehiclePosition.z);
                seedVertexCounts[group]++;
            }

            if (seedVertexCounts.Any(count => count == 0))
                throw new InvalidOperationException("Cow gait generation could not locate all four hooves.");

            // The cow is one imported mesh, but its legs and teats are separate connected
            // triangle islands. Grow each leg from its hoof component so nearby udder
            // geometry can never be selected merely because it occupies the same area.
            var componentParents = Enumerable.Range(0, vertices.Length).ToArray();
            var triangles = generatedMesh.triangles;
            if (triangles.Length % 3 != 0)
                throw new InvalidOperationException("Cow gait mesh has invalid triangle topology.");

            for (var triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
            {
                UnionVertices(componentParents, triangles[triangleIndex], triangles[triangleIndex + 1]);
                UnionVertices(componentParents, triangles[triangleIndex + 1], triangles[triangleIndex + 2]);
            }

            var componentGroups = new Dictionary<int, int>();
            for (var index = 0; index < hoofSeedGroups.Length; index++)
            {
                var group = hoofSeedGroups[index];
                if (group < 0)
                    continue;

                var componentRoot = FindVertexRoot(componentParents, index);
                if (componentGroups.TryGetValue(componentRoot, out var existingGroup) &&
                    existingGroup != group)
                {
                    throw new InvalidOperationException(
                        "Cow gait generation found a mesh component shared by multiple legs.");
                }

                componentGroups[componentRoot] = group;
            }

            if (componentGroups.Values.Distinct().Count() != 4)
                throw new InvalidOperationException("Cow gait generation could not isolate four leg components.");

            var componentPositionSums = new Dictionary<int, Vector3>();
            var componentVertexCounts = new Dictionary<int, int>();
            for (var index = 0; index < vertices.Length; index++)
            {
                var componentRoot = FindVertexRoot(componentParents, index);
                componentPositionSums.TryGetValue(componentRoot, out var positionSum);
                componentVertexCounts.TryGetValue(componentRoot, out var vertexCount);
                componentPositionSums[componentRoot] = positionSum + vehiclePositions[index];
                componentVertexCounts[componentRoot] = vertexCount + 1;
            }

            var earMinimumHeight = localCowMin.y + cowBounds.size.y * 0.72f;
            var earMinimumForward = localCowCenter.z + cowBounds.size.z * 0.18f;
            var earMinimumLateral = cowBounds.size.x * 0.16f;
            var earComponents = new HashSet<int>[2]
            {
                new HashSet<int>(),
                new HashSet<int>()
            };

            foreach (var pair in componentPositionSums)
            {
                var componentCenter = pair.Value / componentVertexCounts[pair.Key];
                if (componentCenter.y < earMinimumHeight ||
                    componentCenter.z < earMinimumForward ||
                    Mathf.Abs(componentCenter.x - localCowCenter.x) < earMinimumLateral)
                {
                    continue;
                }

                var side = componentCenter.x < localCowCenter.x ? 0 : 1;
                earComponents[side].Add(pair.Key);
            }

            if (earComponents[0].Count != 2 || earComponents[1].Count != 2)
            {
                throw new InvalidOperationException(
                    $"Cow ear generation expected two mesh islands per ear but found " +
                    $"{earComponents[0].Count}/{earComponents[1].Count}.");
            }

            var earPivots = new Vector3[2];
            var earVertexCounts = new int[2];
            for (var side = 0; side < earComponents.Length; side++)
                earPivots[side] = CalculateEarPivot(
                    vehiclePositions,
                    componentParents,
                    earComponents[side],
                    localCowCenter,
                    side == 0);

            for (var index = 0; index < vertices.Length; index++)
            {
                var vehiclePosition = vehiclePositions[index];
                var componentRoot = FindVertexRoot(componentParents, index);
                if (vehiclePosition.y >= hipHeight ||
                    !componentGroups.TryGetValue(componentRoot, out var group))
                {
                    continue;
                }

                var isLeft = (group & 1) == 0;
                var isFront = group < 2;
                var groupCenter = seedPositionSums[group] / seedVertexCounts[group];
                var diagonalDirection = isLeft == isFront ? 1f : -1f;
                var influence = Mathf.InverseLerp(hipHeight, hoofSeedHeight, vehiclePosition.y);
                var pivot = new Vector3(
                    groupCenter.x,
                    hipHeight,
                    groupCenter.y);

                poseADeltas[index] = ConvertGaitDelta(
                    vertices[index],
                    vehiclePosition,
                    pivot,
                    diagonalDirection * GaitLegSwingDegrees * influence,
                    meshFilter.transform,
                    vehicleRoot);
                poseBDeltas[index] = ConvertGaitDelta(
                    vertices[index],
                    vehiclePosition,
                    pivot,
                    -diagonalDirection * GaitLegSwingDegrees * influence,
                    meshFilter.transform,
                    vehicleRoot);
                animatedVertices++;
                animatedGroupCounts[group]++;
            }

            for (var index = 0; index < vertices.Length; index++)
            {
                var componentRoot = FindVertexRoot(componentParents, index);
                var side = earComponents[0].Contains(componentRoot)
                    ? 0
                    : earComponents[1].Contains(componentRoot) ? 1 : -1;
                if (side < 0)
                    continue;

                var angle = side == 0 ? EarFlapDegrees : -EarFlapDegrees;
                var delta = ConvertRotationDelta(
                    vertices[index],
                    vehiclePositions[index],
                    earPivots[side],
                    angle,
                    Vector3.forward,
                    meshFilter.transform,
                    vehicleRoot);
                if (side == 0)
                    leftEarDeltas[index] = delta;
                else
                    rightEarDeltas[index] = delta;
                earVertexCounts[side]++;
            }

            if (animatedVertices == 0)
                throw new InvalidOperationException("Cow gait generation did not identify any leg vertices.");

            generatedMesh.AddBlendShapeFrame(GaitPoseAName, 100f, poseADeltas, zeroNormals, zeroTangents);
            generatedMesh.AddBlendShapeFrame(GaitPoseBName, 100f, poseBDeltas, zeroNormals, zeroTangents);
            generatedMesh.AddBlendShapeFrame(LeftEarFlapName, 100f, leftEarDeltas, zeroNormals, zeroTangents);
            generatedMesh.AddBlendShapeFrame(RightEarFlapName, 100f, rightEarDeltas, zeroNormals, zeroTangents);
            generatedMesh.RecalculateBounds();

            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(CowGaitMeshPath);
            if (existingMesh != null)
            {
                EditorUtility.CopySerialized(generatedMesh, existingMesh);
                Object.DestroyImmediate(generatedMesh);
                generatedMesh = existingMesh;
                EditorUtility.SetDirty(existingMesh);
            }
            else
            {
                AssetDatabase.CreateAsset(generatedMesh, CowGaitMeshPath);
            }

            var materials = sourceRenderer.sharedMaterials;
            var shadowCastingMode = sourceRenderer.shadowCastingMode;
            var receiveShadows = sourceRenderer.receiveShadows;
            var lightProbeUsage = sourceRenderer.lightProbeUsage;
            var reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            var renderingLayerMask = sourceRenderer.renderingLayerMask;
            Object.DestroyImmediate(sourceRenderer);

            var gaitRenderer = meshFilter.gameObject.AddComponent<SkinnedMeshRenderer>();
            gaitRenderer.sharedMesh = generatedMesh;
            gaitRenderer.sharedMaterials = materials;
            gaitRenderer.localBounds = ExpandBounds(generatedMesh.bounds, 0.12f);
            gaitRenderer.updateWhenOffscreen = false;
            gaitRenderer.quality = SkinQuality.Auto;
            gaitRenderer.shadowCastingMode = shadowCastingMode;
            gaitRenderer.receiveShadows = receiveShadows;
            gaitRenderer.lightProbeUsage = lightProbeUsage;
            gaitRenderer.reflectionProbeUsage = reflectionProbeUsage;
            gaitRenderer.renderingLayerMask = renderingLayerMask;

            Debug.Log(
                $"Moo-tor Vehicle: generated lightweight cow gait blend shapes " +
                $"vertices={vertices.Length} animatedLegVertices={animatedVertices} " +
                $"hoofSeeds={string.Join("/", seedVertexCounts)} " +
                $"legComponents={componentGroups.Count} " +
                $"groups={string.Join("/", animatedGroupCounts)} " +
                $"earComponents={earComponents[0].Count}/{earComponents[1].Count} " +
                $"earVertices={string.Join("/", earVertexCounts)}.");
        }

        private static int FindVertexRoot(int[] parents, int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }

            return index;
        }

        private static void UnionVertices(int[] parents, int first, int second)
        {
            var firstRoot = FindVertexRoot(parents, first);
            var secondRoot = FindVertexRoot(parents, second);
            if (firstRoot != secondRoot)
                parents[secondRoot] = firstRoot;
        }

        private static Vector3 CalculateEarPivot(
            IReadOnlyList<Vector3> vehiclePositions,
            int[] componentParents,
            HashSet<int> earComponents,
            Vector3 cowCenter,
            bool isLeft)
        {
            var minimumLateralDistance = float.PositiveInfinity;
            var maximumLateralDistance = 0f;
            for (var index = 0; index < vehiclePositions.Count; index++)
            {
                if (!earComponents.Contains(FindVertexRoot(componentParents, index)))
                    continue;

                var distance = Mathf.Abs(vehiclePositions[index].x - cowCenter.x);
                minimumLateralDistance = Mathf.Min(minimumLateralDistance, distance);
                maximumLateralDistance = Mathf.Max(maximumLateralDistance, distance);
            }

            var attachmentLimit = Mathf.Lerp(minimumLateralDistance, maximumLateralDistance, 0.2f);
            var attachmentSum = Vector3.zero;
            var attachmentVertices = 0;
            for (var index = 0; index < vehiclePositions.Count; index++)
            {
                if (!earComponents.Contains(FindVertexRoot(componentParents, index)))
                    continue;

                var position = vehiclePositions[index];
                if (Mathf.Abs(position.x - cowCenter.x) > attachmentLimit)
                    continue;

                attachmentSum += position;
                attachmentVertices++;
            }

            if (attachmentVertices == 0)
                throw new InvalidOperationException("Cow ear generation could not locate an ear attachment.");

            var pivot = attachmentSum / attachmentVertices;
            pivot.x = cowCenter.x + (isLeft ? -minimumLateralDistance : minimumLateralDistance);
            return pivot;
        }

        private static Vector3 ConvertGaitDelta(
            Vector3 meshPosition,
            Vector3 vehiclePosition,
            Vector3 pivot,
            float angle,
            Transform meshTransform,
            Transform vehicleRoot)
        {
            return ConvertRotationDelta(
                meshPosition,
                vehiclePosition,
                pivot,
                angle,
                Vector3.right,
                meshTransform,
                vehicleRoot);
        }

        private static Vector3 ConvertRotationDelta(
            Vector3 meshPosition,
            Vector3 vehiclePosition,
            Vector3 pivot,
            float angle,
            Vector3 axis,
            Transform meshTransform,
            Transform vehicleRoot)
        {
            var rotatedVehiclePosition =
                pivot + Quaternion.AngleAxis(angle, axis) * (vehiclePosition - pivot);
            var rotatedWorldPosition = vehicleRoot.TransformPoint(rotatedVehiclePosition);
            return meshTransform.InverseTransformPoint(rotatedWorldPosition) - meshPosition;
        }

        private static Bounds ExpandBounds(Bounds bounds, float amount)
        {
            bounds.Expand(amount * 2f);
            return bounds;
        }

        private static Vector3 AxisVector(int axis)
        {
            switch (axis)
            {
                case 0: return Vector3.right;
                case 1: return Vector3.up;
                default: return Vector3.forward;
            }
        }

        private static Bounds CalculateWorldBounds(GameObject target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException($"'{target.name}' has no renderers.");

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static void ReplaceRendererArrays(GameObject root, Renderer[] cowRenderers)
        {
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                    continue;

                var serialized = new SerializedObject(component);
                var changed = false;
                changed |= SetObjectArray(serialized, "bodyMeshes", cowRenderers.Cast<Object>().ToArray());
                changed |= SetObjectArray(serialized, "renderers", cowRenderers.Cast<Object>().ToArray());
                if (changed)
                    serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void ReplaceSerializedReferences(GameObject root, Object from, Object to)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;

                try
                {
                    var serialized = new SerializedObject(component);
                    var iterator = serialized.GetIterator();
                    var changed = false;
                    var enterChildren = true;
                    while (iterator.Next(enterChildren))
                    {
                        enterChildren = true;
                        if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                            iterator.objectReferenceValue == from)
                        {
                            iterator.objectReferenceValue = to;
                            changed = true;
                        }
                    }

                    if (changed)
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"Moo-tor Vehicle: skipped reference scan on '{component.name}' " +
                        $"({component.GetType().FullName}): {exception.Message}");
                }
            }
        }

        private static void ReplaceSerializedString(GameObject root, string from, string to)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;

                try
                {
                    var serialized = new SerializedObject(component);
                    var iterator = serialized.GetIterator();
                    var changed = false;
                    var enterChildren = true;
                    while (iterator.Next(enterChildren))
                    {
                        enterChildren = true;
                        if (iterator.propertyType == SerializedPropertyType.String &&
                            string.Equals(iterator.stringValue, from, StringComparison.Ordinal))
                        {
                            iterator.stringValue = to;
                            changed = true;
                        }
                    }

                    if (changed)
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"Moo-tor Vehicle: skipped string scan on '{component.name}' " +
                        $"({component.GetType().FullName}): {exception.Message}");
                }
            }
        }

        private static void CreateOrUpdateManifest()
        {
            if (AssetDatabase.LoadMainAssetAtPath(ManifestPath) == null &&
                !AssetDatabase.CopyAsset(TemplateManifestPath, ManifestPath))
            {
                throw new InvalidOperationException("Could not copy the mod manifest template.");
            }

            var manifest = AssetDatabase.LoadMainAssetAtPath(ManifestPath)
                ?? throw new InvalidOperationException("Moo-tor Vehicle manifest did not load.");
            manifest.name = "ModManifest";
            var serialized = new SerializedObject(manifest);
            SetString(serialized, "ModId", "MootorVehicle");
            SetString(serialized, "DisplayName", "Moo-tor Vehicle");
            SetString(serialized, "Author", "Dudeldups");
            SetString(serialized, "Version", "0.1.0");
            SetString(serialized, "AssetBundleName", BundleName + "." + BundleVariant);
            SetObject(serialized, "ModAssembly", AssetDatabase.LoadMainAssetAtPath(ModRoot + "/MootorVehicle.asmdef"));
            SetObject(serialized, "LocalesFolder", AssetDatabase.LoadMainAssetAtPath(ModRoot + "/Locales"));
            SetInt(serialized, "TargetPlatforms", 1);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manifest);
        }

        private static void BuildWindowsAssetBundle()
        {
            var assetNames = new[]
            {
                VehicleTypePath,
                VehiclePrefabPath,
                CowModelPath,
                MooAudioPath
            };

            foreach (var assetPath in assetNames)
            {
                var importer = AssetImporter.GetAtPath(assetPath)
                    ?? throw new InvalidOperationException($"No importer exists for '{assetPath}'.");
                importer.SetAssetBundleNameAndVariant(BundleName, BundleVariant);
                importer.SaveAndReimport();
            }

            var outputDirectory = Path.Combine(ModRoot, "AssetBundles", "Windows");
            Directory.CreateDirectory(outputDirectory);
            var build = new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetBundleVariant = BundleVariant,
                assetNames = assetNames
            };

            var manifest = BuildPipeline.BuildAssetBundles(
                outputDirectory,
                new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                BuildTarget.StandaloneWindows64);
            if (manifest == null)
                throw new InvalidOperationException("Unity returned no AssetBundleManifest for Moo-tor Vehicle.");

            var expectedPath = Path.Combine(outputDirectory, BundleName + "." + BundleVariant);
            if (!File.Exists(expectedPath))
                throw new FileNotFoundException("Expected Moo-tor Vehicle bundle was not produced.", expectedPath);

            Debug.Log($"Moo-tor Vehicle: built Windows AssetBundle '{expectedPath}'.");
        }

        private static void ValidateWindowsAssetBundle()
        {
            var bundlePath = ToAbsoluteProjectPath(
                ModRoot + "/AssetBundles/Windows/" + BundleName + "." + BundleVariant);
            var bundle = AssetBundle.LoadFromFile(bundlePath)
                ?? throw new InvalidOperationException("Built Moo-tor Vehicle AssetBundle could not be loaded.");
            try
            {
                var bundledType = bundle.LoadAsset<Object>(VehicleTypePath)
                    ?? throw new InvalidOperationException("Built bundle is missing the VehicleType asset.");
                var bundledPrefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath)
                    ?? throw new InvalidOperationException("Built bundle is missing the vehicle prefab.");

                var wheelControllers = bundledPrefab
                    .GetComponentsInChildren<MonoBehaviour>(true)
                    .Count(component => component != null && string.Equals(
                        component.GetType().FullName,
                        "NWH.WheelController3D.WheelController",
                        StringComparison.Ordinal));
                var cowVisual = FindDirectOrNested(bundledPrefab.transform, "MootorVehicle_CowVisual");
                var seat = FindDirectOrNested(bundledPrefab.transform, "MootorVehicle_RiderSeat");
                var cowRenderers = cowVisual != null
                    ? cowVisual.GetComponentsInChildren<Renderer>(true).Count(renderer => renderer.enabled)
                    : 0;
                var gaitRenderer = cowVisual != null
                    ? cowVisual.GetComponentInChildren<SkinnedMeshRenderer>(true)
                    : null;
                var gaitConfigured = gaitRenderer?.sharedMesh != null &&
                    gaitRenderer.sharedMesh.GetBlendShapeIndex(GaitPoseAName) >= 0 &&
                    gaitRenderer.sharedMesh.GetBlendShapeIndex(GaitPoseBName) >= 0;
                var earFlapsConfigured = gaitRenderer?.sharedMesh != null &&
                    gaitRenderer.sharedMesh.GetBlendShapeIndex(LeftEarFlapName) >= 0 &&
                    gaitRenderer.sharedMesh.GetBlendShapeIndex(RightEarFlapName) >= 0;
                var rigidbody = bundledPrefab.GetComponent<Rigidbody>();
                var bodyCollider = bundledPrefab.GetComponentInChildren<BoxCollider>(true);
                var hornConfigured = false;

                foreach (var component in bundledPrefab.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null)
                        continue;
                    var serialized = new SerializedObject(component);
                    var clips = serialized.FindProperty("soundManager.hornComponent.clips");
                    if (clips == null || !clips.isArray || clips.arraySize != 1)
                        continue;
                    var clip = clips.GetArrayElementAtIndex(0).objectReferenceValue as AudioClip;
                    hornConfigured |= clip != null && string.Equals(clip.name, "Moo", StringComparison.OrdinalIgnoreCase);
                }

                if (wheelControllers != 4 || cowRenderers == 0 || seat == null ||
                    rigidbody == null || bodyCollider == null || !hornConfigured ||
                    !gaitConfigured || !earFlapsConfigured)
                {
                    throw new InvalidOperationException(
                        $"Built bundle validation failed: type={bundledType.name} wheels={wheelControllers} " +
                        $"cowRenderers={cowRenderers} seat={seat != null} rigidbody={rigidbody != null} " +
                        $"bodyCollider={bodyCollider != null} horn={hornConfigured} " +
                        $"gait={gaitConfigured} earFlaps={earFlapsConfigured}.");
                }

                Debug.Log(
                    $"Moo-tor Vehicle: validated built bundle type='{bundledType.name}' " +
                    $"wheels={wheelControllers} cowRenderers={cowRenderers} riderSeat=true " +
                    $"mass={rigidbody.mass:F0} horn='Moo' gait=true earFlaps=true.");
            }
            finally
            {
                bundle.Unload(false);
            }
        }

        private static void CreateThumbnail()
        {
            var cowSource = AssetDatabase.LoadAssetAtPath<GameObject>(CowModelPath);
            if (cowSource == null)
                return;

            const int previewLayer = 30;
            GameObject? previewCow = null;
            GameObject? cameraObject = null;
            GameObject? keyLightObject = null;
            GameObject? fillLightObject = null;
            RenderTexture? renderTexture = null;
            Texture2D? thumbnail = null;
            var previousActive = RenderTexture.active;
            try
            {
                previewCow = Object.Instantiate(cowSource);
                previewCow.name = "Moo-tor Vehicle Preview";
                previewCow.hideFlags = HideFlags.HideAndDontSave;
                SetLayerRecursively(previewCow, previewLayer);
                var previewRenderers = previewCow.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in previewRenderers)
                    renderer.enabled = true;
                var bounds = NormalizeCowVisual(previewCow);
                foreach (var renderer in previewRenderers)
                {
                    var meshFilter = renderer.GetComponent<MeshFilter>();
                    var mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                    var material = renderer.sharedMaterial;
                    Debug.Log(
                        $"Moo-tor Vehicle: preview renderer='{renderer.name}' layer={renderer.gameObject.layer} " +
                        $"enabled={renderer.enabled} vertices={(mesh != null ? mesh.vertexCount : 0)} " +
                        $"material='{material?.name}' shader='{material?.shader?.name}' queue={material?.renderQueue}.");
                }

                cameraObject = new GameObject("Moo-tor Vehicle Preview Camera")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                var camera = cameraObject.AddComponent<Camera>();
                camera.fieldOfView = 28f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 100f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.22f, 0.16f, 1f);
                camera.cullingMask = 1 << previewLayer;
                camera.allowHDR = false;
                camera.transform.position = bounds.center + new Vector3(4.2f, 2.7f, 5.2f);
                camera.transform.rotation = Quaternion.LookRotation(
                    bounds.center + Vector3.up * 0.15f - camera.transform.position,
                    Vector3.up);

                keyLightObject = CreatePreviewLight(
                    "Moo-tor Vehicle Preview Key",
                    previewLayer,
                    Quaternion.Euler(35f, -35f, 0f),
                    2.2f);
                fillLightObject = CreatePreviewLight(
                    "Moo-tor Vehicle Preview Fill",
                    previewLayer,
                    Quaternion.Euler(20f, 145f, 0f),
                    0.9f);

                renderTexture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 4,
                    hideFlags = HideFlags.HideAndDontSave
                };
                renderTexture.Create();
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                thumbnail = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                thumbnail.ReadPixels(new Rect(0f, 0f, 512f, 512f), 0, 0);
                thumbnail.Apply();
                if (!HasVisiblePreviewContent(thumbnail))
                    throw new InvalidOperationException(
                        "Thumbnail is blank even though Unity has a graphics device.");

                File.WriteAllBytes(ToAbsoluteProjectPath(ThumbnailPath), thumbnail.EncodeToPNG());
                AssetDatabase.ImportAsset(ThumbnailPath, ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("Moo-tor Vehicle: rendered thumbnail from the supplied cow model.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Moo-tor Vehicle: thumbnail render failed: {exception.Message}");
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (thumbnail != null)
                    Object.DestroyImmediate(thumbnail);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    Object.DestroyImmediate(renderTexture);
                }
                if (fillLightObject != null)
                    Object.DestroyImmediate(fillLightObject);
                if (keyLightObject != null)
                    Object.DestroyImmediate(keyLightObject);
                if (cameraObject != null)
                    Object.DestroyImmediate(cameraObject);
                if (previewCow != null)
                    Object.DestroyImmediate(previewCow);
            }
        }

        private static GameObject CreatePreviewLight(
            string name,
            int layer,
            Quaternion rotation,
            float intensity)
        {
            var lightObject = new GameObject(name)
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = layer
            };
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.cullingMask = 1 << layer;
            light.transform.rotation = rotation;
            return lightObject;
        }

        private static bool HasVisiblePreviewContent(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            if (pixels.Length == 0)
                return false;

            var background = pixels[0];
            var changedPixels = 0;
            foreach (var pixel in pixels)
            {
                var difference =
                    Mathf.Abs(pixel.r - background.r) +
                    Mathf.Abs(pixel.g - background.g) +
                    Mathf.Abs(pixel.b - background.b) +
                    Mathf.Abs(pixel.a - background.a);
                if (difference > 12)
                    changedPixels++;
            }

            return changedPixels > pixels.Length / 100;
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve Unity project root.");
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                transform.gameObject.layer = layer;
        }

        private static Transform? FindDirectOrNested(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                    return child;
            return null;
        }

        private static bool SetString(SerializedObject serialized, string path, string value)
        {
            var property = serialized.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.String)
                return false;
            property.stringValue = value;
            return true;
        }

        private readonly struct CowMaterialFixResult
        {
            internal CowMaterialFixResult(
                int rendererCount,
                int decalMasksCleared,
                int hdrpMaterialsFixed,
                int hdrpMaterialsValidated,
                string shaderNames)
            {
                RendererCount = rendererCount;
                DecalMasksCleared = decalMasksCleared;
                HdrpMaterialsFixed = hdrpMaterialsFixed;
                HdrpMaterialsValidated = hdrpMaterialsValidated;
                ShaderNames = shaderNames;
            }

            internal int RendererCount { get; }
            internal int DecalMasksCleared { get; }
            internal int HdrpMaterialsFixed { get; }
            internal int HdrpMaterialsValidated { get; }
            internal string ShaderNames { get; }
        }

        private static CowMaterialFixResult FixSolidCowMaterialsForBundle(GameObject cowVisual)
        {
            const uint hdrpDecalLayerMask = 0x0000FF00u;
            var renderers = cowVisual.GetComponentsInChildren<Renderer>(true);
            var materials = new HashSet<Material>();
            var shaderNames = new HashSet<string>(StringComparer.Ordinal);
            var decalMasksCleared = 0;
            var hdrpMaterialsFixed = 0;
            var hdrpMaterialsValidated = 0;

            foreach (var renderer in renderers)
            {
                var previousMask = renderer.renderingLayerMask;
                renderer.renderingLayerMask &= ~hdrpDecalLayerMask;
                if (renderer.renderingLayerMask != previousMask)
                    decalMasksCleared++;

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !materials.Add(material))
                        continue;

                    var shaderName = material.shader != null ? material.shader.name : "<null>";
                    var shaderGraphTarget = material.GetTag("ShaderGraphTargetId", false, string.Empty);
                    if (!shaderName.StartsWith("HDRP/", StringComparison.Ordinal) &&
                        !shaderGraphTarget.StartsWith("HD", StringComparison.Ordinal) &&
                        !material.HasProperty("_SupportDecals"))
                    {
                        shaderNames.Add(shaderName);
                        continue;
                    }

                    RebindMaterialToHdrpLit(material);

                    SetOpaqueMaterialColor(material, "_BaseColor");
                    SetOpaqueMaterialColor(material, "baseColorFactor");
                    SetMaterialFloat(material, "_SurfaceType", 0f);
                    SetMaterialFloat(material, "_AlphaCutoffEnable", 0f);
                    SetMaterialFloat(material, "_SupportDecals", 0f);
                    SetMaterialFloat(material, "_ReceivesSSR", 0f);
                    SetMaterialFloat(material, "_ReceivesSSRTransparent", 0f);
                    SetMaterialFloat(material, "_RefractionModel", 0f);
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                    material.SetOverrideTag("RenderType", "Opaque");

                    if (TryValidateHdrpMaterialForBundle(material))
                        hdrpMaterialsValidated++;

                    material.EnableKeyword("_DISABLE_DECALS");
                    material.EnableKeyword("_DISABLE_SSR");
                    material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
                    SetMaterialFloat(material, "_ZWrite", 1f);
                    SetMaterialFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    SetMaterialFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                    hdrpMaterialsFixed++;
                    var finalShaderName = material.shader != null ? material.shader.name : "<null>";
                    shaderNames.Add(string.Equals(shaderName, finalShaderName, StringComparison.Ordinal)
                        ? finalShaderName
                        : shaderName + "->" + finalShaderName);
                }
            }

            var orderedShaderNames = shaderNames.ToList();
            orderedShaderNames.Sort(StringComparer.Ordinal);
            return new CowMaterialFixResult(
                renderers.Length,
                decalMasksCleared,
                hdrpMaterialsFixed,
                hdrpMaterialsValidated,
                string.Join("|", orderedShaderNames));
        }

        private static void SetOpaqueMaterialColor(Material material, string property)
        {
            if (!material.HasProperty(property))
                return;

            var color = material.GetColor(property);
            color.a = 1f;
            material.SetColor(property, color);
        }

        private static void RebindMaterialToHdrpLit(Material material)
        {
            var hdrpLit = Shader.Find("HDRP/Lit") ??
                          Shader.Find("High Definition Render Pipeline/Lit");
            if (hdrpLit == null || material.shader == hdrpLit)
                return;

            var baseColor = GetMaterialColor(material, "baseColorFactor", "_BaseColor", Color.white);
            baseColor.a = 1f;
            var baseTextureProperty = FirstMaterialTextureProperty(
                material, "baseColorTexture", "_BaseColorMap", "_MainTex");
            var baseTexture = baseTextureProperty != null ? material.GetTexture(baseTextureProperty) : null;
            var baseScale = baseTextureProperty != null ? material.GetTextureScale(baseTextureProperty) : Vector2.one;
            var baseOffset = baseTextureProperty != null ? material.GetTextureOffset(baseTextureProperty) : Vector2.zero;

            var normalTextureProperty = FirstMaterialTextureProperty(material, "normalTexture", "_NormalMap");
            var normalTexture = normalTextureProperty != null ? material.GetTexture(normalTextureProperty) : null;
            var normalScale = GetMaterialFloat(
                material, "normalTexture_scale", "normalScale", "_NormalScale", 1f);
            var metallic = GetMaterialFloat(material, "metallicFactor", "_Metallic", null, 0f);
            var roughness = GetMaterialFloat(material, "roughnessFactor", null, null, 1f);

            material.shader = hdrpLit;
            SetMaterialColor(material, "_BaseColor", baseColor);
            SetMaterialTexture(material, "_BaseColorMap", baseTexture, baseScale, baseOffset);
            SetMaterialTexture(material, "_NormalMap", normalTexture, Vector2.one, Vector2.zero);
            SetMaterialFloat(material, "_NormalScale", normalScale);
            SetMaterialFloat(material, "_Metallic", metallic);
            SetMaterialFloat(material, "_Smoothness", 1f - Mathf.Clamp01(roughness));
        }

        private static string? FirstMaterialTextureProperty(Material material, params string[] properties)
        {
            foreach (var property in properties)
                if (material.HasProperty(property) && material.GetTexture(property) != null)
                    return property;
            return null;
        }

        private static Color GetMaterialColor(
            Material material,
            string firstProperty,
            string secondProperty,
            Color fallback)
        {
            if (material.HasProperty(firstProperty))
                return material.GetColor(firstProperty);
            if (material.HasProperty(secondProperty))
                return material.GetColor(secondProperty);
            return fallback;
        }

        private static float GetMaterialFloat(
            Material material,
            string firstProperty,
            string? secondProperty,
            string? thirdProperty,
            float fallback)
        {
            if (material.HasProperty(firstProperty))
                return material.GetFloat(firstProperty);
            if (secondProperty != null && material.HasProperty(secondProperty))
                return material.GetFloat(secondProperty);
            if (thirdProperty != null && material.HasProperty(thirdProperty))
                return material.GetFloat(thirdProperty);
            return fallback;
        }

        private static void SetMaterialColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
                material.SetColor(property, value);
        }

        private static void SetMaterialTexture(
            Material material,
            string property,
            Texture? texture,
            Vector2 scale,
            Vector2 offset)
        {
            if (!material.HasProperty(property))
                return;

            material.SetTexture(property, texture);
            material.SetTextureScale(property, scale);
            material.SetTextureOffset(property, offset);
        }

        private static void SetMaterialFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }

        private static bool TryValidateHdrpMaterialForBundle(Material material)
        {
            const string hdMaterialTypeName = "UnityEngine.Rendering.HighDefinition.HDMaterial";
            const string shaderGraphApiTypeName = "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType(hdMaterialTypeName, false);
                    if (type == null)
                        continue;

                    var method = type.GetMethod(
                        "ValidateMaterial",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Static,
                        null,
                        new[] { typeof(Material) },
                        null);
                    if (method == null)
                        continue;

                    var result = method.Invoke(null, new object[] { material });
                    if (result is not bool validated || validated)
                        return true;
                    break;
                }

                // glTFast's HDRP graph can lack the target tag used by HDMaterial's
                // dispatcher. Validate it through HDRP's graph-specific path instead.
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType(shaderGraphApiTypeName, false);
                    if (type == null)
                        continue;

                    var method = type.GetMethod(
                        "ValidateLightingMaterial",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Static,
                        null,
                        new[] { typeof(Material) },
                        null);
                    if (method == null)
                        continue;

                    method.Invoke(null, new object[] { material });
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool SetInt(SerializedObject serialized, string path, int value)
        {
            var property = serialized.FindProperty(path);
            if (property == null ||
                (property.propertyType != SerializedPropertyType.Integer &&
                 property.propertyType != SerializedPropertyType.Enum))
                return false;
            property.intValue = value;
            return true;
        }

        private static bool SetFloat(SerializedObject serialized, string path, float value)
        {
            var property = serialized.FindProperty(path);
            if (property == null)
                return false;
            if (property.propertyType == SerializedPropertyType.Float)
            {
                property.floatValue = value;
                return true;
            }
            if (property.propertyType == SerializedPropertyType.Integer)
            {
                property.intValue = Mathf.RoundToInt(value);
                return true;
            }
            return false;
        }

        private static bool SetBool(
            SerializedObject serialized,
            string path,
            bool value,
            string? requiredSiblingPath = null)
        {
            if (requiredSiblingPath != null && serialized.FindProperty(requiredSiblingPath) == null)
                return false;
            var property = serialized.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.Boolean)
                return false;
            property.boolValue = value;
            return true;
        }

        private static bool SetVector3(SerializedObject serialized, string path, Vector3 value)
        {
            var property = serialized.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.Vector3)
                return false;
            property.vector3Value = value;
            return true;
        }

        private static bool SetAnimationCurve(
            SerializedObject serialized,
            string path,
            AnimationCurve value)
        {
            var property = serialized.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.AnimationCurve)
                return false;
            property.animationCurveValue = value;
            return true;
        }

        private static bool SetObject(SerializedObject serialized, string path, Object? value)
        {
            var property = serialized.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                return false;
            property.objectReferenceValue = value;
            return true;
        }

        private static bool ClearArray(SerializedObject serialized, string path)
        {
            var property = serialized.FindProperty(path);
            if (property == null || !property.isArray)
                return false;
            property.arraySize = 0;
            return true;
        }

        private static bool SetObjectArray(SerializedObject serialized, string path, Object[] values)
        {
            var property = serialized.FindProperty(path);
            if (property == null || !property.isArray)
                return false;

            property.arraySize = values.Length;
            for (var index = 0; index < values.Length; index++)
            {
                var element = property.GetArrayElementAtIndex(index);
                if (element.propertyType != SerializedPropertyType.ObjectReference)
                    return false;
                element.objectReferenceValue = values[index];
            }

            return true;
        }

        private static bool SetStringArray(SerializedObject serialized, string path, string[] values)
        {
            var property = serialized.FindProperty(path);
            if (property == null || !property.isArray)
                return false;

            property.arraySize = values.Length;
            for (var index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).stringValue = values[index];

            return true;
        }
    }
}
