#nullable enable
using System;
using BAModAPI;
using UnityEngine;

namespace ModdedVehiclesIntegration
{
    internal sealed class ModdedVehiclesIntegrationRuntime : MonoBehaviour
    {
        private readonly DealerCatalogIntegration catalogIntegration = new DealerCatalogIntegration();
        private readonly DealerDeskInteractionIntegration deskInteractionIntegration =
            new DealerDeskInteractionIntegration();
        private ModContext? context;
        private object? currentSave;
        private bool wasEnteringBuilding;

        internal static ModdedVehiclesIntegrationRuntime Initialize(ModContext context)
        {
            var existing = FindObjectOfType<ModdedVehiclesIntegrationRuntime>();
            if (existing != null)
            {
                existing.context = context;
                existing.deskInteractionIntegration.SetCatalogSynchronizer(
                    existing.SynchronizeCatalogForInteraction);
                return existing;
            }

            var runtimeObject = new GameObject(nameof(ModdedVehiclesIntegrationRuntime));
            DontDestroyOnLoad(runtimeObject);
            var runtime = runtimeObject.AddComponent<ModdedVehiclesIntegrationRuntime>();
            runtime.context = context;
            runtime.deskInteractionIntegration.SetCatalogSynchronizer(runtime.SynchronizeCatalogForInteraction);
            return runtime;
        }

        internal void Shutdown()
        {
            deskInteractionIntegration.Shutdown();
            catalogIntegration.RestoreExternalCatalogs();
            DealerServiceIntegration.Restore();
            Destroy(gameObject);
        }

        private void Update()
        {
            RefreshLayoutCacheOnBuildingEntry();
            deskInteractionIntegration.Update(context);

            var save = SaveGameManager.Current;
            if (ReferenceEquals(currentSave, save))
                return;

            currentSave = save;
            catalogIntegration.ResetTracking();
            if (save != null)
                RefreshAll();
        }

        private void RefreshLayoutCacheOnBuildingEntry()
        {
            var enteringBuilding = InstanceBehavior<BuildingManager>.Instance?.enteringBuilding == true;
            if (enteringBuilding && !wasEnteringBuilding)
            {
                DealerLayoutIntegration.EnsureApplied(context);
                DealerServiceIntegration.EnsureApplied(context);
            }

            wasEnteringBuilding = enteringBuilding;
        }

        private void SynchronizeCatalogForInteraction()
        {
            catalogIntegration.Synchronize();
        }

        private void RefreshAll()
        {
            var save = SaveGameManager.Current;
            if (save == null)
                return;

            DealerLayoutIntegration.EnsureApplied(context);
            DealerServiceIntegration.EnsureApplied(context);
            catalogIntegration.Synchronize();
        }
    }
}
