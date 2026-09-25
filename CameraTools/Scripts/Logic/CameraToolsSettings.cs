using UnityEngine;

namespace CameraTools
{
    public sealed class CameraToolsSettings
    {
        public bool EnableGameplayTweaks { get; set; } = true;

        public int GameplayMaxZoom { get; set; } = 43;

        public int GameplayDefaultPitch { get; set; } = 35;

        public int GameplayMinPitch { get; set; } = 1;

        public int GameplayMaxPitch { get; set; } = 80;

        public int VehicleMaxZoom { get; set; } = 65;

        public int VehicleCameraHeightCm { get; set; } = 0;

        public bool FollowPlayerOnFoot { get; set; } = false;

        public bool EnableFirstPersonZoom { get; set; } = true;

        public bool ShowModeHints { get; set; } = true;

        public int FirstPersonMouseSensitivity { get; set; } = 5;

        public int FreeCameraMouseSensitivity { get; set; } = 5;

        public KeyCode FreeCameraHotkey { get; set; } = KeyCode.PageUp;

        public KeyCode ScenicViewHotkey { get; set; } = KeyCode.F7;

        public KeyCode HideUiHotkey { get; set; } = KeyCode.F6;

        public bool HideMapMarkersWithUi { get; set; } = false;

        public bool DisableCityFog { get; set; } = false;

        public bool EnableCameraToolsDebug { get; set; } = false;

        public bool EnableCameraModesDebugLogging { get; set; } = false;

        public bool EnableUpdateNoticeDebugLogging { get; set; } = false;

        public bool EnableJobBoardUiDebugLogging { get; set; } = false;

        public bool EnableHiddenUiDebugLogging { get; set; } = false;

        public bool EnableTimeMachineDebugLogging { get; set; } = false;

        public bool EnableGameplayZoomDebugLogging { get; set; } = false;

        public bool EnableVehicleDebugLogging { get; set; } = false;

        public bool EnableVehicleDebugOverlay { get; set; } = false;

        public bool EnableIndoorCameraDebugLogging { get; set; } = false;

        public bool EnableMapTopDown { get; set; } = true;

        public int MapPitch { get; set; } = 90;

        public int MapDistance { get; set; } = 800;

        public int MapOrthographicSize { get; set; } = 70;
    }
}
