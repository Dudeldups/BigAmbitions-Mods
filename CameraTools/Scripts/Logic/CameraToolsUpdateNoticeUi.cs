#nullable enable
using BAModAPI;
using Localizor;
using UnityEngine;

namespace CameraTools
{
    // Keeps the acknowledged update notice and shares its rounded style with mode hints.
    internal sealed class CameraToolsUpdateNoticeUi
    {
        private const int CurrentNoticeVersion = 7;
        private const string SeenVersionPreference = "camera_tools_update_notice_seen_version";
        private const int UpdateWindowId = 348723;
        private const float UpdateWindowWidth = 540f;
        private const float UpdateWindowMinimumHeight = 220f;
        private const float HintDurationSeconds = 7f;
        private const float PanelWidth = 530f;
        private const float PanelHeight = 138f;
        private const float PanelMargin = 24f;

        private ModContext? context;
        private CameraToolsSettings? settings;
        private string modId = string.Empty;
        private bool updateVisible;
        private bool isFreeCameraActive;
        private Rect updateWindowRect = new Rect(0f, 0f, UpdateWindowWidth, UpdateWindowMinimumHeight);
        private Vector2 updateScrollPosition;
        private string? titleKey;
        private string? bodyKey;
        private float visibleUntil;
        private bool isSuppressedByNativeUi;
        private bool wasDisplayed;
        private Texture2D? solidTexture;
        private Texture2D? panelBackgroundTexture;
        private Texture2D? updateBackgroundTexture;
        private Texture2D? buttonBackgroundTexture;
        private Texture2D? buttonActiveBackgroundTexture;
        private GUIStyle? titleStyle;
        private GUIStyle? bodyStyle;
        private GUIStyle? updateWindowStyle;
        private GUIStyle? buttonStyle;

        public void Initialize(ModContext currentContext, CameraToolsSettings currentSettings)
        {
            context = currentContext;
            settings = currentSettings;
            modId = currentContext.ModId;
            var seenVersion = LoadSeenVersion();
            updateVisible = seenVersion < CurrentNoticeVersion;
            updateScrollPosition = Vector2.zero;
            isFreeCameraActive = false;
            ClearModeHint();
            isSuppressedByNativeUi = false;
            titleStyle = null;
            bodyStyle = null;
            updateWindowStyle = null;
            buttonStyle = null;
            LogDebug($"initialized: seenVersion={seenVersion}, currentVersion={CurrentNoticeVersion}, updatePending={updateVisible}");
        }

        public void SetSuppressedByNativeUi(bool suppressed, bool freeCameraActive)
        {
            if (isSuppressedByNativeUi != suppressed)
                LogDebug($"native UI suppression changed: suppressed={suppressed}, updatePending={updateVisible}");
            isSuppressedByNativeUi = suppressed;
            isFreeCameraActive = freeCameraActive;
        }

        public void ShowModeHint(string localizedTitleKey, string localizedBodyKey)
        {
            if (settings?.ShowModeHints != true)
            {
                LogDebug($"control hint skipped because disabled: title={localizedTitleKey}");
                return;
            }

            titleKey = localizedTitleKey;
            bodyKey = localizedBodyKey;
            visibleUntil = Time.unscaledTime + HintDurationSeconds;
            wasDisplayed = false;
            LogDebug($"control hint requested: title={localizedTitleKey}, body={localizedBodyKey}");
        }

        public void ClearModeHint()
        {
            titleKey = null;
            bodyKey = null;
            visibleUntil = 0f;
            wasDisplayed = false;
        }

        public void OnGui()
        {
            if (updateVisible && SaveGameManager.Current != null &&
                !isSuppressedByNativeUi && !isFreeCameraActive)
                DrawUpdateNotice();

            DrawModeHint();
        }

