using System;
using System.Collections.Generic;
using System.Reflection;
using BigAmbitions.Characters;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

namespace CustomNPCAPI
{
    internal static class CustomNpcFactory
    {
        private static readonly BindingFlags ReflectionFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type _homelessType;
        private static Type _appearanceSetterType;
        private static Type _baseHumanType;
        private static Type _sellerStandControllerType;
        private static Material _invisibleProxyMaterial;
        private static Transform _itemsContainer;

        internal static GameObject CreateRoot(CustomNpcDefinition definition, Transform parent)
        {
            var root = new GameObject(string.IsNullOrWhiteSpace(definition.GameObjectName)
                ? $"CustomNPCAPI.{definition.Id}"
                : definition.GameObjectName);

            parent = parent != null ? parent : ResolveItemsContainer();
            if (parent != null)
                root.transform.SetParent(parent, false);

            root.transform.position = definition.Position;
            var forward = Flatten(definition.Forward);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            root.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            return root;
        }

        internal static bool TryAttachVisual(Transform parent, CustomNpcDefinition definition, Func<Transform, GameObject> visualFactory, out GameObject visualRoot)
        {
            visualRoot = null;
            if (parent == null || definition == null)
                return false;

            try
            {
                visualRoot = visualFactory != null
                    ? visualFactory(parent)
                    : (!string.IsNullOrWhiteSpace(definition.PrefabName) ? PrefabHelper.CreatePrefab(definition.PrefabName, parent) : null);
                if (visualRoot == null)
                    return false;

                visualRoot.name = string.IsNullOrWhiteSpace(definition.VisualObjectName)
                    ? $"{definition.DisplayName ?? definition.Id}Visual"
                    : definition.VisualObjectName;
                visualRoot.transform.SetParent(parent, false);
                visualRoot.SetActive(true);
                visualRoot.transform.localPosition = definition.LocalPosition;
                visualRoot.transform.localRotation = Quaternion.Euler(definition.LocalEulerAngles);
                visualRoot.transform.localScale = definition.LocalScale;

                DisablePhysics(visualRoot);
                EnableRenderers(visualRoot);
                InitializeHumanoidVisual(visualRoot, definition);
                StripChildren(visualRoot, definition.HiddenChildObjectNames);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CustomNPCAPI] Failed to attach visual for '{definition.Id}'. {exception}");
                visualRoot = null;
                return false;
            }
        }

        internal static Component AttachInteractionHost(GameObject root, CustomNpcDefinition definition, bool hasVisual)
        {
            var type = ResolveSellerStandControllerType();
            if (type == null)
                throw new InvalidOperationException("SellerStandController could not be resolved in the current game build.");

            var interactionRenderer = CreateInteractionRendererProxy(root.transform, definition);
            if (interactionRenderer == null)
                throw new InvalidOperationException("Interaction proxy renderer could not be created.");

            AddInteractionCollider(root, definition, hasVisual);
            var navTarget = new GameObject("NavMeshTarget").transform;
            navTarget.SetParent(root.transform, false);
            navTarget.localPosition = definition.NavTargetLocalOffset;

            var controller = (Component)root.AddComponent(type);
            CustomNpcReflection.SetMemberValue(controller, "primaryInteractionEnabled", true);
            CustomNpcReflection.SetMemberValue(controller, "simpleOverlayType", 4);
            CustomNpcReflection.SetMemberValue(controller, "detailedOverlayType", 1024);
            CustomNpcReflection.SetMemberValue(controller, "customOverlayHeaderKey", definition.OverlayHeaderKey ?? definition.NameKey ?? string.Empty);
            CustomNpcReflection.SetMemberValue(controller, "blockOutline", true);
            CustomNpcReflection.SetMemberValue(controller, "renderers", new[] { interactionRenderer });
            CustomNpcReflection.SetMemberValue(controller, "navMeshTargets", new[] { navTarget });
            CustomNpcReflection.SetMemberValue(controller, "itemsToSell", new[] { "ba:itemname_hotdog" });

            if (!hasVisual)
            {
                var sellerPosition = new GameObject("SellerPosition").transform;
                sellerPosition.SetParent(root.transform, false);
                sellerPosition.localPosition = definition.SellerPositionLocalOffset;
                CustomNpcReflection.SetMemberValue(controller, "sellerPosition", sellerPosition);
            }

            CustomNpcReflection.TryInvokeParameterlessMethod(controller, "Show");
            return controller;
        }

