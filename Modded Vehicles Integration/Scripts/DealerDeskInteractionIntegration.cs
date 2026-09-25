#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Buildings;
using Dialogs;
using JimmysUnityUtilities;
using Player.HUD.ItemInfoOverlays;
using Services;
using UI.Notification;
using UnityEngine;
using Vehicles.VehicleTypes;

namespace ModdedVehiclesIntegration
{
    internal sealed class DealerDeskInteractionIntegration
    {
        private const string NoVehiclesNotificationKey = "modded-vehicles-integration:no_mod_vehicles";
        private const string DealerUnavailableNotificationKey = "modded-vehicles-integration:dealer_unavailable";

        private static readonly HashSet<string> InteractiveDealerContactIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "City Cars",
            "Manhattan Luxury Cars",
            "The Hamptons Axis"
        };

        private Vector3 lastStandingPosition;
        private Quaternion lastStandingRotation;
        private bool hasStandingSnapshot;
        private bool dealerDialogWasOpen;
        private bool failureLogged;
        private SpecialEmployeeController? directInteractionDesk;
        private Action? directInteractionAction;
        private Action? synchronizeCatalogBeforeDialog;

        internal void SetCatalogSynchronizer(Action synchronizer)
        {
            synchronizeCatalogBeforeDialog = synchronizer;
        }

        internal void Update(ModContext? context)
        {
            try
            {
                InstallDirectDeskInteraction(context);

                var character = InstanceBehavior<GameManager>.Instance?.playerController?.Character;
                if (character == null)
                    return;

                var dialog = DialogController.current;
                var contactId = dialog?.contact?.id;
                var isDealerVehicleDialog =
                    dialog?.dialogType == DialogType.Physical &&
                    dialog.dialog is VehicleStoreDialog &&
                    !string.IsNullOrEmpty(contactId) &&
                    InteractiveDealerContactIds.Contains(contactId!);

                if (!isDealerVehicleDialog)
                {
                    dealerDialogWasOpen = false;
                    if (!character.isSittingOn)
                    {
                        lastStandingPosition = character.transform.position;
                        lastStandingRotation = character.transform.rotation;
                        hasStandingSnapshot = true;
                    }

                    return;
                }

                if (dealerDialogWasOpen)
                    return;

                dealerDialogWasOpen = true;
                var modVehicleCount = GetModVehicleCount(contactId!);

                if (modVehicleCount == 0)
                {
                    dialog!.FinishDialog();
                    RestoreStandingPosition(character);
                    Notifications.ShowError(NoVehiclesNotificationKey, NoVehiclesNotificationKey, true);
                    context?.Logger.Warn(
                        $"Modded Vehicles Integration: desk clicked at '{contactId}', but its mod-only catalogue is empty; " +
                        "closed the dialog to prevent the native vanilla-showroom fallback.");
                    dealerDialogWasOpen = false;
                    return;
                }

                character.Reset();
                RestoreStandingPosition(character);
            }
            catch (Exception exception)
            {
                if (failureLogged)
                    return;

                failureLogged = true;
                context?.Logger.Error(exception);
            }
        }

        internal void Shutdown()
        {
            if (directInteractionAction != null && ReferenceEquals(CtaManager.ctaAction, directInteractionAction))
                CtaManager.Clear();

            directInteractionDesk = null;
            directInteractionAction = null;
            synchronizeCatalogBeforeDialog = null;
        }

        private void InstallDirectDeskInteraction(ModContext? context)
        {
            var registration = InstanceBehavior<BuildingManager>.Instance?.buildingRegistration;
            var dealerContactId = registration?.BusinessName;
            var selectedDesk = MouseController.currentTargetEntity as SpecialEmployeeController;
            if (!BuildingManager.IsInsideBuilding ||
                string.IsNullOrEmpty(dealerContactId) ||
                !InteractiveDealerContactIds.Contains(dealerContactId!) ||
                selectedDesk == null ||
                selectedDesk.GetEmployeeType != SpecialEmployeeController.SpecialEmployeeType.VehicleStore)
            {
                if (directInteractionAction != null && ReferenceEquals(CtaManager.ctaAction, directInteractionAction))
                    CtaManager.Clear();

                directInteractionDesk = null;
                directInteractionAction = null;
                return;
            }

            if (!ReferenceEquals(directInteractionDesk, selectedDesk) || directInteractionAction == null)
            {
                directInteractionDesk = selectedDesk;
                directInteractionAction = () => InteractWithDeskDirectly(selectedDesk, context);
            }

            CtaManager.ctaAction = directInteractionAction;
        }

