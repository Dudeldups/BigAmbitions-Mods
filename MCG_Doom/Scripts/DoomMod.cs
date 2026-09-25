using System;
using System.Threading.Tasks;
using BAModAPI;

[assembly: RegisterModClass(typeof(MCG_Doom.DoomMod))]

namespace MCG_Doom
{
    [ModEntryOnCityLoad]
    public sealed class DoomMod : IModBigAmbitions
    {
        private IDisposable _registration;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            _registration?.Dispose();
            _registration = DoomRegistration.Register(context);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            _registration?.Dispose();
            _registration = null;
            return Task.CompletedTask;
        }
    }
}
