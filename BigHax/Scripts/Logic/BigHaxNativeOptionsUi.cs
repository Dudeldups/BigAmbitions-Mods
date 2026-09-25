#nullable enable
using BAModAPI;
using Localizor;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BigHax
{
    internal sealed class BigHaxNativeOptionsUi
    {
        private const string BaUnifiedUiWorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3790426259";
        private const float RowHeight = 50f;
        private const float ContentHeight = 2319f;
        private GameObject? root;
        private GameObject? panel;
        private Transform? content;
        private ModContext? context;
        private BigHaxSettings? settings;
        private readonly System.Action close;
        private System.Action? confirmUnlockAllContacts;
        private System.Action? confirmUnlockAllCourses;
        private System.Func<bool>? arePurchaseLimitsDisabled;
        private System.Action<bool>? setPurchaseLimitsDisabled;

        public BigHaxNativeOptionsUi(System.Action close)
        {
            this.close = close;
        }

        public void ConfigureUnlockActions(System.Action unlockAllContacts, System.Action unlockAllCourses)
        {
            confirmUnlockAllContacts = unlockAllContacts;
            confirmUnlockAllCourses = unlockAllCourses;
        }

        public void ConfigurePurchaseLimitOption(System.Func<bool> readDisabled, System.Action<bool> setDisabled)
        {
            arePurchaseLimitsDisabled = readDisabled;
            setPurchaseLimitsDisabled = setDisabled;
        }

        public void EnsureCreated(ModContext modContext, BigHaxSettings currentSettings, bool visible)
        {
            try
            {
                context = modContext;
                settings = currentSettings;
                if (panel == null)
                {
                    Build();
                    panel!.SetActive(visible);
                    if (visible)
                    {
                        Canvas.ForceUpdateCanvases();
                        LayoutRebuilder.ForceRebuildLayoutImmediate(content!.GetComponent<RectTransform>());
                    }
                    return;
                }
                SetVisible(visible);
            }
            catch (System.Exception exception)
            {
                modContext.Logger.Error(exception);
            }
        }
        public void SetVisible(bool visible)
        {
            if (visible && panel != null && !panel.activeSelf)
            {
                // The game can change language while this panel is hidden. Rebuild
                // on each opening so every label comes from the active locale.
                DestroyVisuals();
                Build();
            }

            if (panel == null || panel.activeSelf == visible) return;

            panel.SetActive(visible);
            if (visible)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content!.GetComponent<RectTransform>());
            }
        }
        public void ConsumeGameplayInputIfNeeded(bool visible) { if (visible) Input.ResetInputAxes(); }

        public void Destroy()
        {
            DestroyVisuals();
            context = null;
            settings = null;
        }

        private void Build()
        {
            root = new GameObject("BigHaxOptionsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Object.DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = short.MaxValue - 8;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            panel = Create("BigHaxOptions", root.transform, Color.white);
            var panelRect = panel.GetComponent<RectTransform>(); panelRect.anchorMin = panelRect.anchorMax = new Vector2(.5f, .5f); panelRect.pivot = new Vector2(.5f, .5f); panelRect.sizeDelta = new Vector2(820, 660);
            panel.AddComponent<RectMask2D>();
            var scroll = panel.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.viewport = panelRect;
            var scrollbarObject = Create("Scrollbar", panel.transform, new Color(.80f, .82f, .85f));
            var scrollbarRect = scrollbarObject.GetComponent<RectTransform>(); scrollbarRect.anchorMin = new Vector2(1, 0); scrollbarRect.anchorMax = new Vector2(1, 1); scrollbarRect.pivot = new Vector2(1, .5f); scrollbarRect.sizeDelta = new Vector2(14, -32); scrollbarRect.anchoredPosition = new Vector2(-14, 0);
            var scrollbar = scrollbarObject.AddComponent<Scrollbar>(); scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var scrollbarHandle = Create("Handle", scrollbarObject.transform, new Color(.33f, .38f, .45f)).GetComponent<Image>();
            scrollbarHandle.rectTransform.anchorMin = Vector2.zero; scrollbarHandle.rectTransform.anchorMax = Vector2.one; scrollbarHandle.rectTransform.offsetMin = scrollbarHandle.rectTransform.offsetMax = Vector2.zero;
            scrollbar.targetGraphic = scrollbarHandle; scrollbar.handleRect = scrollbarHandle.rectTransform;
            var contentObject = Create("Content", panel.transform, Color.clear); content = contentObject.transform;
            var contentRect = contentObject.GetComponent<RectTransform>(); contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = new Vector2(1, 1); contentRect.pivot = new Vector2(.5f, 1); contentRect.offsetMin = new Vector2(22, -ContentHeight); contentRect.offsetMax = new Vector2(-22, 0);
            var layout = contentObject.AddComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(12, 12, 18, 18); layout.spacing = 7; layout.childControlWidth = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            scroll.content = contentRect; scroll.verticalScrollbar = scrollbar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            Label(Localize("bighax_options_header"), 26, TextAnchor.MiddleCenter, new Color(.10f, .14f, .19f), 44);
            FallbackNotice(Localize("bighax_ui_fallback_notice"));
            Section(Localize("bighax_category_money"));
            Toggle(Localize("bighax_disable_casino_bet_limit_label"), () => settings!.DisableCasinoBetLimit, v => { settings!.DisableCasinoBetLimit = v; BigHaxOptionPersistence.SaveDisableCasinoBetLimit(context!.ModId, v); });
            Toggle(Localize("bighax_disable_investment_limit_label"), () => settings!.DisableInvestmentLimit, v => { settings!.DisableInvestmentLimit = v; BigHaxOptionPersistence.SaveDisableInvestmentLimit(context!.ModId, v); });
            Toggle(Localize("bighax_vantander_maximum_loan_override_label"), () => settings!.EnableVantanderMaxLoanOverride, v => { settings!.EnableVantanderMaxLoanOverride = v; BigHaxOptionPersistence.SaveEnableVantanderMaxLoanOverride(context!.ModId, v); });
            Separator();
            Section(Localize("bighax_category_employee"));
            Toggle(Localize("bighax_enable_recruitment_candidate_maximum_skill_label"), () => settings!.EnableRecruitmentCandidateMaximumSkill, v => { settings!.EnableRecruitmentCandidateMaximumSkill = v; BigHaxOptionPersistence.SaveEnableRecruitmentCandidateMaximumSkill(context!.ModId, v); });
            Toggle(Localize("bighax_remove_employee_demands_label"), () => settings!.RemoveEmployeeDemands, v => { settings!.RemoveEmployeeDemands = v; BigHaxOptionPersistence.SaveRemoveEmployeeDemands(context!.ModId, v); });
            Toggle(Localize("bighax_maximum_employee_satisfaction_label"), () => settings!.EnableMaximumEmployeeSatisfaction, v => { settings!.EnableMaximumEmployeeSatisfaction = v; BigHaxOptionPersistence.SaveEnableMaximumEmployeeSatisfaction(context!.ModId, v); });
            Toggle(Localize("bighax_maximum_headhunter_recruitment_points_label"), () => settings!.EnableMaximumHeadhunterRecruitmentPoints, v => { settings!.EnableMaximumHeadhunterRecruitmentPoints = v; BigHaxOptionPersistence.SaveEnableMaximumHeadhunterRecruitmentPoints(context!.ModId, v); });
            Slider(
                Localize("bighax_maximum_hr_manager_capacity_label"),
                () => settings!.HrManagerCapacityIndex,
                0,
                BigHaxSettings.HrManagerCapacityValues.Length - 1,
                v => { settings!.HrManagerCapacityIndex = v; BigHaxOptionPersistence.SaveHrManagerCapacityIndex(context!.ModId, v); },
                v => BigHaxSettings.HrManagerCapacityValues[v].ToString());
            SteppedSlider(Localize("bighax_employee_training_skill_increase_label"), () => settings!.EmployeeTrainingSkillIncrease, BigHaxSettings.EmployeeTrainingSkillIncreaseValues, v => { settings!.EmployeeTrainingSkillIncrease = v; BigHaxOptionPersistence.SaveEmployeeTrainingSkillIncrease(context!.ModId, v); }, v => v.ToString());
            Separator();
            Section(Localize("bighax_category_player"));
            Toggle(Localize("bighax_disable_player_hunger_and_energy_decay_label"), () => settings!.DisablePlayerHungerAndEnergyDecay, v => { settings!.DisablePlayerHungerAndEnergyDecay = v; BigHaxOptionPersistence.SaveDisablePlayerHungerAndEnergyDecay(context!.ModId, v); });
            Toggle(Localize("bighax_disable_player_happiness_decay_label"), () => settings!.DisablePlayerHappinessDecay, v => { settings!.DisablePlayerHappinessDecay = v; BigHaxOptionPersistence.SaveDisablePlayerHappinessDecay(context!.ModId, v); });
            Separator();
            Section(Localize("bighax_category_unlock"));
            ActionButton(Localize("bighax_unlock_all_contacts_button"), confirmUnlockAllContacts);
            ActionButton(Localize("bighax_unlock_all_courses_button"), confirmUnlockAllCourses);
            Separator();
            Section(Localize("bighax_category_business"));
            Toggle(Localize("bighax_enable_instant_imports_label"), () => settings!.EnableInstantImports, v => { settings!.EnableInstantImports = v; BigHaxOptionPersistence.SaveEnableInstantImports(context!.ModId, v); });
            Toggle(Localize("bighax_enable_instant_furniture_deliveries_label"), () => settings!.EnableInstantFurnitureDeliveries, v => { settings!.EnableInstantFurnitureDeliveries = v; BigHaxOptionPersistence.SaveEnableInstantFurnitureDeliveries(context!.ModId, v); });
            if (arePurchaseLimitsDisabled != null && setPurchaseLimitsDisabled != null)
                Toggle(Localize("bighax_disable_purchase_limits_label"), arePurchaseLimitsDisabled, setPurchaseLimitsDisabled);
            SteppedSlider(Localize("bighax_installation_firm_fee_percentage_label"), () => settings!.InstallationFirmFeePercentage, BigHaxSettings.InstallationFirmFeePercentageValues, v => { settings!.InstallationFirmFeePercentage = v; BigHaxOptionPersistence.SaveInstallationFirmFeePercentage(context!.ModId, v); }, v => v + "%");
            Slider(Localize("bighax_customer_traffic_multiplier_label"), () => settings!.CustomerTrafficMultiplierIndex, 0, 5, v => { settings!.CustomerTrafficMultiplierIndex = v; BigHaxOptionPersistence.SaveCustomerTrafficMultiplierIndex(context!.ModId, v); }, v => new[] { "1x", "1.5x", "2x", "3x", "5x", "10x" }[v]);
            Slider(Localize("bighax_building_customer_capacity_multiplier_label"), () => settings!.BuildingCustomerCapacityMultiplierIndex, 0, 5, v => { settings!.BuildingCustomerCapacityMultiplierIndex = v; BigHaxOptionPersistence.SaveBuildingCustomerCapacityMultiplierIndex(context!.ModId, v); }, v => new[] { "1x", "1.5x", "2x", "3x", "5x", "10x" }[v]);
            Separator();
            Section(Localize("bighax_category_vehicle"));
            Toggle(Localize("bighax_disable_illegal_parking_penalties_label"), () => settings!.DisableIllegalParkingPenalties, v => { settings!.DisableIllegalParkingPenalties = v; BigHaxOptionPersistence.SaveDisableIllegalParkingPenalties(context!.ModId, v); });
            Toggle(Localize("bighax_no_vehicle_damage_label"), () => settings!.EnableNoVehicleDamage, v => { settings!.EnableNoVehicleDamage = v; BigHaxOptionPersistence.SaveEnableNoVehicleDamage(context!.ModId, v); });
            Toggle(Localize("bighax_infinite_vehicle_fuel_label"), () => settings!.EnableInfiniteVehicleFuel, v => { settings!.EnableInfiniteVehicleFuel = v; BigHaxOptionPersistence.SaveEnableInfiniteVehicleFuel(context!.ModId, v); });
            Toggle(Localize("bighax_never_dirty_vehicles_label"), () => settings!.EnableNeverDirtyVehicles, v => { settings!.EnableNeverDirtyVehicles = v; BigHaxOptionPersistence.SaveEnableNeverDirtyVehicles(context!.ModId, v); });
            Toggle(Localize("bighax_disable_traffic_label"), () => settings!.DisableTraffic, v => { settings!.DisableTraffic = v; BigHaxOptionPersistence.SaveDisableTraffic(context!.ModId, v); });
            Toggle(Localize("bighax_disable_parked_cars_label"), () => settings!.DisableParkedCars, v => { settings!.DisableParkedCars = v; BigHaxOptionPersistence.SaveDisableParkedCars(context!.ModId, v); });
            SteppedSlider(Localize("bighax_freight_truck_delivery_places_label", new Dictionary<string, string> { { "vehicleName", Localize("ba:vehicletype_freighttruckt1") } }), () => settings!.FreightTruckT1DeliveryPlaces, BigHaxSettings.FreightTruckT1DeliveryPlacesValues, v => { settings!.FreightTruckT1DeliveryPlaces = v; BigHaxOptionPersistence.SaveFreightTruckT1DeliveryPlaces(context!.ModId, v); }, v => v.ToString());
            Separator();
            Section(Localize("bighax_category_capacity"));
            Toggle(Localize("bighax_active_vehicle_enabled_label"), () => settings!.EnableActiveVehicleCapacityOverride, v => { settings!.EnableActiveVehicleCapacityOverride = v; BigHaxOptionPersistence.SaveActiveVehicleCapacityEnabled(context!.ModId, v); });
            SteppedSlider(Localize("bighax_standard_fridge_capacity_label", new Dictionary<string, string> { { "itemName", Localize("ba:itemname_standardfridge") } }), () => settings!.StandardFridgeCapacity, BigHaxSettings.StandardFridgeCapacityValues, v => { settings!.StandardFridgeCapacity = v; BigHaxOptionPersistence.SaveStandardFridgeCapacity(context!.ModId, v); }, v => v.ToString());
            SteppedSlider(Localize("bighax_pallet_shelf_capacity_label", new Dictionary<string, string> { { "itemName", Localize("ba:itemname_palletshelf") } }), () => settings!.PalletShelfCapacity, BigHaxSettings.PalletShelfCapacityValues, v => { settings!.PalletShelfCapacity = v; BigHaxOptionPersistence.SavePalletShelfCapacity(context!.ModId, v); }, v => v.ToString());
            SteppedSlider(Localize("bighax_storage_shelf_capacity_label", new Dictionary<string, string> { { "itemName", Localize("ba:itemname_storageshelf") } }), () => settings!.StorageShelfCapacity, BigHaxSettings.StorageShelfCapacityValues, v => { settings!.StorageShelfCapacity = v; BigHaxOptionPersistence.SaveStorageShelfCapacity(context!.ModId, v); }, v => v.ToString());
            Separator();
            Section(Localize("bighax_category_time"));
            Toggle(Localize("bighax_enable_extended_bed_sleep_label"), () => settings!.EnableExtendedBedSleep, v => { settings!.EnableExtendedBedSleep = v; BigHaxOptionPersistence.SaveEnableExtendedBedSleep(context!.ModId, v); });
            Separator();
            Label(Localize("bighax_feedback_prompt"), 16, TextAnchor.MiddleCenter, new Color(.22f, .27f, .34f), 38);
            FeedbackButtons();
            CloseButton();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            contentRect.anchoredPosition = Vector2.zero;
            panel.SetActive(false);
        }

        private void DestroyVisuals()
        {
            if (root != null)
                Object.Destroy(root);
            root = null;
            panel = null;
            content = null;
        }

        private void Toggle(string name, System.Func<bool> read, System.Action<bool> write)
        {
            var row = Row();
            Text(row.transform, name, 16, TextAnchor.MiddleLeft, new Color(.10f, .12f, .15f), new Vector2(26, 0), new Vector2(-360, 0));
            var toggle = row.AddComponent<Toggle>();
            var background = Create("Toggle background", row.transform, new Color(.76f, .79f, .83f)).GetComponent<Image>();
            var backgroundRect = background.rectTransform; backgroundRect.anchorMin = backgroundRect.anchorMax = new Vector2(1, .5f); backgroundRect.sizeDelta = new Vector2(58, 30); backgroundRect.anchoredPosition = new Vector2(-150, 0);
            var knob = Create("Toggle knob", background.transform, Color.white).GetComponent<Image>();
            var knobRect = knob.rectTransform; knobRect.anchorMin = knobRect.anchorMax = new Vector2(.5f, .5f); knobRect.sizeDelta = new Vector2(22, 22);
            System.Action<bool> updateAppearance = value =>
            {
                background.color = value ? new Color(.39f, .70f, .28f) : new Color(.76f, .79f, .83f);
                knobRect.anchoredPosition = new Vector2(value ? 15 : -15, 0);
            };
            toggle.targetGraphic = background; toggle.graphic = knob; toggle.isOn = read(); updateAppearance(toggle.isOn);
            toggle.onValueChanged.AddListener(v => { updateAppearance(v); write(v); });
        }

        private void Slider(string name, System.Func<int> read, int min, int max, System.Action<int> write, System.Func<int, string> format)
        {
            var row = Row();
            var label = Text(row.transform, name, 16, TextAnchor.MiddleLeft, new Color(.10f, .12f, .15f), Vector2.zero, Vector2.zero);
            var labelRect = label.rectTransform; labelRect.anchorMin = labelRect.anchorMax = new Vector2(0, .5f); labelRect.pivot = new Vector2(0, .5f); labelRect.sizeDelta = new Vector2(430, RowHeight); labelRect.anchoredPosition = new Vector2(26, 0);
            var value = Text(row.transform, format(read()), 15, TextAnchor.MiddleRight, new Color(.10f, .12f, .15f), Vector2.zero, Vector2.zero);
            // Keep the value clear of the thumb at the slider's minimum position.
            var valueRect = value.rectTransform; valueRect.anchorMin = valueRect.anchorMax = new Vector2(1, .5f); valueRect.pivot = new Vector2(1, .5f); valueRect.sizeDelta = new Vector2(70, RowHeight); valueRect.anchoredPosition = new Vector2(-285, 0);
            var sliderObject = Create("Slider", row.transform, Color.clear); var sr = sliderObject.GetComponent<RectTransform>(); sr.anchorMin = new Vector2(1, .5f); sr.anchorMax = new Vector2(1, .5f); sr.sizeDelta = new Vector2(230, 24); sr.anchoredPosition = new Vector2(-125, 0);
            var slider = sliderObject.AddComponent<Slider>(); slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = true; slider.value = read();
            var track = Create("Track", sliderObject.transform, new Color(.30f, .34f, .39f)).GetComponent<Image>(); track.rectTransform.anchorMin = new Vector2(0, .38f); track.rectTransform.anchorMax = new Vector2(1, .62f); track.rectTransform.offsetMin = track.rectTransform.offsetMax = Vector2.zero;
            var fill = Create("Fill", track.transform, new Color(.39f, .70f, .28f)).GetComponent<Image>(); fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(.5f, 1); fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var dragHandleObject = new GameObject("Drag handle", typeof(RectTransform)); dragHandleObject.transform.SetParent(sliderObject.transform, false);
            var dragHandleRect = dragHandleObject.GetComponent<RectTransform>(); dragHandleRect.anchorMin = dragHandleRect.anchorMax = new Vector2(.5f, .5f); dragHandleRect.sizeDelta = new Vector2(2, 2);
            var handle = Create("Handle", sliderObject.transform, new Color(.35f, .66f, .27f)).GetComponent<Image>();
            var handleRect = handle.rectTransform; handleRect.anchorMin = handleRect.anchorMax = new Vector2(.5f, .5f); handleRect.sizeDelta = new Vector2(26, 26);
            System.Action<float> updateAppearance = currentValue =>
            {
                var normalized = Mathf.InverseLerp(min, max, currentValue);
                fill.rectTransform.anchorMax = new Vector2(normalized, 1);
                handleRect.anchoredPosition = new Vector2((normalized - .5f) * sr.rect.width, 0);
            };
            slider.handleRect = dragHandleRect; slider.targetGraphic = handle; slider.value = read(); updateAppearance(slider.value);
            slider.onValueChanged.AddListener(v => { var i = Mathf.RoundToInt(v); updateAppearance(v); value.text = format(i); write(i); });
        }

        private void SteppedSlider(
            string name,
            System.Func<int> read,
            int[] values,
            System.Action<int> write,
            System.Func<int, string> format)
        {
            Slider(
                name,
                () => BigHaxSettings.GetStepIndex(values, read()),
                0,
                values.Length - 1,
                index => write(values[index]),
                index => format(values[index]));
        }

        private GameObject Row() { var row = Create("Row", content!, Color.clear); row.AddComponent<LayoutElement>().preferredHeight = RowHeight; return row; }
        private void Section(string value) { Label(value, 19, TextAnchor.LowerLeft, new Color(.22f, .27f, .34f), 42); }
        private void Separator()
        {
            var row = Create("Separator", content!, Color.clear);
            row.AddComponent<LayoutElement>().preferredHeight = 18f;
            var line = Create("Line", row.transform, new Color(.70f, .73f, .77f));
            var rect = line.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, .5f);
            rect.anchorMax = new Vector2(1f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(-12f, 2f);
        }
        private void CloseButton()
        {
            var row = Row();
            var image = row.GetComponent<Image>();
            // Match the game's destructive/exit action color.
            image.color = new Color(.94f, .25f, .31f);
            var button = row.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => close());
            Text(row.transform, Localize("bighax_ui_close_button"), 16, TextAnchor.MiddleCenter, Color.white, Vector2.zero, Vector2.zero);
        }

        private void ActionButton(string label, System.Action? onClick)
        {
            if (onClick == null)
                return;

            var row = Row();
            var image = row.GetComponent<Image>();
            image.color = new Color(.18f, .48f, .84f);
            var button = row.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            Text(row.transform, label, 16, TextAnchor.MiddleCenter, Color.white, Vector2.zero, Vector2.zero);
        }

        private void FeedbackButtons()
        {
            var row = Row();
            CreateFeedbackButton(
                row.transform,
                "Steam",
                new Color(.10f, .17f, .24f),
                -166f,
                BigHaxFeedbackLinks.SteamIcon,
                BigHaxFeedbackLinks.OpenSteam);
            CreateFeedbackButton(
                row.transform,
                "Discord",
                new Color(.35f, .40f, .95f),
                166f,
                BigHaxFeedbackLinks.DiscordIcon,
                BigHaxFeedbackLinks.OpenDiscord);
        }

        private static void CreateFeedbackButton(
            Transform parent,
            string label,
            Color color,
            float x,
            Sprite? icon,
            System.Action onClick)
        {
            var buttonObject = Create(label + "Button", parent, color);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(320f, 42f);
            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(() => onClick());
            Text(buttonObject.transform, label, 16, TextAnchor.MiddleCenter, Color.white, Vector2.zero, Vector2.zero).raycastTarget = false;

            if (icon == null)
                return;

            var iconObject = Create("BrandIcon", buttonObject.transform, Color.white);
            var iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, .5f);
            iconRect.pivot = new Vector2(.5f, .5f);
            iconRect.anchoredPosition = new Vector2(28f, 0f);
            iconRect.sizeDelta = new Vector2(24f, 24f);
            var image = iconObject.GetComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
        private void Label(string value, int size, TextAnchor align, Color color, float height)
        {
            // Text and Image are both Graphics, so they must not live on the same GameObject.
            var item = new GameObject("Label", typeof(RectTransform), typeof(LayoutElement));
            item.transform.SetParent(content!, false);
            item.GetComponent<LayoutElement>().preferredHeight = height;
            var text = item.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = value;
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            var rect = item.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private void FallbackNotice(string value)
        {
            var item = Create("FallbackNotice", content!, new Color(.98f, .83f, .42f));
            item.AddComponent<LayoutElement>().preferredHeight = 96f;
            var button = item.AddComponent<Button>();
            button.targetGraphic = item.GetComponent<Image>();
            button.onClick.AddListener(() => Application.OpenURL(BaUnifiedUiWorkshopUrl));
            var text = Text(item.transform, value, 15, TextAnchor.MiddleLeft, new Color(.22f, .16f, .06f), new Vector2(18f, 28f), new Vector2(-18f, -8f));
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            var link = Text(item.transform, "Steam Workshop", 16, TextAnchor.MiddleLeft, new Color(.05f, .32f, .75f), new Vector2(18f, 6f), new Vector2(-18f, -66f));
            link.fontStyle = FontStyle.Bold;
            link.raycastTarget = false;
        }
        private static Text Text(Transform parent, string value, int size, TextAnchor align, Color color, Vector2 left, Vector2 right) { var item = new GameObject("Text", typeof(RectTransform), typeof(Text)); item.transform.SetParent(parent, false); var r = item.GetComponent<RectTransform>(); r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = left; r.offsetMax = right; var t = item.GetComponent<Text>(); t.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); t.text = value; t.fontSize = size; t.alignment = align; t.color = color; return t; }
        private static GameObject Create(string name, Transform parent, Color color) { var o = new GameObject(name, typeof(RectTransform), typeof(Image)); o.transform.SetParent(parent, false); o.GetComponent<Image>().color = color; return o; }
        private static string Localize(string key) { return key.Localize().ToString(); }
        private static string Localize(string key, Dictionary<string, string> arguments) { return key.Localize(arguments).ToString(); }
    }
}
