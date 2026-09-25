#nullable enable
using BAModAPI;
using BigAmbitions.Mods;

namespace CameraTools
{
    public sealed class CameraToolsOptions
    {
        private const string GameplayZoomKey = "camera_tools_gameplay_max_zoom";
        private const string VehicleZoomKey = "camera_tools_vehicle_max_zoom";
        private const string VehicleCameraHeightKey = "camera_tools_vehicle_camera_height_cm";
        private const string MapDistanceKey = "camera_tools_map_distance";
        private const string ScenicViewHotkeyKey = "camera_tools_scenic_view_hotkey";
        private const string HideUiHotkeyKey = "camera_tools_hide_ui_hotkey";
        private const string HideMapMarkersKey = "camera_tools_hide_map_markers";
        private const string FollowPlayerKey = "camera_tools_follow_player_on_foot";
        private const string FirstPersonKey = "camera_tools_first_person_zoom";
        private const string ShowModeHintsKey = "camera_tools_show_control_hints";
        private const string FreeCameraHotkeyKey = "camera_tools_free_camera_hotkey";
        private const string FirstPersonSensitivityKey = "camera_tools_first_person_mouse_sensitivity";
        private const string FreeCameraSensitivityKey = "camera_tools_free_camera_mouse_sensitivity";
        // Preserve the original preference key so existing enabled settings migrate automatically.
        private const string DisableCityFogKey = "camera_tools_disable_city_map_fog";
        private static readonly string[] HotkeyChoices =
        {
            "cameratools_hotkey_f4",
            "cameratools_hotkey_f6",
            "cameratools_hotkey_f7",
            "cameratools_hotkey_f8",
            "cameratools_hotkey_f9",
            "cameratools_hotkey_f10",
            "cameratools_hotkey_home",
            "cameratools_hotkey_insert",
            "cameratools_hotkey_delete",
            "cameratools_hotkey_page_up",
            "cameratools_hotkey_page_down",
            "cameratools_hotkey_1",
            "cameratools_hotkey_2",
            "cameratools_hotkey_3",
            "cameratools_hotkey_4",
            "cameratools_hotkey_5",
            "cameratools_hotkey_6",
            "cameratools_hotkey_7",
            "cameratools_hotkey_8",
            "cameratools_hotkey_9",
            "cameratools_hotkey_0"
        };

        private static readonly UnityEngine.KeyCode[] HotkeyValues =
        {
            UnityEngine.KeyCode.F4,
            UnityEngine.KeyCode.F6,
            UnityEngine.KeyCode.F7,
            UnityEngine.KeyCode.F8,
            UnityEngine.KeyCode.F9,
            UnityEngine.KeyCode.F10,
            UnityEngine.KeyCode.Home,
            UnityEngine.KeyCode.Insert,
            UnityEngine.KeyCode.Delete,
            UnityEngine.KeyCode.PageUp,
            UnityEngine.KeyCode.PageDown,
            UnityEngine.KeyCode.Alpha1,
            UnityEngine.KeyCode.Alpha2,
            UnityEngine.KeyCode.Alpha3,
            UnityEngine.KeyCode.Alpha4,
            UnityEngine.KeyCode.Alpha5,
            UnityEngine.KeyCode.Alpha6,
            UnityEngine.KeyCode.Alpha7,
            UnityEngine.KeyCode.Alpha8,
            UnityEngine.KeyCode.Alpha9,
            UnityEngine.KeyCode.Alpha0
        };

        private ModContext? context;
        private string? registeredModId;

