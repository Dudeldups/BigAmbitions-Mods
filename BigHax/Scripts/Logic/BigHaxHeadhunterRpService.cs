#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Characters.Skills;
using BigAmbitions.Tags;
using Buildings.Office.Headquarters;
using Entities;
using Entities.Employee.JobDemands;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxHeadhunterRpService
    {
        private const string CandidateReceivedEvent = "ba:gameevent_candidatereceived";
        private const int DiagnosticCalculationLimit = 12;
        private const int CandidateCleanupDetailLogLimit = 5;
        private const int CandidateCleanupSummaryInterval = 100;

        private static readonly FieldInfo? CandidateDemandsToIgnoreField = typeof(EmployeeInstance).GetField(
            "DemandsToIgnore",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? PlanDemandsToIgnoreField = typeof(HeadhunterPlan).GetField(
            "DemandsToIgnore",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static bool enabled;
        private static int diagnosticCalculationCount;
        private static int diagnosticCandidateCount;

        private ModContext? context;
        private BigHaxMethodDetour? candidateDemandsDetour;
        private int candidateCleanupCheckedCount;
        private int candidateCleanupLogCount;
        private int candidateCleanupRemovedDemandCount;
        private int candidateCleanupTouchedCandidateCount;
        private BigHaxMethodDetour? helperDetour;
        private bool isSubscribed;
        private BigHaxMethodDetour? planGetterDetour;

        public void Initialize()
        {
            helperDetour = Install(
                typeof(HeadhunterHelper).GetMethod(nameof(HeadhunterHelper.CalculateMaxDealBreakersPoints), new[] { typeof(float) }),
                typeof(BigHaxHeadhunterRpService).GetMethod(nameof(CalculateMaxDealBreakersPoints), BindingFlags.Static | BindingFlags.NonPublic),
                "headhunter RP/helper");
            planGetterDetour = Install(
                typeof(HeadhunterPlan).GetProperty(nameof(HeadhunterPlan.AvailableDealBreakersPoints))?.GetGetMethod(),
                typeof(BigHaxHeadhunterRpService).GetMethod(nameof(GetAvailableDealBreakersPoints), BindingFlags.Static | BindingFlags.NonPublic),
                "headhunter RP/plan getter");
            candidateDemandsDetour = Install(
                typeof(HeadhunterPlan).GetMethod("GetRandomDemandsForCandidate", BindingFlags.Instance | BindingFlags.NonPublic),
                typeof(BigHaxHeadhunterRpService).GetMethod(nameof(GetRandomDemandsForCandidate), BindingFlags.Static | BindingFlags.NonPublic),
                "headhunter RP/candidate demand generation");
            AttachUiHooks();
        }

        public void ApplyConfiguredBehavior(ModContext context, BigHaxSettings settings)
        {
            this.context = context;
            var changed = enabled != settings.EnableMaximumHeadhunterRecruitmentPoints;
            enabled = settings.EnableMaximumHeadhunterRecruitmentPoints;
            if (changed)
            {
                diagnosticCalculationCount = 0;
                diagnosticCandidateCount = 0;
                ResetCandidateCleanupLogCounters();
                BigHaxLogger.Diagnostic(
                    "Headhunter RP configured: enabled=" + enabled +
                    ", override=" + BigHaxSettings.MaximumHeadhunterRecruitmentPoints +
                    ", helperDetour=" + (helperDetour?.IsApplied == true) +
                    ", planGetterDetour=" + (planGetterDetour?.IsApplied == true) +
                    ", candidateDemandsDetour=" + (candidateDemandsDetour?.IsApplied == true) + ".");
            }

            if (enabled)
                Subscribe();
            else
                Unsubscribe();

            AttachUiHooks();
            BigHaxHeadhunterRpUiHook.RefreshAll();
        }

        public void AttachUiHooks()
        {
            var added = 0;
            foreach (var tab in Resources.FindObjectsOfTypeAll<HeadhuntersRecruitingTab>())
            {
                if (tab == null || tab.GetComponent<BigHaxHeadhunterRpUiHook>() != null)
                    continue;

                tab.gameObject.AddComponent<BigHaxHeadhunterRpUiHook>();
                added++;
            }

            if (added > 0)
                BigHaxLogger.Diagnostic("Headhunter RP UI hooks attached: count=" + added + ".");
        }

        public void Shutdown()
        {
            enabled = false;
            Unsubscribe();
            BigHaxHeadhunterRpUiHook.RefreshAll();
            Restore(candidateDemandsDetour, "headhunter RP/candidate demand generation");
            Restore(planGetterDetour, "headhunter RP/plan getter");
            Restore(helperDetour, "headhunter RP/helper");
            context = null;
            planGetterDetour = null;
            helperDetour = null;
            candidateDemandsDetour = null;
            diagnosticCalculationCount = 0;
            diagnosticCandidateCount = 0;
            ResetCandidateCleanupLogCounters();
        }

        internal static int GetConfiguredPoints(float skill)
        {
            var value = enabled
                ? BigHaxSettings.MaximumHeadhunterRecruitmentPoints
                : Mathf.FloorToInt(skill / 100f * 100f);
            if (diagnosticCalculationCount < DiagnosticCalculationLimit)
            {
                diagnosticCalculationCount++;
                BigHaxLogger.Diagnostic(
                    "Headhunter RP calculated: skill=" + skill +
                    ", enabled=" + enabled +
                    ", availablePoints=" + value + ".");
            }

            return value;
        }

        private static int CalculateMaxDealBreakersPoints(float skill)
        {
            return GetConfiguredPoints(skill);
        }

        private static int GetAvailableDealBreakersPoints(HeadhunterPlan plan)
        {
            return GetConfiguredPoints(plan?.HeadhunterSkillValue ?? 0f);
        }

        private void Subscribe()
        {
            if (isSubscribed)
                return;

            GameEvent.onGameEventTriggered += HandleGameEvent;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
                return;

            GameEvent.onGameEventTriggered -= HandleGameEvent;
            isSubscribed = false;
        }

        private void HandleGameEvent(string eventId)
        {
            if (!enabled || eventId != CandidateReceivedEvent)
                return;

            try
            {
                CleanLatestCandidateDemands();
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
                BigHaxLogger.DiagnosticException("Headhunter candidate demand cleanup", exception);
            }
        }

        private void CleanLatestCandidateDemands()
        {
            var candidates = SaveGameManager.Current?.CandidateEmployeeInstances;
            if (candidates == null || candidates.Count == 0)
                return;

            var candidate = candidates[candidates.Count - 1];
            if (candidate?.demands == null || candidate.demands.Count == 0)
                return;

            var plan = candidate.GetAssignedHeadhunterPlan();
            var demandsToIgnore = plan != null
                ? GetDemandsToIgnore(plan)
                : new List<string>();
            var allPossibleDealBreakersExcluded = plan != null && AreAllPossibleDealBreakersExcluded(plan);
            AddUniqueRange(demandsToIgnore, GetStringListField(CandidateDemandsToIgnoreField, candidate));
            if (!allPossibleDealBreakersExcluded && demandsToIgnore.Count == 0)
            {
                BigHaxLogger.WarnOnce(
                    context,
                    "headhunter-candidate-cleanup-no-exclusions",
                    "BigHax: headhunter candidate demand cleanup found no exclusions for the latest candidate.");
                return;
            }

            var originalDemandCount = candidate.demands.Count;
            if (allPossibleDealBreakersExcluded)
            {
                candidate.demands.Clear();
            }
            else
            {
                for (var index = candidate.demands.Count - 1; index >= 0; index--)
                {
                    if (demandsToIgnore.Contains(candidate.demands[index]))
                        candidate.demands.RemoveAt(index);
                }
            }

            var removedDemandCount = originalDemandCount - candidate.demands.Count;
            candidateCleanupCheckedCount++;
            if (removedDemandCount > 0)
            {
                candidateCleanupTouchedCandidateCount++;
                candidateCleanupRemovedDemandCount += removedDemandCount;
            }

            var shouldLogDetail = candidateCleanupLogCount < CandidateCleanupDetailLogLimit;
            var shouldLogSummary =
                candidateCleanupCheckedCount > 0 &&
                candidateCleanupCheckedCount % CandidateCleanupSummaryInterval == 0;
            var shouldLog = shouldLogDetail || shouldLogSummary;
            if (shouldLog)
            {
                if (shouldLogDetail)
                    candidateCleanupLogCount++;

                var prefix = shouldLogSummary && !shouldLogDetail
                    ? "BigHax: headhunter candidate demand cleanup summary"
                    : "BigHax: checked headhunter candidate demands";
                BigHaxLogger.Info(
                    context,
                    prefix + "; removed=" + removedDemandCount +
                    ", originalDemands=" + originalDemandCount +
                    ", remainingDemands=" + candidate.demands.Count +
                    ", exclusions=" + demandsToIgnore.Count +
                    ", allPossibleDealBreakersExcluded=" + allPossibleDealBreakersExcluded +
                    ", checkedCandidates=" + candidateCleanupCheckedCount +
                    ", touchedCandidates=" + candidateCleanupTouchedCandidateCount +
                    ", totalRemovedDemands=" + candidateCleanupRemovedDemandCount +
                    ", plan=" + (plan?.id ?? "unknown") +
                    ", skill=" + (plan?.skillRecruiting ?? candidate.GetPrimarySkill()) + ".");
            }

            if (removedDemandCount <= 0)
                return;

            SaveGameManager.Current!.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
        }

        private void ResetCandidateCleanupLogCounters()
        {
            candidateCleanupCheckedCount = 0;
            candidateCleanupLogCount = 0;
            candidateCleanupRemovedDemandCount = 0;
            candidateCleanupTouchedCandidateCount = 0;
        }

        private static List<string>? GetRandomDemandsForCandidate(HeadhunterPlan plan, float totalSkillValue)
        {
            var requiredDemandCount = JobDemandHelper.GetIdealNumberOfDemands(plan.skillRecruiting, totalSkillValue);
            var demands = new List<string>();
            if (enabled && AreAllPossibleDealBreakersExcluded(plan))
            {
                LogCandidateResult(plan, totalSkillValue, requiredDemandCount, demands, "all possible deal-breakers excluded by hax", requiredDemandCount);
                return demands;
            }

            var demandsToIgnore = GetDemandsToIgnore(plan);
            var excludedDemandSlotCount = 0;
            if (requiredDemandCount == 0)
            {
                LogCandidateResult(plan, totalSkillValue, requiredDemandCount, demands, "no demands required");
                return demands;
            }

            var skillData = SkillHelper.GetData(plan.skillRecruiting);
            if (skillData == null)
            {
                LogCandidateResult(plan, totalSkillValue, requiredDemandCount, demands, "skill data unavailable");
                return null;
            }

            if (skillData.HasTag(TagRef.Skilltag.forcefulltime))
            {
                if (!TryAddDemandOrAcceptExcluded(
                    plan,
                    totalSkillValue,
                    demands,
                    demandsToIgnore,
                    "ba:jobdemand_fulltime",
                    "forced full-time demand",
                    ref requiredDemandCount,
                    ref excludedDemandSlotCount))
                {
                    return null;
                }
            }
            else if (skillData.HasTag(TagRef.Skilltag.hashoursperweekdemand))
            {
                var excludePartTime = plan.dealBreakerTypes.Contains("ba:headhuntersdealbreaker_parttime");
                var excludeFullTime = plan.dealBreakerTypes.Contains("ba:headhuntersdealbreaker_fulltime");
                if (excludePartTime && excludeFullTime)
                {
                    if (!enabled)
                    {
                        LogCandidateResult(plan, totalSkillValue, requiredDemandCount, demands, "both work schedules excluded; vanilla rejection");
                        return null;
                    }

                    // With the 1000-RP hax, excluding every schedule demand means
                    // this candidate simply has no schedule demand.
                    requiredDemandCount--;
                    excludedDemandSlotCount++;
                }
                else if (excludePartTime)
                {
                    if (!TryAddDemandOrAcceptExcluded(
                        plan,
                        totalSkillValue,
                        demands,
                        demandsToIgnore,
                        "ba:jobdemand_fulltime",
                        "full-time fallback demand",
                        ref requiredDemandCount,
                        ref excludedDemandSlotCount))
                    {
                        return null;
                    }
                }
                else if (excludeFullTime)
                {
                    if (!TryAddDemandOrAcceptExcluded(
                        plan,
                        totalSkillValue,
                        demands,
                        demandsToIgnore,
                        "ba:jobdemand_parttime",
                        "part-time fallback demand",
                        ref requiredDemandCount,
                        ref excludedDemandSlotCount))
                    {
                        return null;
                    }
                }
                else
                {
                    var scheduleDemand = JobDemandHelper.GetRandomHoursPerWeekDemandForSkill(plan.skillRecruiting);
                    if (string.IsNullOrEmpty(scheduleDemand))
                    {
                        LogCandidateResult(plan, totalSkillValue, requiredDemandCount, demands, "schedule demand unavailable");
                        return null;
                    }

                    if (!TryAddDemandOrAcceptExcluded(
                        plan,
                        totalSkillValue,
                        demands,
                        demandsToIgnore,
                        scheduleDemand,
                        "schedule demand",
                        ref requiredDemandCount,
                        ref excludedDemandSlotCount))
                    {
                        return null;
                    }
                }
            }

            var jobSpecificDemand = JobDemandHelper.GetRandomJobSpecificDemandForSkill(plan.skillRecruiting);
            if (!string.IsNullOrEmpty(jobSpecificDemand))
            {
                if (!TryAddDemandOrAcceptExcluded(
                    plan,
                    totalSkillValue,
                    demands,
                    demandsToIgnore,
                    jobSpecificDemand,
                    "job-specific demand",
                    ref requiredDemandCount,
                    ref excludedDemandSlotCount))
                {
                    return null;
                }
            }

            while (requiredDemandCount > 0)
            {
                var demand = JobDemandHelper.GetRandomDemandForSkill(plan.skillRecruiting, demands, demandsToIgnore);
                if (string.IsNullOrEmpty(demand))
                {
                    if (!enabled)
                    {
                        LogCandidateResult(plan, totalSkillValue, requiredDemandCount, demands, "no permitted random demand; vanilla rejection");
                        return null;
                    }

                    // All remaining demands were deliberately excluded. Treat that
                    // as a successful no-demand result instead of stopping recruitment.
                    excludedDemandSlotCount += requiredDemandCount;
                    break;
                }

                demands.Add(demand);
                requiredDemandCount--;
            }

            var result = "candidate demands generated";
            if (excludedDemandSlotCount > 0 || requiredDemandCount > 0)
                result = "excluded demand slots accepted by hax";

            LogCandidateResult(
                plan,
                totalSkillValue,
                requiredDemandCount,
                demands,
                result,
                excludedDemandSlotCount);
            return demands;
        }

        private static List<string> GetDemandsToIgnore(HeadhunterPlan plan)
        {
            var demandsToIgnore = new List<string>();
            AddUniqueRange(demandsToIgnore, GetStringListField(PlanDemandsToIgnoreField, plan));
            if (plan.skillRecruiting == "ba:skill_hrmanager")
                AddUniqueRange(demandsToIgnore, JobDemandHelper.HealthInsuranceDemands);

            foreach (var dealBreakerType in plan.dealBreakerTypes)
            {
                var dealBreaker = HeadhunterHelper.GetData(dealBreakerType);
                if (dealBreaker?.applicableJobDemands != null)
                    AddUniqueRange(demandsToIgnore, dealBreaker.applicableJobDemands);
            }

            return demandsToIgnore;
        }

        private static bool AreAllPossibleDealBreakersExcluded(HeadhunterPlan plan)
        {
            var skillData = SkillHelper.GetData(plan.skillRecruiting);
            var possibleDealBreakers = skillData?.possibleDealbreakers;
            if (possibleDealBreakers == null || possibleDealBreakers.Count == 0)
                return false;

            for (var index = 0; index < possibleDealBreakers.Count; index++)
            {
                var dealBreakerType = possibleDealBreakers[index];
                if (!string.IsNullOrEmpty(dealBreakerType) && !plan.dealBreakerTypes.Contains(dealBreakerType))
                    return false;
            }

            return true;
        }

        private static IEnumerable<string>? GetStringListField(FieldInfo? field, object instance)
        {
            return field?.GetValue(instance) as IEnumerable<string>;
        }

        private static void AddUniqueRange(List<string> destination, IEnumerable<string>? values)
        {
            if (values == null)
                return;

            foreach (var value in values)
            {
                if (!string.IsNullOrEmpty(value) && !destination.Contains(value))
                    destination.Add(value);
            }
        }

        private static bool TryAddDemandOrAcceptExcluded(
            HeadhunterPlan plan,
            float totalSkillValue,
            List<string> demands,
            List<string> demandsToIgnore,
            string demand,
            string source,
            ref int requiredDemandCount,
            ref int excludedDemandSlotCount)
        {
            if (!demandsToIgnore.Contains(demand))
            {
                demands.Add(demand);
                requiredDemandCount--;
                return true;
            }

            if (!enabled)
            {
                LogCandidateResult(plan, totalSkillValue, requiredDemandCount, demands, source + " excluded; vanilla rejection");
                return false;
            }

            requiredDemandCount--;
            excludedDemandSlotCount++;
            return true;
        }

        private static void LogCandidateResult(
            HeadhunterPlan plan,
            float totalSkillValue,
            int remainingDemandCount,
            List<string> demands,
            string result,
            int excludedDemandSlotCount = 0)
        {
            if (diagnosticCandidateCount >= 24)
                return;

            diagnosticCandidateCount++;
            BigHaxLogger.Diagnostic(
                "Headhunter candidate demand result: plan=" + plan.id +
                ", skill=" + plan.skillRecruiting +
                ", totalSkill=" + totalSkillValue +
                ", haxEnabled=" + enabled +
                ", exclusions=" + plan.dealBreakerTypes.Count +
                ", generatedDemands=" + demands.Count +
                ", excludedDemandSlots=" + excludedDemandSlotCount +
                ", remainingDemandSlots=" + remainingDemandCount +
                ", result=" + result +
                ", demands=[" + string.Join(",", demands.ToArray()) + "].");
        }

        private static BigHaxMethodDetour? Install(MethodInfo? target, MethodInfo? replacement, string name)
        {
            if (target == null || replacement == null)
            {
                BigHaxLogger.Diagnostic(name + " detour failed: method not found.");
                return null;
            }

            var detour = new BigHaxMethodDetour(target, replacement);
            if (!detour.Apply(out var error))
            {
                BigHaxLogger.Diagnostic(name + " detour failed: " + error);
                return detour;
            }

            BigHaxLogger.Diagnostic(name + " detour installed.");
            return detour;
        }

        private static void Restore(BigHaxMethodDetour? detour, string name)
        {
            if (detour != null && !detour.Restore(out var error))
                BigHaxLogger.Diagnostic(name + " detour restore skipped: " + error);
        }
    }

    internal sealed class BigHaxHeadhunterRpUiHook : MonoBehaviour
    {
        private static readonly FieldInfo? DealBreakersField = typeof(HeadhuntersRecruitingTab).GetField(
            "dealBreakers",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private Coroutine? refreshCoroutine;

        private void OnEnable()
        {
            ScheduleRefresh();
        }

        public static void RefreshAll()
        {
            foreach (var hook in Resources.FindObjectsOfTypeAll<BigHaxHeadhunterRpUiHook>())
                hook?.ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            if (refreshCoroutine == null && gameObject.activeInHierarchy)
                refreshCoroutine = StartCoroutine(RefreshAfterVanillaSetup());
        }

        private IEnumerator RefreshAfterVanillaSetup()
        {
            yield return null;
            refreshCoroutine = null;

            try
            {
                var tab = GetComponent<HeadhuntersRecruitingTab>();
                var dealBreakers = tab == null || DealBreakersField == null
                    ? null
                    : DealBreakersField.GetValue(tab) as HeadhuntersDealBreakers;
                if (dealBreakers == null)
                {
                    BigHaxLogger.Diagnostic("Headhunter RP UI refresh failed: deal-breaker controller unavailable.");
                    yield break;
                }

                if (tab == null)
                    yield break;

                var planUi = tab.GetComponentInParent<HeadhunterPlanUI>();
                var skill = planUi?.currentPlan?.HeadhunterSkillValue ?? 0f;
                dealBreakers.availableDealBreakersPoints = BigHaxHeadhunterRpService.GetConfiguredPoints(skill);
                BigHaxLogger.Diagnostic(
                    "Headhunter RP UI refreshed: plan=" + (planUi?.currentPlan?.id ?? "none") +
                    ", availablePoints=" + dealBreakers.availableDealBreakersPoints + ".");
            }
            catch (Exception exception)
            {
                BigHaxLogger.DiagnosticException("Headhunter RP UI refresh", exception);
            }
        }
    }
}
