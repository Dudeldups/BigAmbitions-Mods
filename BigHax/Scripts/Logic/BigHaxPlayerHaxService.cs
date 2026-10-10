#nullable enable
using System.Reflection;
using System.Linq;
using BAModAPI;
using Helpers;
using UnityEngine;

namespace BigHax
{
    /// <summary>
    /// Uses the game's own non-persistent energy invincibility switch and its
    /// recalculation hook rather than continuously restoring player stats.
    /// </summary>
    internal sealed class BigHaxPlayerHaxService
    {
        private const string NewHourEvent = "ba:gameevent_newhour";
        private static readonly FieldInfo? EnergyInvincibilityField = typeof(EnergyHelper).GetField(
            "Invincibility",
            BindingFlags.Static | BindingFlags.NonPublic);

        private bool energyInvincibilityEnabledByBigHax;
        private bool hungerAndEnergyDecayDisabled;
        private bool happinessDecayDisabled;
        private bool isSubscribed;
        private float? protectedHappiness;
        private ModContext? context;
        private static BigHaxPlayerHaxService? happinessService;
        // Raw IL fingerprint of Build 3682's UpdateHappiness. Do not suppress an
        // unknown future implementation that might have new side effects.
        private const string SupportedHappinessUpdateHash = "352578E5D133CE86C25BC37AB1F9206B6EBDB002518A0EB38ED4A377119B5D7A";
        private BigHaxMethodDetour? happinessUpdateDetour;
        private readonly System.Collections.Generic.List<BigHaxHappinessPanelObserver> panelObservers = new();
        private int updateTraceCount;
        private float? lastObservedHappiness;
        private float lastHourlyEventAt = -1f;
        private string lastEventId = "<none>";
        private bool warnedUnexpectedDrop;

        public void ApplyConfiguredBehavior(ModContext modContext, BigHaxSettings settings)
        {
            context = modContext;
            ConfigureDiagnostics();
            ApplyHungerAndEnergyDecaySetting(settings.DisablePlayerHungerAndEnergyDecay);
            hungerAndEnergyDecayDisabled = settings.DisablePlayerHungerAndEnergyDecay;

            var saveGame = SaveGameManager.Current;
            var playerStatsChanged = false;
            if (hungerAndEnergyDecayDisabled && saveGame != null)
            {
                if (saveGame.Energy < 100f)
                {
                    saveGame.Energy = 100f;
                    playerStatsChanged = true;
                }

                if (saveGame.Hunger < 100f)
                {
                    saveGame.Hunger = 100f;
                    playerStatsChanged = true;
                }
            }

            var wasHappinessDisabled = happinessDecayDisabled;
            happinessDecayDisabled = settings.DisablePlayerHappinessDecay;
            ConfigureHappinessProtection(wasHappinessDisabled);
            if (happinessDecayDisabled)
            {
                if (saveGame != null && saveGame.Happiness < 100f)
                {
                    saveGame.Happiness = 100f;
                    playerStatsChanged = true;
                }

                protectedHappiness = 100f;
            }
            else
            {
                protectedHappiness = null;
            }

            if (playerStatsChanged)
            {
                SaveGameManager.MarkChange();
                BigHaxLogger.Diagnostic(
                    "Player hax filled enabled stats: " + DescribePlayerStats() + ".");
            }

            if (hungerAndEnergyDecayDisabled || happinessDecayDisabled)
            {
                Subscribe();
            }
            else
            {
                Unsubscribe();
            }

            BigHaxLogger.Diagnostic(
                "Player hax configured: hungerEnergyDisabled=" + hungerAndEnergyDecayDisabled +
                ", energyInvincibilityFieldFound=" + (EnergyInvincibilityField != null) +
                ", energyInvincibilityValue=" + GetEnergyInvincibilityValue() +
                ", " + DescribePlayerStats() +
                ", happinessDisabled=" + happinessDecayDisabled +
                ", protectedHappiness=" + FormatHappiness(protectedHappiness));
            BigHaxLogger.HappinessDiagnostic(context,
                "Happiness diagnostics configured: enabled=" + happinessDecayDisabled +
                ", current=" + FormatHappiness(saveGame?.Happiness) +
                ", protected=" + FormatHappiness(protectedHappiness) +
                ", modifiers=" + DescribeModifiers(saveGame) + ".");
        }

