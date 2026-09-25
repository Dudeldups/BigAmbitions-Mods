#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using BigAmbitions.Items;
using Blueprints;
using BusinessLayoutSets;
using UnityEngine;

namespace ModdedVehiclesIntegration
{
    internal static class DealerLayoutIntegration
    {
        private const string CarDealershipBusinessType = "ba:businesstype_cardealership";
        private const string VehicleStoreDeskItem = "ba:itemname_specialemployeedesk";
        private const string VehicleStoreCustomValue = "VehicleStore";
        private const float CustomerChairDistance = 1.5f;

        private static readonly LayoutDefinition[] Layouts =
        {
            new LayoutDefinition(
                "ba:buildingsize_d",
                2,
                "GarmentDistrictCarDealershipCheap",
                "ba:itemname_officedesk1"),
            new LayoutDefinition(
                "ba:buildingsize_m",
                1,
                "MurrayHillCarDealershipLuxury",
                "ba:itemname_officedesk2left",
                "ba:itemname_officedesk2right"),
            new LayoutDefinition(
                "ba:buildingsize_m",
                1,
                "HamptonsCarDealershipLuxury",
                "ba:itemname_officedesk2left",
                "ba:itemname_officedesk2right")
        };

        private static readonly List<PatchRecord> AppliedPatches = new List<PatchRecord>();

        internal static void EnsureApplied(ModContext? context)
        {
            foreach (var definition in Layouts)
                TryApply(definition, context);
        }

        internal static void Restore()
        {
            for (var index = AppliedPatches.Count - 1; index >= 0; index--)
            {
                var patch = AppliedPatches[index];
                patch.Desk.itemName = patch.OriginalItemName;
                patch.Desk.customValue = patch.OriginalCustomValue;
                patch.Desk.customColors = patch.OriginalCustomColors;
                patch.Desk.stackedItems = patch.OriginalStackedItems;
                patch.Layout.Items.Remove(patch.CustomerChair);
            }

            AppliedPatches.Clear();
        }

        private static void TryApply(LayoutDefinition definition, ModContext? context)
        {
            BusinessLayoutSet? layout;
            try
            {
                layout = BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
                    CarDealershipBusinessType,
                    new BuildingSizeInfo(definition.BuildingSize, definition.BuildingVersion),
                    definition.LayoutName.ToLowerInvariant(),
                    false);
            }
            catch
            {
                return;
            }

            if (layout?.Items == null)
                return;

            foreach (var deskItemName in definition.DeskItemNames)
                TryApplyDesk(layout, definition, deskItemName, context);
        }

        private static void TryApplyDesk(
            BusinessLayoutSet layout,
            LayoutDefinition definition,
            string deskItemName,
            ModContext? context)
        {
            if (IsAlreadyTracked(layout, deskItemName))
                return;

            var desk = layout.Items.Find(item =>
                item != null && string.Equals(item.itemName, deskItemName, StringComparison.Ordinal));
            if (desk == null)
            {
                context?.Logger.Warn(
                    $"Modded Vehicles Integration: desk '{deskItemName}' was not found in '{definition.LayoutName}'.");
                return;
            }

            var employeeChairLink = desk.stackedItems?.Find(child => child != null && child.attachmentIndex == 2);
            var employeeChair = employeeChairLink == null
                ? null
                : layout.Items.Find(item => item != null && item.id == employeeChairLink.childId);
            if (employeeChair == null)
            {
                context?.Logger.Warn(
                    $"Modded Vehicles Integration: '{definition.LayoutName}' has no chair attached to its dealer desk.");
                return;
            }

            var customerChair = CloneChair(employeeChair);
            PositionCustomerChair(desk, customerChair);
            var originalStackedItems = desk.stackedItems ?? new List<AttachableChild>();
            var updatedStackedItems = new List<AttachableChild>(originalStackedItems)
            {
                new AttachableChild
                {
                    childId = customerChair.id,
                    childItemName = customerChair.itemName,
                    attachmentIndex = 3
                }
            };

            AppliedPatches.Add(new PatchRecord(
                layout,
                desk,
                desk.itemName,
                desk.customValue,
                desk.customColors,
                originalStackedItems,
                customerChair));

            desk.itemName = VehicleStoreDeskItem;
            desk.customValue = VehicleStoreCustomValue;
            desk.customColors = new List<CustomColor>();
            desk.stackedItems = updatedStackedItems;
            layout.Items.Add(customerChair);
        }

