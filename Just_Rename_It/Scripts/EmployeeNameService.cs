#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BAModAPI;
using BigAmbitions.Characters;
using Entities;
using Localizor;
using UnityEngine;

namespace JustRenameIt
{
    [Serializable]
    internal sealed class NpcNameList
    {
        public string displayName = string.Empty;
        public string[] maleFirstNames = Array.Empty<string>();
        public string[] femaleFirstNames = Array.Empty<string>();
        public string[] lastNames = Array.Empty<string>();
        public string[] femaleLastNames = Array.Empty<string>();
        public string nameSeparator = " ";
        public bool familyNameFirst = false;
        public bool disableSecondGivenName = false;
    }

    internal sealed class EmployeeNameService
    {
        internal const string NoneListId = "none";
        internal const string ChaosListId = "chaos";
        private const string LegacySaveKey = "justrenameit:npc_name_list";
        private const string EmployeeSaveKey = "justrenameit:employee_name_list";
        private const string RivalSaveKey = "justrenameit:rival_name_list";
        private const int SecondFirstNameChancePercent = 1;
        private readonly System.Random random = new System.Random();
        private readonly Dictionary<string, NpcNameList> nameLists =
            new Dictionary<string, NpcNameList>(StringComparer.OrdinalIgnoreCase);
        private NpcNameList[] chaosFormats = Array.Empty<NpcNameList>();
        private string[] chaosMaleFirstNames = Array.Empty<string>();
        private string[] chaosFemaleFirstNames = Array.Empty<string>();
        private string[] chaosLastNames = Array.Empty<string>();
        private string[] chaosFemaleLastNames = Array.Empty<string>();
        private GameInstance? game;
        private string employeeListId = NoneListId;
        private string rivalListId = NoneListId;

        internal EmployeeNameService(ModContext context)
        {
            var directory = Path.Combine(context.ModRootPath, "Config");
            try
            {
                if (!Directory.Exists(directory))
                {
                    JustRenameItLog.Warn("NPC name list directory was not found at " + directory);
                    return;
                }
                foreach (var path in Directory.GetFiles(directory, "*_names.json"))
                {
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    var id = fileName.Substring(0, fileName.Length - "_names".Length);
                    var parsed = JsonUtility.FromJson<NpcNameList>(File.ReadAllText(path));
                    if (parsed == null || !IsValid(parsed.maleFirstNames) ||
                        !IsValid(parsed.femaleFirstNames) || !IsValid(parsed.lastNames) ||
                        (parsed.femaleLastNames != null && parsed.femaleLastNames.Length > 0 &&
                            (!IsValid(parsed.femaleLastNames) ||
                             parsed.femaleLastNames.Length != parsed.lastNames.Length)))
                    {
                        JustRenameItLog.Warn("NPC name list is empty or invalid: " + path);
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(parsed.displayName)) parsed.displayName = id;
                    nameLists[id] = parsed;
                    JustRenameItLog.Names($"Loaded NPC list {id}: male={parsed.maleFirstNames.Length}, " +
                        $"female={parsed.femaleFirstNames.Length}, surnames={parsed.lastNames.Length}.");
                }
                if (nameLists.Count == 0) JustRenameItLog.Warn("No valid NPC name list was found.");
            }
            catch (Exception exception)
            {
                JustRenameItLog.Warn("Could not load NPC name lists: " + exception.Message);
            }
            BuildChaosPools();
        }

        internal bool HasEmployeeAutoNames => employeeListId == ChaosListId || nameLists.ContainsKey(employeeListId);
        internal bool HasRivalAutoNames => rivalListId == ChaosListId || nameLists.ContainsKey(rivalListId);
        internal bool HasEmployeeBulkNames =>
            employeeListId == NoneListId || HasEmployeeAutoNames;
        internal bool HasRivalBulkNames => HasRivalAutoNames;
        internal string EmployeeListId => employeeListId;
        internal string RivalListId => rivalListId;
        internal IEnumerable<KeyValuePair<string, string>> AvailableNameLists => nameLists
            .Where(pair => !string.Equals(pair.Key, "en", StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Value.displayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value.displayName));

        internal void Load(GameInstance save)
        {
            game = save;
            var defaultId = GetDefaultListId(LocalizorManager.LoadedLocale);
            employeeListId = LoadSelection(save, EmployeeSaveKey, defaultId);
            rivalListId = LoadSelection(save, RivalSaveKey, defaultId);
            JustRenameItLog.Names($"Selected name lists: employees={employeeListId}, rivals={rivalListId}.");
        }

