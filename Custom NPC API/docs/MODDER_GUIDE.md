# Custom NPC API — Modder Guide

## 1. Purpose and ownership

Custom NPC API provides the physical, interactive NPC layer for a Big Ambitions mod. You describe the final NPC you want, provide an interaction callback, and receive a handle that controls the spawned object.

The public API is intentionally small:

```text
CustomNpcDefinition         appearance, placement and interaction labels
CustomNpcSpawnOptions       callback, parent, custom visual factory and initial state
CustomNpcApi.Spawn          create or replace an NPC
CustomNpcHandle             inspect, show, hide, disable interaction or dispose
CustomNpcApi.OpenDialog     open a dialog type registered by the consuming mod
CustomNpcPhone              create contacts and append phone-message history
```

Your mod owns the gameplay around the NPC. This includes deciding when the NPC exists, tracking quests and relationships, moving the NPC, granting rewards, creating map markers and persisting mod-specific state.

## 2. Add the dependency

Keep Custom NPC API as a separate mod beside the consuming mod:

```text
Assets/Mods/
  Custom NPC API/
  My Mod/
```

Reference the API assembly from the consuming mod's `.asmdef`:

```json
{
  "references": [
    "GUID:94353d1844744b1c85e1418224d759c9"
  ]
}
```

If the consuming assembly uses `overrideReferences`, it still needs the normal Big Ambitions and Unity precompiled references required by its own code.

Use the normal Big Ambitions SDK and Unity workflow to build the consuming mod.
Custom NPC API is a separate runtime dependency: players need both mods enabled,
and the consuming mod should not contain another copy of `CustomNPCAPI.dll`.

## 3. Spawn at the right time

Spawn world NPCs after the city has loaded and the API host is active. `CustomNpcApi.IsHostActive` reports whether the API is ready.

Keep the returned handle and dispose it when the owning runtime unloads:

```csharp
private CustomNpcHandle _alex;

private void CreateNpc()
{
    if (!CustomNpcApi.IsHostActive)
        return;

    _alex = CustomNpcApi.Spawn("MyModId", CreateAlexDefinition(), CreateAlexOptions());
}

private void RemoveNpc()
{
    _alex?.Dispose();
    _alex = null;
}
```

The pair `ownerModId + definition.Id` uniquely identifies an active NPC. Spawning the same pair again disposes the old instance before creating its replacement.

## 4. Define a vanilla humanoid

```csharp
private static CustomNpcDefinition CreateAlexDefinition()
{
    return new CustomNpcDefinition
    {
        Id = "mymod:alex",
        DisplayName = "Alex",
        NameKey = "mymod:alex_name",
        OverlayHeaderKey = "mymod:alex_name",
        CtaTextKey = "mymod:cta_talk_alex",

        PrefabName = "Characters/Homeless",
        Gender = "Male",
        AgeInDays = 35 * 365,
        AppearanceSeed = 104729,

        Position = new Vector3(301.58f, 0.09f, -188.47f),
        Forward = new Vector3(0f, 0f, -1f),
        LocalPosition = Vector3.zero,
        LocalEulerAngles = new Vector3(0f, 90f, 0f),
        LocalScale = Vector3.one,

        Interactable = true
    };
}
```

The important fields are:

| Field | Meaning |
| --- | --- |
| `Id` | Stable ID unique within the owner mod. Prefix it with your mod ID. |
| `DisplayName` | Plain name used by the API and fallback visual. |
| `NameKey` | Localization key for the NPC's name. |
| `OverlayHeaderKey` | Localization key shown by the interaction overlay. Usually the same as `NameKey`. |
| `CtaTextKey` | Localization key for the click action, such as “Talk to Alex.” |
| `PrefabName` | Vanilla prefab name used by the game's `PrefabHelper`. |
| `Position` | World-space spawn position. |
| `Forward` | Flat world-space facing direction. A zero vector falls back safely. |
| `LocalPosition` | Offset applied to the visual beneath the NPC root. |
| `LocalEulerAngles` | Visual rotation correction. Many humanoids need `y = 90`. |
| `LocalScale` | Visual scale. Prefer uniform scaling. |
| `Gender` | Game gender enum name, normally `Male` or `Female`. |
| `AgeInDays` | Age passed to compatible appearance generators. |
| `AppearanceSeed` | Stable seed used to reproduce the same generated appearance. |
| `Interactable` | Whether the API creates a clickable interaction host. |

