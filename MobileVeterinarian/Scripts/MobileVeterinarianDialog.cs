#nullable enable
using System.Collections.Generic;
using Dialogs;
using Entities;
using Extensions;
using Localizor;

namespace MobileVeterinarian
{
    internal sealed class MobileVeterinarianDialog : Dialog
    {
        internal MobileVeterinarianDialog()
        {
            npcNameKey = MobileVeterinarianRuntime.ContactNameKey;
            var runtime = MobileVeterinarianRuntime.Current;
            DialogController.current.ShowEntry(runtime != null
                ? runtime.CreateInitialDialogEntry()
                : CreateTerminalEntry(MobileVeterinarianRuntime.ServiceUnavailableKey));
        }

        internal static DialogEntry CreateQuoteEntry(TreatmentQuote quote)
        {
            var data = quote.CreateLocalizationData();
            SendNpcMessage(MobileVeterinarianRuntime.TreatmentPriceKey, data);
            return new DialogEntry
            {
                headerKey = MobileVeterinarianRuntime.ContactNameKey,
                messageData = MobileVeterinarianRuntime.TreatmentPriceKey.Localize(data),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "mobileveterinarian:request_treatment".Localize(
                    new Dictionary<string, string> { { "price", quote.Price.ToShortCurrencyFormat() } }),
                OnConfirm = () => ConfirmQuote(quote),
                OnCancel = DialogController.current.CancelDialog,
                onCancelMessage = new TextMessage("ba:messagetype_contacts_message_player_cancel_call")
            };
        }

        internal static DialogEntry CreateTerminalEntry(
            string localizationKey,
            Dictionary<string, string>? data = null)
        {
            SendNpcMessage(localizationKey, data);
            return new DialogEntry
            {
                headerKey = MobileVeterinarianRuntime.ContactNameKey,
                messageData = localizationKey.Localize(data),
                Template = DialogEntry.TemplateType.Text,
                OnVisible = DialogController.current.FinishDialog,
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private static DialogEntry ConfirmQuote(TreatmentQuote quote)
        {
            var runtime = MobileVeterinarianRuntime.Current;
            if (runtime == null)
                return CreateTerminalEntry(MobileVeterinarianRuntime.ServiceUnavailableKey);

            if (!runtime.TryStartVisit(quote, out var failureKey, out var failureData))
                return CreateTerminalEntry(failureKey, failureData);

            var dispatchedData = new Dictionary<string, string> { { "animal", quote.AnimalName } };
            return CreateTerminalEntry("mobileveterinarian:visit_dispatched", dispatchedData);
        }

        private static void SendNpcMessage(string localizationKey, Dictionary<string, string>? data)
        {
            var contact = DialogController.current?.contact;
            if (contact == null)
                return;

            contact.SendMessage(new TextMessage(localizationKey, data, true, true), false);
        }
    }
}
