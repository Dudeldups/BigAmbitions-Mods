#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Rivals;

namespace JustRenameIt
{
    internal sealed class RivalNameService
    {
        private const string SaveKeyPrefix = "justrenameit:rival_name:";
        private readonly EmployeeNameService names;

        internal RivalNameService(EmployeeNameService names) => this.names = names;

        internal void Apply(GameInstance game)
        {
            try
            {
                ApplyNames(game);
            }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
            }
        }

        internal bool RenameAll(GameInstance game, out int count)
        {
            count = 0;
            try
            {
                if (!names.HasRivalBulkNames || !TryGetStandardRivals(game, out var specialIds, out var allRivals,
                        out var standardRivals)) return false;
                var used = names.GetUsedNames();
                foreach (var rival in allRivals)
                    if (!string.IsNullOrWhiteSpace(rival.rivalName)) used.Add(rival.rivalName);

                var changes = new Dictionary<RivalData, string>();
                foreach (var rival in standardRivals)
                {
                    if (!names.TryCreateName(rival.gender, used, true, out var replacement))
                    {
                        JustRenameItLog.Warn("No unused name remains in selected list for rival " + rival.id + ".");
                        return false;
                    }
                    changes.Add(rival, replacement);
                }

                game.modData ??= new Dictionary<string, string>();
                foreach (var pair in changes)
                {
                    game.modData[SaveKeyPrefix + pair.Key.id] = pair.Value;
                    pair.Key.rivalName = pair.Value;
                    JustRenameItLog.Rivals($"Standard rival id={pair.Key.id}: '{pair.Value}'.");
                }
                SaveGameManager.MarkChange();
                count = changes.Count;
                JustRenameItLog.Rivals($"Replaced {count} standard rival names; protectedSpecial={specialIds.Count}.");
                return count > 0;
            }
            catch (Exception exception)
            {
                JustRenameItLog.Error(exception);
                return false;
            }
        }

        private void ApplyNames(GameInstance game)
        {
            if (!TryGetStandardRivals(game, out var specialIds, out var allRivals, out var standardRivals))
                return;

            var used = names.GetUsedNames();
            foreach (var special in allRivals.Where(rival => specialIds.Contains(rival.id)))
                if (!string.IsNullOrWhiteSpace(special.rivalName)) used.Add(special.rivalName);

            var restored = 0;
            var created = 0;
            foreach (var rival in standardRivals)
            {
                var key = SaveKeyPrefix + rival.id;
                if (game.modData != null && game.modData.TryGetValue(key, out var savedName) &&
                    !string.IsNullOrWhiteSpace(savedName))
                {
                    rival.rivalName = savedName;
                    used.Add(savedName);
                    restored++;
                }
            }

            if (names.HasRivalAutoNames)
                foreach (var rival in standardRivals)
                {
                    var key = SaveKeyPrefix + rival.id;
                    if (game.modData != null && game.modData.TryGetValue(key, out var savedName) &&
                        !string.IsNullOrWhiteSpace(savedName)) continue;
                    if (!names.TryCreateName(rival.gender, used, true, out var replacement))
                    {
                        JustRenameItLog.Warn("No unused name remains in selected list for rival " + rival.id + ".");
                        continue;
                    }

                    game.modData ??= new Dictionary<string, string>();
                    game.modData[key] = replacement;
                    rival.rivalName = replacement;
                    created++;
                    JustRenameItLog.Rivals($"Standard rival id={rival.id}: '{replacement}'.");
                }

            if (created > 0) SaveGameManager.MarkChange();
            JustRenameItLog.Rivals($"Standard rivals: total={standardRivals.Count}, " +
                $"restored={restored}, created={created}, protectedSpecial={specialIds.Count}.");
        }

        private static bool TryGetStandardRivals(GameInstance game, out HashSet<string> specialIds,
            out List<RivalData> allRivals, out List<RivalData> standardRivals)
        {
            specialIds = new HashSet<string>(StringComparer.Ordinal);
            allRivals = new List<RivalData>();
            standardRivals = new List<RivalData>();
            if (game.specialRivalStates != null)
                foreach (var state in game.specialRivalStates)
                    if (state != null && !string.IsNullOrEmpty(state.rivalId))
                        specialIds.Add(state.rivalId);

            var specialRivals = RivalsHelper.GetSpecialRivals();
            if (specialRivals != null)
                foreach (var special in specialRivals)
                    if (special != null && special.rivalData != null &&
                        !string.IsNullOrEmpty(special.rivalData.id))
                        specialIds.Add(special.rivalData.id);

            // The four story rivals must be identifiable before any rival is changed.
            if (specialIds.Count < 4)
            {
                JustRenameItLog.Rivals("Rival names deferred: only " + specialIds.Count +
                    " special rival IDs are available.");
                return false;
            }

            allRivals = RivalsHelper.GetAllRivalData().Where(rival => rival != null).ToList();
            var protectedIds = specialIds;
            standardRivals = allRivals.Where(rival =>
                    !string.IsNullOrEmpty(rival.id) && !protectedIds.Contains(rival.id))
                .OrderBy(rival => rival.id, StringComparer.Ordinal).ToList();
            if (standardRivals.Count == 0)
            {
                JustRenameItLog.Rivals("Rival names deferred: no standard rival data is available.");
                return false;
            }
            return true;
        }
    }
}
