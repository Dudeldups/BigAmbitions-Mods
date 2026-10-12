#nullable enable
using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using Helpers;
using UnityEngine;

// The native cache is cleared when leaving a save. These mod lifecycle hooks
// restore the purchase/save-load key before city objects Awake, without polling.
[ModEntryMainMenu]
public sealed class McLarenSennaMainMenuPrefabRegistration : IModBigAmbitions
{
    private ModContext? context;
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext modContext)
    {
        context = modContext;
        McLarenSennaPlayerPrefabRegistration.LoadAndBind(modContext, "main-menu-load");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        if (context != null)
            McLarenSennaPlayerPrefabRegistration.LoadAndBind(context, "main-menu-unload");
        context = null;
        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public sealed class McLarenSennaCityPrefabRegistration : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext context)
    {
        McLarenSennaPlayerPrefabRegistration.LoadAndBind(context, "city-load");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync() => Task.CompletedTask;
}

internal static class McLarenSennaPlayerPrefabRegistration
{
    internal const string PrefabName = "Vehicles/PlayerVehicles/mclarensenna";
    private const string CacheKey = "Prefabs/" + PrefabName + ".prefab";
    private static readonly FieldInfo? CacheField = typeof(PrefabHelper).GetField(
        "PrefabCache", BindingFlags.Static | BindingFlags.NonPublic);
    private static GameObject? ownedPrefab;
    private static UnityEngine.Object? previousPrefab;

    internal static bool LoadAndBind(ModContext context, string source)
    {
        var bundle = AssetService.GetBundle(context.ModId, McLarenSennaMod.BundleKey);
        var prefab = bundle?.LoadAsset<GameObject>(McLarenSennaMod.VehiclePrefabPath);
        return Bind(context, prefab, source);
    }

    internal static bool Bind(ModContext? context, GameObject? prefab, string source)
    {
        try
        {
            var cache = CacheField?.GetValue(null) as IDictionary;
            if (prefab == null || cache == null)
                throw new InvalidOperationException("McLaren player prefab or native cache is unavailable.");

            if (!ReferenceEquals(cache[CacheKey], prefab))
            {
                // Capture the current cache generation. A save unload may have
                // removed our old binding; never restore an entry from that save.
                if (!ReferenceEquals(cache[CacheKey], ownedPrefab))
                    previousPrefab = cache[CacheKey] as UnityEngine.Object;
                cache[CacheKey] = prefab;
            }
            ownedPrefab = prefab;

            // Use the game's exact dealer/save-load lookup. It must resolve our
            // loaded asset without the failing cross-bundle filename enumeration.
            if (!ReferenceEquals(PrefabHelper.LoadPrefabAssetByName(PrefabName), prefab))
                throw new InvalidOperationException("Native purchase lookup did not resolve the McLaren prefab.");

            McLarenSennaDiagnostics.DealerEntryInfo(context,
                $"McLarenSenna: player prefab bound and purchase lookup verified key='{CacheKey}' source='{source}'.");
            return true;
        }
        catch (Exception exception)
        {
            Release();
            var message = $"McLarenSenna: player prefab binding failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}";
            if (context != null) context.Logger.Warn(message);
            else Debug.LogWarning(message);
            return false;
        }
    }

    internal static void Release()
    {
        var cache = CacheField?.GetValue(null) as IDictionary;
        if (cache != null && ownedPrefab != null && ReferenceEquals(cache[CacheKey], ownedPrefab))
        {
            if (previousPrefab != null) cache[CacheKey] = previousPrefab;
            else cache.Remove(CacheKey);
        }
        ownedPrefab = null;
        previousPrefab = null;
    }
}
