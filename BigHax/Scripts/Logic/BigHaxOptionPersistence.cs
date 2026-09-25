#nullable enable
namespace BigHax
{
    internal static class BigHaxOptionPersistence
    {
        public static void LoadIntoSettings(string modId, BigHaxSettings settings)
        {
            var migratedStepValues = false;
            settings.UiHotkeyIndex = LoadInt(
                modId,
                BigHaxOptionIds.UiToggleHotkey,
                BigHaxSettings.DefaultUiHotkeyIndex);
            settings.DisableCasinoBetLimit = LoadBool(
                modId,
                BigHaxOptionIds.DisableCasinoBetLimit,
                BigHaxSettings.DefaultDisableCasinoBetLimit);
            settings.DisableIllegalParkingPenalties = LoadBool(
                modId,
                BigHaxOptionIds.DisableIllegalParkingPenalties,
                BigHaxSettings.DefaultDisableIllegalParkingPenalties);
            settings.CustomerTrafficMultiplierIndex = LoadCustomerTrafficMultiplierIndex(modId);
            settings.BuildingCustomerCapacityMultiplierIndex = LoadInt(
                modId,
                BigHaxOptionIds.BuildingCustomerCapacityMultiplier,
                BigHaxSettings.DefaultBuildingCustomerCapacityMultiplierIndex);
            settings.DisableInvestmentLimit = LoadDisableInvestmentLimit(modId);
            settings.EnableVantanderMaxLoanOverride = LoadBool(
                modId,
                BigHaxOptionIds.EnableVantanderMaxLoanOverride,
                BigHaxSettings.DefaultEnableVantanderMaxLoanOverride);

            settings.StandardFridgeCapacity = LoadSteppedInt(
                modId,
                BigHaxOptionIds.StandardFridgeCapacity,
                BigHaxSettings.DefaultStandardFridgeCapacity,
                BigHaxSettings.StandardFridgeCapacityValues,
                roundTowardLowerValue: false,
                ref migratedStepValues);

            settings.PalletShelfCapacity = LoadSteppedInt(
                modId,
                BigHaxOptionIds.PalletShelfCapacity,
                BigHaxSettings.DefaultPalletShelfCapacity,
                BigHaxSettings.PalletShelfCapacityValues,
                roundTowardLowerValue: false,
                ref migratedStepValues);
            settings.StorageShelfCapacity = LoadSteppedInt(
                modId,
                BigHaxOptionIds.StorageShelfCapacity,
                BigHaxSettings.DefaultStorageShelfCapacity,
                BigHaxSettings.StorageShelfCapacityValues,
                roundTowardLowerValue: false,
                ref migratedStepValues);

            settings.EmployeeTrainingSkillIncrease = LoadSteppedInt(
                modId,
                BigHaxOptionIds.EmployeeTrainingSkillIncrease,
                BigHaxSettings.DefaultEmployeeTrainingSkillIncrease,
                BigHaxSettings.EmployeeTrainingSkillIncreaseValues,
                roundTowardLowerValue: false,
                ref migratedStepValues);

            settings.EnableRecruitmentCandidateMaximumSkill = LoadEnableRecruitmentCandidateMaximumSkill(modId);
            settings.RemoveEmployeeDemands = LoadBool(
                modId,
                BigHaxOptionIds.RemoveEmployeeDemands,
                BigHaxSettings.DefaultRemoveEmployeeDemands);
            settings.EnableMaximumEmployeeSatisfaction = LoadBool(
                modId,
                BigHaxOptionIds.EnableMaximumEmployeeSatisfaction,
                BigHaxSettings.DefaultEnableMaximumEmployeeSatisfaction);
            settings.DisablePlayerHungerAndEnergyDecay = LoadBool(
                modId,
                BigHaxOptionIds.DisablePlayerHungerAndEnergyDecay,
                BigHaxSettings.DefaultDisablePlayerHungerAndEnergyDecay);
            settings.DisablePlayerHappinessDecay = LoadBool(
                modId,
                BigHaxOptionIds.DisablePlayerHappinessDecay,
                BigHaxSettings.DefaultDisablePlayerHappinessDecay);
            settings.EnableInstantImports = LoadBool(
                modId,
                BigHaxOptionIds.EnableInstantImports,
                BigHaxSettings.DefaultEnableInstantImports);
            settings.EnableInstantFurnitureDeliveries = LoadBool(
                modId,
                BigHaxOptionIds.EnableInstantFurnitureDeliveries,
                BigHaxSettings.DefaultEnableInstantFurnitureDeliveries);
            settings.EnableExtendedBedSleep = LoadBool(
                modId,
                BigHaxOptionIds.EnableExtendedBedSleep,
                BigHaxSettings.DefaultEnableExtendedBedSleep);
            settings.EnableNoVehicleDamage = LoadBool(modId, BigHaxOptionIds.EnableNoVehicleDamage, BigHaxSettings.DefaultEnableNoVehicleDamage);
            settings.EnableInfiniteVehicleFuel = LoadBool(modId, BigHaxOptionIds.EnableInfiniteVehicleFuel, BigHaxSettings.DefaultEnableInfiniteVehicleFuel);
            settings.EnableNeverDirtyVehicles = LoadBool(modId, BigHaxOptionIds.EnableNeverDirtyVehicles, BigHaxSettings.DefaultEnableNeverDirtyVehicles);
            settings.DisableTraffic = LoadBool(modId, BigHaxOptionIds.DisableTraffic, BigHaxSettings.DefaultDisableTraffic);
            settings.DisableParkedCars = LoadBool(modId, BigHaxOptionIds.DisableParkedCars, BigHaxSettings.DefaultDisableParkedCars);
            settings.InstallationFirmFeePercentage = LoadSteppedInt(
                modId,
                BigHaxOptionIds.InstallationFirmFeePercentage,
                BigHaxSettings.DefaultInstallationFirmFeePercentage,
                BigHaxSettings.InstallationFirmFeePercentageValues,
                roundTowardLowerValue: true,
                ref migratedStepValues);
            settings.EnableMaximumHeadhunterRecruitmentPoints = LoadBool(
                modId,
                BigHaxOptionIds.EnableMaximumHeadhunterRecruitmentPoints,
                BigHaxSettings.DefaultEnableMaximumHeadhunterRecruitmentPoints);
            settings.HrManagerCapacityIndex = UnityEngine.Mathf.Clamp(
                LoadInt(modId, BigHaxOptionIds.HrManagerCapacity, BigHaxSettings.DefaultHrManagerCapacityIndex),
                0,
                BigHaxSettings.HrManagerCapacityValues.Length - 1);

            settings.FreightTruckT1DeliveryPlaces = LoadSteppedInt(
                modId,
                BigHaxOptionIds.FreightTruckT1DeliveryPlaces,
                BigHaxSettings.DefaultFreightTruckT1DeliveryPlaces,
                BigHaxSettings.FreightTruckT1DeliveryPlacesValues,
                roundTowardLowerValue: false,
                ref migratedStepValues);

            settings.EnableActiveVehicleCapacityOverride = LoadBool(
                modId,
                BigHaxOptionIds.ActiveVehicleCapacityEnabled,
                false);

            if (settings.CustomerTrafficMultiplierIndex < 0 ||
                settings.CustomerTrafficMultiplierIndex >= BigHaxSettings.CustomerTrafficMultiplierValues.Length)
            {
                settings.CustomerTrafficMultiplierIndex = BigHaxSettings.DefaultCustomerTrafficMultiplierIndex;
            }

            if (settings.BuildingCustomerCapacityMultiplierIndex < 0 ||
                settings.BuildingCustomerCapacityMultiplierIndex >= BigHaxSettings.BuildingCustomerCapacityMultiplierValues.Length)
            {
                settings.BuildingCustomerCapacityMultiplierIndex = BigHaxSettings.DefaultBuildingCustomerCapacityMultiplierIndex;
            }

            settings.UiHotkeyIndex = BigHaxHotkeys.ClampIndex(settings.UiHotkeyIndex);
            if (migratedStepValues)
                UnityEngine.PlayerPrefs.Save();
        }

