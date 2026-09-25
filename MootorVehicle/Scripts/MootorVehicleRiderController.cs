#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

namespace MootorVehicle
{
    [DefaultExecutionOrder(100)]
    internal sealed class MootorVehicleRiderController : MonoBehaviour
    {
        private const string RiderSeatName = "MootorVehicle_RiderSeat";
        private const string CowVisualName = "MootorVehicle_CowVisual";
        private const string SittingClipName = "SitDeliveryTruck";
        private const string GaitPoseAName = "MootorGaitA";
        private const string GaitPoseBName = "MootorGaitB";
        private const string LeftEarFlapName = "MootorEarLeft";
        private const string RightEarFlapName = "MootorEarRight";
        private const float RiderScale = 0.94f;
        private const float InitialParkedVisualHeightOffset = 0f;
        private const float PostRideParkedVisualHeightOffset = -0.18f;
        private const float MountedVisualHeightOffset = -0.18f;
        private const float MooHornVolume = 0.65f;
        private const float GaitStartSpeed = 0.15f;
        private const float GaitFullSpeed = 1.5f;
        private const float MinimumStridesPerSecond = 0.8f;
        private const float RegularMaximumStridesPerSecond = 1.8f;
        private const float EnergizedMaximumStridesPerSecond = 3.6f;
        private const float EarFlapDuration = 0.55f;
        private const float EarFlapMinimumDelay = 3.5f;
        private const float EarFlapMaximumDelay = 9f;
        private const int MaximumAttempts = 20;
        private const int MaximumEngineStartAttempts = 4;
        private const float DormantEngineGracePeriod = 0.08f;
        private const float EngineRestartDelay = 0.1f;
        private const float EngineStartRetryDelay = 0.4f;
        private const float MinimumHealthyEngineRpm = 100f;

        private static readonly Vector3 LeftHandOffset = new(-0.3f, -0.05f, 0.38f);
        private static readonly Vector3 RightHandOffset = new(0.3f, -0.05f, 0.38f);
        private static readonly Vector3 LeftElbowHintOffset = new(-0.46f, 0.03f, 0.18f);
        private static readonly Vector3 RightElbowHintOffset = new(0.46f, 0.03f, 0.18f);
        private static readonly Vector3 LeftFootOffset = new(-0.44f, -0.42f, 0.1f);
        private static readonly Vector3 RightFootOffset = new(0.44f, -0.42f, 0.1f);
        private static readonly Vector3 LeftKneeHintOffset = new(-0.64f, 0.1f, 0.4f);
        private static readonly Vector3 RightKneeHintOffset = new(0.64f, 0.1f, 0.4f);

        private readonly List<UnityEngine.Object> ownedAssets = new();
        private VehicleController? vehicle;
        private PhysicsVehicle? physicsVehicle;
        private Rigidbody? vehicleBody;
        private ModContext? context;
        private GameObject? riderRoot;
        private Transform? hips;
        private Transform? riderSeat;
        private Transform? cowVisual;
        private Transform? heightAdjustedSeat;
        private Vector3 cowVisualBasePosition;
        private Vector3 riderSeatBasePosition;
        private bool rideHeightConfigured;
        private bool hasBeenMounted;
        private SkinnedMeshRenderer? cowGaitRenderer;
        private int gaitPoseAIndex = -1;
        private int gaitPoseBIndex = -1;
        private int leftEarFlapIndex = -1;
        private int rightEarFlapIndex = -1;
        private float gaitPhase;
        private float gaitBlend;
        private float nextEarFlapAt;
        private float earFlapStartedAt;
        private int activeEarMask;
        private bool earFlapActive;
        private PoseLimb? leftArm;
        private PoseLimb? rightArm;
        private PoseLimb? leftLeg;
        private PoseLimb? rightLeg;
        private PlayableGraph poseGraph;
        private AnimationClipPlayable pose;
        private float poseLength;
        private float poseTime;
        private bool occupied;
        private int attempts;
        private float nextAttempt;
        private int engineStartAttempts;
        private float nextEngineStartAttempt;
        private bool engineStartFailureLogged;
        private bool engineRestartPending;
        private bool engineReady;
        private float dormantThrottleDetectedAt = -1f;
        private AudioSource? mooHornSource;
        private bool hornPressed;
        private float mountControlGraceUntil;
        private string? lastFailure;

