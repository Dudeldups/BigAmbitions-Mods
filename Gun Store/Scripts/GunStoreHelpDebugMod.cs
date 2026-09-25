#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
#if GUN_STORE_HELP_UI_DEBUG
using System.Runtime.CompilerServices;
using System.Text;
#endif
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem;
using Helpers;
using Localizor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
#if GUN_STORE_HELP_UI_DEBUG
using UnityEngine.EventSystems;
#endif
using UnityEngine.SceneManagement;

internal static class GunStoreDiagnosticFlags
{
    internal static readonly bool Debug = false;
    internal static readonly bool ShelfLifecycle = false;
}

[DefaultExecutionOrder(10000)]
internal sealed class GunStoreHelpDebugRuntime : MonoBehaviour
{
    internal static GunStoreHelpDebugRuntime? Active { get; private set; }
    private const string GunStoreBundleKey = "AssetBundles/gunstore-businesstype.unity3d";
    private const string RoundedShelfItemName = "ba:itemname_roundedshelf";
    private const string CheapGiftItemName = "ba:itemname_cheapgift";
    private const string ExpensiveFlowerItemName = "ba:itemname_expensiveflower";
    private const int GeneratedDisplayVersion = 21;
    private static readonly float[] ShelfVisualRetryDelays = { 0f, 0f, 2f, 3f, 5f, 10f, 10f };
    private ModContext? context;
    private bool shuttingDown;
    private Coroutine? pendingNavigationPatch;
    private bool pendingForcedNavigationRefresh;
    private bool gameLoadedLateCallbackRegistered;
    private bool postCitySaveRepairCompleted;
    private Coroutine? shelfVisualRepairCoroutine;
    private Coroutine? gunStoreVisualSetupCoroutine;
    private Coroutine? npcBannerCoroutine;
    private readonly HashSet<string> loggedGunStoreVisualSetupFailures = new(StringComparer.Ordinal);
    private readonly HashSet<int> loggedNpcShelfVisualRestorations = new();
    private readonly Dictionary<Material, Material> displayMaterialCache = new();
    private readonly Dictionary<Material, Material> shelfGlassMaterialCache = new();
    private readonly HashSet<int> failedShelfLifecycleHooks = new();
    private readonly HashSet<int> hookedShelfPrefabIds = new();
    private bool loggedMissingShelfPrefabHook;
    private static readonly FieldInfo? ShelfVisualItemsField = typeof(ShelfController).GetField(
        "_visualItems",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ShelfItemsVisualsContainerField = typeof(ShelfController).GetField(
        "itemsVisualsContainer",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly IReadOnlyDictionary<string, string> GunStoreVisualPrefabPaths =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gunstore-businesstype:itemname_ak47"] = "Assets/Mods/Gun Store/Prefabs/Ak47.prefab",
            ["gunstore-businesstype:itemname_ammosmall"] = "Assets/Mods/Gun Store/Prefabs/AmmoSmall.prefab",
            ["gunstore-businesstype:itemname_wincheatersxp"] = "Assets/Mods/Gun Store/Prefabs/WinCheaterSXP.prefab",
            ["gunstore-businesstype:itemname_berettam9"] = "Assets/Mods/Gun Store/Prefabs/BerettaM9.prefab",
            ["gunstore-businesstype:itemname_ammolarge"] = "Assets/Mods/Gun Store/Prefabs/AmmoLarge.prefab",
            ["gunstore-businesstype:itemname_rpg"] = "Assets/Mods/Gun Store/Prefabs/Rpg.prefab"
        };

    public static GunStoreHelpDebugRuntime Initialize(ModContext context)
    {
        var existing = FindObjectOfType<GunStoreHelpDebugRuntime>();
        var created = existing == null;
        if (existing == null)
        {
            var runtimeObject = new GameObject(nameof(GunStoreHelpDebugRuntime));
            DontDestroyOnLoad(runtimeObject);
            existing = runtimeObject.AddComponent<GunStoreHelpDebugRuntime>();
        }

        existing.context = context;
        Active = existing;
        existing.shuttingDown = false;
#if GUN_STORE_HELP_UI_DEBUG
        GunStoreHelpDebugLogger.StartSession();
        GunStoreHelpDebugLogger.Trace(
            $"Runtime Initialize: created={created}, activeScene={SceneManager.GetActiveScene().name}, " +
            $"frame={Time.frameCount}.");
#endif
        LocalizorManager.OnLanguageChanged -= existing.HandleLanguageChanged;
        LocalizorManager.OnLanguageChanged += existing.HandleLanguageChanged;
        SceneManager.sceneLoaded -= existing.HandleSceneLoaded;
        SceneManager.sceneLoaded += existing.HandleSceneLoaded;
        GlobalEvents.onEnterBuilding -= existing.HandleEnterBuilding;
        GlobalEvents.onEnterBuilding += existing.HandleEnterBuilding;
        GlobalEvents.onEnterBuildingDelayed -= existing.HandleEnterBuildingDelayed;
        GlobalEvents.onEnterBuildingDelayed += existing.HandleEnterBuildingDelayed;
        GlobalEvents.onCityMapClosed -= existing.HandleCityMapClosed;
        GlobalEvents.onCityMapClosed += existing.HandleCityMapClosed;
        GlobalEvents.onItemDropped -= existing.HandleItemDropped;
        GlobalEvents.onItemDropped += existing.HandleItemDropped;
        if (!existing.gameLoadedLateCallbackRegistered)
        {
            GlobalEvents.RegisterOnGameLoadedLateCallback(existing.HandleGameLoadedLate);
            existing.gameLoadedLateCallbackRegistered = true;
#if GUN_STORE_HELP_UI_DEBUG
            GunStoreHelpDebugLogger.Trace("Registered GlobalEvents OnGameLoadedLate callback.");
#endif
        }
        existing.ScheduleNavigationPatch(reason: "initialize");
        return existing;
    }

    internal void ScheduleNavigationPatch(bool forceRefresh = false, string reason = "unspecified")
    {
        pendingForcedNavigationRefresh |= forceRefresh;
#if GUN_STORE_HELP_UI_DEBUG
        GunStoreHelpDebugLogger.Trace(
            $"ScheduleNavigationPatch: reason={reason}, force={forceRefresh}, " +
            $"frame={Time.frameCount}, pendingCoroutine={pendingNavigationPatch != null}.");
#endif

        if (pendingNavigationPatch != null)
            StopCoroutine(pendingNavigationPatch);

        pendingNavigationPatch = StartCoroutine(PatchNavigationAfterUiRefresh());
    }

#if GUN_STORE_HELP_UI_DEBUG
    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F9))
            return;

        try
        {
            var result = GunStoreHelpDebugSnapshotWriter.WriteSnapshot();
            context?.Logger.Info(
                $"Captured Gun Store Help UI debug snapshot at '{result.LogPath}' " +
                $"({result.HelpComponentCount} help components, {result.RootCount} roots, " +
                $"{result.ElementCount} UI elements)."
            );
        }
        catch (Exception exception)
        {
            GunStoreHelpDebugLogger.Error("Failed to capture the Help UI snapshot.", exception);
            context?.Logger.Error(exception);
        }
    }
