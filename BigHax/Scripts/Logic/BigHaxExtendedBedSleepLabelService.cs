#nullable enable
using BAModAPI;
using Localizor;
using PlayerActivity;
using System.Reflection;
using UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BigHax
{
    internal sealed class BigHaxExtendedBedSleepLabelService
    {
        private Slider? subscribedSlider;
        private static readonly BindingFlags UiFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo? SliderField = typeof(PlayerActivityUI).GetField("slider", UiFieldFlags);
        private static readonly FieldInfo? SliderLabelField = typeof(PlayerActivityUI).GetField("sliderLabel", UiFieldFlags);
        private UnityAction<float>? handler;
        private BigHaxSettings? settings;
        private Component? sourceTextContainer;
        private Component? overlayTextContainer;
        private GameObject? wakeTimeOverlay;

        public void Attach(BigHaxSettings currentSettings)
        {
            var activityUi = InstanceBehavior<UIs>.Instance?.playerActivityUI;
            var slider = activityUi == null ? null : SliderField?.GetValue(activityUi) as Slider;
            if (slider == null)
                return;

            Detach();
            settings = currentSettings;
            subscribedSlider = slider;
            handler = UpdateLabel;
            slider.onValueChanged.AddListener(handler);
        }

        public void Detach()
        {
            if (subscribedSlider != null && handler != null)
                subscribedSlider.onValueChanged.RemoveListener(handler);

            subscribedSlider = null;
            handler = null;
            RestoreNativeWakeLabel();
            settings = null;
        }

        private void UpdateLabel(float rawMinutes)
        {
            if (settings?.EnableExtendedBedSleep != true || rawMinutes <= 24f * 60f)
            {
                RestoreNativeWakeLabel();
                return;
            }

            var activityUi = InstanceBehavior<UIs>.Instance?.playerActivityUI;
            if (activityUi?.GetCurrentActivity?.GetType().FullName != "PlayerActivity.SleepActivity")
            {
                RestoreNativeWakeLabel();
                return;
            }

            var totalMinutes = Mathf.RoundToInt(rawMinutes);
            var wakeMinutes = (SaveGameManager.Current.Hour * 60 + Mathf.FloorToInt(SaveGameManager.Current.Minute) + totalMinutes) % (24 * 60);
            var wakeTime = string.Format("{0:00}:{1:00}", wakeMinutes / 60, wakeMinutes % 60);
            // This listener is attached after PlayerActivityUI has registered its
            // own slider listener, so the corrected localized data is written
            // last in the same slider event without any deferred work.
            WriteLabel(activityUi, totalMinutes, wakeTime);
        }

        private void WriteLabel(PlayerActivityUI activityUi, int totalMinutes, string wakeTime)
        {
            if (settings?.EnableExtendedBedSleep != true ||
                activityUi.GetCurrentActivity?.GetType().FullName != "PlayerActivity.SleepActivity")
                return;

            var localizedText = "sleepui_slider_label_bed".Localize(new
            {
                sleepHours = totalMinutes / 60,
                sleepMinutes = totalMinutes % 60,
                time = wakeTime
            });
            var label = SliderLabelField?.GetValue(activityUi);
            TrySetLabelData(label, localizedText);
            ShowWakeTimeOverlay(label);
        }

        private static void TrySetLabelData(object? label, object localizedText)
        {
            if (label == null)
                return;

            foreach (var method in label.GetType().GetMethods(UiFieldFlags))
            {
                if (method.Name != "SetData")
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 1 || !parameters[0].ParameterType.IsInstanceOfType(localizedText))
                    continue;

                method.Invoke(label, new object[] { localizedText });
                return;
            }
        }

        private void ShowWakeTimeOverlay(object? label)
        {
            if (!(label is Component labelComponent))
                return;

            var nativeText = label.GetType()
                .GetProperty("TextContainer", UiFieldFlags)?.GetValue(label);
            if (!(nativeText is Component nativeTextComponent))
                return;

            var textProperty = nativeText.GetType()
                .GetProperty("text", UiFieldFlags);
            if (textProperty == null || textProperty.PropertyType != typeof(string) || !textProperty.CanWrite)
                return;

            if (wakeTimeOverlay == null || sourceTextContainer != nativeTextComponent)
            {
                RestoreNativeWakeLabel();
                wakeTimeOverlay = UnityEngine.Object.Instantiate(labelComponent.gameObject, labelComponent.transform.parent);
                wakeTimeOverlay.name = "BigHaxExtendedBedWakeTime";
                wakeTimeOverlay.transform.SetSiblingIndex(labelComponent.transform.GetSiblingIndex() + 1);
                var overlayLabel = wakeTimeOverlay.GetComponent(labelComponent.GetType());
                if (overlayLabel is Behaviour overlayLocalization)
                    overlayLocalization.enabled = false;

                overlayTextContainer = overlayLabel?.GetType()
                    .GetProperty("TextContainer", UiFieldFlags)?.GetValue(overlayLabel) as Component;
                sourceTextContainer = nativeTextComponent;
            }

            if (overlayTextContainer == null)
                return;

            var currentText = textProperty.GetValue(nativeText) as string;
            if (string.IsNullOrEmpty(currentText))
                return;

            textProperty.SetValue(overlayTextContainer, currentText);
            if (nativeTextComponent is Behaviour nativeTextBehaviour)
                nativeTextBehaviour.enabled = false;
            wakeTimeOverlay.SetActive(true);
        }

        private void RestoreNativeWakeLabel()
        {
            if (sourceTextContainer is Behaviour sourceTextBehaviour)
                sourceTextBehaviour.enabled = true;
            if (wakeTimeOverlay != null)
                wakeTimeOverlay.SetActive(false);
        }
    }
}