        private static bool IsAlreadyTracked(BusinessLayoutSet layout, string originalDeskItemName)
        {
            foreach (var patch in AppliedPatches)
            {
                if (ReferenceEquals(patch.Layout, layout) &&
                    string.Equals(patch.OriginalItemName, originalDeskItemName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static BusinessLayoutSets.Item CloneChair(BusinessLayoutSets.Item source)
        {
            var settings = source.playerItemPurchaserSettings;
            return new BusinessLayoutSets.Item
            {
                id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
                rotation = source.rotation,
                position = source.position,
                itemName = source.itemName,
                playerItemPurchaserSettings = settings == null
                    ? new PlayerItemPurchaserSettings()
                    : new PlayerItemPurchaserSettings
                    {
                        name = settings.name,
                        enabled = settings.enabled,
                        itemName = settings.itemName,
                        itemQuantity = settings.itemQuantity
                    },
                stackedItems = new List<AttachableChild>(),
                parentId = string.Empty,
                dirtSpotsThatAffects = new List<int>(),
                customPositions = source.customPositions == null
                    ? new List<SerializableVector3>()
                    : new List<SerializableVector3>(source.customPositions),
                customColors = source.customColors == null
                    ? new List<CustomColor>()
                    : new List<CustomColor>(source.customColors),
                customValue = source.customValue ?? string.Empty,
                worldSpaceTextValue = source.worldSpaceTextValue ?? string.Empty,
                linkedItemName = source.linkedItemName
            };
        }

        private static void PositionCustomerChair(
            BusinessLayoutSets.Item desk,
            BusinessLayoutSets.Item customerChair)
        {
            Quaternion deskRotation = desk.rotation;
            var offset = deskRotation * Vector3.back * CustomerChairDistance;
            customerChair.position = new SerializableVector3(
                desk.position.x + offset.x,
                customerChair.position.y,
                desk.position.z + offset.z);
            customerChair.rotation = desk.rotation;
        }

        private sealed class LayoutDefinition
        {
            internal LayoutDefinition(
                string buildingSize,
                int buildingVersion,
                string layoutName,
                params string[] deskItemNames)
            {
                BuildingSize = buildingSize;
                BuildingVersion = buildingVersion;
                LayoutName = layoutName;
                DeskItemNames = deskItemNames;
            }

            internal string BuildingSize { get; }
            internal int BuildingVersion { get; }
            internal string LayoutName { get; }
            internal string[] DeskItemNames { get; }
        }

        private sealed class PatchRecord
        {
            internal PatchRecord(
                BusinessLayoutSet layout,
                BusinessLayoutSets.Item desk,
                string originalItemName,
                string originalCustomValue,
                List<CustomColor> originalCustomColors,
                List<AttachableChild> originalStackedItems,
                BusinessLayoutSets.Item customerChair)
            {
                Layout = layout;
                Desk = desk;
                OriginalItemName = originalItemName;
                OriginalCustomValue = originalCustomValue;
                OriginalCustomColors = originalCustomColors;
                OriginalStackedItems = originalStackedItems;
                CustomerChair = customerChair;
            }

            internal BusinessLayoutSet Layout { get; }
            internal BusinessLayoutSets.Item Desk { get; }
            internal string OriginalItemName { get; }
            internal string OriginalCustomValue { get; }
            internal List<CustomColor> OriginalCustomColors { get; }
            internal List<AttachableChild> OriginalStackedItems { get; }
            internal BusinessLayoutSets.Item CustomerChair { get; }
        }
    }
}
