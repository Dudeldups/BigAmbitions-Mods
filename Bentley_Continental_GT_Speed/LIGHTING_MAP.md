# Model-aligned exterior lighting

Lamp functions use complete selected triangles from the original GLB. `Lighting/BentleyLampFaces.json` stores source vertex counts, exact face ordinals and topology hashes; authoring fails if the source topology changes. Housings, covers, body panels and badges retain their own non-emitting materials.

| Source | Physical emitter | Function |
| --- | --- | --- |
| Object_86 | Horizontal eyebrows | White DRL / amber front indicator on the requested side |
| Object_86 | Six projector lenses | White low/high beam |
| Object_86 | Lower crystal diamond tips | White daytime pattern |
| Object_86 / Object_240 | Rear diamond tips | Red tail / brighter brake |
| Object_86 | Outer rear horizontal ribbons | Amber rear indicators |
| Object_240 | Inner rear horizontal ribbons | White reverse |
| Object_123 / Object_149 | Mirror strips | Amber indicators |
| Object_86 | Center high stop strip | Brake only |
| Object_86 | Plate lamp faces | White with main lights |
| Object_86 | Small side marker lenses | Amber front / red rear with main lights |

Design reference: [Bentley press release](https://www.audi.com/en/media-releases/the-new-continental-gt-speed-redefining-the-definitive-grand-tourer-16067), describing the eyebrow, crystal upper element, illuminated lower pattern and rear diamond-tip effect. The inner reverse / outer indicator assignment is inferred from separate model ribbons rather than an OEM wiring diagram. Adaptive matrix masking and welcome animation are not simulated.

## Source geometry and independent states

Player emitters are four native SkinnedMeshRenderers, each parented to the exact original renderer with identity local transform and the original bind poses, bone weights, bone references and root bone. Only the selected faces are copied, with a 0.5 mm local normal offset. UV0 records function/side; UV1 records the original source vertex index for alignment verification.

Previously the player used one combined mesh baked in vehicle-root space. It had no skin binding. A changed source pose therefore moved the original lamps without moving their emission. The regression test now moves an actually weighted bone: source geometry moves about 51.963 mm, while the source-bound emitters remain within 0.505 mm of their paired source vertices, including the intentional normal offset. This corrects the binding mismatch; the exact live state behind the supplied asymmetric front screenshot was not captured.

Traffic retains one combined rest-pose MeshRenderer because Gley's native light component owns that renderer contract. The AI constructor disables only the Bentley player emitter children on its own cloned visual, enables the traffic mesh, clears player references and uses the separate NPC material. There is no duplicate glow and no NPC polling controller.

Player lighting follows native NWH low/high/brake/reverse callbacks, vehicle lifecycle events and indicator input callbacks. Only the cached indicator phase is animated per frame. Property blocks are written to each actual player emitter; they remain isolated per vehicle and are cleared on shutdown. Gley writes the traffic property block directly, including its own blink phase. No source material is recolored to emulate lamps.

## Rear emitting surfaces

The red tail and brake roles share the planar end faces of individual crystals. In the previous map, a rearward-normal threshold also selected the bevels above, below and beside each end face. The previous map also required nearly identical geometric face normals within each tip. Slightly noncoplanar triangles failed that test, sometimes leaving only one of six triangles on a tip. The selection now uses the authored rear-facing vertex normals within the individual crystal components. This adds 262 triangles on 82 outer tips and 104 triangles on 36 inner tips. Both intensities use the same complete crystal tips. The rear-facing normals are also present on separate 4.59–4.69 mm wide rectangular inserts between the crystals. The map excludes those 144 two-triangle rectangles, retaining all 92 outer and 70 inner crystal components with their complete previously verified face selections (510 and 382 triangles respectively).

The horizontal inserts have multiple nearly parallel layers. A connected-component check for short fragments still allowed multiple full-width layers on each side. The current selection uses the outward lens surface on each insert, 138 triangles for the inner white reverse pair and 172 for the outer amber indicator pair. Authoring verifies exactly one continuous component spanning over 10 cm per side for each pair. The combined traffic mesh and player skins use the same face map.

A fresh Unity process also revealed a serialization failure when updating an existing emitter asset with `EditorUtility.CopySerialized`. For Object_240 the updated submesh referenced 806 vertices but the saved vertex buffer still held 2,478. A repeated prefab depth render produced zero reverse-light pixels. Mesh authoring now clears and fills the existing Mesh directly, keeping its asset identity. Verification checks the vertex count against the indexed submesh range for every player skin and the traffic mesh.

## Road illumination

Two player spotlights are positioned from the actual projector centers, 7 cm ahead of the front body extent. Each now uses 2500 lux at 6 m / 90000 cd, so their combined intensity matches the original donor's 5000 lux at 6 m / 180000 cd. The earlier replacement had reduced the total to 18000 cd. A 12 degree downward aim and 70 degree cone replace the earlier 5 degree / 55 degree setting; low/high ranges remain 55/80 m. Analytic ground incidence at 5 m gives about 395.7 lux per beam rather than 39.6 lux. This incidence check establishes aim/intensity; it is not a rendered game-road measurement. The AI retains one native centered spotlight at the authored forward extent with the same aim/angle.

## Exposure, damage and verification

Emission stays in the calibrated pre-exposed color space fixed by 14f7851. The original exposure multiplication reduced DRL blue from 2.0 to 0.000061 at exposure 1/32768. GPU render/readback now verifies all ten roles on/off at neutral and bright-daylight exposure; off functions return zero alpha.

The new player meshes and the traffic mesh use the existing exterior damage material identity and source skinning, so they share impact/repair ownership. They add no colliders or navigation obstacles. Driving, parking, paint and audio are unchanged.

Retained checks cover source topology, symmetric role presence, source transform/bone identity, paired vertex alignment in rest and moved poses, reverse ribbon islands, road intensity/aim, actual native player callbacks and all player property blocks, native Gley state/phase ownership without duplicate emitters, and existing impact/repair behavior. The isolated GPU depth probe also renders the selected front faces against the complete opaque vehicle geometry; it is an editor fixture, not an in-game observation.

General and lighting-specific debug flags default to false. The local opt-in marker is `Config/lighting-diagnostics.enabled`. Diagnostics report shader/support, player source bindings, requested output values and native callbacks without per-frame discovery.

The individual light states, the transition from tail to brake, and combined brake/reverse/indicator operation still require visual confirmation in the game after installing this revision.
A separate Unity editor process loaded the saved prefab and rendered each state against the opaque car geometry. Left/right lit pixel counts at the fixed camera were 550/589 for tail, 772/852 for brake (including the high stop), 178/196 for reverse and 140/0 for the left indicator.
