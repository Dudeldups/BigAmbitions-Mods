#nullable enable
using System.Collections;
using System.Collections.Generic;
using BAModAPI;
using Helpers;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

namespace MootorVehicle
{
    internal sealed class MootorVehicleAmbientMooController : MonoBehaviour
    {
        private const string AudioHostName = "MootorVehicle_AmbientMoo";
        private const string TriggerHostName = "MootorVehicle_AmbientMooTrigger";
        private const float ProximityRadius = 25f;
        private const float FullVolumeRadius = 10f;
        private const float AmbientVolume = 0.12f;
        private const float InitialDelayMinimum = 2.5f;
        private const float InitialDelayMaximum = 6.5f;
        private const float RepeatDelayMinimum = 9f;
        private const float RepeatDelayMaximum = 18f;

        private readonly HashSet<int> playerColliderIds = new();
        private readonly Collider[] dismountOverlapBuffer = new Collider[64];
        private PhysicsVehicle? physicsVehicle;
        private ModContext? context;
        private GameObject? audioHost;
        private GameObject? triggerHost;
        private AudioSource? audioSource;
        private SphereCollider? proximityTrigger;
        private Coroutine? mooCoroutine;
        private Coroutine? dismountRearmCoroutine;
        private bool initialized;
        private bool mounted;