        internal static void BuildFallbackStandVisual(Transform parent, CustomNpcDefinition definition)
        {
            if (parent == null || definition == null)
                return;

            var countertop = Block(
                parent,
                "Countertop",
                new Vector3(0f, 0.9f, 0f),
                new Vector3(1.7f, 0.16f, 0.7f),
                new Color(0.33f, 0.24f, 0.16f));
            Block(countertop.transform, "CounterAccent", new Vector3(0f, -0.09f, 0f), new Vector3(1.78f, 0.03f, 0.78f), new Color(0.88f, 0.76f, 0.34f));
            Block(parent, "CrateBase", new Vector3(0f, 0.38f, 0f), new Vector3(1.55f, 0.72f, 0.62f), new Color(0.18f, 0.16f, 0.14f));
            Block(parent, "SignPostLeft", new Vector3(-0.58f, 1.4f, -0.18f), new Vector3(0.08f, 1f, 0.08f), new Color(0.22f, 0.18f, 0.12f));
            Block(parent, "SignPostRight", new Vector3(0.58f, 1.4f, -0.18f), new Vector3(0.08f, 1f, 0.08f), new Color(0.22f, 0.18f, 0.12f));
            Block(parent, "SignBoard", new Vector3(0f, 1.75f, -0.18f), new Vector3(1.28f, 0.5f, 0.08f), new Color(0.75f, 0.69f, 0.52f));

            var label = new GameObject("NpcLabel");
            label.transform.SetParent(parent, false);
            label.transform.localPosition = new Vector3(0f, 1.75f, -0.24f);
            var text = label.AddComponent<TextMesh>();
            text.text = (definition.DisplayName ?? definition.Id ?? "NPC").ToUpperInvariant();
            text.fontSize = 72;
            text.characterSize = 0.06f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(0.1f, 0.08f, 0.05f);
        }

        private static Renderer CreateInteractionRendererProxy(Transform parent, CustomNpcDefinition definition)
        {
            var proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            proxy.name = "InteractionRendererProxy";
            proxy.transform.SetParent(parent, false);
            proxy.transform.localPosition = definition.InteractionRendererLocalPosition;
            proxy.transform.localScale = definition.InteractionRendererLocalScale;
            var collider = proxy.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
            var renderer = proxy.GetComponent<Renderer>();
            if (renderer == null) return null;
            renderer.forceRenderingOff = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            var material = InvisibleProxyMaterial();
            if (material != null) renderer.sharedMaterial = material;
            return renderer;
        }

        private static void AddInteractionCollider(GameObject root, CustomNpcDefinition definition, bool hasVisual)
        {
            var collider = root.AddComponent<BoxCollider>();
            collider.center = hasVisual ? definition.ColliderCenterWithPrefab : definition.ColliderCenterFallback;
            collider.size = hasVisual ? definition.ColliderSizeWithPrefab : definition.ColliderSizeFallback;
        }

