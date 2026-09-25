#nullable enable
using System;
using BAModAPI;
using Helpers;
using Vehicles.VehicleTypes;

namespace BigHax
{
    /// <summary>
    /// Keeps player vehicles pristine on vehicle entry and collision. Collision repair
    /// is handled by a component attached to the player vehicle's rigidbody, not by
    /// a global physics scan or a continuous update check.
    /// </summary>
    internal sealed class BigHaxVehicleConditionService
    {
        public void ApplyConfiguredConditions(BigHaxSettings settings)
        {
            if (!HasEnabledConditions(settings))
                return;

            var controllers = VehicleHelper.AllPlayerVehicles;
            if (controllers != null)
            {
                foreach (var controller in controllers)
                {
                    if (controller?.vehicleInstance == null)
                        continue;

                    EnsureCollisionGuard(controller);
                    ApplyControllerConditions(controller, settings);
                }
            }

            // Process controllers first: Repair and SetDirtiness also refresh the
            // currently visible vehicle meshes. The save pass then covers vehicles
            // that are not presently instantiated in the scene.
            var changed = false;
            var savedVehicles = SaveGameManager.Current?.VehicleInstances;
            if (savedVehicles != null)
            {
                foreach (var vehicle in savedVehicles)
                    changed |= ApplySavedVehicleConditions(vehicle, settings);
            }

            if (changed)
                SaveGameManager.MarkChange();
        }

        public bool HasEnabledConditions(BigHaxSettings settings)
        {
            return settings.EnableNoVehicleDamage ||
                   settings.EnableInfiniteVehicleFuel ||
                   settings.EnableNeverDirtyVehicles;
        }

        public void ApplyActiveVehicleConditions(BigHaxSettings settings)
        {
            var activeVehicleId = SaveGameManager.Current?.ActiveVehicleId;
            if (string.IsNullOrWhiteSpace(activeVehicleId))
                return;

            var controllers = VehicleHelper.AllPlayerVehicles;
            if (controllers == null)
                return;

            foreach (var controller in controllers)
            {
                if (controller?.vehicleInstance == null ||
                    !string.Equals(controller.vehicleInstance.id, activeVehicleId, StringComparison.Ordinal))
                {
                    continue;
                }

                EnsureCollisionGuard(controller);
                ApplyControllerConditions(controller, settings);
                return;
            }
        }

        private static void EnsureCollisionGuard(VehicleController controller)
        {
            if (controller.GetComponent<BigHaxVehicleCollisionGuard>() == null)
                controller.gameObject.AddComponent<BigHaxVehicleCollisionGuard>();
        }

        private static bool ApplySavedVehicleConditions(VehicleInstance? vehicle, BigHaxSettings settings)
        {
            if (vehicle == null)
                return false;

            var changed = false;
            if (settings.EnableNoVehicleDamage && (vehicle.damage > 0.001f || (vehicle.deformations?.Count ?? 0) > 0))
            {
                vehicle.damage = 0f;
                vehicle.deformations?.Clear();
                changed = true;
            }

            if (settings.EnableNeverDirtyVehicles && vehicle.dirtiness > 0.001f)
            {
                vehicle.dirtiness = 0f;
                changed = true;
            }

            if (settings.EnableInfiniteVehicleFuel)
            {
                var vehicleType = VehicleTypeHelper.GetVehicleType(vehicle.vehicleTypeName);
                if (vehicleType != null && vehicleType.maxFuel > 0f && vehicle.fuel < vehicleType.maxFuel - 0.001f)
                {
                    vehicle.fuel = vehicleType.maxFuel;
                    changed = true;
                }
            }

            return changed;
        }

        private static void ApplyControllerConditions(VehicleController controller, BigHaxSettings settings)
        {
            var vehicle = controller.vehicleInstance;
            if (vehicle == null)
                return;

            var maxFuel = controller.vehicleType?.maxFuel ?? -1f;
            var carController = controller.GetComponent<CarController>() ?? controller.GetComponentInChildren<CarController>(true);
            var currentFuel = carController?.GetCurrentFuel() ?? vehicle.fuel;

            // Use the controller setters for loaded cars so the visible fuel gauge,
            // dirt material, and deformation mesh update immediately as well.
            if (settings.EnableInfiniteVehicleFuel &&
                controller.vehicleType != null &&
                controller.vehicleType.maxFuel > 0f &&
                currentFuel < controller.vehicleType.maxFuel - 0.001f)
            {
                // VehicleController changes the saved value, but the driving HUD is
                // backed by CarController's runtime fuel module. Its setter invokes
                // the UI refresh callback as well.
                if (carController != null)
                    carController.SetFuel(controller.vehicleType.maxFuel);
                else
                    controller.SetFuel(controller.vehicleType.maxFuel);
            }

            if (settings.EnableNeverDirtyVehicles && vehicle.dirtiness > 0.001f)
                controller.SetDirtiness(0f);

            if (settings.EnableNoVehicleDamage &&
                (vehicle.damage > 0.001f || (vehicle.deformations?.Count ?? 0) > 0))
            {
                controller.Repair();
            }
        }
    }
}