        public static void SaveCustomerTrafficMultiplierIndex(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.CustomerTrafficMultiplier, value);
        }

        public static void SaveBuildingCustomerCapacityMultiplierIndex(string modId, int value)
        {
            SaveInt(
                modId,
                BigHaxOptionIds.BuildingCustomerCapacityMultiplier,
                UnityEngine.Mathf.Clamp(value, 0, BigHaxSettings.BuildingCustomerCapacityMultiplierValues.Length - 1));
        }

        public static void SaveDisableCasinoBetLimit(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.DisableCasinoBetLimit, value);
        }

        public static void SaveDisableIllegalParkingPenalties(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.DisableIllegalParkingPenalties, value);
        }

        public static void SaveStandardFridgeCapacity(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.StandardFridgeCapacity, value);
        }

        public static void SaveDisableInvestmentLimit(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.DisableInvestmentLimit, value);
        }

        public static void SavePalletShelfCapacity(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.PalletShelfCapacity, value);
        }

        public static void SaveStorageShelfCapacity(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.StorageShelfCapacity, value);
        }

        public static void SaveEnableVantanderMaxLoanOverride(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.EnableVantanderMaxLoanOverride, value);
        }

        public static void SaveEmployeeTrainingSkillIncrease(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.EmployeeTrainingSkillIncrease, value);
        }

        public static void SaveEnableRecruitmentCandidateMaximumSkill(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.EnableRecruitmentCandidateMaximumSkill, value);
        }

        public static void SaveRemoveEmployeeDemands(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.RemoveEmployeeDemands, value);
        }

        public static void SaveEnableMaximumEmployeeSatisfaction(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.EnableMaximumEmployeeSatisfaction, value);
        }

        public static void SaveDisablePlayerHungerAndEnergyDecay(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.DisablePlayerHungerAndEnergyDecay, value);
        }

        public static void SaveDisablePlayerHappinessDecay(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.DisablePlayerHappinessDecay, value);
        }

        public static void SaveEnableInstantImports(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.EnableInstantImports, value);
        }

        public static void SaveEnableInstantFurnitureDeliveries(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.EnableInstantFurnitureDeliveries, value);
        }

        public static void SaveEnableExtendedBedSleep(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.EnableExtendedBedSleep, value);
        }

        public static void SaveEnableNoVehicleDamage(string modId, bool value) => SaveBool(modId, BigHaxOptionIds.EnableNoVehicleDamage, value);

        public static void SaveEnableInfiniteVehicleFuel(string modId, bool value) => SaveBool(modId, BigHaxOptionIds.EnableInfiniteVehicleFuel, value);

        public static void SaveEnableNeverDirtyVehicles(string modId, bool value) => SaveBool(modId, BigHaxOptionIds.EnableNeverDirtyVehicles, value);

        public static void SaveDisableTraffic(string modId, bool value) => SaveBool(modId, BigHaxOptionIds.DisableTraffic, value);

        public static void SaveDisableParkedCars(string modId, bool value) => SaveBool(modId, BigHaxOptionIds.DisableParkedCars, value);

        public static void SaveInstallationFirmFeePercentage(string modId, int value) => SaveInt(modId, BigHaxOptionIds.InstallationFirmFeePercentage, UnityEngine.Mathf.Clamp(value, 0, 100));

        public static void SaveEnableMaximumHeadhunterRecruitmentPoints(string modId, bool value) => SaveBool(modId, BigHaxOptionIds.EnableMaximumHeadhunterRecruitmentPoints, value);

        public static void SaveHrManagerCapacityIndex(string modId, int value) => SaveInt(
            modId,
            BigHaxOptionIds.HrManagerCapacity,
            UnityEngine.Mathf.Clamp(value, 0, BigHaxSettings.HrManagerCapacityValues.Length - 1));

        public static void SaveFreightTruckT1DeliveryPlaces(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.FreightTruckT1DeliveryPlaces, value);
        }

        public static void SaveActiveVehicleCapacityEnabled(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.ActiveVehicleCapacityEnabled, value);
        }

        public static void SaveUiHotkeyIndex(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.UiToggleHotkey, value);
        }

        public static int LoadUpdateNoticeSeenVersion(string modId)
        {
            return LoadInt(modId, BigHaxOptionIds.UpdateNoticeSeenVersion, 0);
        }

        public static void SaveUpdateNoticeSeenVersion(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.UpdateNoticeSeenVersion, value);
        }

        private static int LoadInt(string modId, string optionId, int defaultValue)
        {
            var key = BuildKey(modId, optionId);
            return UnityEngine.PlayerPrefs.HasKey(key) ? UnityEngine.PlayerPrefs.GetInt(key) : defaultValue;
        }

        private static int LoadSteppedInt(
            string modId,
            string optionId,
            int defaultValue,
            int[] values,
            bool roundTowardLowerValue,
            ref bool migrated)
        {
            var key = BuildKey(modId, optionId);
            if (!UnityEngine.PlayerPrefs.HasKey(key))
                return defaultValue;

            var storedValue = UnityEngine.PlayerPrefs.GetInt(key);
            var steppedValue = SnapToStep(storedValue, values, roundTowardLowerValue);
            if (steppedValue == storedValue)
                return steppedValue;

            UnityEngine.PlayerPrefs.SetInt(key, steppedValue);
            migrated = true;
            return steppedValue;
        }

        private static int SnapToStep(int value, int[] values, bool roundTowardLowerValue)
        {
            var selected = values[0];
            var selectedDistance = long.MaxValue;

            for (var index = 0; index < values.Length; index++)
            {
                var candidate = values[index];
                if (roundTowardLowerValue && candidate > value)
                    continue;

                if (!roundTowardLowerValue && candidate < value)
                    continue;

                var distance = System.Math.Abs((long)candidate - value);
                if (distance < selectedDistance)
                {
                    selected = candidate;
                    selectedDistance = distance;
                }
            }

            if (selectedDistance != long.MaxValue)
                return selected;

            selected = values[0];
            for (var index = 0; index < values.Length; index++)
            {
                if (roundTowardLowerValue && values[index] < selected)
                    selected = values[index];
                else if (!roundTowardLowerValue && values[index] > selected)
                    selected = values[index];
            }

            return selected;
        }

        private static int LoadCustomerTrafficMultiplierIndex(string modId)
        {
            var currentKey = BuildKey(modId, BigHaxOptionIds.CustomerTrafficMultiplier);
            if (UnityEngine.PlayerPrefs.HasKey(currentKey))
                return UnityEngine.PlayerPrefs.GetInt(currentKey);

            var legacyKey = BuildKey(modId, BigHaxOptionIds.LegacyCustomerTrafficMultiplier);
            if (!UnityEngine.PlayerPrefs.HasKey(legacyKey))
                return BigHaxSettings.DefaultCustomerTrafficMultiplierIndex;

            var legacyValue = UnityEngine.PlayerPrefs.GetInt(legacyKey);
            return MapLegacyCustomerTrafficMultiplierToIndex(legacyValue);
        }

        private static bool LoadDisableInvestmentLimit(string modId)
        {
            var currentKey = BuildKey(modId, BigHaxOptionIds.DisableInvestmentLimit);
            if (UnityEngine.PlayerPrefs.HasKey(currentKey))
                return UnityEngine.PlayerPrefs.GetInt(currentKey) != 0;

            var hundredsMillionsKey = BuildKey(modId, BigHaxOptionIds.MaximumInvestmentHundredsMillions);
            if (UnityEngine.PlayerPrefs.HasKey(hundredsMillionsKey))
                return UnityEngine.PlayerPrefs.GetInt(hundredsMillionsKey) > 10;

            var legacyKey = BuildKey(modId, BigHaxOptionIds.LegacyMaximumInvestmentBillions);
            if (!UnityEngine.PlayerPrefs.HasKey(legacyKey))
                return BigHaxSettings.DefaultDisableInvestmentLimit;

            var legacyBillions = UnityEngine.PlayerPrefs.GetInt(legacyKey);
            return legacyBillions > 1;
        }

        private static bool LoadEnableRecruitmentCandidateMaximumSkill(string modId)
        {
            var currentKey = BuildKey(modId, BigHaxOptionIds.EnableRecruitmentCandidateMaximumSkill);
            if (UnityEngine.PlayerPrefs.HasKey(currentKey))
                return UnityEngine.PlayerPrefs.GetInt(currentKey) != 0;

            var legacyKey = BuildKey(modId, BigHaxOptionIds.LegacyRecruitmentCandidateMaximumSkill);
            return UnityEngine.PlayerPrefs.HasKey(legacyKey) &&
                   UnityEngine.PlayerPrefs.GetInt(legacyKey) >= BigHaxSettings.RecruitmentCandidateMaximumSkillOverride;
        }

        private static int MapLegacyCustomerTrafficMultiplierToIndex(int legacyValue)
        {
            return legacyValue switch
            {
                <= 1 => 0,
                2 => 2,
                3 => 3,
                4 => 3,
                _ => 4
            };
        }

        private static bool LoadBool(string modId, string optionId, bool defaultValue)
        {
            var key = BuildKey(modId, optionId);
            if (!UnityEngine.PlayerPrefs.HasKey(key))
                return defaultValue;

            return UnityEngine.PlayerPrefs.GetInt(key) != 0;
        }

        private static void SaveInt(string modId, string optionId, int value)
        {
            var key = BuildKey(modId, optionId);
            UnityEngine.PlayerPrefs.SetInt(key, value);
            UnityEngine.PlayerPrefs.Save();
        }

        private static void SaveBool(string modId, string optionId, bool value)
        {
            var key = BuildKey(modId, optionId);
            UnityEngine.PlayerPrefs.SetInt(key, value ? 1 : 0);
            UnityEngine.PlayerPrefs.Save();
        }

        private static string BuildKey(string modId, string optionId)
        {
            return "m:" + modId + ":" + optionId;
        }
    }
}
