using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BAModAPI;
using Dialogs;
using Entities;
using Helpers;
using Player.HUD.ItemInfoOverlays;
using UnityEngine;

namespace CustomNPCAPI
{
    public static class CustomNpcApi
    {
        public const string ApiVersion = "1.0.0";
        public static bool IsHostActive { get; internal set; }
        public static bool DeveloperToolsEnabled { get; set; }

        private static readonly Dictionary<int, CustomNpcHandle> HandlesByControllerId = new Dictionary<int, CustomNpcHandle>();
        private static readonly HashSet<CustomNpcHandle> Handles = new HashSet<CustomNpcHandle>();
        private static bool _ctaInstalled;

        public static IReadOnlyCollection<CustomNpcHandle> ActiveNpcs => Handles.Where(handle => handle != null && !handle.IsDisposed).ToArray();

        public static CustomNpcHandle Spawn(string ownerModId, CustomNpcDefinition definition, CustomNpcSpawnOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(ownerModId))
                throw new ArgumentException("Owner mod id is required.", nameof(ownerModId));
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Id))
                throw new ArgumentException("NPC Id is required.", nameof(definition));

            options = options ?? new CustomNpcSpawnOptions();
            if (TryGetById(ownerModId, definition.Id, out var existing))
                existing.Dispose();

            EnsureCtaBehaviorInstalled();

            var root = CustomNpcFactory.CreateRoot(definition, options.Parent);
            if (root == null)
                return null;

            try
            {
                var hasVisual = CustomNpcFactory.TryAttachVisual(root.transform, definition, options.VisualFactory, out _);
                if (!hasVisual && options.BuildFallbackVisual)
                    CustomNpcFactory.BuildFallbackStandVisual(root.transform, definition);

                Component controller = null;
                if (definition.Interactable)
                    controller = CustomNpcFactory.AttachInteractionHost(root, definition, hasVisual);

                var handle = new CustomNpcHandle(ownerModId, definition.Clone(), root, controller, options);
                Handles.Add(handle);
                if (controller != null)
                    HandlesByControllerId[controller.GetInstanceID()] = handle;

                handle.SetVisible(options.Visible);
                return handle;
            }
            catch
            {
                UnityEngine.Object.Destroy(root);
                throw;
            }
        }

        public static bool TryGetById(string ownerModId, string npcId, out CustomNpcHandle handle)
        {
            handle = Handles.FirstOrDefault(candidate =>
                candidate != null && !candidate.IsDisposed &&
                string.Equals(candidate.OwnerModId, ownerModId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Definition?.Id, npcId, StringComparison.OrdinalIgnoreCase));
            return handle != null;
        }

        public static void DespawnAll(string ownerModId)
        {
            foreach (var handle in Handles.Where(candidate =>
                         candidate != null &&
                         string.Equals(candidate.OwnerModId, ownerModId, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                handle.Dispose();
            }
        }

        public static bool OpenDialog(string dialogTypeKey)
        {
            if (string.IsNullOrWhiteSpace(dialogTypeKey))
                return false;

            try
            {
                var dialogType = (CallDialogType)ModEnumHash.GetSafeHash(dialogTypeKey);
                return CustomNpcReflection.TryOpenDialog(dialogType);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CustomNPCAPI] Failed to open dialog '{dialogTypeKey}'. {exception}");
                return false;
            }
        }

        internal static void ActivateHost()
        {
            IsHostActive = true;
            RemoveCtaBehaviors(includeCurrent: false);
            _ctaInstalled = false;
            EnsureCtaBehaviorInstalled();
        }

        internal static void DeactivateHost()
        {
            foreach (var handle in Handles.ToArray())
                handle?.Dispose();

            Handles.Clear();
            HandlesByControllerId.Clear();
            RemoveCtaBehaviors(includeCurrent: true);
            _ctaInstalled = false;
            IsHostActive = false;
        }

        internal static void Unregister(CustomNpcHandle handle)
        {
            if (handle == null)
                return;

            Handles.Remove(handle);
            if (handle.Controller != null)
                HandlesByControllerId.Remove(handle.Controller.GetInstanceID());
        }

        internal static bool TryGetHandle(EntityController controller, out CustomNpcHandle handle)
        {
            handle = null;
            return controller != null && HandlesByControllerId.TryGetValue(controller.GetInstanceID(), out handle) && handle != null && !handle.IsDisposed;
        }

        private static void RemoveCtaBehaviors(bool includeCurrent)
        {
            try
            {
                var field = typeof(CtaManager).GetField("CtaBehaviors", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var list = field?.GetValue(null) as IList<ICtaBehavior>;
                if (list == null)
                    return;

                for (var index = list.Count - 1; index >= 0; index--)
                {
                    var item = list[index];
                    if (item == null)
                        continue;

                    var isCurrent = item is CustomNpcCtaBehavior;
                    var typeName = item.GetType().FullName ?? string.Empty;
                    var isCustomNpcBehavior = typeName.IndexOf("CustomNPCAPI.CustomNpcApi+CustomNpcCtaBehavior", StringComparison.Ordinal) >= 0;
                    if (isCustomNpcBehavior && (includeCurrent || !isCurrent))
                        list.RemoveAt(index);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CustomNPCAPI] Failed to clean CTA behavior state. {exception}");
            }
        }

        private static void EnsureCtaBehaviorInstalled()
        {
            if (_ctaInstalled)
                return;

            var field = typeof(CtaManager).GetField("CtaBehaviors", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var list = field?.GetValue(null) as IList<ICtaBehavior>;
            if (list == null)
                return;

            if (!list.Any(item => item is CustomNpcCtaBehavior))
                list.Insert(0, new CustomNpcCtaBehavior());

            _ctaInstalled = true;
        }

        private sealed class CustomNpcCtaBehavior : ICtaBehavior
        {
            public override bool ShouldShow(EntityController entityController)
            {
                return TryGetHandle(entityController, out var handle) && handle.Definition.Interactable && handle.InteractionEnabled;
            }

            public override (string, Action) GetCta(EntityController entityController)
            {
                if (!TryGetHandle(entityController, out var handle))
                    return (string.Empty, null);

                var context = handle.CreateInteractionContext();
                var text = ResolveCtaText(handle, context);
                return (text, () => handle.InvokeInteraction());
            }

            private static string ResolveCtaText(CustomNpcHandle handle, CustomNpcInteractionContext context)
            {
                try
                {
                    var customText = handle.Options?.CtaTextResolver?.Invoke(context);
                    if (!string.IsNullOrWhiteSpace(customText))
                        return customText;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[CustomNPCAPI] CTA resolver failed for '{handle.Definition?.Id}'. {exception}");
                }

                var definition = handle.Definition;
                if (!string.IsNullOrWhiteSpace(definition.CtaTextKey))
                    return definition.CtaTextKey;

                // CtaField localizes the value returned by ICtaBehavior. Returning display text
                // makes the game interpret that text as a missing localization key.
                return "customnpcapi:cta_talk";
            }
        }
    }

    public sealed class CustomNpcHandle : IDisposable
    {
        internal CustomNpcHandle(string ownerModId, CustomNpcDefinition definition, GameObject root, Component controller, CustomNpcSpawnOptions options)
        {
            OwnerModId = ownerModId;
            Definition = definition;
            Root = root;
            Controller = controller;
            Options = options;
            CacheComponentStates();
        }

        public string OwnerModId { get; }
        public CustomNpcDefinition Definition { get; }
        public GameObject Root { get; private set; }
        public Component Controller { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool IsVisible { get; private set; }
        public bool InteractionEnabled { get; private set; }
        internal CustomNpcSpawnOptions Options { get; }

        private readonly Dictionary<Renderer, bool> _rendererStates = new Dictionary<Renderer, bool>();
        private readonly Dictionary<Collider, bool> _colliderStates = new Dictionary<Collider, bool>();
        private readonly Dictionary<Animator, bool> _animatorStates = new Dictionary<Animator, bool>();

        public void SetVisible(bool visible)
        {
            if (IsDisposed || Root == null)
                return;

            IsVisible = visible;
            if (!Root.activeSelf)
                Root.SetActive(true);

            foreach (var pair in _rendererStates.ToArray())
                if (pair.Key != null) pair.Key.enabled = visible && pair.Value;
            foreach (var pair in _colliderStates.ToArray())
                if (pair.Key != null) pair.Key.enabled = visible && pair.Value;
            foreach (var pair in _animatorStates.ToArray())
            {
                if (pair.Key == null) continue;
                pair.Key.enabled = visible && pair.Value;
                if (visible && pair.Value) pair.Key.Update(0f);
            }

            SetInteractionEnabled(visible);
        }

        private void CacheComponentStates()
        {
            if (Root == null)
                return;

            foreach (var renderer in Root.GetComponentsInChildren<Renderer>(true))
                if (renderer != null && !_rendererStates.ContainsKey(renderer)) _rendererStates[renderer] = renderer.enabled;
            foreach (var collider in Root.GetComponentsInChildren<Collider>(true))
                if (collider != null && !_colliderStates.ContainsKey(collider)) _colliderStates[collider] = collider.enabled;
            foreach (var animator in Root.GetComponentsInChildren<Animator>(true))
                if (animator != null && !_animatorStates.ContainsKey(animator)) _animatorStates[animator] = animator.enabled;
        }

        public void SetInteractionEnabled(bool enabled)
        {
            if (IsDisposed)
                return;

            InteractionEnabled = enabled && Definition.Interactable;
            if (Controller == null)
                return;

            CustomNpcReflection.SetMemberValue(Controller, "primaryInteractionEnabled", InteractionEnabled);
            if (InteractionEnabled)
                CustomNpcReflection.TryInvokeParameterlessMethod(Controller, "Show");
            else if (!CustomNpcReflection.TryInvokeParameterlessMethod(Controller, "Hide"))
                CustomNpcReflection.SetMemberValue(Controller, "blockOutline", true);
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            SetInteractionEnabled(false);
            IsDisposed = true;
            CustomNpcApi.Unregister(this);
            if (Root != null)
                UnityEngine.Object.Destroy(Root);
            Root = null;
            Controller = null;
        }

        internal CustomNpcInteractionContext CreateInteractionContext()
        {
            return new CustomNpcInteractionContext
            {
                OwnerModId = OwnerModId,
                Definition = Definition,
                Handle = this
            };
        }

        internal void InvokeInteraction()
        {
            if (IsDisposed || !InteractionEnabled)
                return;

            try
            {
                Options?.OnInteract?.Invoke(CreateInteractionContext());
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CustomNPCAPI] Interaction failed for '{Definition?.Id}'. {exception}");
            }
        }
    }
}