`GameObjectName` and `VisualObjectName` optionally override the generated hierarchy names. They are useful for diagnostics but should not be used as persistent IDs.

## 5. Choose and tune a prefab

Known vanilla candidates exposed by the developer preview are:

```text
Characters/Homeless
Characters/Pedestrian
Characters/CasinoCustomer
Characters/CinemaTheaterCustomer
Characters/FullServiceCustomer
Characters/GymCustomer
Characters/HairdresserCustomer
Characters/NightclubCustomer
Characters/NightclubOutsidePedestrian
Characters/SelfServiceCustomer
Characters/StreetPerformer
Characters/CarnivalPedestrian
Characters/WaterPedestrian
Characters/DummyHuman
Characters/DummyAi
Characters/HumanDefinitionLow
```

These prefabs do not all contain the same components or default pose. Test the exact prefab and game build you plan to support.

For compatible generated humans, the combination of `Gender`, `AgeInDays` and `AppearanceSeed` produces a stable appearance across reloads. If a prefab does not expose the expected appearance method, the API falls back to that prefab's normal appearance setup.

Some prefabs contain props that do not suit the character. Hide them by exact child-object name:

```csharp
HiddenChildObjectNames = new[]
{
    "DonationBucket",
    "DissambledBox"
};
```

## 6. Place and face the NPC

`Position` is in world space. Capture it while standing at the intended location. `Forward` should normally be horizontal:

```csharp
Position = new Vector3(301.58f, 0.09f, -188.47f),
Forward = new Vector3(0f, 0f, -1f)
```

Correct a sideways visual with `LocalEulerAngles.y`; do not rotate the world position to compensate:

```csharp
LocalEulerAngles = new Vector3(0f, 90f, 0f)
```

Uniform scale avoids distortion:

```csharp
LocalScale = Vector3.one;
LocalScale = Vector3.one * 0.75f;
```

The remaining offset and collider fields are advanced tuning controls. Their defaults work for common humanoids:

- `NavTargetLocalOffset` controls where the player navigates for interaction.
- `ColliderCenterWithPrefab` and `ColliderSizeWithPrefab` define the clickable volume when a visual exists.
- `ColliderCenterFallback` and `ColliderSizeFallback` define it for the generated fallback stand.
- `InteractionRendererLocalPosition` and `InteractionRendererLocalScale` position the invisible selection proxy.
- `SellerPositionLocalOffset` is used by the fallback interaction host when no visual was created.

Change these only when testing shows that the interaction target or clickable area is misplaced.

## 7. Handle physical interaction

Supply `OnInteract` in `CustomNpcSpawnOptions`:

```csharp
private static CustomNpcSpawnOptions CreateAlexOptions()
{
    return new CustomNpcSpawnOptions
    {
        OnInteract = context =>
        {
            Debug.Log($"Clicked {context.Definition.Id} owned by {context.OwnerModId}");

            // Run mod-owned quest, shop, dialog or other gameplay logic here.
        }
    };
}
```

The context exposes:

- `OwnerModId`
- the cloned `CustomNpcDefinition`
- the NPC's `CustomNpcHandle`
- `Root`, the spawned root `GameObject`

Exceptions thrown by the callback are caught and logged by the API so they do not break the shared CTA handler.

### CTA localization

The game localizes the string returned by the CTA handler. Therefore `CtaTextKey` must be a registered localization key, not display text:

```json
{
  "mymod:cta_talk_alex": "Talk to Alex"
}
```

```csharp
CtaTextKey = "mymod:cta_talk_alex";
```