        public void Initialize(VehicleController controller, ModContext? modContext)
        {
            vehicle = controller;
            physicsVehicle = controller.GetComponent<PhysicsVehicle>();
            vehicleBody = controller.GetComponent<Rigidbody>();
            context = modContext;
            ConfigureMooHorn();
            ConfigureRideHeight();

            if (controller.controlledByPlayer)
                NotifyMounted();
            else if (!occupied)
            {
                ApplyRideHeight(false);
                enabled = false;
            }
        }

        internal void NotifyMounted()
        {
            if (vehicle == null)
                return;

            enabled = true;
            if (occupied)
                return;

            occupied = true;
            hasBeenMounted = true;
            GetComponent<MootorVehicleAmbientMooController>()?.NotifyMounted();
            GetComponent<MootorVehicleFuelController>()?.NotifyMounted();
            attempts = 0;
            nextAttempt = 0f;
            engineStartAttempts = 0;
            nextEngineStartAttempt = Time.unscaledTime + 0.15f;
            engineStartFailureLogged = false;
            engineRestartPending = true;
            engineReady = false;
            dormantThrottleDetectedAt = -1f;
            hornPressed = false;
            mountControlGraceUntil = Time.unscaledTime + 0.5f;
            lastFailure = null;
            ScheduleNextEarFlap(true);

            ApplyRideHeight(true);
            ResetDrivetrainForMount();
            LogInfo("mounted; preparing current player appearance.");
        }

        private void ResetDrivetrainForMount()
        {
            if (physicsVehicle == null)
                return;

            try
            {
                var engine = physicsVehicle.powertrain.engine;
                var transmission = physicsVehicle.powertrain.transmission;
                engine.StopEngine();
                transmission.ShiftInto(0, true);
                transmission.currentGearRatio = 0f;
                engineRestartPending = true;
                nextEngineStartAttempt = Time.unscaledTime + EngineRestartDelay;
                LogInfo("normalized engine and transmission for mount; scheduled clean restart.");
            }
            catch (Exception exception)
            {
                engineRestartPending = false;
                context?.Logger.Warn(
                    $"Moo-tor Vehicle rider vehicle={vehicle?.GetInstanceID()}: could not normalize " +
                    $"drivetrain on mount; using fallback recovery: " +
                    exception.GetBaseException().Message);
            }
        }

        private void LateUpdate()
        {
            if (vehicle == null || !occupied)
            {
                enabled = false;
                return;
            }

            if (!vehicle.controlledByPlayer && Time.unscaledTime >= mountControlGraceUntil)
            {
                occupied = false;
                GetComponent<MootorVehicleAmbientMooController>()?.NotifyDismounted();
                GetComponent<MootorVehicleFuelController>()?.NotifyDismounted();
                ApplyRideHeight(false);
                ResetCowGait();
                ResetCowEarFlap();
                mooHornSource?.Stop();
                RemoveRider();
                enabled = false;
                LogInfo("dismounted; visible rider removed.");
                return;
            }

            UpdateEngineStart();
            UpdateMooHorn();
            UpdateCowGait();
            UpdateCowEarFlap();

            try
            {
                if (riderRoot == null)
                {
                    if (attempts >= MaximumAttempts || Time.unscaledTime < nextAttempt)
                        return;

                    attempts++;
                    nextAttempt = Time.unscaledTime + 0.5f;
                    CreateRider();
                }

                poseTime = Mathf.Repeat(poseTime + Time.deltaTime, poseLength);
                pose.SetTime(poseTime);
                leftArm?.RestoreAnimationPose();
                rightArm?.RestoreAnimationPose();
                leftLeg?.RestoreAnimationPose();
                rightLeg?.RestoreAnimationPose();
                poseGraph.Evaluate(0f);
                AlignWithSeat();
                ApplyCowRidingPose();
            }
            catch (Exception exception)
            {
                RemoveRider();
                var reason = exception.GetBaseException().Message;
                if (reason != lastFailure || attempts >= MaximumAttempts)
                {
                    lastFailure = reason;
                    context?.Logger.Warn(
                        $"Moo-tor Vehicle rider vehicle={vehicle.GetInstanceID()} " +
                        $"attempt={attempts}/{MaximumAttempts}: {reason}");
                }
            }
        }

