#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Characters;
using Buildings.BuildingTypes.Special.PrivateDriverService;
using Helpers;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.AI;
using UnityEngine.Playables;
using UnityEngine.Rendering;

[DefaultExecutionOrder(100)]
internal sealed class FerrariSF90SpiderDriverController : MonoBehaviour
{
    private const string SteeringWheelName = "STEERING_WHEEL";
    private const string SittingClipName = "SitDeliveryTruck";
    private const float SeatedScale = 0.94f;
    // First in-game fit: the SF90 seatback is noticeably reclined. The old pose used -2°;
    // -14° is the final in-game fit after a further 2° rearward correction.
    private const float DriverLeanDegrees = -14f;
    private const float HandHalfSpacing = 0.19f;
    private const float HandForwardOffset = 0.075f;
    private const float HandHeightOffset = 0.015f;
    private const float FootRaise = 0.16f;
    private const float FootForwardOffset = 0.26f;
    private const int ExitRecoveryDelayFrames = 3;
    private const float ExitNavMeshProbeRadius = 1.25f;
    private const float ExitGroundOffset = 0.05f;
    private const float ExitCapsuleRadius = 0.28f;
    private const float ExitCapsuleBottom = 0.34f;
    private const float ExitCapsuleTop = 1.62f;
    private static readonly Vector2[] SafeExitOffsets =
    {
        new(-1.75f, 0f),
        new(1.75f, 0f),
        new(-2.10f, 0.85f),
        new(2.10f, 0.85f),
        new(-2.10f, -0.85f),
        new(2.10f, -0.85f),
        new(0f, 2.85f),
        new(0f, -2.85f),
    };
    // Pelvis position relative to the SF90Spider steering-wheel pivot, in vehicle axes.
    private static readonly Vector3 SeatOffset = new(0f, -0.34f, -0.55f);
    private const int MaximumAttempts = 20;
    private readonly List<UnityEngine.Object> ownedAssets = new();
    private VehicleController? vehicle;
    private Transform? vehicleRoot;
    private bool ambientTraffic;
    private ModContext? context;
    private GameObject? driverRoot;
    private GameObject? sourceNpc;
    private Transform? hips;
    private Transform? steeringWheel;
    private SeatedLimb? leftArm;
    private SeatedLimb? rightArm;
    private SeatedLimb? leftLeg;
    private SeatedLimb? rightLeg;
    private PlayableGraph poseGraph;
    private AnimationClipPlayable pose;
    private float poseLength;
    private float poseTime;
    private bool occupied;
    private int attempts;
    private float nextAttempt;
    private string? lastFailure;
    private readonly Collider[] exitOverlapBuffer = new Collider[24];
    private Coroutine? exitRecoveryCoroutine;
    private NavMeshObstacle[]? exitObstacles;
    private bool[]? exitObstacleStates;
    private bool exitObstaclesDisabled;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        vehicleRoot = controller.transform;
        context = modContext;
        exitObstacles = controller.GetComponentsInChildren<NavMeshObstacle>(true);
    }

    internal void InitializeForAmbientTraffic(ModContext? modContext)
    {
        vehicleRoot = transform;
        ambientTraffic = true;
        context = modContext;
    }

    private void LateUpdate()
    {
        if (vehicleRoot == null)
            return;
        if (ambientTraffic && GetComponent<PrivateDriverVehicle>() != null)
        {
            if (occupied || driverRoot != null)
                RemoveDriver();
            occupied = false;
            return;
        }

        var isOccupied = ambientTraffic || vehicle != null && vehicle.controlledByPlayer;
        if (isOccupied != occupied)
        {
            occupied = isOccupied;
            attempts = 0;
            nextAttempt = 0f;
            lastFailure = null;
            if (!occupied)
            {
                RemoveDriver();
                LogInfo("exited; seated model removed.");
                DisableExitObstacles();
                ScheduleExitRecovery();
            }
            else
            {
                StopExitRecovery();
                LogInfo("occupied; preparing current player appearance.");
            }
        }

        if (!occupied)
            return;

        try
        {
            if (driverRoot == null)
            {
                if (attempts >= MaximumAttempts || Time.unscaledTime < nextAttempt)
                    return;
                attempts++;
                nextAttempt = Time.unscaledTime + 0.5f;
                CreateDriver();
            }

            // Evaluate only the native sitting clip, without player controller scripts,
            // animation events, navigation, colliders or animator state behaviours.
            poseTime = Mathf.Repeat(poseTime + Time.deltaTime, poseLength);
            pose.SetTime(poseTime);
            leftArm?.RestoreAnimationPose();
            rightArm?.RestoreAnimationPose();
            leftLeg?.RestoreAnimationPose();
            rightLeg?.RestoreAnimationPose();
            poseGraph.Evaluate(0f);
            AlignWithSeat();
            AlignHandsWithWheel();
            RaiseFeetAndKnees();
        }
        catch (Exception ex)
        {
            RemoveDriver();
            var reason = ex.GetBaseException().Message;
            if (reason != lastFailure || attempts >= MaximumAttempts)
            {
                lastFailure = reason;
                context?.Logger.Warn($"FerrariSF90Spider driver vehicle={vehicleRoot.GetInstanceID()} " +
                                     $"attempt={attempts}/{MaximumAttempts}: {reason}");
            }
        }
    }

    private void CreateDriver()
    {
        Transform sourceRoot;
        AppearanceSetter? appearance;
        if (ambientTraffic)
        {
            var pedestrianPrefab = PrefabHelper.LoadPrefabAssetByName("Characters/Pedestrian");
            if (pedestrianPrefab == null)
                throw new InvalidOperationException("Native pedestrian prefab is unavailable.");
            var wasActive = pedestrianPrefab.activeSelf;
            try
            {
                pedestrianPrefab.SetActive(false);
                sourceNpc = Instantiate(pedestrianPrefab);
            }
            finally
            {
                pedestrianPrefab.SetActive(wasActive);
            }
            sourceNpc.name = "FerrariSF90Spider_NpcAppearanceSource";
            appearance = sourceNpc.GetComponentInChildren<AppearanceSetter>(true);
            if (appearance == null)
                throw new InvalidOperationException("Native pedestrian appearance is unavailable.");
            var seed = (vehicleRoot!.GetInstanceID() & 0x3fffffff) + 1;
            appearance.SetRandomAppearance((seed & 1) == 0 ? Gender.Male : Gender.Female,
                25 * 365 + seed % (25 * 365), seed);
            sourceRoot = sourceNpc.transform;
        }
        else
        {
            var character = PlayerHelper.PlayerController?.Character;
            appearance = character?.appearanceSetter;
            if (character == null || appearance == null)
                throw new InvalidOperationException("Player appearance is not ready.");
            sourceRoot = character.transform;
        }

        var sourceAnimator = typeof(AppearanceSetter).GetField("animator",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(appearance) as Animator;
        if (sourceAnimator == null || sourceAnimator.avatar == null || !sourceAnimator.avatar.isHuman)
            throw new InvalidOperationException("Source humanoid animator/avatar is not ready.");

        AnimationClip? sittingClip = null;
        var clipController = sourceAnimator.runtimeAnimatorController;
        if (clipController != null)
            foreach (var clip in clipController.animationClips)
                if (clip != null && string.Equals(clip.name, SittingClipName, StringComparison.Ordinal))
                    sittingClip = clip;
        if (sittingClip == null && ambientTraffic)
        {
            var playerController = PlayerHelper.PlayerController?.Character?.appearanceSetter?
                .GetComponentInChildren<Animator>(true)?.runtimeAnimatorController;
            if (playerController != null)
                foreach (var clip in playerController.animationClips)
                    if (clip != null && string.Equals(clip.name, SittingClipName, StringComparison.Ordinal))
                        sittingClip = clip;
        }
        if (sittingClip == null || sittingClip.length <= 0f)
            throw new InvalidOperationException($"Native seated animation '{SittingClipName}' is unavailable.");

        steeringWheel = null;
        if (ambientTraffic)
        {
            foreach (var child in vehicleRoot!.GetComponentsInChildren<Transform>(true))
                if (child.name == "NpcDriverSeatAnchor")
                    steeringWheel = child;
        }
        else
        {
            foreach (var child in vehicleRoot!.GetComponentsInChildren<Transform>(true))
                if (child.name.IndexOf(SteeringWheelName, StringComparison.OrdinalIgnoreCase) >= 0)
                    steeringWheel = child;
        }
        if (steeringWheel == null)
            throw new InvalidOperationException("SF90Spider steering-wheel seat reference is missing.");

        if (!sourceAnimator.transform.IsChildOf(sourceRoot) && sourceAnimator.transform != sourceRoot)
            throw new InvalidOperationException("Source animator is outside the character hierarchy.");

        driverRoot = new GameObject(ambientTraffic ? "FerrariSF90Spider_SeatedNpcDriver" : "FerrariSF90Spider_SeatedPlayer");
        driverRoot.SetActive(false);
        driverRoot.layer = sourceRoot.gameObject.layer;
        driverRoot.transform.SetParent(vehicleRoot, false);
        var transforms = new Dictionary<Transform, Transform>();
        CopyTransforms(sourceRoot, driverRoot.transform, transforms);
        // EnterVehicle hides the real character by setting its root scale to zero.
        // Scale around the anchored hips, preserving the tested seat height.
        driverRoot.transform.localScale = Vector3.one * SeatedScale;
        driverRoot.transform.localRotation = Quaternion.identity;
        driverRoot.transform.localPosition = Vector3.zero;

        var rendererCount = 0;
        var suppressedCount = 0;
        var lowerDetailRenderers = GetLowerDetailRenderers(appearance.transform);
        var mergedMesh = ambientTraffic ? appearance.lastMergedMeshRenderer : null;
        foreach (var source in appearance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var isMergedMesh = source == mergedMesh;
            if ((!source.enabled && !isMergedMesh) || source.sharedMesh == null ||
                !IsActiveWithinCharacter(source.transform, sourceRoot))
                continue;
            // enabled/activeSelf do not include shadow-only, forced-hidden or LOD
            // visibility. Drawing those as ordinary meshes can overlap the body.
            if ((!isMergedMesh && source.forceRenderingOff) ||
                (!isMergedMesh && source.shadowCastingMode == ShadowCastingMode.ShadowsOnly) ||
                lowerDetailRenderers.Contains(source))
            {
                suppressedCount++;
                LogInfo($"skipped mesh='{source.name}' forceRenderingOff={source.forceRenderingOff} " +
                        $"shadowMode={source.shadowCastingMode} lowerDetail={lowerDetailRenderers.Contains(source)}.");
                continue;
            }
            CopyRenderer(source, transforms);
            rendererCount++;
        }
        if (rendererCount == 0)
            throw new InvalidOperationException(
                $"Source has no visible skinned appearance meshes; " +
                $"total={appearance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length} ambient={ambientTraffic}.");

        var animator = transforms[sourceAnimator.transform].gameObject.AddComponent<Animator>();
        animator.avatar = sourceAnimator.avatar;
        animator.applyRootMotion = false;
        animator.fireEvents = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        driverRoot.SetActive(true);
        poseGraph = PlayableGraph.Create("FerrariSF90Spider seated player");
        poseGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        pose = AnimationClipPlayable.Create(poseGraph, sittingClip);
        pose.SetApplyFootIK(false);
        pose.SetApplyPlayableIK(false);
        AnimationPlayableOutput.Create(poseGraph, "Seated pose", animator).SetSourcePlayable(pose);
        poseLength = sittingClip.length;
        poseTime = 0f;
        poseGraph.Play();
        poseGraph.Evaluate(0f);
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null)
            throw new InvalidOperationException("Seated avatar has no humanoid hips bone.");

        AlignWithSeat();
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        leftArm = CreateArm(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand);
        rightArm = CreateArm(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand);
        leftLeg = CreateLimb(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot, "left leg");
        rightLeg = CreateLimb(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot, "right leg");
        var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        var originalLeftHand = VehiclePosition(leftHand);
        var originalRightHand = VehiclePosition(rightHand);
        var originalLeftFoot = VehiclePosition(leftFoot);
        var originalRightFoot = VehiclePosition(rightFoot);
        AlignHandsWithWheel();
        RaiseFeetAndKnees();
        LogInfo($"hand alignment halfSpacing={HandHalfSpacing:F3} forward={HandForwardOffset:F3} " +
                $"height={HandHeightOffset:F3} " +
                $"leftBefore={originalLeftHand} leftAfter={VehiclePosition(leftHand)} " +
                $"rightBefore={originalRightHand} rightAfter={VehiclePosition(rightHand)}.");
        LogInfo($"leg alignment footRaise={FootRaise:F3} forward={FootForwardOffset:F3} " +
                $"leftBefore={originalLeftFoot} leftAfter={VehiclePosition(leftFoot)} " +
                $"rightBefore={originalRightFoot} rightAfter={VehiclePosition(rightFoot)}.");
        LogInfo($"created from current player appearance; renderers={rendererCount} " +
                $"suppressedRenderers={suppressedCount} scale={SeatedScale:F2} " +
                $"transforms={transforms.Count} clip='{sittingClip.name}' " +
                $"hips={VehiclePosition(hips)} head={VehiclePosition(head)} " +
                $"leftHand={VehiclePosition(leftHand)} rightHand={VehiclePosition(rightHand)} " +
                $"steeringWheel={VehiclePosition(steeringWheel)}.");
        if (ambientTraffic && FerrariSF90SpiderDiagnostics.DebugEnabled &&
            FerrariSF90SpiderDiagnostics.NpcDriverDebugEnabled)
            context?.Logger.Info($"FerrariSF90Spider NPC driver vehicle={vehicleRoot?.GetInstanceID()} " +
                                 $"renderers={rendererCount} source=native-pedestrian.");
        if (sourceNpc != null)
        {
            Destroy(sourceNpc);
            sourceNpc = null;
        }
    }

    private void AlignWithSeat()
    {
        if (driverRoot == null || hips == null || steeringWheel == null || vehicleRoot == null)
            return;
        driverRoot.transform.rotation =
            vehicleRoot.rotation * Quaternion.Euler(DriverLeanDegrees, 0f, 0f);
        var seatPosition = steeringWheel.position + vehicleRoot.TransformVector(SeatOffset);
        driverRoot.transform.position += seatPosition - hips.position;
    }

    private SeatedLimb? CreateArm(Animator animator, HumanBodyBones upperBone,
        HumanBodyBones lowerBone, HumanBodyBones handBone)
        => CreateLimb(animator, upperBone, lowerBone, handBone, handBone.ToString());

    private SeatedLimb? CreateLimb(Animator animator, HumanBodyBones upperBone,
        HumanBodyBones lowerBone, HumanBodyBones endBone, string description)
    {
        var upper = animator.GetBoneTransform(upperBone);
        var lower = animator.GetBoneTransform(lowerBone);
        var end = animator.GetBoneTransform(endBone);
        if (upper != null && lower != null && end != null)
            return new SeatedLimb(upper, lower, end);
        context?.Logger.Warn($"FerrariSF90Spider driver vehicle={vehicleRoot?.GetInstanceID()}: " +
                             $"cannot refine {description}; limb bones are missing. Keeping native pose.");
        return null;
    }

    private void AlignHandsWithWheel()
    {
        if (vehicleRoot == null || steeringWheel == null)
            return;
        var centerX = vehicleRoot.InverseTransformPoint(steeringWheel.position).x;
        AlignHand(leftArm, centerX - HandHalfSpacing);
        AlignHand(rightArm, centerX + HandHalfSpacing);
    }

    private void AlignHand(SeatedLimb? arm, float targetX)
    {
        if (arm == null || vehicleRoot == null)
            return;
        var target = vehicleRoot.InverseTransformPoint(arm.End.position);
        target.x = targetX;
        target.y += HandHeightOffset;
        target.z += HandForwardOffset;
        arm.AimAt(vehicleRoot.TransformPoint(target), vehicleRoot.forward);
    }

    private void RaiseFeetAndKnees()
    {
        RaiseFoot(leftLeg);
        RaiseFoot(rightLeg);
    }

    private void RaiseFoot(SeatedLimb? leg)
    {
        if (leg == null || vehicleRoot == null)
            return;
        var target = vehicleRoot.InverseTransformPoint(leg.End.position);
        target.y += FootRaise;
        target.z += FootForwardOffset;
        leg.AimAt(vehicleRoot.TransformPoint(target), vehicleRoot.forward);
    }

    private sealed class SeatedLimb
    {
        private readonly Transform upper;
        private readonly Transform lower;
        public readonly Transform End;
        private Quaternion upperPose;
        private Quaternion lowerPose;
        private Quaternion endPose;
        private bool hasAdjustment;

        public SeatedLimb(Transform upper, Transform lower, Transform end)
        {
            this.upper = upper;
            this.lower = lower;
            End = end;
        }

        public void RestoreAnimationPose()
        {
            if (!hasAdjustment)
                return;
            upper.localRotation = upperPose;
            lower.localRotation = lowerPose;
            End.localRotation = endPose;
            hasAdjustment = false;
        }

        public void AimAt(Vector3 target, Vector3 fallbackDirection)
        {
            // Retain the native elbow bend and grip orientation. Only rotate bones;
            // do not stretch the mesh or move the shoulder/torso.
            if (!TrySolveElbow(upper.position, lower.position, End.position, target,
                    fallbackDirection, out var elbow, out var reachableTarget))
                return;
            upperPose = upper.localRotation;
            lowerPose = lower.localRotation;
            endPose = End.localRotation;
            hasAdjustment = true;
            var endRotation = End.rotation;
            upper.rotation = Quaternion.FromToRotation(lower.position - upper.position,
                elbow - upper.position) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(End.position - lower.position,
                reachableTarget - lower.position) * lower.rotation;
            End.rotation = endRotation;
        }
    }

    private static bool TrySolveElbow(Vector3 shoulder, Vector3 elbow, Vector3 hand,
        Vector3 target, Vector3 fallbackDirection, out Vector3 solvedElbow, out Vector3 reachableTarget)
    {
        solvedElbow = elbow;
        reachableTarget = hand;
        var upperLength = Vector3.Distance(shoulder, elbow);
        var lowerLength = Vector3.Distance(elbow, hand);
        var reach = target - shoulder;
        if (upperLength < 0.0001f || lowerLength < 0.0001f || reach.sqrMagnitude < 0.000001f)
            return false;
        var direction = reach.normalized;
        var distance = Mathf.Clamp(reach.magnitude,
            Mathf.Abs(upperLength - lowerLength) + 0.0001f, upperLength + lowerLength - 0.0001f);
        var bend = Vector3.ProjectOnPlane(elbow - shoulder, direction);
        if (bend.sqrMagnitude < 0.000001f)
            bend = Vector3.ProjectOnPlane(fallbackDirection, direction);
        if (bend.sqrMagnitude < 0.000001f)
            return false;
        var along = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) /
                    (2f * distance);
        var across = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        solvedElbow = shoulder + direction * along + bend.normalized * across;
        reachableTarget = shoulder + direction * distance;
        return true;
    }

    private static bool IsActiveWithinCharacter(Transform child, Transform root)
    {
        // Ignore the hidden character root, but retain clothing/gender selection beneath it.
        for (var current = child; current != root; current = current.parent)
        {
            if (current == null || !current.gameObject.activeSelf)
                return false;
        }
        return true;
    }

    private static HashSet<Renderer> GetLowerDetailRenderers(Transform root)
    {
        var result = new HashSet<Renderer>();
        // The visual copy has no LODGroup. Retain one complete, highest-detail
        // representation instead of drawing all of its LODs at the same time.
        foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
        {
            if (!group.enabled)
                continue;
            var lods = group.GetLODs();
            if (lods.Length == 0)
                continue;
            var highestDetail = new HashSet<Renderer>(lods[0].renderers);
            for (var level = 1; level < lods.Length; level++)
                foreach (var renderer in lods[level].renderers)
                    if (renderer != null && !highestDetail.Contains(renderer))
                        result.Add(renderer);
        }
        return result;
    }

    private static void CopyTransforms(Transform source, Transform destination,
        Dictionary<Transform, Transform> transforms)
    {
        transforms.Add(source, destination);
        foreach (Transform child in source)
        {
            var copy = new GameObject(child.name);
            copy.SetActive(child.gameObject.activeSelf);
            copy.layer = child.gameObject.layer;
            copy.transform.SetParent(destination, false);
            copy.transform.localPosition = child.localPosition;
            copy.transform.localRotation = child.localRotation;
            copy.transform.localScale = child.localScale;
            CopyTransforms(child, copy.transform, transforms);
        }
    }

    private void CopyRenderer(SkinnedMeshRenderer source, Dictionary<Transform, Transform> transforms)
    {
        var destination = transforms[source.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
        var mesh = Instantiate(source.sharedMesh);
        ownedAssets.Add(mesh);
        destination.sharedMesh = mesh;
        var sourceBones = source.bones;
        var bones = new Transform[sourceBones.Length];
        for (var index = 0; index < sourceBones.Length; index++)
        {
            if (sourceBones[index] == null || !transforms.TryGetValue(sourceBones[index], out bones[index]))
                throw new InvalidOperationException($"Appearance mesh '{source.name}' has an unmapped bone at {index}.");
        }
        destination.bones = bones;
        if (source.rootBone != null)
        {
            if (!transforms.TryGetValue(source.rootBone, out var rootBone))
                throw new InvalidOperationException($"Appearance mesh '{source.name}' has an unmapped root bone.");
            destination.rootBone = rootBone;
        }
        var sourceMaterials = source.sharedMaterials;
        var materials = new Material[sourceMaterials.Length];
        for (var index = 0; index < sourceMaterials.Length; index++)
        {
            if (sourceMaterials[index] == null)
                throw new InvalidOperationException($"Appearance mesh '{source.name}' has a missing material.");
            materials[index] = new Material(sourceMaterials[index]);
            ownedAssets.Add(materials[index]);
        }
        destination.sharedMaterials = materials;
        for (var index = 0; index < mesh.blendShapeCount; index++)
            destination.SetBlendShapeWeight(index, source.GetBlendShapeWeight(index));
        var properties = new MaterialPropertyBlock();
        source.GetPropertyBlock(properties);
        destination.SetPropertyBlock(properties);
        for (var index = 0; index < materials.Length; index++)
        {
            properties.Clear();
            source.GetPropertyBlock(properties, index);
            if (!properties.isEmpty)
                destination.SetPropertyBlock(properties, index);
        }
        destination.localBounds = source.localBounds;
        destination.updateWhenOffscreen = true;
        destination.quality = source.quality;
        destination.renderingLayerMask = source.renderingLayerMask;
        destination.shadowCastingMode = ShadowCastingMode.Off;
        destination.receiveShadows = source.receiveShadows;
        LogInfo($"copied mesh='{source.name}' vertices={mesh.vertexCount} " +
                $"blendShapes={mesh.blendShapeCount} materials={materials.Length} " +
                $"sourceShadowMode={source.shadowCastingMode} receiveShadows={source.receiveShadows} " +
                $"renderingLayerMask={source.renderingLayerMask}.");
    }

    private string VehiclePosition(Transform? target) =>
        target != null && vehicleRoot != null ? vehicleRoot.InverseTransformPoint(target.position).ToString("F3") : "missing";

    private void LogInfo(string message)
    {
        if (ambientTraffic && !FerrariSF90SpiderDiagnostics.NpcDriverDebugEnabled)
            return;
        FerrariSF90SpiderDiagnostics.Info(
            context,
            $"FerrariSF90Spider driver vehicle={vehicleRoot?.GetInstanceID()} ambient={ambientTraffic}: {message}");
    }

    private void RemoveDriver()
    {
        if (poseGraph.IsValid())
            poseGraph.Destroy();
        if (driverRoot != null)
        {
            driverRoot.SetActive(false);
            Destroy(driverRoot);
        }
        driverRoot = null;
        if (sourceNpc != null)
        {
            Destroy(sourceNpc);
            sourceNpc = null;
        }
        hips = null;
        leftArm = null;
        rightArm = null;
        leftLeg = null;
        rightLeg = null;
        foreach (var asset in ownedAssets)
            if (asset != null) Destroy(asset);
        ownedAssets.Clear();
    }

    private void ScheduleExitRecovery()
    {
        StopExitRecovery();
        exitRecoveryCoroutine = StartCoroutine(RecoverInvalidExitPlacement());
    }

    private void StopExitRecovery()
    {
        if (exitRecoveryCoroutine != null)
            StopCoroutine(exitRecoveryCoroutine);
        exitRecoveryCoroutine = null;
        RestoreExitObstacles();
    }

    private void DisableExitObstacles()
    {
        if (exitObstaclesDisabled || exitObstacles == null || exitObstacles.Length == 0)
            return;

        exitObstacleStates = new bool[exitObstacles.Length];
        var disabled = 0;
        for (var index = 0; index < exitObstacles.Length; index++)
        {
            var obstacle = exitObstacles[index];
            exitObstacleStates[index] = obstacle != null && obstacle.enabled;
            if (obstacle == null || !obstacle.enabled)
                continue;
            obstacle.enabled = false;
            disabled++;
        }

        exitObstaclesDisabled = disabled > 0;
        if (exitObstaclesDisabled)
            LogInfo($"temporarily disabled {disabled} NavMesh obstacle(s) for exit recovery.");
    }

    private void RestoreExitObstacles()
    {
        if (!exitObstaclesDisabled || exitObstacles == null || exitObstacleStates == null)
            return;

        var restored = 0;
        for (var index = 0; index < exitObstacles.Length && index < exitObstacleStates.Length; index++)
        {
            var obstacle = exitObstacles[index];
            if (obstacle == null)
                continue;
            obstacle.enabled = exitObstacleStates[index];
            if (exitObstacleStates[index])
                restored++;
        }

        exitObstaclesDisabled = false;
        exitObstacleStates = null;
        if (restored > 0)
            LogInfo($"restored {restored} NavMesh obstacle(s) after exit recovery.");
    }

    private IEnumerator RecoverInvalidExitPlacement()
    {
        for (var frame = 0; frame < ExitRecoveryDelayFrames; frame++)
            yield return null;

        exitRecoveryCoroutine = null;
        try
        {
            if (vehicle == null || vehicle.controlledByPlayer)
                yield break;

            var player = PlayerHelper.PlayerController;
            if (player == null)
                yield break;

            var playerRoot = player.transform;
            var agents = playerRoot.GetComponentsInChildren<NavMeshAgent>(true);
            if (agents.Length == 0 ||
                (HasUsableAgent(agents) && IsExitCapsuleClear(playerRoot.position, playerRoot)))
            {
                yield break;
            }

            if (!TryFindSafeExitPosition(playerRoot, out var safePosition))
            {
                context?.Logger.Warn(
                    $"player exit was blocked or off NavMesh at {playerRoot.position:F3}; " +
                    "no clear recovery point was found.");
                yield break;
            }

            PlacePlayerAtSafeExit(playerRoot, agents, safePosition);
            LogInfo($"recovered player exit to clear NavMesh position {safePosition:F3}.");
        }
        finally
        {
            RestoreExitObstacles();
        }
    }

    private static bool HasUsableAgent(IReadOnlyList<NavMeshAgent> agents)
    {
        foreach (var agent in agents)
            if (agent != null && agent.enabled && agent.isOnNavMesh)
                return true;
        return false;
    }

    private bool TryFindSafeExitPosition(Transform playerRoot, out Vector3 safePosition)
    {
        safePosition = playerRoot.position;
        if (vehicle == null)
            return false;

        var right = Vector3.ProjectOnPlane(vehicle.transform.right, Vector3.up).normalized;
        var forward = Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up).normalized;
        if (right.sqrMagnitude < 0.9f || forward.sqrMagnitude < 0.9f)
            return false;

        var bestDistance = float.PositiveInfinity;
        var found = false;
        foreach (var offset in SafeExitOffsets)
        {
            var requested = vehicle.transform.position + right * offset.x + forward * offset.y;
            requested.y = playerRoot.position.y;
            if (!NavMesh.SamplePosition(
                    requested,
                    out var hit,
                    ExitNavMeshProbeRadius,
                    NavMesh.AllAreas))
            {
                continue;
            }

            var candidate = hit.position + Vector3.up * ExitGroundOffset;
            if (!IsExitCapsuleClear(candidate, playerRoot))
                continue;

            var distance = (candidate - playerRoot.position).sqrMagnitude;
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            safePosition = candidate;
            found = true;
        }

        return found;
    }

    private bool IsExitCapsuleClear(Vector3 position, Transform playerRoot)
    {
        var overlapCount = Physics.OverlapCapsuleNonAlloc(
            position + Vector3.up * ExitCapsuleBottom,
            position + Vector3.up * ExitCapsuleTop,
            ExitCapsuleRadius,
            exitOverlapBuffer,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        for (var index = 0; index < overlapCount; index++)
        {
            var collider = exitOverlapBuffer[index];
            exitOverlapBuffer[index] = null!;
            if (collider == null || collider.transform.IsChildOf(playerRoot))
                continue;
            return false;
        }
        return overlapCount < exitOverlapBuffer.Length;
    }

    private static void PlacePlayerAtSafeExit(
        Transform playerRoot,
        IReadOnlyList<NavMeshAgent> agents,
        Vector3 safePosition)
    {
        var characterControllers = playerRoot.GetComponentsInChildren<CharacterController>(true);
        var controllerStates = new bool[characterControllers.Length];
        for (var index = 0; index < characterControllers.Length; index++)
        {
            var controller = characterControllers[index];
            controllerStates[index] = controller != null && controller.enabled;
            if (controller != null)
                controller.enabled = false;
        }

        foreach (var agent in agents)
            if (agent != null)
                agent.enabled = false;

        playerRoot.position = safePosition;
        Physics.SyncTransforms();

        foreach (var agent in agents)
        {
            if (agent == null)
                continue;
            agent.enabled = true;
            if (agent.isOnNavMesh)
            {
                agent.Warp(safePosition);
                agent.ResetPath();
            }
        }

        for (var index = 0; index < characterControllers.Length; index++)
            if (characterControllers[index] != null)
                characterControllers[index].enabled = controllerStates[index];
        Physics.SyncTransforms();
    }

    private void OnDisable()
    {
        StopExitRecovery();
        RestoreExitObstacles();
        RemoveDriver();
        occupied = false;
    }

    private void OnDestroy()
    {
        StopExitRecovery();
        RestoreExitObstacles();
        RemoveDriver();
    }
}

