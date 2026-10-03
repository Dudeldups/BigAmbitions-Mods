#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UI.Components;
using UI.Notification;
using UI.Smartphone.Apps.BizMan.StartBusiness;
using UnityEngine;
using UnityEngine.UI;

namespace JustRenameIt
{
    /// <summary>
    /// Wraps the existing game buttons. The game validates names as file names,
    /// so its save method receives a temporary slash-free name. After that save
    /// succeeds, the display name is restored while retaining the safe logo path.
    /// </summary>
    internal sealed class BusinessNameInputHook
    {
        private const string BusinessSavedEvent = "ba:gameevent_newbusiness";
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo? StartNameField = typeof(StartBusinessUI)
            .GetField("businessNameField", PrivateInstance);
        private static readonly FieldInfo? StartRegistrationField = typeof(StartBusinessUI)
            .GetField("_buildingRegistration", PrivateInstance);
        private static readonly FieldInfo? SettingsNameField = typeof(BizManSettings)
            .GetField("businessName", PrivateInstance);
        private static readonly FieldInfo? SettingsButtonField = typeof(BizManSettings)
            .GetField("saveChangesButton", PrivateInstance);
        private static readonly FieldInfo? SettingsBusinessField = typeof(BizManSettings)
            .GetField("_bizManBusiness", PrivateInstance);
        private static readonly FieldInfo? KeyboardSubmitField = typeof(KeyboardInputHandler)
            .GetField("_onSubmit", PrivateInstance);

        private readonly BusinessNameService service;
        private readonly Dictionary<Button, ButtonHook> buttons = new Dictionary<Button, ButtonHook>();
        private readonly Dictionary<KeyboardInputHandler, SubmitHook> submitHandlers =
            new Dictionary<KeyboardInputHandler, SubmitHook>();
        private readonly List<Action<Address>> pendingLogoHandlers = new List<Action<Address>>();
        private readonly List<StartBusinessActivationHook> activationHooks =
            new List<StartBusinessActivationHook>();
        private readonly HashSet<int> missingStartButtons = new HashSet<int>();

        internal BusinessNameInputHook(BusinessNameService service) => this.service = service;

        internal void Scan()
        {
            foreach (var view in UnityEngine.Object.FindObjectsOfType<StartBusinessUI>(true))
            {
                var activation = view.GetComponent<StartBusinessActivationHook>();
                if (activation == null) activation = view.gameObject.AddComponent<StartBusinessActivationHook>();
                activation.Owner = this;
                if (!activationHooks.Contains(activation)) activationHooks.Add(activation);
                InstallStartBusiness(view);
            }

            foreach (var view in UnityEngine.Object.FindObjectsOfType<BizManSettings>(true))
            {
                var input = SettingsNameField?.GetValue(view) as TMP_InputField;
                var save = SettingsButtonField?.GetValue(view) as Button;
                ExtendInput(input);
                if (save != null && input != null)
                    Install(save, input, () =>
                        (SettingsBusinessField?.GetValue(view) as BizManBusiness)?.buildingRegistration);
                else
                    JustRenameItLog.Warn("BizMan save button was not found; slash names cannot be saved there.");
            }
            JustRenameItLog.Businesses("Native business save hooks active: " + buttons.Count + ".");
        }

        internal void Uninstall()
        {
            foreach (var handler in pendingLogoHandlers.ToArray())
                GlobalEvents.onBuildingRegistrationChange -= handler;
            pendingLogoHandlers.Clear();
            foreach (var activation in activationHooks)
                if (activation != null) activation.Owner = null;
            activationHooks.Clear();
            foreach (var hook in buttons.Values)
                if (hook.Button != null && ReferenceEquals(hook.Button.onClick, hook.Replacement))
                    hook.Button.onClick = hook.Original;
            buttons.Clear();
            foreach (var hook in submitHandlers.Values)
                if (hook.Handler != null &&
                    ReferenceEquals(KeyboardSubmitField?.GetValue(hook.Handler), hook.Replacement))
                    KeyboardSubmitField?.SetValue(hook.Handler, hook.Original);
            submitHandlers.Clear();
            missingStartButtons.Clear();
        }

