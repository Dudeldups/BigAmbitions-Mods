#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime
    {
        private readonly List<CityFogState> cityFogStates = new List<CityFogState>();
        private readonly HashSet<int> trackedCityFogIds = new HashSet<int>();
        private bool cityFogSuppressionApplied;
        private bool cityFogProfileScanPending = true;

        private void UpdateCityFogSuppression()
        {
            if (settings == null || !settings.DisableCityFog)
            {
                RestoreCityFogState();
                return;
            }

            if (IsPlayerInFogPreservingInterior())
            {
                RestoreCityFogEnabledValues(clearTrackedProfiles: false);
                return;
            }

            if (!cityFogSuppressionApplied || cityFogProfileScanPending)
            {
                CaptureNewCityFogProfiles();
                cityFogProfileScanPending = false;
            }

            foreach (var state in cityFogStates)
                state.DisableFog();

            if (cityFogSuppressionApplied)
                return;

            cityFogSuppressionApplied = true;
            context?.Logger.Info($"CameraTools: outdoor city fog disabled; trackedProfiles={cityFogStates.Count}.");
        }

        private void CaptureNewCityFogProfiles()
        {
            var addedProfiles = 0;
            foreach (var volume in VolumeManager.instance.GetVolumes(-1))
            {
                if (volume == null)
                    continue;

                // Reading Volume.profile can clone a shared profile. Repeated global resource
                // scans therefore created work and allocations every two seconds. VolumeManager
                // gives us only active registered volumes, and sharedProfile avoids that clone.
                var profile = volume.sharedProfile ?? volume.profile;
                if (profile == null || !profile.TryGet<Fog>(out var fog) || fog == null)
                    continue;

                var fogId = fog.GetInstanceID();
                if (!trackedCityFogIds.Add(fogId))
                    continue;

                cityFogStates.Add(new CityFogState(fog));
                addedProfiles++;
            }

            context?.Logger.Info(
                $"CameraTools: city fog profile scan completed after activation or scene load; addedProfiles={addedProfiles}, trackedProfiles={cityFogStates.Count}.");
        }

        private void HandleCityFogSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            cityFogProfileScanPending = true;
            GlobalEvents.RegisterOnGameLoadedLateCallback(RegisterTimeMachineHooks);
        }

        private static bool IsPlayerInFogPreservingInterior()
        {
            try
            {
                if (BuildingManager.IsInsideBuilding)
                    return true;
            }
            catch
            {
            }

            try
            {
                return Parking.UndergroundParking.UndergroundParkingManager.IsInsideParking;
            }
            catch
            {
                return false;
            }
        }

        private void RestoreCityFogState()
        {
            RestoreCityFogEnabledValues(clearTrackedProfiles: true);
            cityFogProfileScanPending = true;
        }

        private void RestoreCityFogEnabledValues(bool clearTrackedProfiles)
        {
            if (cityFogSuppressionApplied)
            {
                foreach (var state in cityFogStates)
                    state.RestoreFog();
            }

            cityFogSuppressionApplied = false;
            if (!clearTrackedProfiles)
                return;

            cityFogStates.Clear();
            trackedCityFogIds.Clear();
        }

        private sealed class CityFogState
        {
            private readonly Fog fog;
            private readonly bool enabledValue;
            private readonly bool enabledOverrideState;

            public CityFogState(Fog fog)
            {
                this.fog = fog;
                enabledValue = fog.enabled.value;
                enabledOverrideState = fog.enabled.overrideState;
            }

            public void DisableFog()
            {
                if (fog == null)
                    return;

                if (!fog.enabled.overrideState)
                    fog.enabled.overrideState = true;
                if (fog.enabled.value)
                    fog.enabled.value = false;
            }

            public void RestoreFog()
            {
                if (fog == null)
                    return;

                if (fog.enabled.value != enabledValue)
                    fog.enabled.value = enabledValue;
                if (fog.enabled.overrideState != enabledOverrideState)
                    fog.enabled.overrideState = enabledOverrideState;
            }
        }
    }
}
