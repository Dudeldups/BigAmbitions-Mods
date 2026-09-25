#nullable enable
using System;
using System.Reflection;
using Entities;
using Localizor;
using UI;
using UI.PurchaseVehicle;
using UnityEngine;

namespace VehicleRepainter
{
    // The acknowledged update popup follows the Camera Tools notice style.
    internal sealed class VehicleRepainterUpdateNoticeUi
    {
        private const int CurrentNoticeVersion = 1;
        private const string SeenVersionPreference = "vehicle_repainter_update_notice_seen_version";
        private const int WindowId = 348724;
        private const float WindowWidth = 540f;
        private const float MinimumHeight = 220f;
        private const float Margin = 24f;

        private static readonly Type? CityMapType = FindType("CityMap");
        private static readonly Type? MiniMenuType = FindType("UI.MiniMenu.MiniMenu");
        private static readonly Type? FullMenuType = FindType("UI.Smartphone.FullMenu");
        private static readonly Type? OptionsType = FindType("Scenes.MainMenu.Options");
        private static readonly Type? PlacementType = FindType("BigAmbitions.PlacementSystem.PlacementSystem");
        private static readonly Type? InteriorDesignerType = FindType("UI.InteriorDesigner.InteriorDesignerUI");
        private static readonly Type? DialogControllerType = FindType("DialogController");
        private static readonly Type? DialogUiType = FindType("UI.Dialog.DialogUI");
        private static readonly FieldInfo? CurrentDialogField = DialogControllerType?.GetField(
            "current", BindingFlags.Public | BindingFlags.Static);
        private static readonly FieldInfo? DialogPanelOpenField = DialogUiType?.GetField(
            "isPanelOpen", BindingFlags.Public | BindingFlags.Instance);

        private readonly VehicleRepainterRuntime runtime;
        private readonly string preferenceKey;
        private bool visible;
        private bool wasDisplayed;
        private Rect windowRect = new Rect(0f, 0f, WindowWidth, MinimumHeight);
        private Vector2 scrollPosition;
        private Texture2D? dividerTexture;
        private Texture2D? windowTexture;
        private Texture2D? buttonTexture;
        private Texture2D? pressedButtonTexture;
        private GUIStyle? titleStyle;
        private GUIStyle? bodyStyle;
        private GUIStyle? windowStyle;
        private GUIStyle? buttonStyle;

        internal VehicleRepainterUpdateNoticeUi(VehicleRepainterRuntime runtime)
        {
            this.runtime = runtime;
            preferenceKey = runtime.ModId + "." + SeenVersionPreference;
            var seenVersion = UnityEngine.PlayerPrefs.GetInt(preferenceKey, 0);
            visible = seenVersion < CurrentNoticeVersion;
            runtime.TraceUpdateNotice(
                $"Initialized: seenVersion={seenVersion}, currentVersion={CurrentNoticeVersion}, pending={visible}.");
        }

        internal void OnGui()
        {
            if (!visible || SaveGameManager.Current == null || IsNativeUiOpen())
                return;

            EnsureStyles();
            var width = Mathf.Min(WindowWidth, Screen.width - Margin * 2f);
            var availableHeight = Screen.height - Margin * 2f;
            if (width <= 0f || availableHeight <= 0f)
                return;

            var body = Localize("vehicle-repainter:update_notice_body");
            var measuredHeight = bodyStyle!.CalcHeight(new GUIContent(body), width - 44f);
            var height = Mathf.Min(Mathf.Max(MinimumHeight, measuredHeight + 150f), availableHeight);
            windowRect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            var previousColor = GUI.color;
            var previousBackgroundColor = GUI.backgroundColor;
            var previousContentColor = GUI.contentColor;
            try
            {
                GUI.color = Color.white;
                GUI.backgroundColor = Color.white;
                GUI.contentColor = Color.white;
                GUI.DrawTexture(windowRect, windowTexture!, ScaleMode.StretchToFill, true);
                windowRect = GUI.Window(WindowId, windowRect, _ => DrawWindowContent(body),
                    GUIContent.none, windowStyle!);
            }
            finally
            {
                GUI.color = previousColor;
                GUI.backgroundColor = previousBackgroundColor;
                GUI.contentColor = previousContentColor;
            }

            if (!wasDisplayed)
            {
                wasDisplayed = true;
                runtime.TraceUpdateNotice("Displayed the update notice after save load.");
            }
        }

