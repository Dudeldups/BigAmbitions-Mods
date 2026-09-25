#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Pink
{
    internal static partial class PinkRuntime
    {
        private const float ExplicitUiPinkStrength = 0.32f;
        private const float TopbarUiPinkStrength = 0.55f;
        private static readonly Color ExplicitUiPink = new Color(0.62f, 0.08f, 0.38f, 1f);
        private static readonly Color HeaderLightPink = new Color(1f, 0.86f, 0.96f, 1f);
        private static readonly Color HeaderDarkText = new Color(0.06f, 0.07f, 0.09f, 1f);
        private static readonly Dictionary<int, ExplicitUiGraphicSnapshot> ExplicitUiPatchedGraphics = new Dictionary<int, ExplicitUiGraphicSnapshot>();

        internal static int ApplyLoadingUiTintPass()
        {
            var patched = 0;
            patched += TryTintExplicitUiGraphic("LoadingScreen(Clone)", "Background");
            patched += TryTintExplicitUiGraphic("LoadingScreen(Clone)", "ProgressBar/BarIndicator/Background");
            patched += TryTintExplicitUiGraphic("LoadingSpinner", "Panel");
            return patched;
        }

        internal static bool ApplyMainHudUiTintPass()
        {
            var foundMainBarTargets = 0;
            var foundTaskTargets = 0;
            var foundSmartphoneTargets = 0;
            var foundHeaderTargets = 0;

            // Stronger tint for the visible topbar chrome. These are white-tinted sprites,
            // so a stronger blend is needed than for normal dark panels.
            if (TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar", TopbarUiPinkStrength, out _))
                foundMainBarTargets++;

            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar/Avatar", TopbarUiPinkStrength, out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar/Avatar/DanceButton/Background", TopbarUiPinkStrength, out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar/Avatar/AccessoriesButton", TopbarUiPinkStrength, out _);

            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar/PlayerStats/Hunger", TopbarUiPinkStrength, out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar/PlayerStats/Energy", TopbarUiPinkStrength, out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar/PlayerStats/Happiness", TopbarUiPinkStrength, out _);

            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Buttons/NotificationsButton", TopbarUiPinkStrength, out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Buttons/HelpButton", TopbarUiPinkStrength, out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Buttons/ReportBugButton", TopbarUiPinkStrength, out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Buttons/SettingsButton", TopbarUiPinkStrength, out _);

            // Objectives/tasks: body stays subtly tinted, headline background becomes hot pink.
            if (TryTintExplicitUiGraphic("Canvases", "Tasks/Container/Panel", out _))
                foundTaskTargets++;
            TryTintExplicitUiGraphic("Canvases", "Tasks/ExpandButton/Background", out _);
            if (TrySetExplicitUiGraphicColor("Canvases", "Tasks/Container/Panel/Headline", HeaderLightPink, out _))
                foundHeaderTargets++;
            TrySetExplicitUiGraphicColor("Canvases", "Tasks/Container/Panel/Headline/Label", HeaderDarkText, out _);
            TrySetExplicitUiGraphicColor("Canvases", "Tasks/Container/Panel/Headline/Icon", HeaderDarkText, out _);

            // BizPhone / smartphone: body stays subtly tinted, headline background becomes hot pink.
            if (TryTintExplicitUiGraphic("Canvases", "Smartphone/Container/Phone", out _))
                foundSmartphoneTargets++;
            TryTintExplicitUiGraphic("Canvases", "Smartphone/Container/Phone/Radio", out _);
            TryTintExplicitUiGraphic("Canvases", "Smartphone/Container/Phone/Radio/Splitter", out _);
            if (TrySetExplicitUiGraphicColor("Canvases", "Smartphone/Container/Headline", HeaderLightPink, out _))
                foundHeaderTargets++;
            TrySetExplicitUiGraphicColor("Canvases", "Smartphone/Container/Headline/Title", HeaderDarkText, out _);
            TrySetExplicitUiGraphicColor("Canvases", "Smartphone/Container/Headline/Icon", HeaderDarkText, out _);

            PinkFileLogger.Info($"MAIN_HUD_UI_TINT_PASS foundMainBarTargets={foundMainBarTargets}/1 foundTaskTargets={foundTaskTargets}/1 foundSmartphoneTargets={foundSmartphoneTargets}/1 foundHeaderTargets={foundHeaderTargets}/2");
            return foundMainBarTargets >= 1;
        }

        private static int TryTintExplicitUiGraphic(string rootName, string relativePath)
        {
            return TryTintExplicitUiGraphic(rootName, relativePath, ExplicitUiPinkStrength, out _) ? 1 : 0;
        }

        private static bool TryTintExplicitUiGraphic(string rootName, string relativePath, out bool patched)
        {
            return TryTintExplicitUiGraphic(rootName, relativePath, ExplicitUiPinkStrength, out patched);
        }

        private static bool TryTintExplicitUiGraphic(string rootName, string relativePath, float strength, out bool patched)
        {
            return TryApplyExplicitUiGraphicColor(rootName, relativePath, original => BlendExplicitUiColor(original, strength), out patched);
        }

        private static bool TrySetExplicitUiGraphicColor(string rootName, string relativePath, Color color, out bool patched)
        {
            return TryApplyExplicitUiGraphicColor(rootName, relativePath, original => new Color(color.r, color.g, color.b, original.a), out patched);
        }

        private static bool TryApplyExplicitUiGraphicColor(string rootName, string relativePath, Func<Color, Color> colorMapper, out bool patched)
        {
            patched = false;

            var fullPath = string.IsNullOrEmpty(relativePath) ? rootName : rootName + "/" + relativePath;
            Transform? target = null;

            var direct = GameObject.Find(fullPath);
            if (direct != null)
            {
                target = direct.transform;
            }
            else
            {
                var root = GameObject.Find(rootName);
                if (root != null)
                    target = string.IsNullOrEmpty(relativePath) ? root.transform : root.transform.Find(relativePath);
            }

            if (target == null)
            {
                PinkFileLogger.Verbose($"Explicit UI target not found: {fullPath}");
                return false;
            }

            var graphic = target.GetComponent<Graphic>();
            if (graphic == null)
            {
                PinkFileLogger.Verbose($"Explicit UI target has no Graphic: {fullPath}");
                return false;
            }

            var id = graphic.GetInstanceID();
            if (ExplicitUiPatchedGraphics.TryGetValue(id, out var existingSnapshot))
            {
                // Vanilla can overwrite UI colors after we set them. Re-apply the selected target color.
                graphic.color = existingSnapshot.PatchedColor;
                return true;
            }

            var originalColor = graphic.color;
            var patchedColor = colorMapper(originalColor);
            ExplicitUiPatchedGraphics[id] = new ExplicitUiGraphicSnapshot(graphic, originalColor, patchedColor);
            graphic.color = patchedColor;
            PinkFileLogger.Info($"EXPLICIT_UI_TINT patched path={fullPath}, graphic={graphic.GetType().Name}, name={graphic.name}");
            patched = true;
            return true;
        }

        private static Color BlendExplicitUiColor(Color original, float strength)
        {
            var target = new Color(ExplicitUiPink.r, ExplicitUiPink.g, ExplicitUiPink.b, original.a);
            var blended = Color.Lerp(original, target, strength);
            blended.a = original.a;
            return blended;
        }

        private static int RestoreExplicitUiTint()
        {
            var restored = 0;
            foreach (var snapshot in ExplicitUiPatchedGraphics.Values)
            {
                if (snapshot.Graphic == null)
                    continue;

                snapshot.Graphic.color = snapshot.OriginalColor;
                restored++;
            }

            ExplicitUiPatchedGraphics.Clear();
            return restored;
        }

        private static void ResetExplicitUiTintState()
        {
            ExplicitUiPatchedGraphics.Clear();
        }

        private readonly struct ExplicitUiGraphicSnapshot
        {
            internal ExplicitUiGraphicSnapshot(Graphic graphic, Color originalColor, Color patchedColor)
            {
                Graphic = graphic;
                OriginalColor = originalColor;
                PatchedColor = patchedColor;
            }

            internal Graphic Graphic { get; }
            internal Color OriginalColor { get; }
            internal Color PatchedColor { get; }
        }
    }
}
