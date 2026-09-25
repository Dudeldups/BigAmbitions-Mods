#nullable enable
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxCasinoBetLimitService
    {
        private static readonly string[] InputPaths =
        {
            "Canvases/FullMenu/Canvas/AppsContainer/Contacts/Layout 30-70/Right/Conversation/Viewport/Content/PlayerMessageTemplate/InputTemplates/BlackjackBetSettings",
            "Canvases/FullMenu/Canvas/AppsContainer/Contacts/Layout 30-70/Right/Conversation/Viewport/Content/PlayerMessageTemplate/InputTemplates/RouletteBetSettings",
            "Canvases/PlayerHUD/DialogUI/Panel/Entries (Scroll View)/Viewport/Content/PlayerMessageTemplate/InputTemplates/BlackjackBetSettings",
            "Canvases/PlayerHUD/DialogUI/Panel/Entries (Scroll View)/Viewport/Content/PlayerMessageTemplate/InputTemplates/RouletteBetSettings"
        };

        private readonly List<CasinoBetInputTarget> resolvedTargets = new List<CasinoBetInputTarget>();

        public void InvalidateCache()
        {
            resolvedTargets.Clear();
        }

        public void ApplyConfiguredLimit(ModContext context, BigHaxSettings settings)
        {
            var targets = ResolveTargets(context);
            if (targets.Count == 0)
            {
                BigHaxLogger.WarnOnce(
                    context,
                    "missing-casino-bet-targets",
                    "BigHax: could not resolve the fixed blackjack/roulette bet input templates.");
                return;
            }

            var configuredLimit = settings.DisableCasinoBetLimit
                ? BigHaxSettings.DisabledCasinoBetLimitAmount
                : 100_000;
            foreach (var target in targets)
            {
                target.ApplyLimit(configuredLimit);
            }
        }

        public void RestoreOriginalLimit()
        {
            foreach (var target in resolvedTargets)
                target.RestoreOriginalValue();
        }

        private List<CasinoBetInputTarget> ResolveTargets(ModContext context)
        {
            if (resolvedTargets.Count > 0)
            {
                RemoveDestroyedTargets();
                return resolvedTargets;
            }

            foreach (var inputPath in InputPaths)
            {
                var inputTransform = ResolveTransform(inputPath);
                if (inputTransform == null)
                    continue;

                var addedForPath = 0;
                foreach (var target in TryCreateTargets(inputTransform.gameObject))
                {
                    resolvedTargets.Add(target);
                    addedForPath++;
                }

                if (addedForPath == 0)
                {
                    BigHaxLogger.WarnOnce(
                        context,
                        "invalid-casino-bet-target-" + inputPath,
                        $"BigHax: found casino bet input path '{inputPath}' but could not patch maxNumeralAmount.");
                    continue;
                }
            }

            return resolvedTargets;
        }

        private static Transform? ResolveTransform(string path)
        {
            var separatorIndex = path.IndexOf('/');
            var rootName = separatorIndex >= 0 ? path.Substring(0, separatorIndex) : path;
            var relativePath = separatorIndex >= 0 ? path.Substring(separatorIndex + 1) : string.Empty;
            var rootObject = GameObject.Find(rootName);
            if (rootObject == null)
                return null;

            return string.IsNullOrEmpty(relativePath) ? rootObject.transform : rootObject.transform.Find(relativePath);
        }

        private void RemoveDestroyedTargets()
        {
            for (var index = resolvedTargets.Count - 1; index >= 0; index--)
            {
                if (resolvedTargets[index].IsDestroyed)
                    resolvedTargets.RemoveAt(index);
            }
        }

        private IEnumerable<CasinoBetInputTarget> TryCreateTargets(GameObject gameObject)
        {
            foreach (var component in gameObject.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;

                var field = component.GetType().GetField("maxNumeralAmount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                    continue;

                yield return new CasinoBetInputTarget(component, field);
            }
        }

        private sealed class CasinoBetInputTarget
        {
            private readonly Component component;
            private readonly object originalValue;
            private readonly FieldInfo valueField;

            public CasinoBetInputTarget(Component component, FieldInfo valueField)
            {
                this.component = component;
                this.valueField = valueField;
                originalValue = valueField.GetValue(component) ?? 100_000;
            }

            public bool IsDestroyed => component == null;

            public void ApplyLimit(int value)
            {
                if (component == null)
                    return;

                valueField.SetValue(component, value);
            }

            public void RestoreOriginalValue()
            {
                if (component == null)
                    return;

                valueField.SetValue(component, originalValue);
            }
        }
    }
}