#endif

    public void Shutdown()
    {
        if (shuttingDown)
            return;

        shuttingDown = true;
        if (ReferenceEquals(Active, this))
            Active = null;
        LocalizorManager.OnLanguageChanged -= HandleLanguageChanged;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        GlobalEvents.onEnterBuilding -= HandleEnterBuilding;
        GlobalEvents.onEnterBuildingDelayed -= HandleEnterBuildingDelayed;
        GlobalEvents.onCityMapClosed -= HandleCityMapClosed;
        GlobalEvents.onItemDropped -= HandleItemDropped;
        if (pendingNavigationPatch != null)
            StopCoroutine(pendingNavigationPatch);
        if (gunStoreVisualSetupCoroutine != null)
            StopCoroutine(gunStoreVisualSetupCoroutine);
        if (npcBannerCoroutine != null)
            StopCoroutine(npcBannerCoroutine);

        foreach (var observer in Resources.FindObjectsOfTypeAll<GunStoreNpcShelfVisualObserver>())
        {
            if (observer != null && observer.gameObject.scene.IsValid())
            {
                observer.enabled = false;
                Destroy(observer);
            }
        }

        foreach (var material in displayMaterialCache.Values)
        {
            if (material != null)
                Destroy(material);
        }

        displayMaterialCache.Clear();

        foreach (var material in shelfGlassMaterialCache.Values)
        {
            if (material != null)
                Destroy(material);
        }

        shelfGlassMaterialCache.Clear();
        Destroy(gameObject);
    }

    private void HandleLanguageChanged()
    {
        ScheduleNavigationPatch(reason: "language-changed");
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (postCitySaveRepairCompleted)
            StartGunStoreVisualSetup($"scene-loaded:{scene.name}:{mode}");

        ScheduleNavigationPatch(reason: $"scene-loaded:{scene.name}:{mode}");
    }

    private void HandleEnterBuildingDelayed(Address address)
    {
        ScheduleGunStoreInteriorVisuals(address, "enter-building-delayed");
    }

    private void HandleEnterBuilding(Address address)
    {
        ScheduleGunStoreInteriorVisuals(address, "enter-building");
    }

    private void HandleCityMapClosed()
    {
        // Teleporting from the map can instantiate nearby rival interiors without
        // producing a new Unity sceneLoaded callback.
        if (postCitySaveRepairCompleted)
            StartGunStoreVisualSetup("city-map-closed");
    }

    private void HandleItemDropped(ItemController item)
    {
        if (item?.BuildingContext?.Registration?.businessTypeName ==
            "gunstore-businesstype:businesstype_gunstore")
            StartGunStoreVisualSetup("item-dropped-in-gun-store");
    }

    private void ScheduleGunStoreInteriorVisuals(Address address, string reason)
    {
        if (!postCitySaveRepairCompleted)
            return;

        // Building addresses may be reconstructed between the entry event and save
        // registrations, so equality can miss a real Gun Store entry. This is a bounded
        // event-triggered pass; TryInstallGunStoreVisualSlot filters to actual Gun Store stock.
        var registration = SaveGameManager.Current?.BuildingRegistrations?
            .FirstOrDefault(item => item != null &&
                (item.Address.Equals(address) || item.Address.ToString() == address.ToString()));
        if (registration != null && !string.Equals(registration.businessTypeName,
                "gunstore-businesstype:businesstype_gunstore", StringComparison.Ordinal))
            return;
        context?.Logger.Info(
            $"Gun Store: building entry visual check: name='{registration?.BusinessName ?? "<unresolved>"}', " +
            $"address={address}, businessType='{registration?.businessTypeName ?? "<unresolved>"}', reason='{reason}'.");
        StartGunStoreVisualSetup($"entered:{address}:{reason}");
    }

    private void HandleGameLoadedLate()
    {
        GlobalEvents.onEnterBuilding -= HandleEnterBuilding;
        GlobalEvents.onEnterBuilding += HandleEnterBuilding;
        GlobalEvents.onEnterBuildingDelayed -= HandleEnterBuildingDelayed;
        GlobalEvents.onEnterBuildingDelayed += HandleEnterBuildingDelayed;
        GlobalEvents.onCityMapClosed -= HandleCityMapClosed;
        GlobalEvents.onCityMapClosed += HandleCityMapClosed;
        GlobalEvents.onItemDropped -= HandleItemDropped;
        GlobalEvents.onItemDropped += HandleItemDropped;
        GunStoreBusinessTypeCityMod.RepairEmptyProductCachesAfterGameLoaded(context);
        GunStoreBusinessTypeCityMod.RestoreAiRivalsAfterGameLoaded(context);
        StartNpcBannerGeneration();
        if (postCitySaveRepairCompleted)
            StartGunStoreVisualSetup("game-loaded-late");

        ScheduleNavigationPatch(forceRefresh: true, reason: "game-loaded-late");
    }

    private IEnumerator PatchNavigationAfterUiRefresh()
    {
        // Let the native Help and localization callbacks finish rebuilding their UI first.
        yield return null;
        pendingNavigationPatch = null;
        RunPostCitySaveRepair();
        var forceRefresh = pendingForcedNavigationRefresh;
        pendingForcedNavigationRefresh = false;
#if GUN_STORE_HELP_UI_DEBUG
        GunStoreHelpDebugLogger.Trace(
            $"Patch coroutine executing: force={forceRefresh}, frame={Time.frameCount}.");
#endif

        try
        {
            var result = GunStoreHelpNavigationPatch.TryApply(this, forceRefresh);
#if GUN_STORE_HELP_UI_DEBUG
            GunStoreHelpDebugLogger.Trace($"Patch coroutine result: {result}.");
#endif
        }
        catch (Exception exception)
        {
            context?.Logger.Error(exception);
        }
    }

    private void RunPostCitySaveRepair()
    {
        if (postCitySaveRepairCompleted || SaveGameManager.Current?.BuildingRegistrations == null)
            return;

        GunStoreBusinessTypeCityMod.RepairEmptyProductCachesAfterGameLoaded(context);
        GunStoreBusinessTypeCityMod.RestoreAiRivalsAfterGameLoaded(context);
        postCitySaveRepairCompleted = true;
        context?.Logger.Info("Gun Store: completed post-city save repair after building registrations became available.");

        StartNpcBannerGeneration();

        if (shelfVisualRepairCoroutine != null)
            StopCoroutine(shelfVisualRepairCoroutine);

        shelfVisualRepairCoroutine = StartCoroutine(RepairMalformedShelfVisuals());

        StartGunStoreVisualSetup("post-city-save-repair");
        StartCoroutine(LogGunStoreVisualDiagnostics());
    }

    private void StartGunStoreVisualSetup(string reason)
    {
        if (gunStoreVisualSetupCoroutine != null)
            StopCoroutine(gunStoreVisualSetupCoroutine);

        gunStoreVisualSetupCoroutine = StartCoroutine(InstallGunStoreShelfVisuals());
        context?.Logger.Info($"Gun Store: scheduled isolated shelf-visual setup; reason='{reason}'.");
    }

    private void StartNpcBannerGeneration()
    {
        if (context == null || npcBannerCoroutine != null)
            return;
        npcBannerCoroutine = StartCoroutine(GunStoreNpcBannerRuntime.Generate(context));
    }

    private IEnumerator RepairMalformedShelfVisuals()
    {
        // Affected shelf controllers can be instantiated after the city callback. Keep the
        // fallback bounded, but cover the first half-minute when business simulation begins.
        for (var pass = 0; pass < 6; pass++)
        {
            yield return pass == 0 ? null : new WaitForSeconds(5f);
            RepairMalformedShelfVisualsInLoadedScenes();
        }

        shelfVisualRepairCoroutine = null;
    }

    private IEnumerator InstallGunStoreShelfVisuals()
    {
        // The game's mod showcase API replaces a template visual on every matching base-game
        // shelf. Gun Store used the same template for several products, which left destroyed
        // visual references in unrelated shops. Add an independent visual slot only to shelves
        // that actually contain Gun Store stock instead.
        for (var pass = 0; pass < ShelfVisualRetryDelays.Length && !shuttingDown; pass++)
        {
            // StartCoroutine runs until its first yield immediately. Correct shelf glass on
            // the scene-loaded callback, before the first frame can show iridescence. Keep
            // a next-frame pass for fixtures whose stock is assigned during scene startup.
            if (pass == 1)
                yield return null;
            else if (pass > 1)
                yield return new WaitForSeconds(ShelfVisualRetryDelays[pass]);

            // This retry window is finite. Later interiors start a fresh window via
            // building-entry or map-close events; there is no permanent world scan.

            var installedCount = 0;
            foreach (var shelf in Resources.FindObjectsOfTypeAll<ShelfController>())
            {
                if (shelf == null)
                    continue;

                AttachShelfLifecycleHook(shelf);
                if (!shelf.gameObject.scene.IsValid() || !shelf.gameObject.scene.isLoaded)
                    continue;

                if (TryInstallGunStoreVisualSlot(shelf, out var itemName, out var shelfName))
                {
                    installedCount++;
                    context?.Logger.Info(
                        $"Gun Store: installed isolated shelf visual: product='{itemName}', shelf='{shelfName}', " +
                        $"position={shelf.transform.position}.");
                }
            }

            if (installedCount > 0)
            {
                context?.Logger.Info(
                    $"Gun Store: installed {installedCount} isolated shelf visual slot(s) on pass {pass + 1}. " +
                    "No base-game showcase fixture definitions were changed.");
            }
        }

        if (hookedShelfPrefabIds.Count == 0 && !loggedMissingShelfPrefabHook)
        {
            loggedMissingShelfPrefabHook = true;
            context?.Logger.Warn("Gun Store: no source shelf prefab accepted the lifecycle hook; newly loaded interiors may need another setup event.");
        }

        gunStoreVisualSetupCoroutine = null;
    }

    private void AttachShelfLifecycleHook(ShelfController shelf)
    {
        if (shelf.itemName != RoundedShelfItemName && shelf.itemName != "ba:itemname_productpanel")
            return;

        if (shelf.GetComponent<GunStoreShelfLifecycleHook>() != null ||
            failedShelfLifecycleHooks.Contains(shelf.GetInstanceID()))
            return;

        try
        {
            // Include loaded prefab assets (invalid scene) as well as live fixtures.
            // The component is inherited by future instances, unlike a one-time scene scan.
            shelf.gameObject.AddComponent<GunStoreShelfLifecycleHook>();
            if (!shelf.gameObject.scene.IsValid())
                hookedShelfPrefabIds.Add(shelf.GetInstanceID());
            if (GunStoreDiagnosticFlags.Debug && GunStoreDiagnosticFlags.ShelfLifecycle)
                context?.Logger.Info(
                    $"Gun Store: hooked shelf lifecycle: fixture='{shelf.itemName}', " +
                    $"sourceAsset={!shelf.gameObject.scene.IsValid()}, object='{shelf.name}'.");
        }
        catch (Exception exception)
        {
            failedShelfLifecycleHooks.Add(shelf.GetInstanceID());
            context?.Logger.Warn(
                $"Gun Store: could not hook shelf lifecycle: fixture='{shelf.itemName}', object='{shelf.name}'.");
            context?.Logger.Error(exception);
        }
    }

    internal void InstallShelfVisualOnInitialization(ShelfController shelf)
    {
        if (shuttingDown || context == null || shelf == null ||
            !shelf.gameObject.scene.IsValid() || !shelf.gameObject.scene.isLoaded)
            return;

        if (TryInstallGunStoreVisualSlot(shelf, out var itemName, out var shelfName))
            context.Logger.Info(
                $"Gun Store: installed shelf visual on fixture initialization: product='{itemName}', " +
                $"fixture='{shelfName}', position={shelf.transform.position}.");
    }

    private bool TryInstallGunStoreVisualSlot(
        ShelfController shelf,
        out string itemName,
        out string shelfName)
    {
        itemName = "<none>";
        shelfName = shelf.name;

        if (ShelfItemsVisualsContainerField?.GetValue(shelf) is not Transform visualsContainer)
            return false;

        var owner = shelf.GetComponentInParent<ItemController>();
        var stock = owner?.ItemInstance == null ? null : ItemHelper.GetStockInstance(owner.ItemInstance);
        var npcProductName = owner?.playerItemPurchaserSettings?.enabled == true
            ? owner.playerItemPurchaserSettings.itemName
            : null;
        var productName = stock?.itemName ?? npcProductName ?? string.Empty;
        if (context == null || productName.Length == 0 ||
            !GunStoreVisualPrefabPaths.TryGetValue(productName, out var prefabPath))
            return false;

        itemName = productName;
        shelfName = owner?.Item?.itemName ?? shelf.name;
        DisableIridescenceOnGunStoreShelfGlass(shelf, itemName);
        var visualSlotName = itemName.GetIdWithoutType();
        var existingVisualSlot = visualsContainer.Find(visualSlotName);
        if (existingVisualSlot != null)
        {
            var existingMarker = existingVisualSlot.GetComponent<GunStoreMeshOnlyDisplayMarker>();
            if (existingMarker != null && existingMarker.Generation == GeneratedDisplayVersion)
            {
                AttachNpcShelfObserver(shelf, owner, existingVisualSlot, itemName);
                EnsureNpcShelfDisplayVisible(shelf, owner, existingVisualSlot, itemName);
                return false;
            }

            // Upgrade visual slots created by 0.1.13/0.1.14 in an already-loaded city. They
            // have the same product name but contain the unbounded placement layout.
            DestroyImmediate(existingVisualSlot.gameObject);
            context.Logger.Info(
                $"Gun Store: replaced legacy shelf display slot: product='{itemName}', shelf='{shelfName}', " +
                $"position={shelf.transform.position}.");
        }

        // Match the exact layouts used by the original showcase registration. The first
        // populated slot is not stable across fixtures and can be a dense gift layout.
        var placementTemplateName = (owner?.Item?.itemName == RoundedShelfItemName
            ? ExpensiveFlowerItemName
            : CheapGiftItemName).GetIdWithoutType();
        var template = visualsContainer.Cast<Transform>().FirstOrDefault(candidate =>
            candidate.name.Equals(placementTemplateName, StringComparison.InvariantCultureIgnoreCase));
        var visualPrefab = AssetService.GetBundle(context.ModId, GunStoreBundleKey)
            .LoadAsset<GameObject>(prefabPath);
        if (template == null)
        {
            LogGunStoreVisualSetupFailure(itemName, shelfName, "no populated base visual slot was available");
            return false;
        }

        if (visualPrefab == null)
        {
            LogGunStoreVisualSetupFailure(itemName, shelfName, $"visual prefab '{prefabPath}' was not found in the Gun Store bundle");
            return false;
        }

        if (template.childCount == 0)
        {
            LogGunStoreVisualSetupFailure(itemName, shelfName, "the base visual slot has no child placement transforms");
            return false;
        }

        var visualSlot = Instantiate(template, visualsContainer);
        visualSlot.name = visualSlotName;
        visualSlot.gameObject.AddComponent<GunStoreMeshOnlyDisplayMarker>().Generation = GeneratedDisplayVersion;
        for (var index = visualSlot.childCount - 1; index >= 0; index--)
            DestroyImmediate(visualSlot.GetChild(index).gameObject);

        var displayCount = 0;
        var meshCount = 0;
        foreach (var placement in template.Cast<Transform>())
        {
            var displayVisual = new GameObject(visualPrefab.name + " Display");
            displayVisual.transform.SetParent(visualSlot, false);
            displayVisual.transform.SetPositionAndRotation(placement.position, placement.rotation);
            meshCount += CopyDisplayMeshHierarchy(visualPrefab.transform, displayVisual.transform);
            displayCount++;
        }

        if (meshCount == 0)
        {
            Destroy(visualSlot.gameObject);
            LogGunStoreVisualSetupFailure(itemName, shelfName, "the product prefab contains no static display meshes");
            return false;
        }

        // Never instantiate the product prefab itself here. It has an ItemController, and Awake
        // registers cargo and interaction overlays before a later Disable can run. Mesh-only
        // copies keep the display visual separate from product gameplay state.
        shelf.ShowItemVisuals(itemName, showDefault: false);
        shelf.UpdateVisuals();
        AttachNpcShelfObserver(shelf, owner, visualSlot, itemName);
        EnsureNpcShelfDisplayVisible(shelf, owner, visualSlot, itemName);
        context.Logger.Info(
            $"Gun Store: installed mesh-only shelf display: product='{itemName}', shelf='{shelfName}', " +
            $"template='{template.name}', displayCount={displayCount}, meshCount={meshCount}, " +
            $"position={shelf.transform.position}.");
        return true;
    }

    private void AttachNpcShelfObserver(
        ShelfController shelf, ItemController? owner, Transform visualSlot, string itemName)
    {
        if (owner?.BuildingContext?.Registration?.RentedByPlayer != false ||
            owner.playerItemPurchaserSettings?.enabled != true ||
            !string.Equals(owner.playerItemPurchaserSettings.itemName, itemName, StringComparison.Ordinal))
            return;

        var observer = shelf.GetComponent<GunStoreNpcShelfVisualObserver>();
        if (observer == null)
        {
            observer = shelf.gameObject.AddComponent<GunStoreNpcShelfVisualObserver>();
            context?.Logger.Info(
                $"Gun Store: attached NPC shelf visual observer: product='{itemName}', " +
                $"position={shelf.transform.position}.");
        }
        observer.Initialize(this, shelf, owner, visualSlot, itemName);
    }

    internal void EnsureNpcShelfDisplayVisible(
        ShelfController shelf, ItemController? owner, Transform visualSlot, string itemName)
    {
        if (shuttingDown)
            return;

        // AI fixtures advertise virtual stock through PlayerItemPurchaserSettings, but
        // their cargo fill can be zero. The native UpdateVisuals then disables every
        // child of the otherwise correctly selected Gun Store visual slot.
        if (owner?.BuildingContext?.Registration?.RentedByPlayer != false ||
            owner.playerItemPurchaserSettings?.enabled != true ||
            !string.Equals(owner.playerItemPurchaserSettings.itemName, itemName, StringComparison.Ordinal) ||
            !shelf.gameObject.activeInHierarchy)
            return;

        var wasActive = visualSlot.gameObject.activeSelf;
        var firstVisualActive = visualSlot.childCount > 0 &&
                                visualSlot.GetChild(0).gameObject.activeSelf;
        var previousFill = shelf.fillState;
        if (wasActive && firstVisualActive && previousFill >= 0.99d)
            return;

        shelf.ShowItemVisuals(itemName, showDefault: false);
        visualSlot.gameObject.SetActive(true);
        shelf.UpdateFillState(1d);
        if (loggedNpcShelfVisualRestorations.Add(shelf.GetInstanceID()))
            context?.Logger.Info(
                $"Gun Store: restored NPC shelf display: product='{itemName}', position={shelf.transform.position}, " +
                $"slotActiveBefore={wasActive}, firstVisualActiveBefore={firstVisualActive}, " +
                $"fillBefore={previousFill:0.###}, visualCount={visualSlot.childCount}.");
    }

    private void DisableIridescenceOnGunStoreShelfGlass(ShelfController shelf, string itemName)
    {
        // RoundedShelf's fixture mesh has M_GlassTransparent_01 as a submaterial. The game
        // asset enables full-strength HDRP iridescence on that glass, producing the colored
        // view-angle-dependent polygons even when the shelf contains no display items.
        // Change only this stocked fixture's renderer; never mutate the game's shared asset.
        var fixtureRenderer = shelf.GetComponent<MeshRenderer>();
        if (fixtureRenderer == null)
            return;

        var materials = fixtureRenderer.sharedMaterials;
        var changed = false;
        for (var index = 0; index < materials.Length; index++)
        {
            var source = materials[index];
            if (source == null || !source.name.StartsWith("M_GlassTransparent", StringComparison.Ordinal) ||
                !source.IsKeywordEnabled("_MATERIAL_FEATURE_IRIDESCENCE") ||
                !source.HasProperty("_IridescenceMask") ||
                source.GetFloat("_IridescenceMask") <= 0f)
            {
                continue;
            }

            if (!shelfGlassMaterialCache.TryGetValue(source, out var corrected))
            {
                corrected = new Material(source)
                {
                    name = source.name + " (Gun Store non-iridescent glass)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                // Keep the original transparent HDRP shader variant and material type.
                // Unity specifies a zero mask as the supported way to disable only the
                // angle-dependent iridescence contribution.
                corrected.SetFloat("_IridescenceMask", 0f);
                shelfGlassMaterialCache.Add(source, corrected);
                context?.Logger.Info(
                    $"Gun Store: prepared non-iridescent shelf glass: source='{source.name}', " +
                    $"shader='{source.shader?.name ?? "<missing>"}', " +
                    $"sourceMaterialID={(source.HasProperty("_MaterialID") ? source.GetFloat("_MaterialID").ToString() : "<missing>")}, " +
                    $"sourceIridescenceMask={(source.HasProperty("_IridescenceMask") ? source.GetFloat("_IridescenceMask").ToString() : "<missing>")}, " +
                    $"surfaceType={(source.HasProperty("_SurfaceType") ? source.GetFloat("_SurfaceType").ToString() : "<missing>")}. ");
            }

            materials[index] = corrected;
            changed = true;
        }

        if (!changed)
            return;

        fixtureRenderer.sharedMaterials = materials;
        context?.Logger.Info(
            $"Gun Store: disabled iridescence on stocked shelf glass: product='{itemName}', " +
            $"shelf='{shelf.name}', position={shelf.transform.position}. " +
            "The original fixture material and all other stores are unchanged.");
    }

    private int CopyDisplayMeshHierarchy(Transform source, Transform destination)
    {
        var copiedMeshCount = 0;
        var sourceFilter = source.GetComponent<MeshFilter>();
        var sourceRenderer = source.GetComponent<MeshRenderer>();
        if (sourceFilter?.sharedMesh != null && sourceRenderer != null)
        {
            var destinationFilter = destination.gameObject.AddComponent<MeshFilter>();
            destinationFilter.sharedMesh = sourceFilter.sharedMesh;

            var destinationRenderer = destination.gameObject.AddComponent<MeshRenderer>();
            destinationRenderer.sharedMaterials = sourceRenderer.sharedMaterials
                .Select(GetCompatibleDisplayMaterial)
                .ToArray();
            destinationRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            destinationRenderer.receiveShadows = sourceRenderer.receiveShadows;
            destinationRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
            destinationRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            copiedMeshCount++;
        }

        foreach (var sourceChild in source.Cast<Transform>())
        {
            var destinationChild = new GameObject(sourceChild.name).transform;
            destinationChild.SetParent(destination, false);
            destinationChild.localPosition = sourceChild.localPosition;
            destinationChild.localRotation = sourceChild.localRotation;
            destinationChild.localScale = sourceChild.localScale;
            copiedMeshCount += CopyDisplayMeshHierarchy(sourceChild, destinationChild);
        }

        return copiedMeshCount;
    }

    private Material? GetCompatibleDisplayMaterial(Material? sourceMaterial)
    {
        if (sourceMaterial == null)
            return null;

        if (displayMaterialCache.TryGetValue(sourceMaterial, out var cachedMaterial))
            return cachedMaterial;

        var hdrpLit = Shader.Find("HDRP/Lit") ??
                      Shader.Find("High Definition Render Pipeline/Lit");
        if (hdrpLit == null)
        {
            var failureKey = $"missing-hdrp-lit:{sourceMaterial.name}";
            if (loggedGunStoreVisualSetupFailures.Add(failureKey))
            {
                context?.Logger.Warn(
                    $"Gun Store: cannot remap display material '{sourceMaterial.name}' because the HDRP/Lit shader was not found.");
            }

            return sourceMaterial;
        }

        var sourceShaderName = sourceMaterial.shader?.name ?? "<missing>";
        var baseColor = GetMaterialColor(
            sourceMaterial,
            Color.white,
            "baseColorFactor",
            "_BaseColor",
            "_Color");
        baseColor.a = 1f;

        var baseTextureProperty = FirstTextureProperty(
            sourceMaterial,
            "baseColorTexture",
            "_BaseColorMap",
            "_MainTex");
        var baseTexture = baseTextureProperty == null
            ? null
            : sourceMaterial.GetTexture(baseTextureProperty);
        var baseTextureScale = baseTextureProperty == null
            ? Vector2.one
            : sourceMaterial.GetTextureScale(baseTextureProperty);
        var baseTextureOffset = baseTextureProperty == null
            ? Vector2.zero
            : sourceMaterial.GetTextureOffset(baseTextureProperty);

        var normalTextureProperty = FirstTextureProperty(
            sourceMaterial,
            "normalTexture",
            "_NormalMap",
            "_BumpMap");
        var normalTexture = normalTextureProperty == null
            ? null
            : sourceMaterial.GetTexture(normalTextureProperty);
        var normalScale = GetMaterialFloat(
            sourceMaterial,
            1f,
            "normalTexture_scale",
            "normalScale",
            "_NormalScale");
        var metallic = GetMaterialFloat(sourceMaterial, 0f, "metallicFactor", "_Metallic");
        var roughness = GetMaterialFloat(sourceMaterial, 1f, "roughnessFactor");
        var smoothness = 1f - Mathf.Clamp01(roughness);

        var compatibleMaterial = new Material(hdrpLit)
        {
            name = sourceMaterial.name + " (Gun Store HDRP Display)",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = (int)RenderQueue.Geometry
        };
        SetMaterialColor(compatibleMaterial, "_BaseColor", baseColor);
        SetMaterialTexture(
            compatibleMaterial,
            "_BaseColorMap",
            baseTexture,
            baseTextureScale,
            baseTextureOffset);
        SetMaterialTexture(
            compatibleMaterial,
            "_NormalMap",
            normalTexture,
            Vector2.one,
            Vector2.zero);
        SetMaterialFloat(compatibleMaterial, "_NormalScale", normalScale);
        SetMaterialFloat(compatibleMaterial, "_Metallic", metallic);
        SetMaterialFloat(compatibleMaterial, "_Smoothness", smoothness);
        SetMaterialFloat(compatibleMaterial, "_SurfaceType", 0f);
        SetMaterialFloat(compatibleMaterial, "_AlphaCutoffEnable", 0f);
        SetMaterialFloat(compatibleMaterial, "_SupportDecals", 0f);
        SetMaterialFloat(compatibleMaterial, "_ReceivesSSR", 0f);
        SetMaterialFloat(compatibleMaterial, "_ReceivesSSRTransparent", 0f);
        SetMaterialFloat(compatibleMaterial, "_RefractionModel", 0f);
        SetMaterialFloat(compatibleMaterial, "_ZWrite", 1f);
        SetMaterialFloat(compatibleMaterial, "_SrcBlend", (float)BlendMode.One);
        SetMaterialFloat(compatibleMaterial, "_DstBlend", (float)BlendMode.Zero);
        compatibleMaterial.SetOverrideTag("RenderType", "Opaque");
        compatibleMaterial.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        compatibleMaterial.DisableKeyword("_ALPHATEST_ON");
        compatibleMaterial.EnableKeyword("_DISABLE_DECALS");
        compatibleMaterial.EnableKeyword("_DISABLE_SSR");
        compatibleMaterial.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
        if (normalTexture != null)
            compatibleMaterial.EnableKeyword("_NORMALMAP_TANGENT_SPACE");

        displayMaterialCache[sourceMaterial] = compatibleMaterial;
        context?.Logger.Info(
            $"Gun Store: remapped display material: source='{sourceMaterial.name}', " +
            $"sourceShader='{sourceShaderName}', targetShader='{hdrpLit.name}', " +
            $"baseColor={baseColor}, metallic={metallic:0.###}, smoothness={smoothness:0.###}, " +
            $"baseTexture='{baseTexture?.name ?? "<none>"}'.");
        return compatibleMaterial;
    }

    private static string? FirstTextureProperty(Material material, params string[] properties)
    {
        return properties.FirstOrDefault(property =>
            material.HasProperty(property) && material.GetTexture(property) != null);
    }

    private static Color GetMaterialColor(
        Material material,
        Color fallback,
        params string[] properties)
    {
        foreach (var property in properties)
        {
            if (material.HasProperty(property))
                return material.GetColor(property);
        }

        return fallback;
    }

    private static float GetMaterialFloat(
        Material material,
        float fallback,
        params string[] properties)
    {
        foreach (var property in properties)
        {
            if (material.HasProperty(property))
                return material.GetFloat(property);
        }

        return fallback;
    }

    private static void SetMaterialColor(Material material, string property, Color value)
    {
        if (material.HasProperty(property))
            material.SetColor(property, value);
    }

    private static void SetMaterialFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
            material.SetFloat(property, value);
    }

    private static void SetMaterialTexture(
        Material material,
        string property,
        Texture? texture,
        Vector2 scale,
        Vector2 offset)
    {
        if (!material.HasProperty(property))
            return;

        material.SetTexture(property, texture);
        material.SetTextureScale(property, scale);
        material.SetTextureOffset(property, offset);
    }

    private IEnumerator LogGunStoreVisualDiagnostics()
    {
        // The shelf instances become available asynchronously. Inspect once after the bounded
        // visual setup window rather than logging inside an update loop.
        yield return new WaitForSeconds(32f);

        if (context == null)
            yield break;

        var sourceMeshes = new HashSet<Mesh>();
        var bundle = AssetService.GetBundle(context.ModId, GunStoreBundleKey);
        foreach (var prefabPath in GunStoreVisualPrefabPaths.Values)
        {
            var prefab = bundle.LoadAsset<GameObject>(prefabPath);
            if (prefab == null)
                continue;

            foreach (var meshFilter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (meshFilter.sharedMesh != null)
                    sourceMeshes.Add(meshFilter.sharedMesh);
            }
        }

        var generatedMeshCount = 0;
        var orphanMeshCount = 0;
        var orphanDescriptions = new List<string>();
        foreach (var meshFilter in Resources.FindObjectsOfTypeAll<MeshFilter>())
        {
            if (meshFilter == null || !meshFilter.gameObject.scene.IsValid() ||
                !meshFilter.gameObject.scene.isLoaded || !sourceMeshes.Contains(meshFilter.sharedMesh))
            {
                continue;
            }

            if (meshFilter.GetComponentInParent<GunStoreMeshOnlyDisplayMarker>() != null)
            {
                generatedMeshCount++;
                continue;
            }

            orphanMeshCount++;
            if (orphanDescriptions.Count < 8)
            {
                orphanDescriptions.Add(
                    $"mesh='{meshFilter.sharedMesh.name}', object='{meshFilter.name}', parent='{meshFilter.transform.parent?.name ?? "<none>"}', " +
                    $"position={meshFilter.transform.position}");
            }
        }

        context.Logger.Info(
            $"Gun Store display diagnostic: generatedGunMeshes={generatedMeshCount}, orphanGunMeshes={orphanMeshCount}. " +
            $"Orphans={string.Join(" | ", orphanDescriptions)}");

        var matchingShelfStates = new List<string>();
        foreach (var shelf in Resources.FindObjectsOfTypeAll<ShelfController>())
        {
            if (shelf == null || !shelf.gameObject.scene.IsValid() || !shelf.gameObject.scene.isLoaded)
                continue;

            var owner = shelf.GetComponentInParent<ItemController>();
            var stock = owner?.ItemInstance == null ? null : ItemHelper.GetStockInstance(owner.ItemInstance);
            if (stock == null || string.IsNullOrEmpty(stock.itemName) ||
                !GunStoreVisualPrefabPaths.ContainsKey(stock.itemName))
            {
                continue;
            }

            var visualsContainer = ShelfItemsVisualsContainerField?.GetValue(shelf) as Transform;
            var slotName = stock.itemName.GetIdWithoutType();
            var existingSlot = visualsContainer?.Find(slotName);
            matchingShelfStates.Add(
                $"stock='{stock.itemName}', fixture='{owner?.Item?.itemName ?? shelf.name}', " +
                $"container={(visualsContainer != null)}, slot={(existingSlot != null)}, " +
                $"markerGeneration={existingSlot?.GetComponent<GunStoreMeshOnlyDisplayMarker>()?.Generation.ToString() ?? "<none>"}, " +
                $"position={shelf.transform.position}");
        }

        context.Logger.Info(
            $"Gun Store shelf-state diagnostic: matchingShelves={matchingShelfStates.Count}. " +
            $"States={string.Join(" | ", matchingShelfStates.Take(20))}");
    }

    private void LogGunStoreVisualSetupFailure(string itemName, string shelfName, string reason)
    {
        var key = $"{itemName}|{shelfName}|{reason}";
        if (loggedGunStoreVisualSetupFailures.Add(key))
        {
            context?.Logger.Warn(
                $"Gun Store: could not install isolated shelf visual: product='{itemName}', shelf='{shelfName}', reason={reason}.");
        }
    }

    private void RepairMalformedShelfVisualsInLoadedScenes()
    {
        if (ShelfVisualItemsField == null)
            return;

        foreach (var shelf in Resources.FindObjectsOfTypeAll<ShelfController>())
        {
            if (shelf == null || !shelf.gameObject.scene.IsValid() || !shelf.gameObject.scene.isLoaded ||
                ShelfVisualItemsField.GetValue(shelf) is not GameObject[] visualItems ||
                !visualItems.Any(visualItem => visualItem == null))
            {
                continue;
            }

            var repairedVisualItems = visualItems.Where(visualItem => visualItem != null).ToArray();
            ShelfVisualItemsField.SetValue(shelf, repairedVisualItems);

            var owner = shelf.GetComponentInParent<ItemController>();
            var stock = owner?.ItemInstance == null ? null : ItemHelper.GetStockInstance(owner.ItemInstance);
            context?.Logger.Warn(
                $"Gun Store: repaired malformed shelf visuals: shelf='{owner?.Item?.itemName ?? shelf.name}', " +
                $"stock='{stock?.itemName ?? "<none>"}', position={shelf.transform.position}, " +
                $"removedNullVisuals={visualItems.Length - repairedVisualItems.Length}. " +
                "This prevents the base game ShelfController from aborting customer purchases.");
        }
    }
}

