#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxInvestmentLimitService
    {
        private const long MaxNumeralAmountFallback = int.MaxValue;

        private static readonly string[] DialogPaths =
        {
            "Canvases/FullMenu/Canvas/AppsContainer/Contacts/Layout 30-70/Right/Conversation/Viewport/Content/PlayerMessageTemplate/InputTemplates/BankInvestment",
            "Canvases/PlayerHUD/DialogUI/Panel/Entries (Scroll View)/Viewport/Content/PlayerMessageTemplate/InputTemplates/BankInvestment"
        };

        private readonly List<InvestmentDialogTarget> resolvedTargets = new List<InvestmentDialogTarget>();

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
                    "missing-investment-targets",
                    "BigHax: could not resolve the fixed bank investment dialog templates.");
                return;
            }

            var configuredLimit = settings.DisableInvestmentLimit
                ? BigHaxSettings.DisabledInvestmentLimitAmount
                : 1_000_000_000L;
            foreach (var target in targets)
                target.ApplyLimit(configuredLimit);
        }

        public void RestoreOriginalLimit()
        {
            foreach (var target in resolvedTargets)
                target.RestoreOriginalValues();
        }

        private List<InvestmentDialogTarget> ResolveTargets(ModContext context)
        {
            if (resolvedTargets.Count > 0)
                return resolvedTargets;

            foreach (var dialogPath in DialogPaths)
            {
                var dialogTransform = ResolveDialogTransform(dialogPath);
                if (dialogTransform == null)
                    continue;

                var dialogTarget = TryCreateTarget(dialogTransform.gameObject);
                if (dialogTarget == null)
                {
                    BigHaxLogger.WarnOnce(
                        context,
                        "invalid-investment-target-" + dialogPath,
                        $"BigHax: found investment dialog path '{dialogPath}' but could not patch its fields.");
                    continue;
                }

                resolvedTargets.Add(dialogTarget);
            }

            return resolvedTargets;
        }

        private static Transform? ResolveDialogTransform(string dialogPath)
        {
            var separatorIndex = dialogPath.IndexOf('/');
            var rootName = separatorIndex >= 0 ? dialogPath.Substring(0, separatorIndex) : dialogPath;
            var relativePath = separatorIndex >= 0 ? dialogPath.Substring(separatorIndex + 1) : string.Empty;
            var rootObject = GameObject.Find(rootName);
            if (rootObject == null)
                return null;

            if (string.IsNullOrEmpty(relativePath))
                return rootObject.transform;

            var result = rootObject.transform.Find(relativePath);
            return result;
        }

        private static InvestmentDialogTarget? TryCreateTarget(GameObject dialogGameObject)
        {
            var dialogComponent = FindComponentWithField(dialogGameObject, "maxInvestment");
            if (dialogComponent == null)
                return null;

            var dialogType = dialogComponent.GetType();
            var maxInvestmentField = dialogType.GetField("maxInvestment", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var amountFieldField = dialogType.GetField("amountField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (maxInvestmentField == null || amountFieldField == null)
                return null;

            var amountField = amountFieldField.GetValue(dialogComponent);
            if (amountField == null)
                return null;

            var maxNumeralAmountField = amountField.GetType().GetField(
                "maxNumeralAmount",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (maxNumeralAmountField == null)
                return null;

            return new InvestmentDialogTarget(dialogComponent, maxInvestmentField, amountField, maxNumeralAmountField);
        }

        private static Component? FindComponentWithField(GameObject gameObject, string fieldName)
        {
            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                if (component.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null)
                    return component;
            }

            return null;
        }

        private sealed class InvestmentDialogTarget
        {
            private readonly object amountField;
            private readonly object dialog;
            private readonly FieldInfo maxInvestmentField;
            private readonly FieldInfo maxNumeralAmountField;
            private readonly object? originalMaxInvestment;
            private readonly object? originalMaxNumeralAmount;

            public InvestmentDialogTarget(
                object dialog,
                FieldInfo maxInvestmentField,
                object amountField,
                FieldInfo maxNumeralAmountField)
            {
                this.dialog = dialog;
                this.maxInvestmentField = maxInvestmentField;
                this.amountField = amountField;
                this.maxNumeralAmountField = maxNumeralAmountField;
                originalMaxInvestment = maxInvestmentField.GetValue(dialog);
                originalMaxNumeralAmount = maxNumeralAmountField.GetValue(amountField);
            }

            public void ApplyLimit(long configuredLimit)
            {
                TrySetNumericValue(maxInvestmentField, dialog, configuredLimit);

                if (!TrySetNumericValue(maxNumeralAmountField, amountField, configuredLimit))
                {
                    var clampedValue = Math.Min(configuredLimit, MaxNumeralAmountFallback);
                    TrySetNumericValue(maxNumeralAmountField, amountField, clampedValue);
                }
            }

            public void RestoreOriginalValues()
            {
                maxInvestmentField.SetValue(dialog, originalMaxInvestment);
                maxNumeralAmountField.SetValue(amountField, originalMaxNumeralAmount);
            }
        }

        private static bool TrySetNumericValue(FieldInfo field, object target, long value)
        {
            var fieldType = field.FieldType;
            try
            {
                if (fieldType == typeof(int))
                {
                    if (value > int.MaxValue)
                        return false;

                    field.SetValue(target, (int)value);
                    return true;
                }

                if (fieldType == typeof(long))
                {
                    field.SetValue(target, value);
                    return true;
                }

                if (fieldType == typeof(float))
                {
                    field.SetValue(target, (float)value);
                    return true;
                }

                if (fieldType == typeof(double))
                {
                    field.SetValue(target, (double)value);
                    return true;
                }

                if (fieldType == typeof(decimal))
                {
                    field.SetValue(target, (decimal)value);
                    return true;
                }

                var convertedValue = Convert.ChangeType(value, fieldType);
                field.SetValue(target, convertedValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

    }
}
