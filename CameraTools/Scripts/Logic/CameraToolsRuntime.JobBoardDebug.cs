#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime : MonoBehaviour
    {
        private static readonly string[] JobBoardUiKeywords =
        {
            "job",
            "board",
            "candidate",
            "candidates",
            "edit",
            "change",
            "text",
            "input",
            "submit",
            "confirm",
            "ok"
        };

        private static readonly string[] JobBoardUiComponentNames =
        {
            "Button",
            "TMP_InputField",
            "InputField",
            "TMP_Text",
            "TextMeshProUGUI",
            "Text"
        };

        private void DumpJobBoardUiDiagnostics(string reason)
        {
            if (settings == null || !settings.EnableCameraToolsDebug || !settings.EnableJobBoardUiDebugLogging)
                return;

            LogJobBoardUiDebug("=== Job board UI diagnostics start ===");
            LogJobBoardUiDebug(
                $"reason={reason}, gameplayActive={IsGameplayActive()}, cityMapOpen={IsCityMapOpen()}, " +
                $"screen={Screen.width}x{Screen.height}, uiHidden={isUiHidden}, scenicView={scenicViewEnabled}");

            LogJobBoardUiCameraState();
            LogJobBoardUiEventSystemState();
            LogJobBoardUiCanvasState();
            LogJobBoardUiCandidates();

            LogJobBoardUiDebug("=== Job board UI diagnostics end ===");
        }

        private void LogJobBoardUiCameraState()
        {
            var liveVirtualCamera = GetLiveVirtualCameraComponent();
            LogJobBoardUiDebug(
                $"liveVcam={(liveVirtualCamera == null ? "none" : GetHierarchyPath(liveVirtualCamera.transform))}, " +
                $"gameplayController={(gameplayController == null ? "none" : GetHierarchyPath(gameplayController.transform))}, " +
                $"gameplayPitch={GetCurrentGameplayPitchForLogging():0.##}, hasManualGameplayPitch={hasManualGameplayPitch}");

            if (gameplayController != null)
            {
                LogJobBoardUiDebug(
                    $"gameplayControllerState: type={gameplayController.GetType().FullName}, enabled={gameplayController.isActiveAndEnabled}, " +
                    $"position={gameplayController.transform.position}, rotation={gameplayController.transform.rotation.eulerAngles}");
            }

            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                LogJobBoardUiDebug(
                    $"Camera.main: path={GetHierarchyPath(mainCamera.transform)}, enabled={mainCamera.enabled}, " +
                    $"position={mainCamera.transform.position}, rotation={mainCamera.transform.rotation.eulerAngles}, " +
                    $"fov={mainCamera.fieldOfView:0.##}, clearFlags={mainCamera.clearFlags}, cullingMask={FormatLayerMask(mainCamera.cullingMask)}");
            }
            else
            {
                LogJobBoardUiDebug("Camera.main: none");
            }

            foreach (var camera in Camera.allCameras)
            {
                if (camera == null || !camera.enabled)
                    continue;

                LogJobBoardUiDebug(
                    $"Enabled camera: path={GetHierarchyPath(camera.transform)}, depth={camera.depth:0.##}, " +
                    $"position={camera.transform.position}, rotation={camera.transform.rotation.eulerAngles}, " +
                    $"fov={camera.fieldOfView:0.##}, clearFlags={camera.clearFlags}");
            }

            if (liveVirtualCamera == null)
                return;

            var follow = TryGetMemberValue(liveVirtualCamera, "Follow", out var followValue) ? followValue : null;
            var lookAt = TryGetMemberValue(liveVirtualCamera, "LookAt", out var lookAtValue) ? lookAtValue : null;
            var priority = TryGetMemberValue(liveVirtualCamera, "Priority", out var priorityValue) ? priorityValue : null;
            LogJobBoardUiDebug(
                $"Live VCAM: type={liveVirtualCamera.GetType().FullName}, enabled={FormatEnabled(liveVirtualCamera)}, " +
                $"priority={FormatMemberValue(priority)}, follow={FormatMemberValue(follow)}, lookAt={FormatMemberValue(lookAt)}");
        }

        private void LogJobBoardUiEventSystemState()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                LogJobBoardUiDebug("EventSystem: none");
                return;
            }

            var selected = eventSystem.currentSelectedGameObject;
            LogJobBoardUiDebug(
                $"EventSystem: path={GetHierarchyPath(eventSystem.transform)}, enabled={eventSystem.isActiveAndEnabled}, " +
                $"alreadySelecting={eventSystem.alreadySelecting}, pointerOverUi={eventSystem.IsPointerOverGameObject()}, " +
                $"selected={(selected == null ? "none" : GetHierarchyPath(selected.transform))}");
        }

        private void LogJobBoardUiCanvasState()
        {
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas == null)
                    continue;

                LogJobBoardUiDebug(
                    $"Canvas: path={GetHierarchyPath(canvas.transform)}, active={canvas.gameObject.activeInHierarchy}, enabled={canvas.enabled}, " +
                    $"renderMode={canvas.renderMode}, sortingOrder={canvas.sortingOrder}, overrideSorting={canvas.overrideSorting}, " +
                    $"worldCamera={FormatMemberValue(canvas.worldCamera)}");
            }
        }

        private void LogJobBoardUiCandidates()
        {
            var keywordCandidates = new List<RectTransform>();
            var offscreenInteractiveCandidates = new List<RectTransform>();

            foreach (var rectTransform in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                if (rectTransform == null)
                    continue;

                if (IsJobBoardKeywordCandidate(rectTransform))
                    keywordCandidates.Add(rectTransform);

                if (IsOffscreenInteractiveCandidate(rectTransform))
                    offscreenInteractiveCandidates.Add(rectTransform);
            }

            LogJobBoardUiDebug($"Keyword candidates found={keywordCandidates.Count}");
            foreach (var rectTransform in keywordCandidates)
                LogJobBoardUiRectTransform("Keyword candidate", rectTransform);

            LogJobBoardUiDebug($"Offscreen interactive candidates found={offscreenInteractiveCandidates.Count}");
            foreach (var rectTransform in offscreenInteractiveCandidates)
                LogJobBoardUiRectTransform("Offscreen interactive", rectTransform);
        }

        private bool IsJobBoardKeywordCandidate(RectTransform rectTransform)
        {
            if (!rectTransform.gameObject.activeInHierarchy)
                return false;

            var path = GetHierarchyPath(rectTransform).ToLowerInvariant();
            if (ContainsAny(path, JobBoardUiKeywords))
                return true;

            var text = GetUiTextSnapshot(rectTransform);
            return !string.IsNullOrWhiteSpace(text) && ContainsAny(text.ToLowerInvariant(), JobBoardUiKeywords);
        }

        private bool IsOffscreenInteractiveCandidate(RectTransform rectTransform)
        {
            if (!rectTransform.gameObject.activeInHierarchy)
                return false;

            if (!HasAnyNamedComponent(rectTransform, JobBoardUiComponentNames))
                return false;

            if (!TryGetScreenRect(rectTransform, out var minX, out var minY, out var maxX, out var maxY))
                return false;

            return minX < -2f ||
                minY < -2f ||
                maxX > Screen.width + 2f ||
                maxY > Screen.height + 2f;
        }

        private void LogJobBoardUiRectTransform(string label, RectTransform rectTransform)
        {
            var builder = new StringBuilder();
            builder.Append(label);
            builder.Append(": path=");
            builder.Append(GetHierarchyPath(rectTransform));
            builder.Append(", active=");
            builder.Append(rectTransform.gameObject.activeInHierarchy);
            builder.Append(", localPosition=");
            builder.Append(rectTransform.localPosition);
            builder.Append(", anchoredPosition=");
            builder.Append(rectTransform.anchoredPosition);
            builder.Append(", sizeDelta=");
            builder.Append(rectTransform.sizeDelta);
            builder.Append(", anchorMin=");
            builder.Append(rectTransform.anchorMin);
            builder.Append(", anchorMax=");
            builder.Append(rectTransform.anchorMax);
            builder.Append(", pivot=");
            builder.Append(rectTransform.pivot);
            builder.Append(", canvas=");
            builder.Append(FormatMemberValue(rectTransform.GetComponentInParent<Canvas>(true)));

            if (TryGetScreenRect(rectTransform, out var minX, out var minY, out var maxX, out var maxY))
            {
                builder.Append(", screenRect=(");
                builder.Append(minX.ToString("0.##"));
                builder.Append(", ");
                builder.Append(minY.ToString("0.##"));
                builder.Append(") -> (");
                builder.Append(maxX.ToString("0.##"));
                builder.Append(", ");
                builder.Append(maxY.ToString("0.##"));
                builder.Append(")");
            }
            else
            {
                builder.Append(", screenRect=n/a");
            }

            var textSnapshot = GetUiTextSnapshot(rectTransform);
            if (!string.IsNullOrWhiteSpace(textSnapshot))
            {
                builder.Append(", text=");
                builder.Append(textSnapshot);
            }

            var interactable = TryGetInteractableSnapshot(rectTransform);
            if (!string.IsNullOrWhiteSpace(interactable))
            {
                builder.Append(", interactable=");
                builder.Append(interactable);
            }

            LogJobBoardUiDebug(builder.ToString());

            var parent = rectTransform.parent as RectTransform;
            if (parent != null)
            {
                LogJobBoardUiDebug(
                    $"  Parent: path={GetHierarchyPath(parent)}, active={parent.gameObject.activeInHierarchy}, anchoredPosition={parent.anchoredPosition}, sizeDelta={parent.sizeDelta}");
            }
        }

        private static bool HasAnyNamedComponent(Component component, string[] componentNames)
        {
            foreach (var componentName in componentNames)
            {
                if (component.GetComponent(componentName) != null)
                    return true;
            }

            return false;
        }

        private static string GetUiTextSnapshot(Component component)
        {
            var values = new List<string>();
            CollectTextValue(component.GetComponent("TMP_Text"), values);
            CollectTextValue(component.GetComponent("TextMeshProUGUI"), values);
            CollectTextValue(component.GetComponent("Text"), values);
            CollectTextValue(component.GetComponent("TMP_InputField"), values);
            CollectTextValue(component.GetComponent("InputField"), values);

            foreach (Transform child in component.transform)
            {
                if (child == null)
                    continue;

                CollectTextValue(child.GetComponent("TMP_Text"), values);
                CollectTextValue(child.GetComponent("TextMeshProUGUI"), values);
                CollectTextValue(child.GetComponent("Text"), values);
                CollectTextValue(child.GetComponent("TMP_InputField"), values);
                CollectTextValue(child.GetComponent("InputField"), values);
            }

            return string.Join(" | ", values.ToArray());
        }

        private static void CollectTextValue(Component? component, List<string> values)
        {
            if (component == null)
                return;

            if (TryGetMemberValue(component, "text", out var textValue) &&
                textValue is string text &&
                !string.IsNullOrWhiteSpace(text) &&
                !values.Contains(text))
            {
                values.Add(text.Trim());
            }

            if (TryGetMemberValue(component, "placeholder", out var placeholderValue) &&
                placeholderValue is Component placeholderComponent &&
                TryGetMemberValue(placeholderComponent, "text", out var placeholderTextValue) &&
                placeholderTextValue is string placeholderText &&
                !string.IsNullOrWhiteSpace(placeholderText) &&
                !values.Contains("placeholder:" + placeholderText))
            {
                values.Add("placeholder:" + placeholderText.Trim());
            }
        }

        private static string TryGetInteractableSnapshot(Component component)
        {
            var details = new List<string>();
            foreach (var componentName in JobBoardUiComponentNames)
            {
                var candidate = component.GetComponent(componentName);
                if (candidate == null)
                    continue;

                if (TryGetMemberValue(candidate, "interactable", out var interactableValue) && interactableValue is bool interactable)
                    details.Add(componentName + "=" + interactable);
                else
                    details.Add(componentName);
            }

            return string.Join(", ", details.ToArray());
        }
    }
}
