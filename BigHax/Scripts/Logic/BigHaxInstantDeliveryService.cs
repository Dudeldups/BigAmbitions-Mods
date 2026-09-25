#nullable enable
using System;
using System.Collections;
using System.Reflection;
using Buildings.BuildingTypes.Special.FurnitureStore;
using Entities;
using UI.Smartphone.Apps.BizMan.PurchasingAgent;
using UnityEngine;
using UnityEngine.UI;

namespace BigHax
{
    /// <summary>
    /// Hooks the existing order buttons and completes the order immediately after
    /// vanilla has created or activated it. This service performs no periodic scan.
    /// </summary>
    internal sealed class BigHaxInstantDeliveryService
    {
        private Action<IEnumerator>? startCoroutine;
        private bool instantImportsEnabled;
        private bool instantFurnitureDeliveriesEnabled;

        public void Initialize(Action<IEnumerator> coroutineStarter)
        {
            startCoroutine = coroutineStarter;
            BigHaxInstantDeliveryUiHooks.FurnitureOrderConfirmed = HandleFurnitureOrderConfirmed;
            BigHaxInstantDeliveryUiHooks.ImportOrderConfirmed = HandleImportOrderConfirmed;
            AttachUiHooks();
        }

        public void ApplyConfiguredBehavior(BigHaxSettings settings)
        {
            var importsChanged = instantImportsEnabled != settings.EnableInstantImports;
            var furnitureChanged = instantFurnitureDeliveriesEnabled != settings.EnableInstantFurnitureDeliveries;
            instantImportsEnabled = settings.EnableInstantImports;
            instantFurnitureDeliveriesEnabled = settings.EnableInstantFurnitureDeliveries;

            AttachUiHooks();
            if (importsChanged || furnitureChanged)
            {
                BigHaxLogger.Diagnostic(
                    "Instant deliveries configured: imports=" + instantImportsEnabled +
                    ", furniture=" + instantFurnitureDeliveriesEnabled +
                    ", mode=order-button hooks (no hourly scan).");
            }
        }

        public void AttachUiHooks()
        {
            var furnitureWatchersAdded = 0;
            foreach (var controller in Resources.FindObjectsOfTypeAll<DialogController>())
            {
                if (controller == null)
                    continue;

                foreach (var child in controller.GetComponentsInChildren<Transform>(true))
                {
                    if (child == null || child.name != "Buttons" || child.GetComponent<BigHaxFurnitureDeliveryButtonsWatcher>() != null)
                        continue;

                    child.gameObject.AddComponent<BigHaxFurnitureDeliveryButtonsWatcher>();
                    furnitureWatchersAdded++;
                }
            }

            var importHooksAdded = 0;
            foreach (var planUi in Resources.FindObjectsOfTypeAll<PurchasingAgentPlanUI>())
            {
                if (planUi == null)
                    continue;

                importHooksAdded += AttachImportButton(planUi.orderButton, planUi, "regular");
                importHooksAdded += AttachImportButton(planUi.urgentOrderButton, planUi, "urgent");
            }

            if (furnitureWatchersAdded > 0 || importHooksAdded > 0)
            {
                BigHaxLogger.Diagnostic(
                    "Instant delivery UI hooks attached: furnitureWatchers=" + furnitureWatchersAdded +
                    ", importButtons=" + importHooksAdded +
                    ", furnitureEntryFieldFound=" + BigHaxFurnitureDeliveryButtonsWatcher.CanInspectCurrentEntry +
                    ", importPlanFieldFound=" + BigHaxImportOrderButtonHook.CanInspectCurrentPlan + ".");
            }
        }

        public void Shutdown()
        {
            instantImportsEnabled = false;
            instantFurnitureDeliveriesEnabled = false;
            startCoroutine = null;
            BigHaxInstantDeliveryUiHooks.FurnitureOrderConfirmed = null;
            BigHaxInstantDeliveryUiHooks.ImportOrderConfirmed = null;
        }