        // Observe the game at its own state/UI transitions instead of polling.
        public void AttachDiagnosticUiHooks()
        {
            if (!BigHaxLogger.DebugEnabled || !BigHaxLogger.HappinessDebugEnabled)
                return;
            var fullMenu = InstanceBehavior<UI.UIs>.Instance?.fullMenu;
            var appsContainer = fullMenu == null ? null : typeof(UI.Smartphone.FullMenu)
                .GetField("appsContainer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(fullMenu) as Transform;
            if (appsContainer == null)
            {
                BigHaxLogger.HappinessDiagnostic(context, "Happiness panel hooks deferred: apps container unavailable.");
                return;
            }
            panelObservers.RemoveAll(observer => observer == null);
            foreach (Transform panel in appsContainer)
            {
                var observer = panel.GetComponent<BigHaxHappinessPanelObserver>();
                if (observer == null)
                    observer = panel.gameObject.AddComponent<BigHaxHappinessPanelObserver>();
                observer.StateChanged = ObserveHappiness;
                if (!panelObservers.Contains(observer))
                    panelObservers.Add(observer);
            }
            BigHaxLogger.HappinessDiagnostic(context, "Happiness panel lifecycle hooks attached: panels=" + panelObservers.Count + ".");
            ObserveHappiness("game-loaded");
        }

        private void ConfigureDiagnostics()
        {
            GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
            if (!BigHaxLogger.DebugEnabled || !BigHaxLogger.HappinessDebugEnabled)
                return;
            GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
        }

        private void ConfigureHappinessProtection(bool wasEnabled)
        {
            if (!happinessDecayDisabled)
            {
                var restored = RemoveHappinessProtection();
                // Recalculate from the untouched modifiers immediately when the
                // user disables the cheat, instead of persisting its filled value.
                if (wasEnabled && restored && SaveGameManager.Current != null)
                    HappinessHelper.UpdateHappiness();
                return;
            }
            happinessService = this;
            if (happinessUpdateDetour?.IsApplied == true)
                return;
            var target = typeof(HappinessHelper).GetMethod(nameof(HappinessHelper.UpdateHappiness),
                BindingFlags.Static | BindingFlags.Public, null, System.Type.EmptyTypes, null);
            var replacement = typeof(BigHaxPlayerHaxService).GetMethod(nameof(ProtectHappinessUpdate),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || replacement == null || !IsSupportedHappinessUpdate(target))
            {
                BigHaxLogger.WarnOnce(context, "happiness-update-unsupported",
                    "BigHax: continuous Happiness protection unavailable: game recalculation has changed. Hourly protection remains active.");
                return;
            }
            happinessUpdateDetour = new BigHaxMethodDetour(target, replacement);
            if (!happinessUpdateDetour.Apply(out var error))
                BigHaxLogger.WarnOnce(context, "happiness-update-hook", "BigHax: could not protect Happiness recalculation: " + error);
            else
                BigHaxLogger.HappinessDiagnostic(context, "Happiness recalculation protection installed (Build 3682); modifier lifetimes remain unchanged.");
        }

        private static bool IsSupportedHappinessUpdate(MethodInfo method)
        {
            try
            {
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il == null)
                    return false;
                using var hash = System.Security.Cryptography.SHA256.Create();
                return System.BitConverter.ToString(hash.ComputeHash(il)).Replace("-", "") == SupportedHappinessUpdateHash;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private bool RemoveHappinessProtection()
        {
            if (happinessUpdateDetour != null && !happinessUpdateDetour.Restore(out var error))
            {
                BigHaxLogger.WarnOnce(context, "happiness-update-restore", "BigHax: could not restore Happiness recalculation: " + error);
                return false;
            }
            happinessUpdateDetour = null;
            if (happinessService == this)
                happinessService = null;
            BigHaxLogger.HappinessDiagnostic(context, "Happiness recalculation protection removed; vanilla calculation restored.");
            return true;
        }

        private static void ProtectHappinessUpdate()
        {
            var saveGame = SaveGameManager.Current;
            var service = happinessService;
            if (saveGame == null || service == null)
                return;
            var before = saveGame.Happiness;
            saveGame.Happiness = 100f;
            if (!Mathf.Approximately(before, saveGame.Happiness))
                SaveGameManager.MarkChange();
            // RunHourly still expires modifiers before calling this method; add,
            // remove, and temporal boosts still manage their own data as usual.
            // Only the final value/low-Happiness emoji calculation is suppressed.
            if (BigHaxLogger.DebugEnabled && BigHaxLogger.HappinessDebugEnabled && service.updateTraceCount < 24)
            {
                service.updateTraceCount++;
                BigHaxLogger.HappinessDiagnostic(service.context,
                    "Happiness recalculation protected " + service.updateTraceCount + "/24: before=" + FormatHappiness(before) +
                    ", final=" + FormatHappiness(saveGame.Happiness) + ", enabled=" + service.happinessDecayDisabled +
                    ", day=" + saveGame.Day + ", hour=" + saveGame.Hour + ", modifiers=" + DescribeModifiers(saveGame) +
                    ", stack=" + new System.Diagnostics.StackTrace(1, false) + ".");
            }
        }

        private void HandleFullMenuToggle(bool open)
        {
            ObserveHappiness("full-menu-" + (open ? "opened" : "closed"));
        }

        private void ObserveHappiness(string source)
        {
            ObserveHappiness(source, lastObservedHappiness);
        }

        private void ObserveHappiness(string source, float? before)
        {
            if (!BigHaxLogger.DebugEnabled || !BigHaxLogger.HappinessDebugEnabled)
                return;
            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
                return;
            var happiness = saveGame.Happiness;
            var details = "Happiness transition: source=" + source + ", before=" + FormatHappiness(before) +
                ", current=" + FormatHappiness(happiness) + ", enabled=" + happinessDecayDisabled +
                ", protected=" + FormatHappiness(protectedHappiness) +
                ", day=" + saveGame.Day + ", hour=" + saveGame.Hour +
                ", lastEvent=" + lastEventId + ", eventHookPresent=" + IsEventHookPresent() +
                ", secondsSinceHourlyEvent=" + (lastHourlyEventAt < 0f ? "<none>" : (Time.unscaledTime - lastHourlyEventAt).ToString("0.###")) +
                ", modifiers=" + DescribeModifiers(saveGame) + ".";
            if (happinessDecayDisabled && happiness < (protectedHappiness ?? 100f) && !warnedUnexpectedDrop)
            {
                context?.Logger.Warn("BigHax: " + details);
                warnedUnexpectedDrop = true;
            }
            BigHaxLogger.HappinessDiagnostic(context, details);
            lastObservedHappiness = happiness;
        }

        public void Shutdown()
        {
            GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
            foreach (var observer in panelObservers)
            {
                if (observer != null)
                {
                    observer.StateChanged = null;
                    UnityEngine.Object.Destroy(observer);
                }
            }
            panelObservers.Clear();
            RemoveHappinessProtection();
            updateTraceCount = 0;
            ApplyHungerAndEnergyDecaySetting(false);
            hungerAndEnergyDecayDisabled = false;
            happinessDecayDisabled = false;
            protectedHappiness = null;
            context = null;
            lastObservedHappiness = null;
            warnedUnexpectedDrop = false;
            Unsubscribe();
        }

        private void ApplyHungerAndEnergyDecaySetting(bool disableDecay)
        {
            if (EnergyInvincibilityField == null)
                return;

            if (disableDecay)
            {
                EnergyInvincibilityField.SetValue(null, true);
                energyInvincibilityEnabledByBigHax = true;
            }
            else if (energyInvincibilityEnabledByBigHax)
            {
                EnergyInvincibilityField.SetValue(null, false);
                energyInvincibilityEnabledByBigHax = false;
            }
        }

        private void Subscribe()
        {
            // GameEvent can clear its static delegate while this service survives
            // a save load. Remove/add is cheap and guarantees the hook is current.
            GameEvent.onGameEventTriggered -= HandleGameEvent;
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
            if (BigHaxLogger.DebugEnabled && BigHaxLogger.HappinessDebugEnabled)
                lastEventId = string.IsNullOrEmpty(eventId) ? "<empty>" : eventId;
            if (eventId != NewHourEvent)
                return;

            lastHourlyEventAt = Time.unscaledTime;
            ObserveHappiness("new-hour-before-protection");
            BigHaxLogger.HappinessDiagnostic(context,
                "Hourly Happiness event entered: current=" + FormatHappiness(SaveGameManager.Current?.Happiness) +
                ", protected=" + FormatHappiness(protectedHappiness) +
                ", modifiers=" + DescribeModifiers(SaveGameManager.Current) + ".");

            if (happinessDecayDisabled)
                PreserveHappinessAfterHourlyUpdate();
        }

        private void PreserveHappinessAfterHourlyUpdate()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
            {
                BigHaxLogger.HappinessDiagnostic(context, "Happiness hourly check skipped: no active save.");
                return;
            }

            var happinessBefore = saveGame.Happiness;

            if (!protectedHappiness.HasValue)
            {
                protectedHappiness = saveGame.Happiness;
                return;
            }

            if (saveGame.Happiness >= protectedHappiness.Value)
            {
                protectedHappiness = saveGame.Happiness;
                BigHaxLogger.HappinessDiagnostic(context,
                    "Happiness hourly check kept current=" + FormatHappiness(saveGame.Happiness) + ".");
                return;
            }

            saveGame.Happiness = protectedHappiness.Value;
            SaveGameManager.MarkChange();
            BigHaxLogger.HappinessDiagnostic(context,
                "Happiness hax hourly check: current=" + FormatHappiness(happinessBefore) +
                ", protected=" + FormatHappiness(protectedHappiness) +
                ", action=restored, final=" + FormatHappiness(saveGame.Happiness));
        }

        private static string GetEnergyInvincibilityValue()
        {
            try
            {
                return EnergyInvincibilityField?.GetValue(null)?.ToString() ?? "<unavailable>";
            }
            catch (System.Exception exception)
            {
                BigHaxLogger.DiagnosticException("Reading energy invincibility", exception);
                return "<read failed>";
            }
        }

        private static string FormatHappiness(float? value)
        {
            return value.HasValue ? value.Value.ToString("0.###") : "<unavailable>";
        }

        private static string DescribeModifiers(GameInstance? saveGame)
        {
            var modifiers = saveGame?.happinessModifiers;
            if (modifiers == null || modifiers.Count == 0)
                return "0";
            return modifiers.Count + " [" + string.Join(", ", modifiers.Take(12).Select(value =>
                value == null ? "<null>" : value.type + ":" + value.hoursLeft)) + "]";
        }

        private bool IsEventHookPresent()
        {
            var handlers = GameEvent.onGameEventTriggered?.GetInvocationList();
            return handlers != null && handlers.Any(handler => handler.Target == this &&
                handler.Method.Name == nameof(HandleGameEvent));
        }

        private static string DescribePlayerStats()
        {
            var saveGame = SaveGameManager.Current;
            return saveGame == null
                ? "energy=<unavailable>, hunger=<unavailable>, happiness=<unavailable>"
                : "energy=" + saveGame.Energy.ToString("0.###") +
                  ", hunger=" + saveGame.Hunger.ToString("0.###") +
                  ", happiness=" + saveGame.Happiness.ToString("0.###");
        }
    }

    internal sealed class BigHaxHappinessPanelObserver : MonoBehaviour
    {
        internal System.Action<string>? StateChanged;
        private void OnEnable() => StateChanged?.Invoke("panel-opened:" + gameObject.name);
        private void OnDisable() => StateChanged?.Invoke("panel-closed:" + gameObject.name);
    }
}
