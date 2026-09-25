#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;

[assembly: RegisterModClass(typeof(BigHax.BigHaxMod))]

namespace BigHax
{
    [ModEntryOnInitializationLoad]
    public sealed class BigHaxMod : IModBigAmbitions
    {
        private static readonly BigHaxSettings SharedSettings = new();
        private static readonly BigHaxOptions SharedOptions = new();
        private static BigHaxRuntime? activeRuntime;
        private static string? activeModId;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            if (activeModId == context.ModId && activeRuntime != null)
                return Task.CompletedTask;

            BigHaxLogger.StartDiagnosticSession();

            if (activeModId != null)
                SharedOptions.Shutdown();

            activeRuntime?.Shutdown();
            activeRuntime = null;

            SharedOptions.Initialize(context, SharedSettings);
            try
            {
                activeRuntime = BigHaxRuntime.Initialize(context, SharedSettings);
            }
            catch (Exception exception)
            {
                context.Logger.Error(exception);
                activeRuntime = null;
            }

            activeModId = context.ModId;
            BigHaxLogger.Info(context, "BigHax: runtime initialized.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            activeRuntime?.Shutdown();
            activeRuntime = null;
            SharedOptions.Shutdown();
            activeModId = null;
            return Task.CompletedTask;
        }
    }
}
