#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using PlayerActivity;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxSleepRestDurationService
    {
        private const int ExtendedBenchRestMinutes = 24 * 60;
        private const int ExtendedBedSleepMinutes = 7 * 24 * 60;
        private static readonly string[] RestBehaviourTypeNames =
        {
            "OutsideBenchController",
            "OutsideChairController",
            "Controllers.OutsideInteractableItemToRest",
            "Controllers.SeatController"
        };
        private static readonly string[] BedBehaviourTypeNames = { "BedController" };
        private static readonly BindingFlags InstanceFieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo? ActiveBalanceConfigMaxDurationField =
            typeof(PlayerActivityBalanceConfig).GetField("maxDurationMinutes", InstanceFieldFlags);

        private readonly Dictionary<int, OriginalRestDurations> originalEnvironmentDurationsByKey =
            new Dictionary<int, OriginalRestDurations>();
        private readonly Dictionary<int, int> originalBedConfigMaxMinutesByKey = new Dictionary<int, int>();
        private readonly Dictionary<int, ActiveBedDuration> originalActiveBedDurationsByKey =
            new Dictionary<int, ActiveBedDuration>();
        private readonly Dictionary<string, EnvironmentPatchDescriptor?> descriptorCache = new Dictionary<string, EnvironmentPatchDescriptor?>();
        private readonly Dictionary<string, Type?> targetTypeCache = new Dictionary<string, Type?>(StringComparer.Ordinal);
        private int loggedPatchExceptions;
        private object? lastAppliedSaveGame;
        private bool? lastAppliedExtendedBedSetting;

        public void InvalidateCache()
        {
            // Scene loading replaces the controllers even when the save and setting stay the same.
            lastAppliedSaveGame = null;
            lastAppliedExtendedBedSetting = null;
        }

        public bool NeedsSettingsApply(BigHaxSettings settings)
        {
            return !ReferenceEquals(lastAppliedSaveGame, SaveGameManager.Current) ||
                   !lastAppliedExtendedBedSetting.HasValue ||
                   lastAppliedExtendedBedSetting.Value != settings.EnableExtendedBedSleep;
        }

        public bool PatchActiveBedSleep(SleepActivity activity, ModContext context)
        {
            var environmentField = typeof(SleepActivity).GetField("_sleepEnvironment", InstanceFieldFlags);
            if (environmentField?.GetValue(activity) is not SleepEnvironment environment ||
                environment.SleepEnvironmentType != SleepEnvironmentType.Bed)
                return false;

            var balanceConfig = environment.BalanceConfig;
            if (balanceConfig == null || ActiveBalanceConfigMaxDurationField == null)
                return false;

            var previousMax = (int)ActiveBalanceConfigMaxDurationField.GetValue(balanceConfig);
            var previousDefault = environment.GetDefaultMinutes();
            if (previousMax >= ExtendedBedSleepMinutes && previousDefault >= ExtendedBedSleepMinutes)
                return false;

            var key = balanceConfig.GetInstanceID();
            if (!originalActiveBedDurationsByKey.ContainsKey(key))
                originalActiveBedDurationsByKey[key] =
                    new ActiveBedDuration(environment, balanceConfig, previousDefault, previousMax,
                        previousDefault < ExtendedBedSleepMinutes, previousMax < ExtendedBedSleepMinutes);

            if (previousDefault < ExtendedBedSleepMinutes)
                environment.SetDefaultMinutes(ExtendedBedSleepMinutes);
            if (previousMax < ExtendedBedSleepMinutes)
                ActiveBalanceConfigMaxDurationField.SetValue(balanceConfig, ExtendedBedSleepMinutes);
            BigHaxLogger.SleepDiagnostic(context,
                "Active bed sleep patched: previousMax=" + previousMax +
                ", currentMax=" + activity.GetMaxSliderValue() + ".");
            return true;
        }

        public void ApplyConfiguredDurations(ModContext context, BigHaxSettings settings)
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.PlayerDefaults == null || saveGame.charactersData == null || saveGame.charactersData.Count == 0)
            {
                BigHaxLogger.SleepDiagnostic(context,
                    "Sleep duration apply deferred: active save has no usable player defaults or character data.");
                return;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var benchResult = PatchLoadedBehaviours(
                "restEnvironment",
                ExtendedBenchRestMinutes,
                RestBehaviourTypeNames);
            PatchResult bedResult;
            var bedConfigResult = default(PatchResult);
            if (settings.EnableExtendedBedSleep)
            {
                bedResult = PatchLoadedBehaviours(
                    "sleepEnvironment",
                    ExtendedBedSleepMinutes,
                    BedBehaviourTypeNames);
                bedConfigResult = PatchLoadedBedSleepConfigurations();
            }
            else
            {
                bedResult = RestoreOriginalDurations("sleepEnvironment");
                RestoreOriginalBedSleepConfigurations();
                RestoreOriginalActiveBedDurations();
            }

            stopwatch.Stop();
            lastAppliedSaveGame = saveGame;
            lastAppliedExtendedBedSetting = settings.EnableExtendedBedSleep;
            BigHaxLogger.SleepDiagnostic(context,
                "Sleep duration apply completed: extendedBed=" + settings.EnableExtendedBedSleep +
                ", bench=" + benchResult +
                ", bed=" + bedResult +
                ", bedConfigs=" + bedConfigResult +
                ", elapsedMs=" + stopwatch.ElapsedMilliseconds + ".");
        }

        public void RestoreOriginalDurationsOnShutdown()
        {
            RestoreOriginalDurations();
            originalEnvironmentDurationsByKey.Clear();
            RestoreOriginalBedSleepConfigurations();
            RestoreOriginalActiveBedDurations();
            originalBedConfigMaxMinutesByKey.Clear();
            lastAppliedSaveGame = null;
            lastAppliedExtendedBedSetting = null;
        }

        private PatchResult PatchLoadedBehaviours(
            string environmentFieldName,
            int extendedMinutes,
            IReadOnlyList<string> targetTypeNames)
        {
            var result = new PatchResult();
            foreach (var component in FindLoadedComponents(targetTypeNames))
            {
                var gameObject = component.gameObject;
                if (gameObject == null || !gameObject.scene.IsValid())
                    continue;

                result.Inspected++;
                var descriptor = GetDescriptor(component.GetType(), environmentFieldName);
                if (descriptor == null)
                {
                    result.Skipped++;
                    continue;
                }

                var outcome = TryPatchEnvironment(component, descriptor.Value, extendedMinutes);
                if (outcome == PatchOutcome.Patched)
                    result.Patched++;
                else if (outcome == PatchOutcome.Skipped)
                    result.Skipped++;
            }

            return result;
        }

        private PatchResult RestoreOriginalDurations(string? environmentFieldName = null)
        {
            if (originalEnvironmentDurationsByKey.Count == 0)
                return default;

            if (environmentFieldName == null)
            {
                var restResult = RestoreOriginalDurations("restEnvironment");
                var sleepResult = RestoreOriginalDurations("sleepEnvironment");
                originalEnvironmentDurationsByKey.Clear();
                return restResult + sleepResult;
            }

            var result = new PatchResult();
            var targetTypes = environmentFieldName == "sleepEnvironment"
                ? BedBehaviourTypeNames
                : RestBehaviourTypeNames;
            foreach (var component in FindLoadedComponents(targetTypes))
            {
                var gameObject = component.gameObject;
                if (gameObject == null || !gameObject.scene.IsValid())
                    continue;

                result.Inspected++;
                var descriptor = GetDescriptor(component.GetType(), environmentFieldName);
                if (descriptor == null)
                {
                    result.Skipped++;
                    continue;
                }

                if (TryRestoreEnvironment(component, descriptor.Value))
                    result.Patched++;
            }

            return result;
        }

        private PatchOutcome TryPatchEnvironment(Component component, EnvironmentPatchDescriptor descriptor, int extendedMinutes)
        {
            try
            {
                var environmentValue = descriptor.EnvironmentField.GetValue(component);
                if (environmentValue == null)
                    return PatchOutcome.Skipped;

                var balanceConfig = descriptor.BalanceConfigProperty.GetValue(environmentValue);
                if (balanceConfig == null)
                    return PatchOutcome.Skipped;

                var currentDefaultMinutes = (int)descriptor.GetDefaultMinutesMethod.Invoke(environmentValue, null);
                var currentMaxMinutes = (int)descriptor.MaxDurationMinutesField.GetValue(balanceConfig);
                if (currentDefaultMinutes >= extendedMinutes && currentMaxMinutes >= extendedMinutes)
                    return PatchOutcome.Unchanged;

                var key = BuildEnvironmentKey(component, descriptor.EnvironmentField.Name);
                if (!originalEnvironmentDurationsByKey.ContainsKey(key))
                    originalEnvironmentDurationsByKey[key] = new OriginalRestDurations(currentDefaultMinutes, currentMaxMinutes);

                descriptor.SetDefaultMinutesMethod.Invoke(environmentValue, new object[] { extendedMinutes });
                descriptor.MaxDurationMinutesField.SetValue(balanceConfig, extendedMinutes);
                descriptor.EnvironmentField.SetValue(component, environmentValue);

                return PatchOutcome.Patched;
            }
            catch (Exception exception)
            {
                LogPatchException(component, descriptor.EnvironmentField.Name, exception);
                return PatchOutcome.Skipped;
            }
        }

        private bool TryRestoreEnvironment(Component component, EnvironmentPatchDescriptor descriptor)
        {
            try
            {
                var key = BuildEnvironmentKey(component, descriptor.EnvironmentField.Name);
                if (!originalEnvironmentDurationsByKey.TryGetValue(key, out var originalDurations))
                    return false;

                var environmentValue = descriptor.EnvironmentField.GetValue(component);
                if (environmentValue == null)
                    return false;

                var balanceConfig = descriptor.BalanceConfigProperty.GetValue(environmentValue);
                if (balanceConfig == null)
                    return false;

                descriptor.SetDefaultMinutesMethod.Invoke(environmentValue, new object[] { originalDurations.DefaultMinutes });
                descriptor.MaxDurationMinutesField.SetValue(balanceConfig, originalDurations.MaxMinutes);
                descriptor.EnvironmentField.SetValue(component, environmentValue);
                return true;
            }
            catch (Exception exception)
            {
                // Scene teardown can leave the controller alive for one frame after its
                // environment config has been released. There is nothing left to restore.
                if (!ContainsMissingReferenceException(exception))
                    LogPatchException(component, descriptor.EnvironmentField.Name + " restore", exception);
                return false;
            }
        }

        private static bool ContainsMissingReferenceException(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is MissingReferenceException)
                    return true;
            }

            return false;
        }

        private PatchResult PatchLoadedBedSleepConfigurations()
        {
            var result = new PatchResult();
            var configType = FindTargetType("PlayerActivity.SleepEnvironmentConfig");
            if (configType == null)
                return result;

            foreach (var candidate in Resources.FindObjectsOfTypeAll(configType))
            {
                if (candidate == null)
                    continue;

                result.Inspected++;
                var sleepEnvironmentTypeField = configType.GetField("sleepEnvironmentType", InstanceFieldFlags);
                var balanceConfigField = configType.GetField("balanceConfig", InstanceFieldFlags);
                var balanceConfig = balanceConfigField?.GetValue(candidate);
                var maxDurationMinutesField = balanceConfig?.GetType().GetField("maxDurationMinutes", InstanceFieldFlags);
                if (sleepEnvironmentTypeField?.GetValue(candidate)?.ToString() != "Bed" ||
                    maxDurationMinutesField == null || maxDurationMinutesField.FieldType != typeof(int))
                {
                    result.Skipped++;
                    continue;
                }

                var currentMaxMinutes = (int)maxDurationMinutesField.GetValue(balanceConfig);
                if (currentMaxMinutes >= ExtendedBedSleepMinutes)
                    continue;

                var key = candidate.GetInstanceID();
                if (!originalBedConfigMaxMinutesByKey.ContainsKey(key))
                    originalBedConfigMaxMinutesByKey[key] = currentMaxMinutes;

                maxDurationMinutesField.SetValue(balanceConfig, ExtendedBedSleepMinutes);
                result.Patched++;
            }

            return result;
        }

        private void RestoreOriginalBedSleepConfigurations()
        {
            if (originalBedConfigMaxMinutesByKey.Count == 0)
                return;

            var configType = FindTargetType("PlayerActivity.SleepEnvironmentConfig");
            if (configType == null)
                return;

            foreach (var candidate in Resources.FindObjectsOfTypeAll(configType))
            {
                if (candidate == null || !originalBedConfigMaxMinutesByKey.TryGetValue(candidate.GetInstanceID(), out var originalMaxMinutes))
                    continue;

                var balanceConfig = candidate.GetType().GetField("balanceConfig", InstanceFieldFlags)?.GetValue(candidate);
                var maxDurationMinutesField = balanceConfig?.GetType().GetField("maxDurationMinutes", InstanceFieldFlags);
                if (maxDurationMinutesField?.FieldType == typeof(int))
                    maxDurationMinutesField.SetValue(balanceConfig, originalMaxMinutes);
            }
        }

        private void RestoreOriginalActiveBedDurations()
        {
            foreach (var original in originalActiveBedDurationsByKey.Values)
            {
                if (original.BalanceConfig == null)
                    continue;

                if (original.DefaultChanged)
                    original.Environment.SetDefaultMinutes(original.DefaultMinutes);
                if (original.MaxChanged)
                    ActiveBalanceConfigMaxDurationField?.SetValue(original.BalanceConfig, original.MaxMinutes);
            }

            originalActiveBedDurationsByKey.Clear();
        }

        private IEnumerable<Component> FindLoadedComponents(IReadOnlyList<string> targetTypeNames)
        {
            var seenInstanceIds = new HashSet<int>();
            foreach (var typeName in targetTypeNames)
            {
                var targetType = FindTargetType(typeName);
                if (targetType == null || !typeof(Component).IsAssignableFrom(targetType))
                    continue;

                foreach (var candidate in Resources.FindObjectsOfTypeAll(targetType))
                {
                    if (candidate is Component component && seenInstanceIds.Add(component.GetInstanceID()))
                        yield return component;
                }
            }
        }

        private Type? FindTargetType(string typeName)
        {
            if (targetTypeCache.TryGetValue(typeName, out var cachedType))
                return cachedType;

            Type? resolvedType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                resolvedType = assembly.GetType(typeName, false);
                if (resolvedType != null)
                    break;
            }

            targetTypeCache[typeName] = resolvedType;
            return resolvedType;
        }

        private void LogPatchException(Component component, string environmentName, Exception exception)
        {
            if (loggedPatchExceptions >= 8)
                return;

            loggedPatchExceptions++;
            BigHaxLogger.DiagnosticException(
                "Freeze diagnostic/sleep-rest " + component.GetType().FullName + "." + environmentName,
                exception);
        }

        private EnvironmentPatchDescriptor? GetDescriptor(Type behaviourType, string environmentFieldName)
        {
            var cacheKey = behaviourType.FullName + "|" + environmentFieldName;
            if (descriptorCache.TryGetValue(cacheKey, out var cachedDescriptor))
                return cachedDescriptor;

            var descriptor = TryCreateDescriptor(behaviourType, environmentFieldName);

            descriptorCache[cacheKey] = descriptor;
            return descriptor;
        }

        private static EnvironmentPatchDescriptor? TryCreateDescriptor(
            Type behaviourType,
            string environmentFieldName)
        {
            var environmentField = behaviourType.GetField(environmentFieldName, InstanceFieldFlags);
            if (environmentField == null)
                return null;

            var getDefaultMinutesMethod = environmentField.FieldType.GetMethod("GetDefaultMinutes", InstanceFieldFlags);
            var setDefaultMinutesMethod = environmentField.FieldType.GetMethod("SetDefaultMinutes", InstanceFieldFlags, null, new[] { typeof(int) }, null);
            var balanceConfigProperty = environmentField.FieldType.GetProperty("BalanceConfig", InstanceFieldFlags);
            var maxDurationMinutesField = balanceConfigProperty?.PropertyType.GetField("maxDurationMinutes", InstanceFieldFlags);
            if (getDefaultMinutesMethod == null || getDefaultMinutesMethod.ReturnType != typeof(int) ||
                setDefaultMinutesMethod == null || balanceConfigProperty == null ||
                maxDurationMinutesField == null || maxDurationMinutesField.FieldType != typeof(int))
                return null;

            return new EnvironmentPatchDescriptor(
                environmentField,
                getDefaultMinutesMethod,
                setDefaultMinutesMethod,
                balanceConfigProperty,
                maxDurationMinutesField);
        }

        private static int BuildEnvironmentKey(Component component, string fieldName)
        {
            unchecked
            {
                return (component.GetInstanceID() * 397) ^ StringComparer.Ordinal.GetHashCode(fieldName);
            }
        }

        private readonly struct EnvironmentPatchDescriptor
        {
            public EnvironmentPatchDescriptor(
                FieldInfo environmentField,
                MethodInfo getDefaultMinutesMethod,
                MethodInfo setDefaultMinutesMethod,
                PropertyInfo balanceConfigProperty,
                FieldInfo maxDurationMinutesField)
            {
                EnvironmentField = environmentField;
                GetDefaultMinutesMethod = getDefaultMinutesMethod;
                SetDefaultMinutesMethod = setDefaultMinutesMethod;
                BalanceConfigProperty = balanceConfigProperty;
                MaxDurationMinutesField = maxDurationMinutesField;
            }

            public FieldInfo EnvironmentField { get; }
            public MethodInfo GetDefaultMinutesMethod { get; }
            public MethodInfo SetDefaultMinutesMethod { get; }
            public PropertyInfo BalanceConfigProperty { get; }
            public FieldInfo MaxDurationMinutesField { get; }
        }

        private readonly struct OriginalRestDurations
        {
            public OriginalRestDurations(int defaultMinutes, int maxMinutes)
            {
                DefaultMinutes = defaultMinutes;
                MaxMinutes = maxMinutes;
            }

            public int DefaultMinutes { get; }
            public int MaxMinutes { get; }
        }

        private readonly struct ActiveBedDuration
        {
            public ActiveBedDuration(SleepEnvironment environment, PlayerActivityBalanceConfig balanceConfig,
                int defaultMinutes, int maxMinutes, bool defaultChanged, bool maxChanged)
            {
                Environment = environment;
                BalanceConfig = balanceConfig;
                DefaultMinutes = defaultMinutes;
                MaxMinutes = maxMinutes;
                DefaultChanged = defaultChanged;
                MaxChanged = maxChanged;
            }

            public SleepEnvironment Environment { get; }
            public PlayerActivityBalanceConfig BalanceConfig { get; }
            public int DefaultMinutes { get; }
            public int MaxMinutes { get; }
            public bool DefaultChanged { get; }
            public bool MaxChanged { get; }
        }

        private enum PatchOutcome
        {
            Unchanged,
            Patched,
            Skipped
        }

        private struct PatchResult
        {
            public int Inspected;
            public int Patched;
            public int Skipped;

            public static PatchResult operator +(PatchResult left, PatchResult right)
            {
                return new PatchResult
                {
                    Inspected = left.Inspected + right.Inspected,
                    Patched = left.Patched + right.Patched,
                    Skipped = left.Skipped + right.Skipped
                };
            }

            public override string ToString()
            {
                return "inspected=" + Inspected + ",patched=" + Patched + ",skipped=" + Skipped;
            }
        }
    }
}