        private void CreateRider()
        {
            var character = PlayerHelper.PlayerController?.Character;
            var appearance = character?.appearanceSetter;
            if (character == null || appearance == null)
                throw new InvalidOperationException("Player appearance is not ready.");

            var sourceAnimator = typeof(AppearanceSetter).GetField(
                "animator",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(appearance) as Animator;
            if (sourceAnimator == null ||
                sourceAnimator.avatar == null ||
                !sourceAnimator.avatar.isHuman ||
                sourceAnimator.runtimeAnimatorController == null)
            {
                throw new InvalidOperationException("Player humanoid animator/avatar is not ready.");
            }

            AnimationClip? sittingClip = null;
            foreach (var clip in sourceAnimator.runtimeAnimatorController.animationClips)
                if (clip != null && string.Equals(clip.name, SittingClipName, StringComparison.Ordinal))
                    sittingClip = clip;

            if (sittingClip == null || sittingClip.length <= 0f)
                throw new InvalidOperationException($"Native seated animation '{SittingClipName}' is unavailable.");

            riderSeat = null;
            foreach (var child in vehicle!.GetComponentsInChildren<Transform>(true))
                if (string.Equals(child.name, RiderSeatName, StringComparison.Ordinal))
                    riderSeat = child;

            if (riderSeat == null)
                throw new InvalidOperationException("Cow rider-seat reference is missing.");

            var sourceRoot = character.transform;
            if (!sourceAnimator.transform.IsChildOf(sourceRoot) && sourceAnimator.transform != sourceRoot)
                throw new InvalidOperationException("Player animator is outside the character hierarchy.");

            riderRoot = new GameObject("MootorVehicle_VisibleRider");
            riderRoot.SetActive(false);
            riderRoot.layer = sourceRoot.gameObject.layer;
            riderRoot.transform.SetParent(vehicle.transform, false);

            var transforms = new Dictionary<Transform, Transform>();
            CopyTransforms(sourceRoot, riderRoot.transform, transforms);
            riderRoot.transform.localScale = Vector3.one * RiderScale;
            riderRoot.transform.localRotation = Quaternion.identity;
            riderRoot.transform.localPosition = Vector3.zero;

            var rendererCount = 0;
            var lowerDetailRenderers = GetLowerDetailRenderers(appearance.transform);
            foreach (var source in appearance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!source.enabled || source.sharedMesh == null || !IsActiveWithinCharacter(source.transform, sourceRoot))
                    continue;

                if (source.forceRenderingOff ||
                    source.shadowCastingMode == ShadowCastingMode.ShadowsOnly ||
                    lowerDetailRenderers.Contains(source))
                {
                    continue;
                }

                CopyRenderer(source, transforms);
                rendererCount++;
            }

            CopyHeldItemRenderers(character.GetHandContent(), sourceRoot, transforms);

            if (rendererCount == 0)
                throw new InvalidOperationException("Player has no visible skinned appearance meshes yet.");

            var animator = transforms[sourceAnimator.transform].gameObject.AddComponent<Animator>();
            animator.avatar = sourceAnimator.avatar;
            animator.applyRootMotion = false;
            animator.fireEvents = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            riderRoot.SetActive(true);
            poseGraph = PlayableGraph.Create("Moo-tor Vehicle visible rider");
            poseGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            pose = AnimationClipPlayable.Create(poseGraph, sittingClip);
            pose.SetApplyFootIK(false);
            pose.SetApplyPlayableIK(false);
            AnimationPlayableOutput.Create(poseGraph, "Cow-riding pose", animator).SetSourcePlayable(pose);
            poseLength = sittingClip.length;
            poseTime = 0f;
            poseGraph.Play();
            poseGraph.Evaluate(0f);

            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null)
                throw new InvalidOperationException("Seated avatar has no humanoid hips bone.");

            AlignWithSeat();
            leftArm = CreateLimb(
                animator,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand);
            rightArm = CreateLimb(
                animator,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand);
            leftLeg = CreateLimb(
                animator,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot);
            rightLeg = CreateLimb(
                animator,
                HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot);

            ApplyCowRidingPose();
            LogInfo(
                $"created visible rider renderers={rendererCount} scale={RiderScale:F2} " +
                $"seatLocal={riderSeat.localPosition} " +
                $"clip='{sittingClip.name}'.");
        }

        private void AlignWithSeat()
        {
            if (riderRoot == null || hips == null || riderSeat == null)
                return;

            riderRoot.transform.rotation = riderSeat.rotation;
            riderRoot.transform.position += riderSeat.position - hips.position;
        }

