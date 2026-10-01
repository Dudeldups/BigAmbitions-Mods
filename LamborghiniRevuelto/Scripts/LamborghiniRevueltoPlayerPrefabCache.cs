#nullable enable
using System;
using System.Collections;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;

// Native purchases and save loading share this exact player-prefab lookup.
// Avoid the ModAPI's cross-bundle enumeration, which can invalidate itself.
internal static class LamborghiniRevueltoPlayerPrefabCache
{
    private const string CacheKey = "Prefabs/Vehicles/PlayerVehicles/lamborghinirevuelto.prefab";
    private static readonly FieldInfo? CacheField = typeof(PrefabHelper).GetField(
        "PrefabCache", BindingFlags.Static | BindingFlags.NonPublic);
    private static GameObject? prefab;
    private static UnityEngine.Object? previousPrefab;
    private static ModContext? context;

    internal static void Install(GameObject playerPrefab, ModContext modContext)
    {
        if (CacheField?.GetValue(null) is not IDictionary)
            throw new InvalidOperationException("LamborghiniRevuelto: native player prefab cache is unavailable.");
        prefab = playerPrefab;
        context = modContext;
        Ensure("mod-load");
    }

    internal static void Ensure(string source)
    {
        if (prefab == null)
            return;
        if (CacheField?.GetValue(null) is not IDictionary cache)
        {
            context?.Logger.Warn("LamborghiniRevuelto: native player prefab cache became unavailable.");
            return;
        }
        if (ReferenceEquals(cache[CacheKey], prefab))
            return;

        previousPrefab = cache[CacheKey] as UnityEngine.Object;
        cache[CacheKey] = prefab;
        LamborghiniRevueltoDiagnostics.WarehouseExitInfo(context,
            $"LamborghiniRevuelto player prefab registered: source={source}, key='{CacheKey}', prefab='{prefab.name}'.");
    }

    internal static void Remove()
    {
        if (CacheField?.GetValue(null) is IDictionary cache &&
            ReferenceEquals(cache[CacheKey], prefab))
        {
            if (previousPrefab != null)
                cache[CacheKey] = previousPrefab;
            else
                cache.Remove(CacheKey);
        }
        prefab = null;
        previousPrefab = null;
        context = null;
    }
}
