#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BigAmbitions.Items;
using Helpers;
using Localizor;
using Localizor.LanguageChangeEvent;

namespace JustRenameIt
{
    internal sealed class ProductNameEntry
    {
        internal readonly string Key;
        internal readonly string OriginalName;
        internal readonly string CurrentName;

        internal ProductNameEntry(string key, string originalName, string currentName)
        {
            Key = key;
            OriginalName = originalName;
            CurrentName = currentName;
        }
    }

    internal sealed class ProductNameService
    {
        private const string ProductSaveKeyPrefix = "justrenameit:product_name:";
        private const string IngredientSaveKeyPrefix = "justrenameit:ingredient_name:";
        private static readonly MethodInfo? RefreshLocalizedText = typeof(TextLocalizationComponent)
            .GetMethod("UpdateEnabledComponents", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo? LocalizationDictionaryField = typeof(LocalizorManager)
            .GetField("LocalizationDictionary", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> originals = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> appliedKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> absentKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly bool ingredients;
        private GameInstance? game;
        private string? capturedLocale;

        internal ProductNameService(bool ingredients = false) => this.ingredients = ingredients;
        private string SaveKeyPrefix => ingredients ? IngredientSaveKeyPrefix : ProductSaveKeyPrefix;
        private string FailureKey => ingredients ? "justrenameit_ingredient_failed" : "justrenameit_product_failed";

        internal void Load(GameInstance save)
        {
            Unload();
            game = save;
            if (!ingredients && save.modData != null)
            {
                var ingredientKeys = new HashSet<string>(GetItemKeys(true), StringComparer.Ordinal);
                var migrated = 0;
                foreach (var pair in save.modData.Where(pair =>
                             pair.Key.StartsWith(ProductSaveKeyPrefix, StringComparison.Ordinal) &&
                             ingredientKeys.Contains(pair.Key.Substring(ProductSaveKeyPrefix.Length))).ToArray())
                {
                    var key = pair.Key.Substring(ProductSaveKeyPrefix.Length);
                    if (!save.modData.ContainsKey(IngredientSaveKeyPrefix + key))
                        save.modData[IngredientSaveKeyPrefix + key] = pair.Value;
                    save.modData.Remove(pair.Key);
                    migrated++;
                    JustRenameItLog.Products("Moved legacy ingredient alias to Ingredients: " + key + ".");
                }
                if (migrated > 0) SaveGameManager.MarkChange();
            }
            if (save.modData != null)
                foreach (var pair in save.modData)
                {
                    if (!pair.Key.StartsWith(SaveKeyPrefix, StringComparison.Ordinal)) continue;
                    var key = pair.Key.Substring(SaveKeyPrefix.Length);
                    if (!string.IsNullOrWhiteSpace(key) && IsValidName(pair.Value))
                        aliases[key] = pair.Value.Trim();
                }
            ApplyToCurrentLocale();
            JustRenameItLog.Products($"Loaded {aliases.Count} {(ingredients ? "ingredient" : "product")} name aliases.");
        }

        internal void Unload()
        {
            RestoreOriginals();
            game = null;
            aliases.Clear();
            originals.Clear();
            appliedKeys.Clear();
            absentKeys.Clear();
            capturedLocale = null;
        }

        internal void OnLanguageChanged()
        {
            originals.Clear();
            appliedKeys.Clear();
            absentKeys.Clear();
            capturedLocale = null;
            if (game != null) ApplyToCurrentLocale();
        }

        internal List<ProductNameEntry> GetEntries(bool vanilla)
        {
            ApplyToCurrentLocale();
            var entries = GetItemKeys(ingredients).Where(key => IsVanilla(key) == vanilla)
                .Select(key =>
                {
                    var original = GetOriginalName(key);
                    return new ProductNameEntry(key, original,
                        aliases.TryGetValue(key, out var alias) ? alias : original);
                })
                .OrderBy(entry => entry.OriginalName, StringComparer.CurrentCultureIgnoreCase).ToList();
            JustRenameItLog.Products($"Discovered {entries.Count} {(vanilla ? "vanilla" : "modded")} " +
                (ingredients ? "ingredients." : "products."));
            return entries;
        }

        internal bool TrySave(IReadOnlyDictionary<string, string> proposed, out string error, out int changed)
        {
            error = FailureKey;
            changed = 0;
            if (game == null) return false;
            var validKeys = new HashSet<string>(GetItemKeys(ingredients), StringComparer.Ordinal);
            var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in proposed)
            {
                if (!validKeys.Contains(pair.Key))
                {
                    JustRenameItLog.Warn("Item rename rejected: item is no longer registered in its group: " + pair.Key + ".");
                    return false;
                }
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
                var old = aliases.TryGetValue(pair.Key, out var alias) ? alias : string.Empty;
                if (old == pair.Value) continue;
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
                JustRenameItLog.Products($"Product {pair.Key}: '{old}' -> '{pair.Value}'.");
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

        internal bool TryReset(bool vanilla, out string error, out int changed)
        {
            error = FailureKey;
            changed = 0;
            if (game == null) return false;
            var keys = aliases.Keys.Where(key => IsVanilla(key) == vanilla).ToArray();
            if (keys.Length == 0)
            {
                error = "justrenameit_no_changes";
                return false;
            }
            foreach (var key in keys)
            {
                aliases.Remove(key);
                game.modData?.Remove(SaveKeyPrefix + key);
            }
            changed = keys.Length;
            SaveGameManager.MarkChange();
            ApplyToCurrentLocale();
            JustRenameItLog.Products($"Reset {changed} {(vanilla ? "vanilla" : "modded")} product names.");
            error = string.Empty;
            return true;
        }

        private void ApplyToCurrentLocale()
        {
            var locale = LocalizorManager.LoadedLocale;
            if (string.IsNullOrEmpty(locale) || !TryGetLocaleTable(locale, out var table)) return;
            if (capturedLocale != locale)
            {
                originals.Clear();
                appliedKeys.Clear();
                absentKeys.Clear();
                capturedLocale = locale;
            }
            foreach (var key in GetItemKeys(ingredients).Concat(aliases.Keys).Distinct(StringComparer.Ordinal))
            {
                if (originals.ContainsKey(key)) continue;
                if (table.TryGetValue(key, out var original)) originals[key] = original;
                else
                {
                    originals[key] = key.GetLocalization();
                    absentKeys.Add(key);
                }
            }
            var modified = false;
            foreach (var key in appliedKeys.ToArray())
            {
                if (aliases.ContainsKey(key)) continue;
                if (absentKeys.Contains(key)) table.Remove(key);
                else if (originals.TryGetValue(key, out var original)) table[key] = original;
                appliedKeys.Remove(key);
                modified = true;
            }
            foreach (var pair in aliases)
            {
                if (table.TryGetValue(pair.Key, out var current) && current == pair.Value)
                {
                    appliedKeys.Add(pair.Key);
                    continue;
                }
                table[pair.Key] = pair.Value;
                appliedKeys.Add(pair.Key);
                modified = true;
            }
            if (!modified) return;
            JustRenameItLog.Products($"Applied {appliedKeys.Count} product aliases for {locale}.");
            try { RefreshLocalizedText?.Invoke(null, null); }
            catch (Exception exception)
            {
                JustRenameItLog.Warn("Product names applied, but visible text could not be refreshed: " + exception.Message);
            }
        }

        private void RestoreOriginals()
        {
            if (capturedLocale == null || !TryGetLocaleTable(capturedLocale, out var table)) return;
            foreach (var key in appliedKeys)
                if (absentKeys.Contains(key)) table.Remove(key);
                else if (originals.TryGetValue(key, out var original)) table[key] = original;
            if (appliedKeys.Count == 0) return;
            try { RefreshLocalizedText?.Invoke(null, null); }
            catch (Exception exception)
            {
                JustRenameItLog.Warn("Product names restored, but visible text could not be refreshed: " + exception.Message);
            }
        }

        private string GetOriginalName(string key)
        {
            if (originals.TryGetValue(key, out var original)) return original;
            if (TryGetLocaleTable(LocalizorManager.LoadedLocale, out var table) &&
                table.TryGetValue(key, out original)) return original;
            return key.GetLocalization();
        }

        private static bool TryGetLocaleTable(string? locale, out Dictionary<string, string> table)
        {
            var tables = LocalizationDictionaryField?.GetValue(null) as
                Dictionary<string, Dictionary<string, string>>;
            if (locale != null && tables != null && tables.TryGetValue(locale, out var found))
            {
                table = found;
                return true;
            }
            table = null!;
            return false;
        }

        private static IEnumerable<string> GetItemKeys(bool ingredient) => ItemsGetter.AllItems == null
            ? Enumerable.Empty<string>()
            : ItemsGetter.AllItems.Where(item => item != null && !string.IsNullOrWhiteSpace(item.itemName) &&
                    !item.isFurniture && string.IsNullOrEmpty(item.vehicleType) &&
                    (item.isADemandedProduct || item.productSalesRatio > 0f) &&
                    (ingredient != (item.isADemandedProduct && item.productSalesRatio > 0f)))
                .Select(item => item.itemName).Distinct(StringComparer.Ordinal);

        private static bool IsVanilla(string key) => key.StartsWith("ba:", StringComparison.OrdinalIgnoreCase);
        private static bool IsValidName(string? value) => !string.IsNullOrWhiteSpace(value) &&
            value!.Length <= 50 && !value.Any(char.IsControl);
    }
}