        private PoseLimb? CreateLimb(
            Animator animator,
            HumanBodyBones upperBone,
            HumanBodyBones lowerBone,
            HumanBodyBones endBone)
        {
            var upper = animator.GetBoneTransform(upperBone);
            var lower = animator.GetBoneTransform(lowerBone);
            var end = animator.GetBoneTransform(endBone);
            if (upper != null && lower != null && end != null)
                return new PoseLimb(upper, lower, end);

            context?.Logger.Warn(
                $"Moo-tor Vehicle rider vehicle={vehicle?.GetInstanceID()}: " +
                $"cannot refine {endBone}; limb bones are missing. Keeping native pose.");
            return null;
        }

        private void ApplyCowRidingPose()
        {
            if (riderSeat == null)
                return;

            leftArm?.AimAt(
                riderSeat.TransformPoint(LeftHandOffset),
                riderSeat.TransformPoint(LeftElbowHintOffset));
            rightArm?.AimAt(
                riderSeat.TransformPoint(RightHandOffset),
                riderSeat.TransformPoint(RightElbowHintOffset));
            leftLeg?.AimAt(
                riderSeat.TransformPoint(LeftFootOffset),
                riderSeat.TransformPoint(LeftKneeHintOffset));
            rightLeg?.AimAt(
                riderSeat.TransformPoint(RightFootOffset),
                riderSeat.TransformPoint(RightKneeHintOffset));
        }

        private void UpdateEngineStart()
        {
            if (physicsVehicle == null || engineReady)
                return;

            try
            {
                var engine = physicsVehicle.powertrain.engine;
                var rpm = engine.RPMPercent * engine.revLimiterRPM;
                var speedKmh = vehicleBody != null ? vehicleBody.velocity.magnitude * 3.6f : 0f;
                var stationaryAtRevLimiter = speedKmh < 0.5f &&
                                             rpm >= engine.revLimiterRPM * 0.95f;
                if (engine.IsRunning && rpm >= MinimumHealthyEngineRpm && !stationaryAtRevLimiter)
                {
                    engineReady = true;
                    engineRestartPending = false;
                    dormantThrottleDetectedAt = -1f;
                    return;
                }

                if (stationaryAtRevLimiter && !engineRestartPending)
                {
                    engine.StopEngine();
                    engineRestartPending = true;
                    nextEngineStartAttempt = Time.unscaledTime + EngineRestartDelay;
                    LogInfo(
                        $"reset stationary rev-limiter drivetrain before restart; " +
                        $"rpm={rpm:F0} speed={speedKmh:F2}kmh.");
                    return;
                }

                if (engineRestartPending)
                {
                    if (Time.unscaledTime < nextEngineStartAttempt)
                        return;

                    engineRestartPending = false;
                    engineStartAttempts++;
                    nextEngineStartAttempt = Time.unscaledTime + EngineStartRetryDelay;
                    dormantThrottleDetectedAt = Time.unscaledTime;
                    engine.StartEngine();
                    physicsVehicle.powertrain.transmission.ShiftInto(1, true);
                    LogInfo(
                        $"requested native engine restart attempt={engineStartAttempts} in drive; " +
                        $"running={engine.IsRunning} rpm={rpm:F0} " +
                        $"gear={physicsVehicle.powertrain.transmission.Gear}.");
                    return;
                }

                if (Mathf.Abs(physicsVehicle.input.Throttle) < 0.05f)
                {
                    dormantThrottleDetectedAt = -1f;
                    return;
                }

                if (dormantThrottleDetectedAt < 0f)
                {
                    dormantThrottleDetectedAt = Time.unscaledTime;
                    return;
                }

                if (Time.unscaledTime < nextEngineStartAttempt ||
                    Time.unscaledTime - dormantThrottleDetectedAt < DormantEngineGracePeriod)
                {
                    return;
                }

                if (engineStartAttempts >= MaximumEngineStartAttempts)
                {
                    if (!engineStartFailureLogged)
                    {
                        engineStartFailureLogged = true;
                        context?.Logger.Warn(
                            $"Moo-tor Vehicle rider vehicle={vehicle?.GetInstanceID()}: native engine " +
                            $"did not start after {engineStartAttempts} requests while mounted.");
                    }
                    return;
                }

                engine.StopEngine();
                engineRestartPending = true;
                nextEngineStartAttempt = Time.unscaledTime + EngineRestartDelay;
                LogInfo(
                    $"reset dormant native engine before restart attempt={engineStartAttempts + 1}; " +
                    $"running={engine.IsRunning} rpm={rpm:F0}.");
            }
            catch (Exception exception)
            {
                if (engineStartFailureLogged)
                    return;
                engineStartFailureLogged = true;
                context?.Logger.Warn(
                    $"Moo-tor Vehicle rider vehicle={vehicle?.GetInstanceID()}: native engine start failed: " +
                    $"{exception.GetBaseException().Message}");
            }
        }