        internal void InstallStartBusiness(StartBusinessUI view)
        {
            var input = StartNameField?.GetValue(view) as TMP_InputField;
            ExtendInput(input);
            var save = FindStartButton(view);
            if (save != null && input != null)
                Install(save, input, () => StartRegistrationField?.GetValue(view) as BuildingRegistration);
            var submit = input?.GetComponent<KeyboardInputHandler>();
            if (submit != null && input != null)
                Install(submit, input, () => StartRegistrationField?.GetValue(view) as BuildingRegistration);
            if (view.isActiveAndEnabled && (save == null || !buttons.ContainsKey(save)) &&
                (submit == null || !submitHandlers.ContainsKey(submit)) &&
                missingStartButtons.Add(view.GetInstanceID()))
                JustRenameItLog.Warn("Start Business save controls were not found; slash names cannot be created there.");
        }

        private static void ExtendInput(TMP_InputField? input)
        {
            if (input == null || input.characterLimit == BusinessNameService.NameLimit) return;
            input.characterLimit = BusinessNameService.NameLimit;
            JustRenameItLog.Businesses("Extended a native business name field to 40 characters.");
        }

        private static Button? FindStartButton(StartBusinessUI view)
        {
            var parent = view.GetComponentInParent<BizManPresentation>();
            var buttons = parent != null
                ? parent.GetComponentsInChildren<Button>(true)
                : view.GetComponentsInChildren<Button>(true);
            foreach (var button in buttons)
                for (var index = 0; index < button.onClick.GetPersistentEventCount(); index++)
                    if (button.onClick.GetPersistentTarget(index) == view &&
                        button.onClick.GetPersistentMethodName(index) == nameof(StartBusinessUI.SetUpBusiness))
                        return button;
            return null;
        }

        private void Install(Button button, TMP_InputField input,
            Func<BuildingRegistration?> getRegistration)
        {
            if (buttons.TryGetValue(button, out var existing))
            {
                if (ReferenceEquals(button.onClick, existing.Replacement)) return;
                buttons.Remove(button);
            }
            var original = button.onClick;
            var replacement = new Button.ButtonClickedEvent();
            replacement.AddListener(() => HandleSave(original.Invoke, input, getRegistration));
            button.onClick = replacement;
            buttons.Add(button, new ButtonHook(button, original, replacement));
            JustRenameItLog.Businesses("Wrapped native business save button: " + button.name + ".");
        }

        private void Install(KeyboardInputHandler handler, TMP_InputField input,
            Func<BuildingRegistration?> getRegistration)
        {
            if (submitHandlers.TryGetValue(handler, out var existing))
            {
                if (ReferenceEquals(KeyboardSubmitField?.GetValue(handler), existing.Replacement)) return;
                submitHandlers.Remove(handler);
            }
            var original = KeyboardSubmitField?.GetValue(handler) as Action;
            if (original == null) return;
            Action replacement = () => HandleSave(original, input, getRegistration);
            KeyboardSubmitField?.SetValue(handler, replacement);
            submitHandlers.Add(handler, new SubmitHook(handler, original, replacement));
            JustRenameItLog.Businesses("Wrapped native business keyboard submit.");
        }

