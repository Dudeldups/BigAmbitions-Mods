#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Items;
using Helpers;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxItemCapacityService
    {
        private readonly Dictionary<string, int> originalCapacities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Item>> resolvedItems = new Dictionary<string, List<Item>>(StringComparer.OrdinalIgnoreCase);

        public void InvalidateCache()
        {
            resolvedItems.Clear();
        }

        public void ApplyConfiguredCapacities(ModContext context, BigHaxSettings settings)
        {
            ApplyItemCapacity(context, BigHaxTargetIds.StandardFridgeItemName, settings.StandardFridgeCapacity);
            ApplyItemCapacity(context, BigHaxTargetIds.PalletShelfItemName, settings.PalletShelfCapacity);
            ApplyItemCapacity(context, BigHaxTargetIds.StorageShelfItemName, settings.StorageShelfCapacity);
            RefreshCurrentCargoHolder(BigHaxTargetIds.StandardFridgeItemName);
            RefreshCurrentCargoHolder(BigHaxTargetIds.PalletShelfItemName);
            RefreshCurrentCargoHolder(BigHaxTargetIds.StorageShelfItemName);
        }

        public void RestoreOriginalCapacities()
        {
            foreach (var pair in resolvedItems)
            {
                if (!originalCapacities.TryGetValue(pair.Key, out var originalCapacity))
                    continue;

                foreach (var item in pair.Value)
                {
                    if (item != null)
                        item.cargoCapacity = originalCapacity;
                }
            }
        }

        private void ApplyItemCapacity(ModContext context, string itemName, int capacity)
        {
            var items = ResolveItems(itemName);
            if (items.Count == 0)
            {
                BigHaxLogger.WarnOnce(context, "missing-item-" + itemName, $"BigHax: could not resolve item definition '{itemName}'.");
                return;
            }

            foreach (var item in items)
            {
                if (!originalCapacities.ContainsKey(itemName))
                    originalCapacities[itemName] = item.cargoCapacity;

                if (item.cargoCapacity != capacity)
                    item.cargoCapacity = capacity;
            }
        }

        private static void RefreshCurrentCargoHolder(string itemName)
        {
            if (PlayerHelper.GetCurrentCargoHolder() is not ItemInstance itemInstance)
                return;

            if (!string.Equals(itemInstance.itemName, itemName, StringComparison.OrdinalIgnoreCase))
                return;

            itemInstance.GetMaxCargoSize();
            TriggerCargoUpdated(itemInstance);
        }

        private static void TriggerCargoUpdated(ItemInstance itemInstance)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var method = typeof(ItemInstance).GetMethod("OnItemsInCargoUpdated", Flags);
            if (method == null)
                return;

            try
            {
                var callback = method.Invoke(itemInstance, null) as Action;
                callback?.Invoke();
            }
            catch
            {
                // Best effort only; some UI paths update lazily from the patched capacity anyway.
            }
        }

        private List<Item> ResolveItems(string itemName)
        {
            if (resolvedItems.TryGetValue(itemName, out var cachedItems))
                return cachedItems;

            var items = new List<Item>();
            foreach (var item in Resources.FindObjectsOfTypeAll<Item>())
            {
                if (item != null && string.Equals(item.itemName, itemName, StringComparison.OrdinalIgnoreCase))
                    items.Add(item);
            }

            resolvedItems[itemName] = items;
            return items;
        }
    }
}
