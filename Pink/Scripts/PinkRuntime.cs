#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pink
{
    internal static partial class PinkRuntime
    {
        private static readonly string[] VehicleTags = { "Vehicle", "AITrafficCar" };
        private static readonly string[] NpcTags = { "NPC" };

        private static readonly string[] VehicleBehaviourTypeTokens =
        {
            "VehicleController", "CarController", "TrafficCar", "AICar", "AiCar", "ParkedVehicle", "VehicleBehaviour", "ShowcaseVehicle"
        };

        private static readonly string[] NpcBehaviourTypeTokens =
        {
            "Pedestrian", "Npc", "NPC", "Customer", "Employee", "Citizen", "Human", "CharacterController"
        };

        // These are checked against the candidate root object name only, not against the full path.
        // Full paths often contain words like Street/Building even for valid NPCs or parked vehicles.
        private static readonly string[] HardRejectRootNameTokens =
        {
            "citymanager", "city manager", "traffic system", "trafficsystem", "citymap", "roads", "road system",
            "buildingblocks", "buildings", "environment", "terrain", "canvas", "ui"
        };

        private static readonly string[] NpcSkipPathTokens =
        {
            // These are usually stationary vendor/carnival helper humans. They dominated the NPC scan,
            // while the visible walking pedestrians stayed untouched. Skip them so behaviour-based pedestrians
            // get processed first.
            "street vendor", "sm_streetvendor", "hotdogstand", "hot dog", "cottoncandy", "cotton candy",
            "popcorncart", "popcorn cart", "carnival"
        };

        private static readonly string[] NpcFallbackPathDenyTokens =
        {
            "gamemanager/player", "upperfloors", "upper floors", "ground floor", "facade", "roof",
            "building", "basement", "semi-basement", "station", "sign", "watertank", "stairs",
            "window", "garage", "metalbarricade"
        };

        private static readonly string[] NpcFallbackStrictMaterialTokens =
        {
            "m_shirt", "m_hoodie", "m_sweater", "m_suit", "m_polo", "m_croptop", "m_top",
            "m_inside", "m_uniform", "m_jacket", "m_disco", "m_sportshirt", "m_highneck",
            "m_bicolor", "m_openedshirt", "m_doctorgown", "m_sneakers"
        };

        private static readonly string[] VehicleDenyTokens =
        {
            "glass", "window", "windshield", "windscreen", "screen", "wheel", "tire", "tyre", "rim", "interior", "seat", "steering",
            "plate", "license", "licence", "holder", "light", "headlight", "taillight", "tail_light", "brake", "indicator",
            "emissive", "emission", "shadow", "decal", "mirror", "chrome", "cab", "cabin", "drivercab", "driver_cab", "cockpit",
            "damage", "damaged", "dent", "dented", "crash", "scratch", "dirt", "rust", "undercarriage", "underbody"
        };

        private static readonly string[] VehicleAllowTokens =
        {
            // Used only when the candidate root is already known to be a vehicle.
            "body", "paint", "carpaint", "car_paint", "door", "hood", "bonnet", "trunk", "boot", "bumper", "fender", "panel",
            "chassis", "shell", "exterior", "bodywork", "flatbed", "van", "truck", "taxi", "saloon", "sedan", "suv",
            "pickup", "compact", "vehicle"
        };

        private static readonly string[] VehicleFallbackAllowTokens =
        {
            // Used by the global active-renderer fallback. Keep this strict; generic words like door/body/panel/exterior
            // also appear on buildings. Do not use bare "van" here: it matches words like "advanced".
            "carpaint", "car_paint", "carbody", "body_car", "paint_car", "vehiclebody", "body_vehicle",
            "flatbed", "truck", "taxi", "taxicab", "yellowcab", "cabbody", "body_cab", "cab_body", "saloon", "sedan", "suv", "pickup",
            "deliveryvan", "delivery_van", "vanbody", "body_van", "van_body",
            "m_car", "m_vehicle", "m_truck", "m_van", "m_taxi", "m_cab", "m_sedan", "m_suv", "m_pickup", "m_flatbed"
        };

        private static readonly string[] TaxiAllowTokens =
        {
            "taxi", "taxicab", "yellowcab", "yellow cab", "cabbody", "body_cab", "cab_body", "m_cab"
        };

        private static readonly string[] TaxiBroadTokens =
        {
            "taxi", "taxicab", "yellowcab", "yellow cab", "cabbody", "body_cab", "cab_body", "m_cab", "cab"
        };

        private static readonly string[] VehicleDenyTokensWithoutCab =
        {
            "glass", "window", "windshield", "windscreen", "screen", "wheel", "tire", "tyre", "rim", "interior", "seat", "steering",
            "plate", "license", "licence", "holder", "light", "headlight", "taillight", "tail_light", "brake", "indicator",
            "emissive", "emission", "shadow", "decal", "mirror", "chrome",
            "damage", "damaged", "dent", "dented", "crash", "scratch", "dirt", "rust", "undercarriage", "underbody"
        };

        private static readonly string[] NpcAllowTokens =
        {
            "shirt", "tshirt", "t-shirt", "tee", "top", "torso", "upper", "chest", "cloth", "clothes", "clothing",
            "jacket", "hoodie", "sweater", "outfit", "body_top", "upperbody", "upper_body", "suit", "polo", "gown"
        };

        private static readonly string[] NpcHardDenyTokens =
        {
            "skin", "face", "eye", "brow", "lash", "mouth", "teeth", "tongue", "hand",
            "arm", "leg", "foot",
            "beard", "mustache", "moustache", "nail", "skinbase", "basewithoutsss"
        };

        private static readonly string[] NpcHairTokens =
        {
            "hair", "ponytail", "bun", "braid", "afro", "mohawk", "fringe", "bangs"
        };

        private static readonly string[] NpcUpperClothingTokens =
        {
            "shirt", "tshirt", "t-shirt", "tee", "top", "hoodie", "sweater", "jacket", "coat", "blazer", "vest",
            "polo", "gown", "uniform", "suit", "openedshirt", "highneckshirt", "hgshirt", "croptop", "body_top",
            "upperbody", "upper_body", "torso/top", "torso top"
        };

        private static readonly string[] NpcLowerClothingTokens =
        {
            "pants", "trouser", "trousers", "jean", "jeans", "cargo", "skirt", "dress", "legging", "leggings",
            "short", "shorts", "bottom", "lowerbody", "lower_body"
        };

        private static readonly string[] NpcFootwearTokens =
        {
            "shoe", "shoes", "sneaker", "sneakers", "boot", "boots", "heel", "heels", "loafer", "loafers", "sock", "socks"
        };

        private static readonly string[] NpcHeadwearTokens =
        {
            "hat", "cap", "beanie", "helmet", "visor"
        };

        private static readonly string[] NpcAccessoryTokens =
        {
            "glasses", "scarf", "bag", "backpack", "tie", "apron", "belt", "watch", "bracelet"
        };

        private static readonly int[] CandidateColorPropertyIds =
        {
            Shader.PropertyToID("_BaseColor"),
            Shader.PropertyToID("_Color"),
            Shader.PropertyToID("_MainColor"),
            Shader.PropertyToID("_TintColor"),
            Shader.PropertyToID("_VehicleColor"),
            Shader.PropertyToID("_PaintColor"),
            Shader.PropertyToID("_PrimaryColor"),
            Shader.PropertyToID("_SecondaryColor"),
            Shader.PropertyToID("_Color1"),
            Shader.PropertyToID("_Color2"),
            Shader.PropertyToID("_AlbedoColor"),
            Shader.PropertyToID("_DiffuseColor")
        };

        private static readonly string[] ShaderColorPropertyDenyTokens =
        {
            "emission", "emissive", "rim", "outline", "shadow", "spec", "highlight", "damage", "dirt"
        };

        private static readonly Dictionary<int, int[]> ShaderColorPropertyIdCache = new Dictionary<int, int[]>();

        private static readonly bool AggressiveNpcClothingTint = true;
        private const bool EnableNpcDiagnosticSamples = false;
        private const int MaxNpcDiagnosticSamples = 80;
        private const int MaxTaxiDiagnosticSamples = 0;
        private const int MaxTrafficLightDiagnosticSamples = 0;
        private const int MaxBlueBinDiagnosticSamples = 0;
        private const int RendererFallbackMaxPasses = 1;

        private static int npcDiagnosticSamples;
        private static int taxiDiagnosticSamples;
        private static int trafficLightDiagnosticSamples;
        private static int blueBinDiagnosticSamples;

        private static readonly Color BasePinkColor = new Color(1f, 0.35f, 0.7f, 1f);

        private static readonly Color[] PinkPalette =
        {
            new Color(0.45f, 0.03f, 0.28f, 1f), // darker 3
            new Color(0.62f, 0.08f, 0.38f, 1f), // darker 2
            new Color(0.82f, 0.18f, 0.52f, 1f), // darker 1
            new Color(1f, 0.35f, 0.7f, 1f),     // base / current bright pink
            new Color(1f, 0.50f, 0.80f, 1f),    // lighter 1
            new Color(1f, 0.65f, 0.88f, 1f),    // lighter 2
            new Color(1f, 0.80f, 0.95f, 1f)     // lighter 3
        };

        private static readonly Color FixedBrightPink = new Color(1f, 0.35f, 0.7f, 1f);
        private static readonly Color FixedDarkPink = new Color(0.62f, 0.08f, 0.38f, 1f);
        private static readonly Color FixedTrashPink = new Color(0.82f, 0.18f, 0.52f, 1f);
        private static readonly Color FixedTrashLightPink = new Color(1f, 0.80f, 0.95f, 1f);
        private const float HotDogStandTintStrength = 0.5f;

        private static readonly Dictionary<int, MaterialColorSnapshot> PatchedMaterials = new Dictionary<int, MaterialColorSnapshot>();
        private static readonly Dictionary<RendererSlotKey, RendererPropertyBlockSnapshot> PatchedRendererSlots = new Dictionary<RendererSlotKey, RendererPropertyBlockSnapshot>();
        private static readonly HashSet<int> ProcessedVehicleRoots = new HashSet<int>();
        private static readonly HashSet<int> ProcessedNpcRoots = new HashSet<int>();
        private static readonly HashSet<int> ProcessedVehicleRenderers = new HashSet<int>();
        private static readonly HashSet<int> ProcessedNpcRenderers = new HashSet<int>();
        private static readonly HashSet<int> ProcessedPlayerRenderers = new HashSet<int>();
        private static readonly HashSet<int> ProcessedWorldPropRenderers = new HashSet<int>();
        private static readonly HashSet<int> SeenCandidateRoots = new HashSet<int>();
        private static readonly HashSet<int> LoggedRejectedRoots = new HashSet<int>();
        private static readonly Dictionary<string, int> BehaviourTypeHitCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static IModLogger? logger;

        internal static int PatchedMaterialCount => PatchedMaterials.Count;
        internal static int PatchedRendererSlotCount => PatchedRendererSlots.Count;
        internal static int ProcessedVehicleRootCount => ProcessedVehicleRoots.Count;
        internal static int ProcessedNpcRootCount => ProcessedNpcRoots.Count;
        internal static int CandidateRootCount => SeenCandidateRoots.Count;
        internal static int ProcessedVehicleRendererCount => ProcessedVehicleRenderers.Count;
        internal static int ProcessedNpcRendererCount => ProcessedNpcRenderers.Count;
        internal static int ProcessedPlayerRendererCount => ProcessedPlayerRenderers.Count;

        internal static void Initialize(string modId, IModLogger? modLogger, bool enableDebugLogging, bool enableVerbosePatchLogging)
        {
            logger = modLogger;
            PinkFileLogger.Initialize(modId, modLogger, enableDebugLogging, enableVerbosePatchLogging);
            LoadLiveDebugSelections();
            PinkFileLogger.Info(
                "PinkRuntime initialized. Renderer layer scan is disabled; scanning controller/tag candidates only. " +
                "Vehicle and NPC tinting is filtered per active renderer/material slot. Vehicle renderer fallback is enabled with stricter vehicle tokens and dynamic shader color-property discovery and delivery-truck cab filtering and aggressive NPC clothing tinting, damage-material filtering, and street-vendor NPC exclusion and strict active NPC clothing renderer fallback and parked-car-stable 7-tone pink palette plus strict world-prop scan for hydrants, trash bins, darker mailboxes, and 50-percent tinted hotdog stands, dark-pink BikeRentalStand holders, PINKCITY_FINAL_V15 release: stronger UI, very light candy-pink headers, player Torso/Top texture tint, bike-stand/world/vehicle/NPC tint.",
                alsoGameLog: true);
        }

        internal static void Reset()
        {
            ResetExplicitUiTintState();
            ResetPlayerTextureState();

            PinkFileLogger.Info(
                $"PinkRuntime reset. patchedMaterials={PatchedMaterials.Count} patchedRendererSlots={PatchedRendererSlots.Count} " +
                $"processedVehicles={ProcessedVehicleRoots.Count} processedNpcs={ProcessedNpcRoots.Count} processedVehicleRenderers={ProcessedVehicleRenderers.Count} processedNpcRenderers={ProcessedNpcRenderers.Count} processedPlayerRenderers={ProcessedPlayerRenderers.Count} candidates={SeenCandidateRoots.Count}.",
                alsoGameLog: true);

            PatchedMaterials.Clear();
            PatchedRendererSlots.Clear();
            ProcessedVehicleRoots.Clear();
            ProcessedNpcRoots.Clear();
            ProcessedVehicleRenderers.Clear();
            ProcessedNpcRenderers.Clear();
            ProcessedPlayerRenderers.Clear();
            ProcessedWorldPropRenderers.Clear();
            SeenCandidateRoots.Clear();
            LoggedRejectedRoots.Clear();
            BehaviourTypeHitCounts.Clear();
            ShaderColorPropertyIdCache.Clear();
            ResetLiveDebugState();
            npcDiagnosticSamples = 0;
            taxiDiagnosticSamples = 0;
            trafficLightDiagnosticSamples = 0;
            blueBinDiagnosticSamples = 0;
            logger = null;
            PinkFileLogger.Shutdown();
        }

        internal static ScanResult ApplyPinkPass(int passIndex)
        {
            var foundVehicleCandidates = 0;
            var foundNpcCandidates = 0;
            var fallbackVehicleRenderers = 0;
            var vehicleRootPatches = 0;
            var vehicleFallbackPatches = 0;
            var npcPatches = 0;
            var npcRendererFallback = 0;
            var npcFallbackPatches = 0;
            var worldPropCandidates = 0;
            var worldPropPatches = 0;
            var newCandidateCount = 0;
            var newPatchCount = 0;
            var skippedAlreadyProcessed = 0;
            var passStart = Time.realtimeSinceStartup;
            var playerUpperBodyPatches = 0;

            foreach (var root in EnumerateVehicleRoots())
            {
                if (root == null)
                    continue;

                foundVehicleCandidates++;
                var rootId = root.GetInstanceID();
                if (SeenCandidateRoots.Add(rootId))
                    newCandidateCount++;

                if (ProcessedVehicleRoots.Contains(rootId))
                {
                    skippedAlreadyProcessed++;
                    continue;
                }

                ProcessedVehicleRoots.Add(rootId);
                var patched = TintVehicleRoot(root);
                vehicleRootPatches += patched;
                newPatchCount += patched;
            }

            if (passIndex <= RendererFallbackMaxPasses)
            {
                foreach (var renderer in EnumerateVehicleFallbackRenderers())
                {
                    if (renderer == null)
                        continue;

                    fallbackVehicleRenderers++;
                    var rendererId = renderer.GetInstanceID();
                    if (ProcessedVehicleRenderers.Contains(rendererId))
                    {
                        skippedAlreadyProcessed++;
                        continue;
                    }

                    ProcessedVehicleRenderers.Add(rendererId);
                    var patched = TintRendererMaterials(renderer, RootKind.Vehicle, strictVehicleFallback: true);
                    vehicleFallbackPatches += patched;
                    newPatchCount += patched;
                }
            }

            foreach (var root in EnumerateNpcRoots())
            {
                if (root == null)
                    continue;

                foundNpcCandidates++;
                var rootId = root.GetInstanceID();
                if (SeenCandidateRoots.Add(rootId))
                    newCandidateCount++;

                if (ProcessedNpcRoots.Contains(rootId))
                {
                    skippedAlreadyProcessed++;
                    continue;
                }

                ProcessedNpcRoots.Add(rootId);
                var patched = TintNpcRoot(root);
                npcPatches += patched;
                newPatchCount += patched;
            }

            if (passIndex <= RendererFallbackMaxPasses)
            {
                foreach (var renderer in EnumerateNpcFallbackRenderers())
                {
                    if (renderer == null)
                        continue;

                    npcRendererFallback++;
                    var rendererId = renderer.GetInstanceID();
                    if (ProcessedNpcRenderers.Contains(rendererId))
                    {
                        skippedAlreadyProcessed++;
                        continue;
                    }

                    ProcessedNpcRenderers.Add(rendererId);
                    var patched = TintRendererMaterials(renderer, RootKind.Npc);
                    npcFallbackPatches += patched;
                    newPatchCount += patched;
                }
            }

            foreach (var renderer in EnumerateSimpleWorldPropRenderers())
            {
                if (renderer == null)
                    continue;

                worldPropCandidates++;
                var rendererId = renderer.GetInstanceID();
                if (ProcessedWorldPropRenderers.Contains(rendererId))
                {
                    skippedAlreadyProcessed++;
                    continue;
                }

                ProcessedWorldPropRenderers.Add(rendererId);
                var patched = TintSimpleWorldPropRenderer(renderer);
                worldPropPatches += patched;
                newPatchCount += patched;
            }

            playerUpperBodyPatches = TintPlayerUpperBodyIfAvailable();
            newPatchCount += playerUpperBodyPatches;

            var elapsedMs = (Time.realtimeSinceStartup - passStart) * 1000f;
            PinkFileLogger.Info(
                $"Pass {passIndex}: vehicleCandidates={foundVehicleCandidates}, vehicleRendererFallback={fallbackVehicleRenderers}, npcCandidates={foundNpcCandidates}, " +
                $"newCandidates={newCandidateCount}, alreadyProcessed={skippedAlreadyProcessed}, newPatches={newPatchCount}, " +
                $"vehicleRootPatches={vehicleRootPatches}, vehicleFallbackPatches={vehicleFallbackPatches}, npcPatches={npcPatches}, " +
                $"playerUpperBodyPatches={playerUpperBodyPatches}, " +
                $"npcRendererFallback={npcRendererFallback}, npcFallbackPatches={npcFallbackPatches}, " +
                $"worldPropCandidates={worldPropCandidates}, worldPropPatches={worldPropPatches}, " +
                $"totalMaterials={PatchedMaterials.Count}, totalRendererSlots={PatchedRendererSlots.Count}, elapsedMs={elapsedMs:0.0}");

            if (passIndex == 1)
                LogBehaviourTypeSummary();

            return new ScanResult(ready: true, foundVehicleCandidates + fallbackVehicleRenderers + foundNpcCandidates + npcRendererFallback, newCandidateCount, newPatchCount);
        }

        internal static bool IsSinglePassWorldReady(out string readinessReason)
        {
            var playerRoot = GetPrimaryPlayerRoot();
            if (playerRoot == null)
            {
                readinessReason = "waiting-player-root";
                return false;
            }

            if (!HasActiveRenderer(playerRoot))
            {
                readinessReason = "waiting-player-renderers";
                return false;
            }

            var pedestrianPoolReady =
                HasActiveObjectAtPath("GameManager/PoolingManager/Pedestrian") ||
                HasActiveObjectAtPath("GameManager/PoolingManager/Pedestrians") ||
                HasActiveObjectAtPath("GameManager/PoolingManager/CasualAi");

            var vehicleRootsReady =
                HasActiveObjectAtPath("AiVehicles") ||
                HasActiveObjectAtPath("ParkedVehicles") ||
                HasActiveObjectAtPath("PlayerVehicles");

            if (!pedestrianPoolReady && !vehicleRootsReady)
            {
                readinessReason = "waiting-city-pools";
                return false;
            }

            readinessReason = pedestrianPoolReady
                ? (vehicleRootsReady ? "player-and-city-pools-ready" : "player-and-pedestrian-pool-ready")
                : "player-and-vehicle-pools-ready";

            return true;
        }

        internal static void Restore()
        {
            var restoredLiveDebugPreview = RestoreLiveDebugPreview();
            var restoredExplicitUiGraphics = RestoreExplicitUiTint();
            var restoredPlayerTextures = RestorePlayerTextureOverrides();
            var restoredMaterialProperties = 0;
            foreach (var snapshot in PatchedMaterials.Values)
            {
                if (snapshot.Material == null)
                    continue;

                foreach (var property in snapshot.Properties)
                {
                    if (snapshot.Material.HasProperty(property.PropertyId))
                    {
                        snapshot.Material.SetColor(property.PropertyId, property.OriginalColor);
                        restoredMaterialProperties++;
                    }
                }
            }

            var restoredRendererSlots = 0;
            foreach (var snapshot in PatchedRendererSlots.Values)
            {
                if (snapshot.Renderer == null)
                    continue;

                snapshot.Renderer.SetPropertyBlock(snapshot.OriginalBlock, snapshot.MaterialIndex);
                restoredRendererSlots++;
            }

            PinkFileLogger.Info(
                $"Restore complete. restoredLiveDebugPreview={restoredLiveDebugPreview}, restoredMaterialProperties={restoredMaterialProperties}, restoredRendererSlots={restoredRendererSlots}, restoredExplicitUiGraphics={restoredExplicitUiGraphics}, restoredPlayerTextures={restoredPlayerTextures}.",
                alsoGameLog: true);
        }


        internal readonly struct ScanResult
        {
            internal ScanResult(bool ready, int foundTargetCount, int newCandidateCount, int newPatchCount)
            {
                Ready = ready;
                FoundTargetCount = foundTargetCount;
                NewCandidateCount = newCandidateCount;
                NewPatchCount = newPatchCount;
            }

            internal bool Ready { get; }
            internal int FoundTargetCount { get; }
            internal int NewCandidateCount { get; }
            internal int NewPatchCount { get; }
        }

        private enum RootKind
        {
            Vehicle,
            Npc
        }

        private static bool HasActiveObjectAtPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            GameObject? target = null;
            try
            {
                target = GameObject.Find(path);
            }
            catch
            {
                return false;
            }

            return target != null && target.activeInHierarchy;
        }

        private static bool HasActiveRenderer(GameObject root)
        {
            if (root == null)
                return false;

            Renderer[] renderers;
            try
            {
                renderers = root.GetComponentsInChildren<Renderer>(false);
            }
            catch
            {
                return false;
            }

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                    return true;
            }

            return false;
        }
    }
}
