#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.SoundSystem;
using Data.VehicleColors;
using Entities;
using Extensions;
using Helpers;
using Localizor;
using Localizor.LanguageChangeEvent;
using Player.HUD.SmartphoneUI;
using UI;
using UI.Elements;
using UI.Overlays;
using UI.PurchaseVehicle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(VehicleRepainter.VehicleRepainterMod))]

namespace VehicleRepainter
{
    [ModEntryOnInitializationLoad]
    public sealed class VehicleRepainterMod : IModBigAmbitions
    {
        private VehicleRepainterRuntime? runtime;
        private VehicleRepainterLifecycle? lifecycle;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            runtime = new VehicleRepainterRuntime(context);
            lifecycle = VehicleRepainterLifecycle.Initialize(runtime);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            lifecycle?.Shutdown();
            lifecycle = null;
            runtime?.Uninstall();
            runtime = null;
            return Task.CompletedTask;
        }
    }

    internal sealed class VehicleRepainterLifecycle : MonoBehaviour
    {
        private Coroutine? pendingInstall;
        private Coroutine? pendingPersistenceRestore;
        private VehicleRepainterRuntime? runtime;
        private VehicleRepainterUpdateNoticeUi? updateNoticeUi;

        internal static VehicleRepainterLifecycle Initialize(VehicleRepainterRuntime runtime)
        {
            var lifecycleObject = new GameObject(nameof(VehicleRepainterLifecycle));
            DontDestroyOnLoad(lifecycleObject);
            var lifecycle = lifecycleObject.AddComponent<VehicleRepainterLifecycle>();
            lifecycle.runtime = runtime;
            lifecycle.updateNoticeUi = new VehicleRepainterUpdateNoticeUi(runtime);
            lifecycle.SubscribeGlobalEvents();
            GlobalEvents.RegisterOnGameLoadedLateCallback(lifecycle.HandleGameLoadedLate);
            lifecycle.ScheduleInstall("mod-load");
            return lifecycle;
        }

        internal void Shutdown()
        {
            if (pendingInstall != null)
            {
                StopCoroutine(pendingInstall);
                pendingInstall = null;
            }

            if (pendingPersistenceRestore != null)
            {
                StopCoroutine(pendingPersistenceRestore);
                pendingPersistenceRestore = null;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnsubscribeGlobalEvents();
            updateNoticeUi?.Shutdown();
            updateNoticeUi = null;
            runtime = null;
            Destroy(gameObject);
        }

        private void OnGUI()
        {
            updateNoticeUi?.OnGui();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SubscribeGlobalEvents();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnsubscribeGlobalEvents();
        }

        private void SubscribeGlobalEvents()
        {
            GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
            GlobalEvents.onGameUnloaded += HandleGameUnloaded;
        }

        private void UnsubscribeGlobalEvents()
        {
            GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SubscribeGlobalEvents();
            GlobalEvents.RegisterOnGameLoadedLateCallback(HandleGameLoadedLate);
            ScheduleInstall($"scene-loaded:{scene.name}");
        }

        private void HandleGameLoadedLate()
        {
            SubscribeGlobalEvents();
            ScheduleInstall("game-loaded-late");
        }

        private void HandleGameUnloaded()
        {
            if (pendingInstall != null)
            {
                StopCoroutine(pendingInstall);
                pendingInstall = null;
            }

            if (pendingPersistenceRestore != null)
            {
                StopCoroutine(pendingPersistenceRestore);
                pendingPersistenceRestore = null;
            }

            runtime?.Uninstall();
        }

        private void ScheduleInstall(string source)
        {
            if (pendingInstall != null)
                StopCoroutine(pendingInstall);

            pendingInstall = StartCoroutine(InstallAfterSceneSetup(source));
        }

        private IEnumerator InstallAfterSceneSetup(string source)
        {
            yield return null;
            pendingInstall = null;
            runtime?.Install(source);

            if (pendingPersistenceRestore != null)
                StopCoroutine(pendingPersistenceRestore);

            pendingPersistenceRestore = StartCoroutine(RestorePersistenceAfterLoad(source));
        }

        private IEnumerator RestorePersistenceAfterLoad(string source)
        {
            const float scanIntervalSeconds = 0.5f;
            object? observedSaveGame = null;
            var pass = 1;

            while (true)
            {
                var currentSaveGame = (object?)SaveGameManager.Current;
                if (!ReferenceEquals(currentSaveGame, observedSaveGame))
                {
                    observedSaveGame = currentSaveGame;
                    pass = 1;
                }

                runtime?.RestorePersistenceState(source, pass);
                pass++;
                yield return new WaitForSecondsRealtime(scanIntervalSeconds);
            }
        }
    }

    internal sealed class VehicleRepainterRuntime
    {
        internal const float BaseRepaintPrice = 800f;
        private const string CustomColorRestoredEvent = "vehicle-repainter:color-restored";
        private const string PaintFinishModDataKey = "vehicle-repainter:finishes:v1";
        private const string FactoryFinishId = "factory";

        private static class DebugOptions
        {
            private const string GlobalDebugMarker = "vehicle-repainter.debug";
            private const string ButtonDebugMarker = "button-diagnostics.debug";
            private const string InteractionDebugMarker = "interaction-diagnostics.debug";
            private const string PersistenceDebugMarker = "color-persistence.debug";
            private const string FinishDebugMarker = "finish-diagnostics.debug";
            private const string UpdateNoticeDebugMarker = "update-notice-diagnostics.debug";

            internal static bool EnableDebugLogging = false;
            internal static bool EnableButtonDiagnostics = false;
            internal static bool EnableInteractionDiagnostics = false;
            internal static bool EnablePersistenceDiagnostics = false;
            internal static bool EnableFinishDiagnostics = false;
            internal static bool EnableUpdateNoticeDiagnostics = false;

            internal static void Configure(string modId)
            {
                EnableDebugLogging = false;
                EnableButtonDiagnostics = false;
                EnableInteractionDiagnostics = false;
                EnablePersistenceDiagnostics = false;
                EnableFinishDiagnostics = false;
                EnableUpdateNoticeDiagnostics = false;
                if (string.IsNullOrWhiteSpace(modId) || !Directory.Exists(modId))
                    return;

                var configDirectory = Path.Combine(modId, "Config");
                EnableDebugLogging = File.Exists(Path.Combine(configDirectory, GlobalDebugMarker));
                EnableButtonDiagnostics = File.Exists(Path.Combine(configDirectory, ButtonDebugMarker));
                EnableInteractionDiagnostics = File.Exists(Path.Combine(configDirectory, InteractionDebugMarker));
                EnablePersistenceDiagnostics = File.Exists(Path.Combine(configDirectory, PersistenceDebugMarker));
                EnableFinishDiagnostics = File.Exists(Path.Combine(configDirectory, FinishDebugMarker));
                EnableUpdateNoticeDiagnostics = File.Exists(Path.Combine(configDirectory, UpdateNoticeDebugMarker));
            }
        }

        private static readonly FieldInfo? CurrentStationTriggerField = typeof(GasStationOverlay).GetField(
            "_currentStationTrigger",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? PurchaseButtonField = typeof(PurchaseVehicleUI).GetField(
            "purchaseButton",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo? SetAssetPriceMethod = typeof(PurchaseVehicleUI).GetMethod(
            "SetAssetPrice",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? ColorsGridLayoutGroupField = typeof(PurchaseVehicleUI).GetField(
            "colorsGridLayoutGroup",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? VehicleColorBackingField = typeof(CarFeatures).GetField(
            "<VehicleColor>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PaintFinishDefinition[] PaintFinishes =
        {
            new PaintFinishDefinition(FactoryFinishId, "vehicle-repainter:finish_factory", BaseRepaintPrice, 0f, 0f, 0f, true),
            new PaintFinishDefinition("matte", "vehicle-repainter:finish_matte", 1000f, 0.5f, 0.12f, 0f),
            new PaintFinishDefinition("satin", "vehicle-repainter:finish_satin", 1200f, 0.75f, 0.42f, 0f),
            new PaintFinishDefinition("gloss", "vehicle-repainter:finish_gloss", 1400f, 1.15f, 0.82f, 0.35f),
            new PaintFinishDefinition("high_gloss", "vehicle-repainter:finish_high_gloss", 1800f, 1.5f, 0.98f, 1f)
        };

        private static readonly CustomColorDefinition[] AdditionalColors =
        {
            new CustomColorDefinition("VehicleRepainter_Onyx", new Color32(20, 23, 28, 255), new Color32(70, 78, 90, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Charcoal", new Color32(43, 47, 54, 255), new Color32(105, 112, 125, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Graphite", new Color32(65, 68, 72, 255), new Color32(125, 130, 138, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Gunmetal", new Color32(70, 82, 90, 255), new Color32(135, 155, 170, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Smoke", new Color32(105, 110, 115, 255), new Color32(170, 178, 185, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Ash", new Color32(135, 140, 145, 255), new Color32(195, 202, 210, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Platinum", new Color32(185, 190, 195, 255), new Color32(240, 245, 250, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Pearl", new Color32(215, 220, 225, 255), new Color32(255, 255, 255, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_White", new Color32(238, 238, 232, 255), new Color32(255, 255, 255, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Cream", new Color32(250, 225, 175, 255), new Color32(255, 245, 215, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Ivory", new Color32(245, 235, 210, 255), new Color32(255, 250, 225, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Champagne", new Color32(224, 202, 160, 255), new Color32(255, 235, 195, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Slate", new Color32(80, 95, 110, 255), new Color32(150, 170, 190, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Maroon", new Color32(75, 0, 20, 255), new Color32(155, 45, 70, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Raspberry", new Color32(155, 25, 75, 255), new Color32(235, 90, 135, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_DeepRed", new Color32(120, 0, 0, 255), new Color32(205, 55, 45, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Burgundy", new Color32(105, 16, 38, 255), new Color32(185, 65, 90, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Cherry", new Color32(170, 10, 45, 255), new Color32(245, 75, 105, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_BrickRed", new Color32(145, 45, 35, 255), new Color32(220, 105, 85, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Scarlet", new Color32(220, 30, 20, 255), new Color32(255, 105, 80, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Coral", new Color32(238, 83, 74, 255), new Color32(255, 160, 140, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Salmon", new Color32(245, 125, 115, 255), new Color32(255, 195, 180, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Terracotta", new Color32(185, 80, 55, 255), new Color32(245, 145, 115, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Chocolate", new Color32(90, 45, 25, 255), new Color32(170, 100, 65, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Taupe", new Color32(120, 100, 85, 255), new Color32(190, 165, 140, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Peach", new Color32(255, 160, 105, 255), new Color32(255, 215, 175, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Apricot", new Color32(240, 175, 95, 255), new Color32(255, 225, 155, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Sand", new Color32(194, 155, 105, 255), new Color32(245, 210, 165, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Tangerine", new Color32(245, 125, 25, 255), new Color32(255, 190, 95, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Orange", new Color32(255, 106, 0, 255), new Color32(255, 175, 85, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Copper", new Color32(166, 79, 45, 255), new Color32(235, 145, 95, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Bronze", new Color32(140, 90, 40, 255), new Color32(220, 160, 90, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Brown", new Color32(83, 43, 27, 255), new Color32(160, 95, 60, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Amber", new Color32(255, 170, 0, 255), new Color32(255, 225, 95, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Gold", new Color32(196, 145, 35, 255), new Color32(255, 220, 115, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Ochre", new Color32(185, 120, 30, 255), new Color32(245, 185, 90, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Honey", new Color32(215, 160, 55, 255), new Color32(255, 220, 125, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Mustard", new Color32(170, 135, 25, 255), new Color32(235, 205, 85, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Lemon", new Color32(240, 225, 35, 255), new Color32(255, 250, 120, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Khaki", new Color32(160, 150, 95, 255), new Color32(225, 215, 150, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Olive", new Color32(110, 110, 20, 255), new Color32(190, 190, 75, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Chartreuse", new Color32(155, 220, 25, 255), new Color32(215, 255, 105, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Avocado", new Color32(110, 145, 45, 255), new Color32(180, 215, 105, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Lime", new Color32(104, 190, 35, 255), new Color32(180, 255, 100, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_SpringGreen", new Color32(35, 200, 80, 255), new Color32(115, 255, 150, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Moss", new Color32(85, 110, 45, 255), new Color32(155, 190, 100, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Sage", new Color32(120, 150, 105, 255), new Color32(185, 215, 165, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Forest", new Color32(25, 85, 40, 255), new Color32(80, 170, 100, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Pine", new Color32(10, 70, 55, 255), new Color32(70, 155, 125, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Emerald", new Color32(0, 120, 72, 255), new Color32(70, 220, 145, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Jade", new Color32(35, 155, 105, 255), new Color32(105, 235, 175, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Mint", new Color32(85, 210, 150, 255), new Color32(160, 255, 210, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Celadon", new Color32(145, 210, 175, 255), new Color32(210, 255, 230, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Seafoam", new Color32(120, 220, 185, 255), new Color32(195, 255, 230, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Turquoise", new Color32(0, 157, 154, 255), new Color32(80, 240, 230, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Lagoon", new Color32(0, 130, 135, 255), new Color32(70, 215, 215, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Teal", new Color32(0, 105, 110, 255), new Color32(65, 195, 195, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Petrol", new Color32(20, 80, 95, 255), new Color32(80, 165, 185, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Aqua", new Color32(45, 220, 220, 255), new Color32(140, 255, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Cyan", new Color32(0, 174, 239, 255), new Color32(95, 225, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_IceBlue", new Color32(155, 215, 235, 255), new Color32(220, 250, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Cerulean", new Color32(25, 145, 205, 255), new Color32(100, 210, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_SkyBlue", new Color32(85, 180, 240, 255), new Color32(165, 225, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_SteelBlue", new Color32(65, 105, 145, 255), new Color32(130, 185, 230, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Denim", new Color32(45, 90, 145, 255), new Color32(105, 165, 225, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Azure", new Color32(0, 112, 221, 255), new Color32(90, 185, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_RoyalBlue", new Color32(35, 65, 190, 255), new Color32(100, 135, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Cobalt", new Color32(30, 55, 125, 255), new Color32(85, 120, 220, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_MidnightBlue", new Color32(15, 30, 70, 255), new Color32(60, 90, 160, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Periwinkle", new Color32(115, 135, 220, 255), new Color32(180, 195, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Indigo", new Color32(55, 45, 145, 255), new Color32(120, 105, 225, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Violet", new Color32(105, 66, 180, 255), new Color32(175, 135, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Amethyst", new Color32(140, 75, 180, 255), new Color32(210, 145, 245, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Grape", new Color32(80, 35, 120, 255), new Color32(150, 95, 205, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Lilac", new Color32(145, 105, 190, 255), new Color32(210, 170, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Lavender", new Color32(170, 125, 215, 255), new Color32(225, 190, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Plum", new Color32(105, 40, 115, 255), new Color32(180, 100, 195, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Mauve", new Color32(160, 115, 150, 255), new Color32(225, 175, 215, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Orchid", new Color32(205, 95, 210, 255), new Color32(250, 165, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Magenta", new Color32(194, 0, 151, 255), new Color32(255, 90, 225, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Fuchsia", new Color32(235, 30, 190, 255), new Color32(255, 125, 225, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_HotPink", new Color32(255, 80, 165, 255), new Color32(255, 170, 215, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Blush", new Color32(225, 145, 165, 255), new Color32(255, 205, 215, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Rose", new Color32(230, 70, 125, 255), new Color32(255, 150, 190, 255), 1f)
        };

        private readonly ModContext context;
        private readonly Dictionary<string, VehicleColor> customVehicleColors =
            new Dictionary<string, VehicleColor>(StringComparer.Ordinal);
        private readonly List<VehicleColor> ownedCustomVehicleColors = new List<VehicleColor>();
        private readonly List<GasStationTrigger> observedStationTriggers = new List<GasStationTrigger>();
        private readonly HashSet<string> reportedButtonFailures = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> reportedPersistenceFailures = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<int, string> restoredCustomVehicleColorsByControllerId =
            new Dictionary<int, string>();
        private readonly Dictionary<int, VehicleFinishState> finishStatesByControllerId =
            new Dictionary<int, VehicleFinishState>();
        private readonly Dictionary<int, string> appliedFinishByControllerId =
            new Dictionary<int, string>();
        private readonly Dictionary<string, string> savedFinishByVehicleId =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static VehicleRepainterRuntime? activeRuntime;
        private object? observedFinishSaveGame;
        private bool privateDriverPaintHooksInstalled;
        private OverlayUI? overlayUi;
        private GasStationOverlay? originalGasStationOverlay;
        private ExtendedGasStationOverlay? extendedGasStationOverlay;
        private RepaintPurchasableAsset? activeRepaintAsset;
        private Coroutine? pendingRepairOverlayRefresh;

        internal VehicleRepainterRuntime(ModContext context)
        {
            this.context = context;
            DebugOptions.Configure(context.ModId);
            TraceButton($"Diagnostic mode enabled for modId='{context.ModId}'.");
            TraceFinish(
                $"Finish diagnostics enabled for modId='{context.ModId}'; " +
                $"global={DebugOptions.EnableDebugLogging}, finish={DebugOptions.EnableFinishDiagnostics}.");
        }

        internal string ModId => context.ModId;

        internal void TraceUpdateNotice(string message)
        {
            if (DebugOptions.EnableDebugLogging && DebugOptions.EnableUpdateNoticeDiagnostics)
                context.Logger.Info($"Vehicle Repainter update notice: {message}");
        }

        internal void Install(string source)
        {
            var uis = InstanceBehavior<UIs>.Instance;
            if (uis == null || uis.overlayUI == null)
            {
                TraceButton($"Deferred installation source='{source}': the game's overlay UI is not ready.");
                return;
            }

            if (extendedGasStationOverlay != null && overlayUi != null &&
                ReferenceEquals(overlayUi, uis.overlayUI) &&
                ReferenceEquals(overlayUi.gasStation, extendedGasStationOverlay))
            {
                EnsurePrivateDriverPaintHooks();
                TraceButton($"Installation already active source='{source}'.");
                return;
            }

            if (extendedGasStationOverlay != null || ownedCustomVehicleColors.Count > 0)
                Uninstall();

            if (CurrentStationTriggerField == null || PurchaseButtonField == null || SetAssetPriceMethod == null ||
                ColorsGridLayoutGroupField == null || VehicleColorBackingField == null)
            {
                context.Logger.Error(
                    "Could not install the Repaint button: required cached vanilla fields were not found; " +
                    $"stationTriggerField={(CurrentStationTriggerField == null ? "missing" : "found")}, " +
                    $"purchaseButtonField={(PurchaseButtonField == null ? "missing" : "found")}, " +
                    $"setAssetPriceMethod={(SetAssetPriceMethod == null ? "missing" : "found")}, " +
                    $"colorsGridField={(ColorsGridLayoutGroupField == null ? "missing" : "found")}, " +
                    $"vehicleColorField={(VehicleColorBackingField == null ? "missing" : "found")}.");
                return;
            }

            if (!InitializeCustomVehicleColors())
                return;

            overlayUi = uis.overlayUI;
            originalGasStationOverlay = overlayUi.gasStation;
            extendedGasStationOverlay = new ExtendedGasStationOverlay(this);
            overlayUi.gasStation = extendedGasStationOverlay;
            ObserveStationTriggers();
            activeRuntime = this;
            EnsurePrivateDriverPaintHooks();
            TraceInteraction($"Installed gas-station overlay extension source='{source}'.");
            TraceButton(
                $"Installed gas-station overlay extension source='{source}'; previousOverlay='{originalGasStationOverlay?.GetType().FullName ?? "null"}', " +
                $"observedTriggers={observedStationTriggers.Count}.");
        }

        internal void Uninstall()
        {
            TraceInteraction("Uninstalling the gas-station overlay extension.");
            if (activeRepaintAsset != null && PurchaseVehicleUI.IsPanelOpen)
                InstanceBehavior<UIs>.Instance?.playerHUD?.purchaseVehicleUI?.Close();

            activeRepaintAsset = null;
            StopObservingStationTriggers();
            var uis = InstanceBehavior<UIs>.Instance;
            if (uis != null && uis.overlayUI != null && extendedGasStationOverlay != null &&
                ReferenceEquals(uis.overlayUI.gasStation, extendedGasStationOverlay))
            {
                GasStationOverlay.Hide();
                uis.overlayUI.gasStation = originalGasStationOverlay ?? new GasStationOverlay();
            }

            overlayUi = null;
            originalGasStationOverlay = null;
            extendedGasStationOverlay = null;
            ReleasePrivateDriverPaintHooks();
            if (ReferenceEquals(activeRuntime, this))
                activeRuntime = null;
            ReleaseCustomVehicleColors();
        }

        private void ObserveStationTriggers()
        {
            var repairStationCount = 0;
            foreach (var trigger in Resources.FindObjectsOfTypeAll<GasStationTrigger>())
            {
                if (trigger == null || !trigger.gameObject.scene.IsValid())
                    continue;

                trigger.onEntered += HandleStationEntered;
                observedStationTriggers.Add(trigger);
                if (trigger.isRepairStation)
                    repairStationCount++;
            }

            TraceButton(
                $"Observed gas-station triggers: total={observedStationTriggers.Count}, repair={repairStationCount}.");
            if (repairStationCount == 0)
            {
                TraceButton(
                    "No loaded repair-station triggers were found during installation. " +
                    "The Repaint button may be unavailable because the service-bay entry events could not be observed.");
            }
        }

        private void StopObservingStationTriggers()
        {
            if (pendingRepairOverlayRefresh != null && overlayUi != null)
                overlayUi.StopCoroutine(pendingRepairOverlayRefresh);

            pendingRepairOverlayRefresh = null;
            foreach (var trigger in observedStationTriggers)
            {
                if (trigger != null)
                    trigger.onEntered -= HandleStationEntered;
            }

            observedStationTriggers.Clear();
        }

        private void HandleStationEntered(GasStationTrigger enteredTrigger)
        {
            reportedButtonFailures.Clear();
            if (overlayUi == null || extendedGasStationOverlay == null ||
                !ReferenceEquals(overlayUi.gasStation, extendedGasStationOverlay))
            {
                WarnButtonFailureOnce(
                    "overlay-replaced",
                    "The Repaint button cannot be injected because the active gas-station overlay is no longer " +
                    $"Vehicle Repainter's extension; activeOverlay='{overlayUi?.gasStation?.GetType().FullName ?? "null"}'. " +
                    "Another mod or a later game initialization step may have replaced it.");
            }

            var vehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
            if (vehicle == null || vehicle.vehicleCollider == null)
            {
                var message =
                    "A gas-station trigger was entered, but the Repaint button cannot be evaluated because " +
                    $"selectedVehicle={(vehicle == null ? "null" : "present")}, " +
                    $"vehicleCollider={(vehicle?.vehicleCollider == null ? "null" : "present")}, " +
                    $"trigger='{DescribeTrigger(enteredTrigger)}'.";
                if (enteredTrigger.isRepairStation)
                    WarnButtonFailureOnce("entry-no-vehicle", message);
                else
                    TraceButton(message);
                return;
            }

            var repairTrigger = enteredTrigger.isRepairStation
                ? enteredTrigger
                : observedStationTriggers.FirstOrDefault(trigger =>
                    trigger != null && trigger.isActiveAndEnabled && trigger.isRepairStation &&
                    trigger.stationCollider != null && trigger.IntersectsBounds(vehicle.vehicleCollider.bounds));
            if (repairTrigger == null)
            {
                TraceButton(
                    $"Skipped repaint overlay refresh for trigger='{DescribeTrigger(enteredTrigger)}': " +
                    "no intersecting repair-station trigger was found.");
                return;
            }

            TraceButton(
                $"Repair-station entry resolved: entered='{DescribeTrigger(enteredTrigger)}', " +
                $"repair='{DescribeTrigger(repairTrigger)}', vehicle='{DescribeVehicle(vehicle)}'.");

            GasStationOverlay.Show(repairTrigger);
            if (overlayUi == null)
                return;

            if (pendingRepairOverlayRefresh != null)
                overlayUi.StopCoroutine(pendingRepairOverlayRefresh);

            pendingRepairOverlayRefresh = overlayUi.StartCoroutine(
                ReassertRepairOverlayNextFrame(vehicle, repairTrigger));
        }

        private IEnumerator ReassertRepairOverlayNextFrame(
            VehicleController vehicle,
            GasStationTrigger repairTrigger)
        {
            yield return null;
            pendingRepairOverlayRefresh = null;

            if (vehicle != null && vehicle.vehicleCollider != null && repairTrigger != null &&
                repairTrigger.isActiveAndEnabled && repairTrigger.IntersectsBounds(vehicle.vehicleCollider.bounds))
            {
                GasStationOverlay.Show(repairTrigger);
                TraceButton(
                    $"Reasserted repair overlay for trigger='{DescribeTrigger(repairTrigger)}', " +
                    $"vehicle='{DescribeVehicle(vehicle)}'.");
            }
            else
            {
                WarnButtonFailureOnce(
                    "reassert-invalid-state",
                    "The Repaint button overlay was not reasserted on the next frame because the repair trigger " +
                    $"or vehicle was no longer valid; trigger='{DescribeTrigger(repairTrigger)}', " +
                    $"vehicle='{DescribeVehicle(vehicle)}'.");
            }
        }

        internal void WarnButtonFailureOnce(string reason, string message)
        {
            if (reportedButtonFailures.Add(reason))
                context.Logger.Warn($"Vehicle Repainter button unavailable [{reason}]: {message}");
        }

        internal void TraceButton(string message)
        {
            if (DebugOptions.EnableDebugLogging && DebugOptions.EnableButtonDiagnostics)
                context.Logger.Info($"Vehicle Repainter button diagnostic: {message}");
        }

        private void TracePersistence(string message)
        {
            if (DebugOptions.EnableDebugLogging && DebugOptions.EnablePersistenceDiagnostics)
                context.Logger.Info($"Vehicle Repainter paint persistence: {message}");
        }

        private void TraceFinish(string message)
        {
            if (DebugOptions.EnableDebugLogging && DebugOptions.EnableFinishDiagnostics)
                context.Logger.Info($"Vehicle Repainter finish diagnostic: {message}");
        }

        private static string DescribeTrigger(GasStationTrigger? trigger)
        {
            if (trigger == null)
                return "null";

            return $"{trigger.name}#{trigger.GetInstanceID()}" +
                   $"(active={trigger.isActiveAndEnabled}, repair={trigger.isRepairStation}, " +
                   $"truckGarage={trigger.isTruckGarage}, collider={(trigger.stationCollider == null ? "null" : "present")})";
        }

        private static string DescribeVehicle(VehicleController? vehicle)
        {
            if (vehicle == null)
                return "null";

            return $"{vehicle.name}#{vehicle.GetInstanceID()}" +
                   $"(instance={(vehicle.vehicleInstance == null ? "null" : vehicle.vehicleInstance.id)}, " +
                   $"type={(vehicle.vehicleType == null ? "null" : vehicle.vehicleType.vehicleTypeName)}, " +
                   $"motor={vehicle.vehicleType != null && vehicle.vehicleType.IsMotorVehicle}, " +
                   $"carFeatures={(vehicle.CarFeatures == null ? "null" : "present")}, " +
                   $"collider={(vehicle.vehicleCollider == null ? "null" : "present")})";
        }

        internal GasStationTrigger? GetCurrentStationTrigger(GasStationOverlay overlay)
        {
            return CurrentStationTriggerField?.GetValue(overlay) as GasStationTrigger;
        }

        internal void OpenRepaintUi(GasStationTrigger stationTrigger)
        {
            var vehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
            var colors = InstanceBehavior<GlobalReferences>.Instance?.vehicleColors;
            if (vehicle == null || vehicle.vehicleInstance == null || vehicle.CarFeatures == null ||
                colors == null || colors.Length == 0)
            {
                context.Logger.Warn("Repaint was requested, but the active vehicle does not expose the normal vehicle color system.");
                return;
            }

            var purchaseUi = InstanceBehavior<UIs>.Instance?.playerHUD?.purchaseVehicleUI;
            if (purchaseUi == null)
            {
                context.Logger.Error("Could not open Repaint: the vanilla vehicle color UI is unavailable.");
                return;
            }

            if (PurchaseVehicleUI.IsPanelOpen)
            {
                context.Logger.Warn("Could not open Repaint because another vehicle purchase panel is already open.");
                return;
            }

            if (vehicle.vehicleCollider == null || !stationTrigger.IntersectsBounds(vehicle.vehicleCollider.bounds) ||
                !Mathf.Approximately(vehicle.CurrentSpeed, 0f))
            {
                context.Logger.Warn("Could not open Repaint because the active vehicle is no longer stopped inside the service bay.");
                return;
            }

            var repaintAsset = new RepaintPurchasableAsset(
                this,
                context,
                vehicle,
                stationTrigger,
                HandleRepaintUiClosed);
            activeRepaintAsset = repaintAsset;
            GasStationOverlay.Hide(stationTrigger);
            purchaseUi.SetAsset(repaintAsset);
            repaintAsset.ApplyColorGridLayout(purchaseUi);
            repaintAsset.BeginSession();

            if (PurchaseButtonField!.GetValue(purchaseUi) is Button purchaseButton)
            {
                var label = purchaseButton.GetComponentInChildren<TextLocalizationComponent>(true);
                if (label != null)
                    label.Key = "vehicle-repainter:confirm";
            }
            repaintAsset.RefreshPrice(purchaseUi);
        }

        private void HandleRepaintUiClosed(RepaintPurchasableAsset repaintAsset)
        {
            if (ReferenceEquals(activeRepaintAsset, repaintAsset))
                activeRepaintAsset = null;
        }

        private bool InitializeCustomVehicleColors()
        {
            var globalReferences = InstanceBehavior<GlobalReferences>.Instance;
            if (globalReferences == null || globalReferences.vehicleColors == null)
            {
                context.Logger.Error("Could not install: the game's vehicle color registry is unavailable.");
                return false;
            }

            var registeredColors = globalReferences.vehicleColors.Where(color => color != null).ToList();
            foreach (var definition in AdditionalColors)
            {
                var color = registeredColors.FirstOrDefault(existing =>
                    string.Equals(((UnityEngine.Object)existing).name, definition.Name, StringComparison.Ordinal));
                if (color == null)
                {
                    color = ScriptableObject.CreateInstance<VehicleColor>();
                    ((UnityEngine.Object)color).name = definition.Name;
                    color.tint = definition.Tint;
                    color.fresnelColor = definition.FresnelColor;
                    color.fresnelPower = definition.FresnelPower;
                    color.randomWeight = 0f;
                    color.hideFlags = HideFlags.HideAndDontSave;
                }

                customVehicleColors[definition.Name] = color;
                ownedCustomVehicleColors.Add(color);
            }

            // Older versions registered these colors globally, which caused vanilla dealers to
            // display the repaint-only palette. Remove any stale entries while keeping the assets
            // alive in this runtime-owned collection for previews and saved-vehicle restoration.
            var dealerColors = registeredColors
                .Where(color => !customVehicleColors.ContainsKey(((UnityEngine.Object)color).name))
                .ToArray();
            if (dealerColors.Length != registeredColors.Count)
                globalReferences.vehicleColors = dealerColors;

            GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
            GlobalEvents.onEnterVehicle += HandleVehicleEntered;
            GlobalEvents.onExitVehicle -= HandleVehicleExited;
            GlobalEvents.onExitVehicle += HandleVehicleExited;
            return true;
        }

        internal List<VehicleColor> GetRepaintColors()
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance?.vehicleColors?
                .Where(color => color != null &&
                                !customVehicleColors.ContainsKey(((UnityEngine.Object)color).name))
                .ToList() ?? new List<VehicleColor>();

            foreach (var definition in AdditionalColors)
            {
                if (customVehicleColors.TryGetValue(definition.Name, out var color))
                    colors.Add(color);
            }

            return colors;
        }

        internal bool TryResolveVehicleColor(string colorName, out VehicleColor vehicleColor)
        {
            return customVehicleColors.TryGetValue(colorName, out vehicleColor) ||
                   VehicleHelper.TryGetVehicleColor(colorName, out vehicleColor);
        }

        internal IReadOnlyList<PaintFinishDefinition> GetPaintFinishes() => PaintFinishes;

        internal float GetRepaintPrice(string finishId)
        {
            return TryGetPaintFinish(finishId, out var finish) ? finish.Price : BaseRepaintPrice;
        }

        internal string GetSavedFinishId(string vehicleId)
        {
            EnsureFinishStoreLoaded();
            return !string.IsNullOrWhiteSpace(vehicleId) && savedFinishByVehicleId.TryGetValue(vehicleId, out var finishId)
                ? finishId
                : FactoryFinishId;
        }

        internal void SaveFinish(string vehicleId, string finishId)
        {
            if (string.IsNullOrWhiteSpace(vehicleId))
                return;

            EnsureFinishStoreLoaded();
            if (string.Equals(finishId, FactoryFinishId, StringComparison.Ordinal))
                savedFinishByVehicleId.Remove(vehicleId);
            else if (TryGetPaintFinish(finishId, out _))
                savedFinishByVehicleId[vehicleId] = finishId;
            else
                return;

            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
                return;

            var data = new PaintFinishSaveData();
            foreach (var pair in savedFinishByVehicleId.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                data.entries.Add(new PaintFinishSaveEntry(pair.Key, pair.Value));

            saveGame.modData ??= new Dictionary<string, string>();
            if (data.entries.Count == 0)
                saveGame.modData.Remove(PaintFinishModDataKey);
            else
                saveGame.modData[PaintFinishModDataKey] = JsonUtility.ToJson(data);

            TraceFinish($"Saved finish '{finishId}' for vehicle id='{vehicleId}'.");
        }

        internal bool SupportsPaintFinish(CarFeatures? carFeatures, int controllerId)
        {
            return carFeatures != null && GetOrCreateFinishState(carFeatures, controllerId).Bindings.Count > 0;
        }

        internal bool ApplyPaintFinish(
            CarFeatures? carFeatures,
            int controllerId,
            string finishId,
            string source,
            bool force = false)
        {
            if (carFeatures == null || !TryGetPaintFinish(finishId, out var finish))
                return false;

            var state = GetOrCreateFinishState(carFeatures, controllerId);
            if (state.Bindings.Count == 0)
                return false;

            if (!force && appliedFinishByControllerId.TryGetValue(controllerId, out var appliedFinishId) &&
                string.Equals(appliedFinishId, finish.Id, StringComparison.Ordinal) &&
                state.Matches(finish))
            {
                return false;
            }

            state.Apply(finish, bindingDiagnostic =>
                TraceFinish($"Finish binding controller={controllerId}, source='{source}': {bindingDiagnostic}"));
            appliedFinishByControllerId[controllerId] = finish.Id;
            TraceFinish(
                $"Applied finish '{finish.Id}' to controller={controllerId}, " +
                $"renderers={state.Bindings.Count}, source='{source}'.");
            return true;
        }

        internal void InvalidateAppliedFinish(int controllerId)
        {
            appliedFinishByControllerId.Remove(controllerId);
        }

        private bool RestoreSavedPaintFinish(
            CarFeatures? carFeatures,
            int controllerId,
            VehicleInstance vehicleInstance,
            string source)
        {
            var finishId = GetSavedFinishId(vehicleInstance.id);
            var restored = ApplyPaintFinish(carFeatures, controllerId, finishId, source);
            if (!restored && carFeatures != null && !SupportsPaintFinish(carFeatures, controllerId) &&
                !string.Equals(finishId, FactoryFinishId, StringComparison.Ordinal))
            {
                WarnPersistenceFailureOnce(
                    $"finish:{vehicleInstance.id}:unsupported",
                    $"Could not restore finish '{finishId}' for vehicle id='{vehicleInstance.id}', " +
                    $"type='{vehicleInstance.vehicleTypeName}', source='{source}' because none of its body meshes " +
                    "exposes a supported smoothness shader property.");
            }

            return restored;
        }

        private VehicleFinishState GetOrCreateFinishState(CarFeatures carFeatures, int controllerId)
        {
            if (finishStatesByControllerId.TryGetValue(controllerId, out var state) && state.Matches(carFeatures))
                return state;

            state = VehicleFinishState.Capture(carFeatures);
            finishStatesByControllerId[controllerId] = state;
            appliedFinishByControllerId.Remove(controllerId);
            TraceFinish(
                $"Captured factory finish for controller={controllerId}; supportedRenderers={state.Bindings.Count}.");
            foreach (var binding in state.Bindings)
                TraceFinish($"Captured finish binding controller={controllerId}: {binding.DescribeState("capture", null)}");
            return state;
        }

        private void EnsureFinishStoreLoaded()
        {
            var saveGame = SaveGameManager.Current;
            if (ReferenceEquals(observedFinishSaveGame, saveGame))
                return;

            observedFinishSaveGame = saveGame;
            savedFinishByVehicleId.Clear();
            finishStatesByControllerId.Clear();
            appliedFinishByControllerId.Clear();
            if (saveGame?.modData == null ||
                !saveGame.modData.TryGetValue(PaintFinishModDataKey, out var serialized) ||
                string.IsNullOrWhiteSpace(serialized))
            {
                return;
            }

            try
            {
                var data = JsonUtility.FromJson<PaintFinishSaveData>(serialized);
                if (data?.entries == null)
                    return;

                foreach (var entry in data.entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.vehicleId) ||
                        string.Equals(entry.finishId, FactoryFinishId, StringComparison.Ordinal) ||
                        !TryGetPaintFinish(entry.finishId, out _))
                    {
                        continue;
                    }

                    savedFinishByVehicleId[entry.vehicleId] = entry.finishId;
                }
            }
            catch (Exception exception)
            {
                WarnPersistenceFailureOnce(
                    "finish-store:invalid-json",
                    $"Could not read '{PaintFinishModDataKey}' ({exception.GetType().Name}: {exception.Message}). " +
                    "Stored vehicle colors remain available and finishes will default to Factory.");
            }
        }

        private static bool TryGetPaintFinish(string finishId, out PaintFinishDefinition finish)
        {
            foreach (var candidate in PaintFinishes)
            {
                if (!string.Equals(candidate.Id, finishId, StringComparison.Ordinal))
                    continue;

                finish = candidate;
                return true;
            }

            finish = default;
            return false;
        }

        internal void RestorePersistenceState(string source, int pass)
        {
            if (pass <= 4)
                RepairInvalidVehicleRecords(source, pass);
            if (customVehicleColors.Count == 0)
                return;

            EnsurePrivateDriverPaintHooks();
            var scanLoadedSavedControllers = ShouldScanLoadedSavedControllers(pass);
            var restoredVehicleCount = RestoreSavedVehiclePaint(
                source,
                scanLoadedSavedControllers,
                out var restoredCustomColorCount,
                out var inspectedVehicleCount,
                out var additionalLoadedSavedControllerCount);
            if (restoredCustomColorCount > 0)
            {
                GameEvent.Invoke(CustomColorRestoredEvent);
                TracePersistence(
                    $"Broadcast '{CustomColorRestoredEvent}' after restoring custom color for " +
                    $"{restoredCustomColorCount} vehicle(s).");
            }

            TracePersistence(
                $"Load restore pass={pass}, source='{source}', inspected={inspectedVehicleCount}, " +
                $"restored={restoredVehicleCount}, scannedLoadedSavedControllers={scanLoadedSavedControllers}, " +
                $"additionalLoadedSavedControllers={additionalLoadedSavedControllerCount}, " +
                $"privateDriverHooksInstalled={privateDriverPaintHooksInstalled}.");
        }

        private void RepairInvalidVehicleRecords(string source, int pass)
        {
            var gameInstance = SaveGameManager.Current;
            if (gameInstance == null || gameInstance.VehicleInstances == null)
                return;

            var invalidPlayerVehicleRecords = gameInstance.VehicleInstances
                .Where(vehicleInstance => !IsValidSavedVehicleRecord(vehicleInstance))
                .ToArray();
            var invalidPrivateDriverVehicleRecords = gameInstance.privateDriverVehicleInstances == null
                ? Array.Empty<VehicleInstance>()
                : gameInstance.privateDriverVehicleInstances
                    .Where(vehicleInstance => !IsValidSavedVehicleRecord(vehicleInstance))
                    .ToArray();

            var removedPlayerVehicleRecords = invalidPlayerVehicleRecords.Length;
            var removedPrivateDriverVehicleRecords = invalidPrivateDriverVehicleRecords.Length;
            if (removedPlayerVehicleRecords == 0 && removedPrivateDriverVehicleRecords == 0)
            {
                if (pass == 4)
                {
                    context.Logger.Info(
                        "Vehicle save recovery scan completed without invalid vehicle records; " +
                        $"source='{source}', playerVehicleRecords={gameInstance.VehicleInstances.Count}, " +
                        $"privateDriverVehicleRecords={gameInstance.privateDriverVehicleInstances?.Count ?? 0}.");
                }

                return;
            }

            gameInstance.VehicleInstances.RemoveAll(invalidPlayerVehicleRecords.Contains);
            gameInstance.privateDriverVehicleInstances?.RemoveAll(invalidPrivateDriverVehicleRecords.Contains);

            var clearedActiveVehicleId = !string.IsNullOrEmpty(gameInstance.ActiveVehicleId);
            if (clearedActiveVehicleId)
                gameInstance.ActiveVehicleId = string.Empty;

            SaveGameManager.MarkChange();
            context.Logger.Warn(
                "Recovered invalid vehicle records from the loaded save; " +
                $"source='{source}', pass={pass}, removedPlayerVehicleRecords={removedPlayerVehicleRecords}, " +
                $"removedPrivateDriverVehicleRecords={removedPrivateDriverVehicleRecords}, " +
                $"clearedActiveVehicleId={clearedActiveVehicleId}. " +
                "Valid vehicles were preserved; save the game after confirming normal interactions.");
        }

        private static bool IsValidSavedVehicleRecord(VehicleInstance? vehicleInstance)
        {
            if (vehicleInstance == null || string.IsNullOrWhiteSpace(vehicleInstance.id) ||
                string.IsNullOrWhiteSpace(vehicleInstance.vehicleTypeName))
                return false;

            try
            {
                return VehicleTypeHelper.GetVehicleType(vehicleInstance.vehicleTypeName) != null;
            }
            catch
            {
                return false;
            }
        }

        private int RestoreSavedVehiclePaint(
            string source,
            bool includeLoadedSavedControllers,
            out int restoredCustomColorCount,
            out int inspectedVehicleCount,
            out int additionalLoadedSavedControllerCount)
        {
            var restoredVehicleCount = 0;
            restoredCustomColorCount = 0;
            var candidates = new Dictionary<int, VehicleController>();
            foreach (var vehicle in VehicleHelper.AllPlayerVehicles.ToArray())
            {
                if (vehicle != null)
                    candidates[vehicle.GetInstanceID()] = vehicle;
            }

            var regularPlayerVehicleCount = candidates.Count;
            if (includeLoadedSavedControllers)
                AddLoadedSavedVehicleControllers(candidates);

            inspectedVehicleCount = candidates.Count;
            additionalLoadedSavedControllerCount = candidates.Count - regularPlayerVehicleCount;
            foreach (var vehicle in candidates.Values)
            {
                if (RestoreSavedVehiclePaint(vehicle, source, out var restoredColor))
                    restoredVehicleCount++;
                if (restoredColor)
                    restoredCustomColorCount++;
            }

            return restoredVehicleCount;
        }

        private static bool ShouldScanLoadedSavedControllers(int pass)
        {
            return pass == 1 || pass == 2 || pass == 4 || pass == 8 ||
                   pass == 16 || pass == 32 || pass == 64 || pass == 120;
        }

        private static void AddLoadedSavedVehicleControllers(Dictionary<int, VehicleController> candidates)
        {
            var savedVehicles = SaveGameManager.Current?.VehicleInstances;
            if (savedVehicles == null || savedVehicles.Count == 0)
                return;

            var savedVehicleIds = new HashSet<string>(
                savedVehicles
                    .Where(vehicleInstance => vehicleInstance != null &&
                                              !string.IsNullOrWhiteSpace(vehicleInstance.id))
                    .Select(vehicleInstance => vehicleInstance.id),
                StringComparer.Ordinal);
            if (savedVehicleIds.Count == 0)
                return;

            foreach (var vehicle in Resources.FindObjectsOfTypeAll<VehicleController>())
            {
                if (vehicle == null || !vehicle.gameObject.scene.IsValid() || vehicle.vehicleInstance == null ||
                    !savedVehicleIds.Contains(vehicle.vehicleInstance.id))
                {
                    continue;
                }

                candidates[vehicle.GetInstanceID()] = vehicle;
            }
        }

        private bool RestoreSavedVehiclePaint(
            VehicleController? vehicle,
            string source,
            out bool restoredColor)
        {
            restoredColor = false;
            if (vehicle == null || vehicle.vehicleInstance == null)
                return false;
            if (activeRepaintAsset != null && activeRepaintAsset.IsPreviewing(vehicle))
                return false;

            restoredColor = RestoreSavedCustomVehicleColor(vehicle, source);
            var restoredFinish = RestoreSavedPaintFinish(
                vehicle.CarFeatures,
                vehicle.GetInstanceID(),
                vehicle.vehicleInstance,
                source);
            return restoredColor || restoredFinish;
        }

        private bool RestoreSavedCustomVehicleColor(VehicleController? vehicle, string source)
        {
            if (vehicle == null || vehicle.vehicleInstance == null)
                return false;

            var vehicleInstance = vehicle.vehicleInstance;
            var colorName = vehicleInstance.vehicleColorName;
            if (string.IsNullOrEmpty(colorName) || !customVehicleColors.TryGetValue(colorName, out var color))
            {
                restoredCustomVehicleColorsByControllerId.Remove(vehicle.GetInstanceID());
                return false;
            }

            if (vehicle.CarFeatures == null)
            {
                WarnPersistenceFailureOnce(
                    $"player:{vehicleInstance.id}:missing-car-features",
                    $"Could not restore custom color '{colorName}' for " +
                    $"vehicle id='{vehicleInstance.id}', type='{vehicleInstance.vehicleTypeName}', " +
                    $"source='{source}' because CarFeatures is unavailable.");
                return false;
            }

            var controllerId = vehicle.GetInstanceID();
            if (restoredCustomVehicleColorsByControllerId.TryGetValue(controllerId, out var restoredColorName) &&
                string.Equals(restoredColorName, colorName, StringComparison.Ordinal) &&
                ReferenceEquals(vehicle.CarFeatures.VehicleColor, color))
                return false;

            vehicle.CarFeatures.SetColor(color);
            restoredCustomVehicleColorsByControllerId[controllerId] = colorName;
            TracePersistence(
                $"Restored custom color '{colorName}' for player vehicle " +
                $"id='{vehicleInstance.id}', type='{vehicleInstance.vehicleTypeName}', source='{source}'.");
            return true;
        }

        private void HandleVehicleEntered(VehicleController vehicle)
        {
            TraceInteraction("Received global vehicle-enter event.", vehicle);
            TraceInteractionNextFrame("Vehicle state one frame after the global vehicle-enter event.", vehicle);
            appliedFinishByControllerId.Remove(vehicle.GetInstanceID());
            RestoreSavedVehiclePaint(vehicle, "vehicle-entered", out _);
        }

        private void EnsurePrivateDriverPaintHooks()
        {
            if (privateDriverPaintHooksInstalled)
                return;

            var added = 0;
            foreach (var button in Resources.FindObjectsOfTypeAll<SmartphonePrivateDriverUiButton>())
            {
                if (button == null || !button.gameObject.scene.IsValid() ||
                    button.GetComponent<PrivateDriverPaintButtonHook>() != null)
                {
                    continue;
                }

                button.gameObject.AddComponent<PrivateDriverPaintButtonHook>();
                added++;
            }

            if (added > 0)
            {
                privateDriverPaintHooksInstalled = true;
                TracePersistence($"Attached private-driver paint restore hook to {added} phone button(s).");
            }
        }

        private void ReleasePrivateDriverPaintHooks()
        {
            foreach (var hook in Resources.FindObjectsOfTypeAll<PrivateDriverPaintButtonHook>())
            {
                if (hook != null && hook.gameObject.scene.IsValid())
                    UnityEngine.Object.Destroy(hook);
            }

            privateDriverPaintHooksInstalled = false;
        }

        internal static void HandlePrivateDriverButtonClicked()
        {
            activeRuntime?.RestorePrivateDriverPaint("private-driver-button");
        }

        private void RestorePrivateDriverPaint(string source)
        {
            var privateDriverVehicle = SmartphonePrivateDriverUI.CurrentVehicle;
            if (privateDriverVehicle == null || privateDriverVehicle.vehicleInstance == null)
            {
                TracePersistence($"No summoned private-driver vehicle was available after source='{source}'.");
                return;
            }

            var vehicleInstance = privateDriverVehicle.vehicleInstance;
            var colorName = vehicleInstance.vehicleColorName;
            var carFeatures = privateDriverVehicle.GetComponent<CarFeatures>();
            if (carFeatures == null)
            {
                WarnPersistenceFailureOnce(
                    $"private-driver:{vehicleInstance.id}:missing-car-features",
                    $"Could not restore paint for private-driver " +
                    $"vehicle id='{vehicleInstance.id}', type='{vehicleInstance.vehicleTypeName}' because CarFeatures is unavailable.");
                return;
            }

            if (!string.IsNullOrEmpty(colorName) && customVehicleColors.TryGetValue(colorName, out var color))
            {
                carFeatures.SetColor(color);
                TracePersistence(
                    $"Restored custom color '{colorName}' for private-driver vehicle " +
                    $"id='{vehicleInstance.id}', type='{vehicleInstance.vehicleTypeName}', source='{source}'.");
            }

            RestoreSavedPaintFinish(
                carFeatures,
                privateDriverVehicle.GetInstanceID(),
                vehicleInstance,
                source);
        }

        private void WarnPersistenceFailureOnce(string reason, string message)
        {
            if (reportedPersistenceFailures.Add(reason))
                context.Logger.Warn($"Vehicle Repainter color persistence failed [{reason}]: {message}");
        }

        private void ReleaseCustomVehicleColors()
        {
            GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
            GlobalEvents.onExitVehicle -= HandleVehicleExited;
            foreach (var color in ownedCustomVehicleColors)
            {
                if (color != null)
                    UnityEngine.Object.Destroy(color);
            }

            customVehicleColors.Clear();
            ownedCustomVehicleColors.Clear();
            restoredCustomVehicleColorsByControllerId.Clear();
            finishStatesByControllerId.Clear();
            appliedFinishByControllerId.Clear();
            savedFinishByVehicleId.Clear();
            observedFinishSaveGame = null;
            reportedPersistenceFailures.Clear();
        }

        private void HandleVehicleExited(VehicleController vehicle)
        {
            TraceInteraction("Received global vehicle-exit event.", vehicle);
            TraceInteractionNextFrame("Vehicle state one frame after the global vehicle-exit event.", vehicle);
        }

        private void TraceInteraction(string message, VehicleController? eventVehicle = null)
        {
            if (!DebugOptions.EnableDebugLogging || !DebugOptions.EnableInteractionDiagnostics)
                return;

            context.Logger.Info(
                $"Vehicle Repainter interaction diagnostic: {message} " +
                $"eventVehicle='{DescribeVehicle(eventVehicle)}'; {DescribeInteractionState()}");
        }

        private void TraceInteractionNextFrame(string message, VehicleController? eventVehicle)
        {
            if (!DebugOptions.EnableDebugLogging || !DebugOptions.EnableInteractionDiagnostics || overlayUi == null)
                return;

            overlayUi.StartCoroutine(TraceInteractionAfterFrame(message, eventVehicle));
        }

        private IEnumerator TraceInteractionAfterFrame(string message, VehicleController? eventVehicle)
        {
            yield return null;
            TraceInteraction(message, eventVehicle);
        }

        private string DescribeInteractionState()
        {
            var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
            var activeOverlay = overlayUi?.gasStation;
            var currentTrigger = activeOverlay == null ? null : GetCurrentStationTrigger(activeOverlay);
            return $"selectedVehicle='{DescribeVehicle(selectedVehicle)}', " +
                   $"usingVehicle={PlayerHelper.IsUsingVehicle}, " +
                   $"insideMotorVehicle={VehicleHelper.IsInsideMotorVehicle()}, " +
                   $"purchasePanelOpen={PurchaseVehicleUI.IsPanelOpen}, " +
                   $"activeRepaintSession={activeRepaintAsset != null}, " +
                   $"activeOverlay='{activeOverlay?.GetType().FullName ?? "null"}', " +
                   $"repaintOverlayActive={ReferenceEquals(activeOverlay, extendedGasStationOverlay)}, " +
                   $"overlayTrigger='{DescribeTrigger(currentTrigger)}'";
        }

        private sealed class ExtendedGasStationOverlay : GasStationOverlay, IOverlay
        {
            private readonly VehicleRepainterRuntime runtime;

            internal ExtendedGasStationOverlay(VehicleRepainterRuntime runtime)
            {
                this.runtime = runtime;
            }

            ButtonInfo[]? IOverlay.GetButtons()
            {
                var stationTrigger = runtime.GetCurrentStationTrigger(this);
                ButtonInfo[]? vanillaButtons;
                try
                {
                    vanillaButtons = base.GetButtons();
                }
                catch (Exception exception)
                {
                    runtime.WarnButtonFailureOnce(
                        "vanilla-get-buttons-exception",
                        $"The vanilla gas-station overlay threw {exception.GetType().Name} while producing its " +
                        $"buttons; trigger='{DescribeTrigger(stationTrigger)}', message='{exception.Message}'.");
                    throw;
                }

                var vehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;

                if (stationTrigger == null)
                {
                    runtime.WarnButtonFailureOnce(
                        "missing-current-trigger",
                        $"The extended overlay has no current station trigger; vanillaButtonCount={vanillaButtons?.Length ?? 0}.");
                    return vanillaButtons;
                }

                if (!stationTrigger.isRepairStation)
                {
                    runtime.TraceButton(
                        $"Skipped button injection for non-repair trigger='{DescribeTrigger(stationTrigger)}'.");
                    return vanillaButtons;
                }

                if (vehicle == null)
                {
                    runtime.WarnButtonFailureOnce(
                        "missing-selected-vehicle",
                        $"Repair trigger='{DescribeTrigger(stationTrigger)}' is active, but GameManager.selectedVehicle is null; " +
                        $"insideMotorVehicle={VehicleHelper.IsInsideMotorVehicle()}, vanillaButtonCount={vanillaButtons?.Length ?? 0}.");
                    return vanillaButtons;
                }

                if (vehicle.vehicleInstance == null || vehicle.CarFeatures == null || vehicle.vehicleType == null ||
                    !vehicle.vehicleType.IsMotorVehicle)
                {
                    runtime.WarnButtonFailureOnce(
                        "unsupported-vehicle-state",
                        $"Repair trigger='{DescribeTrigger(stationTrigger)}' is active, but the selected vehicle " +
                        $"does not satisfy repaint requirements; vehicle='{DescribeVehicle(vehicle)}', " +
                        $"insideMotorVehicle={VehicleHelper.IsInsideMotorVehicle()}, vanillaButtonCount={vanillaButtons?.Length ?? 0}.");
                    return vanillaButtons;
                }

                if (vanillaButtons == null)
                {
                    runtime.WarnButtonFailureOnce(
                        "vanilla-service-buttons-null",
                        $"The vanilla repair overlay returned no Repair/Wash buttons, so Repaint was not appended; " +
                        $"trigger='{DescribeTrigger(stationTrigger)}', vehicle='{DescribeVehicle(vehicle)}', " +
                        $"insideMotorVehicle={VehicleHelper.IsInsideMotorVehicle()}. This commonly indicates a " +
                        "car-versus-truck garage mismatch or that the player is no longer considered inside the vehicle.");
                    return vanillaButtons;
                }

                var result = new ButtonInfo[vanillaButtons.Length + 1];
                Array.Copy(vanillaButtons, result, vanillaButtons.Length);
                result[result.Length - 1] = new ButtonInfo(
                    "RepaintVehicle",
                    "vehicle-repainter:repaint",
                    new { price = BaseRepaintPrice.ToShortCurrencyFormat() },
                    "blue",
                    () => runtime.OpenRepaintUi(stationTrigger),
                    PlayerAction.SpecialInteract,
                    Mathf.Approximately(vehicle.CurrentSpeed, 0f));
                runtime.TraceButton(
                    $"Appended Repaint button; trigger='{DescribeTrigger(stationTrigger)}', " +
                    $"vehicle='{DescribeVehicle(vehicle)}', vanillaButtonCount={vanillaButtons.Length}, " +
                    $"speed={vehicle.CurrentSpeed:0.###}.");
                return result;
            }
        }

        private sealed class RepaintPurchasableAsset : IPurchasableAsset
        {
            private readonly VehicleRepainterRuntime runtime;
            private readonly ModContext context;
            private readonly VehicleController vehicle;
            private readonly GasStationTrigger stationTrigger;
            private readonly Action<RepaintPurchasableAsset> onClosed;
            private readonly VehiclePaintSnapshot originalPaint;
            private readonly string originalSavedColorName;
            private string committedColorName;
            private string selectedColorName;
            private string selectedFinishId;
            private readonly bool finishAvailable;
            private bool closed;
            private bool movementLocked;
            private bool purchaseCompleted;
            private GridLayoutGroup? colorGridLayout;
            private ColorGridLayoutSnapshot? originalColorGridLayout;
            private Image? colorGridBackground;
            private bool colorGridBackgroundWasEnabled;
            private GameObject? finishSelectorRoot;
            private readonly Dictionary<string, FinishButtonVisual> finishButtonVisuals =
                new Dictionary<string, FinishButtonVisual>(StringComparer.Ordinal);

            internal RepaintPurchasableAsset(
                VehicleRepainterRuntime runtime,
                ModContext context,
                VehicleController vehicle,
                GasStationTrigger stationTrigger,
                Action<RepaintPurchasableAsset> onClosed)
            {
                this.runtime = runtime;
                this.context = context;
                this.vehicle = vehicle;
                this.stationTrigger = stationTrigger;
                this.onClosed = onClosed;
                originalSavedColorName = vehicle.vehicleInstance.vehicleColorName;
                committedColorName = ResolveInitialColorName(vehicle);
                selectedColorName = committedColorName;
                selectedFinishId = runtime.GetSavedFinishId(vehicle.vehicleInstance.id);
                finishAvailable = runtime.SupportsPaintFinish(vehicle.CarFeatures, vehicle.GetInstanceID());

                VehicleColor originalColor;
                if (!runtime.TryResolveVehicleColor(committedColorName, out originalColor))
                    originalColor = vehicle.CarFeatures.VehicleColor;

                originalPaint = new VehiclePaintSnapshot(vehicle.CarFeatures, originalColor);
            }

            internal bool IsPreviewing(VehicleController candidate)
            {
                return !closed && ReferenceEquals(vehicle, candidate);
            }

            internal void BeginSession()
            {
                if (closed || movementLocked)
                    return;

                stationTrigger.onExited += HandleStationExited;
                GlobalEvents.onExitVehicle += HandleVehicleExited;
                vehicle.SetFreeze(true);
                movementLocked = true;
                runtime.TraceInteraction("Started repaint session and froze the serviced vehicle.", vehicle);
            }

            internal void ApplyColorGridLayout(PurchaseVehicleUI purchaseUi)
            {
                if (ColorsGridLayoutGroupField!.GetValue(purchaseUi) is not GridLayoutGroup gridLayout)
                {
                    context.Logger.Warn("Could not resize the repaint color grid because the vanilla layout is unavailable.");
                    return;
                }

                var gridRect = gridLayout.transform as RectTransform;
                var panelRect = gridRect?.parent as RectTransform;
                var colorsSectionRect = panelRect?.parent as RectTransform;
                var sectionsContainer = colorsSectionRect?.parent;
                var colorsLabelRect = colorsSectionRect?.Find("Label") as RectTransform;
                var specsSection = sectionsContainer?.Find("Specs")?.gameObject;
                if (gridRect == null || panelRect == null || colorsSectionRect == null ||
                    colorsLabelRect == null || specsSection == null)
                {
                    context.Logger.Warn("Could not expand the repaint color section because the vanilla UI hierarchy is unavailable.");
                    return;
                }

                colorGridLayout = gridLayout;
                originalColorGridLayout = new ColorGridLayoutSnapshot(
                    gridLayout,
                    gridRect,
                    panelRect,
                    colorsSectionRect,
                    colorsLabelRect,
                    specsSection);
                colorGridBackground = gridLayout.GetComponent<Image>();
                if (colorGridBackground != null)
                {
                    colorGridBackgroundWasEnabled = colorGridBackground.enabled;
                    colorGridBackground.enabled = false;
                }

                gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                specsSection.SetActive(false);
                colorsSectionRect.anchoredPosition = new Vector2(0f, colorsSectionRect.anchoredPosition.y);
                colorsSectionRect.sizeDelta = new Vector2(1050f, colorsSectionRect.sizeDelta.y);
                panelRect.sizeDelta = new Vector2(990f, 290f);
                gridRect.sizeDelta = new Vector2(990f, 290f);
                // Keep the heading above the expanded swatch grid instead of letting
                // the first row cover its lower half.
                colorsLabelRect.anchoredPosition = new Vector2(
                    30f,
                    colorsLabelRect.anchoredPosition.y + 20f);
                colorsLabelRect.sizeDelta = new Vector2(990f, colorsLabelRect.sizeDelta.y);

                gridLayout.constraintCount = 16;
                gridLayout.cellSize = new Vector2(52f, 45f);
                gridLayout.spacing = new Vector2(7f, 7f);
                gridLayout.padding = new RectOffset(25, 25, 12, 12);
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)gridLayout.transform);
                CreateFinishSelector(colorsSectionRect);
            }

            private void CreateFinishSelector(RectTransform colorsSectionRect)
            {
                if (finishSelectorRoot != null)
                    UnityEngine.Object.Destroy(finishSelectorRoot);

                finishButtonVisuals.Clear();
                finishSelectorRoot = new GameObject("VehicleRepainterFinishSelector", typeof(RectTransform));
                finishSelectorRoot.transform.SetParent(colorsSectionRect, false);
                var rootRect = finishSelectorRoot.GetComponent<RectTransform>();
                rootRect.anchorMin = new Vector2(0f, 1f);
                rootRect.anchorMax = new Vector2(0f, 1f);
                rootRect.pivot = new Vector2(0f, 1f);
                rootRect.anchoredPosition = new Vector2(30f, -345f);
                rootRect.sizeDelta = new Vector2(990f, 82f);
                finishSelectorRoot.transform.SetAsLastSibling();

                CreateText(
                    finishSelectorRoot.transform,
                    "vehicle-repainter:finish".Localize().ToString(),
                    20,
                    TextAnchor.MiddleLeft,
                    new Color(0.12f, 0.15f, 0.2f),
                    new Vector2(0f, 52f),
                    new Vector2(0f, -4f));

                if (!finishAvailable)
                {
                    CreateText(
                        finishSelectorRoot.transform,
                        "vehicle-repainter:finish_unavailable".Localize().ToString(),
                        17,
                        TextAnchor.MiddleLeft,
                        new Color(0.4f, 0.42f, 0.46f),
                        new Vector2(0f, 4f),
                        new Vector2(0f, -32f));
                    return;
                }

                var finishes = runtime.GetPaintFinishes();
                for (var index = 0; index < finishes.Count; index++)
                {
                    var finish = finishes[index];
                    var buttonObject = CreateUiObject(
                        $"Finish_{finish.Id}",
                        finishSelectorRoot.transform,
                        new Color(0.83f, 0.85f, 0.88f));
                    var buttonRect = buttonObject.GetComponent<RectTransform>();
                    var minX = index / (float)finishes.Count;
                    var maxX = (index + 1f) / finishes.Count;
                    buttonRect.anchorMin = new Vector2(minX, 0f);
                    buttonRect.anchorMax = new Vector2(maxX, 0f);
                    buttonRect.pivot = new Vector2(0.5f, 0f);
                    buttonRect.offsetMin = new Vector2(index == 0 ? 0f : 6f, 4f);
                    buttonRect.offsetMax = new Vector2(index == finishes.Count - 1 ? 0f : -6f, 44f);

                    var image = buttonObject.GetComponent<Image>();
                    var button = buttonObject.AddComponent<Button>();
                    button.targetGraphic = image;
                    var capturedFinishId = finish.Id;
                    button.onClick.AddListener(() => SelectFinish(capturedFinishId));
                    var label = CreateText(
                        buttonObject.transform,
                        finish.LabelKey.Localize().ToString(),
                        16,
                        TextAnchor.MiddleCenter,
                        new Color(0.12f, 0.15f, 0.2f),
                        Vector2.zero,
                        Vector2.zero);
                    label.resizeTextForBestFit = true;
                    label.resizeTextMinSize = 10;
                    label.raycastTarget = false;
                    finishButtonVisuals[finish.Id] = new FinishButtonVisual(image, label);
                }

                SelectFinish(selectedFinishId);
            }

            private void SelectFinish(string finishId)
            {
                selectedFinishId = finishId;
                foreach (var pair in finishButtonVisuals)
                {
                    var selected = string.Equals(pair.Key, selectedFinishId, StringComparison.Ordinal);
                    pair.Value.Background.color = selected
                        ? new Color(0.17f, 0.48f, 0.82f)
                        : new Color(0.83f, 0.85f, 0.88f);
                    pair.Value.Label.color = selected
                        ? Color.white
                        : new Color(0.12f, 0.15f, 0.2f);
                }

                runtime.ApplyPaintFinish(
                    vehicle.CarFeatures,
                    vehicle.GetInstanceID(),
                    selectedFinishId,
                    "finish-preview",
                    force: true);
                RefreshPrice();
                GameEvent.Invoke("vehicle-repainter:finish-preview");
            }

            internal void RefreshPrice(PurchaseVehicleUI? purchaseUi = null)
            {
                purchaseUi ??= InstanceBehavior<UIs>.Instance?.playerHUD?.purchaseVehicleUI;
                if (closed || purchaseUi == null || !PurchaseVehicleUI.IsPanelOpen ||
                    !ReferenceEquals(runtime.activeRepaintAsset, this))
                    return;

                SetAssetPriceMethod!.Invoke(purchaseUi, new object[] { false });
                var price = GetPurchasePrice();
                if (PurchaseButtonField!.GetValue(purchaseUi) is Button purchaseButton)
                    purchaseButton.interactable = SaveGameManager.Current != null &&
                                                  SaveGameManager.Current.Money >= price;

                runtime.TraceFinish($"Selected finish '{selectedFinishId}' for vehicle id='{vehicle.vehicleInstance?.id}'; repaint price={price:0}.");
            }

            private static GameObject CreateUiObject(string name, Transform parent, Color color)
            {
                var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
                gameObject.transform.SetParent(parent, false);
                gameObject.GetComponent<Image>().color = color;
                return gameObject;
            }

            private static Text CreateText(
                Transform parent,
                string value,
                int fontSize,
                TextAnchor alignment,
                Color color,
                Vector2 offsetMin,
                Vector2 offsetMax)
            {
                var gameObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
                gameObject.transform.SetParent(parent, false);
                var rect = gameObject.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = offsetMin;
                rect.offsetMax = offsetMax;
                var text = gameObject.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                text.text = value;
                text.fontSize = fontSize;
                text.alignment = alignment;
                text.color = color;
                text.raycastTarget = false;
                return text;
            }

            public string GetLocalizeKey() => "vehicle-repainter:title";

            public float GetPurchasePrice() => finishAvailable
                ? runtime.GetRepaintPrice(selectedFinishId)
                : BaseRepaintPrice;

            public string GetInitialColor() => committedColorName;

            public List<(string key, string value)> GetSpecs() => new List<(string, string)>();

            public List<(string, Color32)> GetColors()
            {
                return runtime.GetRepaintColors()
                    .Select((color, index) => new SortableVehicleColor(color, index))
                    .OrderBy(color => color.Group)
                    .ThenBy(color => color.Hue)
                    .ThenBy(color => color.Value)
                    .ThenByDescending(color => color.Saturation)
                    .ThenBy(color => color.OriginalIndex)
                    .Select(color => (color.Name, color.Tint))
                    .ToList();
            }

            public void SetColor(string colorName, bool updateVisuals = true)
            {
                if (!runtime.TryResolveVehicleColor(colorName, out var vehicleColor))
                {
                    context.Logger.Warn($"Could not preview unresolved vehicle color '{colorName}'.");
                    return;
                }

                selectedColorName = colorName;
                if (updateVisuals && vehicle.CarFeatures != null)
                {
                    runtime.InvalidateAppliedFinish(vehicle.GetInstanceID());
                    vehicle.CarFeatures.SetColor(vehicleColor);
                    // Other vehicle mods may refresh their own paint blocks on this event.
                    // Reapply the selected finish afterwards so their color refresh cannot
                    // leave the preview at the factory finish.
                    GameEvent.Invoke("vehicle-repainter:color-preview");
                    runtime.ApplyPaintFinish(
                        vehicle.CarFeatures,
                        vehicle.GetInstanceID(),
                        selectedFinishId,
                        "color-preview",
                        force: true);
                }
            }

            public void ResetColor()
            {
                if (closed)
                    return;

                runtime.TraceInteraction("Closing repaint session.", vehicle);
                closed = true;
                stationTrigger.onExited -= HandleStationExited;
                GlobalEvents.onExitVehicle -= HandleVehicleExited;

                if (!purchaseCompleted && vehicle != null && vehicle.CarFeatures != null)
                {
                    originalPaint.Restore(vehicle.CarFeatures);
                    runtime.InvalidateAppliedFinish(vehicle.GetInstanceID());
                    if (vehicle.vehicleInstance != null)
                        vehicle.vehicleInstance.vehicleColorName = originalSavedColorName;
                    GameEvent.Invoke("vehicle-repainter:color-reset");
                }

                if (movementLocked && vehicle != null)
                    vehicle.SetFreeze(false);

                RestoreColorGridLayout();

                RestoreGasStationOverlayIfStillRelevant();
                onClosed(this);
                runtime.TraceInteraction("Closed repaint session and restored the vehicle/UI state.", vehicle);
            }

            public bool Purchase()
            {
                var activeVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
                if (!ReferenceEquals(activeVehicle, vehicle) || vehicle.vehicleInstance == null ||
                    vehicle.CarFeatures == null || vehicle.vehicleCollider == null ||
                    !stationTrigger.IntersectsBounds(vehicle.vehicleCollider.bounds) ||
                    !Mathf.Approximately(vehicle.CurrentSpeed, 0f))
                {
                    context.Logger.Warn(
                        "Repaint confirmation was rejected because the serviced vehicle is no longer active, stopped, and inside the service bay.");
                    return false;
                }

                var transaction = new TransactionInfo("vehicle-repainter:transaction");
                if (vehicle.vehicleType.taxDeductible)
                    transaction.SetTaxDeductibleName("ba:businesstype_gasstation");

                var price = GetPurchasePrice();
                if (!GameManager.ChangeMoneySafe(
                        -price,
                        transaction,
                        null,
                        stationTrigger.cbc?.buildingRegistration?.Address,
                        force: false,
                        showNotification: true))
                {
                    context.Logger.Warn(
                        $"Repaint confirmation was rejected for vehicle id '{vehicle.vehicleInstance.id}' " +
                        $"at price={price:0} due to insufficient money.");
                    return false;
                }

                runtime.TraceFinish($"Purchased finish '{selectedFinishId}' for vehicle id='{vehicle.vehicleInstance.id}' at price={price:0}.");

                vehicle.vehicleInstance.vehicleColorName = selectedColorName;
                SetColor(selectedColorName);
                runtime.SaveFinish(vehicle.vehicleInstance.id, selectedFinishId);
                committedColorName = selectedColorName;
                purchaseCompleted = true;
                SaveGameManager.MarkChange();
                GameEvent.Invoke(string.Empty);
                InstanceBehavior<SfxManager>.Instance.PlayAudio(
                    SoundType.PurchaseSuccess,
                    vehicle.transform.position,
                    1f,
                    isPlayerCreatedSound: true);

                return true;
            }

            public void Order(Address deliveryAddress, Contact storeContact, bool showNotification)
            {
                // Delivery is hidden by the vanilla UI because repainting is opened outside a vehicle store.
            }

            public IEnumerator ShowcaseAnimation()
            {
                yield break;
            }

            public IEnumerator CancelShowcaseAnimation()
            {
                yield break;
            }

            private void HandleStationExited(GasStationTrigger exitedStation)
            {
                if (closed || !ReferenceEquals(exitedStation, stationTrigger))
                    return;

                runtime.TraceInteraction("Serviced vehicle exited the repaint station; cancelling session.", vehicle);
                CancelSession();
            }

            private void HandleVehicleExited(VehicleController exitedVehicle)
            {
                if (closed || !ReferenceEquals(exitedVehicle, vehicle))
                    return;

                runtime.TraceInteraction("Serviced vehicle emitted its exit event; cancelling repaint session.", vehicle);
                CancelSession();
            }

            private void CancelSession()
            {
                var purchaseUi = InstanceBehavior<UIs>.Instance?.playerHUD?.purchaseVehicleUI;
                if (purchaseUi != null && PurchaseVehicleUI.IsPanelOpen)
                    purchaseUi.Close();
                else
                    ResetColor();
            }

            private void RestoreGasStationOverlayIfStillRelevant()
            {
                var activeVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
                if (!ReferenceEquals(activeVehicle, vehicle) || !PlayerHelper.IsUsingVehicle ||
                    vehicle.vehicleCollider == null || !stationTrigger.IntersectsBounds(vehicle.vehicleCollider.bounds))
                {
                    return;
                }

                GasStationOverlay.Show(stationTrigger);
            }

            private void RestoreColorGridLayout()
            {
                if (colorGridLayout == null || originalColorGridLayout == null)
                    return;

                if (finishSelectorRoot != null)
                {
                    UnityEngine.Object.Destroy(finishSelectorRoot);
                    finishSelectorRoot = null;
                    finishButtonVisuals.Clear();
                }

                originalColorGridLayout.Restore(colorGridLayout);
                if (colorGridBackground != null)
                    colorGridBackground.enabled = colorGridBackgroundWasEnabled;

                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)colorGridLayout.transform);
                colorGridLayout = null;
                originalColorGridLayout = null;
                colorGridBackground = null;
            }

            private readonly struct SortableVehicleColor
            {
                internal readonly string Name;
                internal readonly Color32 Tint;
                internal readonly int Group;
                internal readonly float Hue;
                internal readonly float Saturation;
                internal readonly float Value;
                internal readonly int OriginalIndex;

                internal SortableVehicleColor(VehicleColor vehicleColor, int originalIndex)
                {
                    Name = ((UnityEngine.Object)vehicleColor).name;
                    Tint = vehicleColor.tint;
                    OriginalIndex = originalIndex;
                    Color.RGBToHSV(Tint, out var hue, out var saturation, out var value);
                    Group = saturation < 0.14f ? 0 : 1;
                    Hue = Group == 0
                        ? 0f
                        : hue >= 0.95f && saturation >= 0.5f
                            ? hue - 1f
                            : hue;
                    Saturation = saturation;
                    Value = value;
                }
            }

            private readonly struct FinishButtonVisual
            {
                internal readonly Image Background;
                internal readonly Text Label;

                internal FinishButtonVisual(Image background, Text label)
                {
                    Background = background;
                    Label = label;
                }
            }

            private sealed class ColorGridLayoutSnapshot
            {
                private readonly GridLayoutGroup.Constraint constraint;
                private readonly int constraintCount;
                private readonly Vector2 cellSize;
                private readonly Vector2 spacing;
                private readonly RectOffset padding;
                private readonly RectTransformSnapshot gridRect;
                private readonly RectTransformSnapshot panelRect;
                private readonly RectTransformSnapshot colorsSectionRect;
                private readonly RectTransformSnapshot colorsLabelRect;
                private readonly GameObject specsSection;
                private readonly bool specsSectionWasActive;

                internal ColorGridLayoutSnapshot(
                    GridLayoutGroup gridLayout,
                    RectTransform gridRect,
                    RectTransform panelRect,
                    RectTransform colorsSectionRect,
                    RectTransform colorsLabelRect,
                    GameObject specsSection)
                {
                    constraint = gridLayout.constraint;
                    constraintCount = gridLayout.constraintCount;
                    cellSize = gridLayout.cellSize;
                    spacing = gridLayout.spacing;
                    padding = new RectOffset(
                        gridLayout.padding.left,
                        gridLayout.padding.right,
                        gridLayout.padding.top,
                        gridLayout.padding.bottom);
                    this.gridRect = new RectTransformSnapshot(gridRect);
                    this.panelRect = new RectTransformSnapshot(panelRect);
                    this.colorsSectionRect = new RectTransformSnapshot(colorsSectionRect);
                    this.colorsLabelRect = new RectTransformSnapshot(colorsLabelRect);
                    this.specsSection = specsSection;
                    specsSectionWasActive = specsSection.activeSelf;
                }

                internal void Restore(GridLayoutGroup gridLayout)
                {
                    gridLayout.constraint = constraint;
                    gridLayout.constraintCount = constraintCount;
                    gridLayout.cellSize = cellSize;
                    gridLayout.spacing = spacing;
                    gridLayout.padding = padding;
                    gridRect.Restore();
                    panelRect.Restore();
                    colorsSectionRect.Restore();
                    colorsLabelRect.Restore();
                    specsSection.SetActive(specsSectionWasActive);
                }
            }

            private sealed class RectTransformSnapshot
            {
                private readonly RectTransform target;
                private readonly Vector2 anchoredPosition;
                private readonly Vector2 sizeDelta;

                internal RectTransformSnapshot(RectTransform target)
                {
                    this.target = target;
                    anchoredPosition = target.anchoredPosition;
                    sizeDelta = target.sizeDelta;
                }

                internal void Restore()
                {
                    if (target == null)
                        return;

                    target.anchoredPosition = anchoredPosition;
                    target.sizeDelta = sizeDelta;
                }
            }

            private string ResolveInitialColorName(VehicleController vehicle)
            {
                if (!string.IsNullOrEmpty(vehicle.vehicleInstance.vehicleColorName) &&
                    runtime.TryResolveVehicleColor(vehicle.vehicleInstance.vehicleColorName, out _))
                {
                    return vehicle.vehicleInstance.vehicleColorName;
                }

                var liveColor = vehicle.CarFeatures?.VehicleColor;
                if (liveColor != null)
                    return ((UnityEngine.Object)liveColor).name;

                var colors = runtime.GetRepaintColors();
                return colors.Count > 0 ? ((UnityEngine.Object)colors[0]).name : string.Empty;
            }

            private sealed class VehiclePaintSnapshot
            {
                private readonly VehicleColor? vehicleColor;
                private readonly List<RendererPaintSnapshot> rendererSnapshots = new List<RendererPaintSnapshot>();

                internal VehiclePaintSnapshot(CarFeatures carFeatures, VehicleColor? vehicleColor)
                {
                    this.vehicleColor = vehicleColor;
                    foreach (var renderer in carFeatures.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null)
                            continue;

                        var propertyBlock = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(propertyBlock);
                        var materialSlotSnapshots = new List<MaterialSlotPaintSnapshot>();
                        var materials = renderer.sharedMaterials;
                        for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                        {
                            var slotPropertyBlock = new MaterialPropertyBlock();
                            renderer.GetPropertyBlock(slotPropertyBlock, materialIndex);
                            materialSlotSnapshots.Add(new MaterialSlotPaintSnapshot(
                                materialIndex,
                                materials[materialIndex],
                                slotPropertyBlock,
                                slotPropertyBlock.isEmpty));
                        }

                        rendererSnapshots.Add(new RendererPaintSnapshot(
                            renderer,
                            propertyBlock,
                            materialSlotSnapshots));
                    }
                }

                internal void Restore(CarFeatures carFeatures)
                {
                    if (vehicleColor != null)
                        carFeatures.SetColor(vehicleColor);

                    foreach (var snapshot in rendererSnapshots)
                    {
                        snapshot.Restore();
                    }

                    VehicleColorBackingField!.SetValue(carFeatures, vehicleColor);
                }
            }

            private readonly struct RendererPaintSnapshot
            {
                internal readonly Renderer Renderer;
                internal readonly MaterialPropertyBlock PropertyBlock;
                private readonly IReadOnlyList<MaterialSlotPaintSnapshot> materialSlotSnapshots;

                internal RendererPaintSnapshot(
                    Renderer renderer,
                    MaterialPropertyBlock propertyBlock,
                    IReadOnlyList<MaterialSlotPaintSnapshot> materialSlotSnapshots)
                {
                    Renderer = renderer;
                    PropertyBlock = propertyBlock;
                    this.materialSlotSnapshots = materialSlotSnapshots;
                }

                internal void Restore()
                {
                    if (Renderer == null)
                        return;

                    Renderer.SetPropertyBlock(PropertyBlock);
                    var materials = Renderer.sharedMaterials;
                    foreach (var snapshot in materialSlotSnapshots)
                    {
                        if (snapshot.MaterialIndex < 0 || snapshot.MaterialIndex >= materials.Length ||
                            !ReferenceEquals(materials[snapshot.MaterialIndex], snapshot.Material))
                        {
                            continue;
                        }

                        Renderer.SetPropertyBlock(
                            snapshot.WasEmpty ? null : snapshot.PropertyBlock,
                            snapshot.MaterialIndex);
                    }
                }
            }

            private readonly struct MaterialSlotPaintSnapshot
            {
                internal readonly int MaterialIndex;
                internal readonly Material? Material;
                internal readonly MaterialPropertyBlock PropertyBlock;
                internal readonly bool WasEmpty;

                internal MaterialSlotPaintSnapshot(
                    int materialIndex,
                    Material? material,
                    MaterialPropertyBlock propertyBlock,
                    bool wasEmpty)
                {
                    MaterialIndex = materialIndex;
                    Material = material;
                    PropertyBlock = propertyBlock;
                    WasEmpty = wasEmpty;
                }
            }
        }

        internal readonly struct PaintFinishDefinition
        {
            internal readonly string Id;
            internal readonly string LabelKey;
            internal readonly float Price;
            internal readonly float SmoothnessBoost;
            internal readonly float NormalizedSmoothness;
            internal readonly float CoatMask;
            internal readonly bool IsFactory;

            internal PaintFinishDefinition(
                string id,
                string labelKey,
                float price,
                float smoothnessBoost,
                float normalizedSmoothness,
                float coatMask,
                bool isFactory = false)
            {
                Id = id;
                LabelKey = labelKey;
                Price = price;
                SmoothnessBoost = smoothnessBoost;
                NormalizedSmoothness = normalizedSmoothness;
                CoatMask = coatMask;
                IsFactory = isFactory;
            }
        }

        private sealed class VehicleFinishState
        {
            private static readonly string[] SmoothnessBoostPropertyNames =
            {
                "Vector1_41991b689ccf4932ad504975ac34399a",
                "_SmoothnessBoost",
                "SmoothnessBoost"
            };

            private static readonly string[] NormalizedSmoothnessPropertyNames =
            {
                "_Smoothness",
                "_Glossiness",
                "_GlossMapScale"
            };

            private static readonly string[] RoughnessPropertyNames =
            {
                "roughnessFactor",
                "_Roughness"
            };

            private static readonly int CoatMaskPropertyId = Shader.PropertyToID("_CoatMask");
            private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
            private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
            private static readonly int BaseColorFactorPropertyId = Shader.PropertyToID("baseColorFactor");

            private readonly CarFeatures carFeatures;
            internal readonly List<RendererFinishBinding> Bindings;

            private VehicleFinishState(CarFeatures carFeatures, List<RendererFinishBinding> bindings)
            {
                this.carFeatures = carFeatures;
                Bindings = bindings;
            }

            internal static VehicleFinishState Capture(CarFeatures carFeatures)
            {
                var bindings = new List<RendererFinishBinding>();
                var declaredBodyRenderers = carFeatures.bodyMeshes != null
                    ? new HashSet<Renderer>(carFeatures.bodyMeshes.Where(renderer => renderer != null))
                    : new HashSet<Renderer>();
                var vehicleTint = carFeatures.VehicleColor != null
                    ? (Color)carFeatures.VehicleColor.tint
                    : Color.clear;

                foreach (var renderer in carFeatures.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null)
                        continue;

                    var materials = renderer.sharedMaterials;
                    var rendererPropertyBlock = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(rendererPropertyBlock);
                    for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    {
                        var material = materials[materialIndex];
                        if (material == null)
                            continue;

                        var slotPropertyBlock = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(slotPropertyBlock, materialIndex);
                        var hasSlotOverrides = !slotPropertyBlock.isEmpty;
                        var hasSlotPaint = HasMatchingVehicleColorOverride(
                            material,
                            slotPropertyBlock,
                            vehicleTint);
                        var hasRendererPaint = !hasSlotOverrides &&
                                               HasMatchingVehicleColorOverride(
                                                   material,
                                                   rendererPropertyBlock,
                                                   vehicleTint);
                        var isDeclaredBodyRenderer = declaredBodyRenderers.Contains(renderer);
                        if (!hasSlotPaint && !hasRendererPaint && !isDeclaredBodyRenderer)
                            continue;

                        // A material-index block fully takes precedence over the renderer-wide block.
                        // Body meshes must therefore receive the finish through the same block that
                        // currently wins for that slot. Capture this route so it can be refreshed if
                        // a vehicle's color-preview hook creates or removes a slot block later.
                        var finishMaterialIndex = hasSlotPaint || (isDeclaredBodyRenderer && hasSlotOverrides)
                            ? materialIndex
                            : -1;
                        var propertyBlock = finishMaterialIndex >= 0 ? slotPropertyBlock : rendererPropertyBlock;

                        if (TryFindFinishProperty(material, out var propertyId, out var propertyName, out var propertyKind))
                        {
                            AddBinding(
                                bindings,
                                renderer,
                                finishMaterialIndex,
                                materialIndex,
                                material,
                                propertyBlock,
                                propertyId,
                                propertyName,
                                propertyKind);
                        }

                        if (material.HasProperty(CoatMaskPropertyId))
                        {
                            AddBinding(
                                bindings,
                                renderer,
                                finishMaterialIndex,
                                materialIndex,
                                material,
                                propertyBlock,
                                CoatMaskPropertyId,
                                "_CoatMask",
                                FinishPropertyKind.CoatMask);
                        }
                    }
                }

                return new VehicleFinishState(carFeatures, bindings);
            }

            internal bool Matches(CarFeatures candidate)
            {
                if (!ReferenceEquals(carFeatures, candidate))
                    return false;

                foreach (var binding in Bindings)
                {
                    if (!binding.TargetRouteIsCurrent())
                        return false;
                }

                return true;
            }

            internal void Apply(PaintFinishDefinition finish, Action<string> trace)
            {
                foreach (var binding in Bindings)
                {
                    var applied = binding.Apply(finish);
                    trace($"success={applied}; {binding.DescribeState("apply", finish)}");
                }
            }

            internal bool Matches(PaintFinishDefinition finish)
            {
                foreach (var binding in Bindings)
                {
                    if (!binding.Matches(finish))
                        return false;
                }

                return true;
            }

            private static void AddBinding(
                ICollection<RendererFinishBinding> bindings,
                Renderer renderer,
                int materialIndex,
                int trackedMaterialIndex,
                Material material,
                MaterialPropertyBlock propertyBlock,
                int propertyId,
                string propertyName,
                FinishPropertyKind propertyKind)
            {
                var factoryValue = propertyBlock.HasFloat(propertyId)
                    ? propertyBlock.GetFloat(propertyId)
                    : material.GetFloat(propertyId);
                bindings.Add(new RendererFinishBinding(
                    renderer,
                    materialIndex,
                    trackedMaterialIndex,
                    material,
                    propertyId,
                    propertyName,
                    propertyKind,
                    factoryValue));
            }

            private static bool HasMatchingVehicleColorOverride(
                Material material,
                MaterialPropertyBlock propertyBlock,
                Color vehicleTint)
            {
                return HasMatchingColor(material, propertyBlock, BaseColorPropertyId, vehicleTint) ||
                       HasMatchingColor(material, propertyBlock, ColorPropertyId, vehicleTint) ||
                       HasMatchingColor(material, propertyBlock, BaseColorFactorPropertyId, vehicleTint);
            }

            private static bool HasMatchingColor(
                Material material,
                MaterialPropertyBlock propertyBlock,
                int propertyId,
                Color expected)
            {
                if (!material.HasProperty(propertyId) || !propertyBlock.HasColor(propertyId))
                    return false;

                var actual = propertyBlock.GetColor(propertyId);
                const float tolerance = 0.015f;
                return Mathf.Abs(actual.r - expected.r) <= tolerance &&
                       Mathf.Abs(actual.g - expected.g) <= tolerance &&
                       Mathf.Abs(actual.b - expected.b) <= tolerance;
            }

            private static bool TryFindFinishProperty(
                Material material,
                out int propertyId,
                out string foundPropertyName,
                out FinishPropertyKind propertyKind)
            {
                propertyId = 0;
                foundPropertyName = string.Empty;
                propertyKind = FinishPropertyKind.SmoothnessBoost;
                var shader = material.shader;
                if (shader == null)
                    return false;

                foreach (var candidateName in SmoothnessBoostPropertyNames)
                {
                    var candidatePropertyId = Shader.PropertyToID(candidateName);
                    if (!material.HasProperty(candidatePropertyId))
                        continue;

                    propertyId = candidatePropertyId;
                    foundPropertyName = candidateName;
                    propertyKind = FinishPropertyKind.SmoothnessBoost;
                    return true;
                }

                try
                {
                    for (var index = 0; index < shader.GetPropertyCount(); index++)
                    {
                        var shaderPropertyName = shader.GetPropertyName(index);
                        var propertyDescription = shader.GetPropertyDescription(index);
                        if (!string.Equals(propertyDescription, "SmoothnessBoost", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(shaderPropertyName, "SmoothnessBoost", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(shaderPropertyName, "_SmoothnessBoost", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        propertyId = Shader.PropertyToID(shaderPropertyName);
                        if (material.HasProperty(propertyId))
                        {
                            foundPropertyName = shaderPropertyName;
                            propertyKind = FinishPropertyKind.SmoothnessBoost;
                            return true;
                        }
                    }
                }
                catch
                {
                    // Some custom shaders do not expose metadata safely. The exact-name fallbacks below
                    // still cover the standard Unity smoothness properties used by many mod vehicles.
                }

                foreach (var candidateName in NormalizedSmoothnessPropertyNames)
                {
                    var candidatePropertyId = Shader.PropertyToID(candidateName);
                    if (!material.HasProperty(candidatePropertyId))
                        continue;

                    propertyId = candidatePropertyId;
                    foundPropertyName = candidateName;
                    propertyKind = FinishPropertyKind.NormalizedSmoothness;
                    return true;
                }

                foreach (var candidateName in RoughnessPropertyNames)
                {
                    var candidatePropertyId = Shader.PropertyToID(candidateName);
                    if (!material.HasProperty(candidatePropertyId))
                        continue;

                    propertyId = candidatePropertyId;
                    foundPropertyName = candidateName;
                    propertyKind = FinishPropertyKind.Roughness;
                    return true;
                }

                return false;
            }
        }

        private enum FinishPropertyKind
        {
            SmoothnessBoost,
            NormalizedSmoothness,
            Roughness,
            CoatMask
        }

        private sealed class RendererFinishBinding
        {
            private readonly Renderer renderer;
            private readonly int materialIndex;
            private readonly int trackedMaterialIndex;
            private readonly Material material;
            private readonly int propertyId;
            private readonly string propertyName;
            private readonly FinishPropertyKind propertyKind;
            private readonly bool slotPropertyBlockWasActive;
            private readonly string shaderPropertyType;
            internal readonly float FactoryValue;

            internal RendererFinishBinding(
                Renderer renderer,
                int materialIndex,
                int trackedMaterialIndex,
                Material material,
                int propertyId,
                string propertyName,
                FinishPropertyKind propertyKind,
                float factoryValue)
            {
                this.renderer = renderer;
                this.materialIndex = materialIndex;
                this.trackedMaterialIndex = trackedMaterialIndex;
                this.material = material;
                this.propertyId = propertyId;
                this.propertyName = propertyName;
                this.propertyKind = propertyKind;
                slotPropertyBlockWasActive = materialIndex >= 0;
                shaderPropertyType = GetShaderPropertyType(material, propertyName);
                FactoryValue = factoryValue;
            }

            internal bool TargetRouteIsCurrent()
            {
                if (renderer == null || trackedMaterialIndex < 0)
                    return false;

                var slotPropertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(slotPropertyBlock, trackedMaterialIndex);
                return (!slotPropertyBlock.isEmpty) == slotPropertyBlockWasActive;
            }

            internal bool Apply(PaintFinishDefinition finish)
            {
                if (renderer == null)
                    return false;

                var value = GetExpectedValue(finish);

                var propertyBlock = new MaterialPropertyBlock();
                if (materialIndex >= 0)
                    renderer.GetPropertyBlock(propertyBlock, materialIndex);
                else
                    renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(propertyId, value);
                if (materialIndex >= 0)
                    renderer.SetPropertyBlock(propertyBlock, materialIndex);
                else
                    renderer.SetPropertyBlock(propertyBlock);

                return Matches(finish);
            }

            internal bool Matches(PaintFinishDefinition finish)
            {
                if (renderer == null)
                    return false;

                var propertyBlock = new MaterialPropertyBlock();
                if (materialIndex >= 0)
                    renderer.GetPropertyBlock(propertyBlock, materialIndex);
                else
                    renderer.GetPropertyBlock(propertyBlock);
                return propertyBlock.HasFloat(propertyId) &&
                       Mathf.Approximately(propertyBlock.GetFloat(propertyId), GetExpectedValue(finish));
            }

            internal string DescribeState(string stage, PaintFinishDefinition? finish)
            {
                if (renderer == null)
                    return $"stage='{stage}', renderer=destroyed, slot={trackedMaterialIndex}, " +
                           $"shaderProperty='{propertyName}'#{propertyId}.";

                var rendererBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(rendererBlock);
                var slotBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(slotBlock, trackedMaterialIndex);
                var visibleBlock = slotBlock.isEmpty ? rendererBlock : slotBlock;
                var materialValue = material != null && material.HasProperty(propertyId)
                    ? material.GetFloat(propertyId).ToString("0.000")
                    : "unsupported";
                var expected = finish.HasValue
                    ? GetExpectedValue(finish.Value).ToString("0.000")
                    : "n/a";
                var colors = DescribePaintColorValues(material, rendererBlock, slotBlock);
                var currentMaterial = GetCurrentSlotMaterial();
                return $"stage='{stage}', renderer='{GetHierarchyPath(renderer.transform)}#{renderer.GetInstanceID()}', " +
                       $"slot={trackedMaterialIndex}, material='{currentMaterial?.name ?? "null"}#{currentMaterial?.GetInstanceID() ?? 0}', " +
                       $"capturedMaterialMatches={(ReferenceEquals(currentMaterial, material))}, " +
                       $"shader='{currentMaterial?.shader?.name ?? "null"}', " +
                       $"property='{propertyName}'#{propertyId} type='{shaderPropertyType}' kind='{propertyKind}', " +
                       $"route='{(materialIndex >= 0 ? "material-slot" : "renderer-wide")}', " +
                       $"slotBlockEmpty={slotBlock.isEmpty}, materialValue={materialValue}, " +
                       $"rendererBlockValue={DescribeBlockFloat(rendererBlock)}, " +
                       $"slotBlockValue={DescribeBlockFloat(slotBlock)}, " +
                       $"effectiveBlock='{(slotBlock.isEmpty ? "renderer-wide" : "material-slot")}', " +
                       $"effectiveValue={DescribeBlockFloat(visibleBlock)}, expected={expected}, " +
                       $"paintColors=[{colors}].";
            }

            private Material? GetCurrentSlotMaterial()
            {
                var materials = renderer.sharedMaterials;
                return trackedMaterialIndex >= 0 && trackedMaterialIndex < materials.Length
                    ? materials[trackedMaterialIndex]
                    : null;
            }

            private string DescribeBlockFloat(MaterialPropertyBlock propertyBlock)
            {
                return propertyBlock.HasFloat(propertyId)
                    ? propertyBlock.GetFloat(propertyId).ToString("0.000")
                    : "unset";
            }

            private static string DescribePaintColorValues(
                Material? candidateMaterial,
                MaterialPropertyBlock rendererBlock,
                MaterialPropertyBlock slotBlock)
            {
                if (candidateMaterial == null)
                    return "material-missing";

                var entries = new List<string>();
                AddPaintColorValue(entries, candidateMaterial, rendererBlock, slotBlock, "_BaseColor");
                AddPaintColorValue(entries, candidateMaterial, rendererBlock, slotBlock, "_Color");
                AddPaintColorValue(entries, candidateMaterial, rendererBlock, slotBlock, "baseColorFactor");
                return entries.Count == 0 ? "no-color-properties" : string.Join(";", entries);
            }

            private static void AddPaintColorValue(
                ICollection<string> entries,
                Material candidateMaterial,
                MaterialPropertyBlock rendererBlock,
                MaterialPropertyBlock slotBlock,
                string candidateName)
            {
                var candidateId = Shader.PropertyToID(candidateName);
                if (!candidateMaterial.HasProperty(candidateId))
                    return;

                var materialValue = candidateMaterial.GetColor(candidateId);
                var rendererValue = rendererBlock.HasColor(candidateId)
                    ? rendererBlock.GetColor(candidateId).ToString()
                    : "unset";
                var slotValue = slotBlock.HasColor(candidateId)
                    ? slotBlock.GetColor(candidateId).ToString()
                    : "unset";
                entries.Add(
                    $"{candidateName}(material={materialValue},renderer={rendererValue},slot={slotValue})");
            }

            private static string GetShaderPropertyType(Material candidateMaterial, string candidatePropertyName)
            {
                try
                {
                    var shader = candidateMaterial.shader;
                    if (shader == null)
                        return "shader-missing";

                    for (var index = 0; index < shader.GetPropertyCount(); index++)
                    {
                        if (string.Equals(
                                shader.GetPropertyName(index),
                                candidatePropertyName,
                                StringComparison.Ordinal))
                        {
                            return shader.GetPropertyType(index).ToString();
                        }
                    }
                }
                catch
                {
                    // Keep property-block application working when a custom shader lacks metadata.
                }

                return "metadata-unavailable";
            }

            private static string GetHierarchyPath(Transform current)
            {
                var parts = new Stack<string>();
                while (current != null)
                {
                    parts.Push(current.name);
                    current = current.parent;
                }

                return string.Join("/", parts);
            }

            private float GetExpectedValue(PaintFinishDefinition finish)
            {
                if (finish.IsFactory)
                    return FactoryValue;

                switch (propertyKind)
                {
                    case FinishPropertyKind.SmoothnessBoost:
                        return finish.SmoothnessBoost;
                    case FinishPropertyKind.Roughness:
                        return 1f - finish.NormalizedSmoothness;
                    case FinishPropertyKind.CoatMask:
                        return finish.CoatMask;
                    default:
                        return finish.NormalizedSmoothness;
                }
            }
        }

        [Serializable]
        private sealed class PaintFinishSaveData
        {
            public List<PaintFinishSaveEntry> entries = new List<PaintFinishSaveEntry>();
        }

        [Serializable]
        private sealed class PaintFinishSaveEntry
        {
            public string vehicleId = string.Empty;
            public string finishId = string.Empty;

            public PaintFinishSaveEntry(string vehicleId, string finishId)
            {
                this.vehicleId = vehicleId;
                this.finishId = finishId;
            }
        }

        private readonly struct CustomColorDefinition
        {
            internal readonly string Name;
            internal readonly Color32 Tint;
            internal readonly Color32 FresnelColor;
            internal readonly float FresnelPower;

            internal CustomColorDefinition(string name, Color32 tint, Color32 fresnelColor, float fresnelPower)
            {
                Name = name;
                Tint = tint;
                FresnelColor = fresnelColor;
                FresnelPower = fresnelPower;
            }
        }
    }

    internal sealed class PrivateDriverPaintButtonHook : MonoBehaviour
    {
        private Button? button;
        private bool subscribed;

        private void OnEnable()
        {
            if (subscribed)
                return;

            button = GetComponent<Button>();
            if (button == null)
                return;

            button.onClick.AddListener(HandleClicked);
            subscribed = true;
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void HandleClicked()
        {
            VehicleRepainterRuntime.HandlePrivateDriverButtonClicked();
        }

        private void Unsubscribe()
        {
            if (subscribed && button != null)
                button.onClick.RemoveListener(HandleClicked);

            subscribed = false;
        }
    }
}