internal static class GunStoreNpcBannerRuntime
{
    internal const string LogoShapeKey = "gunstore_npc_pistol";
    private const string BusinessTypeName = "gunstore-businesstype:businesstype_gunstore";
    private static readonly LogoSize[] BannerSizes =
    {
        LogoSize.SquareSign, LogoSize.WideSign, LogoSize.Billboard
    };
    private static readonly FieldInfo? BusinessLogosField = typeof(LogoHelper).GetField(
        "BusinessLogos", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly Type? BusinessLogoKeyType = typeof(LogoHelper).GetNestedType(
        "BusinessLogoKey", BindingFlags.NonPublic);

    private static bool TryCacheGeneratedBanner(string name, LogoSize size, Texture2D texture)
    {
        // The current game only replaces an existing key in StoreGeneratedTexture.
        // Missing AI logo files do not create that key, so seed it with the game's
        // own cache-entry type before asking storefront signs to refresh.
        if (BusinessLogosField?.GetValue(null) is not IDictionary cache || BusinessLogoKeyType == null)
            return false;

        var key = Activator.CreateInstance(BusinessLogoKeyType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new object[] { name, size, false }, null);
        if (key == null)
            return false;

        if (cache.Contains(key))
            LogoHelper.StoreGeneratedTexture(name, size, false, texture);
        else
            cache.Add(key, new BusinessLogoCacheEntry(texture, default));

        return ReferenceEquals(LogoHelper.GetBusinessLogoTexture(name, size, false), texture);
    }

    private static bool TryAddPistolToWideSign(Texture2D wideSign)
    {
        // Big Ambitions' WideSign capture contains only the business name. Its
        // square and billboard captures include the shape, so compose the same
        // existing Gun Store icon into the unused left margin of the wide sign.
        var source = BusinessTypeHelper.GetData(BusinessTypeName)?.icon?.texture;
        if (source == null || wideSign.width < wideSign.height * 3)
            return false;

        var renderTexture = RenderTexture.GetTemporary(source.width, source.height, 0,
            RenderTextureFormat.ARGB32);
        var previousActive = RenderTexture.active;
        var readableIcon = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;
            readableIcon.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readableIcon.Apply();

            var sourcePixels = readableIcon.GetPixels32();
            var signPixels = wideSign.GetPixels32();
            var minX = source.width;
            var minY = source.height;
            var maxX = -1;
            var maxY = -1;
            for (var y = 0; y < source.height; y++)
            {
                for (var x = 0; x < source.width; x++)
                {
                    if (sourcePixels[y * source.width + x].a <= 16)
                        continue;
                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            if (maxX < minX || maxY < minY)
                return false;

            // The 50x50 icon contains substantial transparent padding. Enlarge the
            // actual silhouette into the 240-pixel margin beside the name, not the
            // padded image, so it remains recognizable at normal camera distance.
            var iconWidth = Mathf.Min(220, wideSign.width / 4 - 20);
            var iconHeight = wideSign.height - 12;
            var left = 12;
            var bottom = (wideSign.height - iconHeight) / 2;
            for (var y = 0; y < iconHeight; y++)
            {
                var sourceY = minY + (y + 0.5f) * (maxY - minY + 1) / iconHeight;
                for (var x = 0; x < iconWidth; x++)
                {
                    var sourceX = minX + (x + 0.5f) * (maxX - minX + 1) / iconWidth;
                    var alpha = readableIcon.GetPixelBilinear(
                        sourceX / source.width, sourceY / source.height).a;
                    if (alpha == 0)
                        continue;

                    var index = (bottom + y) * wideSign.width + left + x;
                    var background = signPixels[index];
                    var remaining = 255 - Mathf.RoundToInt(alpha * 255f);
                    signPixels[index] = new Color32(
                        (byte)(background.r * remaining / 255),
                        (byte)(background.g * remaining / 255),
                        (byte)(background.b * remaining / 255), 255);
                }
            }

            wideSign.SetPixels32(signPixels);
            wideSign.Apply(false, false);
            return true;
        }
        finally
        {
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(renderTexture);
            UnityEngine.Object.Destroy(readableIcon);
        }
    }

    internal static void Prime(ModContext context)
    {
        var addedShape = !LogoHelper.LogoShapeSprites.ContainsKey(LogoShapeKey);
        if (addedShape)
        {
            var icon = BusinessTypeHelper.GetData(BusinessTypeName)?.icon;
            if (icon == null)
            {
                context.Logger.Warn("Gun Store: NPC banner pistol icon was not available.");
                return;
            }

            LogoHelper.LogoShapeSprites[LogoShapeKey] = Sprite.Create(
                icon.texture, icon.rect, new Vector2(0.5f, 0.5f), icon.pixelsPerUnit);
        }

        if (addedShape)
            context.Logger.Info(
                $"Gun Store: registered storefront pistol logo shape for {GunStoreBusinessTypeCityMod.AiRivalBusinessNames.Count} NPC names.");
    }

    internal static IEnumerator Generate(ModContext context)
    {
        // The game's late-loaded callback can precede this mod's city-load entry point.
        // Wait for the NPC defaults and logo generator instead of permanently abandoning signs.
        var ready = false;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (BusinessLogoGenerator.Instance != null &&
                CompetitionHelper.GetBusinessDefault(GunStoreBusinessTypeCityMod.AiRivalBusinessNames[0]) != null)
            {
                Prime(context);
                ready = LogoHelper.LogoShapeSprites.ContainsKey(LogoShapeKey);
                if (ready)
                    break;
            }

            yield return new WaitForSeconds(0.5f);
        }

        if (!ready)
        {
            context.Logger.Warn("Gun Store: NPC banners could not be generated within 30 seconds: " +
                                $"generatorReady={BusinessLogoGenerator.Instance != null}, " +
                                $"logoShapeReady={LogoHelper.LogoShapeSprites.ContainsKey(LogoShapeKey)}.");
            yield break;
        }

        foreach (var name in GunStoreBusinessTypeCityMod.AiRivalBusinessNames)
        {
            var settings = CompetitionHelper.GetBusinessDefault(name)?.logoSettings?.Clone()
                ?? new LogoSettings();
            settings.logoShape = LogoShapeKey;
            var path = Path.Combine(Application.persistentDataPath, "GunStoreAiLogos",
                LogoHelper.GetBusinessNamePathSafe(name));
            var completed = false;
            BusinessLogoGenerator.Create(name, settings, path, false, () =>
            {
                completed = true;
                try
                {
                    var cachedCount = 0;
                    foreach (var size in BannerSizes)
                    {
                        var file = Path.Combine(path, size + ".jpg");
                        if (!File.Exists(file))
                        {
                            context.Logger.Warn($"Gun Store: generated banner file missing: business='{name}', size={size}.");
                            continue;
                        }

                        var texture = new Texture2D(2, 2);
                        if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(file)))
                        {
                            UnityEngine.Object.Destroy(texture);
                            context.Logger.Warn($"Gun Store: generated banner image invalid: business='{name}', size={size}.");
                            continue;
                        }

                        if (size == LogoSize.WideSign && !TryAddPistolToWideSign(texture))
                        {
                            UnityEngine.Object.Destroy(texture);
                            context.Logger.Warn($"Gun Store: could not add the pistol icon to wide banner for '{name}'.");
                            continue;
                        }
                        if (size == LogoSize.WideSign)
                            context.Logger.Info($"Gun Store: enlarged pistol silhouette on wide banner for '{name}'.");

                        if (!TryCacheGeneratedBanner(name, size, texture))
                        {
                            UnityEngine.Object.Destroy(texture);
                            context.Logger.Warn($"Gun Store: could not register generated banner in game logo cache: business='{name}', size={size}.");
                        }
                        else
                        {
                            cachedCount++;
                        }
                    }

                    var wideSign = LogoHelper.GetBusinessLogoTexture(name, LogoSize.WideSign, false);
                    var success = cachedCount == BannerSizes.Length &&
                                  wideSign != null && wideSign != LogoHelper.GetNullTexture();
                    if (!success)
                    {
                        context.Logger.Warn($"Gun Store: storefront banner generation incomplete for '{name}': cached={cachedCount}/{BannerSizes.Length}.");
                        return;
                    }

                    var refreshedSigns = 0;
                    foreach (var registration in SaveGameManager.Current?.BuildingRegistrations?.AsEnumerable()
                                 ?? Enumerable.Empty<BuildingRegistration>())
                    {
                        if (registration == null || registration.RentedByPlayer ||
                            !string.Equals(registration.BusinessName, name, StringComparison.Ordinal))
                            continue;

                        var building = InstanceBehavior<CityManager>.Instance?
                            .FindCityBuildingController(registration.Address);
                        if (building == null)
                            continue;
                        building.UpdateSign();
                        refreshedSigns++;
                    }

                    context.Logger.Info($"Gun Store: cached {cachedCount} NPC banner sizes for '{name}' and refreshed {refreshedSigns} loaded storefront sign(s).");
                }
                catch (Exception exception)
                {
                    context.Logger.Warn($"Gun Store: failed to refresh storefront banner for '{name}': {exception.Message}");
                }
            });

            for (var attempt = 0; !completed && attempt < 40; attempt++)
                yield return new WaitForSeconds(0.25f);
            if (!completed)
                context.Logger.Warn($"Gun Store: storefront banner generation timed out for '{name}'.");
        }
    }
}

