#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Tags;
using Controllers;
using Entities;
using Helpers;
using Streets;
using UI.Elements;
using UI.Notification;
using UI.Overlays;
using UnityEngine;

[assembly: RegisterModClass(typeof(GoodbyeIRS.GoodbyeIrsMod))]

namespace GoodbyeIRS
{
    /// <summary>
    /// Lets the player permanently disable IRS tax handling for one save after
    /// bringing a fuel container into the IRS building.
    /// </summary>
    [ModEntryOnCityLoad]
    public sealed class GoodbyeIrsMod : IModBigAmbitions
    {
        private static GoodbyeIrsRuntime? runtime;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            runtime = GoodbyeIrsRuntime.Initialize(context);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            runtime?.Shutdown();
            runtime = null;
            return Task.CompletedTask;
        }
    }

    [DefaultExecutionOrder(-10000)]
    internal sealed class GoodbyeIrsRuntime : MonoBehaviour
    {
        // TaxHelper.RunDaily checks Day % daysPerYear immediately after
        // GlobalEvents.onNewDay. This value cannot be reached in normal play,
        // so it reliably makes that one check false without changing aging.
        private const int TaxCheckBypassDaysPerYear = int.MaxValue;
        private const string ActivatedModDataKey = "goodbye-irs:activated_v1";
        private static readonly Vector3[] FirePositions =
        {
            new(1092.21f, 0.01f, -93.86f),
            new(1095.63f, 0.01f, -94.14f),
            new(1098.94f, 0.01f, -95.62f),
            new(1105.92f, 0.01f, -93.96f),
            new(1093.50f, 0.01f, -95.80f),
            new(1096.90f, 0.01f, -93.65f),
            new(1101.35f, 0.01f, -94.05f),
            new(1103.55f, 0.01f, -95.45f)
        };
        private static readonly FireStyle[] FireStyles =
        {
            new(0.65f, 0.65f, 0.70f, 0.60f), // small and sparse
            new(1.00f, 1.00f, 1.00f, 1.00f), // standard
            new(1.38f, 1.60f, 1.55f, 1.45f), // tall and dense
            new(0.90f, 2.20f, 0.50f, 2.00f)  // low and wide
        };
        private static Material? fallbackParticleMaterial;
        private static bool fallbackParticleMaterialSearchComplete;

        private static GoodbyeIrsRuntime? instance;

        private ModContext? context;
        private bool activationPromptVisible;
        private bool restoreCalendarPending;
        private int daysPerYearBeforeTaxCheck;
        private IrsActivationOverlay? activationOverlay;
        private GameObject? fireSceneryRoot;
        private bool nativeFireFailureLogged;

        internal static GoodbyeIrsRuntime Initialize(ModContext context)
        {
            var runtime = FindObjectOfType<GoodbyeIrsRuntime>();
            if (runtime == null)
            {
                var runtimeObject = new GameObject(nameof(GoodbyeIrsRuntime));
                DontDestroyOnLoad(runtimeObject);
                runtime = runtimeObject.AddComponent<GoodbyeIrsRuntime>();
            }

            runtime.context = context;
            instance = runtime;
            runtime.SubscribeToIrsEntry();

            if (IsActivatedForCurrentSave())
            {
                runtime.CleanUpTaxLiabilities();
                runtime.SubscribeToNewDay();
                runtime.CloseIrsBuilding();
                runtime.CreateFireScenery();
                runtime.StartCoroutine(runtime.RemoveIrsStaffAfterOneFrame());
                context.Logger.Info("Goodbye IRS is already active for this save.");
            }
            else
            {
                context.Logger.Info("Goodbye IRS is waiting for the player to bring a fuel container to the IRS.");
            }

            return runtime;
        }

        internal void Shutdown()
        {
            RestoreCalendarIfNeeded();
            if (fireSceneryRoot != null)
                Destroy(fireSceneryRoot);

            if (instance == this)
                instance = null;

            Destroy(gameObject);
        }

        private void OnDisable()
        {
            GlobalEvents.onNewDay -= HandleNewDay;
            GlobalEvents.onEnterBuilding -= HandleEnterBuilding;
            GlobalEvents.onExitBuilding -= HandleExitBuilding;
            HideActivationOverlay();
            RestoreCalendarIfNeeded();
        }

        private void SubscribeToNewDay()
        {
            GlobalEvents.onNewDay -= HandleNewDay;
            GlobalEvents.onNewDay += HandleNewDay;
        }

        private void SubscribeToIrsEntry()
        {
            GlobalEvents.onEnterBuilding -= HandleEnterBuilding;
            GlobalEvents.onEnterBuilding += HandleEnterBuilding;
            GlobalEvents.onExitBuilding -= HandleExitBuilding;
            GlobalEvents.onExitBuilding += HandleExitBuilding;
        }

        private void HandleEnterBuilding(Address address)
        {
            if (!IsIrs(address))
                return;

            if (activationPromptVisible || IsActivatedForCurrentSave())
                return;

            var heldItem = PlayerHelper.ItemInstanceInHands;
            if (heldItem?.ItemCached == null || !heldItem.ItemCached.HasTag(TagRef.Itemtag.isfuelcontainer))
                return;

            activationPromptVisible = true;
            activationOverlay = new IrsActivationOverlay(this);
            OverlayUI.Show(activationOverlay);
        }

        private void HandleExitBuilding(Address address)
        {
            if (!IsIrs(address))
                return;

            HideActivationOverlay();
        }

        private static bool IsIrs(Address address)
        {
            return address == TaxHelper.GetIRSAddress();
        }

        private void CreateFireScenery()
        {
            if (fireSceneryRoot != null)
                return;

            fireSceneryRoot = new GameObject("[Goodbye IRS] Fire Scenery");
            fireSceneryRoot.transform.SetParent(transform, false);

            for (var index = 0; index < FirePositions.Length; index++)
                CreateFireSpot(FirePositions[index], FireStyles[index % FireStyles.Length]);

            context?.Logger.Info($"Placed {FirePositions.Length} varied cosmetic IRS fire effects.");
        }

        private void CreateFireSpot(Vector3 position, FireStyle style)
        {
            var fireSpot = new GameObject("IRS Fire");
            fireSpot.transform.SetParent(fireSceneryRoot!.transform, false);
            fireSpot.transform.position = position;

            // The game ships VFX_Fire. It lives in the Visual Effect Graph
            // runtime, which is optional in the mod SDK, so access it through
            // reflection instead of adding a hard assembly dependency.
            if (!TryAttachNativeFireEffect(fireSpot))
                CreateFallbackFlameParticles(fireSpot, style);

            var light = fireSpot.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.28f, 0.04f);
            light.intensity = 2.5f * style.scale;
            light.range = 2.8f + 1.2f * style.scale;
            light.shadows = LightShadows.None;
        }

        private IEnumerator RemoveIrsStaffAfterOneFrame()
        {
            yield return null;
            RemoveIrsStaff();
        }

        private void RemoveIrsStaff()
        {
            var removedCount = 0;
            foreach (var station in FindObjectsOfType<IRSStationController>())
            {
                if (!(station.employee is IRSEmployee))
                    continue;

                // UnassignEmployee destroys the employee's character object,
                // including one temporarily walking away from its counter.
                station.UnassignEmployee();
                removedCount++;
            }

            if (removedCount > 0)
                context?.Logger.Info($"Removed {removedCount} IRS employee(s) after activation.");
        }

        private void CloseIrsBuilding()
        {
            var registration = BuildingHelper.GetBuildingRegistration(TaxHelper.GetIRSAddress());
            if (registration == null || registration.temporarilyClosed)
                return;

            // Special-service buildings do not use a player-editable schedule.
            // This is the game's own persistent closed state, which prevents
            // the player from entering and keeps its stations inactive.
            registration.temporarilyClosed = true;
            GameEvent.Invoke(string.Empty);
            context?.Logger.Info("Closed the IRS building permanently for this save.");
        }

        private bool TryAttachNativeFireEffect(GameObject fireSpot)
        {
            try
            {
                var assetType = Type.GetType("UnityEngine.VFX.VisualEffectAsset, Unity.VisualEffectGraph.Runtime");
                var effectType = Type.GetType("UnityEngine.VFX.VisualEffect, Unity.VisualEffectGraph.Runtime");
                if (assetType == null || effectType == null)
                {
                    LogNativeFireUnavailable("Visual Effect Graph runtime was not loaded.");
                    return false;
                }

                UnityEngine.Object? fireAsset = null;
                foreach (var asset in Resources.FindObjectsOfTypeAll(assetType))
                {
                    if (asset.name == "VFX_Fire")
                    {
                        fireAsset = asset;
                        break;
                    }
                }

                if (fireAsset == null)
                {
                    LogNativeFireUnavailable("VFX_Fire is not currently loaded by the game.");
                    return false;
                }

                var visualEffect = fireSpot.AddComponent(effectType);
                var assetProperty = effectType.GetProperty("visualEffectAsset", BindingFlags.Public | BindingFlags.Instance);
                if (assetProperty == null)
                {
                    Destroy(visualEffect);
                    LogNativeFireUnavailable("VisualEffect.visualEffectAsset was unavailable.");
                    return false;
                }

                assetProperty.SetValue(visualEffect, fireAsset);
                return true;
            }
            catch (Exception exception)
            {
                LogNativeFireUnavailable(exception.Message);
                return false;
            }
        }

        private void CreateFallbackFlameParticles(GameObject fireSpot, FireStyle style)
        {
            // A built-in particle system is deliberately used as the fallback:
            // it has no asset-bundle dependency and simulates internally rather
            // than requiring a per-frame mod callback.
            var particleMaterial = GetCompatibleParticleMaterial();
            if (particleMaterial == null)
            {
                context?.Logger.Warn("No compatible HDRP particle material was loaded; IRS flames are disabled to avoid missing-shader boxes.");
                return;
            }

            CreateFlameLayer(fireSpot, "Outer flame", new Color(1f, 0.12f, 0f), 1.25f, 18f, 0.9f, particleMaterial, style);
            CreateFlameLayer(fireSpot, "Inner flame", new Color(1f, 0.72f, 0.02f), 0.72f, 24f, 0.72f, particleMaterial, style);
        }

        private static Material? GetCompatibleParticleMaterial()
        {
            if (fallbackParticleMaterialSearchComplete)
                return fallbackParticleMaterial;

            fallbackParticleMaterialSearchComplete = true;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (candidate.name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) < 0 ||
                    candidate.shader == null || !candidate.shader.isSupported)
                    continue;

                fallbackParticleMaterial = new Material(candidate);
                return fallbackParticleMaterial;
            }

            return null;
        }

        private static void CreateFlameLayer(
            GameObject fireSpot,
            string layerName,
            Color color,
            float size,
            float emissionRate,
            float lifetime,
            Material particleMaterial,
            FireStyle style)
        {
            var layer = new GameObject(layerName);
            layer.transform.SetParent(fireSpot.transform, false);
            layer.transform.localPosition = Vector3.up * 0.15f;

            var particles = layer.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 48;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime * 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f * style.rise, 0.75f * style.rise);
            main.startSize = new ParticleSystem.MinMaxCurve(size * style.scale * 0.55f, size * style.scale);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = particles.emission;
            emission.rateOverTime = emissionRate * style.emission;

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = 0.28f * style.spread;
            shape.angle = 12f;

            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.y = new ParticleSystem.MinMaxCurve(0.45f * style.rise, 1.2f * style.rise);

            var sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.45f),
                    new Keyframe(0.25f, 1f),
                    new Keyframe(1f, 0.08f)));

            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(new Color(1f, 0.04f, 0f), 0.65f),
                    new GradientColorKey(new Color(0.18f, 0.01f, 0f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.95f, 0.12f),
                    new GradientAlphaKey(0.55f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.minParticleSize = 0f;
            renderer.maxParticleSize = 3f;
            renderer.sharedMaterial = particleMaterial;
        }

        private void LogNativeFireUnavailable(string reason)
        {
            if (nativeFireFailureLogged)
                return;

            nativeFireFailureLogged = true;
            context?.Logger.Warn($"Native IRS fire VFX could not be attached: {reason}");
        }

        private void ActivateFromOverlay()
        {
            HideActivationOverlay();

            var save = SaveGameManager.Current;
            if (save == null || !ConsumeHeldFuelContainer())
                return;

            save.modData ??= new Dictionary<string, string>();
            save.modData[ActivatedModDataKey] = "1";
            CleanUpTaxLiabilities();
            SubscribeToNewDay();
            CloseIrsBuilding();
            CreateFireScenery();
            RemoveIrsStaff();
            GameEvent.Invoke(string.Empty);
            ShowActivationConfirmation();
            context?.Logger.Info("The IRS was disabled for the current save.");
        }

        private void HideActivationOverlay()
        {
            activationPromptVisible = false;
            if (activationOverlay != null)
                OverlayUI.Hide(activationOverlay);

            activationOverlay = null;
        }

        private static bool ConsumeHeldFuelContainer()
        {
            var heldItem = PlayerHelper.ItemInstanceInHands;
            if (heldItem?.ItemCached == null || !heldItem.ItemCached.HasTag(TagRef.Itemtag.isfuelcontainer))
                return false;

            // Assigning null invokes PlayerHelper.RemoveItemsFromHands(), which
            // cleanly detaches the item from the player and HUD. Do not invoke
            // OnItemInHandsCargoUpdated afterwards: that routine expects a held
            // item and dereferences it while checking container tags.
            PlayerHelper.ItemInstanceInHands = null;
            return true;
        }

        private static void ShowActivationConfirmation()
        {
            try
            {
                Notifications.Show(
                    NotificationType.Success,
                    "goodbye-irs:activation_success",
                    null,
                    7f,
                    "GoodbyeIRSActivated",
                    null,
                    true,
                    false);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Goodbye IRS could not show its activation confirmation: {exception.Message}");
            }
        }

        private static bool IsActivatedForCurrentSave()
        {
            var save = SaveGameManager.Current;
            return save?.modData != null &&
                   save.modData.TryGetValue(ActivatedModDataKey, out var value) &&
                   value == "1";
        }

        private struct FireStyle
        {
            public readonly float scale;
            public readonly float emission;
            public readonly float rise;
            public readonly float spread;

            public FireStyle(float scale, float emission, float rise, float spread)
            {
                this.scale = scale;
                this.emission = emission;
                this.rise = rise;
                this.spread = spread;
            }
        }

        private sealed class IrsActivationOverlay : IOverlay
        {
            private readonly GoodbyeIrsRuntime runtime;

            public IrsActivationOverlay(GoodbyeIrsRuntime runtime)
            {
                this.runtime = runtime;
            }

            public Vector3 GetTargetPosition()
            {
                var player = InstanceBehavior<GameManager>.Instance?.playerController;
                return player != null ? player.transform.position + Vector3.up * 1.8f : Vector3.zero;
            }

            public LabelInfo GetFirstLineLabel() => new("goodbye-irs:activation_overlay_description");

            public LabelInfo GetSecondLineLeftLabel() => null!;

            public LabelInfo GetSecondLineRightLabel() => null!;

            public ButtonInfo[] GetButtons()
            {
                return new[]
                {
                    new ButtonInfo(
                        "GoodbyeIRSActivate",
                        "goodbye-irs:activation_confirm",
                        "red",
                        runtime.ActivateFromOverlay,
                        PlayerAction.Interact)
                };
            }
        }

        private void HandleNewDay()
        {
            if (!IsActivatedForCurrentSave())
                return;

            // The event is invoked directly before TaxHelper.RunDaily. Remove
            // old debt first, then make its annual modulo check fail for this
            // game tick. PlayerHelper.IncreasePlayerAge already ran by here.
            CleanUpTaxLiabilities();

            var save = SaveGameManager.Current;
            if (save?.gameVariables == null || restoreCalendarPending)
                return;

            daysPerYearBeforeTaxCheck = save.gameVariables.daysPerYear;
            save.gameVariables.daysPerYear = TaxCheckBypassDaysPerYear;
            restoreCalendarPending = true;

            if (daysPerYearBeforeTaxCheck > 0 && save.Day % daysPerYearBeforeTaxCheck == 0)
            {
                context?.Logger.Info(
                    $"Skipped IRS assessment on day {save.Day}; restored year length will remain {daysPerYearBeforeTaxCheck}.");
            }

            StartCoroutine(RestoreCalendarAfterTaxCheck());
        }

        private IEnumerator RestoreCalendarAfterTaxCheck()
        {
            // TaxHelper.RunDaily and the rest of GameManager.NewDay execute
            // synchronously before the next frame.
            yield return null;
            RestoreCalendarIfNeeded();
        }

        private void RestoreCalendarIfNeeded()
        {
            if (!restoreCalendarPending)
                return;

            var save = SaveGameManager.Current;
            if (save?.gameVariables != null)
                save.gameVariables.daysPerYear = daysPerYearBeforeTaxCheck;

            restoreCalendarPending = false;
        }

        private void CleanUpTaxLiabilities()
        {
            var save = SaveGameManager.Current;
            if (save == null)
                return;

            var changed = false;
            if (save.currentUnpaidTaxes != null)
            {
                save.currentUnpaidTaxes = null;
                changed = true;
            }

            if (save.currentBackTaxes > 0f)
            {
                save.currentBackTaxes = 0f;
                changed = true;
            }

            if (save.TodoTasks != null)
            {
                for (var index = save.TodoTasks.Count - 1; index >= 0; index--)
                {
                    if (save.TodoTasks[index].type != TodoTaskType.PayTaxes)
                        continue;

                    save.TodoTasks.RemoveAt(index);
                    changed = true;
                }
            }

            if (!changed)
                return;

            GameEvent.Invoke(string.Empty);
            context?.Logger.Info("Cleared outstanding IRS debt and tax tasks.");
        }
    }
}
