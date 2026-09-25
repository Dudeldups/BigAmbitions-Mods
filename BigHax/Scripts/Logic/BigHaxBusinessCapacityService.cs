#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using BigAmbitions.Items;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxBusinessCapacityService
    {
        private readonly Dictionary<string, int> originalAddedCustomersPerHour = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Item>> resolvedItems = new Dictionary<string, List<Item>>(StringComparer.OrdinalIgnoreCase);

        public void InvalidateCache()
        {
            resolvedItems.Clear();
        }

        public void ApplyConfiguredCapacities(ModContext context, BigHaxSettings settings)
        {
            var multiplier = Mathf.Max(1f, settings.CustomerTrafficMultiplier);
            var throughputItems = ResolveThroughputItems();
            if (throughputItems.Count == 0)
            {
                BigHaxLogger.WarnOnce(context, "missing-business-capacity-items", "BigHax: could not resolve any business throughput item definitions.");
                return;
            }

            foreach (var item in throughputItems)
            {
                if (!originalAddedCustomersPerHour.ContainsKey(item.itemName))
                    originalAddedCustomersPerHour[item.itemName] = item.addedCustomersPerHour;

                var originalValue = originalAddedCustomersPerHour[item.itemName];
                var targetValue = multiplier <= 1f
                    ? originalValue
                    : Mathf.Max(originalValue, Mathf.CeilToInt(originalValue * multiplier));

                if (item.addedCustomersPerHour != targetValue)
                    item.addedCustomersPerHour = targetValue;
            }
        }

        public void RestoreOriginalCapacities()
        {
            foreach (var pair in resolvedItems)
            {
                if (!originalAddedCustomersPerHour.TryGetValue(pair.Key, out var originalValue))
                    continue;

                foreach (var item in pair.Value)
                {
                    if (item != null)
                        item.addedCustomersPerHour = originalValue;
                }
            }
        }

        private List<Item> ResolveThroughputItems()
        {
            var allResolvedItems = new List<Item>();
            foreach (var item in Resources.FindObjectsOfTypeAll<Item>())
            {
                if (item == null || item.addedCustomersPerHour <= 0 || string.IsNullOrWhiteSpace(item.itemName))
                    continue;

                if (!resolvedItems.TryGetValue(item.itemName, out var itemDefinitions))
                {
                    itemDefinitions = new List<Item>();
                    resolvedItems[item.itemName] = itemDefinitions;
                }

                if (!itemDefinitions.Contains(item))
                    itemDefinitions.Add(item);
            }

            foreach (var itemDefinitions in resolvedItems.Values)
                allResolvedItems.AddRange(itemDefinitions);

            return allResolvedItems;
        }
    }
}
