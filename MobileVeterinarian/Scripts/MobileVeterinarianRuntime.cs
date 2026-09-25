#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BAModAPI;
using BigAmbitions.SaveSystem.Legacy;
using CustomNPCAPI;
using Dialogs;
using Entities;
using Extensions;
using Helpers;
using Localizor;
using UI.Smartphone;
using UI.Notification;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MobileVeterinarian
{
    internal sealed class MobileVeterinarianRuntime : MonoBehaviour
    {
        internal const string ContactNameKey = "mobileveterinarian:contact_name";
        internal const string TreatmentPriceKey = "mobileveterinarian:treatment_price";
        internal const string ServiceUnavailableKey = "mobileveterinarian:service_unavailable";

        // Balance values. The vanilla repair reference is VehicleInstance.CalculateRepairCost(),
        // currently damage fraction * vehicle price * 0.5.
        public const float BaseCalloutFee = 150f;
        public const float PricePerDamagePercentagePoint = 8f;
        public const float MinimumTreatmentPrice = 200f;
        public const float MaximumTreatmentPrice = 2000f;

        private const string DialogTypeKey = "mobileveterinarian_calldialog";
        private const string VeterinarianNpcId = "mobile-veterinarian-active-visit";
        private const float DamageTolerance = 0.001f;
        private const float StationarySpeedTolerance = 0.5f;
        private const float MaxAnimalTravelDistance = 1.5f;
        private const float TreatmentClearance = 0.3f;
        private const float SideNavMeshProbeRadius = 1f;
        private const float FallbackNavMeshProbeRadius = 1.25f;
        private const float MinimumSideSeparation = 0.35f;
        private const float MonitorIntervalSeconds = 0.1f;
        private const float DoctorSettleSeconds = 0.75f;
        private const float NodAngleDegrees = 18f;
        private const float NodPhaseSeconds = 0.23f;
        private const float BetweenNodsSeconds = 0.10f;
        private const float LookAtAnimalSeconds = 1.1f;
        private const float FinalPoseSeconds = 0.75f;
        private const float GestureTimeoutSeconds = 10f;

        private ModContext? context;
        private GameObject? doctorPrefab;
        private VisitState? activeVisit;
        private Coroutine? visitCoroutine;
        private Coroutine? gestureCoroutine;
        private bool sceneCallbackInstalled;
        private bool shuttingDown;

        internal static MobileVeterinarianRuntime? Current { get; private set; }

        internal static MobileVeterinarianRuntime Install(ModContext context, GameObject? doctorPrefab)
        {
            if (Current != null)
                Current.Shutdown("runtime replaced");

            var root = new GameObject("MobileVeterinarian.Runtime");
            var runtime = root.AddComponent<MobileVeterinarianRuntime>();
            runtime.context = context;
            runtime.doctorPrefab = doctorPrefab;
            Current = runtime;
            runtime.RegisterPhoneContact();
            return runtime;
        }

        internal DialogEntry CreateInitialDialogEntry()
        {
            if (!TryCreateQuote(out var quote, out var failureKey, out var failureData))
                return MobileVeterinarianDialog.CreateTerminalEntry(failureKey, failureData);

            return MobileVeterinarianDialog.CreateQuoteEntry(quote);
        }

        internal bool TryStartVisit(
            TreatmentQuote quote,
            out string failureKey,
            out Dictionary<string, string>? failureData)
        {
            if (!TryCreateQuote(out var currentQuote, out failureKey, out failureData))
                return false;

            if (!string.Equals(currentQuote.VehicleId, quote.VehicleId, StringComparison.Ordinal))
            {
                failureKey = "mobileveterinarian:not_mounted";
                failureData = null;
                return false;
            }

            // Damage cannot normally change while the animal is stopped, but always charge the
            // amount shown in the confirmation flow rather than silently replacing the quote.
            if (Math.Abs(currentQuote.Price - quote.Price) > 0.01f)
            {
                failureKey = TreatmentPriceKey;
                failureData = currentQuote.CreateLocalizationData();
                return false;
            }

            if (!TryFindVisitPositions(
                    currentQuote.Controller,
                    currentQuote.Registration,
                    out var spawnPosition,
                    out var treatmentPosition))
            {
                LogWarning(
                    $"NavMesh placement result success=false vehicleId='{currentQuote.VehicleId}' " +
                    $"type='{currentQuote.VehicleTypeName}'.");
                failureKey = "mobileveterinarian:cannot_reach";
                failureData = currentQuote.CreateLocalizationData();
                return false;
            }

            LogInfo(
                $"NavMesh placement result success=true vehicleId='{currentQuote.VehicleId}' " +
                $"spawn={FormatVector(spawnPosition)} treatment={FormatVector(treatmentPosition)}.");

            activeVisit = new VisitState(currentQuote, spawnPosition, treatmentPosition);
            InstallSceneCallback();
            visitCoroutine = StartCoroutine(RunVisit(activeVisit));
            return true;
        }

        internal void Shutdown(string reason)
        {
            if (ReferenceEquals(Current, this))
                Current = null;

            // Unity can destroy the native component before the mod loader invokes unload.
            // In that state, accessing Component.gameObject throws even though the managed
            // reference is still non-null.
            if (this == null || shuttingDown)
                return;

            shuttingDown = true;
            CancelActiveVisit(reason, false);
            RemoveSceneCallback();
            Destroy(gameObject);
        }

        private void RegisterPhoneContact()
        {
            try
            {
                var dialogType = (CallDialogType)ModEnumHash.GetSafeHash(DialogTypeKey);
                CallDialogFactory.RegisterDialog(dialogType, () => new MobileVeterinarianDialog());
                var contact = CustomNpcPhone.EnsureContact(new CustomNpcPhoneDefinition
                {
                    ContactId = ContactNameKey,
                    ContactCategory = "General",
                    DescriptionKey = "mobileveterinarian:contact_description",
                    DialogTypeKey = DialogTypeKey
                });

                if (contact == null)
                {
                    LogWarning("Phone-contact registration failed: Contact.GetContact returned null.");
                    return;
                }

                LogInfo(
                    $"Phone-contact registration success id='{contact.id}' dialog='{DialogTypeKey}' " +
                    $"customNpcHostActive={CustomNpcApi.IsHostActive}.");
            }
            catch (Exception exception)
            {
                context?.Logger.Error("Mobile Veterinarian phone-contact registration failed. " + exception);
            }
        }

        private bool TryCreateQuote(
            out TreatmentQuote quote,
            out string failureKey,
            out Dictionary<string, string>? failureData)
        {
            quote = null!;
            failureData = null;

            if (doctorPrefab == null)
            {
                failureKey = ServiceUnavailableKey;
                LogWarning("Supported-animal validation skipped reason=doctor_prefab_unavailable.");
                return false;
            }

            if (activeVisit != null)
            {
                failureKey = "mobileveterinarian:already_active";
                LogInfo("Supported-animal validation success=false reason=visit_already_active.");
                return false;
            }

            if (!TryResolveMountedVehicle(out var controller))
            {
                failureKey = "mobileveterinarian:not_mounted";
                return false;
            }

            var instance = controller.vehicleInstance;
            var vehicleTypeName = instance?.vehicleTypeName ?? string.Empty;
            LogInfo(
                $"Mounted vehicle resolution success=true vehicleId='{instance?.id ?? "<none>"}' " +
                $"type='{vehicleTypeName}' controlledByPlayer={controller.controlledByPlayer}.");

            if (!AnimalVehicleRegistry.TryGet(vehicleTypeName, out var registration))
            {
                failureKey = "mobileveterinarian:unsupported_animal";
                LogInfo($"Supported-animal validation success=false type='{vehicleTypeName}'.");
                return false;
            }

            var animalName = ResolveAnimalName(registration, vehicleTypeName);
            failureData = new Dictionary<string, string> { { "animal", animalName } };
            LogInfo($"Supported-animal validation success=true type='{vehicleTypeName}' animal='{animalName}'.");

            var damageFraction = Mathf.Clamp01(instance!.damage);
            var damagePercentage = damageFraction * 100f;
            LogInfo(
                $"Current health vehicleId='{instance.id}' healthPercent={100f - damagePercentage:F1} " +
                $"damagePercent={damagePercentage:F1} rawDamage={instance.damage:F4}.");
            if (damageFraction <= DamageTolerance)
            {
                failureKey = "mobileveterinarian:not_damaged";
                return false;
            }

            var speed = ResolveCurrentSpeed(controller);
            if (speed > StationarySpeedTolerance)
            {
                failureKey = "mobileveterinarian:must_be_stationary";
                LogInfo($"Stationary validation success=false speed={speed:F3}.");
                return false;
            }

            var configuredPrice = BaseCalloutFee + damagePercentage * PricePerDamagePercentagePoint;
            var treatmentPrice = RoundPrice(Mathf.Clamp(
                configuredPrice,
                MinimumTreatmentPrice,
                MaximumTreatmentPrice));
            var vanillaRepairReference = -1f;
            try
            {
                vanillaRepairReference = instance.CalculateRepairCost();
            }
            catch (Exception exception)
            {
                LogWarning($"Vanilla repair-price reference unavailable: {exception.Message}");
            }
            LogInfo(
                $"Calculated treatment price vehicleId='{instance.id}' damagePercent={damagePercentage:F1} " +
                $"baseFee={BaseCalloutFee:F0} perPoint={PricePerDamagePercentagePoint:F0} " +
                $"minimum={MinimumTreatmentPrice:F0} maximum={MaximumTreatmentPrice:F0} " +
                $"vanillaRepairReference={vanillaRepairReference:F0} result={treatmentPrice:F0}.");

            var canAfford = SaveGameManager.Current != null &&
                            SaveGameManager.Current.Money + 0.001f >= treatmentPrice;
            LogInfo(
                $"Affordability result vehicleId='{instance.id}' canAfford={canAfford} " +
                $"balance={SaveGameManager.Current?.Money ?? 0f:F0} price={treatmentPrice:F0}.");
            if (!canAfford)
            {
                failureKey = "mobileveterinarian:insufficient_funds";
                failureData["price"] = treatmentPrice.ToShortCurrencyFormat();
                return false;
            }

            quote = new TreatmentQuote(
                controller,
                registration,
                animalName,
                damagePercentage,
                treatmentPrice);
            failureKey = string.Empty;
            failureData = null;
            return true;
        }

        private bool TryResolveMountedVehicle(out VehicleController controller)
        {
            controller = null!;
            var save = SaveGameManager.Current;
            var activeVehicleId = save?.ActiveVehicleId;
            if (!PlayerHelper.IsUsingVehicle || string.IsNullOrWhiteSpace(activeVehicleId))
            {
                LogInfo(
                    $"Mounted vehicle resolution success=false isUsingVehicle={PlayerHelper.IsUsingVehicle} " +
                    $"activeVehicleId='{activeVehicleId ?? "<none>"}'.");
                return false;
            }

            var vehicles = VehicleHelper.AllPlayerVehicles;
            if (vehicles == null)
            {
                LogInfo("Mounted vehicle resolution success=false reason=vehicle_collection_unavailable.");
                return false;
            }

            foreach (var candidate in vehicles)
            {
                if (candidate?.vehicleInstance == null || !candidate.controlledByPlayer ||
                    !string.Equals(candidate.vehicleInstance.id, activeVehicleId, StringComparison.Ordinal))
                {
                    continue;
                }

                controller = candidate;
                return true;
            }

            LogInfo(
                $"Mounted vehicle resolution success=false reason=active_controlled_vehicle_not_found " +
                $"activeVehicleId='{activeVehicleId}'.");
            return false;
        }

        private IEnumerator RunVisit(VisitState visit)
        {
            yield return null;

            LogInfo($"Visit queued vehicleId='{visit.Quote.VehicleId}' waitingForPhoneClose={FullMenu.IsOpen}.");
            while (FullMenu.IsOpen)
            {
                if (!IsVisitStillValid(visit, out var queuedCancellationReason))
                {
                    CancelActiveVisit(queuedCancellationReason, true);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(MonitorIntervalSeconds);
            }

            // Let the full-menu close transition finish before creating anything the player
            // is expected to see in the world.
            yield return new WaitForSecondsRealtime(0.25f);

            if (!IsVisitStillValid(visit, out var cancellationReason))
            {
                CancelActiveVisit(cancellationReason, true);
                yield break;
            }

            CustomNpcHandle? veterinarian = null;
            try
            {
                var towardAnimal = Flatten(visit.Quote.Controller.transform.position - visit.SpawnPosition);
                veterinarian = CustomNpcApi.Spawn(
                    context?.ModId ?? "MobileVeterinarian",
                    new CustomNpcDefinition
                    {
                        Id = VeterinarianNpcId,
                        DisplayName = ContactNameKey.Localize().ToString(),
                        NameKey = ContactNameKey,
                        PrefabName = string.Empty,
                        GameObjectName = "MobileVeterinarian.ActiveVeterinarian",
                        VisualObjectName = "DoctorVisual",
                        Interactable = false,
                        Position = visit.SpawnPosition,
                        Forward = towardAnimal.sqrMagnitude > 0.001f ? towardAnimal : Vector3.forward
                    },
                    new CustomNpcSpawnOptions
                    {
                        Visible = true,
                        BuildFallbackVisual = false,
                        VisualFactory = parent => InstantiateDoctorVisual(parent)
                    });
            }
            catch (Exception exception)
            {
                context?.Logger.Error("Mobile Veterinarian NPC spawn failed. " + exception);
            }

            if (veterinarian?.Root == null ||
                !veterinarian.Root.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer != null))
            {
                veterinarian?.Dispose();
                LogWarning($"Veterinarian spawn result success=false vehicleId='{visit.Quote.VehicleId}'.");
                CancelActiveVisit("veterinarian spawn failed", true, "mobileveterinarian:cannot_reach");
                yield break;
            }

            visit.Veterinarian = veterinarian;
            LogInfo(
                $"Veterinarian spawn result success=true vehicleId='{visit.Quote.VehicleId}' " +
                $"position={FormatVector(visit.SpawnPosition)}.");

            yield return null;
            var doctorHead = FindDoctorHead(veterinarian.Root.transform);
            if (doctorHead == null)
            {
                LogWarning("Treatment could not start: bundled doctor head bone was not found.");
                CancelActiveVisit("doctor head bone missing", true, ServiceUnavailableKey);
                yield break;
            }

            LogInfo(
                $"Treatment start vehicleId='{visit.Quote.VehicleId}' animal='{visit.Quote.AnimalName}' " +
                $"damagePercent={visit.Quote.DamagePercentage:F1}.");

            var gestureCompleted = false;
            gestureCoroutine = StartCoroutine(RunDoctorTreatmentGesture(
                veterinarian.Root.transform,
                doctorHead,
                visit.Quote.Controller.transform,
                () => gestureCompleted = true));
            LogInfo($"Treatment gesture started head='{doctorHead.name}' sequence=nod2_look_animal_nod2.");

            var gestureDeadline = Time.unscaledTime + GestureTimeoutSeconds;
            while (!gestureCompleted && Time.unscaledTime < gestureDeadline)
            {
                if (!IsVisitStillValid(visit, out cancellationReason))
                {
                    CancelActiveVisit(cancellationReason, true);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(MonitorIntervalSeconds);
            }

            gestureCoroutine = null;
            if (!gestureCompleted)
            {
                CancelActiveVisit("doctor gesture timed out", true);
                yield break;
            }

            if (!IsVisitStillValid(visit, out cancellationReason))
            {
                CancelActiveVisit(cancellationReason, true);
                yield break;
            }

            var save = SaveGameManager.Current;
            if (save == null || save.Money + 0.001f < visit.Quote.Price)
            {
                LogInfo(
                    $"Affordability result before repair vehicleId='{visit.Quote.VehicleId}' canAfford=false " +
                    $"balance={save?.Money ?? 0f:F0} price={visit.Quote.Price:F0}.");
                CancelActiveVisit("funds changed during visit", true, "mobileveterinarian:insufficient_funds");
                yield break;
            }

            try
            {
                visit.Quote.Controller.Repair();
                SaveGameManager.MarkChange();
            }
            catch (Exception exception)
            {
                context?.Logger.Error("Mobile Veterinarian vehicle repair failed. " + exception);
                CancelActiveVisit("vehicle repair threw an exception", true);
                yield break;
            }
            var fullyRestored = visit.Quote.Controller.vehicleInstance != null &&
                                visit.Quote.Controller.vehicleInstance.damage <= DamageTolerance &&
                                (visit.Quote.Controller.vehicleInstance.deformations?.Count ?? 0) == 0;
            LogInfo(
                $"Repair result vehicleId='{visit.Quote.VehicleId}' success={fullyRestored} " +
                $"remainingDamage={visit.Quote.Controller.vehicleInstance?.damage ?? -1f:F4}.");
            if (!fullyRestored)
            {
                CancelActiveVisit("vehicle repair did not fully restore health", true);
                yield break;
            }

            visit.RepairCompleted = true;
            visit.PaymentAttempted = true;
            var paymentSucceeded = false;
            try
            {
                paymentSucceeded = GameManager.ChangeMoneySafe(
                    -visit.Quote.Price,
                    new TransactionInfo("mobileveterinarian:transaction"),
                    force: false,
                    showNotification: true);
            }
            catch (Exception exception)
            {
                context?.Logger.Error("Mobile Veterinarian payment failed. " + exception);
            }
            visit.PaymentCharged = paymentSucceeded;
            LogInfo(
                $"Payment result vehicleId='{visit.Quote.VehicleId}' attempted=true success={paymentSucceeded} " +
                $"amount={visit.Quote.Price:F0}.");
            if (!paymentSucceeded)
            {
                CancelActiveVisit("payment rejected after repair", true, "mobileveterinarian:insufficient_funds");
                yield break;
            }

            var notificationData = visit.Quote.CreateLocalizationData();
            ShowNotification(
                NotificationType.Success,
                "mobileveterinarian:treatment_completed",
                notificationData,
                "mobileveterinarian_treatment_completed");
            TryAppendContactMessage("mobileveterinarian:treatment_completed", notificationData);
            LogInfo(
                $"Treatment completed vehicleId='{visit.Quote.VehicleId}' charged={visit.PaymentCharged} " +
                $"price={visit.Quote.Price:F0}.");

            CompleteVisit("treatment completed");
        }

        private GameObject InstantiateDoctorVisual(Transform parent)
        {
            var source = doctorPrefab ?? throw new InvalidOperationException("Doctor prefab is unavailable.");
            return Instantiate(source, parent, false);
        }

        private IEnumerator RunDoctorTreatmentGesture(
            Transform doctorRoot,
            Transform head,
            Transform animal,
            Action onCompleted)
        {
            // Face the mounted animal from the first visible frame. Using the third-person
            // camera as the initial target made the doctor appear turned 90 degrees when the
            // player had orbited the camera to the animal's side.
            FaceTarget(doctorRoot, animal.position);
            yield return new WaitForSecondsRealtime(DoctorSettleSeconds);

            var restingRotation = head.localRotation;
            yield return NodTwice(doctorRoot, head);
            head.localRotation = restingRotation;

            FaceTarget(doctorRoot, animal.position);
            yield return new WaitForSecondsRealtime(LookAtAnimalSeconds);

            yield return NodTwice(doctorRoot, head);
            head.localRotation = restingRotation;
            yield return new WaitForSecondsRealtime(FinalPoseSeconds);
            onCompleted.Invoke();
        }

        private static IEnumerator NodTwice(Transform doctorRoot, Transform head)
        {
            var restingRotation = head.rotation;
            var loweredRotation = Quaternion.AngleAxis(NodAngleDegrees, doctorRoot.right) * restingRotation;
            for (var index = 0; index < 2; index++)
            {
                yield return RotateWorld(head, restingRotation, loweredRotation, NodPhaseSeconds);
                yield return RotateWorld(head, loweredRotation, restingRotation, NodPhaseSeconds);
                if (index == 0)
                    yield return new WaitForSecondsRealtime(BetweenNodsSeconds);
            }
        }

        private static IEnumerator RotateWorld(
            Transform target,
            Quaternion from,
            Quaternion to,
            float duration)
        {
            var startedAt = Time.unscaledTime;
            while (target != null)
            {
                var progress = Mathf.Clamp01((Time.unscaledTime - startedAt) / duration);
                target.rotation = Quaternion.Slerp(from, to, progress);
                if (progress >= 1f)
                    yield break;
                yield return null;
            }
        }

        private static Transform? FindDoctorHead(Transform root)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            return transforms.FirstOrDefault(transform =>
                       string.Equals(transform.name, "Bip001 Head_023_85", StringComparison.OrdinalIgnoreCase))
                   ?? transforms.FirstOrDefault(transform =>
                       transform.name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void FaceTarget(Transform source, Vector3 target)
        {
            var direction = Flatten(target - source.position);
            if (direction.sqrMagnitude > 0.001f)
                source.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        private bool IsVisitStillValid(VisitState visit, out string reason)
        {
            if (visit == null || activeVisit != visit)
            {
                reason = "visit state replaced";
                return false;
            }

            var controller = visit.Quote.Controller;
            if (controller == null || controller.gameObject == null || controller.vehicleInstance == null)
            {
                reason = "target animal disappeared";
                return false;
            }

            if (!PlayerHelper.IsUsingVehicle || !controller.controlledByPlayer ||
                !string.Equals(SaveGameManager.Current?.ActiveVehicleId, visit.Quote.VehicleId, StringComparison.Ordinal))
            {
                reason = "player dismounted or changed vehicle";
                return false;
            }

            if (ResolveCurrentSpeed(controller) > StationarySpeedTolerance)
            {
                reason = "target animal moved";
                return false;
            }

            if (HorizontalDistance(controller.transform.position, visit.AnimalStartPosition) > MaxAnimalTravelDistance)
            {
                reason = "target animal drove away";
                return false;
            }

            if (SceneManager.GetActiveScene().handle != visit.SceneHandle)
            {
                reason = "active scene changed";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool TryFindVisitPositions(
            VehicleController controller,
            AnimalVehicleRegistration registration,
            out Vector3 spawnPosition,
            out Vector3 treatmentPosition)
        {
            spawnPosition = default;
            treatmentPosition = default;
            var preferred = registration.TreatmentPositionOffset;
            var candidateOffsets = new[]
            {
                preferred,
                new Vector3(-preferred.x, preferred.y, preferred.z),
                new Vector3(0f, 0f, 1.9f),
                new Vector3(0f, 0f, -1.9f)
            };
            var candidateResults = new List<string>(candidateOffsets.Length);

            for (var index = 0; index < candidateOffsets.Length; index++)
            {
                var offset = candidateOffsets[index];
                var isSideCandidate = index < 2;
                var candidateName = index == 0
                    ? "preferred-side"
                    : index == 1
                        ? "opposite-side"
                        : index == 2
                            ? "front"
                            : "rear";
                var requestedTreatment = controller.transform.TransformPoint(offset);
                var probeRadius = isSideCandidate ? SideNavMeshProbeRadius : FallbackNavMeshProbeRadius;
                if (!NavMesh.SamplePosition(requestedTreatment, out var treatmentHit, probeRadius, NavMesh.AllAreas))
                {
                    candidateResults.Add(candidateName + ":no-navmesh");
                    continue;
                }

                if (isSideCandidate)
                {
                    var localHit = controller.transform.InverseTransformPoint(treatmentHit.position);
                    if (Mathf.Sign(localHit.x) != Mathf.Sign(offset.x) ||
                        Mathf.Abs(localHit.x) < MinimumSideSeparation)
                    {
                        candidateResults.Add(candidateName + ":crossed-side");
                        continue;
                    }
                }

                if (!HasVehicleClearance(controller, treatmentHit.position))
                {
                    candidateResults.Add(candidateName + ":animal-clearance");
                    continue;
                }

                var away = Flatten(treatmentHit.position - controller.transform.position);
                if (away.sqrMagnitude < 0.001f)
                {
                    candidateResults.Add(candidateName + ":at-animal-center");
                    continue;
                }

                // A single safe standing point beside the animal is sufficient. The doctor
                // faces the animal in place, so route availability cannot reject the visit.
                treatmentPosition = treatmentHit.position;
                spawnPosition = treatmentHit.position;
                candidateResults.Add(candidateName + ":selected");
                LogInfo("NavMesh placement candidates " + string.Join(", ", candidateResults.ToArray()) + ".");
                return true;
            }

            LogInfo("NavMesh placement candidates " + string.Join(", ", candidateResults.ToArray()) + ".");
            return false;
        }

        private static bool HasVehicleClearance(VehicleController controller, Vector3 position)
        {
            var collider = controller.vehicleCollider ?? controller.GetComponentInChildren<Collider>(true);
            if (collider == null)
                return HorizontalDistance(controller.transform.position, position) >= 1f;

            var nearest = collider.bounds.ClosestPoint(position);
            return HorizontalDistance(nearest, position) >= TreatmentClearance;
        }

        private void CancelActiveVisit(string reason, bool showNotification, string? notificationKey = null)
        {
            var visit = activeVisit;
            if (visit == null)
                return;

            LogWarning(
                $"Visit cancellation reason='{reason}' vehicleId='{visit.Quote.VehicleId}' " +
                $"repairCompleted={visit.RepairCompleted} paymentAttempted={visit.PaymentAttempted} " +
                $"paymentCharged={visit.PaymentCharged}.");

            var key = string.IsNullOrWhiteSpace(notificationKey)
                ? "mobileveterinarian:visit_cancelled"
                : notificationKey!;
            if (showNotification)
            {
                var data = visit.Quote.CreateLocalizationData();
                ShowNotification(NotificationType.Error, key, data, "mobileveterinarian_visit_cancelled");
                TryAppendContactMessage(key, data);
            }

            CleanupVisit(visit);
        }

        private void CompleteVisit(string reason)
        {
            var visit = activeVisit;
            if (visit == null)
                return;

            LogInfo($"Visit cleanup reason='{reason}' vehicleId='{visit.Quote.VehicleId}'.");
            CleanupVisit(visit);
        }

        private void CleanupVisit(VisitState visit)
        {
            if (visitCoroutine != null)
            {
                var coroutine = visitCoroutine;
                visitCoroutine = null;
                StopCoroutine(coroutine);
            }

            if (gestureCoroutine != null)
            {
                StopCoroutine(gestureCoroutine);
                gestureCoroutine = null;
            }

            if (visit.Veterinarian != null)
            {
                visit.Veterinarian.Dispose();
                visit.Veterinarian = null;
            }

            if (activeVisit == visit)
                activeVisit = null;
            RemoveSceneCallback();
        }

        private void InstallSceneCallback()
        {
            if (sceneCallbackInstalled)
                return;

            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            sceneCallbackInstalled = true;
        }

        private void RemoveSceneCallback()
        {
            if (!sceneCallbackInstalled)
                return;

            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            sceneCallbackInstalled = false;
        }

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            CancelActiveVisit(
                $"scene changed from '{previous.name}' to '{next.name}'",
                !shuttingDown);
        }

        private void OnDestroy()
        {
            if (!shuttingDown)
                CancelActiveVisit("runtime destroyed", false);
            RemoveSceneCallback();
            if (ReferenceEquals(Current, this))
                Current = null;
        }

        private void OnApplicationQuit()
        {
            shuttingDown = true;
            CancelActiveVisit("application quit", false);
            RemoveSceneCallback();
        }

        private static string ResolveAnimalName(
            AnimalVehicleRegistration registration,
            string vehicleTypeName)
        {
            var configured = registration.AnimalNameKeyOrText;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                try
                {
                    var localized = configured!.Localize().ToString();
                    if (!string.IsNullOrWhiteSpace(localized) &&
                        !string.Equals(localized, configured, StringComparison.Ordinal))
                    {
                        return localized;
                    }
                }
                catch
                {
                }

                return configured!;
            }

            try
            {
                var localized = vehicleTypeName.GetLocalization()?.ToString();
                if (!string.IsNullOrWhiteSpace(localized) &&
                    !string.Equals(localized, vehicleTypeName, StringComparison.Ordinal))
                {
                    return localized!;
                }
            }
            catch
            {
            }

            return "mobileveterinarian:animal_generic".Localize().ToString();
        }

        private static float ResolveCurrentSpeed(VehicleController controller)
        {
            var rigidbody = controller.GetComponent<Rigidbody>() ??
                            controller.GetComponentInChildren<Rigidbody>(true);
            var rigidbodySpeed = rigidbody != null ? rigidbody.velocity.magnitude : 0f;
            return Mathf.Max(Mathf.Abs(controller.CurrentSpeed), rigidbodySpeed);
        }

        private static float RoundPrice(float value) => Mathf.Ceil(value / 5f) * 5f;

        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value.normalized;
        }

        private static float HorizontalDistance(Vector3 left, Vector3 right)
        {
            left.y = 0f;
            right.y = 0f;
            return Vector3.Distance(left, right);
        }

        private static string FormatVector(Vector3 value) =>
            $"({value.x:F2},{value.y:F2},{value.z:F2})";

        private void TryAppendContactMessage(string key, Dictionary<string, string> data)
        {
            try
            {
                var contact = CustomNpcPhone.FindContact(ContactNameKey);
                contact?.SendMessage(new TextMessage(key, data, true, true), true);
            }
            catch (Exception exception)
            {
                LogWarning($"Contact message failed key='{key}' reason='{exception.Message}'.");
            }
        }

        private void ShowNotification(
            NotificationType type,
            string key,
            Dictionary<string, string> data,
            string duplicateId)
        {
            try
            {
                Notifications.Show(type, key, data, 5f, duplicateId, null, true, false);
            }
            catch (Exception exception)
            {
                LogWarning($"Notification failed key='{key}' reason='{exception.Message}'.");
            }
        }

        [System.Diagnostics.Conditional("MOBILEVETERINARIAN_DIAGNOSTICS")]
        private void LogInfo(string message) => context?.Logger.Info("Mobile Veterinarian: " + message);

        private void LogWarning(string message) => context?.Logger.Warn("Mobile Veterinarian: " + message);

        private sealed class VisitState
        {
            internal VisitState(TreatmentQuote quote, Vector3 spawnPosition, Vector3 treatmentPosition)
            {
                Quote = quote;
                SpawnPosition = spawnPosition;
                TreatmentPosition = treatmentPosition;
                AnimalStartPosition = quote.Controller.transform.position;
                SceneHandle = SceneManager.GetActiveScene().handle;
            }

            internal TreatmentQuote Quote { get; }
            internal Vector3 SpawnPosition { get; }
            internal Vector3 TreatmentPosition { get; }
            internal Vector3 AnimalStartPosition { get; }
            internal int SceneHandle { get; }
            internal CustomNpcHandle? Veterinarian { get; set; }
            internal bool RepairCompleted { get; set; }
            internal bool PaymentAttempted { get; set; }
            internal bool PaymentCharged { get; set; }
        }
    }

    internal sealed class TreatmentQuote
    {
        internal TreatmentQuote(
            VehicleController controller,
            AnimalVehicleRegistration registration,
            string animalName,
            float damagePercentage,
            float price)
        {
            Controller = controller;
            Registration = registration;
            AnimalName = animalName;
            DamagePercentage = damagePercentage;
            Price = price;
            VehicleId = controller.vehicleInstance.id;
            VehicleTypeName = controller.vehicleInstance.vehicleTypeName;
        }

        internal VehicleController Controller { get; }
        internal AnimalVehicleRegistration Registration { get; }
        internal string AnimalName { get; }
        internal float DamagePercentage { get; }
        internal float Price { get; }
        internal string VehicleId { get; }
        internal string VehicleTypeName { get; }

        internal Dictionary<string, string> CreateLocalizationData()
        {
            return new Dictionary<string, string>
            {
                { "animal", AnimalName },
                { "damage", DamagePercentage.ToString("0.#") },
                { "price", Price.ToShortCurrencyFormat() }
            };
        }
    }
}
