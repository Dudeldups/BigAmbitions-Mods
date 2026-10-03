#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BigAmbitions.Rivals;
using Helpers;
using JimmysUnityUtilities;
using Localizor;
using Localizor.LanguageChangeEvent;
using UI.Smartphone.Apps.Contacts;
using UnityEngine;

namespace JustRenameIt
{
    internal sealed class SpecialRivalNameEntry
    {
        internal readonly string Id;
        internal readonly string OriginalName;
        internal readonly string CurrentName;

        internal SpecialRivalNameEntry(string id, string originalName, string currentName)
        {
            Id = id;
            OriginalName = originalName;
            CurrentName = currentName;
        }
    }

    internal sealed class GeneralNameService
    {
        private const string SpecialNamePrefix = "justrenameit:special_rival_name:";
        private const string HelpKey = "help_special_rivals_content";
        private static readonly FieldInfo? LocalizationDictionaryField = typeof(LocalizorManager)
            .GetField("LocalizationDictionary", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo? RefreshLocalizedText = typeof(TextLocalizationComponent)
            .GetMethod("UpdateEnabledComponents", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo? ContactIconsField = typeof(ContactsApp)
            .GetField("PredefinedContactIconSprites", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly Dictionary<string, string> originalSpecialNames =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> specialAliases =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Sprite> ownedContactIcons =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private GameInstance? game;
        private string? helpOriginal;
        private string? helpLocale;

        internal void Load(GameInstance save)
        {
            Unload();
            game = save;
            if (save.modData != null)
                foreach (var pair in save.modData)
                {
                    if (!pair.Key.StartsWith(SpecialNamePrefix, StringComparison.Ordinal)) continue;
                    var id = pair.Key.Substring(SpecialNamePrefix.Length);
                    if (!string.IsNullOrWhiteSpace(id) && IsValidName(pair.Value))
                        specialAliases[id] = pair.Value.Trim();
                }
            try { ApplySpecialAliases(); }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
                JustRenameItLog.Warn("Special-rival names could not be applied while loading this save.");
            }
            JustRenameItLog.Rivals($"Loaded {specialAliases.Count} special-rival name aliases.");
        }

        internal void Unload()
        {
            try
            {
                RestoreSpecialHelp();
                SyncContactIcons(false);
                if (game != null)
                    foreach (var rival in GetSpecialRivals())
                        if (originalSpecialNames.TryGetValue(rival.id, out var original))
                        {
                            MigrateContact(game, rival.rivalName, original);
                            rival.rivalName = original;
                        }
            }
            catch (Exception exception) { JustRenameItLog.Error(exception); }
            originalSpecialNames.Clear();
            specialAliases.Clear();
            game = null;
            helpOriginal = null;
            helpLocale = null;
        }

        internal void OnLanguageChanged()
        {
            helpOriginal = null;
            helpLocale = null;
            try { ApplySpecialAliases(); }
            catch (Exception exception) { JustRenameItLog.Error(exception); }
        }

        internal void Apply()
        {
            try { ApplySpecialAliases(); }
            catch (Exception exception) { JustRenameItLog.Error(exception); }
        }

        internal string GetPlayerName() => game?.charactersData?.FirstOrDefault()?.name ?? string.Empty;

        internal bool TrySavePlayerName(string proposed, out string error)
        {
            error = "justrenameit_player_failed";
            var character = game?.charactersData?.FirstOrDefault();
            if (character == null) return false;
            var name = (proposed ?? string.Empty).Trim();
            if (!ValidateName(name, out error)) return false;
            if (character.name == name)
            {
                error = "justrenameit_no_changes";
                return false;
            }
            var previous = character.name;
            character.name = name;
            var live = PlayerHelper.CharacterData;
            if (live != null && !ReferenceEquals(live, character)) live.name = name;
            SaveGameManager.MarkChange();
            JustRenameItLog.Rename($"Player name changed: '{previous}' -> '{name}'.");
            error = string.Empty;
            return true;
        }

        internal List<SpecialRivalNameEntry> GetSpecialEntries()
        {
            ApplySpecialAliases();
            var entries = GetSpecialRivals()
                .Where(rival => originalSpecialNames.ContainsKey(rival.id))
                .OrderBy(rival => originalSpecialNames[rival.id], StringComparer.CurrentCultureIgnoreCase)
                .Select(rival => new SpecialRivalNameEntry(rival.id,
                    originalSpecialNames[rival.id], rival.rivalName))
                .ToList();
            JustRenameItLog.Rivals("General tab special-rival entries=" + entries.Count + ".");
            return entries;
        }

        internal bool TrySaveSpecialNames(IReadOnlyDictionary<string, string> proposed, out string error,
            out int changed)
        {
            error = "justrenameit_special_rivals_failed";
            changed = 0;
            if (game == null) return false;
            ApplySpecialAliases();
            var special = GetSpecialRivals().ToDictionary(rival => rival.id, StringComparer.Ordinal);
            if (special.Count != 4) return false;
            var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in proposed)
            {
                if (!special.ContainsKey(pair.Key)) return false;
                var name = (pair.Value ?? string.Empty).Trim();
                if (!ValidateName(name, out error)) return false;
                normalized[pair.Key] = name;
            }
            if (normalized.Count == 0)
            {
                error = "justrenameit_no_changes";
                return false;
            }
            var targetNames = special.Values.ToDictionary(rival => rival.id,
                rival => normalized.TryGetValue(rival.id, out var name) ? name : rival.rivalName,
                StringComparer.Ordinal);
            var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rival in RivalsHelper.GetAllRivalData().Where(rival => rival != null))
            {
                var name = targetNames.TryGetValue(rival.id, out var target) ? target : rival.rivalName;
                if (!allNames.Add(name))
                {
                    error = "justrenameit_special_rival_duplicate";
                    return false;
                }
            }
            foreach (var pair in normalized)
            {
                var rival = special[pair.Key];
                if (string.Equals(rival.rivalName, pair.Value, StringComparison.Ordinal)) continue;
                var ownedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    rival.rivalName,
                    originalSpecialNames[pair.Key]
                };
                if (specialAliases.TryGetValue(pair.Key, out var oldAlias)) ownedNames.Add(oldAlias);
                var conflictingContact = game.Contacts.FirstOrDefault(contact => contact != null &&
                    string.Equals(contact.id, pair.Value, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(contact.description, "rival", StringComparison.Ordinal) &&
                    !ownedNames.Contains(contact.id));
                if (conflictingContact != null)
                {
                    JustRenameItLog.Rivals($"Special rival id={pair.Key} rejected name '{pair.Value}': " +
                        "another rival contact already uses it.");
                    error = "justrenameit_name_contact_conflict";
                    return false;
                }
            }

            var migrations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in normalized)
            {
                var rival = special[pair.Key];
                if (string.Equals(rival.rivalName, pair.Value, StringComparison.Ordinal)) continue;
                migrations[rival.rivalName] = pair.Value;
                if (string.Equals(pair.Value, originalSpecialNames[pair.Key], StringComparison.Ordinal))
                {
                    specialAliases.Remove(pair.Key);
                    game.modData?.Remove(SpecialNamePrefix + pair.Key);
                }
                else
                {
                    specialAliases[pair.Key] = pair.Value;
                    game.modData ??= new Dictionary<string, string>();
                    game.modData[SpecialNamePrefix + pair.Key] = pair.Value;
                }
                rival.rivalName = pair.Value;
                changed++;
                JustRenameItLog.Rivals($"Special rival id={pair.Key}: '{pair.Value}'.");
            }
            if (changed == 0)
            {
                error = "justrenameit_no_changes";
                return false;
            }
            foreach (var contact in game.Contacts.Where(contact => contact?.category == ContactCategoryName.Rivals))
                if (migrations.TryGetValue(contact.id, out var replacement)) contact.id = replacement;
            if (game.PlayerDefaults != null &&
                game.PlayerDefaults.contactsLastCategoryName == ContactCategoryName.Rivals &&
                game.PlayerDefaults.contactsLastName != null &&
                migrations.TryGetValue(game.PlayerDefaults.contactsLastName, out var selected))
                game.PlayerDefaults.contactsLastName = selected;
            SaveGameManager.MarkChange();
            SyncContactIcons(true);
            UpdateSpecialHelp();
            error = string.Empty;
            return true;
        }