// Runtime-only marker that makes the one-time shelf-display migration idempotent across scene
// reloads while still allowing a newer implementation to replace older generated slots.
internal sealed class GunStoreMeshOnlyDisplayMarker : MonoBehaviour
{
    public int Generation;
}

// Attached to the loaded vanilla fixture prefabs, so each future interior gets an
// initialization callback even when the game does not emit a building-entry event.
[DefaultExecutionOrder(10001)]
internal sealed class GunStoreShelfLifecycleHook : MonoBehaviour
{
    private IEnumerator Start()
    {
        var shelf = GetComponent<ShelfController>();
        if (shelf == null)
            yield break;

        // ShelfController.Start selects its stock after Awake. Give saved cargo and NPC
        // purchaser settings a few bounded chances to arrive, without a permanent scan.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            yield return attempt == 0 ? null : new WaitForSeconds(attempt == 1 ? 0.1f : 0.5f);
            GunStoreHelpDebugRuntime.Active?.InstallShelfVisualOnInitialization(shelf);
        }
    }
}

// React to the game's shelf cargo/visibility lifecycle after the bounded scene setup
// has ended. This keeps virtual-stock NPC displays visible without global polling.
internal sealed class GunStoreNpcShelfVisualObserver : MonoBehaviour
{
    private GunStoreHelpDebugRuntime? runtime;
    private ShelfController? shelf;
    private ItemController? owner;
    private Transform? visualSlot;
    private string? itemName;
    private ItemInstance? itemInstance;
    private Coroutine? pendingRefresh;

