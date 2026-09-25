#nullable enable
using System;
using System.Collections.Generic;
using Buildings;
using Dialogs;
using Helpers;
using SpecialServices.Bank;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxLoanLimitService
    {
        private readonly List<PatchedSettingsState> patchedStates = new List<PatchedSettingsState>();

        public void InvalidateCache()
        {
        }

        public void ApplyConfiguredLimit(BigHaxSettings settings)
        {
            if (!TryGetCurrentBankSettings(out var bankSettings, out var building, out var currentValue))
                return;

            var state = GetOrCreateState(bankSettings, currentValue);

            if (settings.EnableVantanderMaxLoanOverride)
            {
                if (!Mathf.Approximately(currentValue, BigHaxSettings.VantanderMaximumLoanOverrideAmount))
                {
                    bankSettings.maxTotalLoanAmount = BigHaxSettings.VantanderMaximumLoanOverrideAmount;
                    BigHaxLogger.Diagnostic(
                        "Vantander loan limit applied: address=" + building.Address +
                        ", original=" + state.OriginalValue.ToString("0.###") +
                        ", previous=" + currentValue.ToString("0.###") +
                        ", final=" + bankSettings.maxTotalLoanAmount.ToString("0.###") + ".");
                }

                return;
            }

            if (!Mathf.Approximately(bankSettings.maxTotalLoanAmount, state.OriginalValue))
            {
                state.Restore();
                BigHaxLogger.Diagnostic(
                    "Vantander loan limit restored: address=" + building.Address +
                    ", final=" + bankSettings.maxTotalLoanAmount.ToString("0.###") + ".");
            }
        }

        public void RestoreOriginalLimit()
        {
            foreach (var state in patchedStates)
                state.Restore();
        }

        private PatchedSettingsState GetOrCreateState(BankSettings bankSettings, float currentValue)
        {
            foreach (var state in patchedStates)
            {
                if (ReferenceEquals(state.Settings, bankSettings))
                    return state;
            }

            var createdState = new PatchedSettingsState(bankSettings, currentValue);
            patchedStates.Add(createdState);
            BigHaxLogger.Diagnostic(
                "Bank settings detected: originalMaximumLoan=" + currentValue.ToString("0.###") + ".");
            return createdState;
        }

        private static bool TryGetCurrentBankSettings(out BankSettings bankSettings, out Building building, out float currentValue)
        {
            bankSettings = null!;
            building = null!;
            currentValue = 0f;

            var dialogController = DialogController.current;
            if (dialogController == null || dialogController.contact == null)
                return false;

            try
            {
                building = BuildingHelper.GetBuilding(dialogController.contact.Address);
            }
            catch
            {
                return false;
            }

            if (building == null || building.SpecialService == null)
                return false;

            // BankDialog itself identifies Jensen Capital by this registration
            // name and treats the other bank as Vantander. Mirror that rule so
            // the Vantander cheat cannot alter Jensen's separate loan limit.
            var registration = BuildingHelper.GetBuildingRegistration(dialogController.contact.Address);
            if (registration != null && string.Equals(registration.BusinessName, "Jensen Capital", StringComparison.Ordinal))
                return false;

            if (building.SpecialService.settings is not BankSettings resolvedSettings)
                return false;

            bankSettings = resolvedSettings;
            currentValue = resolvedSettings.maxTotalLoanAmount;
            return true;
        }

        private sealed class PatchedSettingsState
        {
            public PatchedSettingsState(BankSettings settings, float originalValue)
            {
                Settings = settings;
                OriginalValue = originalValue;
            }

            public BankSettings Settings { get; }
            public float OriginalValue { get; }

            public void Restore()
            {
                Settings.maxTotalLoanAmount = OriginalValue;
            }
        }
    }
}