        private void ApplySpecialAliases()
        {
            if (game == null) return;
            var rivals = GetSpecialRivals();
            if (rivals.Count != 4)
            {
                JustRenameItLog.Rivals("Special-rival name aliases deferred: registered=" + rivals.Count + ".");
                return;
            }
            foreach (var rival in rivals)
            {
                if (!originalSpecialNames.ContainsKey(rival.id))
                    originalSpecialNames[rival.id] = rival.rivalName;
                if (!specialAliases.TryGetValue(rival.id, out var alias)) continue;
                var original = originalSpecialNames[rival.id];
                var migrated = MigrateContact(game, original, alias);
                if (rival.rivalName != alias)
                    migrated |= MigrateContact(game, rival.rivalName, alias);
                if (migrated) SaveGameManager.MarkChange();
                rival.rivalName = alias;
            }
            SyncContactIcons(true);
            UpdateSpecialHelp();
        }

        private void SyncContactIcons(bool addAliases)
        {
            var icons = ContactIconsField?.GetValue(null) as Dictionary<string, Sprite>;
            if (icons == null) return;
            foreach (var pair in ownedContactIcons)
                if (icons.TryGetValue(pair.Key, out var icon) && ReferenceEquals(icon, pair.Value))
                    icons.Remove(pair.Key);
            ownedContactIcons.Clear();
            if (!addAliases || specialAliases.Count == 0) return;

            var references = InstanceBehavior<GlobalReferences>.Instance;
            if (references?.contactIcons == null) return;
            foreach (var icon in references.contactIcons)
                if (icon != null && !icons.ContainsKey(icon.name)) icons.Add(icon.name, icon);
            foreach (var pair in specialAliases)
            {
                if (!originalSpecialNames.TryGetValue(pair.Key, out var original) ||
                    string.Equals(original, pair.Value, StringComparison.Ordinal)) continue;
                if (!icons.TryGetValue(original, out var portrait))
                {
                    JustRenameItLog.Rivals("No predefined contact portrait for special rival '" + original + "'.");
                    continue;
                }
                if (icons.ContainsKey(pair.Value)) continue;
                icons.Add(pair.Value, portrait);
                ownedContactIcons.Add(pair.Value, portrait);
                JustRenameItLog.Rivals("Contact portrait mapped: '" + original + "' -> '" + pair.Value + "'.");
            }
        }