        public void Initialize(ModContext modContext, CameraToolsSettings settings)
        {
            context = modContext;
            settings.HideMapMarkersWithUi = LoadSavedHideMapMarkersValue(modContext.ModId, settings.HideMapMarkersWithUi);
            settings.DisableCityFog = LoadSavedDisableCityFogValue(modContext.ModId, settings.DisableCityFog);
            settings.FollowPlayerOnFoot = LoadSavedBool(modContext.ModId, FollowPlayerKey, settings.FollowPlayerOnFoot);
            settings.EnableFirstPersonZoom = LoadSavedBool(modContext.ModId, FirstPersonKey, settings.EnableFirstPersonZoom);
            settings.ShowModeHints = LoadSavedBool(modContext.ModId, ShowModeHintsKey, settings.ShowModeHints);
            settings.FirstPersonMouseSensitivity = LoadSavedSlider(modContext.ModId, FirstPersonSensitivityKey, settings.FirstPersonMouseSensitivity);
            settings.FreeCameraMouseSensitivity = LoadSavedSlider(modContext.ModId, FreeCameraSensitivityKey, settings.FreeCameraMouseSensitivity);
            settings.VehicleCameraHeightCm = UnityEngine.Mathf.Clamp(
                UnityEngine.PlayerPrefs.GetInt(modContext.ModId + "." + VehicleCameraHeightKey, settings.VehicleCameraHeightCm), -50, 150);
            settings.FreeCameraHotkey = LoadSavedHotkey(modContext.ModId, FreeCameraHotkeyKey, settings.FreeCameraHotkey);
            if (!string.IsNullOrEmpty(registeredModId))
            {
                LogOptionsDebug(modContext, $"CameraTools: unregistering previous options for modId={registeredModId}.");
                OptionsService.RemoveModOptions(registeredModId);
            }

            LogOptionsDebug(modContext, $"CameraTools: removing stale options for current modId={modContext.ModId} before registration.");
            OptionsService.RemoveModOptions(modContext.ModId);

            try
            {
                LogOptionsDebug(modContext, $"CameraTools: building options for modId={modContext.ModId}.");
                var options =
                    new ModOptions()
                        .AddHeader("cameratools_options_header")
                        .AddDropdown(FreeCameraHotkeyKey, "cameratools_free_camera_hotkey_label", HotkeyChoices,
                            GetHotkeyIndex(settings.FreeCameraHotkey),
                            value =>
                            {
                                settings.FreeCameraHotkey = HotkeyValues[value];
                                SaveInt(modContext.ModId, FreeCameraHotkeyKey, (int)settings.FreeCameraHotkey);
                            })
                        .AddDropdown(ScenicViewHotkeyKey, "cameratools_scenic_view_hotkey_label", HotkeyChoices,
                            GetHotkeyIndex(settings.ScenicViewHotkey),
                            value => settings.ScenicViewHotkey = HotkeyValues[value])
                        .AddDropdown(HideUiHotkeyKey, "cameratools_hide_ui_hotkey_label", HotkeyChoices,
                            GetHotkeyIndex(settings.HideUiHotkey),
                            value => settings.HideUiHotkey = HotkeyValues[value])
                        .AddToggle(HideMapMarkersKey, "cameratools_hide_map_markers_label",
                            settings.HideMapMarkersWithUi,
                            value =>
                            {
                                settings.HideMapMarkersWithUi = value;
                                SaveHideMapMarkersValue(modContext.ModId, value);
                            })
                        .AddSlider(GameplayZoomKey, "cameratools_gameplay_zoom_label", 15, 43, settings.GameplayMaxZoom,
                            value => settings.GameplayMaxZoom = value, "cameratools_slider_value")
                        .AddSlider(MapDistanceKey, "cameratools_map_distance_label", 100, 800, settings.MapDistance,
                            value => settings.MapDistance = value, "cameratools_slider_value")
                        .AddSlider(VehicleZoomKey, "cameratools_vehicle_zoom_label", 20, 120, settings.VehicleMaxZoom,
                            value => settings.VehicleMaxZoom = value, "cameratools_slider_value")
                        .AddSlider(VehicleCameraHeightKey, "cameratools_vehicle_camera_height_label", -50, 150,
                            settings.VehicleCameraHeightCm,
                            value =>
                            {
                                settings.VehicleCameraHeightCm = value;
                                SaveInt(modContext.ModId, VehicleCameraHeightKey, value);
                            }, "cameratools_vehicle_height_value")
                        .AddSlider(FirstPersonSensitivityKey, "cameratools_fp_sensitivity_label", 1, 10, settings.FirstPersonMouseSensitivity,
                            value =>
                            {
                                settings.FirstPersonMouseSensitivity = value;
                                SaveInt(modContext.ModId, FirstPersonSensitivityKey, value);
                            }, "cameratools_slider_value")
                        .AddSlider(FreeCameraSensitivityKey, "cameratools_free_camera_sensitivity_label", 1, 10, settings.FreeCameraMouseSensitivity,
                            value =>
                            {
                                settings.FreeCameraMouseSensitivity = value;
                                SaveInt(modContext.ModId, FreeCameraSensitivityKey, value);
                            }, "cameratools_slider_value")
                        .AddToggle(DisableCityFogKey, "cameratools_disable_city_fog_label",
                            settings.DisableCityFog,
                            value =>
                            {
                                settings.DisableCityFog = value;
                                SaveDisableCityFogValue(modContext.ModId, value);
                            })
                        .AddToggle(FollowPlayerKey, "cameratools_follow_player_label",
                            settings.FollowPlayerOnFoot,
                            value =>
                            {
                                settings.FollowPlayerOnFoot = value;
                                SaveInt(modContext.ModId, FollowPlayerKey, value ? 1 : 0);
                            })
                        .AddToggle(FirstPersonKey, "cameratools_first_person_label",
                            settings.EnableFirstPersonZoom,
                            value =>
                            {
                                settings.EnableFirstPersonZoom = value;
                                SaveInt(modContext.ModId, FirstPersonKey, value ? 1 : 0);
                            })
                        .AddToggle(ShowModeHintsKey, "cameratools_show_mode_hints_label",
                            settings.ShowModeHints,
                            value =>
                            {
                                settings.ShowModeHints = value;
                                SaveInt(modContext.ModId, ShowModeHintsKey, value ? 1 : 0);
                            });

                LogOptionsDebug(modContext, $"CameraTools: built options count = {options.Options.Count} for modId={modContext.ModId}.");

                LogOptionsDebug(modContext, $"CameraTools: registering options for modId={modContext.ModId}.");
                OptionsService.Register(modContext.ModId, options);
                registeredModId = modContext.ModId;
                LogOptionsDebug(modContext, $"CameraTools: options registered successfully for modId={modContext.ModId}.");
            }
            catch (System.Exception exception)
            {
                LogOptionsDebug(modContext, $"CameraTools: failed to build/register options. {exception}");
                throw;
            }
        }

