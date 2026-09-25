#nullable enable
using System;
using System.Reflection;
using Buildings;
using Helpers;
using Streets;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxInstallationFeeService
    {
        private const float FeeCostPerSquareMeter = 586f;
        private const int DiagnosticCalculationLimit = 12;

        private static int configuredPercentage = BigHaxSettings.DefaultInstallationFirmFeePercentage;
        private static int diagnosticCalculationCount;

        private BigHaxMethodDetour? addressDetour;
        private BigHaxMethodDetour? buildingSizeDetour;

        public void Initialize()
        {
            var helperType = typeof(InteriorInstallationFirmHelper);
            addressDetour = Install(
                helperType.GetMethod(nameof(InteriorInstallationFirmHelper.GetInstallationFee), new[] { typeof(Address) }),
                typeof(BigHaxInstallationFeeService).GetMethod(nameof(GetInstallationFeeForAddress), BindingFlags.Static | BindingFlags.NonPublic),
                "installation fee/address");
            buildingSizeDetour = Install(
                helperType.GetMethod(nameof(InteriorInstallationFirmHelper.GetInstallationFee), new[] { typeof(string) }),
                typeof(BigHaxInstallationFeeService).GetMethod(nameof(GetInstallationFeeForBuildingSize), BindingFlags.Static | BindingFlags.NonPublic),
                "installation fee/building size");
        }

        public void ApplyConfiguredPercentage(BigHaxSettings settings)
        {
            var percentage = Mathf.Clamp(settings.InstallationFirmFeePercentage, 0, 100);
            if (settings.InstallationFirmFeePercentage != percentage)
                settings.InstallationFirmFeePercentage = percentage;

            if (configuredPercentage == percentage)
                return;

            configuredPercentage = percentage;
            diagnosticCalculationCount = 0;
            BigHaxLogger.Diagnostic(
                "Installation firm fee configured: percentage=" + configuredPercentage +
                ", addressDetour=" + (addressDetour?.IsApplied == true) +
                ", buildingSizeDetour=" + (buildingSizeDetour?.IsApplied == true) + ".");
        }

        public void Shutdown()
        {
            Restore(addressDetour, "installation fee/address");
            Restore(buildingSizeDetour, "installation fee/building size");
            addressDetour = null;
            buildingSizeDetour = null;
            configuredPercentage = BigHaxSettings.DefaultInstallationFirmFeePercentage;
            diagnosticCalculationCount = 0;
        }

        private static float GetInstallationFeeForAddress(Address selectedAddress)
        {
            if (selectedAddress == null || selectedAddress.IsUndefined())
                return 0f;

            return CalculateFee(BuildingHelper.GetBuilding(selectedAddress).BuildingSize);
        }

        private static float GetInstallationFeeForBuildingSize(string buildingSize)
        {
            return CalculateFee(buildingSize);
        }

        private static float CalculateFee(string buildingSize)
        {
            var squareMeters = BuildingSizeHelper.GetData(buildingSize).squareMeters;
            var vanillaFee = squareMeters * FeeCostPerSquareMeter;
            var effectiveFee = vanillaFee * configuredPercentage / 100f;
            if (diagnosticCalculationCount < DiagnosticCalculationLimit)
            {
                diagnosticCalculationCount++;
                BigHaxLogger.Diagnostic(
                    "Installation fee calculated: buildingSize=" + buildingSize +
                    ", squareMeters=" + squareMeters +
                    ", vanillaFee=" + vanillaFee +
                    ", percentage=" + configuredPercentage +
                    ", effectiveFee=" + effectiveFee + ".");
            }

            return effectiveFee;
        }

        private static BigHaxMethodDetour? Install(MethodInfo? target, MethodInfo? replacement, string name)
        {
            if (target == null || replacement == null)
            {
                BigHaxLogger.Diagnostic(name + " detour failed: method not found.");
                return null;
            }

            var detour = new BigHaxMethodDetour(target, replacement);
            if (!detour.Apply(out var error))
            {
                BigHaxLogger.Diagnostic(name + " detour failed: " + error);
                return detour;
            }

            BigHaxLogger.Diagnostic(name + " detour installed.");
            return detour;
        }

        private static void Restore(BigHaxMethodDetour? detour, string name)
        {
            if (detour != null && !detour.Restore(out var error))
                BigHaxLogger.Diagnostic(name + " detour restore skipped: " + error);
        }
    }
}
