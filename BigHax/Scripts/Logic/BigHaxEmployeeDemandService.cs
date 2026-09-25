#nullable enable
using System.Collections;
using System;
using System.Reflection;
using BAModAPI;
using Entities;
using Helpers;
using UI.Smartphone.Apps.Contacts;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxEmployeeDemandService
    {
        private const string EmployeeHiredEvent = "ba:gameevent_employeehired";
        private const string CandidateReceivedEvent = "ba:gameevent_candidatereceived";
        private const string NewDayEvent = "ba:gameevent_newday";
        private const string NewDemandMessageKey = "ba:messagetype_employee_contact_message_new_demand";
        private static readonly FieldInfo? ContactsToSendNotificationField =
            typeof(ContactsHelper).GetField("ContactsToSendNotification", BindingFlags.Static | BindingFlags.NonPublic);

        private ModContext? context;
        private bool demandRemovalEnabled;
        private bool maximumSatisfactionEnabled;
        private bool isSubscribed;
        private bool initialDemandRemovalProcessed;
        private bool initialMaximumSatisfactionProcessed;
        private bool dailyCleanupScheduled;
        private bool saveCleanupAfterLoad;
        private Action? scheduleDailyCleanup;

        public void SetDailyCleanupScheduler(Action scheduler)
        {
            scheduleDailyCleanup = scheduler;
            dailyCleanupScheduled = false;
        }

        public void ApplyConfiguredBehavior(ModContext context, BigHaxSettings settings)
        {
            this.context = context;
            demandRemovalEnabled = settings.RemoveEmployeeDemands;
            maximumSatisfactionEnabled = settings.EnableMaximumEmployeeSatisfaction;
            BigHaxLogger.EmployeeDiagnostic(context,
                "Employee hax configured: removeDemands=" + demandRemovalEnabled +
                ", maximumSatisfaction=" + maximumSatisfactionEnabled + ".");
            if (!demandRemovalEnabled)
                initialDemandRemovalProcessed = false;
            if (!maximumSatisfactionEnabled)
                initialMaximumSatisfactionProcessed = false;

            if (!demandRemovalEnabled && !maximumSatisfactionEnabled)
            {
                dailyCleanupScheduled = false;
                Unsubscribe();
                return;
            }

            Subscribe();
            ApplyCurrentSaveEmployeeStateOnce();
        }

        public void InvalidateCache()
        {
            initialDemandRemovalProcessed = false;
            initialMaximumSatisfactionProcessed = false;
            dailyCleanupScheduled = false;
        }

        public void RebindAfterGameLoad(ModContext context, BigHaxSettings settings)
        {
            // GameEvent.ResetStaticData clears its delegate during save loading.
            // Remove first so this also remains safe if the delegate survived.
            GameEvent.onGameEventTriggered -= HandleGameEvent;
            isSubscribed = false;
            InvalidateCache();
            ApplyConfiguredBehavior(context, settings);
            BigHaxLogger.EmployeeDiagnostic(context, "Employee event handler rebound after game load.");
        }

        public bool TryScheduleDailyCleanup()
        {
            if ((!demandRemovalEnabled && !maximumSatisfactionEnabled) || dailyCleanupScheduled)
                return false;

            dailyCleanupScheduled = true;
            return true;
        }

        public IEnumerator ClearNewDemandsAfterDailyUpdate()
        {
            // The game raises onNewDay before its employee loop generates skill-based demands.
            yield return null;
            dailyCleanupScheduled = false;

            if (!demandRemovalEnabled && !maximumSatisfactionEnabled)
                yield break;

            try
            {
                var employees = SaveGameManager.Current?.EmployeeInstances;
                if (employees == null)
                    yield break;

                var clearedEmployeeCount = 0;
                var removedMessageCount = 0;
                var satisfactionRaisedEmployeeCount = 0;
                foreach (var employee in employees)
                {
                    if (demandRemovalEnabled)
                    {
                        var removedDemands = ClearDemands(employee);
                        if (removedDemands > 0)
                        {
                            clearedEmployeeCount++;
                            removedMessageCount += RemoveDemandMessages(employee);
                        }
                    }

                    if (maximumSatisfactionEnabled
                        ? SetMaximumSatisfaction(employee)
                        : demandRemovalEnabled && RaiseSatisfactionForDemandFreeEmployee(employee))
                        satisfactionRaisedEmployeeCount++;
                }

                MarkSaveChangedIfNeeded(clearedEmployeeCount > 0 || removedMessageCount > 0 || satisfactionRaisedEmployeeCount > 0);
                BigHaxLogger.EmployeeDiagnostic(context,
                    "Employee hax daily cleanup: employees=" + employees.Count +
                    ", demandsRemoved=" + clearedEmployeeCount +
                    ", demandMessagesRemoved=" + removedMessageCount +
                    ", satisfactionChanged=" + satisfactionRaisedEmployeeCount +
                    ", " + DescribeSatisfaction(employees));
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
                BigHaxLogger.DiagnosticException("Employee hax daily cleanup", exception);
            }
        }

        public IEnumerator RemoveSavedDemandMessagesAfterLoad()
        {
            // Contact queues can finish restoring after the normal runtime initialization pass.
            yield return null;

            if (!demandRemovalEnabled)
                yield break;

            try
            {
                var employees = SaveGameManager.Current?.EmployeeInstances;
                if (employees == null)
                    yield break;

                var removedMessageCount = 0;
                foreach (var employee in employees)
                    removedMessageCount += RemoveDemandMessages(employee);

                MarkSaveChangedIfNeeded(removedMessageCount > 0);
                SaveDemandCleanupAfterLoadIfNeeded();
                BigHaxLogger.EmployeeDiagnostic(context,
                    "Employee hax post-load message cleanup: employees=" + employees.Count +
                    ", demandMessagesRemoved=" + removedMessageCount +
                    ", " + DescribeSatisfaction(employees));
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
                BigHaxLogger.DiagnosticException("Employee hax post-load cleanup", exception);
            }
        }

        public void Unsubscribe()
        {
            if (!isSubscribed)
                return;

            GameEvent.onGameEventTriggered -= HandleGameEvent;
            isSubscribed = false;
        }

        private void Subscribe()
        {
            if (isSubscribed)
                return;

            GameEvent.onGameEventTriggered += HandleGameEvent;
            isSubscribed = true;
        }

        private void ApplyCurrentSaveEmployeeStateOnce()
        {
            var demandRemovalNeedsInitialPass = demandRemovalEnabled && !initialDemandRemovalProcessed;
            var maximumSatisfactionNeedsInitialPass = maximumSatisfactionEnabled && !initialMaximumSatisfactionProcessed;
            if (!demandRemovalNeedsInitialPass && !maximumSatisfactionNeedsInitialPass)
                return;

            try
            {
                var saveGame = SaveGameManager.Current;
                if (saveGame == null)
                {
                    BigHaxLogger.EmployeeDiagnostic(context, "Employee hax initial pass deferred: no active save.");
                    return;
                }

                var removedDemandCount = 0;
                var removedMessageCount = 0;
                var satisfactionChangedCount = 0;

                var employees = saveGame.EmployeeInstances;
                if (employees != null)
                {
                    foreach (var employee in employees)
                    {
                        if (demandRemovalNeedsInitialPass)
                        {
                            removedDemandCount += ClearDemands(employee);
                            removedMessageCount += RemoveDemandMessages(employee);
                        }

                        if (maximumSatisfactionNeedsInitialPass && SetMaximumSatisfaction(employee))
                            satisfactionChangedCount++;
                    }
                }

                // Recruitment candidates already carry scheduling demands before they are
                // hired, so clear those too while the cheat is enabled.
                var candidates = saveGame.CandidateEmployeeInstances;
                if (demandRemovalNeedsInitialPass && candidates != null)
                {
                    foreach (var candidate in candidates)
                        removedDemandCount += ClearDemands(candidate);
                }

                if (demandRemovalEnabled)
                    initialDemandRemovalProcessed = true;
                if (maximumSatisfactionEnabled)
                    initialMaximumSatisfactionProcessed = true;

                var demandStateChanged = removedDemandCount > 0 || removedMessageCount > 0;
                var changed = demandStateChanged || satisfactionChangedCount > 0;
                MarkSaveChangedIfNeeded(changed);
                saveCleanupAfterLoad |= demandStateChanged;
                BigHaxLogger.EmployeeDiagnostic(context,
                    "Employee hax initial pass: employees=" + (employees?.Count ?? 0) +
                    ", candidates=" + (candidates?.Count ?? 0) +
                    ", demandsRemoved=" + removedDemandCount +
                    ", demandMessagesRemoved=" + removedMessageCount +
                    ", satisfactionChanged=" + satisfactionChangedCount +
                    ", " + DescribeSatisfaction(employees));
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
                BigHaxLogger.DiagnosticException("Employee hax initial pass", exception);
            }
        }

        private void HandleGameEvent(string eventId)
        {
            if (!demandRemovalEnabled && !maximumSatisfactionEnabled)
                return;

            if (eventId == NewDayEvent)
            {
                if (TryScheduleDailyCleanup())
                    scheduleDailyCleanup?.Invoke();

                return;
            }

            if (eventId == CandidateReceivedEvent && demandRemovalEnabled)
            {
                ClearLatestRecruitmentCandidateDemands();
                return;
            }

            if (eventId == EmployeeHiredEvent)
                ApplyEmployeeStateAfterHire();
        }

        private void ClearLatestRecruitmentCandidateDemands()
        {
            try
            {
                var candidates = SaveGameManager.Current?.CandidateEmployeeInstances;
                if (candidates == null || candidates.Count == 0)
                    return;

                // GenerateCandidate has already added the employee when this event fires.
                var candidate = candidates[candidates.Count - 1];
                var removedDemandCount = ClearDemands(candidate);
                MarkSaveChangedIfNeeded(removedDemandCount > 0);
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
                BigHaxLogger.DiagnosticException("Employee hax candidate cleanup", exception);
            }
        }

        private void ApplyEmployeeStateAfterHire()
        {
            try
            {
                var employees = SaveGameManager.Current?.EmployeeInstances;
                if (employees == null || employees.Count == 0)
                    return;

                // Do not assume the freshly hired employee is always the last list entry.
                // Hiring is infrequent, so a full pass is cheap and avoids timing/order issues.
                var removedDemandCount = 0;
                var removedMessageCount = 0;
                var satisfactionChangedCount = 0;
                foreach (var employee in employees)
                {
                    if (demandRemovalEnabled)
                    {
                        removedDemandCount += ClearDemands(employee);
                        removedMessageCount += RemoveDemandMessages(employee);
                    }

                    if (maximumSatisfactionEnabled && SetMaximumSatisfaction(employee))
                        satisfactionChangedCount++;
                }

                MarkSaveChangedIfNeeded(removedDemandCount > 0 || removedMessageCount > 0 || satisfactionChangedCount > 0);
                BigHaxLogger.EmployeeDiagnostic(context,
                    "Employee hax employee hired: employees=" + employees.Count +
                    ", demandsRemoved=" + removedDemandCount +
                    ", demandMessagesRemoved=" + removedMessageCount +
                    ", satisfactionChanged=" + satisfactionChangedCount +
                    ", " + DescribeSatisfaction(employees));
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
                BigHaxLogger.DiagnosticException("Employee hax hired-employee cleanup", exception);
            }
        }

        private static int ClearDemands(EmployeeInstance? employee)
        {
            if (employee?.demands == null)
                return 0;

            var removedDemandCount = employee.demands.Count;
            employee.demands.Clear();
            return removedDemandCount;
        }

        private static bool RaiseSatisfactionForDemandFreeEmployee(EmployeeInstance? employee)
        {
            if (employee?.demands == null || employee.demands.Count != 0 || employee.satisfaction >= 100f)
                return false;

            // Big Ambitions normally changes satisfaction by at most 0.6 per daily update.
            // With an empty demand list, the game's own calculation instead produces a zero delta.
            employee.satisfaction = Mathf.Min(100f, employee.satisfaction + 0.6f);
            return true;
        }

        private static bool SetMaximumSatisfaction(EmployeeInstance? employee)
        {
            if (employee == null || Mathf.Abs(employee.satisfaction - 100f) < 0.001f)
                return false;

            employee.satisfaction = 100f;
            return true;
        }

        private static string DescribeSatisfaction(IList? employees)
        {
            if (employees == null || employees.Count == 0)
                return "satisfaction=none";

            var employeeCount = 0;
            var maximumSatisfactionCount = 0;
            var minimumSatisfaction = float.MaxValue;
            var maximumSatisfaction = float.MinValue;

            foreach (var item in employees)
            {
                if (item is not EmployeeInstance employee)
                    continue;

                employeeCount++;
                minimumSatisfaction = Mathf.Min(minimumSatisfaction, employee.satisfaction);
                maximumSatisfaction = Mathf.Max(maximumSatisfaction, employee.satisfaction);
                if (employee.satisfaction >= 99.999f)
                    maximumSatisfactionCount++;
            }

            if (employeeCount == 0)
                return "satisfaction=none";

            return "satisfaction=min " + minimumSatisfaction.ToString("0.###") +
                   ", max " + maximumSatisfaction.ToString("0.###") +
                   ", at100=" + maximumSatisfactionCount + "/" + employeeCount;
        }

        private static int RemoveDemandMessages(EmployeeInstance? employee)
        {
            if (employee == null)
                return 0;

            var contact = employee.GetContact();
            if (contact?.messagesQueue == null || contact.messagesQueue.Count == 0)
                return 0;

            var retainedMessages = new System.Collections.Generic.Queue<TextMessage>();
            var removedMessageCount = 0;
            while (contact.messagesQueue.Count > 0)
            {
                var message = contact.messagesQueue.Dequeue();
                if (message.messageKey == NewDemandMessageKey)
                {
                    removedMessageCount++;
                    continue;
                }

                retainedMessages.Enqueue(message);
            }

            while (retainedMessages.Count > 0)
                contact.messagesQueue.Enqueue(retainedMessages.Dequeue());

            if (removedMessageCount > 0 && !contact.HasUnreadMessages)
                RemovePendingContactNotification(contact);

            return removedMessageCount;
        }

        private static void RemovePendingContactNotification(Contact contact)
        {
            if (ContactsToSendNotificationField?.GetValue(null) is not IList pendingContacts)
                return;

            for (var index = pendingContacts.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(pendingContacts[index], contact))
                    pendingContacts.RemoveAt(index);
            }
        }

        private static void MarkSaveChangedIfNeeded(bool changed)
        {
            if (!changed || SaveGameManager.Current == null)
                return;

            SaveGameManager.Current.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
        }

        private void SaveDemandCleanupAfterLoadIfNeeded()
        {
            if (!saveCleanupAfterLoad)
                return;

            saveCleanupAfterLoad = false;
            SaveGameManager.Save(SaveGameManager.SaveType.Default, null, null);
        }
    }
}
