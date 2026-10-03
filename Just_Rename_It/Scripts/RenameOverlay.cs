#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.InputSystem;
using Entities;
using Localizor;
using TMPro;
using UI.Notification;
using UI.Smartphone.Apps.MyEmployees;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace JustRenameIt
{
    internal sealed class RenameOverlay
    {
        private enum RenameTab { General, Employees, Vehicles, Products, Ingredients }

        private static readonly Color Backdrop = new Color(0.08f, 0.13f, 0.20f, 0.82f);
        private static readonly Color Slate = new Color(0.28f, 0.34f, 0.43f);
        private static readonly Color Ink = new Color(0.18f, 0.23f, 0.31f);
        private static readonly Color Blue = new Color(0.13f, 0.47f, 0.87f);
        private readonly JustRenameItRuntime runtime;
        private readonly List<EmployeeInstance> employees = new List<EmployeeInstance>();
        private readonly List<TMP_InputField> employeeInputs = new List<TMP_InputField>();
        private readonly List<VehicleNameEntry> vehicles = new List<VehicleNameEntry>();
        private readonly List<TMP_InputField> vehicleInputs = new List<TMP_InputField>();
        private readonly List<ProductNameEntry> products = new List<ProductNameEntry>();
        private readonly List<TMP_InputField> productInputs = new List<TMP_InputField>();
        private readonly List<ProductNameEntry> ingredients = new List<ProductNameEntry>();
        private readonly List<TMP_InputField> ingredientInputs = new List<TMP_InputField>();
        private readonly List<SpecialRivalNameEntry> specialRivals = new List<SpecialRivalNameEntry>();
        private readonly List<TMP_InputField> specialRivalInputs = new List<TMP_InputField>();
        private readonly Dictionary<EmployeeInstance, string> pendingEmployees = new Dictionary<EmployeeInstance, string>();
        private readonly Dictionary<string, string> pendingVehicles = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> pendingProducts = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> pendingIngredients = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> pendingSpecialRivals = new Dictionary<string, string>(StringComparer.Ordinal);
        private string? pendingPlayerName;
        private TMP_InputField? playerInput;
        private GameObject? root;
        private RectTransform? rows;
        private ScrollRect? scroll;
        private TextMeshProUGUI? hintLabel;
        private TextMeshProUGUI? errorLabel;
        private Button? saveButton;
        private Button? vehicleTabButton;
        private Button? productTabButton;
        private Button? ingredientTabButton;
        private Button? generalTabButton;
        private Image? vehicleTabUnderline;
        private Image? productTabUnderline;
        private Image? ingredientTabUnderline;
        private Image? generalTabUnderline;
        private Button? employeeNameDropdown;
        private Button? rivalNameDropdown;
        private Button? vanillaVehicleTab;
        private Button? moddedVehicleTab;
        private Image? vanillaVehicleUnderline;
        private Image? moddedVehicleUnderline;
        private GameObject? dropdownOverlay;
        private GameObject? confirmationOverlay;
        private RectTransform? panelRect;
        private GameObject? mainTabStrip;
        private GameObject? scrollTrack;
        private GameObject? vehicleTabStrip;
        private GameObject? vehicleActions;
        private Button? presetButton;
        private Button? undoPresetButton;
        private Button? undoModdedButton;
        private Button? resetProductsButton;
        private Button? resetIngredientsButton;
        private TMP_FontAsset? gameFont;
        private InputAction? cancelAction;
        private RenameTab currentTab;
        private bool showVanillaVehicles = true;
        private bool showVanillaProducts = true;
        private bool showVanillaIngredients = true;
        private bool manualMode;

        internal RenameOverlay(JustRenameItRuntime runtime) => this.runtime = runtime;
        internal bool IsOpen => root != null;

        internal void OpenManager()
        {
            OpenCore(Array.Empty<EmployeeInstance>(), false);
        }

        internal void Open(IEnumerable<EmployeeInstance> selected)
        {
            OpenCore(selected.Where(employee => employee?.characterData != null), true);
        }

        private void OpenCore(IEnumerable<EmployeeInstance> selected, bool manual)
        {
            Close();
            manualMode = manual;
            employees.AddRange(selected.Distinct());
            try
            {
                gameFont = UnityEngine.Object.FindObjectOfType<MyEmployees>()?
                    .GetComponentInChildren<TMP_Text>(true)?.font ?? TMP_Settings.defaultFontAsset;
                BuildUi();
                SwitchTab(manual ? RenameTab.Employees : RenameTab.General);
                if (InputActionHelper.PlayerInputActionMap.TryGetValue(PlayerAction.Cancel, out var action))
                {
                    cancelAction = action;
                    cancelAction.performed += HandleCancelAction;
                }
                else
                    JustRenameItLog.Warn("Cancel input action unavailable; rename UI cannot intercept Escape.");
                if (employeeInputs.Count > 0 && EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(employeeInputs[0].gameObject);
                JustRenameItLog.Ui($"Rename UI opened; manual={manualMode}, " +
                    $"selectedEmployees={employees.Count}, canvasOrder=32760.");
            }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
                Close();
                Notifications.ShowError("justrenameit_rename_failed", null, true);
            }
        }

        internal void Close()
        {
            HideDropdown();
            HideConfirmation();
            if (cancelAction != null)
            {
                cancelAction.performed -= HandleCancelAction;
                cancelAction = null;
            }
            if (root != null)
            {
                var selected = EventSystem.current?.currentSelectedGameObject;
                if (selected != null && selected.transform.IsChildOf(root.transform))
                    EventSystem.current!.SetSelectedGameObject(null);
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
                root = null;
            }
            rows = null;
            scroll = null;
            hintLabel = null;
            errorLabel = null;
            saveButton = null;
            vehicleTabButton = null;
            productTabButton = null;
            ingredientTabButton = null;
            generalTabButton = null;
            vehicleTabUnderline = null;
            productTabUnderline = null;
            ingredientTabUnderline = null;
            generalTabUnderline = null;
            employeeNameDropdown = null;
            rivalNameDropdown = null;
            vanillaVehicleTab = null;
            moddedVehicleTab = null;
            vanillaVehicleUnderline = null;
            moddedVehicleUnderline = null;
            panelRect = null;
            mainTabStrip = null;
            scrollTrack = null;
            vehicleTabStrip = null;
            vehicleActions = null;
            presetButton = null;
            undoPresetButton = null;
            undoModdedButton = null;
            resetProductsButton = null;
            resetIngredientsButton = null;
            showVanillaVehicles = true;
            showVanillaProducts = true;
            showVanillaIngredients = true;
            manualMode = false;
            employees.Clear();
            employeeInputs.Clear();
            vehicles.Clear();
            vehicleInputs.Clear();
            productInputs.Clear();
            products.Clear();
            ingredients.Clear();
            specialRivals.Clear();
            pendingEmployees.Clear();
            pendingVehicles.Clear();
            pendingProducts.Clear();
            pendingIngredients.Clear();
            pendingSpecialRivals.Clear();
            pendingPlayerName = null;
            playerInput = null;
            ingredientInputs.Clear();
            specialRivalInputs.Clear();
        }

        private void HandleCancelAction(InputAction.CallbackContext context)
        {
            if (!IsOpen) return;
            if (confirmationOverlay != null) HideConfirmation();
            else if (dropdownOverlay != null) HideDropdown();
            else Close();
            InputActionHelper.Reset(PlayerAction.Cancel);
            JustRenameItLog.Ui("Rename UI handled Escape; game cancel action consumed.");
        }

        private void BuildUi()
        {
            root = new GameObject("Just Rename It Dialog", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32760;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var shade = ImageRect("Click blocker", root.transform, Backdrop);
            Stretch(shade.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            shade.raycastTarget = true;

            var panel = ImageRect("Rename panel", shade.transform, new Color(0.95f, 0.96f, 0.97f));
            panelRect = panel.rectTransform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(960f, 720f);
            panelRect.anchoredPosition = Vector2.zero;
            var shadow = panel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.36f);
            shadow.effectDistance = new Vector2(0f, -12f);

            var header = ImageRect("Header", panel.transform, Slate);
            Stretch(header.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -72f), Vector2.zero);
            var title = TextRect("Title", header.transform,
                Text(manualMode ? "justrenameit_manual_title" : "justrenameit_manager_title"), 25f,
                Color.white, TextAlignmentOptions.MidlineLeft);
            Stretch(title.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(28f, 0f), new Vector2(-80f, 0f));
            var close = ButtonRect("Close", header.transform, "×", Slate, Color.white, Close, 27f);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(62f, 62f);
            closeRect.anchoredPosition = new Vector2(-39f, 0f);

            var tabStrip = ImageRect("Tab strip", panel.transform, new Color(0.82f, 0.85f, 0.89f));
            mainTabStrip = tabStrip.gameObject;
            Stretch(tabStrip.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(28f, -139f), new Vector2(-28f, -79f));
            generalTabButton = ButtonRect("General tab", panel.transform,
                Text("justrenameit_tab_general"), Color.white, Ink,
                () => SwitchTab(RenameTab.General), 15f);
            Stretch(generalTabButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -135f), new Vector2(248f, -82f));
            vehicleTabButton = ButtonRect("Vehicles tab", panel.transform,
                Text("justrenameit_tab_vehicles"), new Color(0.88f, 0.90f, 0.93f), Ink,
                () => SwitchTab(RenameTab.Vehicles), 17f);
            Stretch(vehicleTabButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(248f, -135f), new Vector2(468f, -82f));
            productTabButton = ButtonRect("Products tab", panel.transform,
                Text("justrenameit_tab_products"), new Color(0.88f, 0.90f, 0.93f), Ink,
                () => SwitchTab(RenameTab.Products), 17f);
            Stretch(productTabButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(468f, -135f), new Vector2(688f, -82f));
            ingredientTabButton = ButtonRect("Ingredients tab", panel.transform,
                Text("justrenameit_tab_ingredients"), new Color(0.88f, 0.90f, 0.93f), Ink,
                () => SwitchTab(RenameTab.Ingredients), 15f);
            Stretch(ingredientTabButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(688f, -135f), new Vector2(908f, -82f));
            generalTabUnderline = ImageRect("General active underline", panel.transform, Blue);
            Stretch(generalTabUnderline.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -139f), new Vector2(248f, -135f));
            vehicleTabUnderline = ImageRect("Vehicles active underline", panel.transform, Blue);
            Stretch(vehicleTabUnderline.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(248f, -139f), new Vector2(468f, -135f));
            productTabUnderline = ImageRect("Products active underline", panel.transform, Blue);
            Stretch(productTabUnderline.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(468f, -139f), new Vector2(688f, -135f));
            ingredientTabUnderline = ImageRect("Ingredients active underline", panel.transform, Blue);
            Stretch(ingredientTabUnderline.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(688f, -139f), new Vector2(908f, -135f));

            var vehicleStrip = ImageRect("Vehicle tab strip", panel.transform, new Color(0.82f, 0.85f, 0.89f));
            vehicleTabStrip = vehicleStrip.gameObject;
            Stretch(vehicleStrip.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(28f, -197f), new Vector2(-28f, -147f));
            vehicleActions = new GameObject("Vehicle actions", typeof(RectTransform));
            vehicleActions.transform.SetParent(panel.transform, false);
            Stretch((RectTransform)vehicleActions.transform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(28f, -254f), new Vector2(-28f, -202f));
            vanillaVehicleTab = ButtonRect("Vanilla vehicles tab", vehicleStrip.transform,
                Text("justrenameit_vehicle_group_vanilla"), Color.white, Ink,
                () => SwitchNameGroup(true), 16f);
            Stretch(vanillaVehicleTab.GetComponent<RectTransform>(), Vector2.zero, new Vector2(0.5f, 1f),
                new Vector2(0f, 4f), new Vector2(0f, 0f));
            moddedVehicleTab = ButtonRect("Modded vehicles tab", vehicleStrip.transform,
                Text("justrenameit_vehicle_group_modded"), new Color(0.88f, 0.90f, 0.93f), Ink,
                () => SwitchNameGroup(false), 16f);
            Stretch(moddedVehicleTab.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), Vector2.one,
                new Vector2(0f, 4f), Vector2.zero);
            vanillaVehicleUnderline = ImageRect("Vanilla underline", vehicleStrip.transform, Blue);
            Stretch(vanillaVehicleUnderline.rectTransform, Vector2.zero, new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, 4f));
            moddedVehicleUnderline = ImageRect("Modded underline", vehicleStrip.transform, Blue);
            Stretch(moddedVehicleUnderline.rectTransform, new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                Vector2.zero, new Vector2(0f, 4f));

            presetButton = ButtonRect("Apply vehicle preset", vehicleActions.transform,
                Text("justrenameit_vehicle_preset_button"), Blue, Color.white, ApplyPreset, 16f);
            Stretch(presetButton.GetComponent<RectTransform>(), Vector2.zero, new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(-5f, 0f));
            undoPresetButton = ButtonRect("Undo vehicle preset", vehicleActions.transform,
                Text("justrenameit_vehicle_preset_undo_button"), new Color(0.72f, 0.76f, 0.81f), Ink,
                UndoPreset, 16f);
            Stretch(undoPresetButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), Vector2.one,
                new Vector2(5f, 0f), Vector2.zero);
            undoModdedButton = ButtonRect("Undo all modded vehicle names", vehicleActions.transform,
                Text("justrenameit_vehicle_modded_undo_button"), new Color(0.72f, 0.76f, 0.81f), Ink,
                UndoAllModded, 16f);
            Stretch(undoModdedButton.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            resetProductsButton = ButtonRect("Reset product names", vehicleActions.transform,
                Text("justrenameit_product_reset_button"), new Color(0.72f, 0.76f, 0.81f), Ink,
                ConfirmResetProducts, 16f);
            Stretch(resetProductsButton.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            resetIngredientsButton = ButtonRect("Reset ingredient names", vehicleActions.transform,
                Text("justrenameit_ingredient_reset_button"), new Color(0.72f, 0.76f, 0.81f), Ink,
                ConfirmResetIngredients, 16f);
            Stretch(resetIngredientsButton.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);

            hintLabel = TextRect("Hint", panel.transform, string.Empty, 16f,
                Ink, TextAlignmentOptions.MidlineLeft);
            Stretch(hintLabel.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(32f, -302f), new Vector2(-32f, -261f));

            var viewport = ImageRect("Rows viewport", panel.transform, new Color(0.88f, 0.90f, 0.93f));
            Stretch(viewport.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(28f, 108f), new Vector2(-58f, -315f));
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 42f;
            scroll.viewport = viewport.rectTransform;

            var track = ImageRect("Vertical scrollbar", panel.transform,
                new Color(0.76f, 0.80f, 0.85f));
            scrollTrack = track.gameObject;
            Stretch(track.rectTransform, new Vector2(1f, 0f), Vector2.one,
                new Vector2(-48f, 108f), new Vector2(-28f, -315f));
            var handle = ImageRect("Handle", track.transform, Slate);
            Stretch(handle.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(3f, 3f), new Vector2(-3f, -3f));
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handle;
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            errorLabel = TextRect("Validation", panel.transform, string.Empty, 15f,
                new Color(0.72f, 0.18f, 0.18f), TextAlignmentOptions.MidlineLeft);
            Stretch(errorLabel.rectTransform, Vector2.zero, new Vector2(1f, 0f),
                new Vector2(32f, 68f), new Vector2(-32f, 102f));
            var cancel = ButtonRect("Cancel", panel.transform, Text("justrenameit_cancel"),
                new Color(0.72f, 0.76f, 0.81f), Ink, Close, 16f);
            Stretch(cancel.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-346f, 20f), new Vector2(-190f, 66f));
            saveButton = ButtonRect("Save", panel.transform, Text("justrenameit_save"),
                Blue, Color.white, Save, 16f);
            Stretch(saveButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-180f, 20f), new Vector2(-28f, 66f));
        }

        private void SwitchTab(RenameTab tab)
        {
            if (root == null || scroll == null) return;
            if (manualMode && tab != RenameTab.Employees) return;
            HideDropdown();
            CaptureInputs();
            currentTab = tab;
            if (errorLabel != null) errorLabel.text = string.Empty;
            mainTabStrip?.SetActive(!manualMode);
            generalTabButton?.gameObject.SetActive(!manualMode);
            vehicleTabButton?.gameObject.SetActive(!manualMode);
            productTabButton?.gameObject.SetActive(!manualMode);
            ingredientTabButton?.gameObject.SetActive(!manualMode);
            vehicleTabStrip?.SetActive(!manualMode && tab != RenameTab.Employees && tab != RenameTab.General);
            vehicleActions?.SetActive(!manualMode && tab != RenameTab.Employees && tab != RenameTab.General);
            if (generalTabButton != null)
                generalTabButton.image.color = tab == RenameTab.General ? Color.white : new Color(0.88f, 0.90f, 0.93f);
            if (vehicleTabButton != null)
                vehicleTabButton.image.color = tab == RenameTab.Vehicles ? Color.white : new Color(0.88f, 0.90f, 0.93f);
            if (productTabButton != null)
                productTabButton.image.color = tab == RenameTab.Products ? Color.white : new Color(0.88f, 0.90f, 0.93f);
            if (ingredientTabButton != null)
                ingredientTabButton.image.color = tab == RenameTab.Ingredients ? Color.white : new Color(0.88f, 0.90f, 0.93f);
            generalTabUnderline?.gameObject.SetActive(!manualMode && tab == RenameTab.General);
            vehicleTabUnderline?.gameObject.SetActive(!manualMode && tab == RenameTab.Vehicles);
            productTabUnderline?.gameObject.SetActive(!manualMode && tab == RenameTab.Products);
            ingredientTabUnderline?.gameObject.SetActive(!manualMode && tab == RenameTab.Ingredients);
            presetButton?.gameObject.SetActive(!manualMode && tab == RenameTab.Vehicles && showVanillaVehicles);
            undoPresetButton?.gameObject.SetActive(!manualMode && tab == RenameTab.Vehicles && showVanillaVehicles);
            undoModdedButton?.gameObject.SetActive(!manualMode && tab == RenameTab.Vehicles && !showVanillaVehicles);
            resetProductsButton?.gameObject.SetActive(!manualMode && tab == RenameTab.Products);
            resetIngredientsButton?.gameObject.SetActive(!manualMode && tab == RenameTab.Ingredients);
            UpdateGroupTabs();
            if (hintLabel != null)
                hintLabel.text = Text(tab == RenameTab.General ? "justrenameit_general_tab_hint" :
                    tab == RenameTab.Employees
                        ? employees.Count > 0 ? "justrenameit_manual_hint" : "justrenameit_employee_select_hint"
                        : tab == RenameTab.Vehicles ? "justrenameit_vehicle_tab_hint" :
                            tab == RenameTab.Products ? "justrenameit_product_tab_hint" :
                                "justrenameit_ingredient_tab_hint");
            if (saveButton != null) saveButton.gameObject.SetActive(manualMode || tab != RenameTab.Employees);
            var compact = !manualMode && tab == RenameTab.Employees && employees.Count == 0;
            if (panelRect != null) panelRect.sizeDelta = new Vector2(960f,
                compact ? 410f : manualMode ? 620f : tab == RenameTab.General ? 780f : 720f);
            if (hintLabel != null)
                Stretch(hintLabel.rectTransform, new Vector2(0f, 1f), Vector2.one,
                    new Vector2(32f, manualMode ? -155f : tab == RenameTab.General ? -194f : -302f),
                    new Vector2(-32f, manualMode ? -104f : tab == RenameTab.General ? -150f : -261f));
            var rowsTop = manualMode ? -168f : tab == RenameTab.General ? -206f : -315f;
            scroll.viewport.offsetMax = new Vector2(-58f, rowsTop);
            if (scrollTrack != null)
                ((RectTransform)scrollTrack.transform).offsetMax = new Vector2(-28f, rowsTop);
            scroll.gameObject.SetActive(!compact);
            scrollTrack?.SetActive(!compact);
            BuildRows();
            JustRenameItLog.Ui("Rename UI tab opened: " + tab + ".");
        }

        private void SwitchNameGroup(bool vanilla)
        {
            if (currentTab == RenameTab.Employees || currentTab == RenameTab.General) return;
            if (currentTab == RenameTab.Vehicles && showVanillaVehicles == vanilla) return;
            if (currentTab == RenameTab.Products && showVanillaProducts == vanilla) return;
            if (currentTab == RenameTab.Ingredients && showVanillaIngredients == vanilla) return;
            CaptureInputs();
            if (currentTab == RenameTab.Vehicles) showVanillaVehicles = vanilla;
            else if (currentTab == RenameTab.Products) showVanillaProducts = vanilla;
            else showVanillaIngredients = vanilla;
            UpdateGroupTabs();
            presetButton?.gameObject.SetActive(currentTab == RenameTab.Vehicles && vanilla);
            undoPresetButton?.gameObject.SetActive(currentTab == RenameTab.Vehicles && vanilla);
            undoModdedButton?.gameObject.SetActive(currentTab == RenameTab.Vehicles && !vanilla);
            if (errorLabel != null) errorLabel.text = string.Empty;
            BuildRows();
            if (currentTab == RenameTab.Vehicles)
                JustRenameItLog.Vehicles("Vehicle tab selected: " + (vanilla ? "vanilla" : "modded") + ".");
            else JustRenameItLog.Products((currentTab == RenameTab.Products ? "Product" : "Ingredient") +
                " tab selected: " + (vanilla ? "vanilla" : "modded") + ".");
        }

        private void UpdateGroupTabs()
        {
            var vanilla = currentTab == RenameTab.Products ? showVanillaProducts :
                currentTab == RenameTab.Ingredients ? showVanillaIngredients : showVanillaVehicles;
            if (vanillaVehicleTab != null)
                vanillaVehicleTab.image.color = vanilla ? Color.white : new Color(0.88f, 0.90f, 0.93f);
            if (moddedVehicleTab != null)
                moddedVehicleTab.image.color = vanilla ? new Color(0.88f, 0.90f, 0.93f) : Color.white;
            vanillaVehicleUnderline?.gameObject.SetActive(vanilla);
            moddedVehicleUnderline?.gameObject.SetActive(!vanilla);
        }

        private void CaptureInputs()
        {
            if (playerInput != null) pendingPlayerName = playerInput.text;
            for (var index = 0; index < specialRivalInputs.Count && index < specialRivals.Count; index++)
                pendingSpecialRivals[specialRivals[index].Id] = specialRivalInputs[index].text;
            for (var index = 0; index < employeeInputs.Count && index < employees.Count; index++)
                pendingEmployees[employees[index]] = employeeInputs[index].text;
            for (var index = 0; index < vehicleInputs.Count && index < vehicles.Count; index++)
                pendingVehicles[vehicles[index].Key] = vehicleInputs[index].text;
            for (var index = 0; index < productInputs.Count && index < products.Count; index++)
                pendingProducts[products[index].Key] = productInputs[index].text;
            for (var index = 0; index < ingredientInputs.Count && index < ingredients.Count; index++)
                pendingIngredients[ingredients[index].Key] = ingredientInputs[index].text;
        }

        private void BuildRows()
        {
            if (scroll == null) return;
            if (rows != null) UnityEngine.Object.Destroy(rows.gameObject);
            employeeNameDropdown = null;
            rivalNameDropdown = null;
            employeeInputs.Clear();
            vehicleInputs.Clear();
            vehicles.Clear();
            productInputs.Clear();
            products.Clear();
            ingredientInputs.Clear();
            ingredients.Clear();
            specialRivalInputs.Clear();
            specialRivals.Clear();
            playerInput = null;
            rows = new GameObject("Rows", typeof(RectTransform)).GetComponent<RectTransform>();
            rows.SetParent(scroll.viewport, false);
            rows.anchorMin = new Vector2(0f, 1f);
            rows.anchorMax = Vector2.one;
            rows.pivot = new Vector2(0.5f, 1f);
            rows.anchoredPosition = Vector2.zero;
            scroll.content = rows;
            scroll.verticalNormalizedPosition = 1f;
            if (currentTab == RenameTab.General)
            {
                var playerName = runtime.GetPlayerName();
                specialRivals.AddRange(runtime.GetSpecialRivalNames());
                var actionTop = (specialRivals.Count + 1) * 60f + 40f;
                rows.sizeDelta = new Vector2(0f, actionTop + 114f);
                playerInput = AddRow(Text("justrenameit_player_label"), pendingPlayerName ?? playerName,
                    0, 60f);
                AddSeparator(68f);
                for (var index = 0; index < specialRivals.Count; index++)
                {
                    var rival = specialRivals[index];
                    specialRivalInputs.Add(AddRow(rival.OriginalName,
                        pendingSpecialRivals.TryGetValue(rival.Id, out var pending) ? pending : rival.CurrentName,
                        index + 1, 60f, 16f));
                }
                AddSeparator(actionTop - 14f);
                AddGeneralActionRow("Rival name actions", actionTop, true);
                AddGeneralActionRow("Employee name actions", actionTop + 60f, false);
            }
            else if (currentTab == RenameTab.Employees)
            {
                rows.sizeDelta = new Vector2(0f, Math.Max(1, employees.Count) * 64f);
                if (employees.Count == 0)
                    AddEmptyRow(Text("justrenameit_employee_select_hint"));
                else
                    for (var index = 0; index < employees.Count; index++)
                    {
                        var employee = employees[index];
                        employeeInputs.Add(AddRow(employee.characterData.name,
                            pendingEmployees.TryGetValue(employee, out var pending) ? pending : employee.characterData.name,
                            index));
                    }
            }
            else if (currentTab == RenameTab.Vehicles)
            {
                vehicles.AddRange(runtime.GetVehicleNames(showVanillaVehicles));
                rows.sizeDelta = new Vector2(0f, Math.Max(1, vehicles.Count) * 64f);
                if (vehicles.Count == 0)
                    AddEmptyRow(Text("justrenameit_vehicle_empty"));
                else
                    for (var index = 0; index < vehicles.Count; index++)
                    {
                        var vehicle = vehicles[index];
                        vehicleInputs.Add(AddRow(vehicle.OriginalName,
                            pendingVehicles.TryGetValue(vehicle.Key, out var pending) ? pending : vehicle.CurrentName,
                            index));
                    }
            }
            else if (currentTab == RenameTab.Products)
            {
                products.AddRange(runtime.GetProductNames(showVanillaProducts));
                rows.sizeDelta = new Vector2(0f, Math.Max(1, products.Count) * 64f);
                if (products.Count == 0)
                    AddEmptyRow(Text("justrenameit_product_empty"));
                else
                    for (var index = 0; index < products.Count; index++)
                    {
                        var product = products[index];
                        productInputs.Add(AddRow(product.OriginalName,
                            pendingProducts.TryGetValue(product.Key, out var pending) ? pending : product.CurrentName,
                            index));
                    }
            }
            else
            {
                ingredients.AddRange(runtime.GetIngredientNames(showVanillaIngredients));
                rows.sizeDelta = new Vector2(0f, Math.Max(1, ingredients.Count) * 64f);
                if (ingredients.Count == 0)
                    AddEmptyRow(Text("justrenameit_ingredient_empty"));
                else
                    for (var index = 0; index < ingredients.Count; index++)
                    {
                        var ingredient = ingredients[index];
                        ingredientInputs.Add(AddRow(ingredient.OriginalName,
                            pendingIngredients.TryGetValue(ingredient.Key, out var pending)
                                ? pending : ingredient.CurrentName, index));
                    }
            }
            UpdateDropdownCaptions();
        }

        private void AddSeparator(float distanceFromTop)
        {
            var separator = ImageRect("Section separator", rows!, Slate);
            separator.raycastTarget = false;
            Stretch(separator.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(16f, -distanceFromTop - 2.5f),
                new Vector2(-16f, -distanceFromTop + 2.5f));
        }

        private void AddGeneralActionRow(string name, float top, bool forRivals)
        {
            var actionRow = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            actionRow.SetParent(rows!, false);
            Stretch(actionRow, new Vector2(0f, 1f), Vector2.one,
                new Vector2(8f, -top - 50f), new Vector2(-8f, -top));
            var dropdown = ButtonRect(name + " dropdown", actionRow,
                string.Empty, Color.white, Ink, () => OpenNameListDropdown(forRivals), 15f);
            Stretch(dropdown.GetComponent<RectTransform>(), Vector2.zero, new Vector2(0.5f, 1f),
                new Vector2(8f, 0f), new Vector2(-6f, 0f));
            DecorateDropdown(dropdown);
            var button = ButtonRect(name + " button", actionRow,
                Text(forRivals ? "justrenameit_rename_rivals_button" : "justrenameit_rename_existing_button"),
                Blue, Color.white, forRivals ? ConfirmRivals : ConfirmEmployees, 16f);
            Stretch(button.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), Vector2.one,
                new Vector2(6f, 0f), new Vector2(-8f, 0f));
            if (forRivals) rivalNameDropdown = dropdown;
            else employeeNameDropdown = dropdown;
        }

        private void AddEmptyRow(string message)
        {
            if (rows == null) return;
            var text = TextRect("Empty state", rows, message, 16f, Ink, TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(22f, -64f), new Vector2(-22f, 0f));
        }

        private TMP_InputField AddRow(string originalName, string currentName, int index,
            float rowHeight = 64f, float topOffset = 0f)
        {
            var row = ImageRect("Name " + (index + 1), rows!,
                index % 2 == 0 ? Color.white : new Color(0.96f, 0.97f, 0.98f));
            row.raycastTarget = false;
            Stretch(row.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(8f, -(index + 1) * rowHeight + 4f - topOffset),
                new Vector2(-8f, -index * rowHeight - 4f - topOffset));
            var name = TextRect("Original name", row.transform, originalName, 16f,
                Ink, TextAlignmentOptions.MidlineLeft);
            Stretch(name.rectTransform, Vector2.zero, new Vector2(0.43f, 1f),
                new Vector2(16f, 0f), new Vector2(-8f, 0f));

            var fieldImage = ImageRect("New name", row.transform, Color.white);
            fieldImage.gameObject.SetActive(false);
            Stretch(fieldImage.rectTransform, new Vector2(0.43f, 0.13f), new Vector2(1f, 0.87f),
                new Vector2(4f, 0f), new Vector2(-16f, 0f));
            var outline = fieldImage.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.66f, 0.71f, 0.77f);
            outline.effectDistance = new Vector2(1f, 1f);
            var input = fieldImage.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = fieldImage;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 50;
            input.caretColor = Blue;
            input.selectionColor = new Color(0.23f, 0.55f, 0.89f, 0.35f);
            var textViewport = new GameObject("Text viewport", typeof(RectTransform), typeof(RectMask2D))
                .GetComponent<RectTransform>();
            textViewport.SetParent(fieldImage.transform, false);
            Stretch(textViewport, Vector2.zero, Vector2.one,
                new Vector2(12f, 3f), new Vector2(-12f, -3f));
            var inputText = TextRect("Text", textViewport, string.Empty, 16f,
                Ink, TextAlignmentOptions.MidlineLeft);
            Stretch(inputText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            input.textViewport = textViewport;
            input.textComponent = inputText;
            input.text = currentName ?? string.Empty;
            fieldImage.gameObject.SetActive(true);
            return input;
        }

        private void UpdateDropdownCaptions()
        {
            if (employeeNameDropdown != null)
            {
                SetButtonText(employeeNameDropdown, Text("justrenameit_employee_names_label") +
                    GetListCaption(runtime.EmployeeNameListId));
            }
            if (rivalNameDropdown != null)
                SetButtonText(rivalNameDropdown, Text("justrenameit_rival_names_label") +
                    GetListCaption(runtime.RivalNameListId));
        }

        private string GetListCaption(string id)
        {
            if (id == EmployeeNameService.NoneListId) return Text("justrenameit_name_list_none");
            if (id == EmployeeNameService.ChaosListId) return Text("justrenameit_name_list_chaos");
            return runtime.GetNameLists().FirstOrDefault(pair => pair.Key == id).Value ?? id;
        }

        private void OpenNameListDropdown(bool forRivals)
        {
            var anchor = forRivals ? rivalNameDropdown : employeeNameDropdown;
            if (anchor == null) return;
            var options = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(EmployeeNameService.NoneListId,
                    Text("justrenameit_name_list_none")),
                new KeyValuePair<string, string>(EmployeeNameService.ChaosListId,
                    Text("justrenameit_name_list_chaos"))
            };
            options.AddRange(runtime.GetNameLists());
            ShowDropdown(options, anchor, id =>
            {
                if (!runtime.SelectNameList(id, forRivals)) ShowError("justrenameit_no_name_list");
                else JustRenameItLog.Ui($"{(forRivals ? "Rival" : "Employee")} list selected: {id}.");
                UpdateDropdownCaptions();
            });
        }

        private void ShowDropdown(IReadOnlyList<KeyValuePair<string, string>> options, Button anchor,
            Action<string> onSelected)
        {
            HideDropdown();
            if (panelRect == null || options.Count == 0) return;
            var blocker = ImageRect("Dropdown click blocker", panelRect, new Color(0f, 0f, 0f, 0f));
            Stretch(blocker.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            blocker.raycastTarget = true;
            var blockerButton = blocker.gameObject.AddComponent<Button>();
            blockerButton.targetGraphic = blocker;
            blockerButton.onClick.AddListener(HideDropdown);
            dropdownOverlay = blocker.gameObject;
            blocker.transform.SetAsLastSibling();

            var height = Math.Min(6, options.Count) * 46f + 8f;
            var menu = ImageRect("Dropdown choices", blocker.transform, Color.white);
            var menuRect = menu.rectTransform;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(panelRect, anchor.transform);
            menuRect.anchorMin = menuRect.anchorMax = new Vector2(0.5f, 0.5f);
            var openUpwards = bounds.min.y - height < panelRect.rect.yMin + 16f;
            menuRect.pivot = new Vector2(0.5f, openUpwards ? 0f : 1f);
            menuRect.sizeDelta = new Vector2(bounds.size.x, height);
            menuRect.anchoredPosition = new Vector2(bounds.center.x,
                openUpwards ? bounds.max.y + 4f : bounds.min.y - 4f);
            var outline = menu.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.40f, 0.46f, 0.54f);
            outline.effectDistance = new Vector2(1f, -1f);

            var viewport = ImageRect("Choices viewport", menu.transform, Color.white);
            Stretch(viewport.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(4f, 4f), new Vector2(options.Count > 6 ? -18f : -4f, -4f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var listScroll = viewport.gameObject.AddComponent<ScrollRect>();
            listScroll.horizontal = false;
            listScroll.vertical = true;
            listScroll.movementType = ScrollRect.MovementType.Clamped;
            listScroll.scrollSensitivity = 34f;
            listScroll.viewport = viewport.rectTransform;
            var content = new GameObject("Choices", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport.transform, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, options.Count * 46f);
            content.anchoredPosition = Vector2.zero;
            listScroll.content = content;
            for (var index = 0; index < options.Count; index++)
            {
                var option = options[index];
                var button = ButtonRect("Choice " + option.Key, content, option.Value,
                    index % 2 == 0 ? Color.white : new Color(0.95f, 0.96f, 0.97f), Ink,
                    () => { HideDropdown(); onSelected(option.Key); }, 15f);
                Stretch(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
                    new Vector2(0f, -(index + 1) * 46f), new Vector2(0f, -index * 46f));
            }
            if (options.Count > 6)
            {
                var track = ImageRect("Choices scrollbar", menu.transform,
                    new Color(0.76f, 0.80f, 0.85f));
                Stretch(track.rectTransform, new Vector2(1f, 0f), Vector2.one,
                    new Vector2(-16f, 4f), new Vector2(-4f, -4f));
                var handle = ImageRect("Handle", track.transform, Slate);
                Stretch(handle.rectTransform, Vector2.zero, Vector2.one,
                    new Vector2(2f, 2f), new Vector2(-2f, -2f));
                var scrollbar = track.gameObject.AddComponent<Scrollbar>();
                scrollbar.targetGraphic = handle;
                scrollbar.handleRect = handle.rectTransform;
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                listScroll.verticalScrollbar = scrollbar;
            }
        }

        private void HideDropdown()
        {
            if (dropdownOverlay == null) return;
            dropdownOverlay.SetActive(false);
            UnityEngine.Object.Destroy(dropdownOverlay);
            dropdownOverlay = null;
        }

        private static void SetButtonText(Button button, string value)
        {
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = value;
        }

        private static void DecorateDropdown(Button button)
        {
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) Stretch(label.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(8f, 0f), new Vector2(-40f, 0f));
            button.gameObject.AddComponent<Outline>().effectColor = new Color(0.65f, 0.70f, 0.76f);
            var left = ImageRect("Chevron left", button.transform, Ink);
            left.raycastTarget = false;
            left.rectTransform.anchorMin = left.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            left.rectTransform.sizeDelta = new Vector2(2f, 12f);
            left.rectTransform.anchoredPosition = new Vector2(-22f, 0f);
            left.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var right = ImageRect("Chevron right", button.transform, Ink);
            right.raycastTarget = false;
            right.rectTransform.anchorMin = right.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            right.rectTransform.sizeDelta = new Vector2(2f, 12f);
            right.rectTransform.anchoredPosition = new Vector2(-14f, 0f);
            right.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        }

        private void ConfirmEmployees()
        {
            if (SaveGameManager.Current?.EmployeeInstances.Count == 0)
            {
                ShowError("justrenameit_no_employees");
                return;
            }
            if (!runtime.HasEmployeeNameList)
            {
                ShowError("justrenameit_no_name_list");
                return;
            }
            ShowBulkConfirmation("justrenameit_rename_existing_confirm", runtime.RenameExistingEmployees);
        }

        private void ConfirmRivals()
        {
            if (!runtime.HasRivalNameList)
            {
                ShowError("justrenameit_no_name_list");
                return;
            }
            ShowBulkConfirmation("justrenameit_rename_rivals_confirm", runtime.RenameExistingRivals);
        }

        private void ShowBulkConfirmation(string messageKey, Action action,
            string confirmKey = "justrenameit_confirm")
        {
            HideDropdown();
            HideConfirmation();
            if (panelRect == null) return;
            var shade = ImageRect("Bulk rename confirmation blocker", panelRect,
                new Color(0.08f, 0.13f, 0.20f, 0.64f));
            Stretch(shade.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            shade.raycastTarget = true;
            confirmationOverlay = shade.gameObject;
            shade.transform.SetAsLastSibling();

            var card = ImageRect("Confirmation card", shade.transform, new Color(0.95f, 0.96f, 0.97f));
            var cardRect = card.rectTransform;
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(690f, 225f);
            cardRect.anchoredPosition = Vector2.zero;
            var message = TextRect("Confirmation message", card.transform, Text(messageKey), 17f,
                Ink, TextAlignmentOptions.TopLeft);
            message.textWrappingMode = TextWrappingModes.Normal;
            Stretch(message.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(28f, 83f), new Vector2(-28f, -28f));
            var cancel = ButtonRect("Cancel rename", card.transform, Text("justrenameit_cancel"),
                new Color(0.72f, 0.76f, 0.81f), Ink, HideConfirmation, 16f);
            Stretch(cancel.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-354f, 20f), new Vector2(-198f, 67f));
            var confirm = ButtonRect("Confirm action", card.transform, Text(confirmKey),
                Blue, Color.white, () =>
                {
                    HideConfirmation();
                    try { action(); }
                    catch (Exception exception)
                    {
                        JustRenameItLog.Error(exception);
                        ShowError("justrenameit_rename_failed");
                    }
                }, 16f);
            Stretch(confirm.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-184f, 20f), new Vector2(-28f, 67f));
        }

        private void HideConfirmation()
        {
            if (confirmationOverlay == null) return;
            confirmationOverlay.SetActive(false);
            UnityEngine.Object.Destroy(confirmationOverlay);
            confirmationOverlay = null;
        }

        private void ApplyPreset()
        {
            try
            {
                CaptureInputs();
                if (!runtime.ApplyVehiclePreset(out var error))
                {
                    ShowError(error);
                    return;
                }
                foreach (var entry in runtime.GetVehicleNames(true))
                    if (VehicleNameService.VanillaPresets.TryGetValue(entry.Key, out var preset))
                        pendingVehicles[entry.Key] = preset;
                BuildRows();
                if (errorLabel != null) errorLabel.text = string.Empty;
                JustRenameItLog.Vehicles("Vehicle preset names applied from the rename UI.");
            }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
                ShowError("justrenameit_vehicle_failed");
            }
        }

        private void UndoPreset()
        {
            ShowBulkConfirmation("justrenameit_vehicle_preset_undo_confirm", UndoPresetConfirmed,
                "justrenameit_reset_confirm");
        }

        private void UndoPresetConfirmed()
        {
            try
            {
                CaptureInputs();
                if (!runtime.UndoVehiclePreset(out var error))
                {
                    ShowError(error);
                    return;
                }
                foreach (var entry in runtime.GetVehicleNames(true))
                    if (VehicleNameService.VanillaPresets.ContainsKey(entry.Key))
                        pendingVehicles[entry.Key] = entry.CurrentName;
                BuildRows();
                if (errorLabel != null) errorLabel.text = string.Empty;
                JustRenameItLog.Vehicles("Vehicle preset names undone from the rename UI.");
            }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
                ShowError("justrenameit_vehicle_failed");
            }
        }

        private void UndoAllModded()
        {
            ShowBulkConfirmation("justrenameit_vehicle_modded_undo_confirm", UndoAllModdedConfirmed,
                "justrenameit_reset_confirm");
        }

        private void UndoAllModdedConfirmed()
        {
            try
            {
                CaptureInputs();
                var current = runtime.GetVehicleNames(false);
                var hasDrafts = current.Any(entry => pendingVehicles.TryGetValue(entry.Key, out var draft) &&
                    !string.Equals(draft.Trim(), entry.CurrentName, StringComparison.Ordinal));
                if (!runtime.UndoAllModdedVehicleNames(out var error))
                {
                    if (error != "justrenameit_no_changes" || !hasDrafts)
                    {
                        ShowError(error);
                        return;
                    }
                }
                foreach (var entry in runtime.GetVehicleNames(false))
                    pendingVehicles[entry.Key] = entry.CurrentName;
                BuildRows();
                if (errorLabel != null) errorLabel.text = string.Empty;
                JustRenameItLog.Vehicles("All modded vehicle names restored from the rename UI.");
            }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
                ShowError("justrenameit_vehicle_failed");
            }
        }

        private void ConfirmResetProducts()
        {
            ShowBulkConfirmation(showVanillaProducts ? "justrenameit_product_vanilla_reset_confirm" :
                "justrenameit_product_modded_reset_confirm", ResetProductsConfirmed,
                "justrenameit_reset_confirm");
        }

        private void ResetProductsConfirmed()
        {
            try
            {
                CaptureInputs();
                var current = runtime.GetProductNames(showVanillaProducts);
                var hasDrafts = current.Any(entry => pendingProducts.TryGetValue(entry.Key, out var draft) &&
                    !string.Equals(draft.Trim(), entry.CurrentName, StringComparison.Ordinal));
                if (!runtime.ResetProductNames(showVanillaProducts, out var error) &&
                    (error != "justrenameit_no_changes" || !hasDrafts))
                {
                    ShowError(error);
                    return;
                }
                foreach (var entry in runtime.GetProductNames(showVanillaProducts))
                    pendingProducts[entry.Key] = entry.CurrentName;
                BuildRows();
                if (errorLabel != null) errorLabel.text = string.Empty;
            }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
                ShowError("justrenameit_product_failed");
            }
        }

        private void ConfirmResetIngredients()
        {
            ShowBulkConfirmation(showVanillaIngredients ? "justrenameit_ingredient_vanilla_reset_confirm" :
                "justrenameit_ingredient_modded_reset_confirm", ResetIngredientsConfirmed,
                "justrenameit_reset_confirm");
        }

        private void ResetIngredientsConfirmed()
        {
            try
            {
                CaptureInputs();
                var current = runtime.GetIngredientNames(showVanillaIngredients);
                var hasDrafts = current.Any(entry => pendingIngredients.TryGetValue(entry.Key, out var draft) &&
                    !string.Equals(draft.Trim(), entry.CurrentName, StringComparison.Ordinal));
                if (!runtime.ResetIngredientNames(showVanillaIngredients, out var error) &&
                    (error != "justrenameit_no_changes" || !hasDrafts))
                {
                    ShowError(error);
                    return;
                }
                foreach (var entry in runtime.GetIngredientNames(showVanillaIngredients))
                    pendingIngredients[entry.Key] = entry.CurrentName;
                BuildRows();
                if (errorLabel != null) errorLabel.text = string.Empty;
            }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
                ShowError("justrenameit_ingredient_failed");
            }
        }

        private void Save()
        {
            try
            {
                CaptureInputs();
                if (currentTab == RenameTab.General) SaveGeneral();
                else if (currentTab == RenameTab.Employees) SaveEmployees();
                else if (currentTab == RenameTab.Vehicles) SaveVehicles();
                else if (currentTab == RenameTab.Products) SaveProducts();
                else SaveIngredients();
            }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
                ShowError(currentTab == RenameTab.General ? "justrenameit_general_failed" :
                    currentTab == RenameTab.Employees ? "justrenameit_rename_failed" :
                    currentTab == RenameTab.Vehicles ? "justrenameit_vehicle_failed" :
                    currentTab == RenameTab.Products ? "justrenameit_product_failed" :
                    "justrenameit_ingredient_failed");
            }
        }

        private void SaveGeneral()
        {
            var playerName = (pendingPlayerName ?? runtime.GetPlayerName()).Trim();
            var playerChanged = !string.Equals(playerName, runtime.GetPlayerName(), StringComparison.Ordinal);
            var specialChanges = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var rival in specialRivals)
            {
                var proposed = pendingSpecialRivals[rival.Id].Trim();
                if (!string.Equals(proposed, rival.CurrentName, StringComparison.Ordinal))
                    specialChanges[rival.Id] = proposed;
            }
            if (!playerChanged && specialChanges.Count == 0)
            {
                ShowError("justrenameit_no_changes");
                return;
            }
            if (playerChanged && (playerName.Length == 0 || playerName.Length > 50 ||
                                  playerName.Any(char.IsControl)))
            {
                ShowError(playerName.Length == 0 ? "justrenameit_name_empty" : "justrenameit_name_long");
                return;
            }
            if (specialChanges.Count > 0 && !runtime.SaveSpecialRivalNames(specialChanges, out var rivalError))
            {
                ShowError(rivalError);
                return;
            }
            if (playerChanged && !runtime.SavePlayerName(playerName, out var playerError))
            {
                ShowError(playerError);
                return;
            }
            pendingPlayerName = null;
            pendingSpecialRivals.Clear();
            BuildRows();
            if (errorLabel != null) errorLabel.text = string.Empty;
        }

        private void SaveEmployees()
        {
            var changes = new Dictionary<EmployeeInstance, string>();
            foreach (var employee in employees)
            {
                var proposed = pendingEmployees[employee].Trim();
                if (!string.Equals(proposed, employee.characterData.name, StringComparison.Ordinal))
                    changes[employee] = proposed;
            }
            if (changes.Count == 0)
            {
                ShowError("justrenameit_no_changes");
                return;
            }
            JustRenameItLog.Ui("Manual rename submitted for " + changes.Count + " employees.");
            if (!runtime.SaveManualNames(changes, out var error))
            {
                JustRenameItLog.Ui("Manual rename rejected: " + error + ".");
                ShowError(error);
                return;
            }
            pendingEmployees.Clear();
            BuildRows();
            if (errorLabel != null) errorLabel.text = string.Empty;
            try
            {
                var actionUi = UnityEngine.Object.FindObjectOfType<MyEmployeesMassActionsUI>();
                if (actionUi != null) actionUi.OnMassActionPerformed(false, true);
            }
            catch (Exception exception) { JustRenameItLog.Error(exception); }
        }

        private void SaveVehicles()
        {
            var proposed = pendingVehicles.ToDictionary(pair => pair.Key,
                pair => pair.Value.Trim(), StringComparer.Ordinal);
            if (!runtime.SaveVehicleNames(proposed, out var error, out var count))
            {
                ShowError(error);
                return;
            }
            JustRenameItLog.Vehicles("Saved " + count + " vehicle model aliases from the UI.");
            pendingVehicles.Clear();
            BuildRows();
            if (errorLabel != null) errorLabel.text = string.Empty;
        }

        private void SaveProducts()
        {
            var proposed = pendingProducts.ToDictionary(pair => pair.Key,
                pair => pair.Value.Trim(), StringComparer.Ordinal);
            if (!runtime.SaveProductNames(proposed, out var error))
            {
                ShowError(error);
                return;
            }
            pendingProducts.Clear();
            BuildRows();
            if (errorLabel != null) errorLabel.text = string.Empty;
        }

        private void SaveIngredients()
        {
            var proposed = pendingIngredients.ToDictionary(pair => pair.Key,
                pair => pair.Value.Trim(), StringComparer.Ordinal);
            if (!runtime.SaveIngredientNames(proposed, out var error))
            {
                ShowError(error);
                return;
            }
            pendingIngredients.Clear();
            BuildRows();
            if (errorLabel != null) errorLabel.text = string.Empty;
        }

        private void ShowError(string key)
        {
            if (errorLabel != null) errorLabel.text = Text(key);
        }

        private TextMeshProUGUI TextRect(string name, Transform parent, string value, float size,
            Color color, TextAlignmentOptions alignment)
        {
            var result = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI))
                .GetComponent<TextMeshProUGUI>();
            result.transform.SetParent(parent, false);
            if (gameFont != null) result.font = gameFont;
            result.text = value;
            result.fontSize = size;
            result.color = color;
            result.alignment = alignment;
            result.textWrappingMode = TextWrappingModes.NoWrap;
            result.overflowMode = TextOverflowModes.Ellipsis;
            result.raycastTarget = false;
            return result;
        }

        private static Image ImageRect(string name, Transform parent, Color color)
        {
            var result = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            result.transform.SetParent(parent, false);
            result.color = color;
            return result;
        }

        private Button ButtonRect(string name, Transform parent, string caption, Color background,
            Color foreground, Action onClick, float fontSize)
        {
            var image = ImageRect(name, parent, background);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            var label = TextRect("Label", button.transform, caption, fontSize,
                foreground, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(8f, 0f), new Vector2(-8f, 0f));
            return button;
        }

        private static void Stretch(RectTransform rect, Vector2 minAnchor, Vector2 maxAnchor,
            Vector2 minOffset, Vector2 maxOffset)
        {
            rect.anchorMin = minAnchor;
            rect.anchorMax = maxAnchor;
            rect.offsetMin = minOffset;
            rect.offsetMax = maxOffset;
        }

        private static string Text(string key) => key.GetLocalization();
    }
}
