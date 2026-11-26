#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Items;
using Blueprints;
using BusinessLayoutSets;
using Helpers;
using Services;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: RegisterModClass(typeof(LamborghiniAventadorIntegration))]

/// <summary>
/// Owns the event-driven integration work that can be safely established once
/// the game's dealer layouts and saved game state are available.
/// </summary>
[ModEntryOnInitializationLoad]
internal sealed class LamborghiniAventadorIntegration : IModBigAmbitions
{
    private ModContext? context;
    private bool dealerReady;
    private bool dealerFailureLogged;

    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext modContext)
    {
        context = modContext;
        Subscribe();
        GlobalEvents.RegisterOnGameLoadedLateCallback(HandleGameLoadedLate);
        TryEnsureDealerStock("mod-load");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        Unsubscribe();
        LamborghiniAventadorDealerStock.RemoveVehicle(LamborghiniAventadorMod.VehicleTypeName);
        context = null;
        dealerReady = false;
        dealerFailureLogged = false;
        return Task.CompletedTask;
    }

    private void Subscribe()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onEnterBuilding += HandleBuildingEntered;
        GlobalEvents.onBuildingRegistrationChange -= HandleBuildingRegistrationChanged;
        GlobalEvents.onBuildingRegistrationChange += HandleBuildingRegistrationChanged;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        GlobalEvents.onGameUnloaded += HandleGameUnloaded;
    }

    private void Unsubscribe()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onBuildingRegistrationChange -= HandleBuildingRegistrationChanged;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Subscribe();
        TryEnsureDealerStock("scene-loaded:" + scene.name);
    }

    private void HandleGameLoadedLate()
    {
        Subscribe();
        TryEnsureDealerStock("game-loaded-late");
    }

    private void HandleGameUnloaded()
    {
        dealerReady = false;
        dealerFailureLogged = false;
    }

    private void HandleBuildingEntered(Address address) =>
        TryEnsureDealerStockForAddress(address, "dealer-entered");

    private void HandleBuildingRegistrationChanged(Address address) =>
        TryEnsureDealerStockForAddress(address, "dealer-registration-changed");

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (isOpen && !dealerReady)
            TryEnsureDealerStock("full-menu");
    }

    private void TryEnsureDealerStockForAddress(Address address, string source)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (LamborghiniAventadorDealerStock.IsTargetDealer(registration?.BusinessName))
            TryEnsureDealerStock(source);
    }

    private void TryEnsureDealerStock(string source)
    {
        if (dealerReady || BusinessLayoutSetHelper.loadingLayouts)
            return;

        try
        {
            dealerReady = LamborghiniAventadorDealerStock.EnsureVehicleAvailable(
                LamborghiniAventadorMod.VehicleTypeName, context);
            if (dealerReady)
            {
                dealerFailureLogged = false;
                LamborghiniAventadorDiagnostics.Info(context,
                    $"LamborghiniAventador: luxury dealer stock ready source='{source}'.");
            }
            else if (!dealerFailureLogged)
            {
                dealerFailureLogged = true;
                context?.Logger.Warn(
                    $"LamborghiniAventador: luxury dealer stock was not ready source='{source}'; " +
                    "a later game, scene, menu, or dealer event will retry.");
            }
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"LamborghiniAventador: luxury dealer stock failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }
}

internal static class LamborghiniAventadorDealerStock
{
    private const string TargetBusinessTypeName = "ba:businesstype_cardealership";
    private const string TargetBuildingSize = "ba:buildingsize_m";
    private const int TargetBuildingVersion = 1;
    private const string TargetLayoutName = "MurrayHillCarDealershipLuxury";
    private static readonly string[] DealerContactIds =
    {
        "The Hamptons Axis",
        "Manhattan Luxury Cars"
    };

    internal static bool IsTargetDealer(string? contactId)
    {
        if (string.IsNullOrEmpty(contactId))
            return false;
        foreach (var id in DealerContactIds)
            if (string.Equals(id, contactId, StringComparison.Ordinal))
                return true;
        return false;
    }

    internal static bool EnsureVehicleAvailable(string vehicleName, ModContext? context)
    {
        if (string.IsNullOrWhiteSpace(vehicleName))
            return false;
        if (AllDealersContainVehicle(vehicleName))
            return true;
        if (BusinessLayoutSetHelper.loadingLayouts)
            return false;

        var vanillaStock = GetLuxuryDealerLayoutVehicles();
        if (vanillaStock.Count == 0)
            return false;

        var allReady = true;
        foreach (var contactId in DealerContactIds)
            allReady &= EnsureDealerStock(contactId, vanillaStock, vehicleName, context);
        return allReady;
    }

    internal static void RemoveVehicle(string vehicleName)
    {
        if (string.IsNullOrWhiteSpace(vehicleName))
            return;
        foreach (var contactId in DealerContactIds)
        {
            if (!ContractItemsForSaleService.TryGetVehiclesForContact(contactId, out List<string> existing) ||
                existing == null)
                continue;
            var remaining = new List<string>();
            foreach (var existingVehicle in existing)
                if (!string.Equals(existingVehicle, vehicleName, StringComparison.Ordinal))
                    AddUnique(remaining, existingVehicle);
            if (remaining.Count == existing.Count)
                continue;
            if (remaining.Count == 0)
                ContractItemsForSaleService.RemoveContact(contactId);
            else
                ContractItemsForSaleService.SetVehiclesForContact(contactId, remaining);
        }
    }

    private static bool EnsureDealerStock(
        string contactId, List<string> vanillaStock, string vehicleName, ModContext? context)
    {
        try
        {
            var merged = new List<string>();
            var hasExisting = ContractItemsForSaleService.TryGetVehiclesForContact(contactId, out List<string> existing);
            if (hasExisting && existing != null)
                AddUniqueRange(merged, existing);
            AddUniqueRange(merged, vanillaStock);
            AddUnique(merged, vehicleName);
            if (hasExisting && existing != null && SameList(existing, merged))
                return true;
            ContractItemsForSaleService.SetVehiclesForContact(contactId, merged);
            return true;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"LamborghiniAventador: could not update dealer '{contactId}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static List<string> GetLuxuryDealerLayoutVehicles()
    {
        var result = new List<string>();
        try
        {
            var layout = BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
                TargetBusinessTypeName,
                new BuildingSizeInfo(TargetBuildingSize, TargetBuildingVersion),
                TargetLayoutName.ToLowerInvariant(),
                false);
            if (layout?.Items == null)
                return result;
            foreach (var item in layout.Items)
            {
                var purchaser = item?.playerItemPurchaserSettings;
                if (purchaser == null || !purchaser.enabled || string.IsNullOrEmpty(purchaser.itemName))
                    continue;
                var vehicleType = ItemsGetter.GetByName(purchaser.itemName)?.vehicleType;
                if (vehicleType != null && vehicleType.Length > 0)
                    AddUnique(result, vehicleType);
            }
        }
        catch
        {
            // The game can unload layout data during save transitions. Later game
            // load, scene, menu, and dealer events provide the next safe attempt.
        }
        return result;
    }

    private static bool AllDealersContainVehicle(string vehicleName)
    {
        foreach (var contactId in DealerContactIds)
            if (!ContractItemsForSaleService.TryGetVehiclesForContact(contactId, out List<string> vehicles) ||
                vehicles == null || !Contains(vehicles, vehicleName))
                return false;
        return true;
    }

    private static bool Contains(IEnumerable<string> values, string value)
    {
        foreach (var item in values)
            if (string.Equals(item, value, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static void AddUniqueRange(List<string> target, IEnumerable<string> source)
    {
        foreach (var item in source)
            AddUnique(target, item);
    }

    private static void AddUnique(List<string> target, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        foreach (var current in target)
            if (string.Equals(current, value, StringComparison.Ordinal))
                return;
        target.Add(value);
    }

    private static bool SameList(List<string> left, List<string> right)
    {
        if (left.Count != right.Count)
            return false;
        for (var i = 0; i < left.Count; i++)
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                return false;
        return true;
    }
}