        private void UpdateSpecialHelp()
        {
            if (specialAliases.Count == 0 && helpOriginal == null) return;
            var locale = LocalizorManager.LoadedLocale;
            if (string.IsNullOrEmpty(locale) || !TryGetLocaleTable(locale, out var table)) return;
            if (helpLocale != locale)
            {
                helpOriginal = table.TryGetValue(HelpKey, out var original) ? original : null;
                helpLocale = locale;
            }
            if (helpOriginal == null) return;
            var updated = helpOriginal;
            var replacements = new List<KeyValuePair<string, string>>();
            var index = 0;
            foreach (var pair in originalSpecialNames)
            {
                if (!specialAliases.TryGetValue(pair.Key, out var alias) || pair.Value == alias) continue;
                var token = "\uE000JRI" + index++ + "\uE001";
                updated = updated.Replace(pair.Value, token);
                replacements.Add(new KeyValuePair<string, string>(token, alias));
            }
            foreach (var pair in replacements) updated = updated.Replace(pair.Key, pair.Value);
            if (table.TryGetValue(HelpKey, out var current) && current == updated) return;
            table[HelpKey] = updated;
            RefreshHelpText();
        }

        private void RestoreSpecialHelp()
        {
            if (helpOriginal == null || helpLocale == null ||
                !TryGetLocaleTable(helpLocale, out var table)) return;
            table[HelpKey] = helpOriginal;
            RefreshHelpText();
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

        private static void RefreshHelpText()
        {
            try { RefreshLocalizedText?.Invoke(null, null); }
            catch (Exception exception)
            {
                JustRenameItLog.Warn("Special-rival help text changed, but visible text could not refresh: " +
                    exception.Message);
            }
        }

        private static List<RivalData> GetSpecialRivals() =>
            RivalsHelper.GetSpecialRivals()?.Where(special => special?.rivalData != null &&
                !string.IsNullOrWhiteSpace(special.rivalData.id))
                .Select(special => special.rivalData).Distinct().ToList() ?? new List<RivalData>();

        private static bool MigrateContact(GameInstance save, string oldName, string newName)
        {
            if (oldName == newName) return false;
            var changed = false;
            foreach (var contact in save.Contacts.Where(contact => contact?.category == ContactCategoryName.Rivals &&
                         string.Equals(contact.id, oldName, StringComparison.OrdinalIgnoreCase)))
            {
                contact.id = newName;
                changed = true;
            }
            if (save.PlayerDefaults != null &&
                save.PlayerDefaults.contactsLastCategoryName == ContactCategoryName.Rivals &&
                string.Equals(save.PlayerDefaults.contactsLastName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                save.PlayerDefaults.contactsLastName = newName;
                changed = true;
            }
            return changed;
        }

        private static bool ValidateName(string name, out string error)
        {
            error = string.Empty;
            if (name.Length == 0)
            {
                error = "justrenameit_name_empty";
                return false;
            }
            if (name.Length > 50 || name.Any(char.IsControl))
            {
                error = "justrenameit_name_long";
                return false;
            }
            return true;
        }

        private static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) &&
            name!.Length <= 50 && !name.Any(char.IsControl);
    }
}
