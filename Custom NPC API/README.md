# Custom NPC API

Custom NPC API is a reusable runtime library for Big Ambitions mods. It lets a mod create named NPCs in the game world without implementing the fragile game-facing interaction plumbing itself.

With the API, a modder can:

- spawn a generated humanoid from a compatible vanilla character prefab
- use a custom character prefab from the consuming mod's AssetBundle
- control world position, facing, local rotation, scale, gender, age and appearance seed
- remove unwanted child props from a vanilla prefab
- make the NPC clickable and handle the interaction with a callback
- open a mod-registered Big Ambitions call dialog
- show, hide, find and despawn NPCs at runtime
- create a vanilla phone contact for an NPC and append conversation history
- preview prefabs and appearances in-game with an optional developer window

The API creates and manages the physical NPC. Your mod remains in control of gameplay rules such as quests, schedules, movement, rewards, relationships, map markers and save data.

## Requirements

Custom NPC API must be installed and enabled as its own mod. A consuming mod references the `CustomNPCAPI` assembly and must not bundle a second copy of `CustomNPCAPI.dll`.

In Unity, add `Custom NPC API/CustomNPCAPI.asmdef` to the consuming assembly's
Assembly Definition References. If you edit the `.asmdef` JSON directly, use:

```json
{
  "references": [
    "GUID:94353d1844744b1c85e1418224d759c9"
  ]
}
```

Build and package the consuming mod with the normal Big Ambitions SDK and Unity
workflow. Distribute Custom NPC API as a required separate mod; do not copy
`CustomNPCAPI.dll` into the consuming mod.

## Quick start

Add a fully rendered CTA entry to your mod's locale file. The CTA path accepts a localization key, but no replacement values:

```json
{
  "mymod:cta_talk_alex": "Talk to Alex"
}
```

Spawn the NPC after the city has loaded:

```csharp
using CustomNPCAPI;
using UnityEngine;

private CustomNpcHandle _alex;

private void SpawnAlex()
{
    var definition = new CustomNpcDefinition
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
        LocalEulerAngles = new Vector3(0f, 90f, 0f),
        LocalScale = Vector3.one
    };

    _alex = CustomNpcApi.Spawn(
        "MyModId",
        definition,
        new CustomNpcSpawnOptions
        {
            OnInteract = context =>
            {
                Debug.Log("Interacted with " + context.Definition.Id);
            }
        });
}
```

`Spawn` returns a `CustomNpcHandle`. Keep it while the NPC exists:

```csharp
_alex.SetVisible(false);              // hide visual and interaction
_alex.SetVisible(true);               // show both again
_alex.SetInteractionEnabled(false);   // keep visible, but disable interaction
_alex.Dispose();                      // remove the NPC permanently
_alex = null;
```

Calling `Spawn` again with the same owner-mod ID and NPC ID replaces the existing NPC. A mod can also clean up all NPCs it owns with `CustomNpcApi.DespawnAll("MyModId")`.

See [the Modder Guide](docs/MODDER_GUIDE.md) for field descriptions, dialogs, custom AssetBundle visuals, phone contacts, JSON definitions, lifecycle guidance and the developer preview window.
