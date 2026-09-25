using System;
using UnityEngine;

namespace CustomNPCAPI
{
    /// <summary>
    /// Small, game-facing NPC definition. Quest state, schedules and story logic deliberately
    /// stay in the consuming mod; this type only describes how an NPC should exist in the world.
    /// </summary>
    [Serializable]
    public sealed class CustomNpcDefinition
    {
        public string Id;
        public string DisplayName;
        public string NameKey;
        public string PrefabName = "Characters/Homeless";
        public string GameObjectName;
        public string VisualObjectName;
        public string OverlayHeaderKey;
        public string CtaTextKey;
        public string CtaTextFallback;

        public bool Interactable = true;
        public string Gender = "Male";
        public int AgeInDays = 42 * 365;
        public int AppearanceSeed = 104729;

        public Vector3 Position;
        public Vector3 Forward = Vector3.forward;
        public Vector3 LocalPosition = Vector3.zero;
        public Vector3 LocalEulerAngles = Vector3.zero;
        public Vector3 LocalScale = Vector3.one;

        public Vector3 NavTargetLocalOffset = new Vector3(0f, 0f, 1.25f);
        public Vector3 SellerPositionLocalOffset = new Vector3(0f, 0f, -0.85f);
        public Vector3 ColliderCenterWithPrefab = new Vector3(0f, 1.05f, -0.05f);
        public Vector3 ColliderSizeWithPrefab = new Vector3(1.3f, 2.1f, 0.55f);
        public Vector3 ColliderCenterFallback = new Vector3(0f, 0.95f, 0f);
        public Vector3 ColliderSizeFallback = new Vector3(1.8f, 1.9f, 1.2f);
        public Vector3 InteractionRendererLocalPosition = new Vector3(0f, 0.9f, 0f);
        public Vector3 InteractionRendererLocalScale = new Vector3(0.08f, 0.08f, 0.08f);
        public string[] HiddenChildObjectNames = Array.Empty<string>();

        public CustomNpcDefinition Clone()
        {
            var clone = (CustomNpcDefinition)MemberwiseClone();
            clone.HiddenChildObjectNames = HiddenChildObjectNames != null
                ? (string[])HiddenChildObjectNames.Clone()
                : Array.Empty<string>();
            return clone;
        }

        /// <summary>Deserialize a definition from Unity-compatible JSON.</summary>
        public static CustomNpcDefinition FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("NPC JSON must not be empty.", nameof(json));

            var definition = JsonUtility.FromJson<CustomNpcDefinition>(json);
            if (definition == null)
                throw new ArgumentException("NPC JSON could not be deserialized.", nameof(json));
            return definition;
        }

        /// <summary>Serialize this definition to Unity-compatible JSON.</summary>
        public string ToJson(bool prettyPrint = true) => JsonUtility.ToJson(this, prettyPrint);
    }

    public sealed class CustomNpcSpawnOptions
    {
        /// <summary>Optional parent. When omitted, Custom NPC API tries GameManager/ItemsContainer.</summary>
        public Transform Parent;

        /// <summary>
        /// Optional custom prefab factory for AssetBundle-owned characters. Vanilla PrefabHelper
        /// is used when this is null.
        /// </summary>
        public Func<Transform, GameObject> VisualFactory;

        /// <summary>Called by the Big Ambitions CTA when the physical NPC is clicked.</summary>
        public Action<CustomNpcInteractionContext> OnInteract;

        /// <summary>
        /// Optional runtime CTA localization-key resolver. The game localizes the returned value;
        /// return null/empty to use CtaTextKey or the API fallback key.
        /// </summary>
        public Func<CustomNpcInteractionContext, string> CtaTextResolver;

        public bool Visible = true;
        public bool BuildFallbackVisual = true;
    }

    public sealed class CustomNpcInteractionContext
    {
        public string OwnerModId { get; internal set; }
        public CustomNpcDefinition Definition { get; internal set; }
        public CustomNpcHandle Handle { get; internal set; }
        public GameObject Root => Handle?.Root;
    }
}