That game UI path does not accept replacement data, so a value such as `"Talk to {npcname}"` cannot receive an `npcname` argument. Create a fully rendered locale entry for each NPC. If no key is supplied, the API uses `customnpcapi:cta_talk`, which displays “Talk.”

`CtaTextResolver` may choose a localization key at click time:

```csharp
CtaTextResolver = context => IsShopOpen
    ? "mymod:cta_shop_alex"
    : "mymod:cta_talk_alex";
```

Return `null` or an empty string to use the definition's `CtaTextKey`. `CtaTextFallback` is retained as a legacy data field but is not used for CTA rendering; plain fallback text would be treated as a missing localization key by the game.

## 8. Open a registered dialog

The API can open a `CallDialogType`, but the consuming mod must define and register that dialog first:

```csharp
var dialogType = (CallDialogType)ModEnumHash.GetSafeHash("mymod_alex_dialog");
CallDialogFactory.RegisterDialog(dialogType, () => new AlexDialog());
```

Then route the physical interaction to it:

```csharp
OnInteract = _ =>
{
    if (!CustomNpcApi.OpenDialog("mymod_alex_dialog"))
        Debug.LogWarning("Alex dialog could not be opened.");
};
```

`OpenDialog` returns `false` for an empty key or when the dialog cannot be opened.

## 9. Use a custom AssetBundle visual

`PrefabName` uses the game's vanilla `PrefabHelper`. For a prefab shipped by the consuming mod, provide a `VisualFactory` instead:

```csharp
var options = new CustomNpcSpawnOptions
{
    VisualFactory = parent =>
    {
        return AssetService.Spawn(
            context.ModId,
            "AssetBundles/mymod.unity3d",
            "Assets/Mods/My Mod/Prefabs/Alex.prefab",
            Vector3.zero,
            Quaternion.identity,
            parent,
            false,
            true,
            true);
    },
    OnInteract = _ => CustomNpcApi.OpenDialog("mymod_alex_dialog")
};

var handle = CustomNpcApi.Spawn(context.ModId, definition, options);
```

The factory receives the NPC root transform and must return the spawned visual `GameObject`, or `null` when creation fails. AssetBundle loading, keys and source paths remain the responsibility of the consuming mod.

`BuildFallbackVisual` defaults to `true`. If neither the vanilla prefab nor `VisualFactory` produces a visual, the API creates a simple labelled stand so the failed asset is obvious and the interaction remains testable. Set it to `false` if an invisible NPC is preferable.

## 10. Control lifecycle and visibility

The handle exposes `Root`, `Controller`, `Definition`, `OwnerModId`, `IsVisible`, `InteractionEnabled` and `IsDisposed` for inspection.

Common operations:

```csharp
handle.SetVisible(false);            // disables renderers, colliders and interaction
handle.SetVisible(true);             // restores the captured component states
handle.SetInteractionEnabled(false); // leaves the visual visible
handle.SetInteractionEnabled(true);
```

Find an active NPC without keeping the original handle:

```csharp
if (CustomNpcApi.TryGetById("MyModId", "mymod:alex", out var handle))
    handle.SetVisible(true);
```

Inspect all API-owned NPCs with `CustomNpcApi.ActiveNpcs`. Filter by `OwnerModId` before changing instances belonging to a particular mod.

Clean up explicitly:

```csharp
handle.Dispose();
CustomNpcApi.DespawnAll("MyModId");
```

Do not keep using a handle after `Dispose`. The API also removes active NPCs when its host unloads, but each consuming mod should still clean up the NPCs it owns.

## 11. Add a phone contact

Create or retrieve a vanilla contact and assign the same registered dialog used by physical interaction:

```csharp
var contact = CustomNpcPhone.EnsureContact(new CustomNpcPhoneDefinition
{
    ContactId = "mymod:alex_contact",
    DescriptionKey = "mymod:alex_contact_description",
    ContactCategory = "General",
    DialogTypeKey = "mymod_alex_dialog"
});
```