        internal void Initialize(VehicleController controller, ModContext? modContext)
        {
            if (initialized)
                return;

            physicsVehicle = controller.GetComponent<PhysicsVehicle>();
            context = modContext;

            try
            {
                ConfigureAudio();
                ConfigureTrigger();
                initialized = true;
            }
            catch (System.Exception exception)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle ambient moo vehicle={controller.GetInstanceID()}: " +
                    $"configuration failed: {exception.GetBaseException().Message}");
            }
        }

        internal void NotifyMounted()
        {
            mounted = true;
            playerColliderIds.Clear();
            if (proximityTrigger != null)
                proximityTrigger.enabled = false;
            StopDismountRearm();
            StopAmbientMoo();
        }

        internal void NotifyDismounted()
        {
            mounted = false;
            if (proximityTrigger != null)
                proximityTrigger.enabled = true;
            StopDismountRearm();
            if (initialized && isActiveAndEnabled)
                dismountRearmCoroutine = StartCoroutine(RearmAfterDismount());
        }

        internal void NotifyPlayerEntered(Collider other)
        {
            if (!initialized || mounted || !IsPlayerCollider(other))
                return;

            if (!playerColliderIds.Add(other.GetInstanceID()))
                return;

            if (mooCoroutine == null)
                mooCoroutine = StartCoroutine(MooWhilePlayerNearby());
        }

        internal void NotifyPlayerExited(Collider other)
        {
            if (!IsPlayerCollider(other))
                return;

            playerColliderIds.Remove(other.GetInstanceID());
            if (playerColliderIds.Count == 0)
                StopAmbientMoo();
        }

        private void ConfigureAudio()
        {
            if (physicsVehicle == null)
                throw new System.InvalidOperationException("native vehicle physics is missing");

            var horn = physicsVehicle.soundManager.hornComponent;
            if (horn.clips == null || horn.clips.Count == 0 || horn.clips[0] == null)
                throw new System.InvalidOperationException("bundled Moo audio clip is missing");

            audioHost = new GameObject(AudioHostName);
            audioHost.transform.SetParent(transform, false);
            audioHost.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            audioSource = audioHost.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.clip = horn.clips[0];
            audioSource.volume = AmbientVolume;
            audioSource.spatialBlend = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Custom;
            audioSource.minDistance = 1f;
            audioSource.maxDistance = ProximityRadius;
            audioSource.SetCustomCurve(
                AudioSourceCurveType.CustomRolloff,
                AnimationCurve.Linear(0f, 1f, 1f, 1f));
            audioSource.dopplerLevel = 0f;
            audioSource.outputAudioMixerGroup = physicsVehicle.soundManager.otherMixerGroup;
            audioSource.clip.LoadAudioData();
        }

        private void ConfigureTrigger()
        {
            triggerHost = new GameObject(TriggerHostName);
            triggerHost.transform.SetParent(transform, false);
            triggerHost.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            var triggerLayer = LayerMask.NameToLayer("Trigger");
            triggerHost.layer = triggerLayer >= 0 ? triggerLayer : gameObject.layer;

            proximityTrigger = triggerHost.AddComponent<SphereCollider>();
            proximityTrigger.isTrigger = true;
            proximityTrigger.radius = ProximityRadius;

            var relay = triggerHost.AddComponent<MootorVehicleAmbientMooTrigger>();
            relay.Initialize(this);
        }

        private IEnumerator MooWhilePlayerNearby()
        {
            yield return new WaitForSeconds(Random.Range(InitialDelayMinimum, InitialDelayMaximum));

            while (!mounted && playerColliderIds.Count > 0)
            {
                if (audioSource != null && !audioSource.isPlaying && !AudioListener.pause)
                {
                    audioSource.volume = GetAmbientVolumeForPlayerDistance();
                    audioSource.pitch = Random.Range(0.97f, 1.03f);
                    if (audioSource.volume > 0f)
                        audioSource.Play();
                }

                yield return new WaitForSeconds(Random.Range(RepeatDelayMinimum, RepeatDelayMaximum));
            }

            mooCoroutine = null;
        }

        private float GetAmbientVolumeForPlayerDistance()
        {
            var distance = Vector3.Distance(PlayerHelper.GetPosition(), transform.position);
            if (distance <= FullVolumeRadius)
                return AmbientVolume;

            var fade = Mathf.InverseLerp(ProximityRadius, FullVolumeRadius, distance);
            return AmbientVolume * fade;
        }

        private IEnumerator RearmAfterDismount()
        {
            // Mounting can temporarily disable the walking collider without generating a fresh
            // trigger-enter on exit. One overlap query after dismount repairs that edge case; this
            // is not a recurring proximity poll.
            yield return new WaitForFixedUpdate();
            dismountRearmCoroutine = null;
            if (mounted || triggerHost == null)
                yield break;

            var count = Physics.OverlapSphereNonAlloc(
                triggerHost.transform.position,
                ProximityRadius,
                dismountOverlapBuffer,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);
            for (var index = 0; index < count; index++)
            {
                var collider = dismountOverlapBuffer[index];
                if (collider != null)
                    NotifyPlayerEntered(collider);
                dismountOverlapBuffer[index] = null!;
            }
        }

        private static bool IsPlayerCollider(Collider other)
        {
            var player = PlayerHelper.PlayerController;
            if (player == null)
                return false;

            var otherTransform = other.transform;
            if (otherTransform == player.transform || otherTransform.IsChildOf(player.transform))
                return true;

            var character = player.Character;
            return character != null &&
                   (otherTransform == character.transform || otherTransform.IsChildOf(character.transform));
        }

        private void StopAmbientMoo()
        {
            if (mooCoroutine != null)
            {
                StopCoroutine(mooCoroutine);
                mooCoroutine = null;
            }

            audioSource?.Stop();
        }

        private void StopDismountRearm()
        {
            if (dismountRearmCoroutine == null)
                return;

            StopCoroutine(dismountRearmCoroutine);
            dismountRearmCoroutine = null;
        }

        private void OnDisable()
        {
            playerColliderIds.Clear();
            StopDismountRearm();
            StopAmbientMoo();
        }

        private void OnDestroy()
        {
            StopAmbientMoo();
            if (triggerHost != null)
                Destroy(triggerHost);
            if (audioHost != null)
                Destroy(audioHost);
        }
    }

    internal sealed class MootorVehicleAmbientMooTrigger : MonoBehaviour
    {
        private MootorVehicleAmbientMooController? controller;

        internal void Initialize(MootorVehicleAmbientMooController owner)
        {
            controller = owner;
        }

        private void OnTriggerEnter(Collider other)
        {
            controller?.NotifyPlayerEntered(other);
        }

        private void OnTriggerExit(Collider other)
        {
            controller?.NotifyPlayerExited(other);
        }
    }
}
