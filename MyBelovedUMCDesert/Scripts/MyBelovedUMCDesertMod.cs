#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Items;
using Blueprints;
using BusinessLayoutSets;
using BigAmbitions.SaveSystem.Legacy;
using Controllers;
using Helpers;
using Services;
using UI;
using UI.Smartphone.Apps.Contacts;
using UnityEngine;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(MyBelovedUMCDesert.MyBelovedUMCDesertMod))]

namespace MyBelovedUMCDesert
{
    [ModEntryOnInitializationLoad]
    public sealed class MyBelovedUMCDesertMod : IModBigAmbitions
    {
        internal const string LegacyContactId = "mybelovedumcdesert:dealer_name";
        internal const string VehicleTypeName = "ba:vehicletype_umcdesert";
        internal const string ShowcaseItemName = "ba:itemname_umcdesertshowcase";
        internal const string GeneralUSTrucksContactId = "General US Trucks";
        internal const int RestoredMaxSpeed = 80;
        internal const float RestoredPrice = 33500f;
        internal const int RestoredCargoCapacity = 18;
        internal const float RestoredEnginePower = 150f;
        internal const float RestoredBrakeForce = 6000f;

        internal const bool EnableShowroomDebugLogging = false;

        private static GameObject? registrarObject;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            MyBelovedUMCDesertFileLogger.Initialize(
                context.ModId,
                context.Logger,
                EnableShowroomDebugLogging);
            EnsureRegistrar(context);
            context.Logger.Info($"My Beloved UMC Desert: restoring showroom slot for '{VehicleTypeName}' at '{GeneralUSTrucksContactId}'.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (registrarObject != null)
            {
                UnityEngine.Object.Destroy(registrarObject);
                registrarObject = null;
            }

            ContractItemsForSaleService.RemoveContact(LegacyContactId);
            GeneralUSTrucksStockService.RemoveModdedPhoneOverride("unload");
            MyBelovedUMCDesertFileLogger.Shutdown();
            return Task.CompletedTask;
        }

        private static void EnsureRegistrar(ModContext context)
        {
            if (registrarObject == null)
            {
                registrarObject = new GameObject("MyBelovedUMCDesert.ContactRegistrar");
                UnityEngine.Object.DontDestroyOnLoad(registrarObject);
            }

            var registrar = registrarObject.GetComponent<MyBelovedUMCDesertContactRegistrar>();
            if (registrar == null)
                registrar = registrarObject.AddComponent<MyBelovedUMCDesertContactRegistrar>();

            registrar.Initialize(context);

        }

        internal static void RegisterDealerPhoneStock(ModContext? context)
        {
            UMCDesertStatsService.ApplyRestoredStats(context);

            // Phone calls can build the dealer list before the player has entered the showroom.
            // In that path VehicleContractSettings may use an explicit ContractItemsForSaleService
            // list instead of re-reading the patched layout. Keep that list complete: preserve all
            // vanilla General US Trucks vehicles and add only the restored UMC Desert.
            GeneralUSTrucksStockService.EnsurePhoneStockIncludesUMCDesert("register-dealer-stock");

            RemoveLegacyContact(context);
        }

        internal static void ApplyShowroomReplacement()
        {
            GeneralUSTrucksShowroomReplacementService.ApplyLayoutPatch();
        }

        private static void RemoveLegacyContact(ModContext? context)
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.Contacts == null)
                return;

