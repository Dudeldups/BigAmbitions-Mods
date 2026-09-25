#nullable enable
using System;
using Entities;

namespace BigHax
{
    internal sealed class BigHaxUnlockService
    {
        private const string ConfirmationTitleKey = "bighax_unlock_confirmation_title";
        private const string ContactsConfirmationBodyKey = "bighax_unlock_all_contacts_confirmation";
        private const string CoursesConfirmationBodyKey = "bighax_unlock_all_courses_confirmation";
        private const string ConfirmationButtonKey = "bighax_unlock_confirmation_button";
        private const string CancelButtonKey = "common_cancel";

        public void ConfirmUnlockAllContacts() => ShowIrreversibleConfirmation(ContactsConfirmationBodyKey, UnlockAllContacts);

        public void ConfirmUnlockAllCourses() => ShowIrreversibleConfirmation(CoursesConfirmationBodyKey, UnlockAllCourses);

        private static void ShowIrreversibleConfirmation(string bodyKey, Action onConfirmed)
        {
            if (HudConfirm.isOpen)
            {
                BigHaxLogger.Diagnostic("Unlock hax confirmation was not shown because another confirmation is already open.");
                return;
            }

            // HudConfirm executes callbacks directly when no dialog is registered. These
            // actions are intentionally irreversible, so do not fall back to that behavior.
            if (HudConfirm.onShow == null)
            {
                BigHaxLogger.Diagnostic("Unlock hax confirmation was not shown because HudConfirm is unavailable.");
                return;
            }

            HudConfirm.Show(
                ConfirmationTitleKey,
                bodyKey,
                onConfirmed,
                null,
                ConfirmationButtonKey,
                CancelButtonKey,
                allowConfirmationSkip: false);
        }

        private static void UnlockAllContacts()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.gameVariables == null)
            {
                BigHaxLogger.Diagnostic("Unlock all contacts skipped: no active save.");
                return;
            }

            var contactsBefore = saveGame.Contacts?.Count ?? 0;
            saveGame.gameVariables.allContactsUnlocked = true;
            ContactsHelper.UnlockAllContacts();
            var contactsAfter = saveGame.Contacts?.Count ?? 0;

            saveGame.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
            BigHaxLogger.Diagnostic("Unlock all contacts completed: contactsBefore=" + contactsBefore + ", contactsAfter=" + contactsAfter + ".");
        }

        private static void UnlockAllCourses()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.gameVariables == null)
            {
                BigHaxLogger.Diagnostic("Unlock all courses skipped: no active save.");
                return;
            }

            var coursesBefore = CountCompletedCourses(saveGame);
            saveGame.gameVariables.allCoursesUnlocked = true;
            EducationHelper.UnlockAllCourses();
            var coursesAfter = CountCompletedCourses(saveGame);

            saveGame.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
            BigHaxLogger.Diagnostic("Unlock all courses completed: completedBefore=" + coursesBefore + ", completedAfter=" + coursesAfter + ".");
        }

        private static int CountCompletedCourses(GameInstance saveGame)
        {
            if (saveGame.PlayerDiplomas == null)
                return 0;

            var completed = 0;
            foreach (var diploma in saveGame.PlayerDiplomas)
            {
                if (diploma.completed)
                    completed++;
            }

            return completed;
        }
    }
}