        private void ConfigureMooHorn()
        {
            if (physicsVehicle == null)
                return;

            var horn = physicsVehicle.soundManager.hornComponent;
            if (mooHornSource != null)
                return;

            try
            {
                if (horn.clips == null || horn.clips.Count == 0 || horn.clips[0] == null)
                    throw new InvalidOperationException("bundled Moo audio clip is missing");

                var hornHost = transform.Find("MootorVehicle_MooHorn");
                if (hornHost == null)
                {
                    var hornObject = new GameObject("MootorVehicle_MooHorn");
                    hornObject.transform.SetParent(transform, false);
                    hornHost = hornObject.transform;
                }

                mooHornSource = hornHost.GetComponent<AudioSource>() ??
                    hornHost.gameObject.AddComponent<AudioSource>();
                mooHornSource.playOnAwake = false;
                mooHornSource.loop = false;
                mooHornSource.clip = horn.clips[0];
                mooHornSource.volume = Mathf.Clamp01(horn.baseVolume);
                mooHornSource.spatialBlend = 1f;
                mooHornSource.rolloffMode = AudioRolloffMode.Logarithmic;
                mooHornSource.minDistance = 1f;
                mooHornSource.maxDistance = 35f;
                mooHornSource.dopplerLevel = 0f;
                mooHornSource.outputAudioMixerGroup = physicsVehicle.soundManager.otherMixerGroup;
                mooHornSource.clip.LoadAudioData();

            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle rider vehicle={vehicle?.GetInstanceID()}: " +
                    $"could not configure native moo horn source: " +
                    $"{exception.GetBaseException().Message}");
            }
        }

        private void UpdateMooHorn()
        {
            if (physicsVehicle == null || mooHornSource == null || mooHornSource.clip == null)
                return;

            var pressed = physicsVehicle.input.Horn;
            if (pressed && !hornPressed && Time.timeScale > 0f && !AudioListener.pause)
            {
                mooHornSource.Stop();
                mooHornSource.volume = Mathf.Clamp01(
                    physicsVehicle.soundManager.masterVolume * MooHornVolume);
                mooHornSource.Play();

            }

            hornPressed = pressed;
        }

        private void ConfigureRideHeight()
        {
            if (rideHeightConfigured || vehicle == null)
                return;

            foreach (var child in vehicle.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(child.name, CowVisualName, StringComparison.Ordinal))
                    cowVisual = child;
                else if (string.Equals(child.name, RiderSeatName, StringComparison.Ordinal))
                    heightAdjustedSeat = child;
            }

            if (cowVisual == null || heightAdjustedSeat == null)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle rider vehicle={vehicle.GetInstanceID()}: " +
                    "could not configure state-specific ride height; cow visual or rider seat is missing.");
                return;
            }

            cowVisualBasePosition = cowVisual.localPosition;
            riderSeatBasePosition = heightAdjustedSeat.localPosition;
            cowGaitRenderer = cowVisual.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (cowGaitRenderer?.sharedMesh != null)
            {
                gaitPoseAIndex = cowGaitRenderer.sharedMesh.GetBlendShapeIndex(GaitPoseAName);
                gaitPoseBIndex = cowGaitRenderer.sharedMesh.GetBlendShapeIndex(GaitPoseBName);
                leftEarFlapIndex = cowGaitRenderer.sharedMesh.GetBlendShapeIndex(LeftEarFlapName);
                rightEarFlapIndex = cowGaitRenderer.sharedMesh.GetBlendShapeIndex(RightEarFlapName);
            }

            if (gaitPoseAIndex < 0 || gaitPoseBIndex < 0)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle rider vehicle={vehicle.GetInstanceID()}: " +
                    "cow gait blend shapes are missing; movement animation is disabled.");
                cowGaitRenderer = null;
            }
            else
            {
                LogInfo(
                    $"cow gait configured renderer='{cowGaitRenderer!.name}' " +
                    $"poses=({gaitPoseAIndex},{gaitPoseBIndex}).");
                if (leftEarFlapIndex < 0 || rightEarFlapIndex < 0)
                {
                    context?.Logger.Warn(
                        $"Moo-tor Vehicle rider vehicle={vehicle.GetInstanceID()}: " +
                        "cow ear blend shapes are missing; ear flaps are disabled.");
                }
                else
                {
                    LogInfo($"cow ear flaps configured poses=({leftEarFlapIndex},{rightEarFlapIndex}).");
                }
            }

            rideHeightConfigured = true;
        }

        private void ApplyRideHeight(bool mounted)
        {
            if (!rideHeightConfigured || cowVisual == null || heightAdjustedSeat == null)
                return;

            var heightOffset = mounted
                ? MountedVisualHeightOffset
                : hasBeenMounted
                    ? PostRideParkedVisualHeightOffset
                    : InitialParkedVisualHeightOffset;
            cowVisual.localPosition = cowVisualBasePosition + Vector3.up * heightOffset;
            heightAdjustedSeat.localPosition = riderSeatBasePosition + Vector3.up * heightOffset;
            LogInfo(
                $"ride height state='{(mounted ? "mounted" : hasBeenMounted ? "parked-after-ride" : "initial-parked")}' " +
                $"offset={heightOffset:F2}m.");
        }

        private void UpdateCowGait()
        {
            if (cowGaitRenderer == null || vehicleBody == null)
                return;

            var localVelocity = transform.InverseTransformDirection(vehicleBody.velocity);
            var speed = new Vector2(localVelocity.x, localVelocity.z).magnitude;
            var targetBlend = Mathf.InverseLerp(GaitStartSpeed, GaitFullSpeed, speed);
            var wasAnimating = gaitBlend > 0.001f;
            gaitBlend = Mathf.MoveTowards(gaitBlend, targetBlend, Time.deltaTime * 4f);
            if (gaitBlend <= 0.001f)
            {
                if (wasAnimating)
                    ResetCowGait();
                return;
            }

            var regularCadence = Mathf.Lerp(
                MinimumStridesPerSecond,
                RegularMaximumStridesPerSecond,
                Mathf.Clamp01(speed / 4f));
            var regularTopSpeed = MootorVehicleFuelController.RegularSpeedLimit / 3.6f;
            var energizedTopSpeed = regularTopSpeed * 2f;
            var energizedSpeedFactor = Mathf.InverseLerp(
                regularTopSpeed,
                energizedTopSpeed,
                speed);
            var stridesPerSecond = Mathf.Lerp(
                regularCadence,
                EnergizedMaximumStridesPerSecond,
                energizedSpeedFactor);
            gaitPhase = Mathf.Repeat(
                gaitPhase + stridesPerSecond * Mathf.PI * 2f * Time.deltaTime,
                Mathf.PI * 2f);
            var gait = Mathf.Sin(gaitPhase) * gaitBlend * 100f;
            cowGaitRenderer.SetBlendShapeWeight(gaitPoseAIndex, Mathf.Max(0f, gait));
            cowGaitRenderer.SetBlendShapeWeight(gaitPoseBIndex, Mathf.Max(0f, -gait));
        }

        private void ResetCowGait()
        {
            if (cowGaitRenderer != null)
            {
                if (gaitPoseAIndex >= 0)
                    cowGaitRenderer.SetBlendShapeWeight(gaitPoseAIndex, 0f);
                if (gaitPoseBIndex >= 0)
                    cowGaitRenderer.SetBlendShapeWeight(gaitPoseBIndex, 0f);
            }

            gaitBlend = 0f;
        }

        private void UpdateCowEarFlap()
        {
            if (cowGaitRenderer == null || leftEarFlapIndex < 0 || rightEarFlapIndex < 0)
                return;

            if (!earFlapActive)
            {
                if (Time.time < nextEarFlapAt)
                    return;

                var choice = UnityEngine.Random.value;
                activeEarMask = choice < 0.4f ? 1 : choice < 0.8f ? 2 : 3;
                earFlapStartedAt = Time.time;
                earFlapActive = true;
            }

            var progress = Mathf.Clamp01((Time.time - earFlapStartedAt) / EarFlapDuration);
            var weight = Mathf.Sin(progress * Mathf.PI);
            weight *= weight * 100f;
            cowGaitRenderer.SetBlendShapeWeight(
                leftEarFlapIndex,
                (activeEarMask & 1) != 0 ? weight : 0f);
            cowGaitRenderer.SetBlendShapeWeight(
                rightEarFlapIndex,
                (activeEarMask & 2) != 0 ? weight : 0f);

            if (progress < 1f)
                return;

            earFlapActive = false;
            activeEarMask = 0;
            ScheduleNextEarFlap(false);
        }

        private void ScheduleNextEarFlap(bool initial)
        {
            var minimumDelay = initial ? 1.5f : EarFlapMinimumDelay;
            var maximumDelay = initial ? 4f : EarFlapMaximumDelay;
            nextEarFlapAt = Time.time + UnityEngine.Random.Range(minimumDelay, maximumDelay);
        }

        private void ResetCowEarFlap()
        {
            if (cowGaitRenderer != null)
            {
                if (leftEarFlapIndex >= 0)
                    cowGaitRenderer.SetBlendShapeWeight(leftEarFlapIndex, 0f);
                if (rightEarFlapIndex >= 0)
                    cowGaitRenderer.SetBlendShapeWeight(rightEarFlapIndex, 0f);
            }

            earFlapActive = false;
            activeEarMask = 0;
            nextEarFlapAt = 0f;
        }

        private static bool IsActiveWithinCharacter(Transform child, Transform root)
        {
            for (var current = child; current != root; current = current.parent)
            {
                if (current == null || !current.gameObject.activeSelf)
                    return false;
            }

            return true;
        }

        private sealed class PoseLimb
        {
            private readonly Transform upper;
            public readonly Transform Middle;
            public readonly Transform End;
            private Quaternion upperPose;
            private Quaternion middlePose;
            private Quaternion endPose;
            private bool hasAdjustment;

            public PoseLimb(Transform upper, Transform middle, Transform end)
            {
                this.upper = upper;
                Middle = middle;
                End = end;
            }

            public void RestoreAnimationPose()
            {
                if (!hasAdjustment)
                    return;

                upper.localRotation = upperPose;
                Middle.localRotation = middlePose;
                End.localRotation = endPose;
                hasAdjustment = false;
            }

            public void AimAt(Vector3 target, Vector3 bendHint)
            {
                if (!TrySolveJoint(
                        upper.position,
                        Middle.position,
                        End.position,
                        target,
                        bendHint,
                        out var solvedMiddle,
                        out var reachableTarget))
                {
                    return;
                }

                upperPose = upper.localRotation;
                middlePose = Middle.localRotation;
                endPose = End.localRotation;
                hasAdjustment = true;
                var endRotation = End.rotation;
                upper.rotation = Quaternion.FromToRotation(
                    Middle.position - upper.position,
                    solvedMiddle - upper.position) * upper.rotation;
                Middle.rotation = Quaternion.FromToRotation(
                    End.position - Middle.position,
                    reachableTarget - Middle.position) * Middle.rotation;
                End.rotation = endRotation;
            }
        }

        private static bool TrySolveJoint(
            Vector3 upper,
            Vector3 middle,
            Vector3 end,
            Vector3 target,
            Vector3 bendHint,
            out Vector3 solvedMiddle,
            out Vector3 reachableTarget)
        {
            solvedMiddle = middle;
            reachableTarget = end;
            var upperLength = Vector3.Distance(upper, middle);
            var lowerLength = Vector3.Distance(middle, end);
            var reach = target - upper;
            if (upperLength < 0.0001f || lowerLength < 0.0001f || reach.sqrMagnitude < 0.000001f)
                return false;

            var direction = reach.normalized;
            var distance = Mathf.Clamp(
                reach.magnitude,
                Mathf.Abs(upperLength - lowerLength) + 0.0001f,
                upperLength + lowerLength - 0.0001f);
            var bend = Vector3.ProjectOnPlane(bendHint - upper, direction);
            if (bend.sqrMagnitude < 0.000001f)
                bend = Vector3.ProjectOnPlane(middle - upper, direction);
            if (bend.sqrMagnitude < 0.000001f)
                return false;

            var along =
                (upperLength * upperLength + distance * distance - lowerLength * lowerLength) /
                (2f * distance);
            var across = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            solvedMiddle = upper + direction * along + bend.normalized * across;
            reachableTarget = upper + direction * distance;
            return true;
        }

        private static HashSet<Renderer> GetLowerDetailRenderers(Transform root)
        {
            var result = new HashSet<Renderer>();
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

        private static void CopyTransforms(
            Transform source,
            Transform destination,
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

        private void CopyRenderer(
            SkinnedMeshRenderer source,
            Dictionary<Transform, Transform> transforms)
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
                    throw new InvalidOperationException(
                        $"Appearance mesh '{source.name}' has an unmapped bone at {index}.");
            }

            destination.bones = bones;
            if (source.rootBone != null)
            {
                if (!transforms.TryGetValue(source.rootBone, out var rootBone))
                    throw new InvalidOperationException(
                        $"Appearance mesh '{source.name}' has an unmapped root bone.");
                destination.rootBone = rootBone;
            }

            var sourceMaterials = source.sharedMaterials;
            var materials = new Material[sourceMaterials.Length];
            for (var index = 0; index < sourceMaterials.Length; index++)
            {
                if (sourceMaterials[index] == null)
                    throw new InvalidOperationException(
                        $"Appearance mesh '{source.name}' has a missing material.");

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
        }

        private int CopyHeldItemRenderers(
            Transform? handContent,
            Transform sourceRoot,
            Dictionary<Transform, Transform> transforms)
        {
            if (handContent == null || PlayerHelper.ItemInstanceInHands == null)
                return 0;

            var copied = 0;
            foreach (var source in handContent.GetComponentsInChildren<Renderer>(true))
            {
                if (!source.enabled || source.forceRenderingOff ||
                    source.shadowCastingMode == ShadowCastingMode.ShadowsOnly || source.transform == null ||
                    !IsActiveWithinCharacter(source.transform, sourceRoot) ||
                    !transforms.TryGetValue(source.transform, out var destinationTransform) ||
                    destinationTransform.GetComponent<Renderer>() != null)
                {
                    continue;
                }

                if (source is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                {
                    CopyRenderer(skinned, transforms);
                    copied++;
                    continue;
                }

                if (source is not MeshRenderer meshRenderer)
                    continue;

                var sourceFilter = source.GetComponent<MeshFilter>();
                if (sourceFilter?.sharedMesh == null)
                    continue;

                var destinationFilter = destinationTransform.gameObject.AddComponent<MeshFilter>();
                destinationFilter.sharedMesh = sourceFilter.sharedMesh;
                var destination = destinationTransform.gameObject.AddComponent<MeshRenderer>();
                destination.sharedMaterials = meshRenderer.sharedMaterials;
                destination.renderingLayerMask = meshRenderer.renderingLayerMask;
                destination.shadowCastingMode = meshRenderer.shadowCastingMode;
                destination.receiveShadows = meshRenderer.receiveShadows;

                var properties = new MaterialPropertyBlock();
                meshRenderer.GetPropertyBlock(properties);
                destination.SetPropertyBlock(properties);
                for (var index = 0; index < meshRenderer.sharedMaterials.Length; index++)
                {
                    properties.Clear();
                    meshRenderer.GetPropertyBlock(properties, index);
                    if (!properties.isEmpty)
                        destination.SetPropertyBlock(properties, index);
                }

                copied++;
            }

            if (copied > 0)
                LogInfo(
                    $"copied carried-item visuals item='{PlayerHelper.ItemInstanceInHands.itemName}' " +
                    $"renderers={copied}.");
            else
                context?.Logger.Warn(
                    $"Moo-tor Vehicle rider vehicle={vehicle?.GetInstanceID()}: carried item " +
                    $"'{PlayerHelper.ItemInstanceInHands.itemName}' has no copyable hand-content renderers.");

            return copied;
        }

        [System.Diagnostics.Conditional("MOOTORVEHICLE_DIAGNOSTICS")]
        private void LogInfo(string message)
        {
            MootorVehicleDiagnostics.Info(
                context,
                $"Moo-tor Vehicle rider vehicle={vehicle?.GetInstanceID()}: {message}");
        }

        private void RemoveRider()
        {
            if (poseGraph.IsValid())
                poseGraph.Destroy();

            if (riderRoot != null)
            {
                riderRoot.SetActive(false);
                Destroy(riderRoot);
            }

            riderRoot = null;
            hips = null;
            riderSeat = null;
            leftArm = null;
            rightArm = null;
            leftLeg = null;
            rightLeg = null;

            foreach (var asset in ownedAssets)
                if (asset != null)
                    Destroy(asset);

            ownedAssets.Clear();
        }

        private void OnDisable()
        {
            GetComponent<MootorVehicleFuelController>()?.NotifyDismounted();
            mooHornSource?.Stop();
            ResetCowGait();
            ResetCowEarFlap();
            RemoveRider();
            occupied = false;
        }

        private void OnDestroy()
        {
            RemoveRider();
        }
    }
}