        private void DrawModeHint()
        {
            if (settings?.ShowModeHints != true || titleKey == null || bodyKey == null ||
                Time.unscaledTime >= visibleUntil ||
                SaveGameManager.Current == null || isSuppressedByNativeUi)
                return;

            EnsureStyles();
            var width = Mathf.Min(PanelWidth, Screen.width - PanelMargin * 2f);
            var height = Mathf.Min(PanelHeight, Screen.height - PanelMargin * 2f);
            if (width <= 0f || height <= 0f)
                return;

            var panel = new Rect(PanelMargin, Screen.height - PanelMargin - height, width, height);
            var previousColor = GUI.color;
            var previousContentColor = GUI.contentColor;
            try
            {
                GUI.color = Color.white;
                GUI.contentColor = Color.white;
                GUI.DrawTexture(panel, panelBackgroundTexture!, ScaleMode.StretchToFill, true);
                GUI.Label(new Rect(panel.x + 20f, panel.y + 12f, width - 40f, 32f), Localize(titleKey), titleStyle!);
                GUI.DrawTexture(new Rect(panel.x + 20f, panel.y + 52f, width - 40f, 2f), solidTexture!);
                GUI.Label(new Rect(panel.x + 20f, panel.y + 64f, width - 40f, height - 72f), Localize(bodyKey), bodyStyle!);
            }
            finally
            {
                GUI.color = previousColor;
                GUI.contentColor = previousContentColor;
            }

            if (!wasDisplayed)
            {
                wasDisplayed = true;
                LogDebug($"control hint displayed: title={titleKey}");
            }
        }