        internal void Unload()
        {
            game = null;
            employeeListId = NoneListId;
            rivalListId = NoneListId;
        }

        internal bool SelectNameList(string id, bool forRivals)
        {
            if (game == null || (id != NoneListId && id != ChaosListId && !nameLists.ContainsKey(id))) return false;
            var previous = forRivals ? rivalListId : employeeListId;
            if (string.Equals(previous, id, StringComparison.OrdinalIgnoreCase)) return true;
            if (forRivals) rivalListId = id;
            else employeeListId = id;
            game.modData ??= new Dictionary<string, string>();
            game.modData[forRivals ? RivalSaveKey : EmployeeSaveKey] = id;
            SaveGameManager.MarkChange();
            JustRenameItLog.Names($"Selected {(forRivals ? "rival" : "employee")} name list changed to {id}.");
            return true;
        }

        private string LoadSelection(GameInstance save, string key, string fallback)
        {
            if (save.modData == null) return fallback;
            if (!save.modData.TryGetValue(key, out var selected))
                save.modData.TryGetValue(LegacySaveKey, out selected);
            if (string.Equals(selected, "en", StringComparison.OrdinalIgnoreCase)) return NoneListId;
            return selected != null &&
                (selected == NoneListId || selected == ChaosListId || nameLists.ContainsKey(selected))
                ? selected : fallback;
        }

        private string GetDefaultListId(string? locale)
        {
            var normalized = locale?.Trim().Replace('_', '-').ToLowerInvariant();
            if (normalized == null || normalized.Length == 0) return NoneListId;
            if (normalized == "en" || normalized.StartsWith("en-", StringComparison.Ordinal))
                return NoneListId;
            if (normalized == "zh-cn" || normalized == "zh-tw" || normalized == "zh")
                return nameLists.ContainsKey("zh") ? "zh" : NoneListId;

            if (nameLists.ContainsKey(normalized)) return normalized;
            var language = normalized.Split('-')[0];
            return nameLists.ContainsKey(language) ? language : NoneListId;
        }

        internal bool ShouldReplaceNewName(EmployeeInstance employee)
        {
            if (!HasEmployeeAutoNames || employee.characterData == null)
                return false;
            var currentName = employee.characterData.name;
            var configured = IsConfiguredName(currentName, employee.characterData.gender, employeeListId);
            var duplicate = IsDuplicateEmployeeName(employee, currentName);
            JustRenameItLog.Names($"New employee id={employee.id}, name='{currentName}', " +
                $"configured={configured}, duplicate={duplicate}.");
            return !configured || duplicate;
        }

        internal bool TryCreateName(EmployeeInstance employee, HashSet<string> used, out string result)
        {
            if (employee.characterData == null)
            {
                result = string.Empty;
                return false;
            }

            return TryCreateName(employee.characterData.gender, used, false, out result);
        }

        internal bool TryCreateVanillaName(Gender gender, HashSet<string> used, out string result)
        {
            const int maxAttempts = 256;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                string candidate;
                try
                {
                    candidate = CharacterNames.GetRandomNameForGender(gender);
                }
                catch (Exception exception)
                {
                    JustRenameItLog.Warn($"Vanilla name generation failed for {gender}: {exception.Message}");
                    result = string.Empty;
                    return false;
                }

                candidate = candidate?.Trim() ?? string.Empty;
                if (candidate.Length == 0 || !used.Add(candidate)) continue;
                result = candidate;
                JustRenameItLog.Names($"Generated a vanilla employee name for {gender} after {attempt + 1} attempts.");
                return true;
            }

            result = string.Empty;
            JustRenameItLog.Warn($"Could not generate a unique vanilla name for {gender} after {maxAttempts} attempts.");
            return false;
        }