            var removedCount = 0;
            for (var i = saveGame.Contacts.Count - 1; i >= 0; i--)
            {
                var contact = saveGame.Contacts[i];
                if (contact == null ||
                    !string.Equals(contact.id, LegacyContactId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                saveGame.Contacts.RemoveAt(i);
                removedCount++;
            }

            if (removedCount == 0)
                return;

            ContractItemsForSaleService.RemoveContact(LegacyContactId);
            saveGame.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
            RefreshContactsUi();
            context?.Logger.Info($"My Beloved UMC Desert: removed legacy standalone contact count={removedCount}.");
        }

        private static void RefreshContactsUi()
        {
            try
            {
                InstanceBehavior<UIs>.Instance?.fullMenu?.contactsApp?.RefreshHeader();
                ContactsApp.onContactAdded?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"My Beloved UMC Desert: contacts UI refresh failed: {exception.Message}");
            }
        }
    }

    internal static class GeneralUSTrucksStockService
    {
        private const string TargetBusinessTypeName = "ba:businesstype_cardealership";
        private const string TargetBuildingSize = "ba:buildingsize_m";
        private const int TargetBuildingVersion = 1;
        private const string TargetLayoutName = "IndustryCityCarDealershipTrucks";
        private const string TargetLayoutKey =
            "ba:businesstype_cardealership|ba:buildingsize_m|1|industrycitycardealershiptrucks";

        internal static void EnsurePhoneStockIncludesUMCDesert(string source)
        {
            var mergedStock = new List<string>();
            var hadExplicitServiceStock = ContractItemsForSaleService.TryGetVehiclesForContact(
                MyBelovedUMCDesertMod.GeneralUSTrucksContactId,
                out List<string> existingStock);

            if (hadExplicitServiceStock && existingStock != null)
                AddUniqueRange(mergedStock, existingStock);

            AddVehiclesFromTruckDealerLayout(mergedStock);
            AddUnique(mergedStock, MyBelovedUMCDesertMod.VehicleTypeName);

            // Never create the broken UMC-only override. If the layout is not ready and no vanilla
            // vehicles could be discovered yet, leave/clear the service entry and try again on the
            // next save/game-loaded/building-entry callback.
            if (mergedStock.Count <= 1)
            {
                ClearOnlyModdedPhoneOverride(source + ":insufficient-stock");
                MyBelovedUMCDesertFileLogger.Info(
                    $"Phone stock not overridden source={source}: only {mergedStock.Count} vehicle(s) discovered; waiting for vanilla stock/layout readiness.");
                return;
            }

            if (hadExplicitServiceStock && existingStock != null && SameVehicleList(existingStock, mergedStock))
            {
                MyBelovedUMCDesertFileLogger.Info(
                    $"Phone stock already complete source={source}, count={mergedStock.Count}.");
                return;
            }

            ContractItemsForSaleService.SetVehiclesForContact(
                MyBelovedUMCDesertMod.GeneralUSTrucksContactId,
                mergedStock);

            MyBelovedUMCDesertFileLogger.Info(
                $"Phone stock ensured source={source}: explicit General US Trucks list count={mergedStock.Count}, vehicles={string.Join(",", mergedStock)}.");
        }

        internal static void ClearOnlyModdedPhoneOverride(string source)
        {
            if (!ContractItemsForSaleService.TryGetVehiclesForContact(
                    MyBelovedUMCDesertMod.GeneralUSTrucksContactId,
                    out List<string> existingStock))
            {
                MyBelovedUMCDesertFileLogger.Info($"Phone stock override check source={source}: no explicit General US Trucks service stock present; vanilla layout fallback remains active.");
                return;
            }

            if (!ContainsOnlyUMCDesert(existingStock))
            {
                MyBelovedUMCDesertFileLogger.Info($"Phone stock override check source={source}: explicit General US Trucks service stock was left untouched count={existingStock.Count}.");
                return;
            }

            ContractItemsForSaleService.RemoveContact(MyBelovedUMCDesertMod.GeneralUSTrucksContactId);
            MyBelovedUMCDesertFileLogger.Info($"Phone stock override cleared source={source}: removed UMC-only General US Trucks service stock.");
        }

        internal static void RemoveModdedPhoneOverride(string source)
        {
            if (!ContractItemsForSaleService.TryGetVehiclesForContact(
                    MyBelovedUMCDesertMod.GeneralUSTrucksContactId,
                    out List<string> existingStock))
            {
                return;
            }

            if (!ContainsUMCDesert(existingStock))
                return;

            ContractItemsForSaleService.RemoveContact(MyBelovedUMCDesertMod.GeneralUSTrucksContactId);
            MyBelovedUMCDesertFileLogger.Info($"Phone stock override removed source={source}: removed General US Trucks list containing UMC Desert.");
        }

        internal static void ResetForNewSaveContext(string source)
        {
            EnsurePhoneStockIncludesUMCDesert($"save-context-reset:{source}");
        }

        private static void AddVehiclesFromTruckDealerLayout(List<string> stock)
        {
            try
            {
                var layoutSet = TryGetTruckDealerLayoutSet();
                if (layoutSet?.Items == null)
                    return;

                foreach (var item in layoutSet.Items)
                {
                    var purchaserSettings = item?.playerItemPurchaserSettings;
                    if (purchaserSettings == null || !purchaserSettings.enabled || string.IsNullOrEmpty(purchaserSettings.itemName))
                        continue;

                    var itemDefinition = ItemsGetter.GetByName(purchaserSettings.itemName);
                    if (itemDefinition == null || string.IsNullOrEmpty(itemDefinition.vehicleType))
                        continue;

                    AddUnique(stock, itemDefinition.vehicleType);
                }
            }
            catch (Exception exception)
            {
                MyBelovedUMCDesertFileLogger.Warn(
                    $"Phone stock layout discovery failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static BusinessLayoutSet? TryGetTruckDealerLayoutSet()
        {
            var layoutSets = BusinessLayoutSetHelper.GetAllBusinessLayoutSets();
            if (layoutSets != null && layoutSets.TryGetValue(TargetLayoutKey, out var layoutSet))
                return layoutSet;

            return BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
                TargetBusinessTypeName,
                new BuildingSizeInfo(TargetBuildingSize, TargetBuildingVersion),
                TargetLayoutName.ToLowerInvariant(),
                false);
        }

        private static void AddUniqueRange(List<string> stock, IEnumerable<string> vehicles)
        {
            foreach (var vehicle in vehicles)
                AddUnique(stock, vehicle);
        }

        private static void AddUnique(List<string> stock, string vehicleName)
        {
            if (string.IsNullOrEmpty(vehicleName))
                return;

            for (var i = 0; i < stock.Count; i++)
            {
                if (string.Equals(stock[i], vehicleName, StringComparison.Ordinal))
                    return;
            }

            stock.Add(vehicleName);
        }

        private static bool SameVehicleList(List<string> existingStock, List<string> desiredStock)
        {
            if (existingStock == null || existingStock.Count != desiredStock.Count)
                return false;

            for (var i = 0; i < desiredStock.Count; i++)
            {
                if (!string.Equals(existingStock[i], desiredStock[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static bool ContainsOnlyUMCDesert(List<string> stock)
        {
            if (stock == null || stock.Count == 0)
                return false;

            for (var i = 0; i < stock.Count; i++)
            {
                if (!string.Equals(stock[i], MyBelovedUMCDesertMod.VehicleTypeName, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static bool ContainsUMCDesert(List<string> stock)
        {
            if (stock == null)
                return false;

            for (var i = 0; i < stock.Count; i++)
            {
                if (string.Equals(stock[i], MyBelovedUMCDesertMod.VehicleTypeName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    internal static class MyBelovedUMCDesertFileLogger
    {
        private const string PreferredLogDirectory =
            @"E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Logs\Mods";

        private static readonly object Gate = new object();

        private static string? filePath;
        private static IModLogger? gameLogger;
        private static bool enabled;
        private static bool initialized;

        internal static bool Enabled => enabled && initialized;
        internal static string? FilePath => filePath;

        internal static void Initialize(string modId, IModLogger? logger, bool enableFileLogging)
        {
            enabled = enableFileLogging;
            gameLogger = logger;

            if (!enabled)
            {
                initialized = false;
                filePath = null;
                return;
            }

            try
            {
                var directory = ResolveLogDirectory();
                Directory.CreateDirectory(directory);

                filePath = Path.Combine(directory, "MyBelovedUMCDesert-showroom-debug.log");
                File.WriteAllText(
                    filePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] My Beloved UMC Desert showroom debug started. modId={modId}, logDirectory={directory}{Environment.NewLine}");

                initialized = true;
                gameLogger?.Info($"My Beloved UMC Desert debug log: {filePath}");
            }
            catch (Exception exception)
            {
                initialized = false;
                filePath = null;
                gameLogger?.Warn($"My Beloved UMC Desert file logger failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static string ResolveLogDirectory()
        {
            try
            {
                Directory.CreateDirectory(PreferredLogDirectory);
                return PreferredLogDirectory;
            }
            catch
            {
                var fallback = Path.Combine(Path.GetTempPath(), "MyBelovedUMCDesert", "Logs");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
        }

        internal static void Shutdown()
        {
            Info("Debug log closed.");
            initialized = false;
            enabled = false;
            filePath = null;
            gameLogger = null;
        }

        internal static void Info(string message)
        {
            Write("INFO", message);
        }

        internal static void Warn(string message)
        {
            Write("WARN", message);
        }

        private static void Write(string level, string message)
        {
            if (!Enabled || string.IsNullOrEmpty(filePath))
                return;

            try
            {
                lock (Gate)
                {
                    File.AppendAllText(
                        filePath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Avoid recursive logging if the file system is unavailable.
            }
        }
    }

    internal static class GeneralUSTrucksShowroomReplacementService
    {
        private const string TargetBusinessTypeName = "ba:businesstype_cardealership";
        private const string TargetBuildingSize = "ba:buildingsize_m";
        private const int TargetBuildingVersion = 1;
        private const string TargetLayoutName = "IndustryCityCarDealershipTrucks";
        private const string TargetLayoutKey =
            "ba:businesstype_cardealership|ba:buildingsize_m|1|industrycitycardealershiptrucks";
        private const string TargetOriginalItemName = "ba:itemname_deliverytruckshowcase";
        private const float PositionTolerance = 0.35f;

        private static readonly Vector3 TargetPosition = new Vector3(1035f, 0f, -152f);

        private static bool hasPatchedLayout;
        private static bool hasLoggedPatch;
        private static int patchAttemptCount;
        private static float lastTargetShowcaseRebuildAt = -999f;

        private static readonly BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static void ResetTransientRuntimeState(string source)
        {
            hasPatchedLayout = false;
            hasLoggedPatch = false;
            patchAttemptCount = 0;
            lastTargetShowcaseRebuildAt = -999f;
            MyBelovedUMCDesertFileLogger.Info($"Showroom transient runtime state reset source={source}.");
        }

        internal static void ApplyLayoutPatch()
        {
            patchAttemptCount++;
            MyBelovedUMCDesertFileLogger.Info(
                $"ApplyLayoutPatch attempt={patchAttemptCount}, hasPatchedLayout={hasPatchedLayout}, targetPosition={FormatVector(TargetPosition)}, targetOriginalItem={TargetOriginalItemName}, replacementItem={MyBelovedUMCDesertMod.ShowcaseItemName}.");

            // Do not skip later attempts just because a previous layout object was patched.
            // The game can reload/recreate layout sets between save loads or building entries while
            // this static flag remains true, which would leave a fresh vanilla layout unpatched.
            if (hasPatchedLayout)
                MyBelovedUMCDesertFileLogger.Info("ApplyLayoutPatch continuing although a previous layout object was already patched.");

            var patchedKnownLayout = TryPatchKnownTruckDealerLayout();
            var patchedRegistrationPayload = TryPatchGeneralUSTrucksRegistrationPayload("after-known-layout");
            if (patchedKnownLayout || patchedRegistrationPayload)
                return;

            var registration = FindGeneralUSTrucksRegistration();
            if (registration == null ||
                registration.BuildingCached == null ||
                string.IsNullOrEmpty(registration.businessTypeName) ||
                string.IsNullOrEmpty(registration.Layout))
            {
                MyBelovedUMCDesertFileLogger.Warn("General US Trucks registration was not ready; layout patch not applied on this attempt.");
                return;
            }

            MyBelovedUMCDesertFileLogger.Info(
                $"Registration fallback found businessName={registration.BusinessName}, businessType={registration.businessTypeName}, layout={registration.Layout}, buildingSize={registration.BuildingCached.BuildingSize}, buildingVersion={registration.BuildingCached.BuildingVersion}.");

            var layoutSet = BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
                registration.businessTypeName,
                new BuildingSizeInfo(registration.BuildingCached),
                registration.Layout.ToLowerInvariant(),
                false);
            if (layoutSet?.Items == null)
            {
                MyBelovedUMCDesertFileLogger.Warn("Registration fallback layout set was null or had no item list.");
                return;
            }

            var patchedFallbackLayout = TryPatchLayoutSet(layoutSet, "registration");
            var patchedFallbackPayload = TryPatchGeneralUSTrucksRegistrationPayload("after-registration-fallback");
            if (!patchedFallbackLayout && !patchedFallbackPayload)
                MyBelovedUMCDesertFileLogger.Warn("Neither the layout set nor the live building registration payload could be patched on this attempt.");
        }

        internal static bool TryPatchGeneralUSTrucksRegistrationPayload(string source)
        {
            var registration = FindGeneralUSTrucksRegistration();
            if (registration == null)
            {
                MyBelovedUMCDesertFileLogger.Info($"Registration payload patch skipped source={source}: General US Trucks registration not ready.");
                return false;
            }

            var changedCount = 0;
            changedCount += TryPatchItemsInMember(registration, "itemInstances", source);
            changedCount += TryPatchItemsInMember(registration, "itemsInBuilding", source);
            changedCount += TryPatchItemsInMember(registration, "deliveredItems", source);
            changedCount += TryPatchItemsInMember(registration, "interiorDesigns", source);

            var buildingCached = registration.BuildingCached;
            if (buildingCached != null)
            {
                changedCount += TryPatchItemsInMember(buildingCached, "itemInstances", source + ".buildingCached");
                changedCount += TryPatchItemsInMember(buildingCached, "itemsInBuilding", source + ".buildingCached");
                changedCount += TryPatchItemsInMember(buildingCached, "deliveredItems", source + ".buildingCached");
            }

            if (changedCount <= 0)
            {
                MyBelovedUMCDesertFileLogger.Info(
                    $"Registration payload patch source={source}: no target delivery-truck showcase payload item found or everything was already corrected.");
                return false;
            }

            hasPatchedLayout = true;
            MyBelovedUMCDesertFileLogger.Info(
                $"Registration payload patch source={source}: patchedOrConfirmedItems={changedCount}, address={registration.Address}, layout={registration.Layout}.");
            return true;
        }

        private static bool TryPatchKnownTruckDealerLayout()
        {
            MyBelovedUMCDesertFileLogger.Info(
                $"Known layout lookup starting loadingLayouts={BusinessLayoutSetHelper.loadingLayouts}, key={TargetLayoutKey}.");

            var layoutSets = BusinessLayoutSetHelper.GetAllBusinessLayoutSets();
            MyBelovedUMCDesertFileLogger.Info(
                $"Known layout cache after GetAll count={layoutSets?.Count ?? -1}, loadingLayouts={BusinessLayoutSetHelper.loadingLayouts}, hasTargetKey={layoutSets != null && layoutSets.ContainsKey(TargetLayoutKey)}.");

            if (layoutSets != null && !layoutSets.ContainsKey(TargetLayoutKey))
                LogTruckDealerLayoutCandidates(layoutSets);

            if (layoutSets == null || !layoutSets.TryGetValue(TargetLayoutKey, out var layoutSet))
            {
                MyBelovedUMCDesertFileLogger.Info("Known layout key was not in cache; trying direct GetOrLoadBusinessLayoutSet fallback.");
                layoutSet = BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
                    TargetBusinessTypeName,
                    new BuildingSizeInfo(TargetBuildingSize, TargetBuildingVersion),
                    TargetLayoutName.ToLowerInvariant(),
                    false);
            }

            if (layoutSet?.Items == null)
            {
                MyBelovedUMCDesertFileLogger.Warn(
                    $"Known truck dealer layout was not available businessType={TargetBusinessTypeName}, buildingSize={TargetBuildingSize}, buildingVersion={TargetBuildingVersion}, layout={TargetLayoutName}.");
                return false;
            }

            return TryPatchLayoutSet(layoutSet, "known-layout");
        }

        private static void LogTruckDealerLayoutCandidates(Dictionary<string, BusinessLayoutSet> layoutSets)
        {
            var loggedCount = 0;
            foreach (var layoutSetPair in layoutSets)
            {
                var layoutSet = layoutSetPair.Value;
                if (layoutSet == null ||
                    !string.Equals(layoutSet.BusinessType, TargetBusinessTypeName, StringComparison.Ordinal) ||
                    layoutSetPair.Key.IndexOf("cardealershiptrucks", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                loggedCount++;
                MyBelovedUMCDesertFileLogger.Info(
                    $"Truck dealer layout candidate key={layoutSetPair.Key}, layout={layoutSet.LayoutName}, businessType={layoutSet.BusinessType}, buildingSize={layoutSet.BuildingSize}, buildingVersion={layoutSet.BuildingVersion}, itemCount={layoutSet.Items?.Count ?? -1}.");
            }

            if (loggedCount == 0)
                MyBelovedUMCDesertFileLogger.Warn("No truck dealer layout candidates were present in the loaded layout cache.");
        }

        private static bool TryPatchLayoutSet(BusinessLayoutSet layoutSet, string source)
        {
            if (layoutSet?.Items == null)
            {
                MyBelovedUMCDesertFileLogger.Warn($"TryPatchLayoutSet received null item list source={source}.");
                return false;
            }

            var itemCount = 0;
            var deliveryShowcaseCount = 0;
            var desertShowcaseCount = 0;
            foreach (var item in layoutSet.Items)
            {
                itemCount++;
                if (item != null && string.Equals(item.itemName, TargetOriginalItemName, StringComparison.Ordinal))
                    deliveryShowcaseCount++;
                if (item != null && string.Equals(item.itemName, MyBelovedUMCDesertMod.ShowcaseItemName, StringComparison.Ordinal))
                    desertShowcaseCount++;

                if (item == null || !IsAtTargetPosition(item.position))
                    continue;

                MyBelovedUMCDesertFileLogger.Info(
                    $"Exact target position candidate source={source}, layout={layoutSet.LayoutName}, businessType={layoutSet.BusinessType}, id={item.id}, item={item.itemName}, purchaserItem={item.playerItemPurchaserSettings?.itemName}, position={FormatVector(item.position)}.");

                if (!string.Equals(item.itemName, TargetOriginalItemName, StringComparison.Ordinal) &&
                    !string.Equals(item.itemName, MyBelovedUMCDesertMod.ShowcaseItemName, StringComparison.Ordinal))
                {
                    MyBelovedUMCDesertFileLogger.Info(
                        $"Not patching exact-position item because itemName '{item.itemName}' is not '{TargetOriginalItemName}'.");
                    continue;
                }

                MyBelovedUMCDesertFileLogger.Info(
                    $"Patching exact showroom layout item source={source}, layout={layoutSet.LayoutName}, businessType={layoutSet.BusinessType}, id={item.id}, from={item.itemName}, to={MyBelovedUMCDesertMod.ShowcaseItemName}.");

                item.itemName = MyBelovedUMCDesertMod.ShowcaseItemName;
                if (item.playerItemPurchaserSettings != null)
                    item.playerItemPurchaserSettings.itemName = MyBelovedUMCDesertMod.ShowcaseItemName;

                hasPatchedLayout = true;
                if (!hasLoggedPatch)
                {
                    hasLoggedPatch = true;
                    MyBelovedUMCDesertFileLogger.Info(
                        $"Patched exact showroom layout item source={source}, layout={layoutSet.LayoutName}, businessType={layoutSet.BusinessType}, id={item.id}, position={FormatVector(item.position)}, item={MyBelovedUMCDesertMod.ShowcaseItemName}.");
                }

                return true;
            }

            MyBelovedUMCDesertFileLogger.Info(
                $"Scanned layout without target match source={source}, layout={layoutSet.LayoutName}, businessType={layoutSet.BusinessType}, buildingSize={layoutSet.BuildingSize}, buildingVersion={layoutSet.BuildingVersion}, itemCount={itemCount}, deliveryShowcaseCount={deliveryShowcaseCount}, desertShowcaseCount={desertShowcaseCount}.");
            return false;
        }


        private static int TryPatchItemsInMember(object owner, string memberName, string source)
        {
            var value = GetMemberValue(owner, memberName);
            if (value == null)
                return 0;

            return TryPatchItemsInValue(value, $"{source}.{memberName}", 0, new HashSet<object>(ReferenceComparer.Instance));
        }

        private static int TryPatchItemsInValue(object value, string source, int depth, HashSet<object> visited)
        {
            if (value == null || depth > 4 || value is string)
                return 0;

            var type = value.GetType();
            if (!type.IsValueType)
            {
                if (visited.Contains(value))
                    return 0;

                visited.Add(value);
            }

            var changed = TryPatchShowcasePayloadObject(value, source);
            if (changed > 0)
                return changed;

            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                    changed += TryPatchItemsInValue(entry.Value, source + ".dictValue", depth + 1, visited);

                return changed;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item == null || item is string)
                        continue;

                    changed += TryPatchItemsInValue(item, source + ".item", depth + 1, visited);
                }
            }

            return changed;
        }

        private static int TryPatchShowcasePayloadObject(object item, string source)
        {
            var itemName = GetStringMember(item, "itemName");
            if (string.IsNullOrEmpty(itemName))
                return 0;

            if (!string.Equals(itemName, TargetOriginalItemName, StringComparison.Ordinal) &&
                !string.Equals(itemName, MyBelovedUMCDesertMod.ShowcaseItemName, StringComparison.Ordinal))
            {
                return 0;
            }

            if (!TryGetVector3Member(item, "position", out var position) || !IsAtTargetPosition(position))
                return 0;

            var patchedFields = 0;
            if (TrySetStringMember(item, "itemName", MyBelovedUMCDesertMod.ShowcaseItemName))
                patchedFields++;

            if (TrySetStringMember(item, "vehicleName", MyBelovedUMCDesertMod.VehicleTypeName))
                patchedFields++;

            var purchaserSettings = GetMemberValue(item, "playerItemPurchaserSettings");
            if (purchaserSettings != null && TrySetStringMember(purchaserSettings, "itemName", MyBelovedUMCDesertMod.ShowcaseItemName))
                patchedFields++;

            TryResetItemCache(item);

            MyBelovedUMCDesertFileLogger.Info(
                $"Patched/confirmed registration payload showcase source={source}, type={item.GetType().Name}, from={itemName}, to={MyBelovedUMCDesertMod.ShowcaseItemName}, position={FormatVector(position)}, patchedFields={patchedFields}.");
            return Math.Max(1, patchedFields);
        }

        internal static void RepairLiveShowroomState(string source)
        {
            try
            {
                var controllers = UnityEngine.Object.FindObjectsOfType<ShowcaseVehicleController>(true);
                var targetCount = 0;
                var patchedCount = 0;
                var rebuiltCount = 0;

                foreach (var controller in controllers)
                {
                    if (controller == null)
                        continue;

                    var distance = Vector3.Distance(controller.transform.position, TargetPosition);
                    if (distance > PositionTolerance)
                        continue;

                    targetCount++;

                    var beforeSummary = DescribeLiveShowcase(controller);
                    var beforeItemInstanceName = controller.ItemInstance?.itemName;
                    var beforeItemInstancePurchaser = controller.ItemInstance?.playerItemPurchaserSettings?.itemName;
                    var needsPatch =
                        string.Equals(controller.itemName, TargetOriginalItemName, StringComparison.Ordinal) ||
                        string.Equals(controller.vehicleName, "ba:vehicletype_deliverytruck", StringComparison.Ordinal) ||
                        string.Equals(controller.playerItemPurchaserSettings?.itemName, TargetOriginalItemName, StringComparison.Ordinal) ||
                        string.Equals(beforeItemInstanceName, TargetOriginalItemName, StringComparison.Ordinal) ||
                        string.Equals(beforeItemInstancePurchaser, TargetOriginalItemName, StringComparison.Ordinal) ||
                        !string.Equals(controller.itemName, MyBelovedUMCDesertMod.ShowcaseItemName, StringComparison.Ordinal) ||
                        !string.Equals(controller.vehicleName, MyBelovedUMCDesertMod.VehicleTypeName, StringComparison.Ordinal);

                    PatchLiveControllerFields(controller);
                    patchedCount++;

                    // Important: Changing fields alone can leave an already-instantiated visual child unchanged.
                    // Rebuild only this exact target slot, and only when the target still looked wrong before
                    // patching or when no enabled renderer exists. This avoids rebuilding forever on every
                    // enter-building callback after a main-menu/save reload.
                    var shouldRebuild = needsPatch || !HasEnabledRenderer(controller.gameObject);
                    var canRebuildNow = Time.unscaledTime - lastTargetShowcaseRebuildAt > 0.75f;
                    if (shouldRebuild && canRebuildNow)
                    {
                        if (TryReinstantiateLiveShowcaseController(controller, source, beforeSummary, needsPatch))
                        {
                            rebuiltCount++;
                            lastTargetShowcaseRebuildAt = Time.unscaledTime;
                        }
                    }
                    else if (shouldRebuild)
                    {
                        MyBelovedUMCDesertFileLogger.Info(
                            $"Live showroom target rebuild skipped source={source}: already rebuilt target recently, before={beforeSummary}, afterFields={DescribeLiveShowcase(controller)}.");
                    }
                    else
                    {
                        MyBelovedUMCDesertFileLogger.Info(
                            $"Live showroom target rebuild not needed source={source}: target fields/renderers already look correct, state={DescribeLiveShowcase(controller)}.");
                    }
                }

                // ContractVehicleForSale scanning uses Resources.FindObjectsOfTypeAll and returned
                // zero candidates in runtime tests. Keep it out of the normal repair path to avoid
                // avoidable stutter in the showroom.

                MyBelovedUMCDesertFileLogger.Info(
                    $"Live showroom repair summary source={source}: targetControllers={targetCount}, patchedFields={patchedCount}, rebuilt={rebuiltCount}, scannedControllers={controllers.Length}.");
            }
            catch (Exception exception)
            {
                MyBelovedUMCDesertFileLogger.Warn($"Live showroom repair failed source={source}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static void RepairLiveContractVehicleForSaleObjects(string source)
        {
            try
            {
                var behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
                var scanned = 0;
                var loggedNearby = 0;
                var targetCount = 0;
                var patchedFields = 0;
                var refreshed = 0;

                foreach (var behaviour in behaviours)
                {
                    if (behaviour == null)
                        continue;

                    var type = behaviour.GetType();
                    if (!string.Equals(type.Name, "ContractVehicleForSale", StringComparison.Ordinal) &&
                        !string.Equals(type.FullName, "Buildings.ContractVehicleForSale", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!behaviour.gameObject.scene.IsValid())
                        continue;

                    scanned++;
                    var distance = Vector3.Distance(behaviour.transform.position, TargetPosition);
                    var vehicleName = GetStringMember(behaviour, "vehicleName") ?? GetStringMember(behaviour, "vehicleTypeName");
                    var currentVehicleController = GetMemberValue(behaviour, "currentVehicleController");
                    var currentVehicleName = GetStringMember(currentVehicleController, "vehicleName") ??
                                             GetStringMember(GetMemberValue(currentVehicleController, "vehicleInstance"), "vehicleName");

                    if (distance <= 12f &&
                        (string.Equals(vehicleName, "ba:vehicletype_deliverytruck", StringComparison.Ordinal) ||
                         string.Equals(vehicleName, MyBelovedUMCDesertMod.VehicleTypeName, StringComparison.Ordinal) ||
                         string.Equals(currentVehicleName, "ba:vehicletype_deliverytruck", StringComparison.Ordinal) ||
                         string.Equals(currentVehicleName, MyBelovedUMCDesertMod.VehicleTypeName, StringComparison.Ordinal)))
                    {
                        loggedNearby++;
                        MyBelovedUMCDesertFileLogger.Info(
                            $"Nearby ContractVehicleForSale source={source}, distanceToTarget={distance:0.###}, {DescribeContractVehicleForSale(behaviour)}.");
                    }

                    // The second delivery truck showcase in this room is about 8.5 units away.
                    // Keep this intentionally tight so only the exact missing/hidden UMC slot is touched.
                    if (distance > 1.75f)
                        continue;

                    targetCount++;
                    var before = DescribeContractVehicleForSale(behaviour);
                    var changed = PatchContractVehicleForSaleFields(behaviour);
                    if (changed > 0)
                        patchedFields += changed;

                    var refreshResult = TryRefreshContractVehicleForSale(behaviour);
                    if (!refreshResult.StartsWith("none", StringComparison.Ordinal))
                        refreshed++;

                    MyBelovedUMCDesertFileLogger.Info(
                        $"ContractVehicleForSale target repair source={source}, changedFields={changed}, refresh={refreshResult}, before={before}, after={DescribeContractVehicleForSale(behaviour)}.");
                }

                MyBelovedUMCDesertFileLogger.Info(
                    $"ContractVehicleForSale repair summary source={source}: scanned={scanned}, nearbyLogged={loggedNearby}, targetCount={targetCount}, patchedFields={patchedFields}, refreshed={refreshed}.");
            }
            catch (Exception exception)
            {
                MyBelovedUMCDesertFileLogger.Warn($"ContractVehicleForSale repair failed source={source}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static int PatchContractVehicleForSaleFields(MonoBehaviour behaviour)
        {
            var changed = 0;
            if (TrySetStringMember(behaviour, "vehicleName", MyBelovedUMCDesertMod.VehicleTypeName))
                changed++;
            if (TrySetStringMember(behaviour, "vehicleTypeName", MyBelovedUMCDesertMod.VehicleTypeName))
                changed++;

            var vehicleInstance = GetMemberValue(behaviour, "vehicleInstance") ??
                                  GetMemberValue(behaviour, "currentVehicleInstance") ??
                                  GetMemberValue(behaviour, "_vehicleInstance");
            if (vehicleInstance != null)
                changed += PatchVehicleInstanceObject(vehicleInstance);

            var currentVehicleController = GetMemberValue(behaviour, "currentVehicleController") ??
                                           GetMemberValue(behaviour, "vehicleController") ??
                                           GetMemberValue(behaviour, "_vehicleController");
            if (currentVehicleController != null)
                changed += PatchVehicleControllerObject(currentVehicleController);

            TryResetItemCache(behaviour);
            return changed;
        }

        private static int PatchVehicleInstanceObject(object vehicleInstance)
        {
            var changed = 0;
            if (TrySetStringMember(vehicleInstance, "vehicleName", MyBelovedUMCDesertMod.VehicleTypeName))
                changed++;
            if (TrySetStringMember(vehicleInstance, "vehicleTypeName", MyBelovedUMCDesertMod.VehicleTypeName))
                changed++;
            if (TrySetStringMember(vehicleInstance, "VehicleTypeName", MyBelovedUMCDesertMod.VehicleTypeName))
                changed++;

            TryResetItemCache(vehicleInstance);
            return changed;
        }

        private static int PatchVehicleControllerObject(object vehicleController)
        {
            var changed = 0;
            if (TrySetStringMember(vehicleController, "vehicleName", MyBelovedUMCDesertMod.VehicleTypeName))
                changed++;
            if (TrySetStringMember(vehicleController, "vehicleTypeName", MyBelovedUMCDesertMod.VehicleTypeName))
                changed++;

            var vehicleInstance = GetMemberValue(vehicleController, "vehicleInstance") ??
                                  GetMemberValue(vehicleController, "VehicleInstance");
            if (vehicleInstance != null)
                changed += PatchVehicleInstanceObject(vehicleInstance);

            TryResetItemCache(vehicleController);
            return changed;
        }

        private static string TryRefreshContractVehicleForSale(MonoBehaviour behaviour)
        {
            var type = behaviour.GetType();

            var stringMethodResult = TryInvokeMethodWithString(type, behaviour, "SetVehicle", MyBelovedUMCDesertMod.VehicleTypeName);
            if (stringMethodResult != null)
                return stringMethodResult;

            var setVehicleInstance = FindMethod(type, "SetVehicleInstance", 1);
            if (setVehicleInstance != null)
            {
                var parameterType = setVehicleInstance.GetParameters()[0].ParameterType;
                var vehicleInstance = TryCreateVehicleInstance(parameterType);
                if (vehicleInstance != null)
                {
                    try
                    {
                        setVehicleInstance.Invoke(behaviour, new[] { vehicleInstance });
                        return "invoked:SetVehicleInstance";
                    }
                    catch (Exception exception)
                    {
                        return "failed:SetVehicleInstance:" + exception.GetType().Name;
                    }
                }
            }

            var zeroParameterCandidates = new[]
            {
                "RefreshVehicle",
                "UpdateVehicle",
                "SpawnVehicle",
                "CreateVehicle",
                "SetVehiclePositionAndRotation",
                "SetVehicleToSafePosition",
                "ResetVehicleShowcaseColor"
            };

            foreach (var candidate in zeroParameterCandidates)
            {
                var method = type.GetMethod(candidate, MemberFlags, null, Type.EmptyTypes, null);
                if (method == null)
                    continue;

                try
                {
                    method.Invoke(behaviour, null);
                    return "invoked:" + candidate;
                }
                catch (Exception exception)
                {
                    return "failed:" + candidate + ":" + exception.GetType().Name;
                }
            }

            return "none:no-refresh-method-found";
        }

        private static string? TryInvokeMethodWithString(Type type, object instance, string methodName, string argument)
        {
            foreach (var method in type.GetMethods(MemberFlags))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
                    continue;

                try
                {
                    method.Invoke(instance, new object[] { argument });
                    return "invoked:" + methodName + "(string)";
                }
                catch (Exception exception)
                {
                    return "failed:" + methodName + "(string):" + exception.GetType().Name;
                }
            }

            return null;
        }

        private static MethodInfo? FindMethod(Type type, string methodName, int parameterCount)
        {
            foreach (var method in type.GetMethods(MemberFlags))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;
                if (method.GetParameters().Length == parameterCount)
                    return method;
            }

            return null;
        }

        private static object? TryCreateVehicleInstance(Type vehicleInstanceType)
        {
            try
            {
                foreach (var constructor in vehicleInstanceType.GetConstructors(MemberFlags))
                {
                    var parameters = constructor.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                    {
                        var instance = constructor.Invoke(new object[] { MyBelovedUMCDesertMod.VehicleTypeName });
                        PatchVehicleInstanceObject(instance);
                        return instance;
                    }
                }

                var parameterless = vehicleInstanceType.GetConstructor(MemberFlags, null, Type.EmptyTypes, null);
                if (parameterless != null)
                {
                    var instance = parameterless.Invoke(null);
                    PatchVehicleInstanceObject(instance);
                    return instance;
                }
            }
            catch (Exception exception)
            {
                MyBelovedUMCDesertFileLogger.Warn($"VehicleInstance creation failed type={vehicleInstanceType.FullName}: {exception.GetType().Name}: {exception.Message}");
            }

            return null;
        }

        private static string DescribeContractVehicleForSale(MonoBehaviour behaviour)
        {
            var vehicleInstance = GetMemberValue(behaviour, "vehicleInstance") ??
                                  GetMemberValue(behaviour, "currentVehicleInstance") ??
                                  GetMemberValue(behaviour, "_vehicleInstance");
            var currentVehicleController = GetMemberValue(behaviour, "currentVehicleController") ??
                                           GetMemberValue(behaviour, "vehicleController") ??
                                           GetMemberValue(behaviour, "_vehicleController");

            return
                $"name={behaviour.name}, activeSelf={behaviour.gameObject.activeSelf}, activeInHierarchy={behaviour.gameObject.activeInHierarchy}, " +
                $"vehicleName={GetStringMember(behaviour, "vehicleName")}, vehicleTypeName={GetStringMember(behaviour, "vehicleTypeName")}, " +
                $"vehicleInstanceName={GetStringMember(vehicleInstance, "vehicleName") ?? GetStringMember(vehicleInstance, "vehicleTypeName")}, " +
                $"currentVehicleController={DescribePossibleVehicleController(currentVehicleController)}, " +
                $"position={FormatVector(behaviour.transform.position)}, renderers={DescribeRenderers(behaviour.gameObject)}";
        }

        private static string DescribePossibleVehicleController(object? vehicleController)
        {
            if (vehicleController == null)
                return "<null>";

            if (vehicleController is Component component)
            {
                return
                    $"name={component.name}, type={component.GetType().Name}, activeSelf={component.gameObject.activeSelf}, activeInHierarchy={component.gameObject.activeInHierarchy}, " +
                    $"vehicleName={GetStringMember(vehicleController, "vehicleName") ?? GetStringMember(GetMemberValue(vehicleController, "vehicleInstance"), "vehicleName")}, " +
                    $"position={FormatVector(component.transform.position)}, renderers={DescribeRenderers(component.gameObject)}";
            }

            return $"type={vehicleController.GetType().FullName}";
        }

        private static bool HasEnabledRenderer(GameObject root)
        {
            if (root == null)
                return false;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                    return true;
            }

            return false;
        }

        private static void PatchLiveControllerFields(ShowcaseVehicleController controller)
        {
            controller.itemName = MyBelovedUMCDesertMod.ShowcaseItemName;
            controller.vehicleName = MyBelovedUMCDesertMod.VehicleTypeName;
            controller.name = "UMCDesertShowcase(Clone)";

            if (controller.playerItemPurchaserSettings != null)
                controller.playerItemPurchaserSettings.itemName = MyBelovedUMCDesertMod.ShowcaseItemName;

            var itemInstance = controller.ItemInstance;
            if (itemInstance != null)
            {
                itemInstance.itemName = MyBelovedUMCDesertMod.ShowcaseItemName;
                if (itemInstance.playerItemPurchaserSettings != null)
                    itemInstance.playerItemPurchaserSettings.itemName = MyBelovedUMCDesertMod.ShowcaseItemName;

                TryResetItemCache(itemInstance);
            }

            TryResetItemCache(controller);
        }

        private static bool TryReinstantiateLiveShowcaseController(
            ShowcaseVehicleController controller,
            string source,
            string beforeSummary,
            bool fieldsNeededPatch)
        {
            var itemInstance = controller.ItemInstance;
            var parent = controller.transform.parent;
            if (itemInstance == null || parent == null)
            {
                MyBelovedUMCDesertFileLogger.Warn(
                    $"Live showroom target rebuild skipped source={source}: missing itemInstance={itemInstance == null}, missingParent={parent == null}, before={beforeSummary}.");
                return false;
            }

            var position = controller.transform.position;
            var rotation = controller.transform.rotation;
            var localScale = controller.transform.localScale;
            var customValue = controller.customValue;
            var oldName = controller.name;

            itemInstance.itemName = MyBelovedUMCDesertMod.ShowcaseItemName;
            if (itemInstance.playerItemPurchaserSettings != null)
                itemInstance.playerItemPurchaserSettings.itemName = MyBelovedUMCDesertMod.ShowcaseItemName;
            TryResetItemCache(itemInstance);

            var replacement = PrefabHelper.CreatePrefabItem(MyBelovedUMCDesertMod.ShowcaseItemName, parent);
            if (!replacement)
            {
                MyBelovedUMCDesertFileLogger.Warn(
                    $"Live showroom target rebuild failed source={source}: PrefabHelper.CreatePrefabItem returned null for {MyBelovedUMCDesertMod.ShowcaseItemName}, before={beforeSummary}.");
                return false;
            }

            replacement.name = "UMCDesertShowcase(Clone)";
            replacement.ItemInstance = itemInstance;
            if (itemInstance.playerItemPurchaserSettings != null)
                replacement.playerItemPurchaserSettings = itemInstance.playerItemPurchaserSettings;
            if (!string.IsNullOrEmpty(customValue))
                replacement.customValue = customValue;

            replacement.transform.position = position;
            replacement.transform.rotation = rotation;
            replacement.transform.localScale = localScale;
            replacement.TogglePhysics(true);

            var replacementShowcase = replacement.GetComponent<ShowcaseVehicleController>();
            if (replacementShowcase != null)
                PatchLiveControllerFields(replacementShowcase);

            controller.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(controller.gameObject);
            MyBelovedUMCDesertFileLogger.Info(
                $"Live showroom target rebuilt source={source}: destroyed={oldName}, replacement={replacement.name}, fieldsNeededPatch={fieldsNeededPatch}, before={beforeSummary}, after={DescribeItemController(replacement)}, position={FormatVector(position)}.");
            return true;
        }

        private static string TryRefreshLiveShowcaseController(ShowcaseVehicleController controller)
        {
            var type = controller.GetType();
            var candidates = new[]
            {
                "RefreshVehicle",
                "UpdateVehicle",
                "SetupVehicle",
                "SpawnVehicle",
                "CreateVehicle",
                "Refresh",
                "Show",
                "Init"
            };

            foreach (var candidate in candidates)
            {
                var method = type.GetMethod(candidate, MemberFlags, null, Type.EmptyTypes, null);
                if (method == null)
                    continue;

                try
                {
                    method.Invoke(controller, null);
                    return "invoked:" + candidate;
                }
                catch (Exception exception)
                {
                    return "failed:" + candidate + ":" + exception.GetType().Name;
                }
            }

            return "no-parameterless-refresh-method-found";
        }

        private static void TryResetItemCache(object owner)
        {
            if (owner == null)
                return;

            try
            {
                var field = owner.GetType().GetField("_itemCached", MemberFlags);
                if (field != null)
                    field.SetValue(owner, null);

                var itemField = owner.GetType().GetField("_item", MemberFlags);
                if (itemField != null)
                    itemField.SetValue(owner, null);

                var vehicleTypeField = owner.GetType().GetField("_vehicleType", MemberFlags);
                if (vehicleTypeField != null)
                    vehicleTypeField.SetValue(owner, null);
            }
            catch
            {
                // Best-effort cache reset only.
            }
        }

        private static string DescribeLiveShowcase(ShowcaseVehicleController controller)
        {
            if (controller == null)
                return "<null controller>";

            var itemInstance = controller.ItemInstance;
            return
                $"name={controller.name}, activeSelf={controller.gameObject.activeSelf}, activeInHierarchy={controller.gameObject.activeInHierarchy}, " +
                $"itemName={controller.itemName}, vehicleName={controller.vehicleName}, purchaserItem={controller.playerItemPurchaserSettings?.itemName}, " +
                $"itemInstanceItem={itemInstance?.itemName}, itemInstancePurchaser={itemInstance?.playerItemPurchaserSettings?.itemName}, " +
                $"position={FormatVector(controller.transform.position)}, renderers={DescribeRenderers(controller.gameObject)}";
        }

        private static string DescribeItemController(ItemController controller)
        {
            if (controller == null)
                return "<null item controller>";

            return
                $"name={controller.name}, activeSelf={controller.gameObject.activeSelf}, activeInHierarchy={controller.gameObject.activeInHierarchy}, " +
                $"itemName={controller.itemName}, purchaserItem={controller.playerItemPurchaserSettings?.itemName}, " +
                $"itemInstanceItem={controller.ItemInstance?.itemName}, itemInstancePurchaser={controller.ItemInstance?.playerItemPurchaserSettings?.itemName}, " +
                $"renderers={DescribeRenderers(controller.gameObject)}";
        }

        private static string DescribeRenderers(GameObject root)
        {
            if (root == null)
                return "<no root>";

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var total = renderers.Length;
            var active = 0;
            var enabled = 0;
            var visible = 0;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;
                if (renderer.gameObject.activeInHierarchy)
                    active++;
                if (renderer.enabled)
                    enabled++;
                if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                    visible++;
            }

            return $"total={total}/active={active}/enabled={enabled}/visible={visible}";
        }

        private static object? GetMemberValue(object? owner, string memberName)
        {
            if (owner == null)
                return null;

            var type = owner.GetType();
            var field = type.GetField(memberName, MemberFlags);
            if (field != null)
                return field.GetValue(owner);

            var property = type.GetProperty(memberName, MemberFlags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(owner, null);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static string? GetStringMember(object? owner, string memberName)
        {
            return GetMemberValue(owner, memberName) as string;
        }

        private static bool TrySetStringMember(object owner, string memberName, string value)
        {
            if (owner == null)
                return false;

            var type = owner.GetType();
            var field = type.GetField(memberName, MemberFlags);
            if (field != null && field.FieldType == typeof(string))
            {
                field.SetValue(owner, value);
                return true;
            }

            var property = type.GetProperty(memberName, MemberFlags);
            if (property != null &&
                property.PropertyType == typeof(string) &&
                property.CanWrite &&
                property.GetIndexParameters().Length == 0)
            {
                property.SetValue(owner, value, null);
                return true;
            }

            return false;
        }

        private static bool TryGetVector3Member(object owner, string memberName, out Vector3 value)
        {
            var memberValue = GetMemberValue(owner, memberName);
            if (memberValue is Vector3 vector)
            {
                value = vector;
                return true;
            }

            value = default;
            return false;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }

        private static BuildingRegistration? FindGeneralUSTrucksRegistration()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.BuildingRegistrations == null)
                return null;

            foreach (var registration in saveGame.BuildingRegistrations)
            {
                if (registration == null)
                    continue;

                if (string.Equals(
                        registration.BusinessName,
                        MyBelovedUMCDesertMod.GeneralUSTrucksContactId,
                        StringComparison.Ordinal))
                {
                    MyBelovedUMCDesertFileLogger.Info(
                        $"Found General US Trucks registration address={registration.Address}, layout={registration.Layout}, businessType={registration.businessTypeName}.");
                    return registration;
                }
            }

            MyBelovedUMCDesertFileLogger.Warn("General US Trucks registration was not found in SaveGameManager.Current.BuildingRegistrations.");
            return null;
        }

        private static bool IsAtTargetPosition(Vector3 position)
        {
            return Vector3.SqrMagnitude(position - TargetPosition) <= PositionTolerance * PositionTolerance;
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";
        }

        internal static void LogLiveShowroomState(string source)
        {
            if (!MyBelovedUMCDesertFileLogger.Enabled)
                return;

            // Log every requested live showroom snapshot while debug logging is enabled.
            // The previous one-shot guard hid later entry/reload states, which made intermittent
            // showroom timing issues hard to diagnose.

            try
            {
                var controllers = UnityEngine.Object.FindObjectsOfType<ShowcaseVehicleController>(true);
                MyBelovedUMCDesertFileLogger.Info($"Live showroom diagnostics source={source}, showcaseControllerCount={controllers.Length}.");

                foreach (var controller in controllers)
                {
                    if (controller == null)
                        continue;

                    var distance = Vector3.Distance(controller.transform.position, TargetPosition);
                    if (distance > 5f &&
                        !string.Equals(controller.vehicleName, MyBelovedUMCDesertMod.VehicleTypeName, StringComparison.Ordinal) &&
                        !string.Equals(controller.vehicleName, "ba:vehicletype_deliverytruck", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    MyBelovedUMCDesertFileLogger.Info(
                        $"Live showcase source={source}, distanceToTarget={distance:0.###}, {DescribeLiveShowcase(controller)}.");
                }
            }
            catch (Exception exception)
            {
                MyBelovedUMCDesertFileLogger.Warn($"Live showroom diagnostics failed source={source}: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    internal static class UMCDesertStatsService
    {
        private static bool hasLoggedStats;

        internal static void ApplyRestoredStats(ModContext? context)
        {
            var vehicleType = VehicleTypeHelper.GetVehicleType(MyBelovedUMCDesertMod.VehicleTypeName);
            if (vehicleType == null)
                return;

            var changed = false;
            if (vehicleType.maxSpeed < MyBelovedUMCDesertMod.RestoredMaxSpeed)
            {
                vehicleType.maxSpeed = MyBelovedUMCDesertMod.RestoredMaxSpeed;
                changed = true;
            }

            if (vehicleType.enginePower < MyBelovedUMCDesertMod.RestoredEnginePower)
            {
                vehicleType.enginePower = MyBelovedUMCDesertMod.RestoredEnginePower;
                changed = true;
            }

            if (vehicleType.brakeForce < MyBelovedUMCDesertMod.RestoredBrakeForce)
            {
                vehicleType.brakeForce = MyBelovedUMCDesertMod.RestoredBrakeForce;
                changed = true;
            }

            if (Math.Abs(vehicleType.price - MyBelovedUMCDesertMod.RestoredPrice) > 0.01f)
            {
                vehicleType.price = MyBelovedUMCDesertMod.RestoredPrice;
                changed = true;
            }

            if (vehicleType.maxCargoCapacity != MyBelovedUMCDesertMod.RestoredCargoCapacity)
            {
                vehicleType.maxCargoCapacity = MyBelovedUMCDesertMod.RestoredCargoCapacity;
                changed = true;
            }

            if ((changed || !hasLoggedStats) && context != null)
            {
                hasLoggedStats = true;
                context.Logger.Info(
                    "My Beloved UMC Desert: restored vehicle stats " +
                    $"maxSpeed={vehicleType.maxSpeed}, " +
                    $"enginePower={vehicleType.enginePower}, " +
                    $"brakeForce={vehicleType.brakeForce}, " +
                    $"price={vehicleType.price}, " +
                    $"maxCargoCapacity={vehicleType.maxCargoCapacity}.");
            }
        }
    }

    internal sealed class MyBelovedUMCDesertContactRegistrar : MonoBehaviour
    {
        private const float WatchIntervalSeconds = 0.5f;
        private const float WatchDurationSeconds = 300f;

        private ModContext? context;
        private bool initialized;
        private bool watchForDealerEntry;
        private bool watcherCapturedDealer;
        private float watchUntil;
        private float nextWatchAt;
        private float nextRuntimeGuardAt;
        private float nextEventRefreshAt;
        private object? lastObservedSaveGame;
        private bool wasInSaveGame;
        private bool runtimeGuardCapturedDealerThisVisit;
        private bool wasInsideGeneralUSTrucks;

        public void Initialize(ModContext context)
        {
            this.context = context;
            if (initialized)
                return;

            initialized = true;
            MyBelovedUMCDesertMod.RegisterDealerPhoneStock(context);
            RegisterGlobalEvents();
            GlobalEvents.RegisterOnGameLoadedCallback(OnGameLoaded);
            PrimeRuntimeForCurrentSave("initialize");
        }

        private void OnDestroy()
        {
            UnregisterGlobalEvents();
        }

        private void RegisterGlobalEvents()
        {
            GlobalEvents.onEnterBuilding = (Action<Address>)Delegate.Remove(
                GlobalEvents.onEnterBuilding,
                new Action<Address>(OnEnterBuilding));
            GlobalEvents.onEnterBuilding = (Action<Address>)Delegate.Combine(
                GlobalEvents.onEnterBuilding,
                new Action<Address>(OnEnterBuilding));

            GlobalEvents.onEnterBuildingDelayed = (Action<Address>)Delegate.Remove(
                GlobalEvents.onEnterBuildingDelayed,
                new Action<Address>(OnEnterBuildingDelayed));
            GlobalEvents.onEnterBuildingDelayed = (Action<Address>)Delegate.Combine(
                GlobalEvents.onEnterBuildingDelayed,
                new Action<Address>(OnEnterBuildingDelayed));
        }

        private void UnregisterGlobalEvents()
        {
            GlobalEvents.onEnterBuilding = (Action<Address>)Delegate.Remove(
                GlobalEvents.onEnterBuilding,
                new Action<Address>(OnEnterBuilding));
            GlobalEvents.onEnterBuildingDelayed = (Action<Address>)Delegate.Remove(
                GlobalEvents.onEnterBuildingDelayed,
                new Action<Address>(OnEnterBuildingDelayed));
        }

        private void OnGameLoaded()
        {
            MyBelovedUMCDesertFileLogger.Info("OnGameLoaded: refreshing dealer stock and attempting showroom layout patch.");
            PrimeRuntimeForCurrentSave("game-loaded");
        }

        private void Update()
        {
            DetectSaveGameContextChange();

            if (Time.unscaledTime >= nextEventRefreshAt)
            {
                // Re-registering every couple of seconds was useful while debugging save reloads,
                // but it is unnecessary work while the player is already inside a dealer.
                nextEventRefreshAt = Time.unscaledTime + 30f;
                RegisterGlobalEvents();
            }

            var insideGeneralUSTrucks = IsCurrentlyInsideGeneralUSTrucks();
            if (!insideGeneralUSTrucks && wasInsideGeneralUSTrucks)
            {
                runtimeGuardCapturedDealerThisVisit = false;
            }

            wasInsideGeneralUSTrucks = insideGeneralUSTrucks;

            // The runtime guard is only a safety net for missed enter-building callbacks after
            // returning to the main menu / loading another save. Once it has captured this
            // dealer visit successfully, do not keep doing expensive scene scans every second.
            if (insideGeneralUSTrucks &&
                !runtimeGuardCapturedDealerThisVisit &&
                Time.unscaledTime >= nextRuntimeGuardAt)
            {
                nextRuntimeGuardAt = Time.unscaledTime + 1f;
                if (TryCaptureDealerFromBuildingManager("runtime-guard", false))
                {
                    runtimeGuardCapturedDealerThisVisit = true;
                    nextRuntimeGuardAt = float.PositiveInfinity;
                }
            }

            if (!watchForDealerEntry || watcherCapturedDealer)
                return;

            if (Time.unscaledTime > watchUntil)
            {
                watchForDealerEntry = false;
                MyBelovedUMCDesertFileLogger.Warn("Dealer entry watcher expired without seeing General US Trucks.");
                return;
            }

            if (Time.unscaledTime < nextWatchAt)
                return;

            nextWatchAt = Time.unscaledTime + WatchIntervalSeconds;
            TryCaptureDealerFromBuildingManager("dealer-entry-watcher", true);
        }

        private void OnEnterBuilding(Address address)
        {
            if (!IsGeneralUSTrucks(address))
            {
                MyBelovedUMCDesertFileLogger.Info($"OnEnterBuilding ignored address={address}.");
                return;
            }

            MyBelovedUMCDesertFileLogger.Info($"OnEnterBuilding General US Trucks address={address}: refreshing dealer stock and attempting showroom layout patch.");
            MyBelovedUMCDesertMod.ApplyShowroomReplacement();
            MyBelovedUMCDesertMod.RegisterDealerPhoneStock(context);

            // Give the normal delayed enter-building callback a chance to do the live repair.
            // The runtime guard should only step in if that callback is missed after a reload.
            nextRuntimeGuardAt = Time.unscaledTime + 3f;
        }

        private void OnEnterBuildingDelayed(Address address)
        {
            if (!IsGeneralUSTrucks(address))
                return;

            MyBelovedUMCDesertFileLogger.Info($"OnEnterBuildingDelayed General US Trucks address={address}: repairing and logging live showroom state.");
            GeneralUSTrucksShowroomReplacementService.TryPatchGeneralUSTrucksRegistrationPayload("enter-building-delayed");
            GeneralUSTrucksShowroomReplacementService.RepairLiveShowroomState("enter-building-delayed");
            GeneralUSTrucksShowroomReplacementService.LogLiveShowroomState("enter-building-delayed");
            watcherCapturedDealer = true;
            watchForDealerEntry = false;
            runtimeGuardCapturedDealerThisVisit = true;
            nextRuntimeGuardAt = float.PositiveInfinity;
        }

        private void DetectSaveGameContextChange()
        {
            var currentSave = (object?)SaveGameManager.Current;
            if (currentSave == null)
            {
                if (wasInSaveGame)
                {
                    wasInSaveGame = false;
                    lastObservedSaveGame = null;
                    watchForDealerEntry = false;
                    watcherCapturedDealer = false;
                    runtimeGuardCapturedDealerThisVisit = false;
                    wasInsideGeneralUSTrucks = false;
                    MyBelovedUMCDesertFileLogger.Info("SaveGameManager.Current is null; likely returned to main menu. Waiting for next save load.");
                }

                return;
            }

            if (!wasInSaveGame || !ReferenceEquals(currentSave, lastObservedSaveGame))
            {
                wasInSaveGame = true;
                lastObservedSaveGame = currentSave;
                PrimeRuntimeForCurrentSave("save-context-changed");
            }
        }

        private void PrimeRuntimeForCurrentSave(string source)
        {
            var currentSave = (object?)SaveGameManager.Current;
            if (currentSave != null)
            {
                wasInSaveGame = true;
                lastObservedSaveGame = currentSave;
            }

            MyBelovedUMCDesertFileLogger.Info($"Priming runtime for current save source={source}.");
            GeneralUSTrucksStockService.ResetForNewSaveContext(source);
            GeneralUSTrucksShowroomReplacementService.ResetTransientRuntimeState(source);
            RegisterGlobalEvents();
            StartDealerEntryWatcher(source);
            MyBelovedUMCDesertMod.ApplyShowroomReplacement();
            MyBelovedUMCDesertMod.RegisterDealerPhoneStock(context);
            runtimeGuardCapturedDealerThisVisit = false;
            wasInsideGeneralUSTrucks = false;
            nextRuntimeGuardAt = 0f;
            nextEventRefreshAt = 0f;
        }

        private void StartDealerEntryWatcher(string source)
        {
            watchForDealerEntry = true;
            watcherCapturedDealer = false;
            watchUntil = Time.unscaledTime + WatchDurationSeconds;
            nextWatchAt = 0f;
            MyBelovedUMCDesertFileLogger.Info(
                $"Dealer entry watcher started source={source}, durationSeconds={WatchDurationSeconds}, intervalSeconds={WatchIntervalSeconds}.");
        }

        private bool TryCaptureDealerFromBuildingManager(string source, bool logNonTarget)
        {
            if (!BuildingManager.IsInsideBuilding)
                return false;

            var buildingManager = InstanceBehavior<BuildingManager>.Instance;
            var registration = buildingManager?.buildingRegistration;
            if (registration == null ||
                !string.Equals(
                    registration.BusinessName,
                    MyBelovedUMCDesertMod.GeneralUSTrucksContactId,
                    StringComparison.Ordinal))
            {
                if (logNonTarget)
                {
                    MyBelovedUMCDesertFileLogger.Info(
                        $"Dealer entry watcher saw inside building but not General US Trucks businessName={registration?.BusinessName}, layout={registration?.Layout}, businessType={registration?.businessTypeName}.");
                }

                return false;
            }

            watcherCapturedDealer = true;
            watchForDealerEntry = false;
            MyBelovedUMCDesertFileLogger.Info(
                $"Dealer runtime capture source={source} General US Trucks layout={registration.Layout}, businessType={registration.businessTypeName}; refreshing patch and logging live showroom.");
            MyBelovedUMCDesertMod.ApplyShowroomReplacement();
            GeneralUSTrucksShowroomReplacementService.TryPatchGeneralUSTrucksRegistrationPayload(source);
            GeneralUSTrucksShowroomReplacementService.RepairLiveShowroomState(source);
            GeneralUSTrucksShowroomReplacementService.LogLiveShowroomState(source);
            return true;
        }

        private bool IsCurrentlyInsideGeneralUSTrucks()
        {
            if (!BuildingManager.IsInsideBuilding)
                return false;

            var buildingManager = InstanceBehavior<BuildingManager>.Instance;
            var registration = buildingManager?.buildingRegistration;
            return string.Equals(
                registration?.BusinessName,
                MyBelovedUMCDesertMod.GeneralUSTrucksContactId,
                StringComparison.Ordinal);
        }

        private static bool IsGeneralUSTrucks(Address address)
        {
            if (address == null)
                return false;

            var registration = BuildingHelper.GetBuildingRegistration(address);
            return string.Equals(
                registration?.BusinessName,
                MyBelovedUMCDesertMod.GeneralUSTrucksContactId,
                StringComparison.Ordinal);
        }
    }
}
