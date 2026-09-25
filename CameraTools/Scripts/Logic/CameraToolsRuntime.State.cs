#nullable enable
using System;
using System.Collections.Generic;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime
    {
        private readonly List<TrackedMemberState> trackedMemberStates = new List<TrackedMemberState>();

        private bool SetTrackedMemberValue(object target, string memberName, object value)
        {
            var existingState = FindTrackedMemberState(target, memberName);
            if (existingState != null)
                return SetMemberValue(target, memberName, value);

            if (!TryGetMemberValue(target, memberName, out var originalValue) || originalValue == null)
                return SetMemberValue(target, memberName, value);

            if (!SetMemberValue(target, memberName, value))
                return false;

            trackedMemberStates.Add(new TrackedMemberState(target, memberName, originalValue));
            return true;
        }

        private bool TryGetTrackedOriginalValue<T>(object target, string memberName, out T value)
        {
            var state = FindTrackedMemberState(target, memberName);
            if (state?.OriginalValue is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default!;
            return false;
        }

        private TrackedMemberState? FindTrackedMemberState(object target, string memberName)
        {
            for (var i = trackedMemberStates.Count - 1; i >= 0; i--)
            {
                var state = trackedMemberStates[i];
                if (ReferenceEquals(state.Target, target) &&
                    string.Equals(state.MemberName, memberName, StringComparison.Ordinal))
                    return state;
            }

            return null;
        }

        private void RestoreTrackedMemberStates()
        {
            if (trackedMemberStates.Count == 0)
                return;

            var restoredCount = 0;
            var failedCount = 0;
            for (var i = trackedMemberStates.Count - 1; i >= 0; i--)
            {
                var state = trackedMemberStates[i];
                if (!IsTrackedTargetAlive(state.Target))
                    continue;

                if (SetMemberValue(state.Target, state.MemberName, state.OriginalValue))
                    restoredCount++;
                else
                    failedCount++;
            }

            trackedMemberStates.Clear();
            context?.Logger.Info($"CameraTools: restored native camera state; members={restoredCount}, failures={failedCount}.");
            if (failedCount > 0)
                context?.Logger.Warn($"CameraTools: {failedCount} native camera member(s) could not be restored during shutdown.");
        }

        private static bool IsTrackedTargetAlive(object target)
        {
            return target is not UnityEngine.Object unityObject || unityObject != null;
        }

        private sealed class TrackedMemberState
        {
            public TrackedMemberState(object target, string memberName, object originalValue)
            {
                Target = target;
                MemberName = memberName;
                OriginalValue = originalValue;
            }

            public object Target { get; }

            public string MemberName { get; }

            public object OriginalValue { get; }
        }
    }
}
