using System;
using System.Globalization;
using System.Linq;
using Helpers;
using UnityEngine;

namespace CustomNPCAPI
{
    internal sealed class CustomNpcDeveloperOverlay : MonoBehaviour
    {
        private static readonly string[] Prefabs =
        {
            "Characters/Homeless", "Characters/Pedestrian", "Characters/CasinoCustomer",
            "Characters/CinemaTheaterCustomer", "Characters/FullServiceCustomer", "Characters/GymCustomer",
            "Characters/HairdresserCustomer", "Characters/NightclubCustomer", "Characters/NightclubOutsidePedestrian",
            "Characters/SelfServiceCustomer", "Characters/StreetPerformer", "Characters/CarnivalPedestrian",
            "Characters/WaterPedestrian", "Characters/DummyHuman", "Characters/DummyAi", "Characters/HumanDefinitionLow"
        };

        private Rect _rect = new Rect(28f, 28f, 600f, 570f);
        private bool _visible;
        private int _prefabIndex;
        private string _gender = "Female";
        private string _age = "12410";
        private string _seed = "22073";
        private string _scale = "1.00";
        private string _localYaw = "90";
        private Vector2 _scroll;
        private CustomNpcHandle _preview;
        private int _serial;
        private bool _hasCapturedPlacement;
        private Vector3 _capturedPosition;
        private Vector3 _capturedForward = Vector3.forward;
        private int _hotControlId;

        private void Update()
        {
            if (!CustomNpcApi.DeveloperToolsEnabled)
            {
                _visible = false;
                return;
            }

            if (Input.GetKeyDown(KeyCode.F7))
                _visible = !_visible;

            if (_visible && IsMouseOverWindow())
                Input.ResetInputAxes();
        }

        private void OnDestroy()
        {
            _preview?.Dispose();
            _preview = null;
        }

        private void OnGUI()
        {
            if (!CustomNpcApi.DeveloperToolsEnabled || !_visible)
                return;

            _rect = GUI.Window(190771, _rect, DrawWindow, "Custom NPC API - Developer");
            CaptureWindowInput();
        }