        private static int AttachImportButton(Button? button, PurchasingAgentPlanUI owner, string orderKind)
        {
            if (button == null)
                return 0;

            var hook = button.GetComponent<BigHaxImportOrderButtonHook>();
            if (hook != null)
                return 0;

            hook = button.gameObject.AddComponent<BigHaxImportOrderButtonHook>();
            hook.Configure(owner, orderKind);
            return 1;
        }

        private void HandleFurnitureOrderConfirmed()
        {
            if (!instantFurnitureDeliveriesEnabled)
                return;

            if (startCoroutine == null)
            {
                BigHaxLogger.Diagnostic("Instant furniture delivery skipped: coroutine runner unavailable.");
                return;
            }

            startCoroutine(CompleteFurnitureOrderAfterVanillaConfirm());
        }

        private IEnumerator CompleteFurnitureOrderAfterVanillaConfirm()
        {
            // The button callback is added after vanilla's callback, but waiting one
            // frame also makes this robust if Unity changes listener ordering.
            yield return null;

            var saveGame = SaveGameManager.Current;
            var contracts = saveGame?.FurnitureDeliveryContracts;
            if (saveGame == null || contracts == null)
            {
                BigHaxLogger.Diagnostic("Instant furniture delivery skipped: no active save or contract list.");
                yield break;
            }

            var pendingBefore = contracts.Count;
            if (pendingBefore == 0)
            {
                BigHaxLogger.Diagnostic("Instant furniture delivery confirmation observed, but vanilla created no contract.");
                yield break;
            }

            foreach (var contract in contracts)
            {
                if (contract == null)
                    continue;

                contract.dayOfDelivery = saveGame.Day;
                contract.hourOfDelivery = saveGame.Hour;
            }

            try
            {
                FurnitureDeliveryHelper.RunHourly();
                BigHaxLogger.Diagnostic(
                    "Instant furniture delivery executed after order confirmation: pendingBefore=" + pendingBefore +
                    ", pendingAfter=" + contracts.Count +
                    ", delivered=" + (pendingBefore - contracts.Count) +
                    ", day=" + saveGame.Day + ", hour=" + saveGame.Hour + ".");
            }
            catch (Exception exception)
            {
                BigHaxLogger.DiagnosticException("Instant furniture delivery", exception);
            }
        }

        private void HandleImportOrderConfirmed(ImportPartnership? partnership, string orderKind)
        {
            if (!instantImportsEnabled)
                return;

            if (startCoroutine == null)
            {
                BigHaxLogger.Diagnostic("Instant import skipped: coroutine runner unavailable.");
                return;
            }

            startCoroutine(CompleteImportOrderAfterVanillaConfirm(partnership, orderKind));
        }

        private IEnumerator CompleteImportOrderAfterVanillaConfirm(ImportPartnership? partnership, string orderKind)
        {
            yield return null;

            var saveGame = SaveGameManager.Current;
            if (saveGame == null || partnership == null)
            {
                BigHaxLogger.Diagnostic(
                    "Instant import skipped after " + orderKind + " order click: " +
                    (saveGame == null ? "no active save" : "current partnership unavailable") + ".");
                yield break;
            }

            if (!partnership.isActive)
            {
                BigHaxLogger.Diagnostic(
                    "Instant import skipped after " + orderKind +
                    " order click: vanilla did not activate the order.");
                yield break;
            }

            var originalDeliveryDay = partnership.nextDeliveryDay;
            var productCount = partnership.products?.Count ?? 0;
            partnership.nextDeliveryDay = saveGame.Day;

            try
            {
                ImportPartnership.DoAllDeliveries();
                var completed = !partnership.isActive || partnership.nextDeliveryDay != saveGame.Day;
                BigHaxLogger.Diagnostic(
                    "Instant import executed after " + orderKind + " order click: products=" + productCount +
                    ", originalDeliveryDay=" + originalDeliveryDay +
                    ", currentDay=" + saveGame.Day +
                    ", activeAfter=" + partnership.isActive +
                    ", nextDeliveryDayAfter=" + partnership.nextDeliveryDay +
                    ", processed=" + completed + ".");
            }
            catch (Exception exception)
            {
                BigHaxLogger.DiagnosticException("Instant import", exception);
            }
        }
    }

