#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;
using UnityEngine.AI;
using PhysicsDamageHandler = NWH.VehiclePhysics2.Damage.DamageHandler;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

namespace MootorVehicle
{
    /// <summary>
    /// Handles cow impacts through Unity collision callbacks. There is no Update or FixedUpdate loop.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    internal sealed class MootorVehicleImpactController : MonoBehaviour
    {
        private const float StrongHorizontalDeltaVelocity = 3f;
        private const float MinimumHorizontalImpactSpeed = 3.5f;
        private const float NormalCrashMooVolume = 0.65f;
        private const float EjectionCrashMooVolume = 0.06f;
        private const string FaintAnimationName = "Faint";
        private const float LyingDuration = 2f;
        private const float GetUpDuration = 1.5f;
        private const float SafeExitDistance = 2.25f;
        private const float SafeExitProbeRadius = 0.75f;
        private const float SafeExitGroundOffset = 0.05f;
        private const NavigationBlocker CrashFallNavigationBlocker = (NavigationBlocker)1000;

        private VehicleController? vehicle;
        private CarController? carController;
        private PhysicsVehicle? physicsVehicle;
        private PhysicsDamageHandler? damageHandler;
        private Rigidbody? vehicleBody;
        private ModContext? context;
        private Coroutine? forcedDismountCoroutine;
        private Coroutine? crashVolumeRestoreCoroutine;
        private bool crashAudioConfigured;
        private bool crashAudioFailureLogged;
        private bool crashMooPlaybackFailureLogged;
        private AudioClip? crashMooClip;
        private AudioSource? crashMooSource;
        private PlayerController? fallenPlayer;
        private Animator? fallenPlayerAnimator;
        private float fallenPlayerAnimatorSpeed = 1f;
        private bool fallNavigationBlocked;

        internal void Initialize(VehicleController controller, ModContext? modContext)
        {
            vehicle = controller;
            carController = controller as CarController ?? controller.GetComponent<CarController>();
            physicsVehicle = controller.GetComponent<PhysicsVehicle>();
            damageHandler = controller.GetComponent<PhysicsDamageHandler>();
            vehicleBody = controller.GetComponent<Rigidbody>();
            context = modContext;
            ConfigureCrashMoo();
        }

        private void ConfigureCrashMoo()
        {
            if (crashAudioConfigured || physicsVehicle == null)
                return;

            try
            {
                var horn = physicsVehicle.soundManager.hornComponent;
                var crash = physicsVehicle.soundManager.crashComponent;
                if (horn?.clips == null || horn.clips.Count == 0 || horn.clips[0] == null)
                    throw new InvalidOperationException("bundled moo clip is unavailable");
                if (crash == null)
                    throw new InvalidOperationException("native crash sound component is unavailable");

                var mooClip = horn.clips[0];
                crashMooClip = mooClip;
                crash.clips ??= new List<AudioClip>();
                crash.clips.Clear();
                crash.clips.Add(mooClip);
                crash.baseVolume = NormalCrashMooVolume;
                mooClip.LoadAudioData();

                var sourceObject = new GameObject("MootorVehicle_StrongImpactMoo");
                sourceObject.transform.SetParent(transform, false);
                crashMooSource = sourceObject.AddComponent<AudioSource>();
                crashMooSource.playOnAwake = false;
                crashMooSource.loop = false;
                crashMooSource.clip = mooClip;
                crashMooSource.volume = EjectionCrashMooVolume;
                crashMooSource.spatialBlend = 1f;
                crashMooSource.rolloffMode = AudioRolloffMode.Linear;
                crashMooSource.minDistance = 3f;
                crashMooSource.maxDistance = 35f;
                crashAudioConfigured = true;

                MootorVehicleDiagnostics.Info(
                    context,
                    $"Moo-tor Vehicle impact audio vehicle={vehicle?.GetInstanceID()} configured " +
                    $"clip='{mooClip.name}' normalVolume={NormalCrashMooVolume:F2} " +
                    $"ejectionVolume={EjectionCrashMooVolume:F2}.");
            }
            catch (Exception exception)
            {
                if (crashAudioFailureLogged)
                    return;

                crashAudioFailureLogged = true;
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact audio vehicle={vehicle?.GetInstanceID()} could not replace " +
                    $"the native crash sound: {exception.GetBaseException().Message}");
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null || vehicle == null || vehicleBody == null ||
                !vehicle.controlledByPlayer || forcedDismountCoroutine != null)
            {
                return;
            }

            var horizontalImpulse = Vector3.ProjectOnPlane(collision.impulse, Vector3.up).magnitude;
            var horizontalDeltaVelocity = horizontalImpulse / Mathf.Max(vehicleBody.mass, 1f);
            var horizontalImpactSpeed = Vector3.ProjectOnPlane(collision.relativeVelocity, Vector3.up).magnitude;
            if (horizontalDeltaVelocity < StrongHorizontalDeltaVelocity ||
                horizontalImpactSpeed < MinimumHorizontalImpactSpeed)
            {
                return;
            }

            var damageBeforeImpact = GetRuntimeDamage();
            var ejectionDamage = CalculateNativeEquivalentDamage(collision);
            var collisionObject = collision.gameObject;
            var collisionObjectName = collisionObject != null ? collisionObject.name : "<unknown>";
            var collisionLayerName = collisionObject != null
                ? LayerMask.LayerToName(collisionObject.layer)
                : "<unknown>";

            MootorVehicleDiagnostics.Info(
                context,
                $"Moo-tor Vehicle strong impact vehicle={vehicle.GetInstanceID()} " +
                $"horizontalDeltaV={horizontalDeltaVelocity:F2}m/s " +
                $"relativeSpeed={horizontalImpactSpeed:F2}m/s damageBefore={damageBeforeImpact:F4} " +
                $"expectedDamage={ejectionDamage:F4} collision='{collisionObjectName}' " +
                $"layer='{collisionLayerName}'; forcing rider dismount.");
            SuppressNativeCrashMooForEjection();
            PlayStrongImpactMoo();
            forcedDismountCoroutine = StartCoroutine(ForceDismountAfterCollision(
                damageBeforeImpact,
                ejectionDamage,
                collisionObjectName,
                collisionLayerName));
        }

