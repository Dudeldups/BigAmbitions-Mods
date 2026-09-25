#nullable enable
using System;
using BAModAPI;

namespace BigHax
{
    internal sealed class BigHaxRecruitmentCandidateService
    {
        private const string CandidateReceivedEvent = "ba:gameevent_candidatereceived";

        private ModContext? context;
        private bool isEnabled;
        private bool isSubscribed;

        public void ApplyConfiguredMaximum(ModContext context, BigHaxSettings settings)
        {
            this.context = context;
            isEnabled = settings.EnableRecruitmentCandidateMaximumSkill;
            Subscribe();
        }

        public void Unsubscribe()
        {
            if (!isSubscribed)
                return;

            GameEvent.onGameEventTriggered -= HandleGameEvent;
            isSubscribed = false;
        }

        public void RebindAfterGameLoad(ModContext context, BigHaxSettings settings)
        {
            GameEvent.onGameEventTriggered -= HandleGameEvent;
            isSubscribed = false;
            ApplyConfiguredMaximum(context, settings);
        }

        private void Subscribe()
        {
            if (isSubscribed)
                return;

            GameEvent.onGameEventTriggered += HandleGameEvent;
            isSubscribed = true;
        }

        private void HandleGameEvent(string eventId)
        {
            if (!isEnabled || eventId != CandidateReceivedEvent)
                return;

            try
            {
                var candidates = SaveGameManager.Current?.CandidateEmployeeInstances;
                if (candidates == null || candidates.Count == 0)
                    return;

                // The event is emitted directly after GenerateCandidate adds this employee.
                var candidate = candidates[candidates.Count - 1];
                if (candidate == null)
                    return;

                DoubleCandidateSkills(candidate);
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
            }
        }

        private static void DoubleCandidateSkills(Entities.EmployeeInstance candidate)
        {
            foreach (var skill in candidate.characterData.skills)
            {
                skill.value = Math.Min(100f, skill.value * 2f);
            }
        }
    }
}
