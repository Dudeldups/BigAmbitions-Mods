#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using Buildings;
using Helpers;
using UnityEngine;

[assembly: RegisterModClass(typeof(GunStoreBusinessTypeMod))]
[assembly: RegisterModClass(typeof(GunStoreBusinessTypeCityMod))]

[ModEntryOnInitializationLoad]
public class GunStoreBusinessTypeMod : IModBigAmbitions
{
    private const string BundleKey = "AssetBundles/gunstore-businesstype.unity3d";
    private const string BusinessTypeAssetPath = "Assets/Mods/Gun Store/GunStore.asset";
    private static readonly string[] ItemAssetPaths =
    {
        "Assets/Mods/Gun Store/Ak47.asset",
        "Assets/Mods/Gun Store/AmmoSmall.asset",
        "Assets/Mods/Gun Store/WinCheaterSxp.asset",
        "Assets/Mods/Gun Store/BerettaM9.asset",
        "Assets/Mods/Gun Store/AmmoLarge.asset",
        "Assets/Mods/Gun Store/Rpg.asset",
        "Assets/Mods/Gun Store/GunPartsCheap.asset",
        "Assets/Mods/Gun Store/GunPartsExpensive.asset"
    };

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public static IReadOnlyList<ScriptableObject> RecipeAssets { get; private set; } = Array.Empty<ScriptableObject>();

