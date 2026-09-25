#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Entities;
using Helpers;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxIllegalParkingService
    {
        private static readonly HashSet<string> ParkingTicketMessageKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "phone_government_parking_ticket",
            "ba:messagetype_phone_government_parking_ticket"
        };
        private static readonly HashSet<string> ParkingTicketTransactionTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ba:transaction_parkingticket",
            "transaction_parkingticket"
        };
        private readonly Dictionary<string, ParkingState> originalParkingStates = new Dictionary<string, ParkingState>(StringComparer.Ordinal);

        public void InvalidateCache()
        {
        }

        public void ApplyConfiguredBehavior(ModContext? context, BigHaxSettings settings)
        {
            if (!settings.DisableIllegalParkingPenalties)
            {
                RestoreTrackedParkingStates();
                return;
            }

            CleanPlayerVehicles();
            CleanSaveGameVehicles();
            RefundParkingTicketTransactions();
            RemoveParkingTicketMessages();
        }

        public void HandleVehicleExited(ModContext? context, BigHaxSettings settings)
        {
            if (!settings.DisableIllegalParkingPenalties)
                return;

            ApplyConfiguredBehavior(context, settings);
        }

        public void HandleVehicleEntered(ModContext? context, BigHaxSettings settings)
        {
            if (!settings.DisableIllegalParkingPenalties)
                return;

            ApplyConfiguredBehavior(context, settings);
        }

        public void RestoreOriginalState()
        {
            RestoreTrackedParkingStates();
            originalParkingStates.Clear();
        }

        private void CleanPlayerVehicles()
        {
            var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
            if (allPlayerVehicles == null)
                return;

            foreach (var vehicleController in allPlayerVehicles)
            {
                var vehicleInstance = vehicleController?.vehicleInstance;
                if (vehicleInstance == null || string.IsNullOrWhiteSpace(vehicleInstance.id))
                    continue;

                var ticketCount = vehicleInstance.parkingTickets?.Count ?? 0;
                var unpaidAmount = vehicleInstance.unpaidParkingAmount;
                var parkingState = vehicleInstance.parkingState;
                if (ticketCount > 0 && vehicleInstance.parkingTickets != null)
                {
                    vehicleInstance.parkingTickets.Clear();
                }

                if (Math.Abs(unpaidAmount) > 0.001f)
                {
                    vehicleInstance.unpaidParkingAmount = 0f;
                }

                if (parkingState == ParkingState.Illegal)
                {
                    TrackOriginalParkingState(vehicleInstance.id, parkingState);
                    vehicleInstance.parkingState = ParkingState.Legal;
                }
            }
        }

        private void CleanSaveGameVehicles()
        {
            var saveGame = SaveGameManager.Current;
            var vehicles = saveGame?.VehicleInstances;
            if (vehicles == null)
                return;

            foreach (var vehicleInstance in vehicles)
            {
                if (vehicleInstance == null || string.IsNullOrWhiteSpace(vehicleInstance.id))
                    continue;

                var ticketCount = vehicleInstance.parkingTickets?.Count ?? 0;
                var unpaidAmount = vehicleInstance.unpaidParkingAmount;
                var parkingState = vehicleInstance.parkingState;
                if (ticketCount > 0 && vehicleInstance.parkingTickets != null)
                {
                    vehicleInstance.parkingTickets.Clear();
                }

                if (Math.Abs(unpaidAmount) > 0.001f)
                {
                    vehicleInstance.unpaidParkingAmount = 0f;
                }

                if (parkingState == ParkingState.Illegal)
                {
                    TrackOriginalParkingState(vehicleInstance.id, parkingState);
                    vehicleInstance.parkingState = ParkingState.Legal;
                }
            }
        }

        private void RefundParkingTicketTransactions()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.Transactions == null || saveGame.Transactions.Count == 0)
                return;

            var originalCount = saveGame.Transactions.Count;
            var refundedAmount = 0f;
            var keptTransactions = new Queue<Transaction>(originalCount);
            while (saveGame.Transactions.Count > 0)
            {
                var transaction = saveGame.Transactions.Dequeue();
                if (transaction != null &&
                    !string.IsNullOrWhiteSpace(transaction.transactionType) &&
                    ParkingTicketTransactionTypes.Contains(transaction.transactionType))
                {
                    refundedAmount += Mathf.Abs(transaction.amount);
                    continue;
                }

                if (transaction != null)
                    keptTransactions.Enqueue(transaction);
            }

            saveGame.Transactions = keptTransactions;
            if (refundedAmount <= 0.001f)
                return;

            saveGame.Money += refundedAmount;
        }

        private void RemoveParkingTicketMessages()
        {
            var contacts = SaveGameManager.Current?.Contacts;
            if (contacts == null)
                return;

            foreach (var contact in contacts)
            {
                if (contact?.messagesQueue == null || contact.messagesQueue.Count == 0)
                    continue;

                var originalCount = contact.messagesQueue.Count;
                var keptMessages = new Queue<TextMessage>(originalCount);
                while (contact.messagesQueue.Count > 0)
                {
                    var message = contact.messagesQueue.Dequeue();
                    if (message != null &&
                        !string.IsNullOrWhiteSpace(message.messageKey) &&
                        ParkingTicketMessageKeys.Contains(message.messageKey))
                    {
                        continue;
                    }

                    if (message != null)
                        keptMessages.Enqueue(message);
                }

                contact.messagesQueue = keptMessages;
            }
        }

        private void TrackOriginalParkingState(string vehicleId, ParkingState parkingState)
        {
            if (string.IsNullOrWhiteSpace(vehicleId) || originalParkingStates.ContainsKey(vehicleId))
                return;

            originalParkingStates[vehicleId] = parkingState;
        }

        private void RestoreTrackedParkingStates()
        {
            if (originalParkingStates.Count == 0)
                return;

            RestoreTrackedParkingStates(VehicleHelper.AllPlayerVehicles);
            RestoreTrackedParkingStates(SaveGameManager.Current?.VehicleInstances);
            originalParkingStates.Clear();
        }

        private void RestoreTrackedParkingStates(IEnumerable<VehicleController>? vehicleControllers)
        {
            if (vehicleControllers == null)
                return;

            foreach (var vehicleController in vehicleControllers)
            {
                var vehicleInstance = vehicleController?.vehicleInstance;
                if (vehicleInstance == null || string.IsNullOrWhiteSpace(vehicleInstance.id))
                    continue;

                RestoreTrackedParkingState(vehicleInstance);
            }
        }

        private void RestoreTrackedParkingStates(IEnumerable<VehicleInstance>? vehicleInstances)
        {
            if (vehicleInstances == null)
                return;

            foreach (var vehicleInstance in vehicleInstances)
            {
                if (vehicleInstance == null || string.IsNullOrWhiteSpace(vehicleInstance.id))
                    continue;

                RestoreTrackedParkingState(vehicleInstance);
            }
        }

        private void RestoreTrackedParkingState(VehicleInstance vehicleInstance)
        {
            if (!originalParkingStates.TryGetValue(vehicleInstance.id, out var originalParkingState))
                return;

            vehicleInstance.parkingState = originalParkingState;
        }
    }
}