    internal void Initialize(
        GunStoreHelpDebugRuntime runtime,
        ShelfController shelf,
        ItemController owner,
        Transform visualSlot,
        string itemName)
    {
        if (ReferenceEquals(this.runtime, runtime) && ReferenceEquals(this.shelf, shelf) &&
            ReferenceEquals(this.owner, owner) && ReferenceEquals(this.visualSlot, visualSlot) &&
            string.Equals(this.itemName, itemName, StringComparison.Ordinal))
        {
            BindItemInstance();
            return;
        }

        Unbind();
        this.runtime = runtime;
        this.shelf = shelf;
        this.owner = owner;
        this.visualSlot = visualSlot;
        this.itemName = itemName;
        owner.onItemInitialized?.AddListener(HandleItemInitialized);
        BindItemInstance();
    }

    private void OnEnable()
    {
        QueueRefresh();
    }

    private void OnDisable()
    {
        if (pendingRefresh != null)
            StopCoroutine(pendingRefresh);
        pendingRefresh = null;
    }

    private void OnDestroy()
    {
        Unbind();
    }

    private void HandleItemInitialized()
    {
        BindItemInstance();
        QueueRefresh();
    }

    private void HandleCargoUpdated()
    {
        QueueRefresh();
    }

    private void BindItemInstance()
    {
        var current = owner?.ItemInstance;
        if (ReferenceEquals(itemInstance, current))
            return;

        itemInstance?.RemoveCallFromOnItemsInCargoUpdated(HandleCargoUpdated);
        itemInstance = current;
        itemInstance?.AddCallToOnItemsInCargoUpdated(HandleCargoUpdated);
    }

