namespace BigHax
{
    public sealed class BigHaxSettings
    {
        public const bool DefaultDisableCasinoBetLimit = false;
        public const bool DefaultDisableIllegalParkingPenalties = false;
        public const bool DefaultDisableInvestmentLimit = false;
        public const int DisabledCasinoBetLimitAmount = 100_000_000;
        public const long DisabledInvestmentLimitAmount = 2_100_000_000L;
        public const bool DefaultEnableVantanderMaxLoanOverride = false;
        public const int VantanderMaximumLoanOverrideAmount = 99_999_999;
        public const int DefaultUiHotkeyIndex = 1;
        public const int DefaultCustomerTrafficMultiplierIndex = 0;
        public const int DefaultBuildingCustomerCapacityMultiplierIndex = 0;
        public const int DefaultFreightTruckT1DeliveryPlaces = 8;
        public const int DefaultStandardFridgeCapacity = 50;
        public const int DefaultPalletShelfCapacity = 60;
        public const int DefaultStorageShelfCapacity = 16;
        public const int ActiveVehicleCapacityOverride = 1000;
        public const int DefaultEmployeeTrainingSkillIncrease = 10;
        public const bool DefaultEnableRecruitmentCandidateMaximumSkill = false;
        public const bool DefaultRemoveEmployeeDemands = false;
        public const bool DefaultEnableMaximumEmployeeSatisfaction = false;
        public const bool DefaultDisablePlayerHungerAndEnergyDecay = false;
        public const bool DefaultDisablePlayerHappinessDecay = false;
        public const bool DefaultEnableInstantImports = false;
        public const bool DefaultEnableInstantFurnitureDeliveries = false;
        public const bool DefaultEnableExtendedBedSleep = false;
        public const bool DefaultEnableNoVehicleDamage = false;
        public const bool DefaultEnableInfiniteVehicleFuel = false;
        public const bool DefaultEnableNeverDirtyVehicles = false;
        public const bool DefaultDisableTraffic = false;
        public const bool DefaultDisableParkedCars = false;
        public const int DefaultInstallationFirmFeePercentage = 100;
        public const bool DefaultEnableMaximumHeadhunterRecruitmentPoints = false;
        public const int MaximumHeadhunterRecruitmentPoints = 1000;
        public const int DefaultHrManagerCapacityIndex = 0;
        public const int RecruitmentCandidateMaximumSkillOverride = 100;