    internal static class BigHaxInstantDeliveryUiHooks
    {
        public static Action? FurnitureOrderConfirmed;
        public static Action<ImportPartnership?, string>? ImportOrderConfirmed;
    }

    internal sealed class BigHaxFurnitureDeliveryButtonsWatcher : MonoBehaviour
    {
        private static readonly FieldInfo? CurrentEntryField = typeof(DialogController).GetField(
            "_currentEntry",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private Coroutine? attachCoroutine;

        public static bool CanInspectCurrentEntry => CurrentEntryField != null;

        private void OnEnable()
        {
            ScheduleAttach();
        }

        private void OnTransformChildrenChanged()
        {
            ScheduleAttach();
        }

        private void ScheduleAttach()
        {
            if (attachCoroutine == null && gameObject.activeInHierarchy)
                attachCoroutine = StartCoroutine(AttachConfirmButtonAfterLayout());
        }

        private IEnumerator AttachConfirmButtonAfterLayout()
        {
            yield return null;
            attachCoroutine = null;

            if (CurrentEntryField == null)
                yield break;

            var controller = GetComponentInParent<DialogController>();
            var entry = controller == null ? null : CurrentEntryField.GetValue(controller) as DialogEntry;
            if (entry == null ||
                entry.InputTemplate != DialogEntry.InputTemplateName.FurnitureDeliverySettings ||
                entry.OnConfirm?.Method.DeclaringType != typeof(Dialogs.FurnitureStoreManagerDialog))
                yield break;

            var buttons = GetComponentsInChildren<Button>(true);
            if (buttons.Length == 0)
                yield break;

            var confirmButton = buttons[buttons.Length - 1];
            if (confirmButton.GetComponent<BigHaxFurnitureDeliveryConfirmHook>() == null)
                confirmButton.gameObject.AddComponent<BigHaxFurnitureDeliveryConfirmHook>();
        }
    }

    internal sealed class BigHaxFurnitureDeliveryConfirmHook : MonoBehaviour
    {
        private Button? button;

        private void Awake()
        {
            button = GetComponent<Button>();
            button?.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            button?.onClick.RemoveListener(HandleClick);
        }

        private static void HandleClick()
        {
            BigHaxInstantDeliveryUiHooks.FurnitureOrderConfirmed?.Invoke();
        }
    }

    internal sealed class BigHaxImportOrderButtonHook : MonoBehaviour
    {
        private static readonly FieldInfo? CurrentPartnershipField = typeof(PurchasingAgentPlanUI).GetField(
            "_currentImportPartnership",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private Button? button;
        private PurchasingAgentPlanUI? owner;
        private string orderKind = "unknown";

        public static bool CanInspectCurrentPlan => CurrentPartnershipField != null;

        public void Configure(PurchasingAgentPlanUI planUi, string kind)
        {
            owner = planUi;
            orderKind = kind;
            button = GetComponent<Button>();
            button?.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            button?.onClick.RemoveListener(HandleClick);
        }

        private void HandleClick()
        {
            ImportPartnership? partnership = null;
            try
            {
                partnership = owner == null || CurrentPartnershipField == null
                    ? null
                    : CurrentPartnershipField.GetValue(owner) as ImportPartnership;
            }
            catch (Exception exception)
            {
                BigHaxLogger.DiagnosticException("Reading current import plan", exception);
            }

            BigHaxInstantDeliveryUiHooks.ImportOrderConfirmed?.Invoke(partnership, orderKind);
        }
    }
}