    private void QueueRefresh()
    {
        if (!isActiveAndEnabled || runtime == null || shelf == null || visualSlot == null ||
            string.IsNullOrEmpty(itemName) || pendingRefresh != null)
            return;

        pendingRefresh = StartCoroutine(RefreshNextFrame());
    }

    private IEnumerator RefreshNextFrame()
    {
        yield return null;
        pendingRefresh = null;
        if (runtime != null && shelf != null && visualSlot != null && itemName != null)
            runtime.EnsureNpcShelfDisplayVisible(shelf, owner, visualSlot, itemName);
    }

    private void Unbind()
    {
        itemInstance?.RemoveCallFromOnItemsInCargoUpdated(HandleCargoUpdated);
        itemInstance = null;
        owner?.onItemInitialized?.RemoveListener(HandleItemInitialized);
    }
}

internal enum GunStoreHelpNavigationPatchResult
{
    HelpSystemNotReady,
    Applied,
    AlreadyPresent
}

internal sealed class GunStoreHelpInitializationObserver : MonoBehaviour
{
    private GunStoreHelpDebugRuntime? runtime;
    private UnityEvent<string>? currentSlugChanged;
    private bool firstPageOpenHandled;

    public void Initialize(
        GunStoreHelpDebugRuntime runtime,
        UnityEvent<string>? currentSlugChanged)
    {
        this.runtime = runtime;

        if (ReferenceEquals(this.currentSlugChanged, currentSlugChanged))
            return;

        this.currentSlugChanged?.RemoveListener(HandleCurrentSlugChanged);
        this.currentSlugChanged = currentSlugChanged;
        this.currentSlugChanged?.AddListener(HandleCurrentSlugChanged);
#if GUN_STORE_HELP_UI_DEBUG
        GunStoreHelpDebugLogger.Trace(
            $"HelpSystem observer initialized: eventAvailable={currentSlugChanged != null}, " +
            $"frame={Time.frameCount}.");
#endif
    }

    private void HandleCurrentSlugChanged(string slug)
    {
        if (firstPageOpenHandled)
            return;

        firstPageOpenHandled = true;
#if GUN_STORE_HELP_UI_DEBUG
        GunStoreHelpDebugLogger.Trace(
            $"HelpSystem first page opened: slug={slug}, frame={Time.frameCount}; " +
            "scheduling post-initialization navigation patch.");
#endif
        runtime?.ScheduleNavigationPatch(
            forceRefresh: true,
            reason: $"help-first-page-opened:{slug}");
    }

    private void OnDestroy()
    {
        currentSlugChanged?.RemoveListener(HandleCurrentSlugChanged);
        currentSlugChanged = null;
        runtime = null;
    }
}