        internal bool TryCreateName(Gender gender, HashSet<string> used, bool forRivals, out string result)
        {
            result = string.Empty;
            var selected = forRivals ? rivalListId : employeeListId;
            if (selected == ChaosListId)
                return TryCreateChaosName(gender, used, forRivals, out result);
            if (!nameLists.TryGetValue(selected, out var names)) return false;

            var firstNames = gender == Gender.Female
                ? names.femaleFirstNames : names.maleFirstNames;
            var lastNames = GetLastNames(names, gender);
            // A hyphenated given name is one name and must never gain a second given name.
            var regularFirstNames = firstNames.Where(first => first.IndexOf('-') < 0).ToArray();
            var useSecondFirstName = !forRivals && !names.disableSecondGivenName &&
                regularFirstNames.Length > 1 &&
                random.Next(100) < SecondFirstNameChancePercent;
            if (useSecondFirstName && TryCreateTwoFirstNames(regularFirstNames, lastNames, names, used, out result))
            {
                JustRenameItLog.Names("Assigned an employee name with a second given name.");
                return true;
            }

            if (TryCreateSingleFirstName(firstNames, lastNames, names, used, out result)) return true;

            // Keep unusually large employee saves usable after the single-name pool is exhausted.
            // Rivals never receive a second regular given name.
            if (!forRivals && !names.disableSecondGivenName && !useSecondFirstName &&
                TryCreateTwoFirstNames(regularFirstNames, lastNames, names, used, out result))
            {
                JustRenameItLog.Names("Single-given-name pool exhausted; assigned an employee a second given name.");
                return true;
            }

            return false;
        }

        private bool TryCreateChaosName(Gender gender, HashSet<string> used, bool forRivals, out string result)
        {
            result = string.Empty;
            if (chaosFormats.Length == 0) return false;

            var firstNames = gender == Gender.Female ? chaosFemaleFirstNames : chaosMaleFirstNames;
            var lastNames = gender == Gender.Female ? chaosFemaleLastNames : chaosLastNames;
            var format = chaosFormats[random.Next(chaosFormats.Length)];
            var regularFirstNames = firstNames.Where(first => first.IndexOf('-') < 0).ToArray();
            var useSecondFirstName = !forRivals && !format.disableSecondGivenName &&
                regularFirstNames.Length > 1 && random.Next(100) < SecondFirstNameChancePercent;
            if (useSecondFirstName && TryCreateTwoFirstNames(regularFirstNames, lastNames, format, used, out result))
            {
                JustRenameItLog.Names($"Chaos assigned a second given name using format '{format.displayName}'.");
                return true;
            }

            if (TryCreateSingleFirstName(firstNames, lastNames, format, used, out result))
            {
                JustRenameItLog.Names($"Chaos mixed names from {chaosFormats.Length} packages using format " +
                    $"'{format.displayName}'.");
                return true;
            }

            if (!forRivals && !format.disableSecondGivenName && !useSecondFirstName &&
                TryCreateTwoFirstNames(regularFirstNames, lastNames, format, used, out result))
            {
                JustRenameItLog.Names("Chaos single-name combinations were exhausted; used two given names.");
                return true;
            }

            return false;
        }

