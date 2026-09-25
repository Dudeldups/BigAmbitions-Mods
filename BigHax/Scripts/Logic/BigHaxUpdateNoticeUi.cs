#nullable enable
using BAModAPI;
using Localizor;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxUpdateNoticeUi
    {
        private const int CurrentNoticeVersion = 13;
        private const int WindowId = 348722;
        private const float WindowWidth = 540f;
        private const float WindowHeight = 260f;
        private const float WindowMargin = 24f;

        private Rect windowRect = new Rect(0f, 0f, WindowWidth, WindowHeight);
        private BigHaxBaUnifiedUpdateNoticeUi? baUnifiedUi;
        private bool isVisible;
        private bool needsCentering = true;
        private bool uiSelectionResolved;
        private float libraryWaitStartedAt = -1f;
        private int hotControlId;

        private Texture2D? solidTexture;
        private GUIStyle? windowStyle;
        private GUIStyle? titleStyle;
        private GUIStyle? bodyStyle;
        private GUIStyle? buttonStyle;

        public void Initialize(string modId)
        {
            baUnifiedUi?.Destroy();
            baUnifiedUi = null;
            uiSelectionResolved = false;
            libraryWaitStartedAt = -1f;
            isVisible = BigHaxOptionPersistence.LoadUpdateNoticeSeenVersion(modId) < CurrentNoticeVersion;
            if (isVisible)
            {
                needsCentering = true;
                ResetStyleCache();
            }
        }

        public void HandleSceneLoaded()
        {
            if (!isVisible)
                return;

            needsCentering = true;
            hotControlId = 0;
            ResetStyleCache();
            if (baUnifiedUi != null)
            {
                baUnifiedUi.Destroy();
                baUnifiedUi = null;
                uiSelectionResolved = false;
                libraryWaitStartedAt = -1f;
            }
        }

        public void ConsumeGameplayInputIfNeeded()
        {
            if (!ShouldDisplay())
                return;

            if (baUnifiedUi != null)
            {
                baUnifiedUi.ConsumeGameplayInputIfNeeded();
                return;
            }

            if (IsMouseOverWindow() || GUIUtility.hotControl == hotControlId)
                Input.ResetInputAxes();
        }

        public void OnGui(ModContext context)
        {
            if (!ShouldDisplay())
                return;

            if (!uiSelectionResolved && !ResolveUi(context))
                return;

            if (baUnifiedUi != null)
            {
                if (baUnifiedUi.EnsureVisible())
                    return;

                baUnifiedUi.Destroy();
                baUnifiedUi = null;
                uiSelectionResolved = false;
                libraryWaitStartedAt = -1f;
                if (!ResolveUi(context))
                    return;
                if (baUnifiedUi != null && baUnifiedUi.EnsureVisible())
                    return;
            }

            EnsureStyles();
            EnsureWindowIsCenteredIfNeeded();
            CaptureHotControl();

            var previousColor = GUI.color;
            var previousBackgroundColor = GUI.backgroundColor;
            var previousContentColor = GUI.contentColor;
            try
            {
                GUI.color = Color.white;
                GUI.backgroundColor = Color.white;
                GUI.contentColor = Color.white;
                windowRect = GUI.Window(WindowId, windowRect, _ => DrawWindow(context), GUIContent.none, windowStyle!);
            }
            finally
            {
                GUI.color = previousColor;
                GUI.backgroundColor = previousBackgroundColor;
                GUI.contentColor = previousContentColor;
            }
        }

        private void DrawWindow(ModContext context)
        {
            GUILayout.BeginVertical();
            GUILayout.Label(Localize("bighax_update_notice_title"), titleStyle!);
            DrawSeparator();
            GUILayout.Label(Localize("bighax_update_notification"), bodyStyle!);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Localize("bighax_update_notice_got_it"), buttonStyle!, GUILayout.Width(150f), GUILayout.Height(42f)))
                Acknowledge(context);

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private bool ShouldDisplay()
        {
            return isVisible && SaveGameManager.Current != null;
        }

        public void Shutdown()
        {
            baUnifiedUi?.Destroy();
            baUnifiedUi = null;
            uiSelectionResolved = false;
            libraryWaitStartedAt = -1f;
            isVisible = false;
        }

        private bool ResolveUi(ModContext context)
        {
            if (BigHaxBaUnifiedUpdateNoticeUi.TryCreate(
                    Localize("bighax_update_notice_title"),
                    Localize("bighax_update_notification"),
                    Localize("bighax_update_notice_got_it"),
                    () => Acknowledge(context),
                    out baUnifiedUi,
                    out var reason))
            {
                uiSelectionResolved = true;
                libraryWaitStartedAt = -1f;
                return true;
            }

            // Workshop mods may load after Big Hax. Wait briefly so a present UI
            // library is selected instead of locking this notice into the fallback.
            if (reason.IndexOf("LIB_BaUnifiedUI is not loaded", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (libraryWaitStartedAt < 0f)
                    libraryWaitStartedAt = Time.realtimeSinceStartup;

                if (Time.realtimeSinceStartup - libraryWaitStartedAt < 3f)
                    return false;
            }

            uiSelectionResolved = true;
            return true;
        }

        private void Acknowledge(ModContext context)
        {
            BigHaxOptionPersistence.SaveUpdateNoticeSeenVersion(context.ModId, CurrentNoticeVersion);
            isVisible = false;
            baUnifiedUi?.Destroy();
            baUnifiedUi = null;
        }

        private void CaptureHotControl()
        {
            var currentEvent = Event.current;
            if (currentEvent == null)
                return;

            if (hotControlId == 0)
                hotControlId = GUIUtility.GetControlID(FocusType.Passive);

            if (!IsMouseOverWindow())
            {
                if (GUIUtility.hotControl == hotControlId &&
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
                    GUIUtility.hotControl = hotControlId;
                    currentEvent.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == hotControlId)
                        GUIUtility.hotControl = 0;

                    currentEvent.Use();
                    break;
            }
        }

        private bool IsMouseOverWindow()
        {
            var mousePosition = Input.mousePosition;
            var guiMousePosition = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
            return windowRect.Contains(guiMousePosition);
        }

        private void DrawSeparator()
        {
            GUILayout.Space(12f);
            var rect = GUILayoutUtility.GetRect(1f, 2f, GUILayout.ExpandWidth(true));
            var previousColor = GUI.color;
            GUI.color = new Color(0.78f, 0.82f, 0.87f, 1f);
            GUI.DrawTexture(rect, solidTexture!);
            GUI.color = previousColor;
            GUILayout.Space(12f);
        }

        private void EnsureWindowIsCenteredIfNeeded()
        {
            if (!needsCentering)
                return;

            windowRect.width = Mathf.Min(WindowWidth, Screen.width - (WindowMargin * 2f));
            windowRect.height = Mathf.Min(WindowHeight, Screen.height - (WindowMargin * 2f));
            windowRect.x = Mathf.Max(WindowMargin, (Screen.width - windowRect.width) * 0.5f);
            windowRect.y = Mathf.Max(WindowMargin, (Screen.height - windowRect.height) * 0.5f);
            needsCentering = false;
        }

        private void ResetStyleCache()
        {
            windowStyle = null;
            titleStyle = null;
            bodyStyle = null;
            buttonStyle = null;
        }

        private void EnsureStyles()
        {
            if (solidTexture == null)
            {
                solidTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                solidTexture.SetPixel(0, 0, Color.white);
                solidTexture.Apply();
            }

            windowStyle ??= CreateWindowStyle();
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.10f, 0.12f, 0.16f, 1f) },
                margin = new RectOffset(0, 0, 0, 0)
            };
            bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                wordWrap = true,
                richText = true,
                normal = { textColor = new Color(0.10f, 0.12f, 0.16f, 1f) },
                margin = new RectOffset(0, 0, 0, 0)
            };
            buttonStyle ??= CreateButtonStyle(
                new Color(0.22f, 0.56f, 0.93f, 1f),
                new Color(0.17f, 0.47f, 0.84f, 1f));
        }

        private GUIStyle CreateWindowStyle()
        {
            var backgroundTexture = MakeRoundedRectTexture(64, 64, new Color(0.97f, 0.97f, 0.98f, 1f), 14);
            return new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(22, 22, 20, 20),
                border = new RectOffset(14, 14, 14, 14),
                normal = { background = backgroundTexture, textColor = Color.clear },
                hover = { background = backgroundTexture, textColor = Color.clear },
                active = { background = backgroundTexture, textColor = Color.clear },
                focused = { background = backgroundTexture, textColor = Color.clear },
                onNormal = { background = backgroundTexture, textColor = Color.clear },
                onHover = { background = backgroundTexture, textColor = Color.clear },
                onActive = { background = backgroundTexture, textColor = Color.clear },
                onFocused = { background = backgroundTexture, textColor = Color.clear }
            };
        }

        private GUIStyle CreateButtonStyle(Color normalColor, Color activeColor)
        {
            var normalBackground = MakeRoundedRectTexture(48, 48, normalColor, 8);
            var activeBackground = MakeRoundedRectTexture(48, 48, activeColor, 8);
            return new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 40f,
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(8, 8, 8, 8),
                normal = { background = normalBackground, textColor = Color.white },
                hover = { background = normalBackground, textColor = Color.white },
                active = { background = activeBackground, textColor = Color.white },
                focused = { background = normalBackground, textColor = Color.white },
                onNormal = { background = activeBackground, textColor = Color.white },
                onHover = { background = activeBackground, textColor = Color.white },
                onActive = { background = activeBackground, textColor = Color.white },
                onFocused = { background = activeBackground, textColor = Color.white }
            };
        }

        private Texture2D MakeRoundedRectTexture(int width, int height, Color color, int radius)
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

        private bool IsInsideCorner(int x, int y, int centerX, int centerY, int radius)
        {
            var deltaX = x - centerX;
            var deltaY = y - centerY;
            return (deltaX * deltaX) + (deltaY * deltaY) <= radius * radius;
        }

        private static string Localize(string key)
        {
            return key.Localize().ToString();
        }
    }
}