        private void DrawWindow(int id)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("Registered / active NPCs");
            foreach (var handle in CustomNpcApi.ActiveNpcs
                         .Where(h => h != _preview)
                         .OrderBy(h => h.OwnerModId)
                         .ThenBy(h => h.Definition?.Id))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{handle.OwnerModId}: {handle.Definition?.Id} ({handle.Definition?.DisplayName})");
                if (GUILayout.Button("Go", GUILayout.Width(55f)) && handle.Root != null && PlayerHelper.PlayerController != null)
                    PlayerHelper.PlayerController.transform.position = handle.Root.transform.position + Vector3.back * 1.5f;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10f);
            GUILayout.Label("Placement");
            var player = PlayerHelper.PlayerController;
            if (player != null)
            {
                var playerForward = FlatForward(player.transform.forward);
                GUILayout.Label($"Player: {FormatVector(player.transform.position)}  forward: {FormatVector(playerForward)}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Capture player transform"))
                {
                    _capturedPosition = player.transform.position;
                    _capturedForward = playerForward;
                    _hasCapturedPlacement = true;
                }
                if (GUILayout.Button("Copy player JSON"))
                    CopyPlacementJson(player.transform.position, playerForward);
                GUILayout.EndHorizontal();
            }

            if (_hasCapturedPlacement)
            {
                GUILayout.Label($"Captured: {FormatVector(_capturedPosition)}  forward: {FormatVector(_capturedForward)}");
                GUILayout.Label("Capture the target spot, walk away, then Spawn / Refresh to inspect it without standing inside the NPC.");
            }
            else
            {
                GUILayout.Label("No placement captured. Preview will spawn 2.5m in front of the player.");
            }

            GUILayout.Space(10f);
            GUILayout.Label("Appearance preview");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30f))) _prefabIndex = (_prefabIndex - 1 + Prefabs.Length) % Prefabs.Length;
            GUILayout.Label(Prefabs[_prefabIndex]);
            if (GUILayout.Button(">", GUILayout.Width(30f))) _prefabIndex = (_prefabIndex + 1) % Prefabs.Length;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Gender", GUILayout.Width(80f));
            _gender = GUILayout.TextField(_gender);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Age days", GUILayout.Width(80f));
            _age = GUILayout.TextField(_age);
            GUILayout.Label("Seed", GUILayout.Width(45f));
            _seed = GUILayout.TextField(_seed);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale", GUILayout.Width(80f));
            _scale = GUILayout.TextField(_scale);
            GUILayout.Label("Local yaw", GUILayout.Width(65f));
            _localYaw = GUILayout.TextField(_localYaw);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn / Refresh")) SpawnPreview();
            if (GUILayout.Button("Despawn"))
            {
                _preview?.Dispose();
                _preview = null;
            }
            if (GUILayout.Button("Copy NPC JSON")) CopyNpcJson();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("F7 toggles this window. DeveloperToolsEnabled is false by default.");
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, _rect.width, 25f));
        }

        private void SpawnPreview()
        {
            _preview?.Dispose();
            _preview = null;

            var player = PlayerHelper.PlayerController;
            if (player == null)
                return;

            var playerForward = FlatForward(player.transform.forward);
            var position = _hasCapturedPlacement
                ? _capturedPosition
                : player.transform.position + playerForward * 2.5f;
            var forward = _hasCapturedPlacement ? _capturedForward : playerForward;
            var scale = Mathf.Clamp(ParseFloat(_scale, 1f), 0.35f, 2.5f);
            var yaw = ParseFloat(_localYaw, 90f);

            var definition = new CustomNpcDefinition
            {
                Id = "developer-preview-" + (++_serial),
                DisplayName = "Preview",
                PrefabName = Prefabs[_prefabIndex],
                Interactable = false,
                Gender = _gender,
                AgeInDays = ParseInt(_age, 12410),
                AppearanceSeed = ParseInt(_seed, 22073),
                Position = position,
                Forward = forward,
                LocalEulerAngles = new Vector3(0f, yaw, 0f),
                LocalScale = Vector3.one * scale
            };

            _preview = CustomNpcApi.Spawn("CustomNPCAPI:developer", definition, new CustomNpcSpawnOptions { Visible = true });
        }

        private void CopyNpcJson()
        {
            var player = PlayerHelper.PlayerController;
            var previewDefinition = _preview?.Definition;
            var position = previewDefinition != null
                ? previewDefinition.Position
                : _hasCapturedPlacement
                    ? _capturedPosition
                    : player != null ? player.transform.position : Vector3.zero;
            var forward = previewDefinition != null
                ? previewDefinition.Forward
                : _hasCapturedPlacement
                    ? _capturedForward
                    : player != null ? FlatForward(player.transform.forward) : Vector3.forward;
            var scale = Mathf.Clamp(ParseFloat(_scale, 1f), 0.35f, 2.5f);
            var yaw = ParseFloat(_localYaw, 90f);

            var definition = new CustomNpcDefinition
            {
                Id = "mymod:npc_id",
                DisplayName = "NPC Name",
                PrefabName = Prefabs[_prefabIndex],
                Interactable = true,
                Gender = _gender,
                AgeInDays = ParseInt(_age, 12410),
                AppearanceSeed = ParseInt(_seed, 22073),
                Position = position,
                Forward = forward,
                LocalEulerAngles = new Vector3(0f, yaw, 0f),
                LocalScale = Vector3.one * scale,
                CtaTextFallback = "Talk to {npcname}"
            };

            GUIUtility.systemCopyBuffer = definition.ToJson(true);
        }

        private static void CopyPlacementJson(Vector3 position, Vector3 forward)
        {
            GUIUtility.systemCopyBuffer = string.Format(CultureInfo.InvariantCulture,
                "\"Position\": {{ \"x\": {0:0.00}, \"y\": {1:0.00}, \"z\": {2:0.00} }}, \"Forward\": {{ \"x\": {3:0.00}, \"y\": {4:0.00}, \"z\": {5:0.00} }}",
                position.x, position.y, position.z, forward.x, forward.y, forward.z);
        }

        private static Vector3 FlatForward(Vector3 value)
        {
            value.y = 0f;
            return value.sqrMagnitude < 0.001f ? Vector3.forward : value.normalized;
        }

        private static string FormatVector(Vector3 value) =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.00}, {1:0.00}, {2:0.00})", value.x, value.y, value.z);


        private void CaptureWindowInput()
        {
            var currentEvent = Event.current;
            if (currentEvent == null)
                return;

            if (_hotControlId == 0)
                _hotControlId = GUIUtility.GetControlID(FocusType.Passive);

            if (!IsMouseOverWindow())
            {
                if (GUIUtility.hotControl == _hotControlId &&
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
                    GUIUtility.hotControl = _hotControlId;
                    currentEvent.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == _hotControlId)
                        GUIUtility.hotControl = 0;
                    currentEvent.Use();
                    break;
            }
        }

        private bool IsMouseOverWindow()
        {
            var mouse = Input.mousePosition;
            var guiMouse = new Vector2(mouse.x, Screen.height - mouse.y);
            return _rect.Contains(guiMouse);
        }

        private static int ParseInt(string value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;
        private static float ParseFloat(string value, float fallback) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }
}