    private BusinessType? modBusinessType;
    private readonly List<Item> modItems = new();
    private GunStoreHelpDebugRuntime? helpDebugRuntime;

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);

        modItems.Clear();
        foreach (var itemAssetPath in ItemAssetPaths)
        {
            var modItem = bundle.LoadAsset<Item>(itemAssetPath);
            if (modItem == null)
                continue;

            modItems.Add(modItem);
            ItemsGetter.RegisterModItem(modItem);
        }

        RecipeAssets = GunStoreRecipeFactory.CreateAllRecipes()
            .Where(recipeAsset => recipeAsset != null)
            .Cast<ScriptableObject>()
            .ToArray();

        modBusinessType = bundle.LoadAsset<BusinessType>(BusinessTypeAssetPath);
        if (modBusinessType != null)
            ModdingAPI.RegisterModBusinessType(modBusinessType);

        helpDebugRuntime = GunStoreHelpDebugRuntime.Initialize(context);

        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        helpDebugRuntime?.Shutdown();
        helpDebugRuntime = null;

        if (modBusinessType != null)
            ModdingAPI.UnregisterModBusinessType(modBusinessType);

        foreach (var modItem in modItems)
            ItemsGetter.UnregisterModItem(modItem.itemName);

        modItems.Clear();
        RecipeAssets = Array.Empty<ScriptableObject>();

        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public class GunStoreBusinessTypeCityMod : IModBigAmbitions
{
    private const string BundleKey = "AssetBundles/gunstore-businesstype.unity3d";
    private const string GunStoreBusinessTypeName = "gunstore-businesstype:businesstype_gunstore";
    private const string RpgItemName = "gunstore-businesstype:itemname_rpg";
    private static readonly Address BlueStoneImporterAddress = new("ba:street_pier", 4);
    private static readonly Address MaritimeImporterAddress = new("ba:street_pier", 7);
    private static readonly string[] GunStoreShelfItemNames =
    {
        "gunstore-businesstype:itemname_ak47",
        "gunstore-businesstype:itemname_ammosmall",
        "gunstore-businesstype:itemname_wincheatersxp",
        "gunstore-businesstype:itemname_berettam9",
        "gunstore-businesstype:itemname_ammolarge",
        "gunstore-businesstype:itemname_rpg"
    };

    private static readonly string[] BlueStoneImporterItemNames =
    {
        "gunstore-businesstype:itemname_ak47",
        "gunstore-businesstype:itemname_ammosmall",
        "gunstore-businesstype:itemname_wincheatersxp",
        "gunstore-businesstype:itemname_berettam9",
        "gunstore-businesstype:itemname_ammolarge",
        "gunstore-businesstype:itemname_rpg"
    };

    private static readonly string[] MaritimeImporterItemNames =
    {
        "gunstore-businesstype:itemname_gunpartscheap",
        "gunstore-businesstype:itemname_gunpartsexpensive"
    };
    private static readonly string[] RetiredAiRivalBusinessNames =
    {
        "Friendly Fire Department",
        "Pew Pew Defense",
        "Guns R Us",
        "McMunition’s",
        "Respawn Disablers",
        "Pay-To-Win Supply Co.",
        "No Brain, Just Aim",
        "Boom Boom & Beyond",
        "Safety Third Firearms"
    };
    internal static IReadOnlyList<string> AiRivalBusinessNames => RetiredAiRivalBusinessNames;
    private static readonly (string FileName, string LayoutName)[] RivalLayouts =
    {
        ("GunStoreRivalsA1.json", "GunStoreRivalsA1"),
        ("GunStoreRivalsC1.json", "GunStoreRivalsC1"),
        ("GunStoreRivalsC2.json", "GunStoreRivalsC2"),
        ("GunStoreRivalsD2.json", "GunStoreRivalsD2"),
        ("GunStoreRivalsM1.json", "GunStoreRivalsM1")
    };
    private static readonly string[] RivalLayoutByBusiness =
    {
        "GunStoreRivalsC1", "GunStoreRivalsC1", "GunStoreRivalsA1",
        "GunStoreRivalsA1", "GunStoreRivalsC2", "GunStoreRivalsD2",
        "GunStoreRivalsM1", "GunStoreRivalsC1", "GunStoreRivalsD2"
    };
    private static readonly string[] PreferredTemplateLayouts =
    {
        "GiftShopRivals", "LiquorRivals", "ElectronicsRivals", "JewelryRivals"
    };

    private const string RoundedShelfItemName = "ba:itemname_roundedshelf";
    private const string ProductPanelItemName = "ba:itemname_productpanel";
    private const string ConsumerGoodsWorkstationType = "ba:factoryworkstationtype_consumergoodsworkstation";

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    private readonly Dictionary<BigAmbitions.Items.Item, string[]> patchedShowcaseShelves = new();
    private readonly List<IList> patchedRecipeLists = new();
    private readonly List<AiBusinessDefault> injectedAiDefaults = new();
    private ImportExportSettings? blueStoneImportSettings;
    private ImportExportSettings? maritimeImportSettings;

    internal static void RepairEmptyProductCachesAfterGameLoaded(ModContext? context)
    {
        var registrations = SaveGameManager.Current?.BuildingRegistrations;
        if (registrations == null)
            return;

        var changed = false;
        foreach (var registration in registrations)
        {
            if (registration == null || !registration.RentedByPlayer ||
                !string.Equals(registration.businessTypeName, GunStoreBusinessTypeName, StringComparison.Ordinal) ||
                registration.cachedAvailableProducts?.Count > 0 ||
                !HasLoadedGunStoreStock(registration))
            {
                continue;
            }

            registration.cachedAvailableProducts ??= new List<string>();

            try
            {
                BusinessHelper.UpdateCachedAvailableProducts(registration);
                changed |= registration.cachedAvailableProducts.Count > 0;
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
            }
        }

        if (changed)
            SaveGameManager.MarkChange();
    }

    internal static void RestoreAiRivalsAfterGameLoaded(ModContext? context)
    {
        var registrations = SaveGameManager.Current?.BuildingRegistrations;
        if (registrations == null)
            return;

        var reopenedCount = 0;
        foreach (var registration in registrations)
        {
            if (registration == null ||
                registration.RentedByPlayer ||
                !string.Equals(registration.businessTypeName, GunStoreBusinessTypeName, StringComparison.Ordinal) ||
                registration.Layout == null ||
                !registration.Layout.StartsWith("GunStoreRivals", StringComparison.Ordinal) ||
                !RetiredAiRivalBusinessNames.Contains(registration.BusinessName, StringComparer.Ordinal))
            {
                continue;
            }

            if (!registration.temporarilyClosed)
                continue;
            registration.temporarilyClosed = false;
            reopenedCount++;
            context?.Logger.Info(
                $"Gun Store: reopened NPC rival '{registration.BusinessName}' at {registration.Address}; " +
                $"layout='{registration.Layout}', savedFixtures={registration.itemInstances?.Count ?? 0}.");
        }
        if (reopenedCount > 0)
        {
            SaveGameManager.MarkChange();
            context?.Logger.Info($"Gun Store: reopened {reopenedCount} previously quarantined NPC rival(s).");
        }
    }

    private static bool HasLoadedGunStoreStock(BuildingRegistration registration)
    {
        if (registration.itemInstances == null)
            return false;

        foreach (var itemInstance in registration.itemInstances.Values)
        {
            if (itemInstance?.ItemCached == null)
                continue;

            var stock = ItemHelper.GetStockInstance(itemInstance);
            if (stock != null && GunStoreShelfItemNames.Contains(stock.itemName))
                return true;
        }

        return false;
    }

    public async Task OnLoadAsync(ModContext context)
    {
        context.Logger.Info(
            "Gun Store city integration loading NPC rivals, layouts, and fixture product catalogs; global visual mappings remain disabled.");

        for (var i = 0; i < 6; i++)
        {
            RegisterRivalLayouts(context);
            PatchAiBusinessDefaults(context);
            PatchShowcaseShelves(context);
            try
            {
                GunStoreNpcBannerRuntime.Prime(context);
            }
            catch (Exception exception)
            {
                context.Logger.Warn("Gun Store: NPC banner initialization failed; rival layouts remain registered.");
                context.Logger.Error(exception);
            }
            AddToImporter();
            PatchImportPartnerships();
            PatchConsumerGoodsWorkstation();

            if (i < 5)
                await Task.Yield();
        }

    }

    public Task OnUnloadAsync()
    {
        RestoreAiBusinessDefaults();
        RestoreConsumerGoodsWorkstation();
        RestoreShowcaseShelves();
        RemoveFromImporter();
        return Task.CompletedTask;
    }

    private static void RegisterRivalLayouts(ModContext context)
    {
        var registered = BusinessLayoutSets.BusinessLayoutSetHelper.GetAllBusinessLayoutSets();
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        var register = typeof(BusinessLayoutSets.BusinessLayoutSetHelper).GetMethod(
            "SetBusinessLayoutSynchronous", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(string) }, null);
        if (register == null)
        {
            context.Logger.Warn("Gun Store: NPC layout registration unavailable: game layout loader not found.");
            return;
        }

        var directory = Path.Combine(Application.temporaryCachePath, "BAModLayouts", context.ModId);
        Directory.CreateDirectory(directory);
        foreach (var layout in RivalLayouts)
        {
            if (registered.Values.Any(item => item.BusinessType == GunStoreBusinessTypeName &&
                                              item.LayoutName == layout.LayoutName))
                continue;

            var assetPath = "Assets/Mods/Gun Store/Layouts/" + layout.FileName;
            var asset = bundle.LoadAsset<TextAsset>(assetPath);
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                context.Logger.Warn($"Gun Store: NPC layout missing from bundle: '{assetPath}'.");
                continue;
            }

            var path = Path.Combine(directory, layout.FileName);
            File.WriteAllText(path, asset.text);
            register.Invoke(null, new object[] { path });
            if (registered.Values.Any(item => item.BusinessType == GunStoreBusinessTypeName &&
                                              item.LayoutName == layout.LayoutName))
                context.Logger.Info($"Gun Store: registered NPC layout '{layout.LayoutName}'.");
            else
                context.Logger.Warn($"Gun Store: NPC layout '{layout.LayoutName}' did not enter the game cache.");
        }
    }

    private void PatchAiBusinessDefaults(ModContext context)
    {
        var allDefaults = CompetitionHelper.GetAllBusinessDefaults();
        if (injectedAiDefaults.Count == 0)
        {
            var templates = allDefaults
                .Where(item => item != null && item.businessTypeName != GunStoreBusinessTypeName &&
                               string.IsNullOrEmpty(item.corporationRivalId))
                .OrderBy(item =>
                {
                    var index = Array.IndexOf(PreferredTemplateLayouts, item.buildingLayout);
                    return index < 0 ? int.MaxValue : index;
                })
                .ThenBy(item => item.name)
                .GroupBy(item => item.buildingLayout)
                .Select(group => group.First())
                .ToArray();
            if (templates.Length == 0)
            {
                context.Logger.Warn("Gun Store: NPC rivals unavailable: no base-game AI business templates found.");
                return;
            }

            for (var index = 0; index < RetiredAiRivalBusinessNames.Length; index++)
            {
                var clone = UnityEngine.Object.Instantiate(templates[index % templates.Length]);
                clone.name = "GunStoreNpc" + index;
                clone.businessName = RetiredAiRivalBusinessNames[index];
                clone.businessTypeName = GunStoreBusinessTypeName;
                clone.buildingLayout = RivalLayoutByBusiness[index];
                clone.logoSettings = (clone.logoSettings ?? new LogoSettings()).Clone();
                clone.logoSettings.logoShape = GunStoreNpcBannerRuntime.LogoShapeKey;
                injectedAiDefaults.Add(clone);
            }
        }

        var cacheField = typeof(CompetitionHelper).GetField("BusinessDefaultsCached", BindingFlags.Static | BindingFlags.NonPublic);
        if (cacheField?.GetValue(null) is AiBusinessDefault[] cached &&
            injectedAiDefaults.Any(item => !cached.Contains(item)))
        {
            cacheField.SetValue(null, cached.Concat(injectedAiDefaults.Where(item => !cached.Contains(item))).ToArray());
            context.Logger.Info($"Gun Store: registered {injectedAiDefaults.Count} NPC business defaults.");
        }

        var byTypeField = typeof(CompetitionHelper).GetField("BusinessDefaultsByType", BindingFlags.Static | BindingFlags.NonPublic);
        if (byTypeField?.GetValue(null) is Dictionary<string, AiBusinessDefault[]> byType)
        {
            byType.TryGetValue(GunStoreBusinessTypeName, out var existing);
            existing ??= Array.Empty<AiBusinessDefault>();
            byType[GunStoreBusinessTypeName] = existing
                .Concat(injectedAiDefaults.Where(item => !existing.Contains(item))).ToArray();
        }
    }

    private void RestoreAiBusinessDefaults()
    {
        var cacheField = typeof(CompetitionHelper).GetField("BusinessDefaultsCached", BindingFlags.Static | BindingFlags.NonPublic);
        if (cacheField?.GetValue(null) is AiBusinessDefault[] cached)
            cacheField.SetValue(null, cached.Where(item => !injectedAiDefaults.Contains(item)).ToArray());

        var byTypeField = typeof(CompetitionHelper).GetField("BusinessDefaultsByType", BindingFlags.Static | BindingFlags.NonPublic);
        if (byTypeField?.GetValue(null) is Dictionary<string, AiBusinessDefault[]> byType &&
            byType.TryGetValue(GunStoreBusinessTypeName, out var defaults))
        {
            var remaining = defaults.Where(item => !injectedAiDefaults.Contains(item)).ToArray();
            if (remaining.Length == 0)
                byType.Remove(GunStoreBusinessTypeName);
            else
                byType[GunStoreBusinessTypeName] = remaining;
        }

        foreach (var item in injectedAiDefaults)
            UnityEngine.Object.Destroy(item);
        injectedAiDefaults.Clear();
    }

    private void PatchShowcaseShelves(ModContext context)
    {
        // Only extend the selectable product catalog. ShelfController visual mappings are
        // global and replacing vanilla templates there corrupts unrelated shop fixtures.
        // Clear any mapping left by an older hot-reloaded version instead.
        foreach (var gunStoreItemName in GunStoreShelfItemNames)
            ShelfController.UnregisterItemToShow(gunStoreItemName);

        if (ItemsGetter.AllItems == null)
            return;

        var patchedShelfCount = 0;
        foreach (var item in ItemsGetter.AllItems)
        {
            if (!ShouldPatchShowcaseShelf(item))
                continue;

            var missingGunStoreItems = GunStoreShelfItemNames
                .Where(gunStoreItemName => !item.itemsThatCanShowcase.Contains(gunStoreItemName))
                .ToArray();
            if (missingGunStoreItems.Length == 0)
                continue;

            patchedShowcaseShelves[item] = item.itemsThatCanShowcase.ToArray();

            item.itemsThatCanShowcase = item.itemsThatCanShowcase.Concat(missingGunStoreItems).ToArray();
            patchedShelfCount++;
        }

        context.Logger.Info(
            $"Gun Store showcase integration registered {GunStoreShelfItemNames.Length} products on " +
            $"{patchedShelfCount} base-game fixture catalog(s). Custom-mod fixtures were not modified.");
    }

    private static bool ShouldPatchShowcaseShelf(BigAmbitions.Items.Item item)
    {
        if (item == null || item.itemsThatCanShowcase == null ||
            !item.itemName.StartsWith("ba:", StringComparison.Ordinal))
            return false;

        return item.itemName == RoundedShelfItemName || item.itemName == ProductPanelItemName;
    }

    private void RestoreShowcaseShelves()
    {
        foreach (var patchedShelf in patchedShowcaseShelves)
            patchedShelf.Key.itemsThatCanShowcase = patchedShelf.Value;

        foreach (var gunStoreItemName in GunStoreShelfItemNames)
            ShelfController.UnregisterItemToShow(gunStoreItemName);

        patchedShowcaseShelves.Clear();
    }

    private void AddToImporter()
    {
        blueStoneImportSettings ??=
            (ImportExportSettings)BuildingHelper.GetBuilding(BlueStoneImporterAddress).SpecialService.settings;
        maritimeImportSettings ??=
            (ImportExportSettings)BuildingHelper.GetBuilding(MaritimeImporterAddress).SpecialService.settings;

        foreach (var gunStoreItemName in BlueStoneImporterItemNames)
        {
            if (!blueStoneImportSettings.itemsAvailable.Contains(gunStoreItemName))
                blueStoneImportSettings.itemsAvailable.Add(gunStoreItemName);
        }

        foreach (var gunPartItemName in MaritimeImporterItemNames)
        {
            if (!maritimeImportSettings.itemsAvailable.Contains(gunPartItemName))
                maritimeImportSettings.itemsAvailable.Add(gunPartItemName);
        }
    }

    private void RemoveFromImporter()
    {
        if (blueStoneImportSettings != null)
        {
            foreach (var gunStoreItemName in BlueStoneImporterItemNames)
                blueStoneImportSettings.itemsAvailable.Remove(gunStoreItemName);
        }

        if (maritimeImportSettings != null)
        {
            foreach (var gunPartItemName in MaritimeImporterItemNames)
                maritimeImportSettings.itemsAvailable.Remove(gunPartItemName);
        }
    }

    private void PatchConsumerGoodsWorkstation()
    {
        if (GunStoreBusinessTypeMod.RecipeAssets.Count == 0)
            return;

        var patchedAnyWorkstation = false;

        foreach (var scriptableObject in Resources.FindObjectsOfTypeAll<ScriptableObject>())
        {
            if (TryPatchWorkstation(scriptableObject))
                patchedAnyWorkstation = true;
        }

        if (TryPatchFactoryWorkstationCaches())
            patchedAnyWorkstation = true;

        if (patchedAnyWorkstation)
            RefreshFactoryWorkstationHelper();
    }

    private bool TryPatchFactoryWorkstationCaches()
    {
        var helperType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("BigAmbitions.Factories.Workstations.FactoryWorkstationHelper", false))
            .FirstOrDefault(type => type != null);
        if (helperType == null)
            return false;

        var bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var patchedAnyWorkstation = false;

        foreach (var fieldName in new[] { "AllWorkstations", "AllWorkstationsByType" })
        {
            var field = helperType.GetField(fieldName, bindingFlags);
            if (field?.GetValue(null) == null)
                continue;

            if (TryPatchWorkstationContainer(field.GetValue(null)))
                patchedAnyWorkstation = true;
        }

        return patchedAnyWorkstation;
    }

    private bool TryPatchWorkstationContainer(object? container)
    {
        if (container == null)
            return false;

        var patchedAnyWorkstation = false;

        if (TryPatchWorkstation(container))
            patchedAnyWorkstation = true;

        if (container is IDictionary dictionary)
        {
            foreach (var value in dictionary.Values)
            {
                if (TryPatchWorkstationContainer(value))
                    patchedAnyWorkstation = true;
            }

            return patchedAnyWorkstation;
        }

        if (container is IEnumerable enumerable && container is not string)
        {
            foreach (var value in enumerable)
            {
                if (TryPatchWorkstationContainer(value))
                    patchedAnyWorkstation = true;
            }
        }

        return patchedAnyWorkstation;
    }

    private bool TryPatchWorkstation(object? workstationObject)
    {
        if (workstationObject == null)
            return false;

        var type = workstationObject.GetType();
        if (type.FullName != "BigAmbitions.Factories.Workstations.FactoryWorkstation")
            return false;

        var workstationTypeField =
            type.GetField("workstationType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var supportedRecipesField =
            type.GetField("supportedRecipes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (workstationTypeField == null || supportedRecipesField == null)
            return false;

        if (!string.Equals(workstationTypeField.GetValue(workstationObject) as string, ConsumerGoodsWorkstationType,
                StringComparison.Ordinal))
            return false;

        if (supportedRecipesField.GetValue(workstationObject) is not IList supportedRecipes)
            return false;

        var addedAnyRecipe = false;
        foreach (var recipeAsset in GunStoreBusinessTypeMod.RecipeAssets)
        {
            if (supportedRecipes.Contains(recipeAsset))
                continue;

            supportedRecipes.Add(recipeAsset);
            addedAnyRecipe = true;
        }

        if (addedAnyRecipe && !patchedRecipeLists.Contains(supportedRecipes))
            patchedRecipeLists.Add(supportedRecipes);

        return addedAnyRecipe;
    }

    private static void RefreshFactoryWorkstationHelper()
    {
        var helperType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("BigAmbitions.Factories.Workstations.FactoryWorkstationHelper", false))
            .FirstOrDefault(type => type != null);
        if (helperType == null)
            return;

        var onFactoryWorkstationsLoaded =
            helperType.GetMethod("OnFactoryWorkstationsLoaded", BindingFlags.Static | BindingFlags.Public |
                                                             BindingFlags.NonPublic);
        if (onFactoryWorkstationsLoaded == null || onFactoryWorkstationsLoaded.GetParameters().Length != 0)
            return;

        onFactoryWorkstationsLoaded.Invoke(null, null);
    }

    private void RestoreConsumerGoodsWorkstation()
    {
        if (GunStoreBusinessTypeMod.RecipeAssets.Count == 0)
            return;

        foreach (var supportedRecipes in patchedRecipeLists)
        {
            foreach (var recipeAsset in GunStoreBusinessTypeMod.RecipeAssets)
                supportedRecipes.Remove(recipeAsset);
        }

        patchedRecipeLists.Clear();
    }

    private void PatchImportPartnerships()
    {
        var importPartnerships = SaveGameManager.Current?.importPartnerships;
        if (importPartnerships == null)
            return;

        foreach (var importPartnership in importPartnerships)
        {
            if (importPartnership == null)
                continue;

            if (IsBlueStoneImportPartnership(importPartnership))
                RemoveImportProduct(importPartnership, RpgItemName);

            if (IsMaritimeImportPartnership(importPartnership))
                importPartnership.AddMissingImportProducts();
        }
    }

    private static bool IsBlueStoneImportPartnership(object importPartnership)
    {
        var addressValue = GetMemberValue(importPartnership, "importAddress") ?? GetMemberValue(importPartnership, "ImportAddress");
        return addressValue is Address address && address.Equals(BlueStoneImporterAddress);
    }

    private static bool IsMaritimeImportPartnership(object importPartnership)
    {
        var addressValue = GetMemberValue(importPartnership, "importAddress") ?? GetMemberValue(importPartnership, "ImportAddress");
        return addressValue is Address address && address.Equals(MaritimeImporterAddress);
    }

    private static void RemoveImportProduct(object importPartnership, string itemName)
    {
        if (GetMemberValue(importPartnership, "products") is IList products)
        {
            for (var i = products.Count - 1; i >= 0; i--)
            {
                var product = products[i];
                if (product == null)
                    continue;

                var productItemName = GetMemberValue(product, "itemName") as string
                    ?? GetMemberValue(product, "ItemName") as string;
                if (string.Equals(productItemName, itemName, StringComparison.Ordinal))
                    products.RemoveAt(i);
            }
        }

        RemoveDictionaryEntry(importPartnership, "ImporterAmountPerItem", itemName);
        RemoveDictionaryEntry(importPartnership, "ContractAmountPerItem", itemName);

        RemoveCollectionEntry(importPartnership, "AmountExceededItems", itemName);
    }

    private static void RemoveDictionaryEntry(object owner, string memberName, string key)
    {
        if (GetMemberValue(owner, memberName) is not IDictionary dictionary)
            return;

        if (dictionary.Contains(key))
            dictionary.Remove(key);
    }

    private static void RemoveCollectionEntry(object owner, string memberName, object value)
    {
        var collection = GetMemberValue(owner, memberName);
        if (collection == null)
            return;

        if (collection is IList list)
        {
            while (list.Contains(value))
                list.Remove(value);

            return;
        }

        var removeMethod = collection.GetType().GetMethod("Remove", BindingFlags.Instance | BindingFlags.Public, null, new[] { value.GetType() }, null);
        removeMethod?.Invoke(collection, new[] { value });
    }

    private static object? GetMemberValue(object owner, string memberName)
    {
        const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var ownerType = owner.GetType();

        var field = ownerType.GetField(memberName, bindingFlags);
        if (field != null)
            return field.GetValue(owner);

        var property = ownerType.GetProperty(memberName, bindingFlags);
        if (property == null || property.GetIndexParameters().Length != 0)
            return null;

        try
        {
            return property.GetValue(owner);
        }
        catch
        {
            return null;
        }
    }

}
