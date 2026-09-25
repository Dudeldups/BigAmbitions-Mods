[h1]Mobile Veterinarian[/h1]

Call a mobile veterinarian from the in-game phone to treat the rideable animal you are currently mounted on.

[b]Required item:[/b] [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3795463421]Custom NPC API[/url]

[h2]How it works[/h2]

[list]
[*] Mount a supported animal and bring it to a stop
[*] Call Mobile Veterinarian from the normal phone contacts
[*] Review a price calculated from the animal's actual damage percentage
[*] Close the phone and remain mounted while the veterinarian performs the treatment
[*] Pay only after the real vehicle damage state is fully repaired
[/list]

The veterinarian checks both sides of the animal for a safe standing position. A cancelled or failed visit never charges the player.

[h2]Supported animals[/h2]

The rideable cow from [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3798865703]Moo-tor Vehicle[/url] is supported out of the box. Moo-tor Vehicle is optional and is not bundled with this mod.

Other rideable-animal mods can register their exact VehicleType, localized animal name and preferred treatment position through the public registry. All service messages insert the name supplied by the animal mod; registrations without a usable name fall back to “your animal.”

[h2]For mod authors[/h2]

The [url=https://github.com/Dudeldups/big-ambitions-mods/tree/main/Assets/Mods/MobileVeterinarian]README and Mobile Veterinarian integration guide[/url] document:

[list]
[*] Assembly and external-build dependency setup
[*] Registration and unload cleanup
[*] Localized animal names
[*] Treatment-position coordinates and automatic opposite-side fallback
[*] A focused integration test checklist
[/list]

[h2]Compatibility[/h2]

Built for Big Ambitions build 3674.

[h2]Credits[/h2]

“Doctor” by AkshayBorkar is licensed under [url=https://creativecommons.org/licenses/by/4.0/]Creative Commons Attribution 4.0 International[/url].