        private float GetRuntimeDamage()
        {
            var nativeDamage = damageHandler?.Damage ?? 0f;
            var savedDamage = vehicle?.vehicleInstance?.damage ?? 0f;
            return Mathf.Max(nativeDamage, savedDamage);
        }

        private float CalculateNativeEquivalentDamage(Collision collision)
        {
            if (vehicleBody == null)
                return 0f;

            var fixedDeltaTime = Mathf.Max(Time.fixedDeltaTime, 0.001f);
            var mass = Mathf.Max(vehicleBody.mass, 1f);
            var damageIntensity = damageHandler != null
                ? damageHandler.damageIntensity
                : vehicle?.vehicleType?.damageIntensity ?? 0.2f;
            damageIntensity = Mathf.Clamp(damageIntensity, 0f, 0.99f);

            // This mirrors NWH DamageHandler's collision damage formula. Its built-in
            // layer filter rejects roads and ground, but those impacts can still be
            // strong enough to throw the rider from an exposed animal mount.
            return collision.impulse.magnitude /
                   (fixedDeltaTime * mass * 10f) *
                   damageIntensity *
                   0.005f;
        }

        private void SuppressNativeCrashMooForEjection()
        {
            var crash = physicsVehicle?.soundManager?.crashComponent;
            if (crash == null)
                return;

            crash.baseVolume = 0f;
            if (crashVolumeRestoreCoroutine != null)
                StopCoroutine(crashVolumeRestoreCoroutine);
            crashVolumeRestoreCoroutine = StartCoroutine(RestoreNativeCrashMooVolume());
        }