`ContactId` must be stable. `ContactCategory` is parsed as a vanilla `ContactCategoryName`; an invalid or empty value falls back to `General`.

Find an existing contact:

```csharp
var existing = CustomNpcPhone.FindContact("mymod:alex_contact");
```

Append localized conversation history:

```csharp
CustomNpcPhone.AppendNpcMessage(contact, "mymod:alex_message_1", true);
CustomNpcPhone.AppendPlayerMessage(contact, "mymod:alex_reply_1");
```

Message arguments are localization keys. The optional Boolean on `AppendNpcMessage` controls whether the game sends an immediate notification. Contact, dialog-override and history changes mark the save as changed.

The API does not decide when a player earns a contact. Call `EnsureContact` from the consuming mod when its own progression rules allow it.

## 12. Load definitions from JSON

`CustomNpcDefinition` is Unity-serializable and includes convenience methods:

```csharp
var definition = CustomNpcDefinition.FromJson(jsonText);
var editableCopy = definition.Clone();
var json = editableCopy.ToJson(prettyPrint: true);
```

`FromJson` throws `ArgumentException` for empty or invalid input. Validate the required `Id` before calling `Spawn`; `Spawn` rejects an empty owner-mod ID, null definition or empty NPC ID.

`Clone` copies the definition and its `HiddenChildObjectNames` array. The handle stores a clone, so later changes to the original definition do not rewrite the already spawned NPC.

## 13. Use the developer preview

The API includes an optional in-game preview window. It is disabled by default and should normally stay disabled in releases.

Enable it from development-only code:

```csharp
CustomNpcApi.DeveloperToolsEnabled = true;
```

Press `F7` to toggle the window. It provides:

- a list of active API NPCs grouped by owner-mod ID
- a `Go` button that moves the player near an active NPC
- live player position and forward direction
- capture and clipboard helpers for placement
- selection from the known vanilla prefab candidates
- gender, age, appearance-seed, uniform-scale and local-yaw controls
- spawn, refresh and despawn controls for a preview NPC
- JSON output for the selected appearance and placement

Suggested workflow:

1. Stand at the exact NPC location.
2. Press `F7` and select `Capture player transform`.
3. Move away so the target location is visible.
4. Choose a prefab and tune gender, age, seed, scale and local yaw.
5. Select `Spawn / Refresh` until the result looks correct.
6. Select `Copy NPC JSON`.
7. Paste the appearance and placement into the consuming mod's definition.
8. Give the definition a stable `Id`, localization keys and interaction callback.
9. Disable `DeveloperToolsEnabled` before release.

The preview is temporary, does not save its NPC, and is not required by production NPCs.

## 14. Scope and troubleshooting

Custom NPC API does not implement:

- quest or objective systems
- story flags, favor or rewards
- daily schedules or waypoint movement
- ambient speech bubbles
- map markers or filters
- apartment/interior routing
- custom inventory or collectible rules
- consuming-mod save formats

These features can be built independently and use `Spawn`, `SetVisible`, `SetInteractionEnabled` and `Dispose` as their physical-NPC boundary.

If an NPC does not appear or behave correctly:

1. Confirm both mods are enabled and `CustomNpcApi.IsHostActive` is true.
2. Confirm `ownerModId` and `Id` are non-empty.
3. Test with `Characters/Homeless` and default scale to separate prefab issues from placement issues.
4. Use the F7 preview to verify position, facing, local yaw and appearance values.
5. Confirm `CtaTextKey`, `NameKey` and `OverlayHeaderKey` exist in the consuming mod's locale file.
6. Confirm the callback was supplied in `CustomNpcSpawnOptions` and `Interactable` is true.
7. For custom visuals, confirm the AssetBundle is loaded and `VisualFactory` returns the spawned object.
8. Dispose stale handles before scene/runtime teardown.

This boundary keeps the shared dependency focused: it handles the game-facing physical NPC and interaction integration, while each consuming mod remains free to implement its own gameplay.
