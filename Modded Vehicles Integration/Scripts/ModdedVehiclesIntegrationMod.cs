#nullable enable
using System.Threading.Tasks;
using BAModAPI;
using UnityEngine;

[assembly: RegisterModClass(typeof(ModdedVehiclesIntegration.ModdedVehiclesIntegrationMod))]

namespace ModdedVehiclesIntegration
{
    [ModEntryOnInitializationLoad]
    public sealed class ModdedVehiclesIntegrationMod : IModBigAmbitions
    {
        private ModdedVehiclesIntegrationRuntime? runtime;

        public string[] RelativeAssetBundlePaths => System.Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            DealerLayoutIntegration.EnsureApplied(context);
            DealerServiceIntegration.EnsureApplied(context);
            runtime = ModdedVehiclesIntegrationRuntime.Initialize(context);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            runtime?.Shutdown();
            runtime = null;
            DealerLayoutIntegration.Restore();
            return Task.CompletedTask;
        }
    }
}
