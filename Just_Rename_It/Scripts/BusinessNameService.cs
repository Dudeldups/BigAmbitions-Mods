#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Buildings;
using Helpers;
using JimmysUnityUtilities;
using UI;
using UnityEngine;
using UnityEngine.Events;

namespace JustRenameIt
{
    internal sealed class BusinessNameService
    {
        internal const int NameLimit = 40;
        private static readonly HashSet<char> InvalidFileNameCharacters =
            new HashSet<char>(Path.GetInvalidFileNameChars());

        internal bool TryPrepareNativeName(string proposed, BuildingRegistration registration,
            out string safeName, out string error)
        {
            safeName = string.Empty;
            error = string.Empty;
            var game = SaveGameManager.Current;
            if (game == null)
            {
                error = "justrenameit_no_game";
                return false;
            }
            var name = proposed.Trim();
            if (!IsValidName(name, out error)) return false;
            safeName = LogoHelper.GetBusinessNamePathSafe(name);
            foreach (var other in game.BuildingRegistrations)
            {
                if (other == registration || !other.HasEstablishedBusiness) continue;
                if (string.Equals(other.BusinessName, name, StringComparison.OrdinalIgnoreCase))
                {
                    error = "justrenameit_business_duplicate";
                    return false;
                }
                // The game's original save method also checks its temporary, slash-free name.
                if (string.Equals(other.BusinessName, safeName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "justrenameit_business_logo_collision";
                    return false;
                }
                if (other.RentedByPlayer &&
                    string.Equals(LogoHelper.GetBusinessNamePathSafe(other.BusinessName), safeName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "justrenameit_business_logo_collision";
                    return false;
                }
            }
            var currentSafeName = string.IsNullOrEmpty(registration.BusinessName)
                ? string.Empty : LogoHelper.GetBusinessNamePathSafe(registration.BusinessName);
            if (!string.Equals(currentSafeName, safeName,
                    StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(LogoHelper.GetPlayerBusinessLogoPath(name)))
            {
                error = "justrenameit_business_logo_collision";
                return false;
            }
            return true;
        }

        internal bool TrySave(IReadOnlyDictionary<BuildingRegistration, string> proposed,
            out string error, out int count, bool regenerateLogo = true)
        {
            error = string.Empty;
            count = 0;
            var game = SaveGameManager.Current;
            if (game == null)
            {
                error = "justrenameit_no_game";
                return false;
            }

            var registrations = game.BuildingRegistrations;
            var owned = registrations.Where(registration => registration.RentedByPlayer &&
                registration.HasEstablishedBusiness).ToList();
            // The native save action has already selected these registrations.
            // A newly created business may not yet satisfy HasEstablishedBusiness.
            foreach (var registration in proposed.Keys)
                if (registrations.Contains(registration) && !owned.Contains(registration))
                    owned.Add(registration);
            if (proposed.Keys.Any(registration => !owned.Contains(registration)))
            {
                foreach (var registration in proposed.Keys.Where(registration => !owned.Contains(registration)))
                    JustRenameItLog.Businesses("Business rename rejected after native save: " +
                        "inSave=" + registrations.Contains(registration) +
                        ", rentedByPlayer=" + registration.RentedByPlayer +
                        ", hasEstablishedBusiness=" + registration.HasEstablishedBusiness + ".");
                error = "justrenameit_business_failed";
                return false;
            }

            var finalNames = new Dictionary<BuildingRegistration, string>();
            JustRenameItLog.Businesses("Validating names for " + proposed.Count + " businesses.");
            foreach (var registration in owned)
            {
                var name = proposed.TryGetValue(registration, out var draft)
                    ? draft.Trim() : registration.BusinessName;
                if (!IsValidName(name, out error)) return false;
                finalNames[registration] = name;
            }

            var changes = finalNames.Where(pair => !string.Equals(pair.Key.BusinessName,
                pair.Value, StringComparison.Ordinal)).ToList();
            if (changes.Count == 0)
            {
                error = "justrenameit_no_changes";
                return false;
            }

            foreach (var change in changes)
            {
                if (registrations.Any(other => other != change.Key && other.HasEstablishedBusiness &&
                    string.Equals(finalNames.TryGetValue(other, out var finalName)
                            ? finalName : other.BusinessName,
                        change.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "justrenameit_business_duplicate";
                    return false;
                }

                // The game uses the cleaned business name as its logo directory.
                // Distinct display names such as A/B and AB must not share it.
                var logoName = LogoHelper.GetBusinessNamePathSafe(change.Value);
                if (finalNames.Any(other => other.Key != change.Key &&
                    string.Equals(LogoHelper.GetBusinessNamePathSafe(other.Value), logoName,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    JustRenameItLog.Businesses("Rejected name because its logo directory collides: '" + change.Value + "'.");
                    error = "justrenameit_business_logo_collision";
                    return false;
                }
            }

            var moves = new List<KeyValuePair<string, string>>();
            foreach (var change in changes)
            {
                var source = LogoHelper.GetPlayerBusinessLogoPath(change.Key.BusinessName);
                var destination = LogoHelper.GetPlayerBusinessLogoPath(change.Value);
                if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(destination))
                {
                    JustRenameItLog.Businesses("Rejected name because the logo directory already exists: '" + change.Value + "'.");
                    error = "justrenameit_business_logo_collision";
                    return false;
                }
                if (Directory.Exists(source))
                    moves.Add(new KeyValuePair<string, string>(source, destination));
            }

            var completed = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (var move in moves)
                {
                    Directory.Move(move.Key, move.Value);
                    completed.Add(move);
                }
            }
            catch (Exception exception)
            {
                for (var index = completed.Count - 1; index >= 0; index--)
                {
                    try { Directory.Move(completed[index].Value, completed[index].Key); }
                    catch (Exception rollbackError) { JustRenameItLog.Error(rollbackError); }
                }
                JustRenameItLog.Error(exception);
                error = "justrenameit_business_failed";
                return false;
            }

            foreach (var change in changes)
            {
                var previous = change.Key.BusinessName;
                change.Key.BusinessName = change.Value;
                JustRenameItLog.Businesses("Renamed business '" + previous + "' to '" + change.Value + "'.");
            }
            foreach (var change in changes)
                RefreshBusiness(change.Key, regenerateLogo);
            count = changes.Count;
            return true;
        }

        internal void RefreshLogoAfterNativeSave(BuildingRegistration registration) =>
            RefreshBusiness(registration, true);

        private static bool IsValidName(string name, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "justrenameit_name_empty";
                return false;
            }
            if (name.Length > NameLimit)
            {
                error = "justrenameit_business_name_long";
                return false;
            }
            if (name.StartsWith(".", StringComparison.Ordinal) || name.Any(char.IsControl) ||
                name.IndexOfAny(new[] { '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0 ||
                name.Any(character => character != '/' && InvalidFileNameCharacters.Contains(character)) ||
                string.IsNullOrEmpty(LogoHelper.GetBusinessNamePathSafe(name)))
            {
                error = "justrenameit_business_invalid";
                return false;
            }
            return true;
        }

        private static void RefreshBusiness(BuildingRegistration registration, bool regenerateLogo)
        {
            try
            {
                var address = registration.Address;
                foreach (var view in UnityEngine.Object.FindObjectsOfType<BizManBusiness>(true))
                    if (view.buildingRegistration == registration) view.RefreshData();

                var generator = BusinessLogoGenerator.Instance;
                if (generator != null && regenerateLogo)
                {
                    var path = LogoHelper.GetPlayerBusinessLogoPath(registration.BusinessName);
                    if (registration.GetBuildingType() == "ba:buildingtype_warehouse")
                        generator.GenerateWarehouseLogo(registration.BusinessName,
                            BusinessTypeHelper.GetData(registration), path, registration.RentedByPlayer);
                    else if (registration.logoSettings != null)
                        BusinessLogoGenerator.Create(registration.BusinessName, registration.logoSettings, path,
                            registration.RentedByPlayer, new UnityAction(() => NotifyBusinessChanged(registration)));
                }
                NotifyBusinessChanged(registration);
                var city = InstanceBehavior<CityManager>.Instance;
                city?.FindCityBuildingController(address)?.UpdatePoi(null);
                city?.UpdateBillboardsFromBusiness(registration.BusinessName);
            }
            catch (Exception exception)
            {
                JustRenameItLog.Warn("Business name saved, but its UI or logo did not refresh: " + exception.Message);
                JustRenameItLog.Error(exception);
            }
        }

        private static void NotifyBusinessChanged(BuildingRegistration registration)
        {
            GlobalEvents.onBuildingRegistrationChange?.Invoke(registration.Address);
            var current = InstanceBehavior<UIs>.Instance?.fullMenu?.bizMan?.business;
            if (current != null && current.buildingRegistration == registration) current.LoadBusinessLogo();
        }
    }
}