        public static readonly float[] CustomerTrafficMultiplierValues = { 1f, 1.5f, 2f, 3f, 5f, 10f };
        public static readonly float[] BuildingCustomerCapacityMultiplierValues = { 1f, 1.5f, 2f, 3f, 5f, 10f };
        public static readonly int[] HrManagerCapacityValues = { 50, 100, 500, 1000, 10000 };
        public static readonly int[] EmployeeTrainingSkillIncreaseValues = { 10, 20, 30, 50, 100 };
        public static readonly int[] InstallationFirmFeePercentageValues = { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        public static readonly int[] FreightTruckT1DeliveryPlacesValues = { 8, 16, 24, 80, 160 };
        public static readonly int[] StandardFridgeCapacityValues = { 50, 100, 200, 500, 1000 };
        public static readonly int[] PalletShelfCapacityValues = { 60, 120, 240, 500, 1000 };
        public static readonly int[] StorageShelfCapacityValues = { 16, 32, 100, 500, 1000 };

        public bool EnableActiveVehicleCapacityOverride { get; set; } = false;

        public bool DisableCasinoBetLimit { get; set; } = DefaultDisableCasinoBetLimit;

        public bool DisableIllegalParkingPenalties { get; set; } = DefaultDisableIllegalParkingPenalties;

        public bool EnableVantanderMaxLoanOverride { get; set; } = DefaultEnableVantanderMaxLoanOverride;

        public bool DisableInvestmentLimit { get; set; } = DefaultDisableInvestmentLimit;

        public int UiHotkeyIndex { get; set; } = DefaultUiHotkeyIndex;

        public int CustomerTrafficMultiplierIndex { get; set; } = DefaultCustomerTrafficMultiplierIndex;

        public int BuildingCustomerCapacityMultiplierIndex { get; set; } = DefaultBuildingCustomerCapacityMultiplierIndex;

        public int FreightTruckT1DeliveryPlaces { get; set; } = DefaultFreightTruckT1DeliveryPlaces;

        public int StandardFridgeCapacity { get; set; } = DefaultStandardFridgeCapacity;

        public int PalletShelfCapacity { get; set; } = DefaultPalletShelfCapacity;

        public int StorageShelfCapacity { get; set; } = DefaultStorageShelfCapacity;

        public int EmployeeTrainingSkillIncrease { get; set; } = DefaultEmployeeTrainingSkillIncrease;

        public bool EnableRecruitmentCandidateMaximumSkill { get; set; } = DefaultEnableRecruitmentCandidateMaximumSkill;

        public bool RemoveEmployeeDemands { get; set; } = DefaultRemoveEmployeeDemands;

        public bool EnableMaximumEmployeeSatisfaction { get; set; } = DefaultEnableMaximumEmployeeSatisfaction;

        public bool DisablePlayerHungerAndEnergyDecay { get; set; } = DefaultDisablePlayerHungerAndEnergyDecay;

        public bool DisablePlayerHappinessDecay { get; set; } = DefaultDisablePlayerHappinessDecay;

        public bool EnableInstantImports { get; set; } = DefaultEnableInstantImports;

        public bool EnableInstantFurnitureDeliveries { get; set; } = DefaultEnableInstantFurnitureDeliveries;

        public bool EnableExtendedBedSleep { get; set; } = DefaultEnableExtendedBedSleep;

        public bool EnableNoVehicleDamage { get; set; } = DefaultEnableNoVehicleDamage;

        public bool EnableInfiniteVehicleFuel { get; set; } = DefaultEnableInfiniteVehicleFuel;

        public bool EnableNeverDirtyVehicles { get; set; } = DefaultEnableNeverDirtyVehicles;

        public bool DisableTraffic { get; set; } = DefaultDisableTraffic;

        public bool DisableParkedCars { get; set; } = DefaultDisableParkedCars;

        public int InstallationFirmFeePercentage { get; set; } = DefaultInstallationFirmFeePercentage;

        public bool EnableMaximumHeadhunterRecruitmentPoints { get; set; } = DefaultEnableMaximumHeadhunterRecruitmentPoints;

        public int HrManagerCapacityIndex { get; set; } = DefaultHrManagerCapacityIndex;

        public int HrManagerCapacity
        {
            get
            {
                var index = HrManagerCapacityIndex;
                if (index < 0 || index >= HrManagerCapacityValues.Length)
                    index = DefaultHrManagerCapacityIndex;

                return HrManagerCapacityValues[index];
            }
        }

        public float CustomerTrafficMultiplier
        {
            get
            {
                var index = CustomerTrafficMultiplierIndex;
                if (index < 0 || index >= CustomerTrafficMultiplierValues.Length)
                    index = DefaultCustomerTrafficMultiplierIndex;

                return CustomerTrafficMultiplierValues[index];
            }
        }

        public float BuildingCustomerCapacityMultiplier
        {
            get
            {
                var index = BuildingCustomerCapacityMultiplierIndex;
                if (index < 0 || index >= BuildingCustomerCapacityMultiplierValues.Length)
                    index = DefaultBuildingCustomerCapacityMultiplierIndex;

                return BuildingCustomerCapacityMultiplierValues[index];
            }
        }

        public static int GetStepIndex(int[] values, int selectedValue)
        {
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index] == selectedValue)
                    return index;
            }

            return 0;
        }

        public UnityEngine.KeyCode UiHotkey => BigHaxHotkeys.GetKeyCode(UiHotkeyIndex);
    }
}
