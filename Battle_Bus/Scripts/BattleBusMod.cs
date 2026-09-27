#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using Blueprints;
using BusinessLayoutSets;
using Helpers;
using Services;
using UI.PurchaseVehicle;
using UnityEngine;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(BattleBusMod))]
[assembly: RegisterModClass(typeof(BattleBusMainMenuRegistration))]
[assembly: RegisterModClass(typeof(BattleBusCityRegistration))]

internal static class BattleBusDiagnostics
{
    internal static bool DebugEnabled { get; set; } = false;
    internal static bool FlightDebugEnabled { get; set; } = false;
    internal static bool EntryDebugEnabled { get; set; } = false;
    internal static bool PaintDebugEnabled { get; set; } = false;
    internal static bool RecoveryDebugEnabled { get; set; } = false;
    internal static bool AudioDebugEnabled { get; set; } = false;
    internal static bool VisualDebugEnabled { get; set; } = false;
    internal static bool DamageDebugEnabled { get; set; } = false;

    internal static void Info(ModContext? context, string message)
    {
        if (DebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void FlightInfo(ModContext? context, string message)
    {
        if (DebugEnabled && FlightDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void EntryInfo(ModContext? context, string message)
    {
        if (DebugEnabled && EntryDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void PaintInfo(ModContext? context, string message)
    {
        if (DebugEnabled && PaintDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void DamageInfo(ModContext? context, string message)
    {
        if (DebugEnabled && DamageDebugEnabled)
            context?.Logger.Info(message);
    }
}

[ModEntryOnInitializationLoad]
public sealed class BattleBusMod : IModBigAmbitions
{
    internal const string VehicleTypeName = "battle-bus-vehicle:vehicletype_battlebus";
    private const string BundleKey = "AssetBundles/battlebus.unity3d";
    private const string VehicleAssetPath = "Assets/Mods/Battle_Bus/BattleBus.asset";
    private const string VehiclePrefabPath = "Assets/Mods/Battle_Bus/BattleBus.prefab";

    private VehicleType? vehicleType;
    private BattleBusRuntime? runtime;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"Battle Bus: failed to load asset bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        if (vehicleType == null)
        {
            context.Logger.Warn($"Battle Bus: failed to load vehicle type '{VehicleAssetPath}'.");
            return Task.CompletedTask;
        }

        var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
        if (prefab == null)
        {
            context.Logger.Warn($"Battle Bus: failed to load vehicle prefab '{VehiclePrefabPath}'.");
            return Task.CompletedTask;
        }

        BattleBusVehicleRegistration.EnsureVehicleTypeRegistered(
            vehicleType, context, "initialization-load");
        BattleBusVehicleRegistration.EnsurePlayerPrefabRegistered(
            prefab, context, "initialization-load");
        runtime = BattleBusRuntime.Initialize(context, vehicleType.vehicleTypeName);
        BattleBusDiagnostics.Info(context,
            $"Battle Bus: registered '{vehicleType.vehicleTypeName}' " +
            $"price={vehicleType.price:0}, maxSpeed={vehicleType.maxSpeed}.");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        runtime?.Shutdown();
        runtime = null;

        if (vehicleType != null)
        {
            BattleBusTruckDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            vehicleType = null;
        }

        return Task.CompletedTask;
    }
}

[ModEntryMainMenu]
public sealed class BattleBusMainMenuRegistration : IModBigAmbitions
{
    private ModContext? context;
    private VehicleType? vehicleType;
    private GameObject? vehiclePrefab;

    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext modContext)
    {
        context = modContext;
        vehicleType = BattleBusVehicleRegistration.LoadVehicleType(modContext);
        vehiclePrefab = BattleBusVehicleRegistration.LoadVehiclePrefab(modContext);
        BattleBusVehicleRegistration.EnsureVehicleTypeRegistered(
            vehicleType, modContext, "main-menu-load");
        BattleBusVehicleRegistration.EnsurePlayerPrefabRegistered(
            vehiclePrefab, modContext, "main-menu-load");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        // The game's scene transition rebuilds part of the player-vehicle cache.
        // Rebind before the city scene starts so saved and newly spawned buses
        // resolve this prefab from the first vehicle lookup.
        if (context != null)
        {
            vehicleType ??= BattleBusVehicleRegistration.LoadVehicleType(context);
            BattleBusVehicleRegistration.EnsureVehicleTypeRegistered(
                vehicleType, context, "main-menu-unload");
            vehiclePrefab ??= BattleBusVehicleRegistration.LoadVehiclePrefab(context);
            BattleBusVehicleRegistration.EnsurePlayerPrefabRegistered(
                vehiclePrefab, context, "main-menu-unload");
        }

        context = null;
        vehicleType = null;
        vehiclePrefab = null;
        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public sealed class BattleBusCityRegistration : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext context)
    {
        BattleBusVehicleRegistration.EnsureVehicleTypeRegistered(
            BattleBusVehicleRegistration.LoadVehicleType(context), context, "city-load");
        BattleBusVehicleRegistration.EnsurePlayerPrefabRegistered(
            BattleBusVehicleRegistration.LoadVehiclePrefab(context), context, "city-load");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync() => Task.CompletedTask;
}

internal static class BattleBusVehicleRegistration
{
    private const string PlayerPrefabCacheKey =
        "Prefabs/Vehicles/PlayerVehicles/battlebus.prefab";

    internal static VehicleType? LoadVehicleType(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, "AssetBundles/battlebus.unity3d");
        if (bundle == null)
        {
            context.Logger.Warn(
                "Battle Bus: vehicle bundle was unavailable during lifecycle registration.");
            return null;
        }

        var type = bundle.LoadAsset<VehicleType>("Assets/Mods/Battle_Bus/BattleBus.asset");
        if (type == null)
            context.Logger.Warn("Battle Bus: VehicleType asset was unavailable during lifecycle registration.");
        return type;
    }

    internal static GameObject? LoadVehiclePrefab(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, "AssetBundles/battlebus.unity3d");
        if (bundle == null)
        {
            context.Logger.Warn(
                "Battle Bus: vehicle bundle was unavailable during player-prefab registration.");
            return null;
        }

        var prefab = bundle.LoadAsset<GameObject>("Assets/Mods/Battle_Bus/BattleBus.prefab");
        if (prefab == null)
            context.Logger.Warn("Battle Bus: player vehicle prefab was unavailable during lifecycle registration.");
        return prefab;
    }

    internal static bool EnsureVehicleTypeRegistered(
        VehicleType? vehicleType, ModContext context, string source)
    {
        if (vehicleType == null)
            return false;

        var isRegistered = VehicleTypeHelper.GetVehicleType(vehicleType.vehicleTypeName) != null &&
                           VehicleTypeHelper.IsModVehicleType(vehicleType.vehicleTypeName);
        if (!isRegistered)
            ModdingAPI.RegisterModVehicleType(vehicleType);

        isRegistered = VehicleTypeHelper.GetVehicleType(vehicleType.vehicleTypeName) != null &&
                       VehicleTypeHelper.IsModVehicleType(vehicleType.vehicleTypeName);
        if (!isRegistered)
        {
            context.Logger.Warn(
                $"Battle Bus: vehicle type registration failed source='{source}'.");
            return false;
        }

        return true;
    }

    internal static bool EnsurePlayerPrefabRegistered(
        GameObject? vehiclePrefab, ModContext context, string source)
    {
        if (vehiclePrefab == null)
            return false;

        try
        {
            var cacheField = typeof(PrefabHelper).GetField(
                "PrefabCache", BindingFlags.Static | BindingFlags.NonPublic);
            var cache = cacheField?.GetValue(null) as IDictionary;
            if (cache == null)
            {
                context.Logger.Warn(
                    $"Battle Bus: player prefab cache unavailable source='{source}'.");
                return false;
            }

            if (!cache.Contains(PlayerPrefabCacheKey) ||
                !ReferenceEquals(cache[PlayerPrefabCacheKey], vehiclePrefab))
            {
                cache[PlayerPrefabCacheKey] = vehiclePrefab;
                context.Logger.Info(
                    $"Battle Bus: player prefab bound key='{PlayerPrefabCacheKey}' " +
                    $"prefab='{vehiclePrefab.name}' source='{source}'.");
            }

            return ReferenceEquals(cache[PlayerPrefabCacheKey], vehiclePrefab);
        }
        catch (Exception exception)
        {
            context.Logger.Warn(
                $"Battle Bus: player prefab binding failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }
}

internal sealed class BattleBusRuntime : MonoBehaviour
{
    private const string VehicleRepainterColorRestoredEvent = "vehicle-repainter:color-restored";
    private const string VehicleRepainterColorPreviewEvent = "vehicle-repainter:color-preview";
    private const string VehicleRepainterColorResetEvent = "vehicle-repainter:color-reset";

    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private bool subscribed;
    private VehicleController? previewVehicle;
    private BattleBusPaintController? previewPaintController;

    internal static BattleBusRuntime Initialize(ModContext context, string vehicleTypeName)
    {
        var host = new GameObject("BattleBusRuntime");
        DontDestroyOnLoad(host);
        var runtime = host.AddComponent<BattleBusRuntime>();
        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName;
        runtime.Subscribe();
        GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
        runtime.TryEnsureDealerStock("mod-load");
        return runtime;
    }

    internal void Shutdown()
    {
        Unsubscribe();
        BattleBusTruckDealerStock.RemoveVehicle(vehicleTypeName);
        Destroy(gameObject);
    }

    private void Subscribe()
    {
        if (subscribed)
            return;
        GlobalEvents.onEnterVehicle += HandleVehicleEntered;
        GlobalEvents.onExitVehicle += HandleVehicleExited;
        GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
        GlobalEvents.onVehicleVariablesChanged += HandleVehicleVariablesChanged;
        GameEvent.onGameEventTriggered += HandleGameEvent;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GameEvent.onGameEventTriggered -= HandleGameEvent;
        subscribed = false;
    }

    private void HandleGameLoadedLate() => TryEnsureDealerStock("game-loaded-late");

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (isOpen)
            TryEnsureDealerStock("full-menu-opened");
    }

    private void Update() => RefreshPaintPreview();

    private void HandleVehicleVariablesChanged()
    {
        var vehicles = VehicleHelper.AllPlayerVehicles;
        if (vehicles == null)
            return;

        foreach (var vehicle in vehicles)
            if (IsBattleBus(vehicle))
                ApplyVehiclePaint(vehicle, "vehicle-variables-changed", true);
    }

    private void RefreshPaintPreview()
    {
        if (!PurchaseVehicleUI.IsPanelOpen)
        {
            RestorePreviewPaint();
            return;
        }

        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!ReferenceEquals(selectedVehicle, previewVehicle))
        {
            RestorePreviewPaint();
            previewVehicle = selectedVehicle;
            if (IsBattleBus(selectedVehicle))
            {
                ApplyVehiclePaint(selectedVehicle, "vehicle-repainter", true);
                previewPaintController = selectedVehicle!.GetComponent<BattleBusPaintController>();
            }
        }

        previewPaintController?.ApplyCurrentColor("vehicle-repainter", false);
    }

    private void RestorePreviewPaint()
    {
        previewPaintController?.ApplyCurrentColor("vehicle-repainter-ended", true);
        previewVehicle = null;
        previewPaintController = null;
    }

    private void TryEnsureDealerStock(string source)
    {
        if (BattleBusTruckDealerStock.EnsureVehicleAvailable(vehicleTypeName, context))
            BattleBusDiagnostics.Info(context,
                $"Battle Bus: General US Trucks stock ready ({source}).");
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        if (vehicle == null)
            return;
        var flight = vehicle.GetComponent<BattleBusFlightController>();
        if (!IsBattleBus(vehicle) && flight == null)
            return;

        ApplyVehiclePaint(vehicle, "vehicle-entered", true);
        if (flight == null)
        {
            context?.Logger.Warn(
                $"Battle Bus: flight component missing on vehicle instance {vehicle.GetInstanceID()}; adding runtime fallback.");
            flight = vehicle.gameObject.AddComponent<BattleBusFlightController>();
        }
        flight.Initialize(vehicle, context);
        vehicle.GetComponent<BattleBusWorkshopRecovery>()?.Initialize(context);
        vehicle.GetComponent<BattleBusAudioController>()?.Initialize(context);
        vehicle.GetComponent<BattleBusVisualDamageController>()?.Initialize(context);
        flight.SetPiloted(true);
        StartCoroutine(ValidateVehicleAfterEntry(vehicle));
    }

    private IEnumerator ValidateVehicleAfterEntry(VehicleController vehicle)
    {
        // The game raises onEnterVehicle before CarController.StartEngine makes
        // the Rigidbody dynamic, so inspect the completed entry on the next frame.
        yield return null;
        if (vehicle == null)
            yield break;

        var body = vehicle.GetComponent<Rigidbody>();
        var physicsController = vehicle.GetComponent("NWH.VehiclePhysics2.VehicleController") as Behaviour;
        var playerVehicleLayer = LayerMask.NameToLayer("PlayerVehicles");
        var switchedRendererCount = 0;
        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
            if (renderer != null && renderer.enabled && renderer.sharedMaterials.Length > 0 &&
                renderer.gameObject.layer == playerVehicleLayer)
                switchedRendererCount++;

        var kinematic = body == null || body.isKinematic;
        var physicsDisabled = physicsController == null || !physicsController.isActiveAndEnabled;
        var renderersNotSwitched = switchedRendererCount == 0;
        var interactionCollider = vehicle.vehicleCollider;
        var interactionBounds = interactionCollider != null ? interactionCollider.bounds : default;
        BattleBusDiagnostics.EntryInfo(context,
            $"Battle Bus entry result vehicle={vehicle.GetInstanceID()}: controlled={vehicle.controlledByPlayer}, " +
            $"kinematic={kinematic}, physicsEnabled={!physicsDisabled}, " +
            $"playerLayerRenderers={switchedRendererCount}, position={vehicle.transform.position}, " +
            $"interactionCollider={(interactionCollider != null ? interactionCollider.GetType().Name : "missing")}, " +
            $"trigger={(interactionCollider != null && interactionCollider.isTrigger)}, " +
            $"interactionBoundsCenter={interactionBounds.center}, size={interactionBounds.size}.");

        if (!vehicle.controlledByPlayer || kinematic || physicsDisabled || renderersNotSwitched)
            context?.Logger.Warn(
                $"Battle Bus entry did not initialize correctly for vehicle={vehicle.GetInstanceID()}: " +
                $"controlled={vehicle.controlledByPlayer}, kinematic={kinematic}, " +
                $"physicsEnabled={!physicsDisabled}, playerLayerRenderers={switchedRendererCount}.");
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (vehicle == null)
            return;
        var flight = vehicle.GetComponent<BattleBusFlightController>();
        if (!IsBattleBus(vehicle) && flight == null)
            return;
        flight?.SetPiloted(false);
    }

    private void HandleGameEvent(string eventName)
    {
        if (string.Equals(eventName, VehicleRepainterColorRestoredEvent,
                StringComparison.Ordinal))
        {
            var vehicles = VehicleHelper.AllPlayerVehicles;
            if (vehicles == null)
                return;
            foreach (var vehicle in vehicles)
                if (IsBattleBus(vehicle))
                    ApplyVehiclePaint(vehicle, "vehicle-repainter-restored", true);
            RestorePreviewPaint();
            return;
        }

        if (!string.Equals(eventName, VehicleRepainterColorPreviewEvent,
                StringComparison.Ordinal) &&
            !string.Equals(eventName, VehicleRepainterColorResetEvent,
                StringComparison.Ordinal))
            return;

        if (!PurchaseVehicleUI.IsPanelOpen)
            return;

        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (IsBattleBus(selectedVehicle))
            ApplyVehiclePaint(selectedVehicle, "vehicle-repainter", true);
    }

    private void ApplyVehiclePaint(VehicleController? vehicle, string source, bool force)
    {
        if (!IsBattleBus(vehicle))
            return;

        var target = vehicle!;
        var paint = target.GetComponent<BattleBusPaintController>();
        if (paint == null)
        {
            context?.Logger.Warn(
                $"Battle Bus paint component missing on vehicle={target.GetInstanceID()}; adding runtime fallback.");
            paint = target.gameObject.AddComponent<BattleBusPaintController>();
        }
        paint.Initialize(target, context);
        paint.ApplyCurrentColor(source, force);
    }

    private bool IsBattleBus(VehicleController? vehicle) =>
        vehicle != null && vehicle.vehicleInstance != null &&
        string.Equals(vehicle.vehicleInstance.vehicleTypeName, vehicleTypeName,
            StringComparison.Ordinal);
}

internal static class BattleBusTruckDealerStock
{
    private const string DealerContactId = "General US Trucks";
    private const string TargetBusinessTypeName = "ba:businesstype_cardealership";
    private const string TargetBuildingSize = "ba:buildingsize_m";
    private const int TargetBuildingVersion = 1;
    private const string TargetLayoutName = "IndustryCityCarDealershipTrucks";

    internal static bool EnsureVehicleAvailable(string vehicleName, ModContext? context)
    {
        if (string.IsNullOrWhiteSpace(vehicleName) || BusinessLayoutSetHelper.loadingLayouts)
            return false;

        // Migrate the previous dealer override without touching any other stock.
        RemoveVehicleFromContact("City Cars", vehicleName);
        var stock = new List<string>();
        if (ContractItemsForSaleService.TryGetVehiclesForContact(DealerContactId,
                out List<string> existing) && existing != null)
            AddUniqueRange(stock, existing);

        if (Contains(stock, vehicleName))
            return true;

        try
        {
            var layout = BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
                TargetBusinessTypeName,
                new BuildingSizeInfo(TargetBuildingSize, TargetBuildingVersion),
                TargetLayoutName.ToLowerInvariant(),
                false);
            if (layout?.Items == null)
                return false;

            foreach (var item in layout.Items)
            {
                var purchaser = item?.playerItemPurchaserSettings;
                if (purchaser == null || !purchaser.enabled || string.IsNullOrEmpty(purchaser.itemName))
                    continue;
                var definition = ItemsGetter.GetByName(purchaser.itemName);
                if (definition != null && !string.IsNullOrEmpty(definition.vehicleType))
                    AddUnique(stock, definition.vehicleType);
            }
            AddUnique(stock, vehicleName);
            ContractItemsForSaleService.SetVehiclesForContact(DealerContactId, stock);
            return true;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"Battle Bus: General US Trucks stock update failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    internal static void RemoveVehicle(string vehicleName)
    {
        RemoveVehicleFromContact(DealerContactId, vehicleName);
        RemoveVehicleFromContact("City Cars", vehicleName);
    }

    private static void RemoveVehicleFromContact(string contactId, string vehicleName)
    {
        if (string.IsNullOrWhiteSpace(vehicleName) ||
            !ContractItemsForSaleService.TryGetVehiclesForContact(contactId,
                out List<string> current) || current == null)
            return;

        var remaining = new List<string>();
        foreach (var item in current)
            if (!string.Equals(item, vehicleName, StringComparison.Ordinal))
                AddUnique(remaining, item);
        if (remaining.Count == current.Count)
            return;
        if (remaining.Count == 0)
            ContractItemsForSaleService.RemoveContact(contactId);
        else
            ContractItemsForSaleService.SetVehiclesForContact(contactId, remaining);
    }

    private static bool Contains(List<string> values, string target)
    {
        foreach (var value in values)
            if (string.Equals(value, target, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static void AddUniqueRange(List<string> target, IEnumerable<string> source)
    {
        foreach (var value in source)
            AddUnique(target, value);
    }

    private static void AddUnique(List<string> target, string value)
    {
        if (!Contains(target, value))
            target.Add(value);
    }
}