internal static class GunStoreHelpNavigationPatch
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly IReadOnlyDictionary<string, NavigationPage[]> NavigationPagesByCategory =
        new Dictionary<string, NavigationPage[]>(StringComparer.Ordinal)
        {
            ["common_business_types"] = new[]
            {
                new NavigationPage(
                    "businesstypes-gunstore",
                    "businesstypes-gunstore")
            },
            ["common_sellable_products"] = new[]
            {
                ProductPage("itemname_ak47"),
                ProductPage("itemname_ammosmall"),
                ProductPage("itemname_wincheatersxp"),
                ProductPage("itemname_berettam9"),
                ProductPage("itemname_ammolarge"),
                ProductPage("itemname_rpg")
            },
            ["common_factory_ingredients"] = new[]
            {
                ProductPage("itemname_gunpartscheap"),
                ProductPage("itemname_gunpartsexpensive")
            }
        };

    public static GunStoreHelpNavigationPatchResult TryApply(
        MonoBehaviour coroutineHost,
        bool forceRefresh = false)
    {
        var foundTargetCategory = false;
        var applied = false;
        var helpSystemCount = 0;
        var behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
#if GUN_STORE_HELP_UI_DEBUG
        GunStoreHelpDebugLogger.Trace(
            $"TryApply begin: force={forceRefresh}, behaviours={behaviours.Length}, " +
            $"scene={SceneManager.GetActiveScene().name}, frame={Time.frameCount}.");
#endif

        foreach (var helpSystem in behaviours)
        {
            if (helpSystem == null || !helpSystem.gameObject.scene.IsValid())
                continue;

            var helpSystemType = helpSystem.GetType();
            if (!string.Equals(helpSystemType.FullName, "UnityEngine.UI.Extensions.HelpSystem.HelpSystem",
                    StringComparison.Ordinal) &&
                !string.Equals(helpSystemType.Name, "HelpSystem", StringComparison.Ordinal))
            {
                continue;
            }

            helpSystemCount++;
#if GUN_STORE_HELP_UI_DEBUG
            GunStoreHelpDebugLogger.Trace(
                $"HelpSystem found: type={helpSystemType.AssemblyQualifiedName}, " +
                $"scene={helpSystem.gameObject.scene.name}, activeSelf={helpSystem.gameObject.activeSelf}, " +
                $"activeHierarchy={helpSystem.gameObject.activeInHierarchy}, enabled={helpSystem.enabled}.");
#endif

            if (coroutineHost is GunStoreHelpDebugRuntime runtime)
            {
                var currentSlugChanged = GetField(helpSystemType, "currentSlugChanged")
                    ?.GetValue(helpSystem) as UnityEvent<string>;
                var observer = helpSystem.GetComponent<GunStoreHelpInitializationObserver>() ??
                               helpSystem.gameObject.AddComponent<GunStoreHelpInitializationObserver>();
                observer.Initialize(runtime, currentSlugChanged);
            }

            var categoriesField = GetField(helpSystemType, "_categories");
            if (categoriesField?.GetValue(helpSystem) is not IEnumerable categories)
            {
#if GUN_STORE_HELP_UI_DEBUG
                GunStoreHelpDebugLogger.Trace(
                    $"HelpSystem categories unavailable: fieldFound={categoriesField != null}, " +
                    $"valueType={categoriesField?.GetValue(helpSystem)?.GetType().FullName ?? "<null>"}.");
#endif
                continue;
            }

#if GUN_STORE_HELP_UI_DEBUG
            var categoryObjects = categories.Cast<object?>().Where(category => category != null).ToArray();
            GunStoreHelpDebugLogger.Trace(
                $"HelpSystem categories ready: count={categoryObjects.Length}, keys=[" +
                string.Join(", ", categoryObjects.Select(category =>
                    GetField(category!.GetType(), "CategoryLocalizorKey")?.GetValue(category) as string ?? "<null>")) +
                "].");
#endif

            var helpSystemChanged = false;
            foreach (var category in categories)
            {
                if (category == null)
                    continue;

                var categoryType = category.GetType();
                var categoryKey = GetField(categoryType, "CategoryLocalizorKey")?.GetValue(category) as string;
                if (categoryKey == null ||
                    !NavigationPagesByCategory.TryGetValue(categoryKey, out var desiredSlugs))
                {
                    continue;
                }

                var pagesField = GetField(categoryType, "Pages");
                if (pagesField?.GetValue(category) is not IList pages)
                    continue;

                foundTargetCategory = true;
#if GUN_STORE_HELP_UI_DEBUG
                GunStoreHelpDebugLogger.Trace(
                    $"Target category found: key={categoryKey}, currentPages={pages.Count}, " +
                    $"desiredPages={desiredSlugs.Length}.");
#endif
                var pageType = GetListElementType(pages.GetType()) ??
                               pages.Cast<object?>().FirstOrDefault(page => page != null)?.GetType();
                if (pageType == null)
                    continue;

                var slugField = GetField(pageType, "Slug");
                var pagePrefixField = GetField(pageType, "PageLocalizorKeyPrefix");
                if (slugField == null || pagePrefixField == null)
                    continue;

                foreach (var desiredPage in desiredSlugs)
                {
                    var existingPage = pages.Cast<object?>()
                        .FirstOrDefault(page =>
                            page != null &&
                            string.Equals(
                                GetField(page.GetType(), "Slug")?.GetValue(page) as string,
                                desiredPage.Slug,
                                StringComparison.Ordinal));
                    if (existingPage != null)
                    {
                        var prefixField = GetField(existingPage.GetType(), "PageLocalizorKeyPrefix");
                        var currentPrefix = prefixField?.GetValue(existingPage) as string;
                        if (!string.Equals(currentPrefix, desiredPage.LocalizorKey, StringComparison.Ordinal))
                        {
                            prefixField?.SetValue(existingPage, desiredPage.LocalizorKey);
                            helpSystemChanged = true;
#if GUN_STORE_HELP_UI_DEBUG
                            GunStoreHelpDebugLogger.Trace(
                                $"Updated page prefix: slug={desiredPage.Slug}, " +
                                $"old={currentPrefix ?? "<null>"}, new={desiredPage.LocalizorKey}.");
#endif
                        }

                        continue;
                    }

                    var newPage = Activator.CreateInstance(pageType);
                    if (newPage == null)
                        continue;

                    slugField.SetValue(newPage, desiredPage.Slug);
                    pagePrefixField.SetValue(newPage, desiredPage.LocalizorKey);
                    pages.Add(newPage);
                    helpSystemChanged = true;
#if GUN_STORE_HELP_UI_DEBUG
                    GunStoreHelpDebugLogger.Trace(
                        $"Added page definition: category={categoryKey}, slug={desiredPage.Slug}, " +
                        $"prefix={desiredPage.LocalizorKey}, newPageCount={pages.Count}.");
#endif
                }
            }

            if (helpSystemChanged || (forceRefresh && foundTargetCategory))
            {
#if GUN_STORE_HELP_UI_DEBUG
                GunStoreHelpDebugLogger.Trace(
                    $"Navigation refresh requested: changed={helpSystemChanged}, force={forceRefresh}, " +
                    $"foundTargetCategory={foundTargetCategory}.");
#endif
                var navigationRefreshed = RefreshGeneratedNavigation(
                    helpSystem,
                    helpSystemType,
                    coroutineHost);
                applied |= helpSystemChanged || navigationRefreshed;
            }
        }

        if (applied)
        {
#if GUN_STORE_HELP_UI_DEBUG
            GunStoreHelpDebugLogger.Trace(
                $"TryApply end: Applied; helpSystems={helpSystemCount}, " +
                $"foundTargetCategory={foundTargetCategory}.");
#endif
            return GunStoreHelpNavigationPatchResult.Applied;
        }

        var result = foundTargetCategory
            ? GunStoreHelpNavigationPatchResult.AlreadyPresent
            : GunStoreHelpNavigationPatchResult.HelpSystemNotReady;
#if GUN_STORE_HELP_UI_DEBUG
        GunStoreHelpDebugLogger.Trace(
            $"TryApply end: {result}; helpSystems={helpSystemCount}, " +
            $"foundTargetCategory={foundTargetCategory}.");
#endif
        return result;
    }

    private static bool RefreshGeneratedNavigation(
        MonoBehaviour helpSystem,
        Type helpSystemType,
        MonoBehaviour coroutineHost)
    {
        var generatedField = GetField(helpSystemType, "_generatedHelpCategories");
        if (generatedField?.GetValue(helpSystem) is not IList generated || generated.Count == 0)
        {
#if GUN_STORE_HELP_UI_DEBUG
            GunStoreHelpDebugLogger.Trace(
                $"Generated navigation not ready: fieldFound={generatedField != null}, " +
                $"count={(generatedField?.GetValue(helpSystem) as IList)?.Count ?? -1}.");
#endif
            return false;
        }

        var loadCategories = helpSystemType.GetMethods(InstanceFlags)
            .FirstOrDefault(method =>
                string.Equals(method.Name, "LoadCategories", StringComparison.Ordinal) &&
                method.GetParameters().Length == 0);
        if (loadCategories == null)
        {
#if GUN_STORE_HELP_UI_DEBUG
            GunStoreHelpDebugLogger.Trace("LoadCategories() method was not found.");
#endif
            return false;
        }

        var openCategoryIndexes = generated.Cast<object?>()
            .Select((category, index) => new
            {
                Index = index,
                IsOpen = category != null &&
                         GetField(category.GetType(), "_isOpen")?.GetValue(category) is true
            })
            .Where(category => category.IsOpen)
            .Select(category => category.Index)
            .ToArray();

#if GUN_STORE_HELP_UI_DEBUG
        GunStoreHelpDebugLogger.Trace(
            $"Rebuilding generated navigation: oldCategoryCount={generated.Count}, " +
            $"openCategoryIndexes=[{string.Join(",", openCategoryIndexes)}], " +
            $"method={loadCategories.DeclaringType?.FullName}.{loadCategories.Name}.");
#endif

        foreach (var generatedEntry in generated.Cast<object?>().ToArray())
        {
            if (generatedEntry is Component component)
                UnityEngine.Object.Destroy(component.gameObject);
            else if (generatedEntry is UnityEngine.Object unityObject)
                UnityEngine.Object.Destroy(unityObject);
        }

        generated.Clear();

        // Object.Destroy is deferred until the end of the frame. Let the old native
        // category objects disappear before LoadCategories creates their replacements.
        coroutineHost.StartCoroutine(ReloadGeneratedNavigationNextFrame(
            helpSystem,
            loadCategories,
            openCategoryIndexes));

        return true;
    }

    private static IEnumerator ReloadGeneratedNavigationNextFrame(
        MonoBehaviour helpSystem,
        MethodInfo loadCategories,
        IReadOnlyCollection<int> openCategoryIndexes)
    {
        yield return null;

#if GUN_STORE_HELP_UI_DEBUG
        GunStoreHelpDebugLogger.Trace(
            $"Invoking native LoadCategories after deferred destruction; frame={Time.frameCount}.");
#endif

        var returnValue = loadCategories.Invoke(helpSystem, null);
        if (returnValue is IEnumerator enumerator)
            yield return enumerator;

        var generatedField = GetField(helpSystem.GetType(), "_generatedHelpCategories");
        if (generatedField?.GetValue(helpSystem) is not IList regeneratedCategories)
            yield break;

        foreach (var index in openCategoryIndexes)
        {
            if (index < 0 || index >= regeneratedCategories.Count)
                continue;

            var category = regeneratedCategories[index];
            var setOpenState = category?.GetType().GetMethod(
                "SetOpenState",
                InstanceFlags,
                null,
                new[] { typeof(bool) },
                null);
            setOpenState?.Invoke(category, new object[] { true });
        }

#if GUN_STORE_HELP_UI_DEBUG
        GunStoreHelpDebugLogger.Trace(
            $"Native navigation rebuilt: categoryCount={regeneratedCategories.Count}, " +
            $"restoredOpenCategoryCount={openCategoryIndexes.Count}.");
#endif
    }

    private static Type? GetListElementType(Type listType)
    {
        if (listType.IsGenericType)
            return listType.GetGenericArguments().FirstOrDefault();

        return listType.GetInterfaces()
            .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>))
            ?.GetGenericArguments()
            .FirstOrDefault();
    }

    private static FieldInfo? GetField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }

        return null;
    }

    private static NavigationPage ProductPage(string itemLocalizorKey)
    {
        var slug = $"products-gunstore-businesstype:{itemLocalizorKey}";
        return new NavigationPage(
            slug,
            slug);
    }

    private readonly struct NavigationPage
    {
        public NavigationPage(string slug, string localizorKey)
        {
            Slug = slug;
            LocalizorKey = localizorKey;
        }

        public string Slug { get; }
        public string LocalizorKey { get; }
    }
}

#if GUN_STORE_HELP_UI_DEBUG
internal static class GunStoreHelpDebugLogger
{
    private const string PreferredLogDirectory =
        @"E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Logs\Mods";

    private static readonly object Sync = new object();
    private static readonly string ResolvedLogDirectory = ResolveLogDirectory();

    public static string LogPath => Path.Combine(ResolvedLogDirectory, "GunStore-help-ui-debug.log");

    public static void StartSession()
    {
        Append($"===== Gun Store Help UI debug session started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====");
        Append("Open the native Help System and press F9 to capture its hierarchy and navigation data.");
    }

    public static void AppendSnapshot(StringBuilder snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        Append(snapshot.ToString());
    }

    public static void Trace(string message)
    {
        Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] TRACE {message}");
    }

    public static void Error(string message, Exception exception)
    {
        Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ERROR {message}{Environment.NewLine}{exception}");
    }

    private static void Append(string message)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath) ?? ResolvedLogDirectory);
            File.AppendAllText(LogPath, message + Environment.NewLine, Encoding.UTF8);
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
            var fallback = Path.Combine(Path.GetTempPath(), "GunStore", "Logs");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}

internal readonly struct GunStoreHelpDebugSnapshotResult
{
    public GunStoreHelpDebugSnapshotResult(string logPath, int helpComponentCount, int rootCount, int elementCount)
    {
        LogPath = logPath;
        HelpComponentCount = helpComponentCount;
        RootCount = rootCount;
        ElementCount = elementCount;
    }

    public string LogPath { get; }
    public int HelpComponentCount { get; }
    public int RootCount { get; }
    public int ElementCount { get; }
}