        private bool TryCreateSingleFirstName(string[] firstNames, string[] lastNames,
            NpcNameList names, HashSet<string> used, out string result)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var candidate = FormatName(firstNames[random.Next(firstNames.Length)], null,
                    lastNames[random.Next(lastNames.Length)], names);
                if (used.Add(candidate))
                {
                    result = candidate;
                    return true;
                }
            }

            foreach (var first in firstNames)
            foreach (var last in lastNames)
            {
                var candidate = FormatName(first, null, last, names);
                if (used.Add(candidate))
                {
                    result = candidate;
                    return true;
                }
            }

            result = string.Empty;
            return false;
        }

        private bool TryCreateTwoFirstNames(string[] firstNames, string[] lastNames,
            NpcNameList names, HashSet<string> used, out string result)
        {
            result = string.Empty;
            if (firstNames.Length < 2) return false;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var first = firstNames[random.Next(firstNames.Length)];
                var middle = firstNames[random.Next(firstNames.Length)];
                if (first == middle) continue;
                var candidate = FormatName(first, middle, lastNames[random.Next(lastNames.Length)], names);
                if (!used.Add(candidate)) continue;
                result = candidate;
                return true;
            }

            foreach (var first in firstNames)
            foreach (var middle in firstNames)
            {
                if (first == middle) continue;
                foreach (var last in lastNames)
                {
                    var candidate = FormatName(first, middle, last, names);
                    if (!used.Add(candidate)) continue;
                    result = candidate;
                    return true;
                }
            }

            return false;
        }

        internal HashSet<string> GetUsedNames(IEnumerable<EmployeeInstance>? employeesBeingRenamed = null)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var game = SaveGameManager.Current;
            if (game == null)
                return used;
            var changingEmployees = employeesBeingRenamed == null
                ? new HashSet<EmployeeInstance>()
                : new HashSet<EmployeeInstance>(employeesBeingRenamed);
            var changingEmployeeNames = new HashSet<string>(changingEmployees
                .Select(employee => employee?.characterData?.name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!), StringComparer.OrdinalIgnoreCase);
            foreach (var employee in game.EmployeeInstances.Concat(game.CandidateEmployeeInstances))
            {
                if (employee != null && changingEmployees.Contains(employee)) continue;
                var employeeName = employee?.characterData?.name;
                if (!string.IsNullOrWhiteSpace(employeeName)) used.Add(employeeName!);
            }
            foreach (var contact in game.Contacts)
            {
                if (contact.IsEmployeeContact && changingEmployeeNames.Contains(contact.id)) continue;
                var contactName = contact?.id;
                if (!string.IsNullOrWhiteSpace(contactName)) used.Add(contactName!);
            }
            return used;
        }

        private bool IsConfiguredName(string? value, Gender gender, string selected)
        {
            if (!nameLists.TryGetValue(selected, out var configuredNames) ||
                value == null || value.Length == 0) return false;
            var firstNames = gender == Gender.Female ? configuredNames.femaleFirstNames : configuredNames.maleFirstNames;
            var lastNames = GetLastNames(configuredNames, gender);
            var separator = GetSeparator(configuredNames);
            foreach (var lastName in lastNames)
            {
                string givenNames;
                if (configuredNames.familyNameFirst)
                {
                    var prefix = lastName + separator;
                    if (!value.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    givenNames = value.Substring(prefix.Length);
                }
                else
                {
                    var suffix = separator + lastName;
                    if (!value.EndsWith(suffix, StringComparison.Ordinal)) continue;
                    givenNames = value.Substring(0, value.Length - suffix.Length);
                }

                if (firstNames.Contains(givenNames)) return true;
                if (configuredNames.disableSecondGivenName || separator.Length == 0) continue;
                foreach (var first in firstNames)
                {
                    if (first.IndexOf('-') >= 0 || givenNames.Length <= first.Length + separator.Length ||
                        !givenNames.StartsWith(first + separator, StringComparison.Ordinal)) continue;
                    var middle = givenNames.Substring(first.Length + separator.Length);
                    if (middle != first && middle.IndexOf('-') < 0 && firstNames.Contains(middle)) return true;
                }
            }
            return false;
        }

        private static string[] GetLastNames(NpcNameList names, Gender gender) =>
            gender == Gender.Female && names.femaleLastNames != null && names.femaleLastNames.Length > 0
                ? names.femaleLastNames : names.lastNames;

        private void BuildChaosPools()
        {
            var packages = nameLists
                .Where(pair => !string.Equals(pair.Key, "en", StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Value)
                .ToArray();
            chaosFormats = packages;
            chaosMaleFirstNames = CombineNames(packages.Select(names => names.maleFirstNames));
            chaosFemaleFirstNames = CombineNames(packages.Select(names => names.femaleFirstNames));
            chaosLastNames = CombineNames(packages.Select(names => names.lastNames));
            chaosFemaleLastNames = CombineNames(packages.Select(names => GetLastNames(names, Gender.Female)));
            if (packages.Length > 0)
                JustRenameItLog.Names($"Built Chaos pools from {packages.Length} packages: " +
                    $"male first names={chaosMaleFirstNames.Length}, female first names={chaosFemaleFirstNames.Length}, " +
                    $"surnames={chaosLastNames.Length} male / {chaosFemaleLastNames.Length} female.");
        }

        private static string[] CombineNames(IEnumerable<string[]> nameGroups) => nameGroups
            .SelectMany(names => names)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        private static string GetSeparator(NpcNameList names) => names.nameSeparator ?? " ";

        private static string FormatName(string first, string? middle, string last, NpcNameList names)
        {
            var givenNames = middle == null ? first : first + GetSeparator(names) + middle;
            return names.familyNameFirst
                ? last + GetSeparator(names) + givenNames
                : givenNames + GetSeparator(names) + last;
        }

        private static bool IsDuplicateEmployeeName(EmployeeInstance target, string? value)
        {
            var game = SaveGameManager.Current;
            return game != null && game.EmployeeInstances.Concat(game.CandidateEmployeeInstances)
                .Any(other => other != target && other?.characterData != null &&
                    string.Equals(other.characterData.name, value, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsValid(string[]? values) => values != null && values.Length > 0 &&
            values.All(value => !string.IsNullOrWhiteSpace(value) && value.Trim() == value) &&
            values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Length;
    }
}
