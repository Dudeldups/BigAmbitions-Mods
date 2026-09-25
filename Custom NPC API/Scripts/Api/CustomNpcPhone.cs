using System;
using System.Linq;
using BAModAPI;
using BigAmbitions.SaveSystem.Legacy;
using Dialogs;
using Entities;
using UI.Smartphone.Apps.Contacts;

namespace CustomNPCAPI
{
    public sealed class CustomNpcPhoneDefinition
    {
        public string ContactId;
        public string DescriptionKey;
        public string ContactCategory = "General";
        public string DialogTypeKey;
    }

    public static class CustomNpcPhone
    {
        public static Contact EnsureContact(CustomNpcPhoneDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.ContactId))
                throw new ArgumentException("A stable ContactId is required.", nameof(definition));

            var existedBefore = FindContact(definition.ContactId) != null;
            var contact = Contact.GetContact(
                definition.ContactId,
                ResolveCategory(definition.ContactCategory),
                definition.DescriptionKey ?? string.Empty);
            if (contact == null)
                return null;

            var changed = ApplyDialogOverride(contact, definition.DialogTypeKey);
            if (!existedBefore || changed)
                MarkSaveChanged();

            return contact;
        }

        public static Contact FindContact(string contactId)
        {
            if (string.IsNullOrWhiteSpace(contactId))
                return null;

            return SaveGameManager.Current?.Contacts?.FirstOrDefault(contact =>
                contact != null && string.Equals(contact.id, contactId, StringComparison.OrdinalIgnoreCase));
        }

        public static bool ApplyDialogOverride(Contact contact, string dialogTypeKey)
        {
            if (contact == null || string.IsNullOrWhiteSpace(dialogTypeKey))
                return false;

            var dialogType = (CallDialogType)ModEnumHash.GetSafeHash(dialogTypeKey);
            if (contact.callDialogTypeOverride == dialogType)
                return false;

            contact.callDialogTypeOverride = dialogType;
            return true;
        }

        public static void AppendNpcMessage(Contact contact, string messageKey, bool sendNotificationInstantly = false)
        {
            if (contact == null || string.IsNullOrWhiteSpace(messageKey))
                return;

            contact.SendMessage(new TextMessage(messageKey, null, true, true), sendNotificationInstantly);
            MarkSaveChanged();
        }

        public static void AppendPlayerMessage(Contact contact, string messageKey)
        {
            if (contact == null || string.IsNullOrWhiteSpace(messageKey))
                return;

            contact.ReceivePlayerMessage(new TextMessage(messageKey, null, true));
            MarkSaveChanged();
        }

        private static ContactCategoryName ResolveCategory(string configuredValue)
        {
            return !string.IsNullOrWhiteSpace(configuredValue) &&
                   Enum.TryParse(configuredValue, true, out ContactCategoryName category)
                ? category
                : ContactCategoryName.General;
        }

        private static void MarkSaveChanged()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
                return;

            saveGame.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
        }
    }
}