        private IEnumerator RestoreNativeCrashMooVolume()
        {
            // Keep the native component silent through its collision callback and immediate
            // scheduling window. Ordinary later impacts return to the established volume.
            yield return new WaitForSecondsRealtime(0.15f);
            crashVolumeRestoreCoroutine = null;
            var crash = physicsVehicle?.soundManager?.crashComponent;
            if (crash != null)
                crash.baseVolume = NormalCrashMooVolume;
        }

        private void PlayStrongImpactMoo()
        {
            try
            {
                if (crashMooSource == null || crashMooClip == null)
                    throw new InvalidOperationException("dedicated strong-impact moo source is unavailable");

                crashMooSource.Stop();
                crashMooSource.PlayOneShot(crashMooClip);
                MootorVehicleDiagnostics.Info(
                    context,
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} played explicit " +
                    $"strong-impact moo clip='{crashMooClip.name}'.");
            }
            catch (Exception exception)
            {
                if (crashMooPlaybackFailureLogged)
                    return;

                crashMooPlaybackFailureLogged = true;
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} could not play " +
                    $"the strong-impact moo: {exception.GetBaseException().Message}");
            }
        }

        private IEnumerator ForceDismountAfterCollision(
            float damageBeforeImpact,
            float ejectionDamage,
            string collisionObjectName,
            string collisionLayerName)
        {
            // Let the physics step and native crash handlers finish before running the standard
            // vehicle-exit path. This preserves the game's normal player and held-item cleanup.
            yield return null;

            EnsureEjectionDamage(
                damageBeforeImpact,
                ejectionDamage,
                collisionObjectName,
                collisionLayerName);

            var riderExited = false;
            try
            {
                if (vehicle != null && vehicle.controlledByPlayer)
                {
                    var engine = physicsVehicle?.powertrain?.engine;
                    if (engine != null)
                    {
                        engine.StopEngine();
                        MootorVehicleDiagnostics.Info(
                            context,
                            $"Moo-tor Vehicle impact vehicle={vehicle.GetInstanceID()} reset drivetrain " +
                            "before forced exit.");
                    }

                    var transmission = physicsVehicle?.powertrain?.transmission;
                    if (transmission != null)
                    {
                        transmission.ShiftInto(0, true);
                        transmission.currentGearRatio = 0f;
                    }

                    vehicle.ExitVehicle();
                    riderExited = true;
                }
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} could not dismount rider: " +
                    exception.GetBaseException().Message);
            }

            try
            {
                if (riderExited)
                {
                    // ExitVehicle can leave the player inside the cow's carved NavMesh hole when
                    // a crash happens beside another obstacle. Give the game's exit setup one
                    // frame, then move the on-foot player to a nearby valid point before falling.
                    yield return null;
                    PlaceRiderAtSafeCrashExit();
                    yield return PlayPlayerFallAndRecovery();
                }
            }
            finally
            {
                forcedDismountCoroutine = null;
            }
        }

        private void EnsureEjectionDamage(
            float damageBeforeImpact,
            float ejectionDamage,
            string collisionObjectName,
            string collisionLayerName)
        {
            var damageAfterNativeHandlers = GetRuntimeDamage();
            if (damageAfterNativeHandlers > damageBeforeImpact + 0.0001f)
            {
                MootorVehicleDiagnostics.Info(
                    context,
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} retained native " +
                    $"collision damage before={damageBeforeImpact:F4} " +
                    $"after={damageAfterNativeHandlers:F4}.");
                return;
            }

            var targetDamage = Mathf.Clamp01(damageBeforeImpact + ejectionDamage);
            if (targetDamage <= damageAfterNativeHandlers + 0.0001f || vehicle?.vehicleInstance == null)
                return;

            if (carController != null)
            {
                carController.SetDamage(targetDamage);
            }
            else
            {
                vehicle.vehicleInstance.damage = targetDamage;
                damageHandler?.SetDamage(targetDamage);
            }

            SaveGameManager.MarkChange();
            GlobalEvents.onVehicleVariablesChanged?.Invoke();
            MootorVehicleDiagnostics.Info(
                context,
                $"Moo-tor Vehicle impact vehicle={vehicle.GetInstanceID()} applied ejection " +
                $"damage before={damageBeforeImpact:F4} after={targetDamage:F4} " +
                $"collision='{collisionObjectName}' layer='{collisionLayerName}'.");
        }

        private void PlaceRiderAtSafeCrashExit()
        {
            var player = PlayerHelper.PlayerController;
            if (player == null || vehicle == null)
                return;

            var root = player.transform;
            var vehiclePosition = vehicle.transform.position;
            var right = Vector3.ProjectOnPlane(vehicle.transform.right, Vector3.up).normalized;
            var forward = Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up).normalized;
            var currentDirection = Vector3.ProjectOnPlane(root.position - vehiclePosition, Vector3.up);
            if (currentDirection.sqrMagnitude < 0.01f)
                currentDirection = right;
            else
                currentDirection.Normalize();

            var directions = new[]
            {
                currentDirection,
                right,
                -right,
                forward,
                -forward,
                (right + forward).normalized,
                (right - forward).normalized,
                (-right + forward).normalized,
                (-right - forward).normalized
            };

            var found = false;
            var safePosition = root.position;
            var bestDistance = float.PositiveInfinity;
            foreach (var direction in directions)
            {
                var requested = vehiclePosition + direction * SafeExitDistance;
                requested.y = root.position.y;
                if (!NavMesh.SamplePosition(
                        requested,
                        out var hit,
                        SafeExitProbeRadius,
                        NavMesh.AllAreas))
                {
                    continue;
                }

                var distance = (hit.position - root.position).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                found = true;
                bestDistance = distance;
                safePosition = hit.position + Vector3.up * SafeExitGroundOffset;
            }

            if (!found)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact vehicle={vehicle.GetInstanceID()} could not find " +
                    $"a safe NavMesh crash exit near={root.position:F3}.");
                return;
            }

            var previousPosition = root.position;
            var characterControllers = root.GetComponentsInChildren<CharacterController>(true);
            var controllerStates = Array.ConvertAll(
                characterControllers,
                controller => controller != null && controller.enabled);
            var agents = root.GetComponentsInChildren<NavMeshAgent>(true);

            try
            {
                foreach (var controller in characterControllers)
                    if (controller != null)
                        controller.enabled = false;
                foreach (var agent in agents)
                    if (agent != null)
                        agent.enabled = false;

                root.position = safePosition;
                Physics.SyncTransforms();
            }
            finally
            {
                foreach (var agent in agents)
                {
                    if (agent == null)
                        continue;

                    // The rider is definitively on foot now. Re-enable even if the game's failed
                    // exit setup left the agent disabled, then anchor it to the sampled point.
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

            MootorVehicleDiagnostics.Info(
                context,
                $"Moo-tor Vehicle impact vehicle={vehicle.GetInstanceID()} placed rider on safe " +
                $"NavMesh crash exit from={previousPosition:F3} to={safePosition:F3} agents={agents.Length}.");
        }

        private IEnumerator PlayPlayerFallAndRecovery()
        {
            fallenPlayer = PlayerHelper.PlayerController;
            var character = fallenPlayer?.Character;
            var appearance = character?.appearanceSetter;
            fallenPlayerAnimator = appearance == null
                ? null
                : typeof(AppearanceSetter).GetField(
                        "animator",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(appearance) as Animator;

            if (fallenPlayer == null || character == null || fallenPlayerAnimator == null ||
                fallenPlayerAnimator.runtimeAnimatorController == null)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} dismounted the rider, " +
                    "but the native player animator was unavailable for the fall animation.");
                ClearFallState(false);
                yield break;
            }

            AnimationClip? faintClip = null;
            foreach (var clip in fallenPlayerAnimator.runtimeAnimatorController.animationClips)
                if (clip != null && string.Equals(clip.name, FaintAnimationName, StringComparison.Ordinal))
                {
                    faintClip = clip;
                    break;
                }

            var faintLayer = -1;
            var faintState = 0;
            for (var layer = 0; layer < fallenPlayerAnimator.layerCount; layer++)
            {
                var fullPath = Animator.StringToHash(
                    $"{fallenPlayerAnimator.GetLayerName(layer)}.{FaintAnimationName}");
                if (fallenPlayerAnimator.HasState(layer, fullPath))
                {
                    faintLayer = layer;
                    faintState = fullPath;
                    break;
                }

                var shortName = Animator.StringToHash(FaintAnimationName);
                if (fallenPlayerAnimator.HasState(layer, shortName))
                {
                    faintLayer = layer;
                    faintState = shortName;
                    break;
                }
            }

            if (faintClip == null || faintClip.length <= 0f ||
                faintLayer < 0)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} dismounted the rider, " +
                    $"but native animation '{FaintAnimationName}' was unavailable.");
                ClearFallState(false);
                yield break;
            }

            try
            {
                fallenPlayer.RemoveGoal();
                fallenPlayer.SetNavigationBlocker(CrashFallNavigationBlocker);
                fallNavigationBlocked = true;
                fallenPlayerAnimatorSpeed = fallenPlayerAnimator.speed;
                fallenPlayerAnimator.speed = 0f;
                fallenPlayerAnimator.Play(faintState, faintLayer, 1f);
                fallenPlayerAnimator.Update(0f);

                MootorVehicleDiagnostics.Info(
                    context,
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} positioned rider " +
                    $"prone with native clip='{faintClip.name}' layer={faintLayer} " +
                    $"length={faintClip.length:F2}s.");

                var elapsed = 0f;
                while (elapsed < LyingDuration && fallenPlayerAnimator != null)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                elapsed = 0f;
                while (elapsed < GetUpDuration && fallenPlayerAnimator != null)
                {
                    elapsed += Time.deltaTime;
                    var normalizedTime = 1f - Mathf.Clamp01(elapsed / GetUpDuration);
                    fallenPlayerAnimator.Play(faintState, faintLayer, normalizedTime);
                    yield return null;
                }

                MootorVehicleDiagnostics.Info(
                    context,
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} completed rider get-up.");
            }
            finally
            {
                ClearFallState(true);
            }
        }

        private void ClearFallState(bool resetAnimation)
        {
            try
            {
                if (fallenPlayerAnimator != null)
                {
                    fallenPlayerAnimator.speed = fallenPlayerAnimatorSpeed;
                    if (resetAnimation && fallenPlayerAnimator.isActiveAndEnabled)
                    {
                        // Faint intentionally has no native exit transition. Rebinding after the
                        // reversed get-up returns the Base Actions layer to its normal default.
                        fallenPlayerAnimator.Rebind();
                        fallenPlayerAnimator.Update(0f);
                    }
                }

                if (fallenPlayer != null)
                {
                    if (resetAnimation)
                        fallenPlayer.ResetWalkingAnimation();
                    if (fallNavigationBlocked)
                        fallenPlayer.UnsetNavigationBlocker(CrashFallNavigationBlocker);
                }
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} could not fully " +
                    $"restore the rider after a fall: {exception.GetBaseException().Message}");
            }
            finally
            {
                fallenPlayer = null;
                fallenPlayerAnimator = null;
                fallenPlayerAnimatorSpeed = 1f;
                fallNavigationBlocked = false;
            }
        }

        private void OnDisable()
        {
            if (crashVolumeRestoreCoroutine != null)
            {
                StopCoroutine(crashVolumeRestoreCoroutine);
                crashVolumeRestoreCoroutine = null;
            }
            var crash = physicsVehicle?.soundManager?.crashComponent;
            if (crash != null)
                crash.baseVolume = NormalCrashMooVolume;

            if (forcedDismountCoroutine != null)
            {
                StopCoroutine(forcedDismountCoroutine);
                forcedDismountCoroutine = null;
            }

            ClearFallState(true);
        }
    }
}