        public void Shutdown()
        {
            if (context == null)
                return;

            if (!string.IsNullOrEmpty(registeredModId))
            {
                LogOptionsDebug(context, $"CameraTools: unregistering options on shutdown for modId={registeredModId}.");
                OptionsService.RemoveModOptions(registeredModId);
            }

            registeredModId = null;
            LogOptionsDebug(context, "CameraTools: options unregistered.");
            context = null;
        }

        private static int GetHotkeyIndex(UnityEngine.KeyCode keyCode)
        {
            for (var i = 0; i < HotkeyValues.Length; i++)
            {
                if (HotkeyValues[i] == keyCode)
                    return i;
            }

            return 0;
        }

        private static bool LoadSavedBool(string modId, string key, bool fallback)
        {
            var preferenceKey = modId + "." + key;
            return UnityEngine.PlayerPrefs.HasKey(preferenceKey)
                ? UnityEngine.PlayerPrefs.GetInt(preferenceKey, fallback ? 1 : 0) != 0
                : fallback;
        }

        private static int LoadSavedSlider(string modId, string key, int fallback)
        {
            return UnityEngine.Mathf.Clamp(UnityEngine.PlayerPrefs.GetInt(modId + "." + key, fallback), 1, 10);
        }

        private static UnityEngine.KeyCode LoadSavedHotkey(string modId, string key, UnityEngine.KeyCode fallback)
        {
            var preferenceKey = modId + "." + key;
            var saved = UnityEngine.PlayerPrefs.GetInt(preferenceKey, (int)fallback);
            foreach (var hotkey in HotkeyValues)
            {
                if ((int)hotkey == saved)
                    return hotkey;
            }

            return fallback;
        }

        private static void SaveInt(string modId, string key, int value)
        {
            UnityEngine.PlayerPrefs.SetInt(modId + "." + key, value);
            UnityEngine.PlayerPrefs.Save();
        }

        private static void LogOptionsDebug(ModContext modContext, string message)
        {
            modContext.Logger.Info(message);
        }

        private static string GetSavedHideMapMarkersKey(string modId)
        {
            return modId + "." + HideMapMarkersKey;
        }

        private static bool LoadSavedHideMapMarkersValue(string modId, bool fallbackValue)
        {
            var key = GetSavedHideMapMarkersKey(modId);
            return UnityEngine.PlayerPrefs.HasKey(key)
                ? UnityEngine.PlayerPrefs.GetInt(key, fallbackValue ? 1 : 0) != 0
                : fallbackValue;
        }

        private static void SaveHideMapMarkersValue(string modId, bool value)
        {
            var key = GetSavedHideMapMarkersKey(modId);
            UnityEngine.PlayerPrefs.SetInt(key, value ? 1 : 0);
            UnityEngine.PlayerPrefs.Save();
        }

        private static string GetSavedDisableCityFogKey(string modId)
        {
            return modId + "." + DisableCityFogKey;
        }

        private static bool LoadSavedDisableCityFogValue(string modId, bool fallbackValue)
        {
            var key = GetSavedDisableCityFogKey(modId);
            return UnityEngine.PlayerPrefs.HasKey(key)
                ? UnityEngine.PlayerPrefs.GetInt(key, fallbackValue ? 1 : 0) != 0
                : fallbackValue;
        }

        private static void SaveDisableCityFogValue(string modId, bool value)
        {
            var key = GetSavedDisableCityFogKey(modId);
            UnityEngine.PlayerPrefs.SetInt(key, value ? 1 : 0);
            UnityEngine.PlayerPrefs.Save();
        }
    }
}
