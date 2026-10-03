#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Localizor;
using Localizor.LanguageChangeEvent;
using Vehicles.VehicleTypes;

namespace JustRenameIt
{
    internal sealed class VehicleNameEntry
    {
        internal readonly string Key;
        internal readonly string OriginalName;
        internal readonly string CurrentName;
        internal readonly bool IsVanilla;

        internal VehicleNameEntry(string key, string originalName, string currentName, bool isVanilla)
        {
            Key = key;
            OriginalName = originalName;
            CurrentName = currentName;
            IsVanilla = isVanilla;
        }
    }

    internal sealed class VehicleNameService
    {
        private const string SaveKeyPrefix = "justrenameit:vehicle_model_name:";
        private const string PresetBackupPrefix = "justrenameit:vehicle_preset_before:";
        internal const string FantonKey = "ba:vehicletype_petrollsfanton";
        internal const string FantonPresetName = "Rolls-Royce Phantom";
        internal const string LimoKey = "ba:vehicletype_limo";
        internal static readonly IReadOnlyDictionary<string, string> VanillaPresets =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ba:vehicletype_anselmoaf90"] = "Ferrari SF90",
                ["ba:vehicletype_bima320"] = "BMW 320i Touring",
                ["ba:vehicletype_ferdinand112"] = "Porsche 911",
                ["ba:vehicletype_freighttruckt1"] = "Freightliner M2",
                ["ba:vehicletype_honzamimic"] = "Honda Civic",
                ["ba:vehicletype_missamvillian"] = "Audi RS6 Avant",
                ["ba:vehicletype_mersaididash"] = "Mercedes-Benz Sprinter",
                ["ba:vehicletype_mersaidimgagt"] = "Mercedes-AMG GT",
                ["ba:vehicletype_mersaidis500"] = "Mercedes-Benz S 500",
                [FantonKey] = FantonPresetName,
                ["ba:vehicletype_umcdesert"] = "GMC Savana",
                ["ba:vehicletype_umcnunavut"] = "GMC Yukon",
                ["ba:vehicletype_deliverytruck"] = "Grumman-Olson P-800",
                ["ba:vehicletype_vordpony"] = "Ford Mustang",
                ["ba:vehicletype_vordtiaravic"] = "Ford Crown Victoria",
                ["ba:vehicletype_vordv150"] = "Ford F-150",
                [LimoKey] = "Rolls-Royce Executive-Limousine"
            };
        private static readonly MethodInfo? RefreshLocalizedText = typeof(TextLocalizationComponent)
            .GetMethod("UpdateEnabledComponents", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo? LocalizationDictionaryField = typeof(LocalizorManager)
            .GetField("LocalizationDictionary", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> originals = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> appliedKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> absentOriginalKeys = new HashSet<string>(StringComparer.Ordinal);
        private GameInstance? game;
        private string? capturedLocale;

        internal void Load(GameInstance save)
        {
            Unload();
            game = save;
            if (save.modData != null)
                foreach (var pair in save.modData)
                {
                    if (!pair.Key.StartsWith(SaveKeyPrefix, StringComparison.Ordinal)) continue;
                    var key = pair.Key.Substring(SaveKeyPrefix.Length);
                    if (!string.IsNullOrWhiteSpace(key) && IsValidName(pair.Value))
                        aliases[key] = pair.Value.Trim();
                }
            ApplyToCurrentLocale();
            JustRenameItLog.Vehicles($"Vehicle aliases loaded: {aliases.Count}.");
        }

        internal void Unload()
        {
            RestoreOriginals();
            game = null;
            aliases.Clear();
            originals.Clear();
            appliedKeys.Clear();
            absentOriginalKeys.Clear();
            capturedLocale = null;
        }

        internal void OnLanguageChanged()
        {
            // Localizor has rebuilt its locale dictionary before raising this event.
            originals.Clear();
            appliedKeys.Clear();
            absentOriginalKeys.Clear();
            capturedLocale = null;
            if (game != null) ApplyToCurrentLocale();
        }

        internal List<VehicleNameEntry> GetEntries(bool vanilla)
        {
            ApplyToCurrentLocale();
            var result = new List<VehicleNameEntry>();
            foreach (var key in GetVehicleKeys())
            {
                if (IsVanillaKey(key) != vanilla) continue;
                var original = GetOriginalName(key);
                if (string.IsNullOrWhiteSpace(original)) original = key;
                if (key == LimoKey && original == key) continue;
                result.Add(new VehicleNameEntry(key, original,
                    aliases.TryGetValue(key, out var alias) ? alias : original, vanilla));
            }
            return result.OrderBy(entry => entry.OriginalName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        internal bool TrySave(IReadOnlyDictionary<string, string> proposed, out string error, out int changed)
        {
            error = "justrenameit_vehicle_failed";
            changed = 0;
            if (game == null) return false;
            var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in proposed)
            {
                if (!IsRenameableNameKey(pair.Key)) return false;
                var value = (pair.Value ?? string.Empty).Trim();
                if (value.Length > 50 || value.Any(char.IsControl))
                {
                    error = "justrenameit_name_long";
                    return false;
                }
                normalized[pair.Key] = string.Equals(value, GetOriginalName(pair.Key), StringComparison.Ordinal)
                    ? string.Empty : value;
            }

            foreach (var pair in normalized)
            {
                var oldValue = aliases.TryGetValue(pair.Key, out var oldAlias) ? oldAlias : string.Empty;
                if (string.Equals(oldValue, pair.Value, StringComparison.Ordinal)) continue;
                if (pair.Value.Length == 0)
                {
                    aliases.Remove(pair.Key);
                    game.modData?.Remove(SaveKeyPrefix + pair.Key);
                }
                else
                {
                    aliases[pair.Key] = pair.Value;
                    game.modData ??= new Dictionary<string, string>();
                    game.modData[SaveKeyPrefix + pair.Key] = pair.Value;
                }
                changed++;
                JustRenameItLog.Vehicles($"Vehicle model {pair.Key}: '{oldValue}' -> '{pair.Value}'.");
            }

            if (changed == 0)
            {
                error = "justrenameit_no_changes";
                return false;
            }
            SaveGameManager.MarkChange();
            ApplyToCurrentLocale();
            error = string.Empty;
            return true;
        }

        internal bool TryApplyPresets(out string error, out int changed)
        {
            var available = new HashSet<string>(GetEntries(true).Select(entry => entry.Key), StringComparer.Ordinal);
            var proposed = VanillaPresets.Where(pair => available.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (proposed.Count == 0)
            {
                changed = 0;
                error = "justrenameit_vehicle_failed";
                JustRenameItLog.Warn("No registered vanilla vehicle matches a configured preset.");
                return false;
            }
            var backups = proposed.Where(pair => !aliases.TryGetValue(pair.Key, out var current) || current != pair.Value)
                .Where(pair => game?.modData == null || !game.modData.ContainsKey(PresetBackupPrefix + pair.Key))
                .ToDictionary(pair => pair.Key,
                    pair => aliases.TryGetValue(pair.Key, out var alias) ? alias : string.Empty,
                    StringComparer.Ordinal);
            if (!TrySave(proposed, out error, out changed)) return false;
            game!.modData ??= new Dictionary<string, string>();
            foreach (var pair in backups) game.modData[PresetBackupPrefix + pair.Key] = pair.Value;
            JustRenameItLog.Vehicles($"Applied {changed} vanilla presets; stored {backups.Count} undo values.");
            return true;
        }

        internal bool TryUndoPresets(out string error, out int changed)
        {
            var proposed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var preset in VanillaPresets)
            {
                if (!IsRenameableNameKey(preset.Key)) continue;
                if (game?.modData != null && game.modData.TryGetValue(PresetBackupPrefix + preset.Key, out var prior))
                    proposed[preset.Key] = prior;
                else if (aliases.TryGetValue(preset.Key, out var current) && current == preset.Value)
                    proposed[preset.Key] = string.Empty;
            }
            if (proposed.Count == 0)
            {
                changed = 0;
                error = "justrenameit_no_changes";
                return false;
            }
            if (!TrySave(proposed, out error, out changed)) return false;
            foreach (var key in proposed.Keys) game!.modData?.Remove(PresetBackupPrefix + key);
            JustRenameItLog.Vehicles($"Undid {changed} vanilla presets, restoring prior model names.");
            return true;
        }

        internal bool TryUndoAllModded(out string error, out int changed)
        {
            error = "justrenameit_vehicle_failed";
            changed = 0;
            if (game == null) return false;
            var keys = aliases.Keys.Where(key => !IsVanillaKey(key)).ToArray();
            if (keys.Length == 0)
            {
                error = "justrenameit_no_changes";
                return false;
            }
            foreach (var key in keys)
            {
                aliases.Remove(key);
                game.modData?.Remove(SaveKeyPrefix + key);
                JustRenameItLog.Vehicles("Restored original modded vehicle model name: " + key + ".");
            }
            changed = keys.Length;
            SaveGameManager.MarkChange();
            ApplyToCurrentLocale();
            error = string.Empty;
            JustRenameItLog.Vehicles("Restored " + changed + " modded vehicle model names.");
            return true;
        }

        private void ApplyToCurrentLocale()
        {
            var locale = LocalizorManager.LoadedLocale;
            if (string.IsNullOrEmpty(locale)) return;
            if (!TryGetLocaleTable(locale, out var table))
            {
                JustRenameItLog.Warn("Vehicle localization table unavailable for locale " + locale + ".");
                return;
            }
            if (!string.Equals(capturedLocale, locale, StringComparison.Ordinal))
            {
                originals.Clear();
                appliedKeys.Clear();
                absentOriginalKeys.Clear();
                capturedLocale = locale;
            }
            foreach (var key in GetVehicleKeys().Concat(aliases.Keys).Distinct(StringComparer.Ordinal))
            {
                if (originals.ContainsKey(key) || !IsRenameableNameKey(key)) continue;
                if (table.TryGetValue(key, out var original)) originals[key] = original;
                else
                {
                    originals[key] = key.GetLocalization();
                    absentOriginalKeys.Add(key);
                }
            }
            var modified = false;
            foreach (var key in appliedKeys.ToArray())
            {
                if (aliases.ContainsKey(key)) continue;
                if (absentOriginalKeys.Contains(key)) table.Remove(key);
                else if (originals.TryGetValue(key, out var original)) table[key] = original;
                appliedKeys.Remove(key);
                modified = true;
            }
            foreach (var alias in aliases)
            {
                if (!originals.ContainsKey(alias.Key)) continue;
                if (table.TryGetValue(alias.Key, out var current) && current == alias.Value)
                {
                    appliedKeys.Add(alias.Key);
                    continue;
                }
                table[alias.Key] = alias.Value;
                appliedKeys.Add(alias.Key);
                modified = true;
            }
            if (!modified) return;
            JustRenameItLog.Vehicles($"Applied {appliedKeys.Count} model aliases for locale {locale}.");
            try
            {
                RefreshLocalizedText?.Invoke(null, null);
            }
            catch (Exception exception)
            {
                JustRenameItLog.Warn("Vehicle names applied, but visible text could not be refreshed: " + exception.Message);
            }
        }

        private void RestoreOriginals()
        {
            if (capturedLocale != null && TryGetLocaleTable(capturedLocale, out var table))
            {
                foreach (var key in appliedKeys)
                    if (absentOriginalKeys.Contains(key)) table.Remove(key);
                    else if (originals.TryGetValue(key, out var original)) table[key] = original;
                if (appliedKeys.Count > 0)
                    try { RefreshLocalizedText?.Invoke(null, null); }
                    catch (Exception exception)
                    {
                        JustRenameItLog.Warn("Vehicle names restored, but visible text could not be refreshed: " + exception.Message);
                    }
            }
        }

        private string GetOriginalName(string key)
        {
            if (originals.TryGetValue(key, out var original)) return original;
            var locale = LocalizorManager.LoadedLocale;
            if (locale != null && TryGetLocaleTable(locale, out var table) &&
                table.TryGetValue(key, out original)) return original;
            return key.GetLocalization();
        }

        private static bool TryGetLocaleTable(string locale, out Dictionary<string, string> table)
        {
            var tables = LocalizationDictionaryField?.GetValue(null) as
                Dictionary<string, Dictionary<string, string>>;
            if (tables != null && tables.TryGetValue(locale, out var found))
            {
                table = found;
                return true;
            }
            table = null!;
            return false;
        }

        private static IEnumerable<string> GetVehicleKeys() => VehicleTypeHelper.GetVehicleTypeNames()
            .Where(IsRenameableMotorVehicle).Concat(new[] { LimoKey }).Distinct(StringComparer.Ordinal);

        private static bool IsRenameableNameKey(string key) =>
            key == LimoKey || IsRenameableMotorVehicle(key);

        private static bool IsRenameableMotorVehicle(string key)
        {
            if (string.IsNullOrEmpty(key) || !VehicleTypeHelper.IsValidVehicleType(key)) return false;
            return VehicleTypeHelper.GetVehicleType(key)?.IsMotorVehicle == true;
        }

        private static bool IsVanillaKey(string key) => !string.IsNullOrEmpty(key) &&
            key.StartsWith("ba:vehicletype_", StringComparison.Ordinal);

        private static bool IsValidName(string? value) => !string.IsNullOrWhiteSpace(value) &&
            value!.Length <= 50 && !value.Any(char.IsControl);
    }
}
