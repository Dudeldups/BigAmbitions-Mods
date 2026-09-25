# Mobile Veterinarian integration guide

Mobile Veterinarian exposes a small public registry for rideable-animal vehicle mods. The service continues to use Big Ambitions' real mounted-vehicle, damage, repair and money systems; the animal mod only describes which vehicle type is eligible and how it should be presented.

## 1. Add the dependency

Add the `MobileVeterinarian` assembly definition as a reference to the animal mod's Assembly Definition in Unity. This makes the public types in the `MobileVeterinarian` namespace available.

For this repository's external build, add the dependency to the animal mod's entry in `tools/external-build/mods.externalbuild.json`:

```json
"dependencies": [
  "MobileVeterinarian"
]
```

When distributing through Steam Workshop, list Mobile Veterinarian as a required item after its Workshop item exists. Mobile Veterinarian itself depends on [Custom NPC API](https://steamcommunity.com/sharedfiles/filedetails/?id=3795463421).

Mobile Veterinarian does not reference or require a compile-time dependency on the consuming animal mod.

## 2. Add a localized animal name

Add a key to the animal mod's English locale. The value should read naturally wherever `{animal}` appears in a sentence:

```json
{
  "myhorses:mobile_vet_animal_name": "your horse"
}
```

Register the localization key rather than hard-coding English. If the optional name is omitted, Mobile Veterinarian first tries the vehicle type's localized display name and otherwise uses the neutral localized fallback “your animal.”

## 3. Register on load

Use the exact `VehicleType` name stored in the animal's `VehicleInstance`:

```csharp
using System.Threading.Tasks;
using BAModAPI;
using MobileVeterinarian;
using UnityEngine;

private const string AnimalVehicleType = "myhorses:vehicletype_horse";

public Task OnLoadAsync(ModContext context)
{
    AnimalVehicleRegistry.Register(
        AnimalVehicleType,
        "myhorses:mobile_vet_animal_name",
        new Vector3(1.45f, 0f, 0.25f));

    return Task.CompletedTask;
}

public Task OnUnloadAsync()
{
    AnimalVehicleRegistry.Unregister(AnimalVehicleType);
    return Task.CompletedTask;
}
```

Calling `Register` again for the same vehicle type replaces the previous registration. Register once during mod load; do not register in `Update` or scan the world.

## 4. Choose the treatment position

`TreatmentPositionOffset` is relative to the animal vehicle transform:

- `x`: right side of the animal; use a positive value for the preferred side
- `y`: vertical offset; normally `0`
- `z`: forward along the animal

The default is `(1.75, 0, -0.2)`. Put the point just outside the animal's collider with enough room for a standing person. Mobile Veterinarian tests the preferred side first and automatically mirrors the `x` coordinate to test the opposite side if necessary. It can then try front and rear fallback points.

The position must resolve to a safe NavMesh point. The registration does not need to provide a path, NPC prefab, animation, repair callback or payment callback.

## 5. What the service validates

At call time Mobile Veterinarian directly resolves the player's controlled vehicle and verifies that:

- a vehicle is currently controlled by the player
- its exact vehicle type is registered
- it has real vehicle damage
- it is stationary or nearly stationary
- no other veterinarian visit is active
- the player can afford the calculated price
- a safe standing point exists beside it

The player remains mounted throughout treatment. Repair and payment occur only after the visible veterinarian sequence completes.

## 6. Integration test checklist

- The phone rejects the vehicle when the registration is absent.
- The phone accepts it after registration.
- Every message uses the registered animal name, not an internal vehicle key.
- The veterinarian can use either side of the animal.
- Undamaged and moving animals are rejected without charge.
- Dismounting or driving away cancels without charge.
- Successful treatment fully clears vehicle damage and charges exactly once.
- Unloading the animal mod unregisters its vehicle type.