        private void DrawWindowContent(string body)
        {
            var width = windowRect.width;
            var height = windowRect.height;
            GUI.Label(new Rect(22f, 18f, width - 44f, 34f),
                Localize("vehicle-repainter:update_notice_title"), titleStyle!);
            GUI.DrawTexture(new Rect(22f, 64f, width - 44f, 2f), dividerTexture!);

            var viewport = new Rect(22f, 82f, width - 44f, Mathf.Max(20f, height - 150f));
            var bodyHeight = bodyStyle!.CalcHeight(new GUIContent(body), viewport.width);
            if (bodyHeight <= viewport.height)
            {
                GUI.Label(viewport, body, bodyStyle);
            }
            else
            {
                var contentWidth = Mathf.Max(1f, viewport.width - 20f);
                var contentHeight = bodyStyle.CalcHeight(new GUIContent(body), contentWidth);
                scrollPosition = GUI.BeginScrollView(viewport, scrollPosition,
                    new Rect(0f, 0f, contentWidth, contentHeight));
                GUI.Label(new Rect(0f, 0f, contentWidth, contentHeight), body, bodyStyle);
                GUI.EndScrollView();
            }

            var buttonRect = new Rect(width - 172f, height - 62f, 150f, 42f);
            var currentEvent = Event.current;
            var background = currentEvent != null && buttonRect.Contains(currentEvent.mousePosition) &&
                (currentEvent.type == EventType.MouseDown || currentEvent.type == EventType.MouseDrag)
                    ? pressedButtonTexture
                    : buttonTexture;
            GUI.DrawTexture(buttonRect, background!, ScaleMode.StretchToFill, true);
            if (GUI.Button(buttonRect, Localize("vehicle-repainter:update_notice_got_it"), buttonStyle!))
                Acknowledge();
        }

        private void Acknowledge()
        {
            UnityEngine.PlayerPrefs.SetInt(preferenceKey, CurrentNoticeVersion);
            UnityEngine.PlayerPrefs.Save();
            visible = false;
            runtime.TraceUpdateNotice($"Acknowledged and saved version {CurrentNoticeVersion}.");
        }

        internal void Shutdown()
        {
            visible = false;
            DestroyTexture(ref dividerTexture);
            DestroyTexture(ref windowTexture);
            DestroyTexture(ref buttonTexture);
            DestroyTexture(ref pressedButtonTexture);
            titleStyle = null;
            bodyStyle = null;
            windowStyle = null;
            buttonStyle = null;
        }

        private void EnsureStyles()
        {
            if (dividerTexture == null)
            {
                dividerTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                dividerTexture.SetPixel(0, 0, new Color(0.78f, 0.82f, 0.87f, 1f));
                dividerTexture.Apply();
            }

            windowTexture ??= MakeRoundedRectTexture((int)WindowWidth, (int)MinimumHeight,
                new Color(0.97f, 0.97f, 0.98f, 1f), 14);
            buttonTexture ??= MakeRoundedRectTexture(150, 42, new Color(0.22f, 0.56f, 0.93f, 1f), 8);
            pressedButtonTexture ??= MakeRoundedRectTexture(150, 42, new Color(0.17f, 0.47f, 0.84f, 1f), 8);
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
            windowStyle ??= new GUIStyle(GUI.skin.window)
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

        private static bool IsNativeUiOpen()
        {
            var options = InstanceBehavior<UIs>.Instance?.options;
            return PurchaseVehicleUI.IsPanelOpen ||
                (options != null && options.gameObject.activeInHierarchy) ||
                IsStaticFlag(CityMapType, "IsOpen") ||
                IsStaticFlag(MiniMenuType, "IsOpen") ||
                IsStaticFlag(FullMenuType, "IsOpen") ||
                IsStaticFlag(OptionsType, "IsVisible") ||
                IsStaticFlag(PlacementType, "IsInPlacementMode") ||
                IsStaticFlag(InteriorDesignerType, "IsOpen") ||
                IsDialogOpen();
        }

        private static bool IsDialogOpen()
        {
            var current = CurrentDialogField?.GetValue(null);
            return current != null && DialogUiType?.IsInstanceOfType(current) == true &&
                DialogPanelOpenField?.GetValue(current) is true;
        }

        private static bool IsStaticFlag(Type? type, string name)
        {
            if (type == null)
                return false;

            try
            {
                return type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetValue(null) is true;
            }
            catch
            {
                return false;
            }
        }

        private static Type? FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static string Localize(string key) => key.Localize().ToString();

        private static void DestroyTexture(ref Texture2D? texture)
        {
            if (texture == null)
                return;

            UnityEngine.Object.Destroy(texture);
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
    }
}
