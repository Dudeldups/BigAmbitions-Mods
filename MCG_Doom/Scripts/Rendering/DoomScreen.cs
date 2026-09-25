using System;
using UnityEngine;

namespace MCG_Doom.Rendering
{
    internal sealed class DoomScreen : IDisposable
    {
        private const int Layer = 5;
        private readonly GameObject _root;
        private readonly Material _material;
        private readonly TextMesh _errorText;
        private readonly DoomControlsHint _controlsHint;

        private DoomScreen(GameObject root, Camera camera, DoomFrameBuffer frameBuffer, Material material, TextMesh errorText, DoomControlsHint controlsHint)
        {
            _root = root;
            Camera = camera;
            FrameBuffer = frameBuffer;
            _material = material;
            _errorText = errorText;
            _controlsHint = controlsHint;
        }

        public Camera Camera { get; }
        public DoomFrameBuffer FrameBuffer { get; }

        public static DoomScreen Create(Transform parent)
        {
            var root = new GameObject("MCG_Doom_Screen");
            root.transform.SetParent(parent, false);
            SetLayerRecursively(root, Layer);

            var frameBuffer = new DoomFrameBuffer(320, 200);
            var camera = CreateCamera(root.transform);
            var material = CreateDisplay(root.transform, frameBuffer.Texture);
            var errorText = CreateErrorText(root.transform);
            var controlsHint = DoomControlsHint.Create(root.transform, Layer);

            return new DoomScreen(root, camera, frameBuffer, material, errorText, controlsHint);
        }

        public void Tick(float deltaSeconds)
        {
            _controlsHint?.Tick(deltaSeconds);
        }

        public void ShowError(string message)
        {
            if (_errorText != null)
            {
                _errorText.text = message;
            }
        }

        public void Dispose()
        {
            if (Camera != null)
            {
                Camera.targetTexture = null;
                Camera.enabled = false;
            }

            FrameBuffer?.Dispose();
            _controlsHint?.Dispose();

            if (_material != null)
            {
                UnityEngine.Object.Destroy(_material);
            }

            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }
        }

        private static Camera CreateCamera(Transform parent)
        {
            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            cameraObject.layer = Layer;

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << Layer;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 50f;
            return camera;
        }

        private static Material CreateDisplay(Transform parent, Texture texture)
        {
            var display = GameObject.CreatePrimitive(PrimitiveType.Quad);
            display.name = "Display";
            display.transform.SetParent(parent, false);
            display.transform.localScale = new Vector3(16f, 10f, 1f);
            SetLayerRecursively(display, Layer);

            var collider = display.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }

            var shader =
                Shader.Find("Unlit/Texture") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Sprites/Default");

            if (shader == null)
            {
                throw new InvalidOperationException("No compatible unlit shader was found for the DOOM display.");
            }

            var material = new Material(shader)
            {
                name = "MCG_Doom_DisplayMaterial",
                mainTexture = texture
            };

            display.GetComponent<MeshRenderer>().sharedMaterial = material;
            return material;
        }

        private static TextMesh CreateErrorText(Transform parent)
        {
            var textObject = new GameObject("ErrorText");
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.1f);
            textObject.layer = Layer;

            var text = textObject.AddComponent<TextMesh>();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.characterSize = 0.16f;
            text.fontSize = 42;
            text.text = string.Empty;
            return text;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
