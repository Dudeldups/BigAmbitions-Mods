#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Helpers;
using UI;
using UI.Notification;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime : MonoBehaviour
    {
        private const int HiddenUiRefreshBurstFrames = 2;
        private const float FirstPersonPoiMaxDistance = 75f;
        private CullingGroup? firstPersonPoiCullingGroup;
        private Transform? firstPersonPoiPlayer;
        private readonly List<FirstPersonPoiVisualState> firstPersonPoiVisualStates = new List<FirstPersonPoiVisualState>();
        private bool lastHiddenUiCityMapOpen;
        private bool lastHiddenUiHideMapMarkers;
        private bool lastHiddenUiVehicleMode;
        private int pendingHiddenUiRefreshFrames;
        private static readonly string[] HideMapMarkersOptionKeys =
        {
            "camera_tools_hide_map_markers"
        };
        private static GameObject? cachedKnownMapMarkerRoot;
        private const float WorldTextOverlayScreenMargin = 24f;

        private void StartFirstPersonPoiDistanceFilter(Transform player)
        {
            StopFirstPersonPoiDistanceFilter();
            if (!firstPersonViewActive || IsCityMapOpen() || IsFreeCameraActive())
                return;

            var cityMap = InstanceBehavior<CityManager>.Instance?.cityMap;
            var camera = GameManager.GetMainCamera();
            if (cityMap?.pois == null || camera == null)
            {
                context?.Logger.Warn("CameraTools: first-person marker distance filter could not find the city map or camera.");
                return;
            }

            firstPersonPoiPlayer = player;
            var spheres = new List<BoundingSphere>();
            var distantCount = 0;
            foreach (var poi in cityMap.pois)
            {
                if (poi == null || !poi.Permanent || poi.target == null)
                    continue;

                var group = poi.GetComponent<CanvasGroup>();
                var addedGroup = group == null;
                if (addedGroup)
                    group = poi.gameObject.AddComponent<CanvasGroup>();
                if (group == null)
                    continue;

                firstPersonPoiVisualStates.Add(new FirstPersonPoiVisualState(
                    group, addedGroup, group.alpha, group.blocksRaycasts, group.interactable));
                spheres.Add(new BoundingSphere(poi.target.position, 0f));
                var visible = (poi.target.position - player.position).sqrMagnitude <=
                    FirstPersonPoiMaxDistance * FirstPersonPoiMaxDistance;
                if (!visible)
                    distantCount++;
                SetFirstPersonPoiVisibility(firstPersonPoiVisualStates.Count - 1, visible);
            }

            if (spheres.Count == 0)
                return;

            try
            {
                firstPersonPoiCullingGroup = new CullingGroup
                {
                    targetCamera = camera,
                    onStateChanged = OnFirstPersonPoiDistanceChanged
                };
                firstPersonPoiCullingGroup.SetBoundingSpheres(spheres.ToArray());
                firstPersonPoiCullingGroup.SetBoundingSphereCount(spheres.Count);
                firstPersonPoiCullingGroup.SetBoundingDistances(new[] { FirstPersonPoiMaxDistance });
                firstPersonPoiCullingGroup.SetDistanceReferencePoint(player);
            }
            catch (Exception exception)
            {
                StopFirstPersonPoiDistanceFilter();
                context?.Logger.Warn($"CameraTools: first-person marker distance filter could not start: {exception.Message}");
                return;
            }

            LogCameraModes($"FP marker distance filter started: range={FirstPersonPoiMaxDistance:0}m, markers={spheres.Count}, distant={distantCount}");
        }

        private void OnFirstPersonPoiDistanceChanged(CullingGroupEvent change)
        {
            if (!firstPersonViewActive || firstPersonPoiPlayer == null ||
                change.index < 0 || change.index >= firstPersonPoiVisualStates.Count)
                return;

            var visible = change.currentDistance == 0;
            SetFirstPersonPoiVisibility(change.index, visible);
            LogCameraModes($"FP marker distance changed: index={change.index}, visible={visible}");
        }

        private void SetFirstPersonPoiVisibility(int index, bool visible)
        {
            var state = firstPersonPoiVisualStates[index];
            if (state.Group == null)
                return;

            state.Group.alpha = visible ? state.OriginalAlpha : 0f;
            state.Group.blocksRaycasts = visible && state.OriginalBlocksRaycasts;
            state.Group.interactable = visible && state.OriginalInteractable;
        }

        private void StopFirstPersonPoiDistanceFilter()
        {
            if (firstPersonPoiCullingGroup != null)
            {
                firstPersonPoiCullingGroup.onStateChanged = null;
                firstPersonPoiCullingGroup.Dispose();
                firstPersonPoiCullingGroup = null;
            }

            var restoredCount = firstPersonPoiVisualStates.Count;
            foreach (var state in firstPersonPoiVisualStates)
            {
                if (state.Group == null)
                    continue;

                state.Group.alpha = state.OriginalAlpha;
                state.Group.blocksRaycasts = state.OriginalBlocksRaycasts;
                state.Group.interactable = state.OriginalInteractable;
                if (state.AddedGroup)
                    Destroy(state.Group);
            }

            firstPersonPoiVisualStates.Clear();
            firstPersonPoiPlayer = null;
            if (restoredCount > 0)
                LogCameraModes($"FP marker distance filter stopped: restored={restoredCount}");
        }

        private readonly struct FirstPersonPoiVisualState
        {
            public FirstPersonPoiVisualState(CanvasGroup group, bool addedGroup, float originalAlpha,
                bool originalBlocksRaycasts, bool originalInteractable)
            {
                Group = group;
                AddedGroup = addedGroup;
                OriginalAlpha = originalAlpha;
                OriginalBlocksRaycasts = originalBlocksRaycasts;
                OriginalInteractable = originalInteractable;
            }

            public CanvasGroup Group { get; }
            public bool AddedGroup { get; }
            public float OriginalAlpha { get; }
            public bool OriginalBlocksRaycasts { get; }
            public bool OriginalInteractable { get; }
        }

        private void HandleHideUiHotkey()
        {
            if (settings == null || !Input.GetKeyDown(settings.HideUiHotkey))
                return;

            isUiHidden = !isUiHidden;
            freeCameraAutoHidUi = false;
            if (isUiHidden)
            {
                pendingHiddenUiRefreshFrames = HiddenUiRefreshBurstFrames;
                ApplyHiddenUi();
            }
            else
            {
                pendingHiddenUiRefreshFrames = 0;
                RestoreHiddenUi();
            }
        }

        private void RefreshHiddenUiState()
        {
            if (!IsGameplayActive())
            {
                if (isUiHidden || hiddenUiStates.Length > 0)
                    RestoreHiddenUi();

                isUiHidden = false;
                pendingHiddenUiRefreshFrames = 0;
                return;
            }

            if (!isUiHidden)
            {
                if (hiddenUiStates.Length > 0)
                    RestoreHiddenUi();

                pendingHiddenUiRefreshFrames = 0;
                return;
            }

            var cityMapOpen = IsCityMapOpen();
            var hideMapMarkers = GetEffectiveHideMapMarkersWithUi();
            var vehicleMode = vehicleDebug.IsVehicleMode;
            var needsRefresh = hiddenUiStates.Length == 0;

            if (cityMapOpen != lastHiddenUiCityMapOpen ||
                hideMapMarkers != lastHiddenUiHideMapMarkers ||
                vehicleMode != lastHiddenUiVehicleMode)
            {
                pendingHiddenUiRefreshFrames = HiddenUiRefreshBurstFrames;
                needsRefresh = true;
            }

            if (!needsRefresh)
            {
                foreach (var state in hiddenUiStates)
                {
                    if (state.Target == null)
                    {
                        pendingHiddenUiRefreshFrames = HiddenUiRefreshBurstFrames;
                        needsRefresh = true;
                        break;
                    }

                    if (state.Target.activeSelf)
                        state.Target.SetActive(false);
                }
            }

            if (!needsRefresh && pendingHiddenUiRefreshFrames > 0)
            {
                pendingHiddenUiRefreshFrames--;
                needsRefresh = true;
            }

            if (!needsRefresh)
                return;

            ApplyHiddenUi();
        }

        private void KeepWorldTextOverlayOnScreen(bool cityMapOpen)
        {
            if (cityMapOpen || isUiHidden || Screen.width <= 0 || Screen.height <= 0)
                return;

            if (!TryResolveWorldTextOverlay(out var overlayRect, out var textSectionRect))
                return;

            if (!overlayRect.gameObject.activeInHierarchy || !textSectionRect.gameObject.activeInHierarchy)
                return;

            if (!TryGetScreenRect(overlayRect, out var minX, out var minY, out var maxX, out var maxY))
                return;

            var deltaX = 0f;
            var deltaY = 0f;
            var margin = WorldTextOverlayScreenMargin;

            if (minX < margin)
                deltaX = margin - minX;
            else if (maxX > Screen.width - margin)
                deltaX = (Screen.width - margin) - maxX;

            if (minY < margin)
                deltaY = margin - minY;
            else if (maxY > Screen.height - margin)
                deltaY = (Screen.height - margin) - maxY;

            if (Mathf.Abs(deltaX) <= 0.01f && Mathf.Abs(deltaY) <= 0.01f)
                return;

            var canvas = overlayRect.GetComponentInParent<Canvas>(true);
            var scaleFactor = canvas != null && canvas.scaleFactor > 0.0001f ? canvas.scaleFactor : 1f;
            overlayRect.anchoredPosition += new Vector2(deltaX / scaleFactor, deltaY / scaleFactor);
        }

        private bool TryResolveWorldTextOverlay(out RectTransform overlayRect, out RectTransform textSectionRect)
        {
            overlayRect = producerOverlayRect!;
            textSectionRect = worldTextSectionRect!;

            if (IsValidWorldTextOverlayRect(producerOverlayRect) && IsValidWorldTextOverlayRect(worldTextSectionRect))
            {
                overlayRect = producerOverlayRect!;
                textSectionRect = worldTextSectionRect!;
                return true;
            }

            if (Time.unscaledTime < nextWorldTextOverlaySearchTime)
                return false;

            nextWorldTextOverlaySearchTime = Time.unscaledTime + 0.5f;
            producerOverlayRect = null;
            worldTextSectionRect = null;

            foreach (var rectTransform in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                if (rectTransform == null)
                    continue;

                var path = GetHierarchyPath(rectTransform);
                if (producerOverlayRect == null &&
                    path.IndexOf("Canvases/InteriorDesignerUI/Overlays/ProducerOverlay", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    path.EndsWith("/ProducerOverlay", StringComparison.OrdinalIgnoreCase))
                {
                    producerOverlayRect = rectTransform;
                }
                else if (worldTextSectionRect == null &&
                    path.IndexOf("Canvases/InteriorDesignerUI/Overlays/ProducerOverlay/WorldTextSection", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    path.EndsWith("/WorldTextSection", StringComparison.OrdinalIgnoreCase))
                {
                    worldTextSectionRect = rectTransform;
                }

                if (producerOverlayRect != null && worldTextSectionRect != null)
                    break;
            }

            if (!IsValidWorldTextOverlayRect(producerOverlayRect) || !IsValidWorldTextOverlayRect(worldTextSectionRect))
                return false;

            overlayRect = producerOverlayRect!;
            textSectionRect = worldTextSectionRect!;
            return true;
        }

        private static bool IsValidWorldTextOverlayRect(RectTransform? rectTransform)
        {
            return rectTransform != null && rectTransform.gameObject != null;
        }

        private void ApplyHiddenUi()
        {
            RestoreHiddenUi();

            var cityMapOpen = IsCityMapOpen();
            var hideMapMarkers = GetEffectiveHideMapMarkersWithUi();
            lastHiddenUiCityMapOpen = cityMapOpen;
            lastHiddenUiHideMapMarkers = hideMapMarkers;
            lastHiddenUiVehicleMode = vehicleDebug.IsVehicleMode;

            var targets = ResolveHiddenUiTargets(cityMapOpen, hideMapMarkers);
            if (targets.Count == 0)
            {
                nextHiddenUiRefreshTime = Time.unscaledTime + HiddenUiRefreshIntervalSeconds;
                return;
            }

            var states = new List<GameObjectActiveState>(targets.Count);
            foreach (var target in targets)
            {
                if (target == null)
                    continue;

                states.Add(new GameObjectActiveState(target, target.activeSelf));
                target.SetActive(false);
            }

            hiddenUiStates = states.ToArray();
            nextHiddenUiRefreshTime = Time.unscaledTime + HiddenUiRefreshIntervalSeconds;
            if (settings?.EnableCameraToolsDebug == true && settings.EnableHiddenUiDebugLogging)
                context?.Logger.Info($"CameraTools: UI hidden; targets={states.Count}, notificationContainers={states.FindAll(state => state.Target.GetComponentInParent<NotificationsUI>(true) != null).Count}.");
        }

        private void RestoreHiddenUi()
        {
            var restoredCount = hiddenUiStates.Length;
            foreach (var state in hiddenUiStates)
            {
                if (state.Target != null)
                    state.Target.SetActive(state.WasActive);
            }

            hiddenUiStates = Array.Empty<GameObjectActiveState>();
            if (settings?.EnableCameraToolsDebug == true && settings.EnableHiddenUiDebugLogging && restoredCount > 0)
                context?.Logger.Info($"CameraTools: UI restored; targets={restoredCount}.");
        }

        private bool GetEffectiveHideMapMarkersWithUi()
        {
            if (settings != null && settings.HideMapMarkersWithUi)
                return true;

            if (TryReadPersistedHideMapMarkersOption(out var persistedValue))
                return persistedValue;

            return false;
        }

        private bool TryReadPersistedHideMapMarkersOption(out bool value)
        {
            value = false;
            if (context == null)
                return false;

            try
            {
                if (!BigAmbitions.Mods.OptionsService.RegisteredEntries.TryGetValue(context.ModId, out var options) || options?.Options == null)
                    return false;

                foreach (var option in options.Options)
                {
                    if (option == null || string.IsNullOrEmpty(option.Id))
                        continue;

                    var matchesKey = false;
                    foreach (var key in HideMapMarkersOptionKeys)
                    {
                        if (string.Equals(option.Id, key, StringComparison.Ordinal))
                        {
                            matchesKey = true;
                            break;
                        }
                    }

                    if (!matchesKey)
                        continue;

                    if (TryReadBoolOptionMember(option, out value))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryReadBoolOptionMember(object option, out bool value)
        {
            value = false;
            var memberNames = new[]
            {
                "Value",
                "CurrentValue",
                "DefaultValue",
                "IsOn",
                "Enabled",
                "value",
                "currentValue",
                "defaultValue",
                "isOn",
                "enabled"
            };

            foreach (var memberName in memberNames)
            {
                if (TryGetBoolMember(option, memberName, out value))
                    return true;
            }

            return false;
        }

        private static List<GameObject> ResolveHiddenUiTargets(bool cityMapOpen, bool hideMapMarkers)
        {
            var targets = new List<GameObject>();
            var seen = new HashSet<int>();

            if (hideMapMarkers)
                AddKnownMapMarkerRoots(targets, seen);
            foreach (var notificationsUi in Resources.FindObjectsOfTypeAll<NotificationsUI>())
            {
                if (notificationsUi != null && notificationsUi.gameObject.scene.IsValid() &&
                    notificationsUi.container != null)
                    TryAddHiddenUiTarget(targets, seen, notificationsUi.container.gameObject);
            }

            foreach (var rectTransform in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                if (rectTransform == null)
                    continue;

                var gameObject = rectTransform.gameObject;
                if (gameObject == null || gameObject.hideFlags != HideFlags.None || !gameObject.activeInHierarchy)
                    continue;

                var path = GetHierarchyPath(rectTransform).ToLowerInvariant();
                if (hideMapMarkers && IsUnderKnownMapMarkerRootPath(path))
                    continue;

                if (IsSpeedometerHudBackgroundPath(path))
                {
                    TryAddHiddenUiTarget(targets, seen, gameObject);
                    continue;
                }

                if (cityMapOpen && IsCityMapControlPath(path))
                {
                    TryAddHiddenUiTarget(targets, seen, gameObject);
                    TryAddHiddenUiTarget(targets, seen, ResolveFixedHudRoot(rectTransform).gameObject);
                    continue;
                }

                var namedMarker = IsNamedMarkerPath(path) || HasMarkerComponentInHierarchy(rectTransform, cityMapOpen);
                var potentialMarker = hideMapMarkers && IsPotentialMarkerUiTransform(rectTransform, cityMapOpen);
                // Verbose per-candidate logging is intentionally disabled for normal testing/release.
                // It creates many string allocations in large UI hierarchies.
                var aggressiveMapMarkerMatch = cityMapOpen && hideMapMarkers &&
                    (namedMarker ||
                    potentialMarker ||
                    path.IndexOf("map", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("filter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("vehicle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("car", StringComparison.OrdinalIgnoreCase) >= 0);
                if (cityMapOpen && !hideMapMarkers && namedMarker)
                    continue;

                if (aggressiveMapMarkerMatch)
                {
                    TryAddHiddenUiTarget(targets, seen, gameObject);
                    TryAddHiddenUiTarget(targets, seen, ResolveWorldMarkerRoot(rectTransform).gameObject);
                    continue;
                }

                if (!ShouldHideUiTransform(rectTransform, namedMarker, cityMapOpen, hideMapMarkers))
                    continue;

                var compactMarker =
                    hideMapMarkers && (potentialMarker ||
                    (!cityMapOpen && IsLikelyWorldMarker(rectTransform)) ||
                    (cityMapOpen && IsLikelyMapMarker(rectTransform)));
                if (compactMarker || namedMarker || aggressiveMapMarkerMatch)
                {
                    TryAddHiddenUiTarget(targets, seen, ResolveWorldMarkerRoot(rectTransform).gameObject);
                    continue;
                }

                if (IsLikelyFixedHudRegion(rectTransform))
                {
                    TryAddHiddenUiTarget(targets, seen, gameObject);
                    TryAddHiddenUiTarget(targets, seen, ResolveFixedHudRoot(rectTransform).gameObject);
                    continue;
                }

                TryAddHiddenUiTarget(targets, seen, gameObject);
            }

            return FilterNestedUiTargets(targets);
        }

        private static bool IsSpeedometerHudBackgroundPath(string lowerPath)
        {
            return lowerPath.IndexOf("speedometer_analogstripbg_", StringComparison.Ordinal) >= 0;
        }

        private static void AddKnownMapMarkerRoots(List<GameObject> targets, HashSet<int> seen)
        {
            if (cachedKnownMapMarkerRoot != null && cachedKnownMapMarkerRoot.activeInHierarchy)
            {
                TryAddHiddenUiTarget(targets, seen, cachedKnownMapMarkerRoot);
                return;
            }

            cachedKnownMapMarkerRoot = null;
            foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null || transform.gameObject == null)
                    continue;

                var gameObject = transform.gameObject;
                if (gameObject.hideFlags != HideFlags.None || !gameObject.activeInHierarchy)
                    continue;

                if (!IsKnownMapMarkerRoot(transform))
                    continue;

                cachedKnownMapMarkerRoot = gameObject;
                TryAddHiddenUiTarget(targets, seen, gameObject);
                return;
            }
        }

        private static bool IsUnderKnownMapMarkerRootPath(string lowerPath)
        {
            return lowerPath.IndexOf("citymanager/citymap/pois/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lowerPath.EndsWith("citymanager/citymap/pois", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCityMapControlPath(string lowerPath)
        {
            if (lowerPath.IndexOf("citymanager/citymap/pois/", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return lowerPath.IndexOf("filter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lowerPath.IndexOf("mapfilter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lowerPath.IndexOf("category", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lowerPath.IndexOf("categories", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lowerPath.IndexOf("legend", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lowerPath.IndexOf("citymapmenu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lowerPath.IndexOf("citymap/menu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lowerPath.IndexOf("mapmenu", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsKnownMapMarkerRoot(Transform transform)
        {
            var name = transform.name;
            if (!IsKnownMapMarkerRootName(name))
                return false;

            var current = transform.parent;
            var climbCount = 0;
            while (current != null && climbCount <= 4)
            {
                if (string.Equals(current.name, "CityMap", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(current.name, "CityManager", StringComparison.OrdinalIgnoreCase))
                    return true;

                current = current.parent;
                climbCount++;
            }

            return false;
        }

        private static bool IsKnownMapMarkerRootName(string name)
        {
            return string.Equals(name, "Pois", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "POIs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Poi", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Markers", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "MapMarkers", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Waypoints", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "MapIcons", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Icons", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryAddHiddenUiTarget(List<GameObject> targets, HashSet<int> seen, GameObject? target)
        {
            if (target == null)
                return;

            var id = target.GetInstanceID();
            if (!seen.Add(id))
                return;

            targets.Add(target);
        }

        private static bool ShouldHideUiTransform(RectTransform transform, bool namedMarker, bool cityMapOpen, bool hideMapMarkers)
        {
            if (transform.GetComponentInParent<Canvas>(true) == null)
                return false;

            var path = GetHierarchyPath(transform).ToLowerInvariant();
            if (path.IndexOf("bizphone", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (ContainsAny(path, HiddenUiExcludeKeywords))
                return false;

            if (namedMarker && !hideMapMarkers)
                return false;

            if (ContainsAny(path, HiddenUiIncludeKeywords) || IsLikelyFixedHudRegion(transform))
                return true;

            if (!hideMapMarkers)
                return false;

            return namedMarker ||
                IsPotentialMarkerUiTransform(transform, cityMapOpen) ||
                (!cityMapOpen && IsLikelyWorldMarker(transform)) ||
                (cityMapOpen && IsLikelyMapMarker(transform));
        }

        private static bool IsPotentialMarkerUiTransform(RectTransform rectTransform, bool cityMapOpen)
        {
            if (!TryGetScreenRect(rectTransform, out var minX, out var minY, out var maxX, out var maxY))
                return false;

            var path = GetHierarchyPath(rectTransform).ToLowerInvariant();
            if (ContainsAny(path, HiddenUiExcludeKeywords))
                return false;

            if (!HasGraphicInMarkerHierarchy(rectTransform) && !HasMarkerComponentInHierarchy(rectTransform, cityMapOpen))
                return false;

            var width = maxX - minX;
            var height = maxY - minY;
            if (width <= 1f || height <= 1f)
                return false;

            if (!cityMapOpen)
                return width <= Screen.width * 0.34f && height <= Screen.height * 0.34f;

            if (IsLikelyFixedHudRegion(rectTransform))
                return false;

            if (width > Screen.width * 0.42f || height > Screen.height * 0.32f)
                return false;

            var aspectRatio = width > height ? width / height : height / width;
            return aspectRatio <= 10f;
        }

        private static bool HasMarkerComponentInHierarchy(Transform transform, bool cityMapOpen)
        {
            var current = transform;
            var climbCount = 0;
            while (current != null && climbCount <= 5)
            {
                foreach (var component in current.GetComponents<Component>())
                {
                    if (component == null)
                        continue;

                    var typeName = component.GetType().Name.ToLowerInvariant();
                    if (ContainsAny(typeName, HiddenComponentMarkerKeywords))
                        return true;

                    if (cityMapOpen && ContainsAny(typeName, HiddenCityMapComponentMarkerKeywords))
                        return true;
                }

                current = current.parent;
                climbCount++;
            }

            return false;
        }

        private static bool IsNamedMarkerPath(string path)
        {
            return ContainsAny(path, HiddenUiMarkerKeywords);
        }

        private static bool IsLikelyFixedHudRegion(RectTransform rectTransform)
        {
            if (!TryGetScreenRect(rectTransform, out var minX, out var minY, out var maxX, out var maxY))
                return false;

            var width = maxX - minX;
            var height = maxY - minY;
            if (width > Screen.width * 0.85f || height > Screen.height * 0.7f)
                return false;

            var centerX = (minX + maxX) * 0.5f;
            var centerY = (minY + maxY) * 0.5f;
            var normalizedX = centerX / Screen.width;
            var normalizedY = centerY / Screen.height;

            var isTopLeftHud = normalizedX <= 0.25f && normalizedY >= 0.7f;
            var isTopCenterHud = normalizedX >= 0.25f && normalizedX <= 0.75f && normalizedY >= 0.75f;
            var isTopRightHud = normalizedX >= 0.75f && normalizedY >= 0.7f;
            var isLeftSideHud = normalizedX <= 0.3f && normalizedY >= 0.25f && normalizedY <= 0.7f;
            var isBottomRightHud = normalizedX >= 0.55f && normalizedY <= 0.42f;
            var isUpperMiddleSupportPanel = normalizedX >= 0.2f && normalizedX <= 0.8f && normalizedY >= 0.5f && normalizedY <= 0.78f;
            var isVehicleActionPanel = normalizedX >= 0.2f && normalizedX <= 0.8f && normalizedY >= 0.68f && normalizedY <= 0.9f;

            return isTopLeftHud || isTopCenterHud || isTopRightHud || isLeftSideHud || isBottomRightHud || isUpperMiddleSupportPanel || isVehicleActionPanel;
        }

        private static bool IsLikelyWorldMarker(RectTransform rectTransform)
        {
            if (!TryGetScreenRect(rectTransform, out var minX, out var minY, out var maxX, out var maxY))
                return false;

            var width = maxX - minX;
            var height = maxY - minY;
            if (width > Screen.width * 0.18f || height > Screen.height * 0.18f)
                return false;

            var hasUiGraphic = HasGraphicInMarkerHierarchy(rectTransform);

            return hasUiGraphic;
        }

        private static bool IsLikelyMapMarker(RectTransform rectTransform)
        {
            if (!TryGetScreenRect(rectTransform, out var minX, out var minY, out var maxX, out var maxY))
                return false;

            var path = GetHierarchyPath(rectTransform).ToLowerInvariant();
            if (ContainsAny(path, HiddenUiExcludeKeywords))
                return false;

            var width = maxX - minX;
            var height = maxY - minY;
            if (width <= 1f || height <= 1f)
                return false;

            if (width > Screen.width * 0.22f || height > Screen.height * 0.22f)
                return false;

            var centerX = (minX + maxX) * 0.5f;
            var centerY = (minY + maxY) * 0.5f;
            var normalizedX = centerX / Screen.width;
            var normalizedY = centerY / Screen.height;
            if (normalizedX < 0.05f || normalizedX > 0.95f || normalizedY < 0.05f || normalizedY > 0.95f)
                return false;

            if (IsLikelyFixedHudRegion(rectTransform))
                return false;

            var aspectRatio = width > height ? width / height : height / width;
            if (aspectRatio > 2.5f)
                return false;

            return HasGraphicInMarkerHierarchy(rectTransform);
        }

        private static RectTransform ResolveWorldMarkerRoot(RectTransform rectTransform)
        {
            var best = rectTransform;
            var current = rectTransform;
            var climbCount = 0;
            while (current.parent is RectTransform parentRect &&
                parentRect.GetComponentInParent<Canvas>(true) != null &&
                !ContainsAny(GetHierarchyPath(parentRect).ToLowerInvariant(), HiddenUiExcludeKeywords) &&
                TryGetScreenRect(parentRect, out var minX, out var minY, out var maxX, out var maxY))
            {
                var width = maxX - minX;
                var height = maxY - minY;
                if (width > Screen.width * 0.18f || height > Screen.height * 0.18f)
                    break;

                best = parentRect;
                current = parentRect;
                climbCount++;
                if (climbCount >= 3)
                    break;
            }

            return best;
        }

        private static RectTransform ResolveFixedHudRoot(RectTransform rectTransform)
        {
            var best = rectTransform;
            var current = rectTransform;
            var climbCount = 0;
            while (current.parent is RectTransform parentRect &&
                parentRect.GetComponentInParent<Canvas>(true) != null &&
                !ContainsAny(GetHierarchyPath(parentRect).ToLowerInvariant(), HiddenUiExcludeKeywords) &&
                TryGetScreenRect(parentRect, out var minX, out var minY, out var maxX, out var maxY))
            {
                var width = maxX - minX;
                var height = maxY - minY;
                if (width > Screen.width * 0.9f || height > Screen.height * 0.45f)
                    break;

                best = parentRect;
                current = parentRect;
                climbCount++;
                if (climbCount >= 4)
                    break;
            }

            return best;
        }

        private static bool HasGraphicInMarkerHierarchy(RectTransform rectTransform)
        {
            return HasGraphicInMarkerHierarchy(rectTransform, depth: 0, visited: 0);
        }

        private static bool HasGraphicInMarkerHierarchy(RectTransform rectTransform, int depth, int visited)
        {
            if (depth > 5 || visited > 64)
                return false;

            if (rectTransform.GetComponent("Image") != null ||
                rectTransform.GetComponent("RawImage") != null ||
                rectTransform.GetComponent("TMP_Text") != null ||
                rectTransform.GetComponent("TextMeshProUGUI") != null ||
                rectTransform.GetComponent("CanvasRenderer") != null)
                return true;

            var nextVisited = visited;
            foreach (Transform child in rectTransform)
            {
                if (child is not RectTransform childRect)
                    continue;

                nextVisited++;
                if (nextVisited > 64)
                    return false;

                if (!TryGetScreenRect(childRect, out _, out _, out var childMaxX, out var childMaxY))
                    continue;

                if (childMaxX <= 0f && childMaxY <= 0f)
                    continue;

                if (HasGraphicInMarkerHierarchy(childRect, depth + 1, nextVisited))
                    return true;
            }

            return false;
        }

        private static bool TryGetScreenRect(RectTransform rectTransform, out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = 0f;
            minY = 0f;
            maxX = 0f;
            maxY = 0f;
            if (Screen.width <= 0 || Screen.height <= 0)
                return false;

            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            minX = corners[0].x;
            minY = corners[0].y;
            maxX = corners[2].x;
            maxY = corners[2].y;
            return maxX - minX > 1f && maxY - minY > 1f;
        }

        private static List<GameObject> FilterNestedUiTargets(List<GameObject> targets)
        {
            var filtered = new List<GameObject>(targets.Count);
            for (var i = 0; i < targets.Count; i++)
            {
                var candidate = targets[i];
                if (candidate == null)
                    continue;

                var isChildOfSelectedTarget = false;
                for (var j = 0; j < targets.Count; j++)
                {
                    if (i == j)
                        continue;

                    var other = targets[j];
                    if (other == null)
                        continue;

                    if (candidate.transform.IsChildOf(other.transform))
                    {
                        isChildOfSelectedTarget = true;
                        break;
                    }
                }

                if (!isChildOfSelectedTarget)
                    filtered.Add(candidate);
            }

            return filtered;
        }

        private static bool ContainsAny(string source, string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                if (source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static readonly string[] HiddenComponentMarkerKeywords =
        {
            "marker",
            "mapmarker",
            "icon",
            "pin",
            "waypoint",
            "blip",
            "poi",
            "locationmarker",
            "buildingmarker",
            "vehiclemarker",
            "carmarker",
            "citymapmarker",
            "minimapmarker",
            "indicator",
            "overlay",
            "floating"
        };

        private static readonly string[] HiddenCityMapComponentMarkerKeywords =
        {
            "citymap",
            "mapicon",
            "maplabel",
            "mapbutton",
            "businessicon",
            "businesslabel",
            "buildingicon",
            "buildinglabel",
            "vehicleicon",
            "vehiclelabel",
            "locationicon",
            "locationlabel"
        };

        private bool IsGameplayInputBlockedByUi(bool forceRefresh = false)
        {
            if (IsOptionsMenuOpen())
                return isGameplayUiBlocked = true;

            if (!forceRefresh && Time.unscaledTime < nextUiStateRefreshTime)
                return isGameplayUiBlocked;

            nextUiStateRefreshTime = Time.unscaledTime + UiStateRefreshIntervalSeconds;
            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                if (eventSystem.IsPointerOverGameObject())
                    return isGameplayUiBlocked = true;

                var selectedGameObject = eventSystem.currentSelectedGameObject;
                if (selectedGameObject != null && selectedGameObject.activeInHierarchy)
                    return isGameplayUiBlocked = true;
            }

            if (IsStaticUiOpen(miniMenuType, "IsOpen"))
                return isGameplayUiBlocked = true;

            if (IsStaticUiOpen(fullMenuType, "IsOpen"))
                return isGameplayUiBlocked = true;

            if (IsDialogPanelOpen())
                return isGameplayUiBlocked = true;

            if (IsStaticUiOpen(placementSystemType, "IsInPlacementMode"))
                return isGameplayUiBlocked = true;

            if (IsStaticUiOpen(interiorDesignerUiType, "IsOpen"))
                return isGameplayUiBlocked = true;

            isGameplayUiBlocked = false;
            return false;
        }

        private bool IsUpdateNoticeSuppressedByNativeUi(bool cityMapOpen)
        {
            return cityMapOpen ||
                IsOptionsMenuOpen() ||
                IsStaticUiOpen(miniMenuType, "IsOpen") ||
                IsStaticUiOpen(fullMenuType, "IsOpen") ||
                IsDialogPanelOpen() ||
                IsStaticUiOpen(placementSystemType, "IsInPlacementMode") ||
                IsStaticUiOpen(interiorDesignerUiType, "IsOpen");
        }

        private bool IsOptionsMenuOpen()
        {
            // The active Options object is authoritative even if its static visibility
            // flag has not yet been updated by the menu that opened it.
            var options = InstanceBehavior<UIs>.Instance?.options;
            return (options != null && options.gameObject.activeInHierarchy) ||
                IsStaticUiOpen(optionsUiType, "IsVisible");
        }

        private static bool IsStaticUiOpen(Type? type, string propertyName)
        {
            if (type == null)
                return false;

            try
            {
                var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return property?.GetValue(null, null) is bool isOpen && isOpen;
            }
            catch
            {
                return false;
            }
        }

        private bool IsDialogPanelOpen()
        {
            if (dialogUiType == null)
                return false;

            if (cachedDialogUiController == null || !cachedDialogUiController.isActiveAndEnabled)
                cachedDialogUiController = FindFirstActiveController(dialogUiType, includeInactive: false);

            return cachedDialogUiController != null &&
                TryGetBoolMember(cachedDialogUiController, "isPanelOpen", out var isPanelOpen) &&
                isPanelOpen;
        }

        private static void ShowPopup(string message, string? duplicateIdentifier = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                Notifications.Show(
                    NotificationType.Info,
                    message,
                    null,
                    6f,
                    duplicateIdentifier,
                    null,
                    false,
                    false);
            }
            catch (Exception exception)
            {
                LogVehicleDebug("Failed to show popup: " + exception.Message);
            }
        }

    }
}