        private void HandleSave(Action original, TMP_InputField input,
            Func<BuildingRegistration?> getRegistration)
        {
            var registration = getRegistration();
            if (registration == null || input == null)
            {
                original();
                return;
            }
            var requested = input.text.Trim();
            if (!requested.Contains('/') || string.Equals(requested, registration.BusinessName,
                    StringComparison.Ordinal))
            {
                original();
                return;
            }
            if (!service.TryPrepareNativeName(requested, registration, out var temporary, out var error))
            {
                Notifications.ShowError(error, null, true);
                JustRenameItLog.Businesses("Native slash name rejected: " + error + ".");
                return;
            }

            // A previous native save may already have left the slash-free name behind.
            // Saving that same temporary value again produces no native save event.
            if (string.Equals(temporary, registration.BusinessName, StringComparison.Ordinal))
            {
                if (!service.TrySave(new Dictionary<BuildingRegistration, string>
                        { [registration] = requested }, out error, out _))
                {
                    JustRenameItLog.Warn("Could not restore a slash in an existing business name: " + error + ".");
                    Notifications.ShowError(error, null, true);
                    return;
                }
                Notifications.Show(NotificationType.Success, "justrenameit_business_saved",
                    null, 4f, null, null, true, true);
                JustRenameItLog.Businesses("Existing slash-free business name restored to '" + requested + "'.");
                return;
            }

            var saved = false;
            var originalRunning = true;
            var changingName = false;
            var originalLogoFinished = false;
            Action<string> onGameEvent = eventId =>
            {
                if (eventId == BusinessSavedEvent) saved = true;
            };
            Action<Address>? onLogo = null;
            onLogo = address =>
            {
                if (address != registration.Address || changingName) return;
                originalLogoFinished = true;
                if (originalRunning || onLogo == null) return;
                RemoveLogoHandler(onLogo);
                service.RefreshLogoAfterNativeSave(registration);
            };
            GlobalEvents.onBuildingRegistrationChange += onLogo;
            pendingLogoHandlers.Add(onLogo);
            GameEvent.onGameEventTriggered += onGameEvent;
            input.SetTextWithoutNotify(temporary);
            try
            {
                original();
            }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
                Notifications.ShowError("justrenameit_business_failed", null, true);
            }
            finally
            {
                originalRunning = false;
                GameEvent.onGameEventTriggered -= onGameEvent;
                if (input != null) input.SetTextWithoutNotify(requested);
            }

            if (!saved)
            {
                RemoveLogoHandler(onLogo);
                JustRenameItLog.Businesses("Native business save did not complete; original name retained.");
                return;
            }
            changingName = true;
            var renamed = service.TrySave(
                new Dictionary<BuildingRegistration, string> { [registration] = requested },
                out error, out _, originalLogoFinished);
            changingName = false;
            if (!renamed)
            {
                RemoveLogoHandler(onLogo);
                JustRenameItLog.Warn("Native business save completed, but slash restoration failed: " + error + ".");
                Notifications.ShowError(error, null, true);
                return;
            }
            if (originalLogoFinished) RemoveLogoHandler(onLogo);
            JustRenameItLog.Businesses("Native business name saved with slash: '" + requested + "'.");
        }

        private void RemoveLogoHandler(Action<Address> handler)
        {
            GlobalEvents.onBuildingRegistrationChange -= handler;
            pendingLogoHandlers.Remove(handler);
        }

        private sealed class ButtonHook
        {
            internal ButtonHook(Button button, Button.ButtonClickedEvent original,
                Button.ButtonClickedEvent replacement)
            {
                Button = button;
                Original = original;
                Replacement = replacement;
            }

            internal Button Button { get; }
            internal Button.ButtonClickedEvent Original { get; }
            internal Button.ButtonClickedEvent Replacement { get; }
        }

        private sealed class SubmitHook
        {
            internal SubmitHook(KeyboardInputHandler handler, Action original, Action replacement)
            {
                Handler = handler;
                Original = original;
                Replacement = replacement;
            }

            internal KeyboardInputHandler Handler { get; }
            internal Action Original { get; }
            internal Action Replacement { get; }
        }
    }

    internal sealed class StartBusinessActivationHook : MonoBehaviour
    {
        internal BusinessNameInputHook? Owner;

        private void OnEnable()
        {
            var view = GetComponent<StartBusinessUI>();
            if (view != null) Owner?.InstallStartBusiness(view);
        }
    }
}
