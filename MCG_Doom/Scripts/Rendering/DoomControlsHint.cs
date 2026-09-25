using System;
using UnityEngine;

namespace MCG_Doom.Rendering
{
    internal sealed class DoomControlsHint : IDisposable
    {
        private const float VisibleSeconds = 7.5f;
        private readonly GameObject _root;
        private readonly Material _backgroundMaterial;
        private float _remainingSeconds;

        private DoomControlsHint(GameObject root, Material backgroundMaterial)
        {
            _root = root;
            _backgroundMaterial = backgroundMaterial;
            _remainingSeconds = VisibleSeconds;
        }

        public static DoomControlsHint Create(Transform parent, int layer)
        {
            var root = new GameObject("ControlsHint");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, -4.15f, -0.25f);
            root.layer = layer;

            var material = CreateBackground(root.transform, layer);
            CreateText(root.transform, layer);

            return new DoomControlsHint(root, material);
        }

        public void Tick(float deltaSeconds)
        {
            if (_root == null || !_root.activeSelf)
            {
                return;
            }

            _remainingSeconds -= Math.Max(0f, deltaSeconds);
            if (_remainingSeconds <= 0f)
            {
                _root.SetActive(false);
            }
        }

        public void Dispose()
        {
            if (_backgroundMaterial != null)
            {
                UnityEngine.Object.Destroy(_backgroundMaterial);
            }

            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }
        }

        private static Material CreateBackground(Transform parent, int layer)
        {
            var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "Background";
            background.transform.SetParent(parent, false);
            background.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            background.transform.localScale = new Vector3(14.8f, 1.15f, 1f);
            background.layer = layer;

            var collider = background.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }

            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                throw new InvalidOperationException("No compatible shader was found for the DOOM controls hint.");
            }

            var material = new Material(shader)
            {
                name = "MCG_Doom_ControlsHintMaterial",
                color = new Color(0f, 0f, 0f, 0.78f)
            };

            background.GetComponent<MeshRenderer>().sharedMaterial = material;
            return material;
        }

        private static void CreateText(Transform parent, int layer)
        {
            var textObject = new GameObject("Text");
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            textObject.layer = layer;

            var text = textObject.AddComponent<TextMesh>();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.characterSize = 0.105f;
            text.fontSize = 52;
            text.text = "P = DOOM MENU     E = AUTOMAP\nBACKSPACE = GAME SELECT     TAB = LEAVE PC";
        }
    }
}
