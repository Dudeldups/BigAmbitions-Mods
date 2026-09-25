using System;
using System.Threading.Tasks;
using BAModAPI;
using UnityEngine;

[assembly: RegisterModClass(typeof(CustomNPCAPI.CustomNpcLibraryMod))]

namespace CustomNPCAPI
{
    [ModEntryOnCityLoad]
    public sealed class CustomNpcLibraryMod : IModBigAmbitions
    {
        private GameObject _driverRoot;
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            CustomNpcApi.ActivateHost();
            _driverRoot = new GameObject("CustomNPCAPI.Runtime");
            _driverRoot.AddComponent<CustomNpcDeveloperOverlay>();
            context.Logger.Info($"Custom NPC API {CustomNpcApi.ApiVersion} ready.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            CustomNpcApi.DeactivateHost();
            if (_driverRoot != null) UnityEngine.Object.Destroy(_driverRoot);
            _driverRoot = null;
            return Task.CompletedTask;
        }
    }
}