        private void DrawUpdateNotice()
        {
            EnsureStyles();
            var width = Mathf.Min(UpdateWindowWidth, Screen.width - PanelMargin * 2f);
            var availableHeight = Screen.height - PanelMargin * 2f;
            if (width <= 0f || availableHeight <= 0f)
                return;

            var body = Localize("cameratools_update_notification");
            var measuredBodyHeight = bodyStyle!.CalcHeight(new GUIContent(body), width - 44f);
            var height = Mathf.Min(Mathf.Max(UpdateWindowMinimumHeight, measuredBodyHeight + 150f), availableHeight);
            updateWindowRect = new Rect((Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f, width, height);

            var previousColor = GUI.color;
            var previousBackgroundColor = GUI.backgroundColor;
            var previousContentColor = GUI.contentColor;
            try
            {
                GUI.color = Color.white;
                GUI.backgroundColor = Color.white;
                GUI.contentColor = Color.white;
                GUI.DrawTexture(updateWindowRect, updateBackgroundTexture!, ScaleMode.StretchToFill, true);
                updateWindowRect = GUI.Window(UpdateWindowId, updateWindowRect, _ => DrawUpdateWindowContent(body),
                    GUIContent.none, updateWindowStyle!);
            }
            finally
            {
                GUI.color = previousColor;
                GUI.backgroundColor = previousBackgroundColor;
                GUI.contentColor = previousContentColor;
            }
        }

        private void DrawUpdateWindowContent(string body)
        {
            var width = updateWindowRect.width;
            var height = updateWindowRect.height;
            GUI.Label(new Rect(22f, 18f, width - 44f, 34f), Localize("cameratools_update_notice_title"), titleStyle!);
            GUI.DrawTexture(new Rect(22f, 64f, width - 44f, 2f), solidTexture!);

            var bodyViewport = new Rect(22f, 82f, width - 44f, Mathf.Max(20f, height - 150f));
            var bodyHeight = bodyStyle!.CalcHeight(new GUIContent(body), bodyViewport.width);
            if (bodyHeight <= bodyViewport.height)
            {
                GUI.Label(bodyViewport, body, bodyStyle);
            }
            else
            {
                var contentWidth = Mathf.Max(1f, bodyViewport.width - 20f);
                var contentHeight = bodyStyle.CalcHeight(new GUIContent(body), contentWidth);
                updateScrollPosition = GUI.BeginScrollView(bodyViewport, updateScrollPosition,
                    new Rect(0f, 0f, contentWidth, contentHeight));
                GUI.Label(new Rect(0f, 0f, contentWidth, contentHeight), body, bodyStyle);
                GUI.EndScrollView();
            }

            var buttonRect = new Rect(width - 172f, height - 62f, 150f, 42f);
            var currentEvent = Event.current;
            var buttonTexture = currentEvent != null &&
                buttonRect.Contains(currentEvent.mousePosition) &&
                (currentEvent.type == EventType.MouseDown || currentEvent.type == EventType.MouseDrag)
                    ? buttonActiveBackgroundTexture
                    : buttonBackgroundTexture;
            GUI.DrawTexture(buttonRect, buttonTexture!, ScaleMode.StretchToFill, true);
            if (GUI.Button(buttonRect, Localize("cameratools_update_notice_got_it"), buttonStyle!))
                AcknowledgeUpdate();
        }

        private int LoadSeenVersion()
        {
            var key = modId + "." + SeenVersionPreference;
            return UnityEngine.PlayerPrefs.HasKey(key) ? UnityEngine.PlayerPrefs.GetInt(key, 0) : 0;
        }

        private void AcknowledgeUpdate()
        {
            UnityEngine.PlayerPrefs.SetInt(modId + "." + SeenVersionPreference, CurrentNoticeVersion);
            UnityEngine.PlayerPrefs.Save();
            updateVisible = false;
            LogDebug($"update acknowledged: savedVersion={CurrentNoticeVersion}");
        }

        public void Shutdown()
        {
            ClearModeHint();
            updateVisible = false;
            modId = string.Empty;
            isSuppressedByNativeUi = false;
            isFreeCameraActive = false;
            DestroyTexture(ref solidTexture);
            DestroyTexture(ref panelBackgroundTexture);
            DestroyTexture(ref updateBackgroundTexture);
            DestroyTexture(ref buttonBackgroundTexture);
            DestroyTexture(ref buttonActiveBackgroundTexture);
            titleStyle = null;
            bodyStyle = null;
            updateWindowStyle = null;
            buttonStyle = null;
            context = null;
            settings = null;
        }

        private void EnsureStyles()
        {
            if (solidTexture == null)
            {
                solidTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                solidTexture.SetPixel(0, 0, new Color(0.78f, 0.82f, 0.87f, 1f));
                solidTexture.Apply();
            }

            panelBackgroundTexture ??= MakeRoundedRectTexture(
                (int)PanelWidth, (int)PanelHeight, new Color(0.97f, 0.97f, 0.98f, 1f), 14);
            updateBackgroundTexture ??= MakeRoundedRectTexture(
                (int)UpdateWindowWidth, (int)UpdateWindowMinimumHeight, new Color(0.97f, 0.97f, 0.98f, 1f), 14);
            buttonBackgroundTexture ??= MakeRoundedRectTexture(150, 42, new Color(0.22f, 0.56f, 0.93f, 1f), 8);
            buttonActiveBackgroundTexture ??= MakeRoundedRectTexture(150, 42, new Color(0.17f, 0.47f, 0.84f, 1f), 8);
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 23,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.10f, 0.12f, 0.16f, 1f) }
            };
            bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                wordWrap = true,
                richText = true,
                normal = { textColor = new Color(0.10f, 0.12f, 0.16f, 1f) }
            };
            updateWindowStyle ??= new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(22, 22, 20, 20),
                border = new RectOffset(14, 14, 14, 14),
                normal = { background = null, textColor = Color.clear },
                hover = { background = null, textColor = Color.clear },
                active = { background = null, textColor = Color.clear },
                focused = { background = null, textColor = Color.clear }
            };
            buttonStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 40f,
                margin = new RectOffset(0, 0, 0, 0),
                normal = { background = null, textColor = Color.white },
                hover = { background = null, textColor = Color.white },
                active = { background = null, textColor = Color.white },
                focused = { background = null, textColor = Color.white }
            };
        }

        private static void DestroyTexture(ref Texture2D? texture)
        {
            if (texture == null)
                return;

            Object.Destroy(texture);
            texture = null;
        }

        private static Texture2D MakeRoundedRectTexture(int width, int height, Color color, int radius)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var transparent = new Color(0f, 0f, 0f, 0f);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var drawPixel = true;
                    if (x < radius && y < radius)
                        drawPixel = IsInsideCorner(x, y, radius - 1, radius - 1, radius);
                    else if (x >= width - radius && y < radius)
                        drawPixel = IsInsideCorner(x, y, width - radius, radius - 1, radius);
                    else if (x < radius && y >= height - radius)
                        drawPixel = IsInsideCorner(x, y, radius - 1, height - radius, radius);
                    else if (x >= width - radius && y >= height - radius)
                        drawPixel = IsInsideCorner(x, y, width - radius, height - radius, radius);

                    texture.SetPixel(x, y, drawPixel ? color : transparent);
                }
            }

            texture.Apply();
            return texture;
        }

        private static bool IsInsideCorner(int x, int y, int centerX, int centerY, int radius)
        {
            var deltaX = x - centerX;
            var deltaY = y - centerY;
            return deltaX * deltaX + deltaY * deltaY <= radius * radius;
        }

        private static string Localize(string key)
        {
            return key.Localize().ToString();
        }

        private void LogDebug(string message)
        {
            if (context == null || settings == null ||
                !settings.EnableCameraToolsDebug || !settings.EnableUpdateNoticeDebugLogging)
                return;

            context.Logger.Info($"CameraTools info panel: {message}");
        }
    }
}
