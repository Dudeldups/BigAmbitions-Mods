# 1967 Chevrolet Camaro - first Unity/runtime validation

The setup intentionally derives the visual, wheel and lamp geometry from the supplied
`1967_chevy_camaro_ss_hidden_jewel.glb` instead of copying target-specific offsets from
another vehicle.

## Headless Unity generation

The preferred first-build path is the isolated PowerShell wrapper; Unity does not need
to be opened manually:

`./tools/build_chevrolet_camaro_1967_isolated.ps1 -Install`

If the source GLB is not already under `Models/` or in the default Downloads locations,
pass it explicitly with `-SourceGlb "<path-to-glb>"`.

The wrapper keeps only `Chevrolet_Camaro_1967` and the `AudiRS6R` donor visible during
the Unity batch build, synchronizes missing imported game DLLs from the main SDK worktree,
generates the audio, invokes `ChevroletCamaro1967Setup.GenerateAndBuild`, copies the
Windows bundle to the flat runtime bundle path, and then runs the external DLL install.

The setup verification should confirm:
   - 4.691 x 1.842 x 1.295 m body target,
   - four independent wheel assemblies and four fixed calipers,
   - M22 4-speed ratios with 4.11 final drive and RWD,
   - Camaro source light mesh present,
   - repaint renderers wired to `CarFeatures.bodyMeshes`,
   - 1,483 kg Rigidbody and the Camaro center-of-mass target.
For code-only changes afterwards, the standard external build command remains:
`./tools/external-build/BuildBigAmbitionsMods.ps1 -ModName Chevrolet_Camaro_1967 -Install`.

When asset/prefab/setup code changes, rerun the isolated wrapper instead.

## First in-game pass

Check these model-dependent items before publishing:

- wheel stance and tire width/radius; no wheel clipping at full steering lock,
- driver hip/hand position and exit markers,
- warm-white low-beam location and road-light direction,
- lower front amber turn lamps only (headlamps must not blink),
- red rear stop/turn behavior, including braking while one side is blinking,
- lower rear white backup lamps only (taillamps must not turn white),
- paint changes only the exterior body/doors/hood/trunk/mirrors,
- window transparency, chrome and badges remain unaffected by repainting,
- damage deformation follows the outer body without detached lamp geometry,
- warehouse entrance/exit and private-driver path,
- City Cars stock registration and NPC traffic/pool reuse,
- synthesized small-block V8 pitch/volume through idle, cruise and full-throttle shifts.

## Intentional owner-choice items

These are not guessed in code beyond a conservative first target:

- `220 kW / 295 hp` uses the documented stock L48 SS 350 rating because the Hidden
  Jewel feature documents modifications but gives no dyno result.
- `$54,900` and `City Cars` are the current gameplay/economy targets and can still be changed
  to a show-car premium / luxury-dealer placement without changing the vehicle setup.
