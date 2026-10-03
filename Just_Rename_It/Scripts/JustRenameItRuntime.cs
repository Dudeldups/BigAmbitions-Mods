#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Mods;
using BigAmbitions.ModsInternal;
using Entities;
using JimmysUnityUtilities;
using Localizor;
using Localizor.LanguageChangeEvent;
using TMPro;
using UI;
using UI.Notification;
using UI.Smartphone.Apps.Contacts;
using UI.Smartphone.Apps.MyEmployees;
using UI.Smartphone.Apps.Rivals;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JustRenameIt
{
    internal sealed class JustRenameItRuntime : MonoBehaviour
    {
        private const string CandidateReceivedEvent = "ba:gameevent_candidatereceived";
        private const string ActionType = "justrenameit_mass_action";
        private static readonly BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly FieldInfo? ActionsField = typeof(EmployeeMassActionHelper).GetField("MassActionsDict", StaticPrivate);
        private static readonly FieldInfo? ActionsByTabField = typeof(EmployeeMassActionHelper).GetField("MassActionsByTabDict", StaticPrivate);
        private static readonly BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo? OptionLabelField = typeof(ModOptionsButtonControl).GetField("label", InstancePrivate);
        private static readonly FieldInfo? OptionButtonField = typeof(ModOptionsButtonControl).GetField("button", InstancePrivate);
        private static JustRenameItRuntime? instance;
        private EmployeeNameService? names;
        private RivalNameService? rivalNames;
        private GeneralNameService? generalNames;
        private VehicleNameService? vehicleNames;
        private ProductNameService? productNames;
        private ProductNameService? ingredientNames;
        private BusinessNameService? businessNames;
        private BusinessNameInputHook? businessInputHook;
        private RenameOverlay? overlay;
        private RenameEmployeeMassAction? renameAction;
        private ModOptionsViewController? optionsView;
        private bool stopped;

        internal static JustRenameItRuntime Initialize(ModContext context)
        {
            var runtime = FindObjectOfType<JustRenameItRuntime>();
            if (runtime == null)
            {
                var root = new GameObject(nameof(JustRenameItRuntime));
                DontDestroyOnLoad(root);
                runtime = root.AddComponent<JustRenameItRuntime>();
            }

            runtime.stopped = false;
            runtime.names = new EmployeeNameService(context);
            runtime.rivalNames = new RivalNameService(runtime.names);
            runtime.generalNames = new GeneralNameService();
            runtime.vehicleNames = new VehicleNameService();
            runtime.productNames = new ProductNameService();
            runtime.ingredientNames = new ProductNameService(true);
            runtime.businessNames = new BusinessNameService();
            runtime.businessInputHook = new BusinessNameInputHook(runtime.businessNames);
            runtime.overlay = new RenameOverlay(runtime);
            instance = runtime;
            LocalizorManager.OnLanguageChanged -= runtime.HandleLanguageChanged;
            LocalizorManager.OnLanguageChanged += runtime.HandleLanguageChanged;
            GameEvent.onGameEventTriggered -= runtime.HandleGameEvent;
            GameEvent.onGameEventTriggered += runtime.HandleGameEvent;
            SceneManager.sceneLoaded -= runtime.HandleSceneLoaded;
            SceneManager.sceneLoaded += runtime.HandleSceneLoaded;
            OptionsService.OnChanged -= runtime.HandleOptionsChanged;
            OptionsService.OnChanged += runtime.HandleOptionsChanged;
            runtime.AttachOptionsView();
            GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
            return runtime;
        }

        internal static void OpenFromMassAction() => instance?.OpenManualRename();

        internal void Shutdown()
        {
            if (stopped) return;
            stopped = true;
            LocalizorManager.OnLanguageChanged -= HandleLanguageChanged;
            GameEvent.onGameEventTriggered -= HandleGameEvent;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            OptionsService.OnChanged -= HandleOptionsChanged;
            GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
            GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
            UnregisterMassAction();
            overlay?.Close();
            names?.Unload();
            generalNames?.Unload();
            vehicleNames?.Unload();
            productNames?.Unload();
            ingredientNames?.Unload();
            businessInputHook?.Uninstall();
            if (optionsView != null)
            {
                var hook = optionsView.GetComponent<JustRenameItOptionsViewHook>();
                if (hook != null) Destroy(hook);
                optionsView = null;
            }
            overlay = null;
            names = null;
            rivalNames = null;
            generalNames = null;
            vehicleNames = null;
            productNames = null;
            ingredientNames = null;
            businessNames = null;
            businessInputHook = null;
            if (instance == this) instance = null;
            Destroy(gameObject);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (stopped) return;
            AttachOptionsView();
            businessInputHook?.Scan();
            GlobalEvents.RegisterOnGameLoadedLateCallback(HandleGameLoadedLate);
        }

        private void AttachOptionsView()
        {
            var view = FindObjectOfType<ModOptionsViewController>(true);
            if (view == null || view == optionsView) return;
            optionsView = view;
            var hook = view.GetComponent<JustRenameItOptionsViewHook>();
            if (hook == null) hook = view.gameObject.AddComponent<JustRenameItOptionsViewHook>();
            hook.Initialize(this);
            JustRenameItLog.Ui("Attached Mod Options view hook.");
        }

        private void HandleOptionsChanged()
        {
            if (stopped || optionsView == null) return;
            var hook = optionsView.GetComponent<JustRenameItOptionsViewHook>();
            if (hook != null) hook.Schedule();
        }

        internal void RefreshOptionsButtonLabel()
        {
            if (optionsView == null) return;
            foreach (var control in optionsView.GetComponentsInChildren<ModOptionsButtonControl>(true))
            {
                var label = OptionLabelField?.GetValue(control) as TextLocalizationComponent;
                if (label == null || label.Key != "justrenameit_open_manager") continue;
                var button = OptionButtonField?.GetValue(control) as Button;
                if (button == null) return;
                var localization = button.GetComponentInChildren<TextLocalizationComponent>(true);
                if (localization != null && localization.Key != "justrenameit_action_button")
                    localization.Key = "justrenameit_action_button";
                var text = button.GetComponentInChildren<TMP_Text>(true);
                var caption = "justrenameit_action_button".GetLocalization();
                if (text != null && text.text != caption)
                {
                    text.text = caption;
                    JustRenameItLog.Ui("Mod Options button caption applied: " + caption + ".");
                }
                var legacyText = button.GetComponentInChildren<Text>(true);
                if (legacyText != null && legacyText.text != caption)
                {
                    legacyText.text = caption;
                    JustRenameItLog.Ui("Mod Options legacy button caption applied: " + caption + ".");
                }
                return;
            }
        }

        private void HandleGameLoadedLate()
        {
            if (stopped) return;
            var game = SaveGameManager.Current;
            if (game == null) return;
            GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
            GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
            GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
            GlobalEvents.onGameUnloaded += HandleGameUnloaded;
            JustRenameItLog.General($"Save loaded: employees={game.EmployeeInstances.Count}, " +
                $"candidates={game.CandidateEmployeeInstances.Count}, locale={LocalizorManager.LoadedLocale}.");
            AttachOptionsView();
            businessInputHook?.Scan();
            TryRegisterMassAction();
            names?.Load(game);
            generalNames?.Load(game);
            rivalNames?.Apply(game);
            vehicleNames?.Load(game);
            productNames?.Load(game);
            ingredientNames?.Load(game);
        }

        private void HandleLanguageChanged()
        {
            if (stopped) return;
            JustRenameItLog.General("Active locale: " + LocalizorManager.LoadedLocale + ".");
            vehicleNames?.OnLanguageChanged();
            productNames?.OnLanguageChanged();
            ingredientNames?.OnLanguageChanged();
            generalNames?.OnLanguageChanged();
            overlay?.Close();
            HandleOptionsChanged();
        }

        private void HandleGameUnloaded()
        {
            UnregisterMassAction();
            businessInputHook?.Uninstall();
            overlay?.Close();
            names?.Unload();
            generalNames?.Unload();
            vehicleNames?.Unload();
            productNames?.Unload();
            ingredientNames?.Unload();
            JustRenameItLog.General("Save unloaded.");
        }

        private void HandleFullMenuToggle(bool shown)
        {
            if (stopped || !shown) return;
            businessInputHook?.Scan();
            if (!TryRegisterMassAction())
                JustRenameItLog.Warn("Rename action is unavailable when the full menu opens: employee actions are not loaded.");
            var game = SaveGameManager.Current;
            if (game != null)
            {
                generalNames?.Apply();
                rivalNames?.Apply(game);
            }
            var panel = InstanceBehavior<UIs>.Instance?.fullMenu?.myEmployees;
            if (panel != null && panel.gameObject.activeInHierarchy)
                JustRenameItLog.Ui($"MyEmployees opened: tab={panel.CurrentTab}, " +
                    $"listedEmployees={panel.employeeScrollerController?.data?.Count ?? 0}, " +
                    $"listedCandidates={panel.candidateScrollerController?.data?.Count ?? 0}.");
        }

        private void HandleGameEvent(string eventId)
        {
            if (stopped || eventId != CandidateReceivedEvent || names == null || SaveGameManager.Current == null)
                return;
            var candidates = SaveGameManager.Current.CandidateEmployeeInstances;
            if (candidates.Count == 0) return;
            var candidate = candidates[candidates.Count - 1];
            JustRenameItLog.Names("Candidate received: id=" + candidate.id + ".");
            RenameNewEmployee(candidate);
        }

        private void RenameNewEmployee(EmployeeInstance candidate)
        {
            try
            {
                if (names == null || !names.ShouldReplaceNewName(candidate)) return;
                var used = names.GetUsedNames();
                if (!names.TryCreateName(candidate, used, out var replacement))
                {
                    JustRenameItLog.Warn("No unused employee name remains for candidate " + candidate.id + ".");
                    return;
                }
                if (!TryApplyRenames(new Dictionary<EmployeeInstance, string> { [candidate] = replacement }, out var error))
                    JustRenameItLog.Warn("Candidate rename failed: " + error);
            }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
            }
        }

        internal void RenameExistingEmployees()
        {
            var game = SaveGameManager.Current;
            if (game == null || names == null || !names.HasEmployeeBulkNames)
            {
                Notifications.ShowError("justrenameit_no_name_list", null, true);
                return;
            }

            var employees = game.EmployeeInstances
                .Where(employee => employee?.characterData != null).ToList();
            if (employees.Count == 0)
            {
                Notifications.ShowError("justrenameit_no_employees", null, true);
                return;
            }

            var useVanillaNames = names.EmployeeListId == EmployeeNameService.NoneListId;
            var used = names.GetUsedNames(employees);
            var changes = new Dictionary<EmployeeInstance, string>();
            JustRenameItLog.Rename($"Bulk rename started: employees={employees.Count}, source=" +
                $"{(useVanillaNames ? "game" : names.EmployeeListId)}, usedNames={used.Count}.");
            foreach (var employee in employees)
            {
                string replacement;
                var generated = useVanillaNames
                    ? names.TryCreateVanillaName(employee.characterData!.gender, used, out replacement)
                    : names.TryCreateName(employee, used, out replacement);
                if (!generated)
                {
                    JustRenameItLog.Warn($"Name source exhausted after {changes.Count} planned employees; " +
                        $"usedNames={used.Count}. Existing employees were not changed.");
                    Notifications.ShowError("justrenameit_rename_failed", null, true);
                    return;
                }
                changes.Add(employee, replacement);
            }

            if (changes.Count == 0)
            {
                Notifications.ShowError("justrenameit_no_employees", null, true);
                return;
            }

            if (!TryApplyRenames(changes, out var error, false))
            {
                JustRenameItLog.Warn("Bulk rename failed: " + error);
                Notifications.ShowError(error, null, true);
                return;
            }

            JustRenameItLog.Rename("Renamed " + changes.Count + " existing employees using " +
                (useVanillaNames ? "the game's default name generator." : "list " + names.EmployeeListId + "."));
            NotifySuccess(changes.Count);
        }

        internal void RenameExistingRivals()
        {
            var game = SaveGameManager.Current;
            if (game == null)
            {
                Notifications.ShowError("justrenameit_no_game", null, true);
                return;
            }
            if (names == null || !names.HasRivalBulkNames)
            {
                Notifications.ShowError("justrenameit_no_name_list", null, true);
                return;
            }
            if (rivalNames == null || !rivalNames.RenameAll(game, out var count))
            {
                Notifications.ShowError("justrenameit_rivals_failed", null, true);
                return;
            }
            try
            {
                var leaderboard = FindObjectOfType<RivalLeaderboard>(true);
                if (leaderboard != null && leaderboard.gameObject.activeInHierarchy) leaderboard.Load();
            }
            catch (Exception exception) { JustRenameItLog.Error(exception); }
            Notifications.Show(NotificationType.Success, "justrenameit_rivals_renamed_count",
                new Dictionary<string, string> { ["count"] = count.ToString() },
                4f, null, null, true, true);
        }

        internal void OpenManager()
        {
            if (SaveGameManager.Current == null)
            {
                Notifications.ShowError("justrenameit_no_game", null, true);
                return;
            }
            overlay?.OpenManager();
            businessInputHook?.Scan();
            JustRenameItLog.Ui("Rename manager opened from Mod Options.");
        }

        internal bool HasEmployeeNameList => names?.HasEmployeeBulkNames == true;
        internal bool HasRivalNameList => names?.HasRivalBulkNames == true;
        internal string EmployeeNameListId => names?.EmployeeListId ?? EmployeeNameService.NoneListId;
        internal string RivalNameListId => names?.RivalListId ?? EmployeeNameService.NoneListId;
        internal List<KeyValuePair<string, string>> GetNameLists() =>
            names?.AvailableNameLists.ToList() ?? new List<KeyValuePair<string, string>>();

        internal string GetPlayerName() => generalNames?.GetPlayerName() ?? string.Empty;

        internal bool SavePlayerName(string proposed, out string error)
        {
            if (generalNames == null)
            {
                error = "justrenameit_player_failed";
                return false;
            }
            var result = generalNames.TrySavePlayerName(proposed, out error);
            if (result)
                Notifications.Show(NotificationType.Success, "justrenameit_player_renamed",
                    null, 4f, null, null, true, true);
            return result;
        }

        internal List<SpecialRivalNameEntry> GetSpecialRivalNames() =>
            generalNames?.GetSpecialEntries() ?? new List<SpecialRivalNameEntry>();

        internal bool SaveSpecialRivalNames(IReadOnlyDictionary<string, string> proposed, out string error)
        {
            if (generalNames == null)
            {
                error = "justrenameit_special_rivals_failed";
                return false;
            }
            var result = generalNames.TrySaveSpecialNames(proposed, out error, out var count);
            if (!result) return false;
            try
            {
                var leaderboard = FindObjectOfType<RivalLeaderboard>(true);
                if (leaderboard != null && leaderboard.gameObject.activeInHierarchy) leaderboard.Load();
                var contacts = InstanceBehavior<UIs>.Instance?.fullMenu?.contactsApp;
                if (contacts != null && contacts.gameObject.activeInHierarchy)
                    contacts.LoadContactsList(ContactCategorySelection.SelectedCategory, false);
            }
            catch (Exception exception) { JustRenameItLog.Error(exception); }
            Notifications.Show(NotificationType.Success, "justrenameit_special_rivals_renamed_count",
                new Dictionary<string, string> { ["count"] = count.ToString() },
                4f, null, null, true, true);
            return true;
        }

        internal bool SelectNameList(string id, bool forRivals) => names?.SelectNameList(id, forRivals) == true;

        internal List<VehicleNameEntry> GetVehicleNames(bool vanilla) =>
            vehicleNames?.GetEntries(vanilla) ?? new List<VehicleNameEntry>();

        internal List<ProductNameEntry> GetProductNames(bool vanilla) =>
            productNames?.GetEntries(vanilla) ?? new List<ProductNameEntry>();

        internal List<ProductNameEntry> GetIngredientNames(bool vanilla) =>
            ingredientNames?.GetEntries(vanilla) ?? new List<ProductNameEntry>();

        internal bool SaveProductNames(IReadOnlyDictionary<string, string> changes, out string error)
        {
            if (productNames == null)
            {
                error = "justrenameit_product_failed";
                return false;
            }
            var result = productNames.TrySave(changes, out error, out var count);
            if (result) NotifyProductChanges(count);
            return result;
        }

        internal bool ResetProductNames(bool vanilla, out string error)
        {
            if (productNames == null)
            {
                error = "justrenameit_product_failed";
                return false;
            }
            var result = productNames.TryReset(vanilla, out error, out var count);
            if (result) NotifyProductChanges(count);
            return result;
        }

        internal bool SaveIngredientNames(IReadOnlyDictionary<string, string> changes, out string error)
        {
            if (ingredientNames == null)
            {
                error = "justrenameit_ingredient_failed";
                return false;
            }
            var result = ingredientNames.TrySave(changes, out error, out var count);
            if (result)
                Notifications.Show(NotificationType.Success, "justrenameit_ingredient_renamed_count",
                    new Dictionary<string, string> { ["count"] = count.ToString() },
                    4f, null, null, true, true);
            return result;
        }

        internal bool ResetIngredientNames(bool vanilla, out string error)
        {
            if (ingredientNames == null)
            {
                error = "justrenameit_ingredient_failed";
                return false;
            }
            var result = ingredientNames.TryReset(vanilla, out error, out var count);
            if (result)
                Notifications.Show(NotificationType.Success, "justrenameit_ingredient_renamed_count",
                    new Dictionary<string, string> { ["count"] = count.ToString() },
                    4f, null, null, true, true);
            return result;
        }

        private static void NotifyProductChanges(int count) =>
            Notifications.Show(NotificationType.Success, "justrenameit_product_renamed_count",
                new Dictionary<string, string> { ["count"] = count.ToString() },
                4f, null, null, true, true);

        internal bool SaveVehicleNames(IReadOnlyDictionary<string, string> changes, out string error, out int count)
        {
            if (vehicleNames == null)
            {
                error = "justrenameit_vehicle_failed";
                count = 0;
                return false;
            }
            var result = vehicleNames.TrySave(changes, out error, out count);
            if (result)
                Notifications.Show(NotificationType.Success, "justrenameit_vehicle_renamed_count",
                    new Dictionary<string, string> { ["count"] = count.ToString() },
                    4f, null, null, true, true);
            return result;
        }

        internal bool ApplyVehiclePreset(out string error)
        {
            if (vehicleNames == null)
            {
                error = "justrenameit_vehicle_failed";
                return false;
            }
            var result = vehicleNames.TryApplyPresets(out error, out var count);
            if (result) NotifyVehicleChanges(count);
            return result;
        }

        internal bool UndoVehiclePreset(out string error)
        {
            if (vehicleNames == null)
            {
                error = "justrenameit_vehicle_failed";
                return false;
            }
            var result = vehicleNames.TryUndoPresets(out error, out var count);
            if (result) NotifyVehicleChanges(count);
            return result;
        }

        internal bool UndoAllModdedVehicleNames(out string error)
        {
            if (vehicleNames == null)
            {
                error = "justrenameit_vehicle_failed";
                return false;
            }
            var result = vehicleNames.TryUndoAllModded(out error, out var count);
            if (result) NotifyVehicleChanges(count);
            return result;
        }

        private static void NotifyVehicleChanges(int count) =>
            Notifications.Show(NotificationType.Success, "justrenameit_vehicle_renamed_count",
                new Dictionary<string, string> { ["count"] = count.ToString() },
                4f, null, null, true, true);

        internal void OpenManualRename()
        {
            var selected = MyEmployeesMassActionsUI.massActionSelectedEmployees;
            var game = SaveGameManager.Current;
            if (selected == null || game == null || selected.Count == 0)
            {
                Notifications.ShowError("myemployees_mass_action_no_employees_selected", null, true);
                return;
            }

            var employees = selected.Where(employee => employee != null && game.EmployeeInstances.Contains(employee))
                .Distinct().ToList();
            if (employees.Count == 0)
            {
                Notifications.ShowError("justrenameit_no_employees", null, true);
                return;
            }

            overlay?.Open(employees);
            JustRenameItLog.Ui($"Manual rename opened from Employees mass actions; selected={employees.Count}, " +
                $"ids={string.Join(",", employees.Select(employee => employee.id).ToArray())}.");
        }

        internal bool SaveManualNames(Dictionary<EmployeeInstance, string> changes, out string error)
        {
            if (!TryApplyRenames(changes, out error)) return false;
            NotifySuccess(changes.Count);
            return true;
        }

        private static bool TryApplyRenames(Dictionary<EmployeeInstance, string> changes, out string error,
            bool logEach = true)
        {
            error = "justrenameit_rename_failed";
            var game = SaveGameManager.Current;
            if (game == null || changes.Count == 0) return false;
            var allEmployees = game.EmployeeInstances.Concat(game.CandidateEmployeeInstances).ToList();
            var selected = new HashSet<EmployeeInstance>(changes.Keys);
            var reserved = new HashSet<string>(allEmployees
                .Where(employee => employee != null && !selected.Contains(employee) && employee.characterData != null)
                .Select(employee => employee.characterData.name), StringComparer.OrdinalIgnoreCase);
            var oldNames = new HashSet<string>(changes.Keys.Select(employee => employee.characterData.name),
                StringComparer.OrdinalIgnoreCase);
            var normalized = new Dictionary<EmployeeInstance, string>();

            foreach (var pair in changes)
            {
                if (pair.Key?.characterData == null || !allEmployees.Contains(pair.Key)) return false;
                var newName = (pair.Value ?? string.Empty).Trim();
                if (newName.Length == 0)
                {
                    error = "justrenameit_name_empty";
                    return false;
                }
                if (newName.Length > 50 || newName.Any(char.IsControl))
                {
                    error = "justrenameit_name_long";
                    return false;
                }
                if (!reserved.Add(newName))
                {
                    error = "justrenameit_name_duplicate";
                    return false;
                }
                if (game.Contacts.Any(contact =>
                        string.Equals(contact.id, newName, StringComparison.OrdinalIgnoreCase) &&
                        (!contact.IsEmployeeContact || !oldNames.Contains(contact.id))))
                {
                    error = "justrenameit_name_contact_conflict";
                    return false;
                }
                normalized.Add(pair.Key, newName);
            }

            var oldNameCounts = allEmployees.Where(employee => employee?.characterData != null &&
                    !string.IsNullOrWhiteSpace(employee.characterData.name))
                .GroupBy(employee => employee.characterData.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var oldToNew = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in normalized)
            {
                var oldName = pair.Key.characterData.name;
                if (string.IsNullOrWhiteSpace(oldName) ||
                    !oldNameCounts.TryGetValue(oldName, out var oldNameCount) || oldNameCount > 1)
                {
                    JustRenameItLog.Warn($"Employee name '{oldName}' is empty or shared by " +
                        "multiple employees; its contact cannot be migrated automatically.");
                    continue;
                }
                oldToNew.Add(oldName, pair.Value);
            }

            foreach (var pair in normalized)
            {
                if (logEach)
                    JustRenameItLog.Rename($"Employee id={pair.Key.id}: '{pair.Key.characterData.name}' -> '{pair.Value}'.");
                pair.Key.characterData.name = pair.Value;
            }
            foreach (var contact in game.Contacts.Where(contact => contact.IsEmployeeContact))
                if (oldToNew.TryGetValue(contact.id, out var newName)) contact.id = newName;
            if (game.PlayerDefaults != null &&
                game.PlayerDefaults.contactsLastCategoryName == ContactCategoryName.Employees &&
                game.PlayerDefaults.contactsLastName != null &&
                oldToNew.TryGetValue(game.PlayerDefaults.contactsLastName, out var selectedName))
                game.PlayerDefaults.contactsLastName = selectedName;

            SaveGameManager.MarkChange();
            try
            {
                RefreshUi();
            }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
            }
            error = string.Empty;
            return true;
        }

        private static void RefreshUi()
        {
            var ui = InstanceBehavior<UIs>.Instance;
            if (ui?.fullMenu?.myEmployees != null && ui.fullMenu.myEmployees.gameObject.activeInHierarchy)
                ui.fullMenu.myEmployees.RefreshList();
            if (ui?.fullMenu?.contactsApp != null && ui.fullMenu.contactsApp.gameObject.activeInHierarchy)
                ui.fullMenu.contactsApp.LoadContactsList(ContactCategorySelection.SelectedCategory, false);
        }

        private static void NotifySuccess(int count)
        {
            Notifications.Show(NotificationType.Success, "justrenameit_renamed_count",
                new Dictionary<string, string> { ["count"] = count.ToString() },
                4f, null, null, true, true);
        }

        private bool TryRegisterMassAction()
        {
            try
            {
                var actions = ActionsField?.GetValue(null) as Dictionary<string, EmployeeMassAction>;
                var byTab = ActionsByTabField?.GetValue(null) as Dictionary<string, string[]>;
                if (actions == null || byTab == null || actions.Count == 0 || !byTab.ContainsKey("Employees"))
                    return false;
                if (actions.ContainsKey(ActionType) && byTab["Employees"].Contains(ActionType)) return true;

                if (renameAction == null)
                {
                    renameAction = ScriptableObject.CreateInstance<RenameEmployeeMassAction>();
                    renameAction.type = ActionType;
                    renameAction.supportedTabs = new List<string> { "Employees" };
                }
                actions[ActionType] = renameAction;
                byTab["Employees"] = byTab["Employees"].Concat(new[] { ActionType }).Distinct().ToArray();
                JustRenameItLog.Ui("Registered Rename mass action; Employees dropdown options=" +
                    byTab["Employees"].Length + ".");
                var panel = InstanceBehavior<UIs>.Instance?.fullMenu?.myEmployees;
                if (panel != null && panel.gameObject.activeInHierarchy && panel.CurrentTab == "Employees")
                    panel.massActionsUI.UpdateMassActionDropdown("Employees");
                return true;
            }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
                return false;
            }
        }

        private void UnregisterMassAction()
        {
            var actions = ActionsField?.GetValue(null) as Dictionary<string, EmployeeMassAction>;
            var byTab = ActionsByTabField?.GetValue(null) as Dictionary<string, string[]>;
            actions?.Remove(ActionType);
            if (byTab != null && byTab.TryGetValue("Employees", out var employeeActions))
                byTab["Employees"] = employeeActions.Where(action => action != ActionType).ToArray();
            if (renameAction != null) Destroy(renameAction);
            renameAction = null;
        }
    }

    internal sealed class JustRenameItOptionsViewHook : MonoBehaviour
    {
        private JustRenameItRuntime? runtime;
        private bool scheduled;

        internal void Initialize(JustRenameItRuntime owner)
        {
            runtime = owner;
            Schedule();
        }

        private void OnEnable() => Schedule();

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= Apply;
            scheduled = false;
        }

        internal void Schedule()
        {
            if (scheduled || !isActiveAndEnabled) return;
            scheduled = true;
            Canvas.willRenderCanvases += Apply;
        }

        private void Apply()
        {
            Canvas.willRenderCanvases -= Apply;
            scheduled = false;
            runtime?.RefreshOptionsButtonLabel();
        }
    }

    internal sealed class RenameEmployeeMassAction : EmployeeMassAction
    {
        public override void Perform() => JustRenameItRuntime.OpenFromMassAction();
    }
}
