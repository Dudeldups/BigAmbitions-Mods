# Attribution — Lamborghini Aventador SVJ

## 3D model

- Title: Lamborghini Aventador
- Author: Outlaw Games ([Sketchfab profile](https://sketchfab.com/Outlaw_Games))
- Source: [Lamborghini Aventador on Sketchfab](https://sketchfab.com/3d-models/lamborghini-aventador-e328d9f5dcfd48bdbe8c73324b52ece3)
- License: [Creative Commons Attribution-NonCommercial 4.0 International](https://creativecommons.org/licenses/by-nc/4.0/)
- Local source: `Models/lamborghini_aventador_svj.glb`
- Identified variant: 2019 Aventador SVJ Coupe (LP 770-4), based on embedded node/material names.
- Modifications: Unity import, scale and orientation correction, own vehicle hierarchy, materials, collision and runtime setup.
- The GLB has no named steering-wheel or driver-seat node. The mod places the driver on the left-hand-drive side (negative root X), with the player/NPC seat anchor lowered to `(-0.38, 0.37, 0.25)` and the hand targets lowered by a further 8 cm after the owner's pose feedback. The static `Steering_wheel` marker positions the hands; the visible wheel is not bound to steering rotation, as requested. Final pose fit remains pending in-game validation.

The GLB embeds its author, source URL and CC BY-NC 4.0 license metadata. The separate `E:\Downloads\lamborghini\attribution.md` describes a Revuelto model and does not apply to this asset.

## Other assets

- Textures are embedded in the attributed GLB and follow its CC BY-NC 4.0 license.
- The six V12 engine layers in `Config/Audio` are byte-identical local copies of the Lamborghini Revuelto engine WAV set, which uses deterministic additive V12 synthesis (no recorded samples, as documented in the Revuelto audio generation manifest). The NWH idle source and custom idle voice both use `Config/Audio/EngineLow.wav`; there is no separate synthesized engine loop or runtime dependency on the Revuelto mod. Wash loop/end clips are byte-identical local copies of the standard SDK Example-Vehicle `CarWashLoop.wav` and `CarWash_Ending.wav`, with new Aventador GUIDs and importer settings; the repository MIT license is included at `Config/SDK_LICENSE.txt`. Horn tones remain target-owned synthesis. No engine recording or third-party audio sample is used.
- Big Ambitions and Unity runtime dependencies are supplied by the game/project and are not redistributed as vehicle-owned third-party assets.

The vehicle mod and its model-derived assets must remain available only for non-commercial use under the source model's license. This mod is not endorsed by Automobili Lamborghini.

- Harmony 2.4.2 is distributed under its MIT license in Config/THIRD_PARTY_NOTICES/0Harmony-LICENSE.txt. The embedded CodeView PDB path in Dependencies/0Harmony.dll was reduced to the PDB file name. Executable assembly data is unchanged.
