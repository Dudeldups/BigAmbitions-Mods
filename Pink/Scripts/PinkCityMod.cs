#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using UnityEngine;

[assembly: RegisterModClass(typeof(Pink.PinkCityMod))]

namespace Pink
{
    [ModEntryOnCityLoad]
    public sealed class PinkCityMod : IModBigAmbitions
    {
        internal static readonly bool EnableManualDebugMode = false;
        internal static readonly bool EnableLiveDebugMode = true;

        // Set this to false for the release build. The logger file/class can stay in the mod.
        private const bool EnableDebugLogging = false;

        // Keep false by default. Turn on only if you need every renderer/material decision in the log.
        private const bool EnableVerbosePatchLogging = false;

        private static GameObject? watcherObject;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            PinkRuntime.Initialize(
                context.ModId,
                context.Logger,
                EnableDebugLogging || EnableManualDebugMode || EnableLiveDebugMode,
                EnableVerbosePatchLogging);

            EnsureWatcher();
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (watcherObject != null)
            {
                UnityEngine.Object.Destroy(watcherObject);
                watcherObject = null;
            }

            PinkRuntime.Restore();
            PinkRuntime.Reset();
            return Task.CompletedTask;
        }

        private static void EnsureWatcher()
        {
            if (watcherObject == null)
            {
                watcherObject = new GameObject("Pink.CityWatcher");
                UnityEngine.Object.DontDestroyOnLoad(watcherObject);
            }

            var watcher = watcherObject.GetComponent<PinkWatcher>();
            if (watcher == null)
                watcher = watcherObject.AddComponent<PinkWatcher>();

            watcher.Initialize();
        }
    }

    [DefaultExecutionOrder(-10000)]
    internal sealed class PinkWatcher : MonoBehaviour
    {
        private const float FirstLoadingUiScanDelaySeconds = 0.05f;
        private const float LoadingUiScanIntervalSeconds = 0.35f;
        private const int MaxLoadingUiScanPasses = 12;
        private const float FirstHudUiScanDelaySeconds = 0.05f;
        private const float HudUiScanIntervalSeconds = 0.25f;
        private const int MaxHudUiScanPasses = 32;
        private const float WorldTintEarliestAfterHudReadySeconds = 0.75f;
        private const float WorldTintLatestAfterHudReadySeconds = 8f;
        private const float WorldTintReadinessPollIntervalSeconds = 0.25f;

        private float elapsedSeconds;
        private bool loadingUiScanStopped;
        private bool hudUiScanStopped;
        private bool gameplayReady;
        private bool worldTintApplied;
        private float gameplayReadyAtSeconds;
        private int loadingUiScanPasses;
        private int hudUiScanPasses;
        private float nextLoadingUiScanAtSeconds;
        private float nextHudUiScanAtSeconds;
        private float nextWorldTintReadinessCheckAtSeconds;

        internal void Initialize()
        {
            elapsedSeconds = 0f;
            loadingUiScanStopped = false;
            hudUiScanStopped = false;
            gameplayReady = false;
            worldTintApplied = false;
            gameplayReadyAtSeconds = -1f;
            loadingUiScanPasses = 0;
            hudUiScanPasses = 0;
            nextLoadingUiScanAtSeconds = FirstLoadingUiScanDelaySeconds;
            nextHudUiScanAtSeconds = FirstHudUiScanDelaySeconds;
            nextWorldTintReadinessCheckAtSeconds = FirstHudUiScanDelaySeconds;

            PinkFileLogger.Info(
                $"Pink watcher initialized. loadingUiInterval={LoadingUiScanIntervalSeconds:0.0}s hudUiInterval={HudUiScanIntervalSeconds:0.00}s maxLoadingUiPasses={MaxLoadingUiScanPasses} maxHudUiPasses={MaxHudUiScanPasses}",
                alsoGameLog: true);
        }

        private void Update()
        {
            elapsedSeconds += Time.unscaledDeltaTime;

            if (PinkCityMod.EnableLiveDebugMode)
            {
                PinkRuntime.HandleLiveDebugHotkeys();
                if (PinkRuntime.ShouldBlockGameplayInputForLiveDebug())
                    Input.ResetInputAxes();
            }
            else if (PinkCityMod.EnableManualDebugMode)
            {
                PinkRuntime.HandleManualDebugHotkeys();
            }

            if (!loadingUiScanStopped && elapsedSeconds >= nextLoadingUiScanAtSeconds)
            {
                loadingUiScanPasses++;
                PinkRuntime.ApplyLoadingUiTintPass();

                if (loadingUiScanPasses >= MaxLoadingUiScanPasses)
                    loadingUiScanStopped = true;
                else
                    nextLoadingUiScanAtSeconds = elapsedSeconds + LoadingUiScanIntervalSeconds;
            }

            if (!hudUiScanStopped && elapsedSeconds >= nextHudUiScanAtSeconds)
            {
                hudUiScanPasses++;
                var hudReadyThisPass = PinkRuntime.ApplyMainHudUiTintPass();
                if (hudReadyThisPass && !gameplayReady)
                {
                    gameplayReady = true;
                    gameplayReadyAtSeconds = elapsedSeconds;
                }

                if (gameplayReady || hudUiScanPasses >= MaxHudUiScanPasses)
                    hudUiScanStopped = true;
                else
                    nextHudUiScanAtSeconds = elapsedSeconds + HudUiScanIntervalSeconds;
            }

            if (!gameplayReady || worldTintApplied)
                return;

            if (gameplayReadyAtSeconds < 0f ||
                elapsedSeconds - gameplayReadyAtSeconds < WorldTintEarliestAfterHudReadySeconds ||
                elapsedSeconds < nextWorldTintReadinessCheckAtSeconds)
                return;

            var waitedSeconds = elapsedSeconds - gameplayReadyAtSeconds;
            var timedOut = waitedSeconds >= WorldTintLatestAfterHudReadySeconds;
            var worldReady = PinkRuntime.IsSinglePassWorldReady(out var readinessReason);
            if (!worldReady && !timedOut)
            {
                nextWorldTintReadinessCheckAtSeconds = elapsedSeconds + WorldTintReadinessPollIntervalSeconds;
                return;
            }

            worldTintApplied = true;
            var result = PinkRuntime.ApplyPinkPass(1);
            if (!result.Ready)
                return;

            PinkFileLogger.Info(
                $"Stopping pink scan. reason={(timedOut ? "single-post-hud-pass-timeout" : "single-post-hud-pass-ready")} readiness={readinessReason} " +
                $"hudUiPasses={hudUiScanPasses} loadingUiPasses={loadingUiScanPasses} waitedAfterHudReady={waitedSeconds:0.00}s " +
                $"foundTargets={result.FoundTargetCount} newCandidates={result.NewCandidateCount} newPatches={result.NewPatchCount} " +
                $"candidatesSeen={PinkRuntime.CandidateRootCount} processedVehicles={PinkRuntime.ProcessedVehicleRootCount} processedNpcs={PinkRuntime.ProcessedNpcRootCount} " +
                $"patchedMaterials={PinkRuntime.PatchedMaterialCount} patchedRendererSlots={PinkRuntime.PatchedRendererSlotCount} processedVehicleRenderers={PinkRuntime.ProcessedVehicleRendererCount} processedNpcRenderers={PinkRuntime.ProcessedNpcRendererCount}.",
                alsoGameLog: true);
        }

        private void OnGUI()
        {
            if (!PinkCityMod.EnableLiveDebugMode)
                return;

            PinkRuntime.DrawLiveDebugOverlay();
        }
    }
}
