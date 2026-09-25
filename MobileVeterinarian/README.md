# Mobile Veterinarian

Mobile Veterinarian adds an in-game phone contact that treats damaged rideable animals at their current location.

The veterinarian treats the animal the player is currently riding. The service never scans for nearby animals and never asks the player to select a target.

## Requirements

- Big Ambitions build 3674
- [Custom NPC API](https://steamcommunity.com/sharedfiles/filedetails/?id=3795463421)
- A supported rideable-animal mod

Support for the cow from Moo-tor Vehicle is included. Mobile Veterinarian does not include the cow itself.

## Using the service

1. Mount a registered rideable animal.
2. Stop the animal in an open area.
3. Open the phone and call **Mobile Veterinarian**.
4. Review the damage-based price and confirm the visit.
5. Close the phone, remain mounted and keep the animal still.

The veterinarian checks both sides of the animal for a safe standing position. Treatment repairs the real vehicle damage state. Payment is taken only after the repair succeeds.

The visit cancels without charge if the player dismounts, moves away, changes scene, unloads the game or loses the target animal.

## Pricing

Version 0.1.0 uses these balance values:

- Base callout fee: $150
- Price per damage percentage point: $8
- Minimum treatment price: $200
- Maximum treatment price: $2,000

## Animal-mod integration

Messages are species-neutral and insert the display name registered by the animal mod. The built-in Moo-tor Vehicle registration supplies “your cow”; another mod can supply “your horse,” “your camel,” or a localized equivalent.

See [MODDER_GUIDE.md](MODDER_GUIDE.md) for the registration API, localization example, treatment-position coordinates, dependency setup and test checklist.

## Diagnostics

Informational diagnostic logging is disabled in the release build. Genuine warnings and errors are still written through the Big Ambitions mod logger for support. No world scan or permanent polling loop is used.

## Credits

The bundled doctor model is “Doctor” by AkshayBorkar, licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Full attribution is included in `Config/ThirdPartyNotices.md`.
