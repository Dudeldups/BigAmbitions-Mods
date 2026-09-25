#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Pink
{
    internal static partial class PinkRuntime
    {
        private const float LiveDebugRadiusMeters = 5f;
        private const int LiveDebugMaxEntries = 120;
        private static readonly Color LiveDebugSelectedPreviewColor = new Color(1f, 0.92f, 0.15f, 1f);

        private static readonly Dictionary<string, LiveDebugSelection> LiveDebugSelections = new Dictionary<string, LiveDebugSelection>(StringComparer.Ordinal);
        private static readonly List<LiveDebugCandidate> LiveDebugCandidates = new List<LiveDebugCandidate>();
        private static readonly Dictionary<int, MaterialColorSnapshot> LiveDebugPatchedMaterials = new Dictionary<int, MaterialColorSnapshot>();
        private static readonly Dictionary<int, HashSet<string>> LiveDebugMaterialSelectionUsers = new Dictionary<int, HashSet<string>>();
        private static readonly Dictionary<RendererSlotKey, RendererPropertyBlockSnapshot> LiveDebugPatchedRendererSlots = new Dictionary<RendererSlotKey, RendererPropertyBlockSnapshot>();
        private static readonly Dictionary<RendererSlotKey, HashSet<string>> LiveDebugRendererSlotSelectionUsers = new Dictionary<RendererSlotKey, HashSet<string>>();

        private static Rect liveDebugWindowRect = new Rect(20f, 120f, 860f, 720f);
        private static Vector2 liveDebugScrollPosition;
        private static bool liveDebugPositionInitialized;
        private static bool liveDebugOverlayVisible;
        private static string liveDebugStatusMessage = "Press F4 to scan nearby colorable meshes.";
        private static float liveDebugStatusUntilSeconds;
        private static Texture2D? liveDebugBlackTexture;
        private static Texture2D? liveDebugGreyTexture;
        private static Texture2D? liveDebugCardEvenTexture;
        private static Texture2D? liveDebugCardOddTexture;
        private static Texture2D? liveDebugCardSelectedTexture;
        private static GUIStyle? liveDebugSmallLabelStyle;
        private static GUIStyle? liveDebugWindowStyle;
        private static GUIStyle? liveDebugLabelStyle;
        private static GUIStyle? liveDebugButtonStyle;
        private static GUIStyle? liveDebugBoxStyle;
        private static int liveDebugHotControlId;

        internal static void HandleLiveDebugHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && liveDebugOverlayVisible)
            {
                liveDebugOverlayVisible = false;
                SetLiveDebugStatus("Closed live debug overlay.");
                return;
            }

            if (Input.GetKeyDown(KeyCode.F4))
            {
                if (liveDebugOverlayVisible)
                {
                    liveDebugOverlayVisible = false;
                    SetLiveDebugStatus("Closed live debug overlay.");
                    return;
                }

                BeginLiveDebugCapture();
            }
        }

        internal static void DrawLiveDebugOverlay()
        {
            if (!liveDebugOverlayVisible)
                return;

            EnsureLiveDebugLayoutAndStyles();
            if (liveDebugStatusUntilSeconds > 0f && Time.realtimeSinceStartup > liveDebugStatusUntilSeconds)
                liveDebugStatusUntilSeconds = 0f;

            CaptureLiveDebugHotControl();

            liveDebugWindowRect = GUI.Window(184205, liveDebugWindowRect, DrawLiveDebugWindow, "Pink Live Debug", liveDebugWindowStyle);
            ConsumeLiveDebugPointerEvents();
            ConsumeLiveDebugScrollWheelIfMouseOverWindow();
        }

        internal static bool ShouldBlockGameplayInputForLiveDebug()
        {
            return liveDebugOverlayVisible && IsMouseOverLiveDebugWindow();
        }

        private static void DrawLiveDebugWindow(int windowId)
        {
            GUILayout.Label($"Radius: {LiveDebugRadiusMeters:0.0}m  Candidates: {LiveDebugCandidates.Count}  Selected: {LiveDebugSelections.Count}", liveDebugLabelStyle);

            if (!string.IsNullOrWhiteSpace(liveDebugStatusMessage))
                GUILayout.Label(liveDebugStatusMessage, liveDebugLabelStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", liveDebugButtonStyle, GUILayout.Width(120f)))
                BeginLiveDebugCapture();

            if (GUILayout.Button("Reset All", liveDebugButtonStyle, GUILayout.Width(120f)))
                ResetAllLiveDebugSelections();

            if (GUILayout.Button("Hide", liveDebugButtonStyle, GUILayout.Width(120f)))
                liveDebugOverlayVisible = false;
            GUILayout.EndHorizontal();

            liveDebugScrollPosition = GUILayout.BeginScrollView(liveDebugScrollPosition, GUILayout.ExpandHeight(true));
            for (var index = 0; index < LiveDebugCandidates.Count; index++)
                DrawLiveDebugCandidateCard(index, LiveDebugCandidates[index]);

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save", liveDebugButtonStyle, GUILayout.Height(36f)))
                SaveLiveDebugSelectionsToDisk();

            if (GUILayout.Button("Close", liveDebugButtonStyle, GUILayout.Width(140f), GUILayout.Height(36f)))
                liveDebugOverlayVisible = false;
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        private static void DrawLiveDebugCandidateCard(int index, LiveDebugCandidate candidate)
        {
            const float cardHeight = 120f;
            var cardRect = GUILayoutUtility.GetRect(0f, cardHeight, GUILayout.ExpandWidth(true));
            var isHovered = cardRect.Contains(Event.current.mousePosition);
            DrawLiveDebugCandidateCardBackground(cardRect, index, isHovered);

            var contentRect = new Rect(cardRect.x + 8f, cardRect.y + 8f, cardRect.width - 16f, cardRect.height - 16f);
            var selected = IsLiveDebugCandidateSelected(candidate);
            GUI.Label(
                new Rect(contentRect.x, contentRect.y, contentRect.width, 22f),
                $"{index + 1}. {candidate.Distance:0.00}m  [{candidate.Category}]  {candidate.RuleLabel}  {(selected ? "HIGHLIGHTED" : "idle")}",
                liveDebugLabelStyle);

            GUI.Label(
                new Rect(contentRect.x, contentRect.y + 24f, contentRect.width, 34f),
                candidate.Path,
                liveDebugSmallLabelStyle ?? liveDebugLabelStyle);

            GUI.Label(
                new Rect(contentRect.x, contentRect.y + 60f, contentRect.width, 26f),
                $"Materials: {candidate.MaterialSummary}",
                liveDebugSmallLabelStyle ?? liveDebugLabelStyle);

            GUI.Label(
                new Rect(contentRect.x, contentRect.y + 86f, contentRect.width, 26f),
                $"Tintable slots: {candidate.SlotSummary}",
                liveDebugSmallLabelStyle ?? liveDebugLabelStyle);

            var currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0 &&
                cardRect.Contains(currentEvent.mousePosition))
            {
                if (selected)
                    ResetLiveDebugCandidate(candidate);
                else
                    PinkifyLiveDebugCandidate(candidate);

                currentEvent.Use();
            }

            GUILayout.Space(8f);
        }

        private static void BeginLiveDebugCapture()
        {
            RestoreLiveDebugPreview();

            if (PinkFileLogger.Enabled)
                DumpNearbyItemsAroundPlayer();

            var playerRoot = GetPrimaryPlayerRoot();
            if (playerRoot == null)
            {
                liveDebugOverlayVisible = true;
                SetLiveDebugStatus("F4 pressed, but no player root could be resolved.");
                return;
            }

            BuildNearbyLiveDebugCandidates(playerRoot.transform.position);
            ReapplyLiveDebugSelectionsToVisibleCandidates();
            liveDebugOverlayVisible = true;
            SetLiveDebugStatus($"Scanned nearby colorable meshes around {GetPath(playerRoot.transform, 8)}.");
        }

        private static void BuildNearbyLiveDebugCandidates(Vector3 playerPosition)
        {
            LiveDebugCandidates.Clear();
            liveDebugScrollPosition = Vector2.zero;
            var groupedCandidates = new Dictionary<string, LiveDebugCandidateBuilder>(StringComparer.Ordinal);

            Renderer[] renderers;
            try
            {
                renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(false);
            }
            catch (Exception ex)
            {
                SetLiveDebugStatus($"Renderer scan failed: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                    continue;

                var distance = Vector3.Distance(playerPosition, renderer.bounds.center);
                if (distance > LiveDebugRadiusMeters)
                    continue;

                var path = GetPath(renderer.transform, 10);
                if (ShouldExcludeLiveDebugRenderer(renderer, path))
                    continue;

                var candidate = TryBuildLiveDebugCandidate(renderer, distance, path);
                if (candidate != null)
                {
                    var groupedPath = BuildLiveDebugGroupedPath(candidate.Path);
                    var groupKey = BuildLiveDebugCandidateGroupKey(candidate.Category, groupedPath);
                    if (!groupedCandidates.TryGetValue(groupKey, out var builder))
                    {
                        builder = new LiveDebugCandidateBuilder(candidate.Category, candidate.RuleLabel, groupedPath);
                        groupedCandidates[groupKey] = builder;
                    }

                    builder.Add(candidate);
                }
            }

            foreach (var builder in groupedCandidates.Values)
                LiveDebugCandidates.Add(builder.Build());

            LiveDebugCandidates.Sort((left, right) => left.Distance.CompareTo(right.Distance));
            if (LiveDebugCandidates.Count > LiveDebugMaxEntries)
                LiveDebugCandidates.RemoveRange(LiveDebugMaxEntries, LiveDebugCandidates.Count - LiveDebugMaxEntries);
        }

        private static bool ShouldExcludeLiveDebugRenderer(Renderer renderer, string path)
        {
            if (renderer == null)
                return true;

            var layerName = LayerMask.LayerToName(renderer.gameObject.layer) ?? string.Empty;
            if (string.Equals(layerName, "Human", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(layerName, "AiVehicles", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(layerName, "ParkedVehicles", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.StartsWith("GameManager/Player", StringComparison.OrdinalIgnoreCase))
                return true;

            if (path.StartsWith("TrafficHolder/", StringComparison.OrdinalIgnoreCase))
                return true;

            if (path.IndexOf("HumanDefinition", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (path.IndexOf("ba:vehicletype_", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private static LiveDebugCandidate? TryBuildLiveDebugCandidate(Renderer renderer, float distance, string path)
        {
            Material[] sharedMaterials;
            try
            {
                sharedMaterials = renderer.sharedMaterials;
            }
            catch
            {
                return null;
            }

            if (sharedMaterials == null || sharedMaterials.Length == 0)
                return null;

            var slots = new List<LiveDebugSlotInfo>();
            var worldColor = GetSimpleWorldPropColor(renderer);
            var worldRule = worldColor == null ? string.Empty : GetSimpleWorldPropRuleName(renderer);
            var safeNpcRoot = ResolveSafeRoot(renderer.gameObject, RootKind.Npc, "live-debug");
            var safeVehicleRoot = ResolveSafeRoot(renderer.gameObject, RootKind.Vehicle, "live-debug");
            var npcLike = safeNpcRoot != null || LooksLikeNpcClothingRenderer(renderer);
            var vehicleLike = safeVehicleRoot != null || LooksLikeVehicleRenderer(renderer);
            var vehicleStrictFallback = safeVehicleRoot == null;
            var worldLike = worldColor != null;
            var rendererShouldSkipVehicle = ShouldSkipWholeVehicleRenderer(renderer);

            for (var materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
            {
                var material = sharedMaterials[materialIndex];
                if (material == null)
                    continue;

                var signature = BuildLiveDebugSlotSignature(renderer, materialIndex, material);
                if (LiveDebugSelections.TryGetValue(signature, out var savedSelection))
                {
                    slots.Add(new LiveDebugSlotInfo(
                        renderer,
                        signature,
                        materialIndex,
                        material.name ?? "<null>",
                        savedSelection.Color,
                        string.IsNullOrWhiteSpace(savedSelection.RuleLabel) ? "saved override" : savedSelection.RuleLabel));
                    continue;
                }

                if (worldLike)
                {
                    if (ShouldSkipSimpleWorldPropSlot(renderer, material))
                        continue;

                    if (!ShouldTintSimpleWorldPropSlot(renderer, material, GetRendererParentAndSharedMaterialNameText(renderer)))
                        continue;

                    slots.Add(new LiveDebugSlotInfo(renderer, signature, materialIndex, material.name ?? "<null>", worldColor!.Value, worldRule));
                    continue;
                }

                if (npcLike && ShouldTintNpcMaterialSlot(renderer, material, materialIndex))
                {
                    slots.Add(new LiveDebugSlotInfo(
                        renderer,
                        signature,
                        materialIndex,
                        material.name ?? "<null>",
                        GetPinkColorFor(renderer, material, materialIndex, RootKind.Npc, false),
                        "npc clothing"));
                    continue;
                }

                if (vehicleLike && !rendererShouldSkipVehicle && ShouldTintVehicleMaterialSlot(renderer, material, materialIndex, sharedMaterials.Length, vehicleStrictFallback))
                {
                    slots.Add(new LiveDebugSlotInfo(
                        renderer,
                        signature,
                        materialIndex,
                        material.name ?? "<null>",
                        GetPinkColorFor(renderer, material, materialIndex, RootKind.Vehicle, vehicleStrictFallback),
                        vehicleStrictFallback ? "vehicle fallback" : "vehicle root"));
                }
            }

            if (slots.Count == 0)
                return null;

            var category = worldLike ? "World" : npcLike ? "NPC" : vehicleLike ? "Vehicle" : "Saved";
            var ruleLabel = worldLike
                ? worldRule
                : npcLike
                    ? "npc clothing"
                    : vehicleLike
                        ? (vehicleStrictFallback ? "vehicle fallback" : "vehicle root")
                        : "saved override";

            return new LiveDebugCandidate(
                distance,
                category,
                ruleLabel,
                NormalizeLiveDebugCandidatePath(path),
                BuildMaterialSummary(sharedMaterials),
                slots.ToArray());
        }

        private static string GetSimpleWorldPropRuleName(Renderer renderer)
        {
            var text = GetRendererParentAndSharedMaterialNameText(renderer);

            if (IsStatueRenderer(renderer, text))
                return "world: statue";

            if (IsUmbrellaRenderer(renderer, text))
                return "world: umbrella";

            if (IsCrosswalkRenderer(renderer, text))
                return "world: crosswalk";

            if (ContainsAnyToken(text, new[] { "hydrant", "firehydrant", "fire_hydrant", "fire hydrant" }))
                return "world: hydrant";

            if (IsBikeRentalStandRenderer(renderer, text))
                return "world: bike stand";

            if (IsTrashCanText(text))
                return "world: trash bin";

            if (LooksLikeClosedBlueTrashBin(renderer, text))
                return "world: blue bin";

            if (IsMailboxText(text))
                return "world: mailbox";

            return "world prop";
        }

        private static void ReapplyLiveDebugSelectionsToVisibleCandidates()
        {
            for (var index = 0; index < LiveDebugCandidates.Count; index++)
            {
                var candidate = LiveDebugCandidates[index];
                if (!IsLiveDebugCandidateSelected(candidate))
                    continue;

                ApplyLiveDebugPreview(candidate);
            }
        }

        private static bool IsLiveDebugCandidateSelected(LiveDebugCandidate candidate)
        {
            for (var index = 0; index < candidate.Slots.Length; index++)
            {
                if (LiveDebugSelections.ContainsKey(candidate.Slots[index].Signature))
                    return true;
            }

            return false;
        }

        private static void PinkifyLiveDebugCandidate(LiveDebugCandidate candidate)
        {
            for (var index = 0; index < candidate.Slots.Length; index++)
            {
                var slot = candidate.Slots[index];
                LiveDebugSelections[slot.Signature] = new LiveDebugSelection(
                    slot.Signature,
                    candidate.Path,
                    slot.MaterialName,
                    slot.Color,
                    slot.RuleLabel);
            }

            ApplyLiveDebugPreview(candidate);
            SetLiveDebugStatus($"Highlighted {candidate.Path}.");
        }

        private static void ResetLiveDebugCandidate(LiveDebugCandidate candidate)
        {
            var removedAny = false;
            for (var index = 0; index < candidate.Slots.Length; index++)
            {
                var slot = candidate.Slots[index];
                if (!LiveDebugSelections.Remove(slot.Signature))
                    continue;

                removedAny = true;
                RestoreLiveDebugPreviewSlot(slot.Renderer, slot, slot.Signature);
            }

            if (removedAny)
                SetLiveDebugStatus($"Reset {candidate.Path}.");
        }

        private static void ResetAllLiveDebugSelections()
        {
            LiveDebugSelections.Clear();
            RestoreLiveDebugPreview();
            SetLiveDebugStatus("Cleared all current live-debug selections.");
        }

        private static void ApplyLiveDebugPreview(LiveDebugCandidate candidate)
        {
            for (var index = 0; index < candidate.Slots.Length; index++)
            {
                var slot = candidate.Slots[index];
                if (!LiveDebugSelections.TryGetValue(slot.Signature, out var selection))
                    continue;

                Material[] materials;
                try
                {
                    materials = slot.Renderer.materials;
                }
                catch (Exception ex)
                {
                    SetLiveDebugStatus($"Preview failed for {candidate.Path}: {ex.GetType().Name}: {ex.Message}");
                    return;
                }

                if (slot.MaterialIndex < 0 || slot.MaterialIndex >= materials.Length)
                    continue;

                var material = materials[slot.MaterialIndex];
                if (material == null)
                    continue;

                ApplyLiveDebugPreviewToMaterial(material, LiveDebugSelectedPreviewColor, selection.Signature);
                ApplyLiveDebugPreviewToRendererSlot(slot.Renderer, slot.MaterialIndex, material, LiveDebugSelectedPreviewColor, selection.Signature);
            }
        }

        private static string BuildLiveDebugCandidateGroupKey(string category, string groupedPath)
        {
            return category + "|" + NormalizeName(groupedPath);
        }

        private static void ApplyLiveDebugPreviewToMaterial(Material material, Color pinkColor, string selectionKey)
        {
            var materialId = material.GetInstanceID();
            if (!LiveDebugMaterialSelectionUsers.TryGetValue(materialId, out var users))
            {
                users = new HashSet<string>(StringComparer.Ordinal);
                LiveDebugMaterialSelectionUsers[materialId] = users;
            }

            var addedUser = users.Add(selectionKey);
            if (!addedUser && LiveDebugPatchedMaterials.ContainsKey(materialId))
                return;

            var changedProperties = new List<MaterialColorProperty>();
            for (var index = 0; index < CandidateColorPropertyIds.Length; index++)
            {
                var propertyId = CandidateColorPropertyIds[index];
                if (!material.HasProperty(propertyId))
                    continue;

                changedProperties.Add(new MaterialColorProperty(propertyId, material.GetColor(propertyId)));
            }

            if (changedProperties.Count == 0)
                return;

            if (!LiveDebugPatchedMaterials.ContainsKey(materialId))
                LiveDebugPatchedMaterials[materialId] = new MaterialColorSnapshot(material, changedProperties.ToArray());

            for (var index = 0; index < changedProperties.Count; index++)
                material.SetColor(changedProperties[index].PropertyId, pinkColor);
        }

        private static void ApplyLiveDebugPreviewToRendererSlot(Renderer renderer, int materialIndex, Material material, Color pinkColor, string selectionKey)
        {
            var supportedPropertyIds = GetSupportedColorPropertyIds(material);
            if (supportedPropertyIds.Count == 0)
                return;

            var key = new RendererSlotKey(renderer.GetInstanceID(), materialIndex);
            if (!LiveDebugRendererSlotSelectionUsers.TryGetValue(key, out var users))
            {
                users = new HashSet<string>(StringComparer.Ordinal);
                LiveDebugRendererSlotSelectionUsers[key] = users;
            }

            var addedUser = users.Add(selectionKey);
            if (!LiveDebugPatchedRendererSlots.ContainsKey(key))
            {
                var originalBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(originalBlock, materialIndex);
                LiveDebugPatchedRendererSlots[key] = new RendererPropertyBlockSnapshot(renderer, materialIndex, originalBlock);
            }
            else if (!addedUser)
            {
                return;
            }

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, materialIndex);
            for (var index = 0; index < supportedPropertyIds.Count; index++)
                block.SetColor(supportedPropertyIds[index], pinkColor);

            renderer.SetPropertyBlock(block, materialIndex);
        }

        private static void RestoreLiveDebugPreviewSlot(Renderer renderer, LiveDebugSlotInfo slot, string selectionKey)
        {
            if (renderer == null)
                return;

            Material[] materials;
            try
            {
                materials = renderer.materials;
            }
            catch
            {
                materials = Array.Empty<Material>();
            }

            if (slot.MaterialIndex >= 0 && slot.MaterialIndex < materials.Length)
            {
                var material = materials[slot.MaterialIndex];
                if (material != null)
                {
                    var materialId = material.GetInstanceID();
                    if (LiveDebugMaterialSelectionUsers.TryGetValue(materialId, out var materialUsers))
                    {
                        materialUsers.Remove(selectionKey);
                        if (materialUsers.Count == 0)
                        {
                            LiveDebugMaterialSelectionUsers.Remove(materialId);
                            if (LiveDebugPatchedMaterials.TryGetValue(materialId, out var materialSnapshot))
                            {
                                foreach (var property in materialSnapshot.Properties)
                                {
                                    if (materialSnapshot.Material != null && materialSnapshot.Material.HasProperty(property.PropertyId))
                                        materialSnapshot.Material.SetColor(property.PropertyId, property.OriginalColor);
                                }

                                LiveDebugPatchedMaterials.Remove(materialId);
                            }
                        }
                    }
                }
            }

            var rendererKey = new RendererSlotKey(renderer.GetInstanceID(), slot.MaterialIndex);
            if (!LiveDebugRendererSlotSelectionUsers.TryGetValue(rendererKey, out var rendererUsers))
                return;

            rendererUsers.Remove(selectionKey);
            if (rendererUsers.Count > 0)
                return;

            LiveDebugRendererSlotSelectionUsers.Remove(rendererKey);
            if (!LiveDebugPatchedRendererSlots.TryGetValue(rendererKey, out var snapshot))
                return;

            if (snapshot.Renderer != null)
                snapshot.Renderer.SetPropertyBlock(snapshot.OriginalBlock, snapshot.MaterialIndex);

            LiveDebugPatchedRendererSlots.Remove(rendererKey);
        }

        private static int RestoreLiveDebugPreview()
        {
            var restored = 0;
            foreach (var snapshot in LiveDebugPatchedMaterials.Values)
            {
                if (snapshot.Material == null)
                    continue;

                foreach (var property in snapshot.Properties)
                {
                    if (snapshot.Material.HasProperty(property.PropertyId))
                    {
                        snapshot.Material.SetColor(property.PropertyId, property.OriginalColor);
                        restored++;
                    }
                }
            }

            foreach (var snapshot in LiveDebugPatchedRendererSlots.Values)
            {
                if (snapshot.Renderer == null)
                    continue;

                snapshot.Renderer.SetPropertyBlock(snapshot.OriginalBlock, snapshot.MaterialIndex);
                restored++;
            }

            LiveDebugPatchedMaterials.Clear();
            LiveDebugMaterialSelectionUsers.Clear();
            LiveDebugPatchedRendererSlots.Clear();
            LiveDebugRendererSlotSelectionUsers.Clear();
            return restored;
        }

        private static void ResetLiveDebugState()
        {
            RestoreLiveDebugPreview();
            LiveDebugSelections.Clear();
            LiveDebugCandidates.Clear();
            liveDebugPositionInitialized = false;
            liveDebugOverlayVisible = false;
            liveDebugScrollPosition = Vector2.zero;
            liveDebugStatusMessage = "Press F4 to scan nearby colorable meshes.";
            liveDebugStatusUntilSeconds = 0f;
        }

        private static void LoadLiveDebugSelections()
        {
            LiveDebugSelections.Clear();

            var filePath = GetLiveDebugSelectionsFilePath();
            if (!File.Exists(filePath))
                return;

            try
            {
                var json = File.ReadAllText(filePath);
                var store = JsonUtility.FromJson<LiveDebugSelectionStore>(json);
                if (store?.Selections == null)
                    return;

                for (var index = 0; index < store.Selections.Length; index++)
                {
                    var entry = store.Selections[index];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Signature))
                        continue;

                    LiveDebugSelections[entry.Signature] = new LiveDebugSelection(
                        entry.Signature,
                        entry.Path ?? string.Empty,
                        entry.MaterialName ?? string.Empty,
                        new Color(entry.R, entry.G, entry.B, entry.A),
                        entry.RuleLabel ?? "saved override");
                }

                PinkFileLogger.Info($"LIVE_DEBUG loaded {LiveDebugSelections.Count} saved selections from {filePath}");
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"LIVE_DEBUG failed to load selections: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void SaveLiveDebugSelectionsToDisk()
        {
            var filePath = GetLiveDebugSelectionsFilePath();

            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var selectionList = new List<LiveDebugSelectionData>(LiveDebugSelections.Count);
                foreach (var selection in LiveDebugSelections.Values)
                {
                    selectionList.Add(new LiveDebugSelectionData
                    {
                        Signature = selection.Signature,
                        Path = selection.Path,
                        MaterialName = selection.MaterialName,
                        RuleLabel = selection.RuleLabel,
                        R = selection.Color.r,
                        G = selection.Color.g,
                        B = selection.Color.b,
                        A = selection.Color.a
                    });
                }

                var store = new LiveDebugSelectionStore
                {
                    Selections = selectionList.ToArray()
                };

                File.WriteAllText(filePath, JsonUtility.ToJson(store, true));
                SetLiveDebugStatus($"Saved {LiveDebugSelections.Count} live-debug selections to {filePath}.");
                PinkFileLogger.Info($"LIVE_DEBUG saved {LiveDebugSelections.Count} selections to {filePath}", alsoGameLog: true);
            }
            catch (Exception ex)
            {
                SetLiveDebugStatus($"Save failed: {ex.GetType().Name}: {ex.Message}");
                PinkFileLogger.Warn($"LIVE_DEBUG save failed: {ex.GetType().Name}: {ex.Message}", alsoGameLog: true);
            }
        }

        private static string GetLiveDebugSelectionsFilePath()
        {
            return Path.Combine(Application.persistentDataPath, "PinkCity", "PinkLiveDebugSelections.json");
        }

        private static string BuildLiveDebugSlotSignature(Renderer renderer, int materialIndex, Material material)
        {
            var rootName = renderer.transform.root != null ? renderer.transform.root.name : string.Empty;
            return NormalizeName(GetPath(renderer.transform, 12)) +
                   "|root=" + NormalizeName(rootName) +
                   "|renderer=" + NormalizeName(renderer.GetType().Name) +
                   "|slot=" + materialIndex +
                   "|material=" + NormalizeName(material.name);
        }

        private static void SetLiveDebugStatus(string message)
        {
            liveDebugStatusMessage = message;
            liveDebugStatusUntilSeconds = Time.realtimeSinceStartup + 6f;
        }

        private static void EnsureLiveDebugLayoutAndStyles()
        {
            if (!liveDebugPositionInitialized)
            {
                liveDebugWindowRect.x = 20f;
                liveDebugWindowRect.y = Mathf.Max(20f, (Screen.height - liveDebugWindowRect.height) * 0.5f);
                liveDebugPositionInitialized = true;
            }

            if (liveDebugBlackTexture == null)
                liveDebugBlackTexture = CreateLiveDebugTexture(new Color(0.05f, 0.05f, 0.05f, 1f));

            if (liveDebugGreyTexture == null)
                liveDebugGreyTexture = CreateLiveDebugTexture(new Color(0.20f, 0.20f, 0.20f, 1f));

            if (liveDebugCardEvenTexture == null)
                liveDebugCardEvenTexture = CreateLiveDebugTexture(new Color(0.16f, 0.16f, 0.16f, 1f));

            if (liveDebugCardOddTexture == null)
                liveDebugCardOddTexture = CreateLiveDebugTexture(new Color(0.24f, 0.24f, 0.24f, 1f));

            if (liveDebugCardSelectedTexture == null)
                liveDebugCardSelectedTexture = CreateLiveDebugTexture(new Color(0.38f, 0.33f, 0.10f, 1f));

            if (liveDebugWindowStyle == null)
            {
                liveDebugWindowStyle = new GUIStyle(GUI.skin.window);
                liveDebugWindowStyle.normal.background = liveDebugBlackTexture;
                liveDebugWindowStyle.onNormal.background = liveDebugBlackTexture;
                liveDebugWindowStyle.normal.textColor = Color.white;
                liveDebugWindowStyle.padding = new RectOffset(10, 10, 24, 10);
            }

            if (liveDebugLabelStyle == null)
            {
                liveDebugLabelStyle = new GUIStyle(GUI.skin.label);
                liveDebugLabelStyle.normal.textColor = Color.white;
                liveDebugLabelStyle.wordWrap = true;
            }

            if (liveDebugSmallLabelStyle == null)
            {
                liveDebugSmallLabelStyle = new GUIStyle(liveDebugLabelStyle);
                liveDebugSmallLabelStyle.fontSize = Mathf.Max(11, liveDebugSmallLabelStyle.fontSize - 1);
                liveDebugSmallLabelStyle.clipping = TextClipping.Clip;
            }

            if (liveDebugButtonStyle == null)
            {
                liveDebugButtonStyle = new GUIStyle(GUI.skin.button);
                liveDebugButtonStyle.normal.textColor = Color.white;
            }

            if (liveDebugBoxStyle == null)
            {
                liveDebugBoxStyle = new GUIStyle(GUI.skin.box);
                liveDebugBoxStyle.normal.background = null;
                liveDebugBoxStyle.normal.textColor = Color.white;
                liveDebugBoxStyle.padding = new RectOffset(8, 8, 8, 8);
            }
        }

        private static Texture2D CreateLiveDebugTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply(false, false);
            return texture;
        }

        private static void DrawLiveDebugCandidateCardBackground(Rect rect, int index, bool isHovered)
        {
            Texture2D? background;
            if (index >= 0 && index < LiveDebugCandidates.Count && IsLiveDebugCandidateSelected(LiveDebugCandidates[index]))
                background = liveDebugCardSelectedTexture;
            else
                background = index % 2 == 0 ? liveDebugCardEvenTexture : liveDebugCardOddTexture;

            if (background != null)
                GUI.DrawTexture(rect, background);

            if (liveDebugGreyTexture != null)
            {
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), liveDebugGreyTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), liveDebugGreyTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), liveDebugGreyTexture);
                GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), liveDebugGreyTexture);
            }
        }

        private static string NormalizeLiveDebugCandidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var normalizedSegments = new List<string>(segments.Length);
            for (var index = 0; index < segments.Length; index++)
            {
                if (IsLiveDebugLodSegment(segments[index]))
                    continue;

                normalizedSegments.Add(segments[index]);
            }

            return normalizedSegments.Count == 0 ? path : string.Join("/", normalizedSegments.ToArray());
        }

        private static string BuildLiveDebugGroupedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            var normalizedPath = NormalizeLiveDebugCandidatePath(path);
            var segments = normalizedPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length <= 2)
                return StripLiveDebugInstanceSuffixes(normalizedPath);

            if (StartsWithAnySegment(segments, 1, new[] { "sm_terracepot_", "sm_terraceumbrella_", "sm_mailbox", "prefab_bikerentalstand", "prefab_parkingmeter" }))
                return JoinLiveDebugSegments(segments, 2);

            if (StartsWithAnySegment(segments, 1, new[] { "prefab_tablegroup" }))
                return JoinLiveDebugSegments(segments, Math.Min(3, segments.Length));

            return JoinLiveDebugSegments(segments, Math.Min(3, segments.Length));
        }

        private static bool StartsWithAnySegment(string[] segments, int index, string[] prefixes)
        {
            if (index < 0 || index >= segments.Length)
                return false;

            var normalized = NormalizeName(segments[index]);
            for (var prefixIndex = 0; prefixIndex < prefixes.Length; prefixIndex++)
            {
                if (normalized.StartsWith(prefixes[prefixIndex], StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static string JoinLiveDebugSegments(string[] segments, int count)
        {
            var safeCount = Mathf.Clamp(count, 0, segments.Length);
            var result = new List<string>(safeCount);
            for (var index = 0; index < safeCount; index++)
                result.Add(StripLiveDebugInstanceSuffixes(segments[index]));

            return string.Join("/", result.ToArray());
        }

        private static string StripLiveDebugInstanceSuffixes(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            var text = value.Trim();
            while (text.EndsWith(")", StringComparison.Ordinal))
            {
                var openIndex = text.LastIndexOf('(');
                if (openIndex < 0)
                    break;

                var suffix = text.Substring(openIndex);
                var numericOnly = true;
                for (var index = 1; index < suffix.Length - 1; index++)
                {
                    if (!char.IsDigit(suffix[index]))
                    {
                        numericOnly = false;
                        break;
                    }
                }

                if (!numericOnly)
                    break;

                text = text.Substring(0, openIndex).TrimEnd();
            }

            return text;
        }

        private static bool IsLiveDebugLodSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
                return false;

            var normalized = NormalizeName(segment);
            var lodIndex = normalized.LastIndexOf("lod", StringComparison.Ordinal);
            if (lodIndex < 0)
                return false;

            if (lodIndex == 0 && normalized.Length > 3)
                return AllDigits(normalized, 3);

            if (lodIndex > 0 && lodIndex >= normalized.Length - 4)
                return AllDigits(normalized, lodIndex + 3);

            return false;
        }

        private static bool AllDigits(string text, int startIndex)
        {
            if (startIndex >= text.Length)
                return false;

            for (var index = startIndex; index < text.Length; index++)
            {
                if (!char.IsDigit(text[index]))
                    return false;
            }

            return true;
        }

        private static void ConsumeLiveDebugScrollWheelIfMouseOverWindow()
        {
            var currentEvent = Event.current;
            if (currentEvent == null || currentEvent.type != EventType.ScrollWheel)
                return;

            if (liveDebugWindowRect.Contains(currentEvent.mousePosition))
                currentEvent.Use();
        }

        private static void ConsumeLiveDebugPointerEvents()
        {
            var currentEvent = Event.current;
            if (currentEvent == null)
                return;

            if (!IsMouseOverLiveDebugWindow())
                return;

            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                case EventType.MouseUp:
                case EventType.MouseDrag:
                case EventType.MouseMove:
                case EventType.ScrollWheel:
                    currentEvent.Use();
                    break;
            }
        }

        private static void CaptureLiveDebugHotControl()
        {
            var currentEvent = Event.current;
            if (currentEvent == null)
                return;

            if (liveDebugHotControlId == 0)
                liveDebugHotControlId = GUIUtility.GetControlID(FocusType.Passive);

            if (!IsMouseOverLiveDebugWindow())
            {
                if (GUIUtility.hotControl == liveDebugHotControlId &&
                    (currentEvent.type == EventType.MouseUp || currentEvent.rawType == EventType.MouseUp))
                {
                    GUIUtility.hotControl = 0;
                }

                return;
            }

            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                case EventType.MouseDrag:
                case EventType.ScrollWheel:
                    GUIUtility.hotControl = liveDebugHotControlId;
                    currentEvent.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == liveDebugHotControlId)
                        GUIUtility.hotControl = 0;
                    currentEvent.Use();
                    break;
            }
        }

        private static bool IsMouseOverLiveDebugWindow()
        {
            var mousePosition = Input.mousePosition;
            var guiMousePosition = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
            return liveDebugWindowRect.Contains(guiMousePosition);
        }

        [Serializable]
        private sealed class LiveDebugSelectionStore
        {
            public LiveDebugSelectionData[] Selections = Array.Empty<LiveDebugSelectionData>();
        }

        [Serializable]
        private sealed class LiveDebugSelectionData
        {
            public string Signature = string.Empty;
            public string Path = string.Empty;
            public string MaterialName = string.Empty;
            public string RuleLabel = string.Empty;
            public float R;
            public float G;
            public float B;
            public float A = 1f;
        }

        private sealed class LiveDebugCandidate
        {
            internal LiveDebugCandidate(
                float distance,
                string category,
                string ruleLabel,
                string path,
                string materialSummary,
                LiveDebugSlotInfo[] slots)
            {
                Distance = distance;
                Category = category;
                RuleLabel = ruleLabel;
                Path = path;
                MaterialSummary = materialSummary;
                Slots = slots;

                var slotParts = new List<string>(slots.Length);
                var seenSlotParts = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < slots.Length; index++)
                {
                    var slotPart = $"{slots[index].MaterialIndex}:{slots[index].MaterialName}";
                    if (seenSlotParts.Add(slotPart))
                        slotParts.Add(slotPart);
                }

                SlotSummary = string.Join(" | ", slotParts.ToArray());
            }

            internal float Distance { get; }
            internal string Category { get; }
            internal string RuleLabel { get; }
            internal string Path { get; }
            internal string MaterialSummary { get; }
            internal LiveDebugSlotInfo[] Slots { get; }
            internal string SlotSummary { get; }
        }

        private readonly struct LiveDebugSlotInfo
        {
            internal LiveDebugSlotInfo(Renderer renderer, string signature, int materialIndex, string materialName, Color color, string ruleLabel)
            {
                Renderer = renderer;
                Signature = signature;
                MaterialIndex = materialIndex;
                MaterialName = materialName;
                Color = color;
                RuleLabel = ruleLabel;
            }

            internal Renderer Renderer { get; }
            internal string Signature { get; }
            internal int MaterialIndex { get; }
            internal string MaterialName { get; }
            internal Color Color { get; }
            internal string RuleLabel { get; }
        }

        private sealed class LiveDebugCandidateBuilder
        {
            private readonly List<LiveDebugSlotInfo> slots = new List<LiveDebugSlotInfo>();
            private readonly List<string> materialSummaries = new List<string>();

            internal LiveDebugCandidateBuilder(string category, string ruleLabel, string path)
            {
                Category = category;
                RuleLabel = ruleLabel;
                Path = path;
                Distance = float.MaxValue;
            }

            internal string Category { get; }
            internal string RuleLabel { get; }
            internal string Path { get; }
            internal float Distance { get; private set; }

            internal void Add(LiveDebugCandidate candidate)
            {
                Distance = Mathf.Min(Distance, candidate.Distance);
                for (var index = 0; index < candidate.Slots.Length; index++)
                    slots.Add(candidate.Slots[index]);

                if (!materialSummaries.Contains(candidate.MaterialSummary))
                    materialSummaries.Add(candidate.MaterialSummary);
            }

            internal LiveDebugCandidate Build()
            {
                var materialSummary = string.Join(" || ", materialSummaries.ToArray());
                return new LiveDebugCandidate(Distance, Category, RuleLabel, Path, materialSummary, slots.ToArray());
            }
        }

        private readonly struct LiveDebugSelection
        {
            internal LiveDebugSelection(string signature, string path, string materialName, Color color, string ruleLabel)
            {
                Signature = signature;
                Path = path;
                MaterialName = materialName;
                Color = color;
                RuleLabel = ruleLabel;
            }

            internal string Signature { get; }
            internal string Path { get; }
            internal string MaterialName { get; }
            internal Color Color { get; }
            internal string RuleLabel { get; }
        }
    }
}
