#nullable enable
using BAModAPI;
using Localizor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BigHax
{
    /// <summary>
    /// Optional BAUnifiedUI-backed screen. All library calls are reflection-only so
    /// Big Hax remains loadable when the separate Workshop dependency is absent.
    /// </summary>
    internal sealed class BigHaxBaUnifiedOptionsUi
    {
        private const string MinimumLibraryVersion = "1.0.1";
        private const string RootName = "BigHax_BAUnifiedUI_Options";
        private const float PanelWidth = 920f;
        private const float PanelHeight = 760f;
        private const float HeaderScale = 1f;

        private readonly BaUiReflection api;
        private readonly Action close;
        private readonly Action? confirmUnlockAllContacts;
        private readonly Action? confirmUnlockAllCourses;
        private readonly Func<bool>? arePurchaseLimitsDisabled;
        private readonly Action<bool>? setPurchaseLimitsDisabled;
        private GameObject? root;
        private RectTransform? content;
        private ModContext? context;
        private BigHaxSettings? settings;
        private bool layoutPrimed;
        private bool rebuildForLanguageChange;
        private string localizationSignature = string.Empty;

        private BigHaxBaUnifiedOptionsUi(
            BaUiReflection api,
            Action close,
            Action? unlockAllContacts,
            Action? unlockAllCourses,
            Func<bool>? readPurchaseLimitsDisabled,
            Action<bool>? setPurchaseLimitsDisabled)
        {
            this.api = api;
            this.close = close;
            confirmUnlockAllContacts = unlockAllContacts;
            confirmUnlockAllCourses = unlockAllCourses;
            arePurchaseLimitsDisabled = readPurchaseLimitsDisabled;
            this.setPurchaseLimitsDisabled = setPurchaseLimitsDisabled;
            LocalizorManager.OnLanguageChanged += HandleLanguageChanged;
        }

        public string LibraryVersion => api.LibraryVersion;
        public string AssemblyName => api.AssemblyName;

        public static bool IsWaitingForNativeOptions(string reason) =>
            reason.IndexOf("native option prefabs are not loaded yet", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsWaitingForLibrary(string reason) =>
            reason.IndexOf("LIB_BaUnifiedUI is not loaded", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool TryCreate(
            ModContext context,
            BigHaxSettings settings,
            bool visible,
            Action close,
            Action? unlockAllContacts,
            Action? unlockAllCourses,
            Func<bool>? readPurchaseLimitsDisabled,
            Action<bool>? setPurchaseLimitsDisabled,
            out BigHaxBaUnifiedOptionsUi? ui,
            out string reason)
        {
            ui = null;
            if (!BaUiReflection.TryResolve(MinimumLibraryVersion, out var api, out reason))
                return false;

            if (!api!.AreNativeOptionsReady())
            {
                reason = "Big Ambitions' native option prefabs are not loaded yet.";
                return false;
            }

            var candidate = new BigHaxBaUnifiedOptionsUi(
                api,
                close,
                unlockAllContacts,
                unlockAllCourses,
                readPurchaseLimitsDisabled,
                setPurchaseLimitsDisabled);
            try
            {
                candidate.EnsureCreated(context, settings, visible);
                ui = candidate;
                reason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                candidate.Destroy();
                reason = "BAUnifiedUI initialization failed: " + exception.GetBaseException().Message;
                return false;
            }
        }

        public void EnsureCreated(ModContext modContext, BigHaxSettings currentSettings, bool visible)
        {
            context = modContext;
            settings = currentSettings;
            if (root == null)
                Build();

            PrimeLayoutIfNeeded();
            SetVisible(visible);
        }

        public void SetVisible(bool visible)
        {
            if (visible && rebuildForLanguageChange)
            {
                DestroyVisuals();
                Build();
                PrimeLayoutIfNeeded();
            }

            if (root == null || root.activeSelf == visible)
                return;

            root.SetActive(visible);
        }

        public void ConsumeGameplayInputIfNeeded(bool visible)
        {
            if (visible)
                Input.ResetInputAxes();
        }

        public void Destroy()
        {
            LocalizorManager.OnLanguageChanged -= HandleLanguageChanged;
            DestroyVisuals();
            context = null;
            settings = null;
        }

        private void Build()
        {
            if (context == null || settings == null)
                throw new InvalidOperationException("Big Hax UI context is not initialized.");

            api.EnsureEventSystem();

            root = new GameObject(RootName, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(root);
            api.SetupOverlayCanvas(root, short.MaxValue - 8, interactive: true);
            api.CreateModalDimmer(root.transform, 0.62f, new UnityAction(close));

            var panel = api.BuildPanel(root.transform, PanelWidth, PanelHeight, "BigHaxOptions", out var header);
            api.CreateHeaderTitle(header, Localize("bighax_options_header"), HeaderScale, rightIconCount: 1);
            api.CreateHeaderCloseButton(header, new UnityAction(close));
            api.TryAttachDraggableWindow(panel, header, "BigHax.Options.BAUnifiedUI");

            var viewportObject = new GameObject("OptionsViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewportObject.transform.SetParent(panel, false);
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(28f, 64f);
            viewport.offsetMax = new Vector2(-44f, -56f);
            var viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(viewport, false);
            content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 12, 12);
            layout.spacing = 0f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            var fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollbar = CreateScrollbar(panel);
            var scroll = viewportObject.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 34f;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            AddCategory(Localize("bighax_category_money"));
            AddToggle(Localize("bighax_disable_casino_bet_limit_label"), () => settings.DisableCasinoBetLimit, value => { settings.DisableCasinoBetLimit = value; BigHaxOptionPersistence.SaveDisableCasinoBetLimit(context.ModId, value); });
            AddToggle(Localize("bighax_disable_investment_limit_label"), () => settings.DisableInvestmentLimit, value => { settings.DisableInvestmentLimit = value; BigHaxOptionPersistence.SaveDisableInvestmentLimit(context.ModId, value); });
            AddToggle(Localize("bighax_vantander_maximum_loan_override_label"), () => settings.EnableVantanderMaxLoanOverride, value => { settings.EnableVantanderMaxLoanOverride = value; BigHaxOptionPersistence.SaveEnableVantanderMaxLoanOverride(context.ModId, value); });
            AddSeparator();

            AddCategory(Localize("bighax_category_employee"));
            AddToggle(Localize("bighax_enable_recruitment_candidate_maximum_skill_label"), () => settings.EnableRecruitmentCandidateMaximumSkill, value => { settings.EnableRecruitmentCandidateMaximumSkill = value; BigHaxOptionPersistence.SaveEnableRecruitmentCandidateMaximumSkill(context.ModId, value); });
            AddToggle(Localize("bighax_remove_employee_demands_label"), () => settings.RemoveEmployeeDemands, value => { settings.RemoveEmployeeDemands = value; BigHaxOptionPersistence.SaveRemoveEmployeeDemands(context.ModId, value); });
            AddToggle(Localize("bighax_maximum_employee_satisfaction_label"), () => settings.EnableMaximumEmployeeSatisfaction, value => { settings.EnableMaximumEmployeeSatisfaction = value; BigHaxOptionPersistence.SaveEnableMaximumEmployeeSatisfaction(context.ModId, value); });
            AddToggle(Localize("bighax_maximum_headhunter_recruitment_points_label"), () => settings.EnableMaximumHeadhunterRecruitmentPoints, value => { settings.EnableMaximumHeadhunterRecruitmentPoints = value; BigHaxOptionPersistence.SaveEnableMaximumHeadhunterRecruitmentPoints(context.ModId, value); });
            AddSlider(
                Localize("bighax_maximum_hr_manager_capacity_label"),
                () => settings.HrManagerCapacityIndex,
                0,
                BigHaxSettings.HrManagerCapacityValues.Length - 1,
                value => { settings.HrManagerCapacityIndex = value; BigHaxOptionPersistence.SaveHrManagerCapacityIndex(context.ModId, value); },
                value => BigHaxSettings.HrManagerCapacityValues[value].ToString());
            AddSteppedSlider(Localize("bighax_employee_training_skill_increase_label"), () => settings.EmployeeTrainingSkillIncrease, BigHaxSettings.EmployeeTrainingSkillIncreaseValues, value => { settings.EmployeeTrainingSkillIncrease = value; BigHaxOptionPersistence.SaveEmployeeTrainingSkillIncrease(context.ModId, value); }, value => value.ToString());
            AddSeparator();

            AddCategory(Localize("bighax_category_player"));
            AddToggle(Localize("bighax_disable_player_hunger_and_energy_decay_label"), () => settings.DisablePlayerHungerAndEnergyDecay, value => { settings.DisablePlayerHungerAndEnergyDecay = value; BigHaxOptionPersistence.SaveDisablePlayerHungerAndEnergyDecay(context.ModId, value); });
            AddToggle(Localize("bighax_disable_player_happiness_decay_label"), () => settings.DisablePlayerHappinessDecay, value => { settings.DisablePlayerHappinessDecay = value; BigHaxOptionPersistence.SaveDisablePlayerHappinessDecay(context.ModId, value); });
            AddSeparator();

            AddCategory(Localize("bighax_category_unlock"));
            AddActionButton(Localize("bighax_unlock_all_contacts_button"), confirmUnlockAllContacts);
            AddActionButton(Localize("bighax_unlock_all_courses_button"), confirmUnlockAllCourses);
            AddSeparator();

            AddCategory(Localize("bighax_category_business"));
            AddToggle(Localize("bighax_enable_instant_imports_label"), () => settings.EnableInstantImports, value => { settings.EnableInstantImports = value; BigHaxOptionPersistence.SaveEnableInstantImports(context.ModId, value); });
            AddToggle(Localize("bighax_enable_instant_furniture_deliveries_label"), () => settings.EnableInstantFurnitureDeliveries, value => { settings.EnableInstantFurnitureDeliveries = value; BigHaxOptionPersistence.SaveEnableInstantFurnitureDeliveries(context.ModId, value); });
            if (arePurchaseLimitsDisabled != null && setPurchaseLimitsDisabled != null)
                AddToggle(Localize("bighax_disable_purchase_limits_label"), arePurchaseLimitsDisabled, setPurchaseLimitsDisabled);
            AddSteppedSlider(Localize("bighax_installation_firm_fee_percentage_label"), () => settings.InstallationFirmFeePercentage, BigHaxSettings.InstallationFirmFeePercentageValues, value => { settings.InstallationFirmFeePercentage = value; BigHaxOptionPersistence.SaveInstallationFirmFeePercentage(context.ModId, value); }, value => value + "%");
            AddSlider(Localize("bighax_customer_traffic_multiplier_label"), () => settings.CustomerTrafficMultiplierIndex, 0, 5, value => { settings.CustomerTrafficMultiplierIndex = value; BigHaxOptionPersistence.SaveCustomerTrafficMultiplierIndex(context.ModId, value); }, value => new[] { "1x", "1.5x", "2x", "3x", "5x", "10x" }[value]);
            AddSlider(Localize("bighax_building_customer_capacity_multiplier_label"), () => settings.BuildingCustomerCapacityMultiplierIndex, 0, 5, value => { settings.BuildingCustomerCapacityMultiplierIndex = value; BigHaxOptionPersistence.SaveBuildingCustomerCapacityMultiplierIndex(context.ModId, value); }, value => new[] { "1x", "1.5x", "2x", "3x", "5x", "10x" }[value]);
            AddSeparator();

            AddCategory(Localize("bighax_category_vehicle"));
            AddToggle(Localize("bighax_disable_illegal_parking_penalties_label"), () => settings.DisableIllegalParkingPenalties, value => { settings.DisableIllegalParkingPenalties = value; BigHaxOptionPersistence.SaveDisableIllegalParkingPenalties(context.ModId, value); });
            AddToggle(Localize("bighax_no_vehicle_damage_label"), () => settings.EnableNoVehicleDamage, value => { settings.EnableNoVehicleDamage = value; BigHaxOptionPersistence.SaveEnableNoVehicleDamage(context.ModId, value); });
            AddToggle(Localize("bighax_infinite_vehicle_fuel_label"), () => settings.EnableInfiniteVehicleFuel, value => { settings.EnableInfiniteVehicleFuel = value; BigHaxOptionPersistence.SaveEnableInfiniteVehicleFuel(context.ModId, value); });
            AddToggle(Localize("bighax_never_dirty_vehicles_label"), () => settings.EnableNeverDirtyVehicles, value => { settings.EnableNeverDirtyVehicles = value; BigHaxOptionPersistence.SaveEnableNeverDirtyVehicles(context.ModId, value); });
            AddToggle(Localize("bighax_disable_traffic_label"), () => settings.DisableTraffic, value => { settings.DisableTraffic = value; BigHaxOptionPersistence.SaveDisableTraffic(context.ModId, value); });
            AddToggle(Localize("bighax_disable_parked_cars_label"), () => settings.DisableParkedCars, value => { settings.DisableParkedCars = value; BigHaxOptionPersistence.SaveDisableParkedCars(context.ModId, value); });
            AddSteppedSlider(Localize("bighax_freight_truck_delivery_places_label", new Dictionary<string, string> { { "vehicleName", Localize("ba:vehicletype_freighttruckt1") } }), () => settings.FreightTruckT1DeliveryPlaces, BigHaxSettings.FreightTruckT1DeliveryPlacesValues, value => { settings.FreightTruckT1DeliveryPlaces = value; BigHaxOptionPersistence.SaveFreightTruckT1DeliveryPlaces(context.ModId, value); }, value => value.ToString());
            AddSeparator();

            AddCategory(Localize("bighax_category_capacity"));
            AddToggle(Localize("bighax_active_vehicle_enabled_label"), () => settings.EnableActiveVehicleCapacityOverride, value => { settings.EnableActiveVehicleCapacityOverride = value; BigHaxOptionPersistence.SaveActiveVehicleCapacityEnabled(context.ModId, value); });
            AddSteppedSlider(Localize("bighax_standard_fridge_capacity_label", new Dictionary<string, string> { { "itemName", Localize("ba:itemname_standardfridge") } }),
                () => settings.StandardFridgeCapacity, BigHaxSettings.StandardFridgeCapacityValues,
                value => { settings.StandardFridgeCapacity = value; BigHaxOptionPersistence.SaveStandardFridgeCapacity(context.ModId, value); }, value => value.ToString());
            AddSteppedSlider(Localize("bighax_pallet_shelf_capacity_label", new Dictionary<string, string> { { "itemName", Localize("ba:itemname_palletshelf") } }),
                () => settings.PalletShelfCapacity, BigHaxSettings.PalletShelfCapacityValues,
                value => { settings.PalletShelfCapacity = value; BigHaxOptionPersistence.SavePalletShelfCapacity(context.ModId, value); }, value => value.ToString());
            AddSteppedSlider(Localize("bighax_storage_shelf_capacity_label", new Dictionary<string, string> { { "itemName", Localize("ba:itemname_storageshelf") } }),
                () => settings.StorageShelfCapacity, BigHaxSettings.StorageShelfCapacityValues,
                value => { settings.StorageShelfCapacity = value; BigHaxOptionPersistence.SaveStorageShelfCapacity(context.ModId, value); }, value => value.ToString());
            AddSeparator();

            AddCategory(Localize("bighax_category_time"));
            AddToggle(Localize("bighax_enable_extended_bed_sleep_label"), () => settings.EnableExtendedBedSleep, value => { settings.EnableExtendedBedSleep = value; BigHaxOptionPersistence.SaveEnableExtendedBedSleep(context.ModId, value); });
            AddSeparator();

            AddFeedbackLabel();
            AddFeedbackButtons();

            AddFooter(panel);
            api.ApplyUiLayer(root);
            root.SetActive(false);
            layoutPrimed = false;
            rebuildForLanguageChange = false;
            localizationSignature = ComputeLocalizationSignature();
        }

        private void PrimeLayoutIfNeeded()
        {
            if (layoutPrimed || root == null || content == null)
                return;

            var wasActive = root.activeSelf;
            if (!wasActive)
                root.SetActive(true);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            content.anchoredPosition = Vector2.zero;
            layoutPrimed = true;

            if (!wasActive)
                root.SetActive(false);
        }

        private void HandleLanguageChanged()
        {
            if (root == null || context == null || settings == null)
                return;

            // Big Ambitions raises a language event during startup even when the
            // localized strings used by this screen have not changed. Rebuilding on
            // the next F5 made that first open pay the full native-prefab cost again.
            if (string.Equals(localizationSignature, ComputeLocalizationSignature(), StringComparison.Ordinal))
                return;

            var wasVisible = root.activeSelf;
            try
            {
                DestroyVisuals();
                Build();
                PrimeLayoutIfNeeded();
                if (wasVisible && root != null)
                    root.SetActive(true);
            }
            catch (Exception exception)
            {
                rebuildForLanguageChange = true;
                context.Logger.Error(exception);
            }
        }

        private static string ComputeLocalizationSignature()
        {
            return string.Join("\u001f", new[]
            {
                Localize("bighax_options_header"),
                Localize("bighax_category_money"),
                Localize("bighax_category_employee"),
                Localize("bighax_category_player"),
                Localize("bighax_category_unlock"),
                Localize("bighax_category_business"),
                Localize("bighax_category_vehicle"),
                Localize("bighax_category_capacity"),
                Localize("bighax_category_time"),
                Localize("bighax_disable_casino_bet_limit_label"),
                Localize("bighax_disable_illegal_parking_penalties_label"),
                Localize("bighax_disable_investment_limit_label"),
                Localize("bighax_vantander_maximum_loan_override_label"),
                Localize("bighax_enable_recruitment_candidate_maximum_skill_label"),
                Localize("bighax_remove_employee_demands_label"),
                Localize("bighax_maximum_employee_satisfaction_label"),
                Localize("bighax_maximum_headhunter_recruitment_points_label"),
                Localize("bighax_maximum_hr_manager_capacity_label"),
                Localize("bighax_disable_player_hunger_and_energy_decay_label"),
                Localize("bighax_disable_player_happiness_decay_label"),
                Localize("bighax_unlock_all_contacts_button"),
                Localize("bighax_unlock_all_courses_button"),
                Localize("bighax_enable_instant_imports_label"),
                Localize("bighax_enable_instant_furniture_deliveries_label"),
                Localize("bighax_disable_purchase_limits_label"),
                Localize("bighax_installation_firm_fee_percentage_label"),
                Localize("bighax_enable_extended_bed_sleep_label"),
                Localize("bighax_no_vehicle_damage_label"),
                Localize("bighax_infinite_vehicle_fuel_label"),
                Localize("bighax_never_dirty_vehicles_label"),
                Localize("bighax_disable_traffic_label"),
                Localize("bighax_disable_parked_cars_label"),
                Localize("bighax_active_vehicle_enabled_label"),
                Localize("bighax_customer_traffic_multiplier_label"),
                Localize("bighax_building_customer_capacity_multiplier_label"),
                Localize("bighax_employee_training_skill_increase_label"),
                Localize("bighax_standard_fridge_capacity_label", new Dictionary<string, string>
                {
                    { "itemName", Localize("ba:itemname_standardfridge") }
                }),
                Localize("bighax_pallet_shelf_capacity_label", new Dictionary<string, string>
                {
                    { "itemName", Localize("ba:itemname_palletshelf") }
                }),
                Localize("bighax_storage_shelf_capacity_label", new Dictionary<string, string>
                {
                    { "itemName", Localize("ba:itemname_storageshelf") }
                }),
                Localize("bighax_freight_truck_delivery_places_label", new Dictionary<string, string>
                {
                    { "vehicleName", Localize("ba:vehicletype_freighttruckt1") }
                }),
                Localize("bighax_feedback_prompt"),
                Localize("bighax_ui_close_button")
            });
        }

        private void AddToggle(string label, Func<bool> read, Action<bool> write)
        {
            api.CreateNativeToggle(content!, label, read(), new UnityAction<bool>(value =>
            {
                write(value);
            }), "BigHaxToggle");
        }

        private void AddCategory(string label)
        {
            var category = new GameObject("Category", typeof(RectTransform), typeof(LayoutElement), typeof(Text));
            category.transform.SetParent(content!, false);
            category.GetComponent<LayoutElement>().preferredHeight = 42f;
            var text = category.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 19;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.LowerLeft;
            text.color = api.MutedTextColor;
            text.text = label;
        }

        private void AddSeparator()
        {
            var row = new GameObject("Separator", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(content!, false);
            row.GetComponent<LayoutElement>().preferredHeight = 18f;

            var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(row.transform, false);
            var rect = line.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(-12f, 2f);
            var color = api.MutedTextColor;
            color.a = 0.45f;
            line.GetComponent<Image>().color = color;
        }

        private void AddFeedbackLabel()
        {
            var label = new GameObject("FeedbackPrompt", typeof(RectTransform), typeof(LayoutElement), typeof(Text));
            label.transform.SetParent(content!, false);
            label.GetComponent<LayoutElement>().preferredHeight = 38f;
            var text = label.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 16;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = api.MutedTextColor;
            text.text = Localize("bighax_feedback_prompt");
        }

        private void AddSlider(string label, Func<int> read, int min, int max, Action<int> write, Func<int, string> format)
        {
            api.CreateNativeSlider(content!, label, min, max, read(), format, new UnityAction<int>(value =>
            {
                write(value);
            }), "BigHaxSlider");
        }

        private void AddSteppedSlider(
            string label,
            Func<int> read,
            int[] values,
            Action<int> write,
            Func<int, string> format)
        {
            AddSlider(
                label,
                () => BigHaxSettings.GetStepIndex(values, read()),
                0,
                values.Length - 1,
                index => write(values[index]),
                index => format(values[index]));
        }

        private void AddActionButton(string label, Action? onClick)
        {
            if (onClick == null)
                return;

            var row = new GameObject("ActionButton", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(content!, false);
            row.GetComponent<LayoutElement>().preferredHeight = 42f;
            var button = api.CreateVanillaButton(row.transform, label, 320f, 36f, new UnityAction(onClick), "Blue", 15f);
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
        }

        private void AddFeedbackButtons()
        {
            var row = new GameObject("FeedbackLinks", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(content!, false);
            row.GetComponent<LayoutElement>().preferredHeight = 48f;

            var steam = api.CreateVanillaButton(
                row.transform,
                "Steam",
                300f,
                40f,
                new UnityAction(BigHaxFeedbackLinks.OpenSteam),
                "Blue",
                15f);
            PositionFeedbackButton(steam, -158f, BigHaxFeedbackLinks.SteamIcon);

            var discord = api.CreateVanillaButton(
                row.transform,
                "Discord",
                300f,
                40f,
                new UnityAction(BigHaxFeedbackLinks.OpenDiscord),
                "Blue",
                15f);
            PositionFeedbackButton(discord, 158f, BigHaxFeedbackLinks.DiscordIcon);
        }

        private static void PositionFeedbackButton(Button button, float x, Sprite? icon)
        {
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(300f, 40f);

            if (icon == null)
                return;

            var iconObject = new GameObject("BrandIcon", typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(button.transform, false);
            var iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = new Vector2(28f, 0f);
            iconRect.sizeDelta = new Vector2(24f, 24f);
            var image = iconObject.GetComponent<Image>();
            image.sprite = icon;
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private Scrollbar CreateScrollbar(RectTransform panel)
        {
            var scrollbarObject = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObject.transform.SetParent(panel, false);
            var rect = scrollbarObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-28f, 4f);
            rect.sizeDelta = new Vector2(10f, -132f);
            var track = scrollbarObject.GetComponent<Image>();
            track.color = api.ListInsetColor;

            var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(rect, false);
            var handleRect = handleObject.GetComponent<RectTransform>();
            Stretch(handleRect);
            var handle = handleObject.GetComponent<Image>();
            handle.color = api.MutedTextColor;
            var scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.targetGraphic = handle;
            scrollbar.handleRect = handleRect;
            return scrollbar;
        }

        private void AddFooter(RectTransform panel)
        {
            var button = api.CreateVanillaButton(panel, Localize("bighax_ui_close_button"), 160f, 36f, new UnityAction(close), "Blue", 15f);
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-30f, 14f);
            rect.sizeDelta = new Vector2(160f, 36f);
        }

        private void DestroyVisuals()
        {
            if (root != null)
                UnityEngine.Object.Destroy(root);
            root = null;
            content = null;
            layoutPrimed = false;
            localizationSignature = string.Empty;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static string Localize(string key) => key.Localize().ToString();
        private static string Localize(string key, Dictionary<string, string> arguments) => key.Localize(arguments).ToString();

        private sealed class BaUiReflection
        {
            private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
            private readonly Type buttonStyleType;
            private readonly MethodInfo ensureEventSystem;
            private readonly MethodInfo setupOverlayCanvas;
            private readonly MethodInfo createModalDimmer;
            private readonly MethodInfo buildPanel;
            private readonly MethodInfo createHeaderTitle;
            private readonly MethodInfo createHeaderCloseButton;
            private readonly MethodInfo createVanillaButton;
            private readonly MethodInfo createNativeToggle;
            private readonly MethodInfo createNativeSlider;
            private readonly MethodInfo applyUiLayer;
            private readonly MethodInfo? attachDraggableWindow;

            private BaUiReflection(
                Assembly assembly,
                string libraryVersion,
                Type bootstrap,
                Type chrome,
                Type widgets,
                Type vanillaSettings)
            {
                AssemblyName = assembly.GetName().Name ?? "LIB_BaUnifiedUI";
                LibraryVersion = libraryVersion;
                buttonStyleType = RequireType(assembly, "Capisoft.Lib.BaUnifiedUI.Chrome.BaVanillaButtonStyle");
                ensureEventSystem = RequireMethod(bootstrap, "EnsureEventSystem", 1);
                setupOverlayCanvas = RequireMethod(chrome, "SetupOverlayCanvas", 3);
                createModalDimmer = RequireMethod(widgets, "CreateModalDimmer", 3);
                buildPanel = RequireMethod(chrome, "BuildPanel", 5);
                createHeaderTitle = RequireMethod(widgets, "CreateHeaderTitleLeft", 5);
                createHeaderCloseButton = RequireMethod(chrome, "CreateHeaderCloseButton", 2);
                createVanillaButton = RequireMethod(chrome, "CreateVanillaButton", 10);
                createNativeToggle = RequireMethod(vanillaSettings, "CreateToggle", 5);
                createNativeSlider = RequireMethod(vanillaSettings, "CreateSlider", 8);
                applyUiLayer = RequireMethod(chrome, "ApplyUiLayer", 1);
                attachDraggableWindow = chrome.GetMethods(PublicStatic)
                    .FirstOrDefault(method => method.Name == "AttachDraggableWindow" && method.GetParameters().Length == 3);

                MutedTextColor = ReadColor(chrome, "MutedTextColor", new Color(0.72f, 0.76f, 0.8f, 1f));
                ListInsetColor = ReadColor(chrome, "ListInsetColor", new Color(0f, 0f, 0f, 0.22f));
            }

            public string AssemblyName { get; }
            public string LibraryVersion { get; }
            public Color MutedTextColor { get; }
            public Color ListInsetColor { get; }

            public bool AreNativeOptionsReady()
            {
                const BindingFlags instancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
                const string controllerTypeName = "BigAmbitions.ModsInternal.ModOptionsViewController";
                var controllerType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(candidate => candidate.GetType(controllerTypeName, throwOnError: false))
                    .FirstOrDefault(candidate => candidate != null);
                if (controllerType == null)
                    return false;

                var togglePrefabField = controllerType.GetField("modOptionsTogglePrefab", instancePrivate);
                var sliderPrefabField = controllerType.GetField("modOptionsSliderPrefab", instancePrivate);
                if (togglePrefabField == null || sliderPrefabField == null)
                    return false;

                foreach (var controller in Resources.FindObjectsOfTypeAll(controllerType))
                {
                    if (controller != null &&
                        togglePrefabField.GetValue(controller) is GameObject togglePrefab && togglePrefab != null &&
                        sliderPrefabField.GetValue(controller) is GameObject sliderPrefab && sliderPrefab != null)
                    {
                        return true;
                    }
                }

                return false;
            }

            public static bool TryResolve(string minimumVersion, out BaUiReflection? api, out string reason)
            {
                api = null;
                var versionTypeName = "Capisoft.Lib.BaUnifiedUI.Core.BaUiVersion";
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(candidate => candidate.GetType(versionTypeName, throwOnError: false) != null);
                if (assembly == null)
                {
                    reason = "LIB_BaUnifiedUI is not loaded";
                    return false;
                }

                try
                {
                    var versionType = RequireType(assembly, versionTypeName);
                    var field = versionType.GetField("Version", PublicStatic)
                        ?? throw new MissingFieldException(versionType.FullName, "Version");
                    var libraryVersion = (field.GetRawConstantValue() ?? field.GetValue(null))?.ToString() ?? string.Empty;
                    if (!TryParseVersion(libraryVersion, out var parsed) ||
                        !TryParseVersion(minimumVersion, out var minimum) ||
                        parsed < minimum)
                    {
                        reason = $"LIB_BaUnifiedUI {libraryVersion} is below required {minimumVersion}";
                        return false;
                    }

                    api = new BaUiReflection(
                        assembly,
                        libraryVersion,
                        RequireType(assembly, "Capisoft.Lib.BaUnifiedUI.Core.BaUiBootstrap"),
                        RequireType(assembly, "Capisoft.Lib.BaUnifiedUI.Chrome.BaUiWidePanelChrome"),
                        RequireType(assembly, "Capisoft.Lib.BaUnifiedUI.Controls.BaUiWidgets"),
                        RequireType(assembly, "Capisoft.Lib.BaUnifiedUI.Controls.BaUiVanillaSettings"));
                    reason = string.Empty;
                    return true;
                }
                catch (Exception exception)
                {
                    reason = "LIB_BaUnifiedUI API is incompatible: " + exception.GetBaseException().Message;
                    return false;
                }
            }

            public void EnsureEventSystem() => Invoke(ensureEventSystem, null, new object?[] { "BigHax_BAUnifiedUI_EventSystem" });

            public void SetupOverlayCanvas(GameObject root, int sortingOrder, bool interactive) =>
                Invoke(setupOverlayCanvas, null, new object?[] { root, sortingOrder, interactive });

            public void CreateModalDimmer(Transform parent, float alpha, UnityAction onClick) =>
                Invoke(createModalDimmer, null, new object?[] { parent, alpha, onClick });

            public RectTransform BuildPanel(Transform parent, float width, float height, string name, out RectTransform header)
            {
                var arguments = new object?[] { parent, width, height, name, null };
                var result = Invoke(buildPanel, null, arguments) as RectTransform
                    ?? throw new InvalidOperationException("BAUnifiedUI did not create a panel.");
                header = arguments[4] as RectTransform
                    ?? throw new InvalidOperationException("BAUnifiedUI did not create a panel header.");
                return result;
            }

            public void CreateHeaderTitle(Transform header, string text, float scale, int rightIconCount) =>
                Invoke(createHeaderTitle, null, new object?[] { header, text, scale, rightIconCount, true });

            public void CreateHeaderCloseButton(Transform header, UnityAction onClick) =>
                Invoke(createHeaderCloseButton, null, new object?[] { header, onClick });

            public Button CreateVanillaButton(Transform parent, string label, float width, float height, UnityAction onClick, string style, float fontSize)
            {
                var styleValue = Enum.Parse(buttonStyleType, style, ignoreCase: true);
                return Invoke(createVanillaButton, null,
                    new object?[] { parent, label, width, height, 1f, onClick, styleValue, fontSize, true, null }) as Button
                    ?? throw new InvalidOperationException("BAUnifiedUI did not create a button.");
            }

            public void CreateNativeToggle(
                Transform parent,
                string label,
                bool value,
                UnityAction<bool> onValueChanged,
                string name) =>
                Invoke(createNativeToggle, null, new object?[] { parent, label, value, onValueChanged, name });

            public void CreateNativeSlider(
                Transform parent,
                string label,
                int min,
                int max,
                int value,
                Func<int, string> format,
                UnityAction<int> onValueChanged,
                string name) =>
                Invoke(createNativeSlider, null,
                    new object?[] { parent, label, min, max, value, format, onValueChanged, name });

            public void ApplyUiLayer(GameObject root) => Invoke(applyUiLayer, null, new object?[] { root });

            public void TryAttachDraggableWindow(RectTransform panel, RectTransform header, string persistentId)
            {
                if (attachDraggableWindow == null)
                    return;
                try
                {
                    Invoke(attachDraggableWindow, null, new object?[] { panel, header, persistentId });
                }
                catch
                {
                }
            }

            private static Type RequireType(Assembly assembly, string fullName) =>
                assembly.GetType(fullName, throwOnError: false)
                ?? throw new TypeLoadException("Missing BAUnifiedUI type " + fullName);

            private static MethodInfo RequireMethod(Type type, string name, int parameterCount) =>
                type.GetMethods(PublicStatic).FirstOrDefault(method => method.Name == name && method.GetParameters().Length == parameterCount)
                ?? throw new MissingMethodException(type.FullName, name);

            private static Color ReadColor(Type type, string name, Color fallback)
            {
                var field = type.GetField(name, PublicStatic);
                return field?.GetValue(null) is Color value ? value : fallback;
            }

            private static object? Invoke(MethodInfo method, object? target, object?[] arguments)
            {
                try
                {
                    return method.Invoke(target, arguments);
                }
                catch (TargetInvocationException exception) when (exception.InnerException != null)
                {
                    throw exception.InnerException;
                }
            }

            private static bool TryParseVersion(string value, out Version version)
            {
                var normalized = (value ?? string.Empty).Split('-', '+')[0];
                return Version.TryParse(normalized, out version);
            }
        }
    }
}
