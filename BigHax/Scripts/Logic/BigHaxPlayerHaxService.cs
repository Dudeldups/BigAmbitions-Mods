#nullable enable
using System.Reflection;
using Helpers;

namespace BigHax
{
    /// <summary>
    /// Uses the game's own non-persistent energy invincibility switch and its
    /// existing hourly event rather than continuously restoring player stats.
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

        public void ApplyConfiguredBehavior(BigHaxSettings settings)
        {
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

            happinessDecayDisabled = settings.DisablePlayerHappinessDecay;
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
        }

        public void Shutdown()
        {
            ApplyHungerAndEnergyDecaySetting(false);
            hungerAndEnergyDecayDisabled = false;
            happinessDecayDisabled = false;
            protectedHappiness = null;
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
            if (eventId != NewHourEvent)
                return;

            if (happinessDecayDisabled)
                PreserveHappinessAfterHourlyUpdate();
        }

        private void PreserveHappinessAfterHourlyUpdate()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
            {
                BigHaxLogger.Diagnostic("Happiness hax hourly check skipped: no active save.");
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
                return;
            }

            saveGame.Happiness = protectedHappiness.Value;
            SaveGameManager.MarkChange();
            BigHaxLogger.Diagnostic(
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
}