internal static class GunStoreHelpDebugSnapshotWriter
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MaximumHierarchyElements = 2000;
    private const int MaximumCollectionEntries = 150;
    private const int MaximumObjectDepth = 6;

    public static GunStoreHelpDebugSnapshotResult WriteSnapshot()
    {
        var builder = new StringBuilder(128 * 1024);
        var helpComponents = FindHelpComponents();
        var roots = FindHelpUiRoots(helpComponents);
        var elementCount = 0;

        builder.AppendLine("============================================================");
        builder.AppendLine($"Gun Store Help UI snapshot: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        builder.AppendLine($"Scene: {SceneManager.GetActiveScene().name}");
        builder.AppendLine($"Screen: {Screen.width}x{Screen.height}");
        AppendEventSystem(builder);
        builder.AppendLine($"Help-like components: {helpComponents.Count}");

        foreach (var component in helpComponents)
        {
            builder.AppendLine();
            builder.AppendLine(
                $"HELP COMPONENT type={component.GetType().AssemblyQualifiedName} " +
                $"path={GetHierarchyPath(component.transform)} active={component.gameObject.activeInHierarchy} " +
                $"enabled={component.enabled}");
            AppendRootObjectMembers(builder, component);
        }

        builder.AppendLine();
        AppendHelpRuntimeTypeSummary(builder);

        builder.AppendLine();
        builder.AppendLine($"HELP UI ROOTS: {roots.Count}");
        foreach (var root in roots)
        {
            builder.AppendLine();
            builder.AppendLine($"ROOT {GetHierarchyPath(root)}");
            AppendHierarchy(root, builder, 0, ref elementCount);
        }

        builder.AppendLine();
        builder.AppendLine(
            $"END snapshot: helpComponents={helpComponents.Count}, roots={roots.Count}, elements={elementCount}");
        GunStoreHelpDebugLogger.AppendSnapshot(builder);
        return new GunStoreHelpDebugSnapshotResult(
            GunStoreHelpDebugLogger.LogPath,
            helpComponents.Count,
            roots.Count,
            elementCount);
    }

    private static List<MonoBehaviour> FindHelpComponents()
    {
        return Resources.FindObjectsOfTypeAll<MonoBehaviour>()
            .Where(component => component != null && component.gameObject.scene.IsValid())
            .Where(component =>
            {
                var typeName = component.GetType().FullName ?? component.GetType().Name;
                return typeName.IndexOf("HelpSystem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       typeName.IndexOf("HelpStructure", StringComparison.OrdinalIgnoreCase) >= 0;
            })
            .OrderBy(component => GetHierarchyPath(component.transform), StringComparer.Ordinal)
            .ToList();
    }

    private static List<Transform> FindHelpUiRoots(IReadOnlyList<MonoBehaviour> helpComponents)
    {
        var roots = new Dictionary<int, Transform>();

        foreach (var component in helpComponents)
            AddUiRoot(roots, component.transform);

        if (roots.Count > 0)
        {
            return roots.Values
                .OrderBy(GetHierarchyPath, StringComparer.Ordinal)
                .ToList();
        }

        foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (transform == null || !transform.gameObject.scene.IsValid())
                continue;

            if (transform.name.IndexOf("Help", StringComparison.OrdinalIgnoreCase) >= 0)
                AddUiRoot(roots, transform);
        }

        foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (behaviour == null || !behaviour.gameObject.scene.IsValid())
                continue;

            var text = TryReadText(behaviour);
            if (text == null)
                continue;

            if (text.IndexOf("Help System", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Gun Store", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddUiRoot(roots, behaviour.transform);
            }
        }

        return roots.Values
            .OrderBy(GetHierarchyPath, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddUiRoot(IDictionary<int, Transform> roots, Transform candidate)
    {
        var canvas = candidate.GetComponentInParent<Canvas>(true);
        var root = canvas != null ? canvas.rootCanvas.transform : candidate.root;
        roots[root.GetInstanceID()] = root;
    }

    private static void AppendEventSystem(StringBuilder builder)
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            builder.AppendLine("EventSystem: <none>");
            return;
        }

        var selected = eventSystem.currentSelectedGameObject;
        builder.AppendLine(
            $"EventSystem: {GetHierarchyPath(eventSystem.transform)}, " +
            $"selected={(selected == null ? "<none>" : GetHierarchyPath(selected.transform))}");
    }

    private static void AppendRootObjectMembers(StringBuilder builder, object root)
    {
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        AppendObjectMembers(builder, root, 1, visited);
    }

    private static void AppendObjectMembers(
        StringBuilder builder,
        object value,
        int depth,
        ISet<object> visited)
    {
        if (depth > MaximumObjectDepth || !visited.Add(value))
            return;

        var type = value.GetType();
        foreach (var field in GetAllFields(type).OrderBy(field => field.Name, StringComparer.Ordinal))
        {
            object? fieldValue;
            try
            {
                fieldValue = field.GetValue(value);
            }
            catch (Exception exception)
            {
                AppendIndent(builder, depth);
                builder.AppendLine($"FIELD {field.Name}: <error {exception.GetType().Name}: {exception.Message}>");
                continue;
            }

            AppendValue(builder, $"FIELD {field.DeclaringType?.Name}.{field.Name}", fieldValue, depth, visited);
        }

        foreach (var property in type.GetProperties(InstanceFlags)
                     .Where(property => property.GetIndexParameters().Length == 0)
                     .Where(property => IsSimpleType(property.PropertyType))
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            try
            {
                AppendValue(builder, $"PROPERTY {property.Name}", property.GetValue(value), depth, visited);
            }
            catch (Exception exception)
            {
                AppendIndent(builder, depth);
                builder.AppendLine($"PROPERTY {property.Name}: <error {exception.GetType().Name}: {exception.Message}>");
            }
        }
    }

    private static void AppendValue(
        StringBuilder builder,
        string label,
        object? value,
        int depth,
        ISet<object> visited)
    {
        AppendIndent(builder, depth);
        if (value == null)
        {
            builder.AppendLine($"{label}: <null>");
            return;
        }

        var type = value.GetType();
        if (IsSimpleType(type))
        {
            builder.AppendLine($"{label}: {FormatSimpleValue(value)}");
            return;
        }

        if (value is UnityEngine.Object unityObject)
        {
            builder.AppendLine(
                $"{label}: unityObject type={type.FullName} name={unityObject.name} id={unityObject.GetInstanceID()}");
            if (depth < MaximumObjectDepth &&
                unityObject is Component &&
                type.Name.IndexOf("HelpCategory", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AppendObjectMembers(builder, value, depth + 1, visited);
            }

            return;
        }

        if (value is IDictionary dictionary)
        {
            builder.AppendLine($"{label}: dictionary type={type.FullName} count={dictionary.Count}");
            var index = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (index >= MaximumCollectionEntries)
                {
                    AppendIndent(builder, depth + 1);
                    builder.AppendLine("<collection truncated>");
                    break;
                }

                AppendValue(builder, $"KEY[{index}]", entry.Key, depth + 1, visited);
                AppendValue(builder, $"VALUE[{index}]", entry.Value, depth + 1, visited);
                index++;
            }

            return;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            builder.AppendLine($"{label}: collection type={type.FullName}");
            var index = 0;
            foreach (var entry in enumerable)
            {
                if (index >= MaximumCollectionEntries)
                {
                    AppendIndent(builder, depth + 1);
                    builder.AppendLine("<collection truncated>");
                    break;
                }

                AppendValue(builder, $"[{index}]", entry, depth + 1, visited);
                index++;
            }

            if (index == 0)
            {
                AppendIndent(builder, depth + 1);
                builder.AppendLine("<empty>");
            }

            return;
        }

        builder.AppendLine($"{label}: object type={type.AssemblyQualifiedName}");
        if (depth < MaximumObjectDepth && ShouldExpand(type))
            AppendObjectMembers(builder, value, depth + 1, visited);
    }

    private static void AppendHelpRuntimeTypeSummary(StringBuilder builder)
    {
        builder.AppendLine("HELP RUNTIME TYPE SUMMARY");
        foreach (var type in GetLoadableTypes()
                     .Where(type =>
                     {
                         var name = type.FullName ?? type.Name;
                         return name.Equals("HelpSystem", StringComparison.Ordinal) ||
                                name.IndexOf("HelpStructure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("HelpCategoryEntry", StringComparison.OrdinalIgnoreCase) >= 0;
                     })
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            builder.AppendLine($"TYPE {type.AssemblyQualifiedName}");
            foreach (var field in type.GetFields(StaticFlags | InstanceFlags)
                         .OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                builder.AppendLine(
                    $"  {(field.IsStatic ? "STATIC" : "INSTANCE")} FIELD " +
                    $"{field.FieldType.FullName} {field.Name}");
                if (!field.IsStatic)
                    continue;

                try
                {
                    var value = field.GetValue(null);
                    builder.AppendLine($"    value={FormatSummaryValue(value)}");
                }
                catch (Exception exception)
                {
                    builder.AppendLine($"    value=<error {exception.GetType().Name}: {exception.Message}>");
                }
            }
        }
    }

    private static IEnumerable<Type> GetLoadableTypes()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type != null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
                yield return type;
        }
    }

    private static IEnumerable<FieldInfo> GetAllFields(Type type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var field in current.GetFields(InstanceFlags | BindingFlags.DeclaredOnly))
                yield return field;
        }
    }

    private static void AppendHierarchy(Transform transform, StringBuilder builder, int depth, ref int elementCount)
    {
        if (elementCount >= MaximumHierarchyElements)
            return;

        elementCount++;
        AppendIndent(builder, depth);
        builder.Append("- ");
        builder.Append(transform.name);
        builder.Append($" activeSelf={transform.gameObject.activeSelf} activeHierarchy={transform.gameObject.activeInHierarchy}");
        builder.Append(" components=[");
        builder.Append(string.Join(", ", transform.gameObject.GetComponents<Component>()
            .Where(component => component != null)
            .Select(component => component.GetType().FullName ?? component.GetType().Name)
            .ToArray()));
        builder.Append(']');

        if (transform is RectTransform rectTransform)
        {
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            var canvas = rectTransform.GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var minimum = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var maximum = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            builder.Append($" rect=({minimum.x:0.#},{minimum.y:0.#})-({maximum.x:0.#},{maximum.y:0.#})");
        }

        var text = TryGetText(transform.gameObject);
        if (!string.IsNullOrWhiteSpace(text))
            builder.Append($" text=\"{Escape(text!)}\"");

        builder.AppendLine();
        for (var index = 0; index < transform.childCount; index++)
            AppendHierarchy(transform.GetChild(index), builder, depth + 1, ref elementCount);
    }

    private static string? TryGetText(GameObject gameObject)
    {
        foreach (var component in gameObject.GetComponents<Component>())
        {
            var text = TryReadText(component);
            if (!string.IsNullOrWhiteSpace(text))
                return text!.Trim();
        }

        return null;
    }

    private static string? TryReadText(Component component)
    {
        var type = component.GetType();
        if (type.Name.IndexOf("Text", StringComparison.OrdinalIgnoreCase) < 0 &&
            type.Name.IndexOf("InputField", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }

        try
        {
            return type.GetProperty("text", InstanceFlags)?.GetValue(component) as string;
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldExpand(Type type)
    {
        var name = type.FullName ?? type.Name;
        return name.IndexOf("Help", StringComparison.OrdinalIgnoreCase) >= 0 ||
               (type.Namespace != null &&
                !type.Namespace.StartsWith("System", StringComparison.Ordinal) &&
                !type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal));
    }

    private static bool IsSimpleType(Type type)
    {
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
               type == typeof(DateTime) || type == typeof(Guid) || type == typeof(Type);
    }

    private static string FormatSimpleValue(object value)
    {
        return value is string text ? $"\"{Escape(text)}\"" : value.ToString() ?? "<null>";
    }

    private static string FormatSummaryValue(object? value)
    {
        if (value == null)
            return "<null>";
        if (IsSimpleType(value.GetType()))
            return FormatSimpleValue(value);
        if (value is ICollection collection)
            return $"{value.GetType().FullName} count={collection.Count}";
        return value.GetType().FullName ?? value.GetType().Name;
    }

    private static void AppendIndent(StringBuilder builder, int depth)
    {
        builder.Append(' ', depth * 2);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var names = new List<string>();
        var current = transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names.ToArray());
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();

        public new bool Equals(object? left, object? right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(object value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }
}
#endif
