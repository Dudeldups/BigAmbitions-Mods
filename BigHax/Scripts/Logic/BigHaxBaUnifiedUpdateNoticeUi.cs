#nullable enable
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BigHax
{
    /// <summary>
    /// Optional BAUnifiedUI renderer for the versioned update notice. The library
    /// is resolved through reflection so Big Hax remains loadable without it.
    /// </summary>
    internal sealed class BigHaxBaUnifiedUpdateNoticeUi
    {
        private const string MinimumLibraryVersion = "1.0.0";
        private const string RootName = "BigHax_BAUnifiedUI_UpdateNotice";
        private const float PanelWidth = 640f;
        private const float PanelHeight = 340f;
        private const float HeaderScale = 1f;

        private readonly BaUiReflection api;
        private GameObject? root;

        private BigHaxBaUnifiedUpdateNoticeUi(BaUiReflection api)
        {
            this.api = api;
        }

        public string LibraryVersion => api.LibraryVersion;
        public string AssemblyName => api.AssemblyName;

        public static bool TryCreate(
            string title,
            string body,
            string acknowledgementLabel,
            Action acknowledge,
            out BigHaxBaUnifiedUpdateNoticeUi? ui,
            out string reason)
        {
            ui = null;
            if (!BaUiReflection.TryResolve(MinimumLibraryVersion, out var api, out reason))
                return false;

            var candidate = new BigHaxBaUnifiedUpdateNoticeUi(api!);
            try
            {
                candidate.Build(title, body, acknowledgementLabel, acknowledge);
                ui = candidate;
                reason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                candidate.Destroy();
                reason = "BAUnifiedUI update notice initialization failed: " + exception.GetBaseException().Message;
                return false;
            }
        }

        public bool EnsureVisible()
        {
            if (root == null)
                return false;

            if (!root.activeSelf)
                root.SetActive(true);

            return true;
        }

        public void ConsumeGameplayInputIfNeeded()
        {
            if (root != null && root.activeInHierarchy)
                Input.ResetInputAxes();
        }

        public void Destroy()
        {
            if (root != null)
                UnityEngine.Object.Destroy(root);
            root = null;
        }

        private void Build(string title, string body, string acknowledgementLabel, Action acknowledge)
        {
            api.EnsureEventSystem();

            root = new GameObject(RootName, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(root);
            api.SetupOverlayCanvas(root, short.MaxValue - 4, interactive: true);

            // The legacy notice can only be dismissed through its acknowledgement
            // button, so clicking the dimmer intentionally remains a no-op.
            api.CreateModalDimmer(root.transform, 0.62f, new UnityAction(() => { }));

            var panel = api.BuildPanel(root.transform, PanelWidth, PanelHeight, "BigHaxUpdateNotice", out var header);
            api.CreateHeaderTitle(header, title, HeaderScale, rightIconCount: 0);

            var bodyLabel = api.CreateCenteredBodyLabel(panel, HeaderScale, wordWrap: true);
            var bodyRect = bodyLabel.GetComponent<RectTransform>();
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(46f, 82f);
            bodyRect.offsetMax = new Vector2(-46f, -68f);
            api.SetText(bodyLabel, body);

            var button = api.CreateVanillaButton(
                panel,
                acknowledgementLabel,
                170f,
                40f,
                new UnityAction(acknowledge),
                "Blue",
                15f);
            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.pivot = new Vector2(1f, 0f);
            buttonRect.anchoredPosition = new Vector2(-30f, 18f);
            buttonRect.sizeDelta = new Vector2(170f, 40f);

            api.ApplyUiLayer(root);
            Canvas.ForceUpdateCanvases();
        }

        private sealed class BaUiReflection
        {
            private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
            private readonly Type buttonStyleType;
            private readonly MethodInfo ensureEventSystem;
            private readonly MethodInfo setupOverlayCanvas;
            private readonly MethodInfo createModalDimmer;
            private readonly MethodInfo buildPanel;
            private readonly MethodInfo createHeaderTitle;
            private readonly MethodInfo createCenteredBodyLabel;
            private readonly MethodInfo createVanillaButton;
            private readonly MethodInfo applyUiLayer;

            private BaUiReflection(
                Assembly assembly,
                string libraryVersion,
                Type bootstrap,
                Type chrome,
                Type widgets)
            {
                AssemblyName = assembly.GetName().Name ?? "LIB_BaUnifiedUI";
                LibraryVersion = libraryVersion;
                buttonStyleType = RequireType(assembly, "Capisoft.Lib.BaUnifiedUI.Chrome.BaVanillaButtonStyle");
                ensureEventSystem = RequireMethod(bootstrap, "EnsureEventSystem", 1);
                setupOverlayCanvas = RequireMethod(chrome, "SetupOverlayCanvas", 3);
                createModalDimmer = RequireMethod(widgets, "CreateModalDimmer", 3);
                buildPanel = RequireMethod(chrome, "BuildPanel", 5);
                createHeaderTitle = RequireMethod(widgets, "CreateHeaderTitleLeft", 5);
                createCenteredBodyLabel = RequireMethod(widgets, "CreateCenteredBodyLabel", 3);
                createVanillaButton = RequireMethod(chrome, "CreateVanillaButton", 10);
                applyUiLayer = RequireMethod(chrome, "ApplyUiLayer", 1);
            }

            public string AssemblyName { get; }
            public string LibraryVersion { get; }

            public static bool TryResolve(string minimumVersion, out BaUiReflection? api, out string reason)
            {
                api = null;
                const string versionTypeName = "Capisoft.Lib.BaUnifiedUI.Core.BaUiVersion";
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(candidate => candidate.GetType(versionTypeName, throwOnError: false) != null);
                if (assembly == null)
                {
                    reason = "LIB_BaUnifiedUI is not loaded";
                    return false;
                }

                try
                {
                    var versionType = RequireType(assembly, versionTypeName);
                    var field = versionType.GetField("Version", PublicStatic)
                        ?? throw new MissingFieldException(versionType.FullName, "Version");
                    var libraryVersion = (field.GetRawConstantValue() ?? field.GetValue(null))?.ToString() ?? string.Empty;
                    if (!TryParseVersion(libraryVersion, out var parsed) ||
                        !TryParseVersion(minimumVersion, out var minimum) ||
                        parsed < minimum)
                    {
                        reason = $"LIB_BaUnifiedUI {libraryVersion} is below required {minimumVersion}";
                        return false;
                    }

                    api = new BaUiReflection(
                        assembly,
                        libraryVersion,
                        RequireType(assembly, "Capisoft.Lib.BaUnifiedUI.Core.BaUiBootstrap"),
                        RequireType(assembly, "Capisoft.Lib.BaUnifiedUI.Chrome.BaUiWidePanelChrome"),
                        RequireType(assembly, "Capisoft.Lib.BaUnifiedUI.Controls.BaUiWidgets"));
                    reason = string.Empty;
                    return true;
                }
                catch (Exception exception)
                {
                    reason = "LIB_BaUnifiedUI update notice API is incompatible: " + exception.GetBaseException().Message;
                    return false;
                }
            }

            public void EnsureEventSystem() => Invoke(ensureEventSystem, null, new object?[] { "BigHax_BAUnifiedUI_EventSystem" });

            public void SetupOverlayCanvas(GameObject target, int sortingOrder, bool interactive) =>
                Invoke(setupOverlayCanvas, null, new object?[] { target, sortingOrder, interactive });

            public void CreateModalDimmer(Transform parent, float alpha, UnityAction onClick) =>
                Invoke(createModalDimmer, null, new object?[] { parent, alpha, onClick });

            public RectTransform BuildPanel(Transform parent, float width, float height, string name, out RectTransform header)
            {
                var arguments = new object?[] { parent, width, height, name, null };
                var result = Invoke(buildPanel, null, arguments) as RectTransform
                    ?? throw new InvalidOperationException("BAUnifiedUI did not create the update notice panel.");
                header = arguments[4] as RectTransform
                    ?? throw new InvalidOperationException("BAUnifiedUI did not create the update notice header.");
                return result;
            }

            public void CreateHeaderTitle(Transform header, string text, float scale, int rightIconCount) =>
                Invoke(createHeaderTitle, null, new object?[] { header, text, scale, rightIconCount, false });

            public Component CreateCenteredBodyLabel(Transform parent, float scale, bool wordWrap) =>
                Invoke(createCenteredBodyLabel, null, new object?[] { parent, scale, wordWrap }) as Component
                ?? throw new InvalidOperationException("BAUnifiedUI did not create the update notice body.");

            public Button CreateVanillaButton(
                Transform parent,
                string label,
                float width,
                float height,
                UnityAction onClick,
                string style,
                float fontSize)
            {
                var styleValue = Enum.Parse(buttonStyleType, style, ignoreCase: true);
                return Invoke(createVanillaButton, null,
                    new object?[] { parent, label, width, height, 1f, onClick, styleValue, fontSize, true, null }) as Button
                    ?? throw new InvalidOperationException("BAUnifiedUI did not create the update notice button.");
            }

            public void SetText(Component label, string value)
            {
                var property = label.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public)
                    ?? throw new MissingMemberException(label.GetType().FullName, "text");
                property.SetValue(label, value, null);
            }

            public void ApplyUiLayer(GameObject target) => Invoke(applyUiLayer, null, new object?[] { target });

            private static Type RequireType(Assembly assembly, string fullName) =>
                assembly.GetType(fullName, throwOnError: false)
                ?? throw new TypeLoadException("Missing BAUnifiedUI type " + fullName);

            private static MethodInfo RequireMethod(Type type, string name, int parameterCount) =>
                type.GetMethods(PublicStatic).FirstOrDefault(method => method.Name == name && method.GetParameters().Length == parameterCount)
                ?? throw new MissingMethodException(type.FullName, name);

            private static object? Invoke(MethodInfo method, object? target, object?[] arguments)
            {
                try
                {
                    return method.Invoke(target, arguments);
                }
                catch (TargetInvocationException exception) when (exception.InnerException != null)
                {
                    throw exception.InnerException;
                }
            }

            private static bool TryParseVersion(string value, out Version version)
            {
                var normalized = (value ?? string.Empty).Split('-', '+')[0];
                return Version.TryParse(normalized, out version);
            }
        }
    }
}