        private static void DisablePhysics(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var rigidbody in root.GetComponentsInChildren<Rigidbody>(true)) rigidbody.isKinematic = true;
        }

        private static void EnableRenderers(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                renderer.enabled = true;
                if (!renderer.gameObject.activeSelf) renderer.gameObject.SetActive(true);
            }
        }

        private static void InitializeHumanoidVisual(GameObject root, CustomNpcDefinition definition)
        {
            try
            {
                var homeless = Resolve(ref _homelessType, "Entities.Homeless", "Homeless");
                var homelessComponent = homeless != null ? root.GetComponent(homeless) : null;
                if (homelessComponent != null)
                {
                    CustomNpcReflection.TryInvokeParameterlessMethod(homelessComponent, "Init");
                    CustomNpcReflection.TryInvokeParameterlessMethod(homelessComponent, "Enable");
                }

                var appearanceType = Resolve(ref _appearanceSetterType, "AppearanceSetter");
                var appearance = appearanceType != null ? root.GetComponent(appearanceType) : null;
                if (appearance != null) ApplyAppearance(appearance, definition);

                var humanType = Resolve(ref _baseHumanType, "BaseHuman");
                var human = humanType != null ? root.GetComponent(humanType) : null;
                if (human != null) CustomNpcReflection.TryInvokeParameterlessMethod(human, "ResetAnimator");

                foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                {
                    if (animator == null) continue;
                    animator.enabled = true;
                    animator.Rebind();
                    animator.Update(0f);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CustomNPCAPI] Humanoid initialization failed for '{definition.Id}'. {exception}");
            }
        }

        private static void ApplyAppearance(Component appearanceSetter, CustomNpcDefinition definition)
        {
            if (appearanceSetter == null)
                return;

            try
            {
                var method = appearanceSetter.GetType().GetMethod("SetRandomAppearance", ReflectionFlags, null,
                    new[] { typeof(Gender), typeof(int), typeof(int) }, null);
                if (method != null)
                {
                    var gender = !string.IsNullOrWhiteSpace(definition?.Gender) && Enum.TryParse(definition.Gender, true, out Gender parsed)
                        ? parsed
                        : Gender.Male;
                    var ageInDays = definition != null && definition.AgeInDays > 0 ? definition.AgeInDays : 42 * 365;
                    var appearanceSeed = definition != null && definition.AppearanceSeed != 0 ? definition.AppearanceSeed : 104729;
                    method.Invoke(appearanceSetter, new object[] { gender, ageInDays, appearanceSeed });
                    return;
                }

                CustomNpcReflection.TryInvokeParameterlessMethod(appearanceSetter, "SetAppearance");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CustomNPCAPI] Failed to apply configured character appearance. {exception}");
                CustomNpcReflection.TryInvokeParameterlessMethod(appearanceSetter, "SetAppearance");
            }
        }

        private static void StripChildren(GameObject root, string[] names)
        {
            if (names == null || names.Length == 0) return;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                foreach (var transform in transforms)
                {
                    if (transform != null && transform != root.transform && string.Equals(transform.name, name, StringComparison.OrdinalIgnoreCase))
                        UnityEngine.Object.Destroy(transform.gameObject);
                }
            }
        }

        private static Transform ResolveItemsContainer()
        {
            if (_itemsContainer != null) return _itemsContainer;

            var transforms = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (var transform in transforms)
            {
                if (transform == null) continue;
                var path = HierarchyPath(transform);
                if (string.Equals(path, "GameManager/ItemsContainer", StringComparison.OrdinalIgnoreCase))
                    return _itemsContainer = transform;
            }

            // Keep StreetQuest's proven fallback for game builds/scenes where the full hierarchy
            // is not available yet but the shared items container itself already exists.
            foreach (var transform in transforms)
            {
                if (transform != null && string.Equals(transform.name, "ItemsContainer", StringComparison.OrdinalIgnoreCase))
                    return _itemsContainer = transform;
            }

            return null;
        }

        private static Type ResolveSellerStandControllerType()
        {
            return Resolve(ref _sellerStandControllerType, "SellerStandController");
        }

        private static Type Resolve(ref Type cache, params string[] names)
        {
            if (cache != null) return cache;
            foreach (var name in names)
            {
                cache = CustomNpcReflection.FindType(name);
                if (cache != null) return cache;
            }
            return null;
        }

        private static Material InvisibleProxyMaterial()
        {
            if (_invisibleProxyMaterial != null) return _invisibleProxyMaterial;
            var material = RuntimeMaterial(new Color(0f, 0f, 0f, 0f));
            if (material == null) return null;
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
            return _invisibleProxyMaterial = material;
        }

        private static GameObject Block(Transform parent, string name, Vector3 pos, Vector3 scale, Color color)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name; block.transform.SetParent(parent, false); block.transform.localPosition = pos; block.transform.localScale = scale;
            var collider = block.GetComponent<Collider>(); if (collider != null) UnityEngine.Object.Destroy(collider);
            var renderer = block.GetComponent<Renderer>(); if (renderer != null) renderer.sharedMaterial = RuntimeMaterial(color);
            return block;
        }

        private static Material RuntimeMaterial(Color color)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;
            var material = new Material(shader);
            if (material.HasProperty("_Color")) material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            return material;
        }

        private static Vector3 Flatten(Vector3 direction) { direction.y = 0f; return direction.normalized; }
        private static string HierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent) names.Push(current.name);
            return string.Join("/", names);
        }
    }
}