        private void InteractWithDeskDirectly(
            SpecialEmployeeController desk,
            ModContext? context)
        {
            var playerController = InstanceBehavior<GameManager>.Instance?.playerController;
            var character = playerController?.Character;
            if (playerController == null || character == null)
                return;

            CaptureStandingPosition(character);
            playerController.RemoveGoal();

            try
            {
                synchronizeCatalogBeforeDialog?.Invoke();
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
            }

            var dealerContactId =
                InstanceBehavior<BuildingManager>.Instance?.buildingRegistration?.BusinessName;
            if (string.IsNullOrEmpty(dealerContactId) ||
                !InteractiveDealerContactIds.Contains(dealerContactId!) ||
                !DealerServiceIntegration.EnsureDealerReady(dealerContactId!, context))
            {
                RecoverFailedInteraction(character);
                Notifications.ShowError(
                    DealerUnavailableNotificationKey,
                    DealerUnavailableNotificationKey,
                    true);
                return;
            }

            var modVehicleCount = GetModVehicleCount(dealerContactId!);
            if (modVehicleCount == 0)
            {
                RecoverFailedInteraction(character);
                Notifications.ShowError(NoVehiclesNotificationKey, NoVehiclesNotificationKey, true);
                context?.Logger.Warn(
                    $"Modded Vehicles Integration: blocked desk interaction at '{dealerContactId}' because " +
                    "its mod-only catalogue is empty.");
                return;
            }

            try
            {
                var interactionAccepted = desk.Interact();
                InstanceBehavior<OverlayManager>.Instance?.HideSimpleOverlayAndClearCta();

                var dialog = DialogController.current;
                var dialogContactId = dialog?.contact?.id;
                var validVehicleDialog =
                    interactionAccepted &&
                    dialog?.dialogType == DialogType.Physical &&
                    dialog.dialog is VehicleStoreDialog &&
                    string.Equals(dialogContactId, dealerContactId, StringComparison.Ordinal);
                if (!validVehicleDialog)
                {
                    context?.Logger.Warn(
                        $"Modded Vehicles Integration: dealer dialog validation failed for '{dealerContactId}' " +
                        $"(accepted={interactionAccepted}, dialogType={dialog?.dialogType.ToString() ?? "<none>"}, " +
                        $"dialog={dialog?.dialog?.GetType().Name ?? "<none>"}, " +
                        $"contact='{dialogContactId ?? "<none>"}'); recovering the player.");
                    TryCloseDialog(context);
                    RecoverFailedInteraction(character);
                    Notifications.ShowError(
                        DealerUnavailableNotificationKey,
                        DealerUnavailableNotificationKey,
                        true);
                    return;
                }

                dealerDialogWasOpen = true;
                character.Reset();
                RestoreStandingPosition(character);
            }
            catch (Exception exception)
            {
                TryCloseDialog(context);
                RecoverFailedInteraction(character);
                context?.Logger.Error(exception);
                Notifications.ShowError(
                    DealerUnavailableNotificationKey,
                    DealerUnavailableNotificationKey,
                    true);
            }
        }

        private void CaptureStandingPosition(ThirdPersonCharacter character)
        {
            if (character.isSittingOn)
                return;

            lastStandingPosition = character.transform.position;
            lastStandingRotation = character.transform.rotation;
            hasStandingSnapshot = true;
        }

        private void RecoverFailedInteraction(ThirdPersonCharacter character)
        {
            character.Reset();
            RestoreStandingPosition(character);
            dealerDialogWasOpen = false;
        }

        private static void TryCloseDialog(ModContext? context)
        {
            try
            {
                DialogController.current?.FinishDialog();
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
            }
        }

        private void RestoreStandingPosition(ThirdPersonCharacter character)
        {
            if (!hasStandingSnapshot)
                return;

            character.WarpSafely(lastStandingPosition);
            character.ForceToRotation(lastStandingRotation);
        }

        private static int GetModVehicleCount(string contactId)
        {
            if (!ContractItemsForSaleService.TryGetVehiclesForContact(contactId, out List<string> vehicleTypeNames) ||
                vehicleTypeNames == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var vehicleTypeName in vehicleTypeNames)
            {
                if (!string.IsNullOrEmpty(vehicleTypeName) && VehicleTypeHelper.IsModVehicleType(vehicleTypeName))
                    count++;
            }

            return count;
        }
    }
}
