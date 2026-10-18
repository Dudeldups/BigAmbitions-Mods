# Dodge Ram 1500 TRX

## Published vehicle data applied

| Area | 2021 TRX source data | Mod setting |
| --- | --- | --- |
| Engine | 6.2 L supercharged HEMI V8; 702 hp (523 kW) at 6,100 rpm; 650 lb-ft (882 Nm) at 4,800 rpm; 6,200 rpm limiter | NWH engine power 523 kW, 700 rpm idle, 6,200 rpm limiter, forced induction enabled, TRX-specific curve |
| Transmission | TorqueFlite 8HP95 eight-speed automatic; forward ratios 4.71, 3.14, 2.11, 1.67, 1.29, 1.00, 0.84, 0.67; reverse 3.30 | Eight forward ratios and reverse 3.30 in the prefab and runtime repair path; final drive 3.55 |
| Four-wheel drive | Full-time 4WD Auto uses a 40:60 front-to-rear torque split; 2.64:1 low range | Center differential configured to 0.40 front bias / 0.60 rear bias in Auto road mode; no selectable low-range mode |
| Chassis | 2,880 kg curb weight; 3.6855 m wheelbase; 1.8923 m front and 1.8821 m rear track | Rigidbody and NWH mass 2,880 kg; wheel controllers positioned from the measured wheelbase and separate track widths |
| Tires | 35-inch all-terrain tires, LT325/65R18, on 18 x 9 inch wheels | 0.4445 m wheel radius and 0.325 m tire width; each corner has its own tire/rim mesh, brake rotor and fixed caliper |
| Brakes | 378 x 30 mm front and 375 x 22 mm rear rotors | Separate 378 mm / 375 mm rotor meshes and four caliper assemblies; 3,500 Nm game brake torque |
| Exterior | 5.916 m long; 2.235 m wide without mirrors / 2.510 m with mirrors; 2.055 m tall | GLB normalized to 5.916 x 2.510 x 2.055 m. The source mirror span supplies the published mirror-inclusive width. |
| Fuel and performance | 125 L fuel capacity; 0-60 mph in 4.5 s; quarter mile in 12.9 s; 118 mph (190 km/h) top speed | 125 L fuel and 190 km/h speed limiter. Acceleration emerges from the documented engine, gears, final drive, mass and tires rather than a separate boost. |
| Purchase reference | US launch MSRP $69,995 | VehicleType price $69,995 |

Values come from the official 2021 Ram TRX technical specifications and
Stellantis launch release linked below. The 24-unit cargo value is a game
capacity chosen for the pickup configuration, not a manufacturer measurement.
It is configured for its in-game pickup cargo capacity and is not allowed on a
separate flatbed or hand-truck loadout.

## Reference-mod setup comparison

| Reference | Setup pattern used | TRX adaptation |
| --- | --- | --- |
| Audi RS6R | VehicleType / prefab seed, game vehicle registration lifecycle, dealer and save hooks | Audi is only an editor-time seed. The generated Dodge prefab is fully unpacked, receives Dodge-owned components and data, and is rejected if any serialized dependency resolves into Audi, BMW M4 or Porsche folders. |
| BMW M4 G82 | Compatibility/bundle checks, purchase identity verification, explicit lifecycle/runtime repair | The Dodge build verifies price, fuel, capacity, dimensions, powertrain, AWD split, damage body, glass, paint, wheels and lights from the finished bundle. Runtime repairs are idempotent and scoped to the Dodge type. |
| Porsche 911 GT3 RS | Complete independent runtime: dealer/save, paint/repair, damage, private driver, collision/warehouse recovery, layered audio and lighting | Those systems were copied into Dodge-named code and reconfigured to TRX geometry/data. Sports-car wheel, engine, body, lamp and appearance selectors were replaced with selectors for the supplied Ram GLB. |

No example mod is modified by this task. Example prefab assets are authoring
inputs only; final prefab and vehicle-type assets must pass a serialized
dependency scan that rejects references to all three donor mod folders.
The storefront preview is rendered from the supplied GLB by
`tools~/render_trx_thumbnail.py`; the Unity setup step checks that the PNG is
large enough and contains vehicle pixels before building the bundle.

## Sources

- [2021 Ram 1500 TRX official specifications (PDF)](https://s3.amazonaws.com/chryslermedia.iconicweb.com/mediasite/specs/2021_RAM_1500_TRX_Specificationsncds40e91jnt64taolkbbpi9eq.pdf)
- [Stellantis launch release for the 2021 Ram 1500 TRX](https://www.media.stellantis.com/me-en/ram-archive/press/all-new-2021-ram-1500-trx-quickest-fastest-and-most-powerful-mass-produced-truck-in-the-world-with-702-horsepower-6-2-liter-supercharged-hemi-v-8-engine)
- [Source 3D model and creator page](https://sketchfab.com/3d-models/2021-ram-1500-trx-372fbf37230c46eca1997841bfaf0758)

## Review corrections and verification (2026-10-03)

The second review found and corrected an inverted GLB orientation, runtime
colliders overriding the authored pickup dimensions, a missing steering/driver
anchor, incorrect lamp regions, and damage limited to the painted shell.

- Upright orientation is derived from the source wheel plane, with all original
  geometry included in scaling. The steering wheel/grips are split into owned
  meshes and bound to native steering; the seated driver uses the same anchor.
- Setup and runtime collider/exit values are compared by executing the runtime
  configuration against an inactive bundle instance.
- 22 compact lamp meshes are authored into the prefab with identity transforms
  and a 0.5 mm surface-normal offset. DRLs use the headlamp signatures; reverse
  uses the two rear-corner clear lenses. Indicators take priority over the
  corresponding shared DRL/brake surface. Lamp getters are compiled once.
- Damage writes and replays the native save-format impact records (maximum 20),
  and removes the legacy deformation callback to avoid duplicate damage.
  It includes the body, grilles, trim, lamp housings, all emission surfaces,
  and badges, while cabin and wheel meshes remain excluded.
- Driver/audio initialization runs after spawn/entry, and repair reconciliation
  runs from the existing game/vehicle/menu lifecycle callbacks. These paths no
  longer discover state repeatedly in Update/LateUpdate.
- `DodgeRam1500TRXReview.Validate` fails on missing/empty lamp meshes, invalid
  upright/steering anchors, noncompact lamp geometry, collider/exit divergence,
  or incorrect damage ownership. `ExportGeometry` supports visual inspection.

Build/editor validation and offline geometry renders do not establish in-game
correctness. In-game driving, seating, night illumination, collisions/repair,
paint/reload, warehouse exits and private-driver operation remain to be observed.


## Purchase shader, geometry and sound correction (2026-10-03)

User runtime feedback reported magenta body/wheels after purchase and a thin,
high engine tone. A Unity material probe confirmed all 26 imported GLB
materials used `Hidden/GraphErrorShader2` with zero material properties.

- The editor reconstructs HDRP/Lit materials from the GLB JSON and 34 embedded
  original PNG textures. Base color, metallic, roughness, normal maps, clearcoat,
  double-sided state and badge cutouts are retained. The light metallic/roughness
  texture is packed into an owned HDRP mask. All extracted assets retain the
  source model's license and attribution.
- The prefab material component binds opaque and clear per-instance materials
  in Awake, before dealer rendering. Authored lamp emission stays HDRP/Unlit.
  Shader rebinding preserves normalized HDRP properties, with no donor assets.
- Authored wheel/contact/caliper centers move 0.06 m forward and 0.03 m down.
  The complete body, interior, steering and lamp hierarchy moves 0.06 m down
  (0.03 m overall plus 0.03 m body adjustment). Body/contact colliders, center
  of mass, exits and road beams receive corresponding corrections. Tire sizes,
  wheelbase and drivetrain specifications remain unchanged.
- Six original synthetic V8 loops now use uneven exhaust-bank pulses, stronger
  low engine orders and filtered combustion noise. Supercharger whistles are
  reduced, the acoustic upper range is compressed and the three bands blend in
  frequency space with bounded audible pitch. Idle uses the owned V8 waveform.
  This is a synthesized sound, not a recorded or in-game verified TRX recording.
- Bundle regression checks cover original colors/textures, HDRP shader identity,
  cutouts, two-instance material isolation, idempotence, all axle corrections and
  1001 audio RPM positions. Existing light-state and saved-damage matrices are
  rerun. Generated PCM is checked for mono/44.1 kHz format, RMS, peaks and seams.

Purchase appearance, moving suspension/contact height, and engine timbre still
require a fresh in-game check after the installed build is loaded.


## Material, caliper and RPM-flare correction (2026-10-03)

Runtime evidence: DeveloperTools `vehicle-diagnostics-20261003-130428.csv`,
81 samples over 18.3 seconds, 11 gear changes, 10 limiter events. From first
gear engagement at 0.929 s, engine RPM reaches 6200 at 1.010 s while speed is
only 2.889 km/h. The first shift occurs at 57.293 km/h; the next sample falls
to 951 RPM. The same flare appears in reverse. The vehicle starts with 0.386
saved/physics damage and one saved deformation. A later curb collision
pollutes peak-G figures; this recording does not establish handling targets.

- Unity JsonUtility created missing nested texture references as index zero.
  Existing editor Newtonsoft.Json now preserves absent GLB properties. Paint
  and all three clear materials have no base/normal/mask maps; all 26 original
  definitions and 34 import roles are checked individually. Texture zero is
  restored to a color texture. HDRP materials start from clean Lit defaults;
  cutout and transparent states are validated after final surface settings.
- Glass alpha remains 0.14 with no opaque depth or shadow pass. Solid paint
  keeps alpha one, opaque depth writing and its authored double-sided state.
  Runtime renderer decal layers are cleared per Dodge instance. Body winding
  and normals agree on both sides (18141 / 17858 aligned, zero opposed faces).
  Mirrored body baking handles tangent handedness and winding explicitly.
- Wheels move another 0.03 m forward (0.09 m cumulative). Body, cabin,
  steering and authored lamps move another 0.03 m down (0.09 m cumulative).
  Body/contact colliders, center of mass and generated road beams follow.
- Caliper meshes retain their original displacement from the source tire axle,
  scaled to the tire diameter, instead of being centered on their own bounds.
  Authored caliper mounts remain axle-centered for steering without wheel spin.
  Front/rear radial offsets are 0.125 / 0.142 m and match left/right.
- The old power interpolation produced approximately 1157 Nm near 2480 RPM,
  above the inherited 780 Nm clutch capacity. A shared prefab/runtime linear
  power profile now peaks at 882 Nm at 4800 RPM and 523 kW at 6100 RPM without
  overshoot. Intermediate torque values are calibration, not a factory dyno
  trace. Clutch capacity is 1100 Nm, engine inertia 0.9 kg m2, fixed automatic
  upshift 5800 RPM, shift duration 0.22 s. No RPM clamping or forced shifting
  is used to conceal slip. NWH wheelbase is explicitly 3.6855 m.

[NWH clutch documentation](https://www.nwhvehiclephysics.com/doku.php/NWH/VehiclePhysics2/Powertrain/ClutchComponent)
explains that slip torque must exceed maximum engine torque; too little
capacity lets the engine spin up without transmitting its torque.

Editor regression checks cover exact maps/importers, opaque/clear shader
states, decal masks, both-side geometry, all four caliper offsets, sampled
engine peaks and prefab/runtime drivetrain parity. Light states, audio
mapping, material isolation, damage replay/repair and collider/exit parity
are rerun. These checks do not verify appearance or shifting in the running
game. Restart with the installed build, repair the previously damaged truck,
then repeat the drive and equivalent left/right appearance checks.


## Exact native player-prefab registration (2026-10-03)

The failed developer spawn logs `Collection was modified` from the game's
AssetService bundle-name fallback, followed by `Prefab wasn't found:
Vehicles/PlayerVehicles/dodgeram1500trx`. The Dodge bundle loads successfully,
but the mod previously did not bind the exact native PrefabHelper cache key.
The corrected BMW on origin/main supplies the lifecycle pattern used here.

- Bind `Prefabs/Vehicles/PlayerVehicles/dodgeram1500trx.prefab` directly to the
  owned bundle prefab before vehicle-type registration. Verify the game's own
  LoadPrefabAssetByName call resolves that exact object. Failed binding or
  type registration prevents partially registered vehicle startup.
- Rebind in main-menu load/unload and city-load mod entrypoints before native
  save/purchase/spawn lookup. No recurring searches or global SDK changes.
- Only this exact Dodge cache key is owned. Restore a previous mapping on
  unload only while still owning the slot; preserve a replacement owner.
  A native cache reset starts a new interval, so stale previous-game mappings
  are not restored. Useful binding diagnostics use the existing global debug
  flag (disabled); failures remain ungated warnings.
- Editor regression checks exercise exact native lookup and CreatePrefab
  construction before/after cache removal, repeated binding,
  missing asset preservation, cache removal/city rebind, prior-owner restore,
  another owner's replacement, unrelated key preservation and all three
  assembly lifecycle registrations. The earlier bundle matrices are rerun.

The build/cache checks do not prove developer spawn or cold saved-car loading
in the running game. Restart the installed build, spawn via DeveloperTools,
then return to the main menu and reload to verify the cache-reset transition.


## Complete impact coverage and final tuning candidate (2026-10-03)

Evidence: `vehicle-diagnostics-20261003-145053.csv` (297 samples). Three recorded
0-100 km/h milestones took 4.231 / 4.197 / 4.304 s. First pull: 0-60 mph
4.02 s, 0-100 mph 8.91 s, maximum 190.26 km/h. The truck already had 54.8%
saved damage at session start; no valid damaging contact occurred in this CSV.
The first run includes a steering input at 4.668 s / 96.16 km/h and another
at 8.935 s / 152.59 km/h; run four contains several steering corrections.
Run three has no steering input before its 100-km/h milestone, but residual
yaw remains. These are calibration observations, not controlled straight-test
validation. There is no recorded unbraked first-gear coast segment, so that complaint is
addressed from the actual clutch configuration rather than inferred telemetry.

[Car and Driver's instrumented 2021 TRX test](https://www.caranddriver.com/reviews/a34450961/2021-ram-1500-trx-by-the-numbers/)
measured 3.7 s to 60 mph and 9.7 s to 100 mph, omitting 0.2 s of rollout. Its
70-0 mph stop was 189 ft / 57.61 m. Manufacturer ratings are 4.5 / 10.5 s
and 118 mph, respectively; launch method and test mass differ.
[Manufacturer release](https://www.media.stellantis.com/ar-es/ram/press/la-totalmente-nueva-ram-trx-1500-2021-la-pickup-de-produccion-mas-rapida-y-potente-del-mundo-con-el-motor-hemi-v8-sobrealimentado-de-6-2-litros-y-702-caballos).

- Retain 523 kW, 882 Nm, 2880 kg, factory ratios and corrected automatic shifts.
  Add quadratic air resistance (air density 1.225 kg/m3, calibrated drag area
  2.25 m2). This is a simulation coefficient, not a published TRX wind-tunnel
  value. The native car Reset clears Rigidbody.drag, so an owned physics force
  supplies resistance independently. Interpolated CSV force integration predicts
  roughly 4.3 s to 100 km/h and 10.0 s to 100 mph; this rough estimate does
  not account for steering/traction transients. A healthy straight retest is
  needed before treating either target as achieved.
- Disable clutch creep (90 -> 0 Nm), matching Audi and BMW. Keep normal wheel
  rolling resistance and engine braking; no velocity snap or idle acceleration.
- Desired braking is 25% stronger *deceleration* than the real 70-mph test:
  57.61 / 1.25 = 46.09 m. The supplied CSV covers 48.86 m from 70 mph to
  0.5 km/h at 3500 Nm. The initial calibrated torque is 3625 Nm, accounting
  for added quadratic drag. Exact stop distance requires an in-game retest.
- Split mixed opaque meshes and clear lamp/mirror lenses from retained cabin
  meshes, keeping their original surfaces and material references. Instance
  damage owns 39 meshes, including bumper plastics, grilles, badges, housings,
  lenses and all 22 emitters. Use one regional field for every vertex and beam
  anchor; remove whole-mesh averaging. Widen truck end-impact coverage and cap
  cumulative displacements at 0.60 m. Repair restores all meshes and anchors.
- Complete the upper/lower rear LED rows at their actual source topology
  boundaries. Separate black unlit base from colored emission; reduce red tail
  and brake intensity to retain saturation. Other lamp states remain separate.

Verification covers six front/rear center and corner impacts, actual deformation
of every nearby lamp function, bumper plastics and lenses, immutable retained
cabin/wheels/brakes and shared assets, deterministic replay and full repair.
Rear coverage probes must detect the former triangular gaps. This candidate is
not runtime-confirmed: repair the old truck, retest impacts with lights on,
coasting, acceleration and braking, then save/reload and repair again.


## Driver, lighting, tint and impact follow-up (2026-10-03)

Latest evidence: `vehicle-diagnostics-20261003-170528.csv`, 231 samples.
The four recorded 0-100 km/h pulls took 4.521 / 4.284 / 4.343 / 4.300 s.
The final pull has no steering before 100 km/h, reaches 60 mph in 4.086 s
and 100 mph in 9.757 s (one steering correction at the higher speed).
Maximum recorded speed is 190.105 km/h. The final straight full-brake stop
covers 46.254 m from 70 mph / 112.654 km/h to the first stop sample at
1.242 km/h. Later samples reverse direction; they are not extra stopping
travel. The corresponding 100-km/h distance is 35.523 m. This matches the
46.09-m target within 0.4%, so retain 3625 Nm, power/ratios and aerodynamic
calibration. No valid damaging impact was captured in this session; impact
stability cannot be certified from these road-plane contacts.

- Hands move another 2 cm back and yaw 10 degrees outward in vehicle axes.
  Restore the animation pose before each correction; an unsolved arm leaves
  the native pose intact. Keep the interior wheel static by clearing native
  steering animation binding, while preserving its driver pose anchor.
- Front wheel assemblies move another 0.005 m forward; rear assemblies another
  0.020 m. Controllers, tires, rims, rotors and fixed calipers stay aligned.
  The effective native wheelbase becomes 3.6705 m. The body drops another
  0.020 m (cumulative 0.110 m); road-light origins follow the body.
- Restore visible radiance through the known working HDRP unlit color channel
  and remove duplicate emissive contribution. All 22 overlays retain their
  function colors, saturated rear intensities, exact topology and damage ownership.
- Split complete rear door and cab rear panes from both inner/outer cabin
  meshes at the measured gap between front and rear panes (z=0.20 m).
  Rear alpha becomes 0.30 with a darker neutral tint; front alpha/color stay
  unchanged at 0.14. Stable material markers survive asset creation, per-instance
  cloning, shader binding and entry/repaint glass restoration.
- Lower COM from 0.63 to 0.55 m. Use a truck-sized approximate inertia tensor
  (8000/8400/1800 kg m^2 for pitch/yaw/roll) in the Rigidbody and native mass
  module. This is simulation calibration, not published manufacturer inertia.
  Raise the chassis underside from -0.09 to +0.09 m relative to the root,
  preserving its upper face and warehouse-trigger reach. No rotation axis is frozen.
- Rear dent slope/minimum are reduced 20%, maximum depth 0.42 -> 0.34 m.
  Front deformation parameters, complete lamp/bumper coverage, repair and saved
  replay remain unchanged.

Bundle checks cover lamp states and visible color after runtime shader rebinding,
whole-pane tint and glass restoration, native/runtime chassis parity, three
non-accumulating synthetic hand evaluations, all six impact regions, shared-mesh
isolation, replay and repair. These checks do not replace in-game verification:
verify lights day/night, entry/repaint/reload, driver hands/static wheel, rear tint,
wheel fit, front/rear crashes and stability, warehouse entry, repair and another
straight acceleration/braking drive with the new installed build.


## City steering, rear rake and hand follow-up (2026-10-03)

Evidence: `vehicle-diagnostics-20261003-210737.csv`, 337 samples, no contacts
or airborne samples. Three straight 0-100 km/h milestones took
4.008 / 4.013 / 4.009 s; maximum speed 190.116 km/h. The first launch contains
four full-lock samples at 11.2-30.2 km/h with center-path radii
11.929 / 6.572 / 7.320 / 8.805 m. This is a brief accelerating turn, not a
steady full-lock turning-circle test. The center-path radius in telemetry is
not the outside-tire radius used in manufacturer turning-circle measurements.

[Manufacturer 2021 TRX specifications](https://s3.amazonaws.com/chryslermedia.iconicweb.com/mediasite/specs/2021_RAM_1500_TRX_Specificationsncds40e91jnt64taolkbbpi9eq.pdf)
list a 14.7-m outside-tire turning diameter. The game's `turnRadius` field
actually represents degrees of maximum steer angle, as confirmed by native
GetMaxTurnRadius/SetMaxTurnRadius. Do not replace it with 14.7 meters.
The installed ExternalPlugins NWH `CalculateSteerAngles` evaluates the steering
curve at vehicle speed (m/s) / 50, matching the
[NWH steering documentation](https://www.nwhvehiclephysics.com/doku.php/NWH/VehiclePhysics2/Steering).

- Keep the vehicle's 35-degree steering limit. Retain full angle through
  20 km/h, then transition smoothly through a 0.68 multiplier at 35 km/h
  to the existing 0.40 multiplier at 54 km/h. Preserve the complete prior
  curve from 54 km/h upward. Steering rate becomes 160 instead of 140 deg/s.
  This addresses early loss of steering lock during city turns, while retaining
  the high-speed restriction. No power, brakes, grip, COM or inertia change.
- Pitch the entire authored body another 0.3122 degrees nose-up around the
  front axle. The rear axle body reference drops 0.020 m; the front axle
  reference remains fixed. Rotate both the original model and the owned painted
  damage shell together; wheel/controller/caliper assemblies retain their prior
  positions. The lamp overlays and cabin move with the body, and beam origins
  and rotations follow the same authored transformation.
- Driver hands yaw another 10 degrees outward (20 degrees cumulative).
  Keep the previous 2-cm backward correction and static interior steering wheel.

Checks cover the front/rear body reference heights, matching painted/trim/lamp
pose, unchanged wheel/controller/caliper alignment, a monotonic steering curve
and runtime/prefab parity, exact unchanged high-speed steering, cumulative hand
rotation without accumulation, all lamp/tint and six impact/repair/replay cases.
Runtime verification still needed: slowly drive a complete full-lock circle in
both directions at 10-15 km/h, check high-speed steering, rear arch/body fit,
hands and lamps after entry/reload/repair. A successful build does not prove
turning feel or body fit in the game.


## Acceleration correction from the latest straight run (2026-10-03)

User requested matching the selected instrumented acceleration reference after
clarifying that evaluating a CSV alone does not make the tuning final.
`vehicle-diagnostics-20261003-210737.csv` predates this correction. Use the
fourth launch, with the logger's elapsed-time origin 38.940 s, straight samples
39.062..48.341 s, full throttle, negligible steering, no brake or airborne wheels.
Interpolated measured times: 0-60 mph (96.56064 km/h) 3.773747 s;
0-100 km/h 3.992924 s; 0-100 mph (160.9344 km/h) 9.230042 s.

[Car and Driver instrumented TRX test](https://www.caranddriver.com/reviews/a34450961/2021-ram-1500-trx-by-the-numbers/)
reports 3.7 s to 60 mph and 9.7 s to 100 mph, omitting 0.2 s of rollout.
The standing-start comparison therefore uses 3.9 / 9.9 s. Manufacturer's
4.5 s claim refers to 60 mph, not 100 km/h.

- Add 5.5% residual propulsion loss through NWH's native engine power modifier
  callback. It is a simulation calibration, not measured factory AWD efficiency.
  Keep 523 kW / 882 Nm at the crank, native power curve, mass, clutch, ratios,
  shift thresholds/duration, brakes and aerodynamic force unchanged.
- Explicitly retain the measured native engine-loss coefficient 0.57 in both
  authoring and runtime. Installed NWH generates P*(1+engineLoss)*modifier,
  then subtracts P*engineLoss at full throttle; hence modifier =
  1 - 0.055/(1+engineLoss) leaves exactly 94.5% net full-throttle propulsion.
  Zero throttle returns unity, preserving idle and engine braking.
- Own one callback per vehicle. Repeated configuration deduplicates it;
  disable, destroy, game unload and mod shutdown remove only that callback.
  Existing foreign callbacks remain intact. Binding failure is actionable;
  startup diagnostics include the applied loss under the existing disabled
  global debug flag.

`tools~/estimate_trx_acceleration.py` reproduces a first-order estimate from the
selected measured speed/time intervals: infer positive propulsion from measured
acceleration plus unchanged quadratic drag, scale it to 94.5%, preserve shift
and nonpositive-acceleration intervals. Assumes the same traction, surface and
shift sequence; this does not simulate the native clutch or rotating inertia.
Estimated times: 3.997374 s to 60 mph, 4.230415 s to 100 km/h, 9.903021 s to
100 mph. These are predictions, not new game measurements.

The final straight stop in the same CSV reaches 0.008 km/h:
100-0 = 35.4444 m; 70 mph-0 = 46.1633 m. C/D's 189-ft stop is 57.6072 m;
25% stronger brakes target 57.6072/1.25 = 46.0858 m. Existing stopping distance
is within 0.17% of that target, so keep 3625 Nm brakes and drag area 2.25 m2.
The measured pre-correction top speed is 190.116 km/h; retain the 190-km/h limiter.

Checks cover original crank peaks, native-engine-loss prefab/runtime parity,
full/zero-throttle callback, compensation math, repeat binding, foreign callback
preservation and disable/re-enable/unload cleanup, plus the full existing
geometry, lamp/material, damage/repair and driver/steering matrix.
Runtime verification required after installing/restarting: straight standing
launch to 60 mph / 100 km/h / 100 mph, top speed, full straight braking and
throttle-release stop. Keep this tuning candidate pending that new CSV.


## Rear lamps, auto-parking and warehouse controls (2026-10-11)

The supplied GLB light atlas contains two rear LED chambers and one narrow
reverse insert per side, with no separate amber indicator island. Rear stop/turn
therefore shares the red LED chambers. The [2021 Ram owner's manual](https://vehicleinfo.mopar.com/assets/publications/en-us/Ram/2021/1500_DT/5633293_21_DT_OM_EN_USC_DIGITAL_E4.pdf)
lists premium rear tail/turn/backup/stop LED functions (printed page 331).
The mesh selection follows the actual supplied model, not inferred rectangular
world-space boxes.

- Select complete connected position/UV islands: 38 upper + 50 lower triangles,
  and 17 reverse triangles per side. Exclude the black housing atlas tile,
  cross ribs and outer bezel. Preserve original winding, UVs and face geometry;
  offset 0.5 mm rearward. Rebuild the eight existing owned meshes in place,
  keeping asset GUIDs, prefab object IDs, damage ownership and body placement.
- Use the two LED chambers for dim red running lights (radiance 0.45), red
  brake lights (2.0) and combined red indicators (2.0). Hand off each side
  explicitly: no competing tail/brake/indicator overlays, and no tail or brake
  filling a turn signal's off phase. Restore running lights after braking and
  signaling. The reverse insert retains white radiance 4.8.
- Match Porsche/Cadillac's four-wheel legal-lane and saved auto-park-position
  checks. Reconcile both saved parking state and the active vehicle HUD from
  the native auto-park button callback (bounded 2.9-second transition), and
  after vehicle exit. Reject handicap/mixed/missing lanes, moving vehicles,
  invalid or distant spots; remove owned subscriptions/listeners on unload.
- Native BuildingManager.MoveCarToSpawnPoint sets Rigidbody constraints 96
  (FreezeRotationY + FreezeRotationZ). After a controlled Dodge warehouse exit,
  wait for the native building transition to finish (maximum 2.5 seconds), then
  release a remaining yaw lock through the native SetFreeze(false) path. Cancel
  on vehicle exit/unload/shutdown. Keep the existing entrance collision guard.
  This addresses a residual yaw lock; it does not alter steering or tuning.
- Keep general/feature diagnostics opt-in, all release defaults false.

Unity bundle checks cover exact rear source faces/UVs and physical insert
bounds, player and traffic day/night/brake/reverse/indicator/hazard states,
restored running lights, parking distance/speed/invalid-spot boundaries, native
yaw-unlock and preservation of constraints without a yaw lock. All existing
material, spawn registration, drivetrain, chassis, hands, steering, six regional
crash/deformation, repair and replay checks remain enabled.

The target-owned `tools~/BuildDodgeIsolated.ps1` creates an ignored isolated
Unity 2022.3.62f2 project from this mod and canonical installed game assemblies.
Use `-ExecuteMethod DodgeRam1500TRXDetailAuthoring.RebuildRearLightingAndBuild`
for rear selection authoring, or its default method for build/verification.
Then run the normal external build with `-ModName Dodge_Ram_1500 -Install`.

Runtime verification remains pending: check both rear lamps at night, braking,
each turn signal and hazards (including off phase), reverse without lower
spill; auto-park on a normal legal lane and inspect the HUD before/after exit
and reload; exit a warehouse and steer immediately in both directions.
The build checks do not establish these results in the running game.


## Restore drivable entry after incomplete bundle build (2026-10-12)

Player.log reports the controlled Dodge at instance -4373842 with disabled NWH,
kinematic Rigidbody, FreezeAll, inactive powertrain/engine/transmission, and
`StateSettings are null. Exiting LOD check!`. The prior isolated builder copied
Editor/Settings and game DLLs, but omitted serialized SDK dependency assets.
Unity left the unresolved references out of the bundle; earlier offline reviews
did not validate those critical native dependencies.

- Copy all SDK dependency asset directories and their metadata into the ignored
  isolated project, excluding GameDlls (handled through the canonical resolver).
  Preserve GUIDs; never modify SDK dependency sources or another mod. Regenerate
  canonical DLL import metadata with a final newline to avoid YAML parser errors.
- Require the actual bundled NWH StateSettings, all three LODs, enabled native
  input/powertrain/engine/transmission/steering/brake definitions, ground preset,
  fallback/mapped road surfaces and all four friction presets. An incomplete
  bundle now fails verification instead of being declared drivable.
- After the bounded native entry transition, use cached CarController.StartEngine
  for a disabled/uninitialized/kinematic vehicle and for dormant engine restart.
  Unlike EngineComponent.StartEngine alone, the native vehicle method enables
  NWH and clears kinematic mode; NWH then initializes its components and invokes
  the game's existing wheel/unfreeze callbacks. Keep controlled-vehicle gating,
  exit/unload cancellation, existing maximum attempts and all tuning parameters.
  Missing settings/start binding and failed native calls produce actionable
  warnings. Gated entry diagnostics include initialization and settings state.

Runtime verification required after restart: load the existing Dodge, enter,
accelerate/steer/brake; exit/re-enter, then repeat warehouse exit and auto-parking.
Do not treat bundle/native-binding verification as observed in-game success.


## NPC, parking, existing rear emitters and drive follow-up (2026-10-13)

Read-only input: `vehicle-diagnostics-20261013-105137.csv`, 491 records including
413 samples and 5 launch attempts. The first is a full-lock accelerating turn
with transient 3-wheel ground contact and must not calibrate straight power.
Run 2 starts already rolling at 1.239 km/h and gives 4.605 s to 100 km/h.
The repeat standing runs 3/4/5 measure 0-60 mph in 4.059825 / 4.050667 /
4.056610 s and 0-100 km/h in 4.298610 / 4.287245 / 4.290397 s. Run 4 reaches
100 mph in 9.884833 s; the measured maximum is 190.102 km/h.

The [instrumented 2021 C/D reference](https://www.caranddriver.com/reviews/a34450961/2021-ram-1500-trx-by-the-numbers/)
uses 3.7 / 9.7 s with 0.2 s rollout omitted, hence standing-start comparison
3.9 / 9.9 s. The late 70-mph stop measures 46.175216 m to the first 0.5-km/h
crossing, versus 189 ft / 1.25 = 46.08576 m (0.194% longer). Include gear-change
event snapshots as well as samples: the transmission selects reverse while the
brake remains held, causing backward travel after the first stop. Including
that travel gives a false 48-m stop. Keep 3625 Nm brake torque, 5.5% propulsion
loss, crank curve/523 kW/882 Nm, ratios, clutch, shifts, drag and limiter. Slightly
reduce engine rotational inertia 0.9 -> 0.8 as a bounded launch-response
calibration. This is not a published factory inertia or a verified new sprint.

The only slow full-lock samples are accelerating: 5.382 m center-path radius at
8.777 km/h, 5.634 at 15.405, 5.953 at 21.450. They are not a steady outside-tire
turning-circle measurement. Most of the circular drive is at 45-50 km/h where
the existing speed curve limits lock. Retain 35 degrees maximum, extend full
lock from 20 to 25 km/h, and use 0.74 rather than 0.68 at 35 km/h. Preserve the
entire restriction curve from 54 km/h onward. The [manufacturer specifications](https://s3.amazonaws.com/chryslermedia.iconicweb.com/mediasite/specs/2021_RAM_1500_TRX_Specificationsncds40e91jnt64taolkbbpi9eq.pdf)
remain the reference for the 14.7-m outside-tire turning diameter.

- Rear dent minimum/slope/per-impact cap are reduced exactly 25%:
  0.032 -> 0.024 m, 0.0104 -> 0.0078 m per excess m/s, 0.34 -> 0.255 m.
  Front equivalents reduce 15%: 0.04 -> 0.034 m, 0.012 -> 0.0102,
  0.36 -> 0.306 m. Regional accumulated caps reduce likewise to 0.45 m rear
  and 0.51 m front; side response stays unchanged. Lenses, lights, bumpers,
  trim, anchors, deterministic saved replay and full repair retain ownership.
- Rear driving illumination is on the exact existing upper/lower LED chamber
  islands already selected for stop/turn. No additional lamps or invented
  emitter surfaces. It follows the powered vehicle in daytime, track and night
  contexts, instead of being suppressed by the environment/headlamp getter.
  Radiance 1.10 remains below brake/turn 2.0. Stop/turn handoff and dark blink
  off phases remain explicit. Headlamp beams still follow native auto lights.
- Parking probes use the vehicle's upright frame, explicit trigger hits and
  all matching lane colliders, rather than a wheel frame or an incidental first
  hit. Reject handicap/mixed lanes, moving/distant/invalid spots. Compare spot
  X/Z and bounded height independently. Rebind only our button listener at entry;
  finish the bounded auto-park callback even after player exit. Synchronize saved
  state, the selected active HUD and the native legal map marker color, without
  changing another vehicle's HUD. Ordinary stationary exit parking also uses
  the four legal probes, without requiring an earlier auto-park destination.
- Prepare the owned traffic pool immediately at mod/city/scene load, before
  native Start initializes traffic and independently of business layout loading.
  Keep contract mutation at the established post-deserialization lifecycle.
  Report specific readiness failure reasons. Preserve foreign pool entries and
  reject unsafe late appends. Bind four target-owned centered graphical hubs
  using the confirmed Senna native-job ownership pattern, with TRX-measured
  centers/radii, circumference, ray length and axle spacing. Remove the second
  per-frame rolling-wheel pose writer; calipers retain position/steer-only follow.

Verification includes actual AI conversion from an inactive synthetic native
four-wheel template, all owned visuals/22 emitters/colliders, four measured
native hubs and idempotent rebinding, append/existing-rebind/late-rejection and
foreign-pool preservation. Parking checks cover incidental first hits and
mixed/handicap rejection. Existing full native dependency, material, registration,
audio, drivetrain, chassis, steering, hand, light-state and six regional
impact/repair/replay reviews remain enabled. `tools~/analyze_trx_drive.py`
reproduces the CSV measurements without modifying the input file.

Runtime validation required after restart: NPC selection/developer spawn and
ambient pooled/reused trucks; legal auto-parking before/after exit/reload; both
existing rear lamps while driving day/night, braking, indicating and reversing;
front/rear impacts and repair/reload; slow complete circles in both directions
at 10-15 km/h and city/high-speed steering; repeat straight launch and braking.
The measured CSV predates this candidate; changed behavior is not confirmed
in game by the build checks.


## Feedback correction — 2026-10-13, parking, pooled drivers and stability

- Auto-parking now observes both Button.onClick and the native SpecialInteract input action. GameManager calls ClickAutoPark directly for the shortcut, bypassing the former button-only guard. A bounded end-of-frame reconciliation validates all four physical wheels, updates HUD/save/POI, and marks the native stopped parking check completed; movement clears that native flag. The native one-frame exit completion is also followed. Illegal/handicap/partial parking is not made legal.
- Ambient traffic initializes its seated driver on each pool activation. Driver initialization has four bounded attempts for native appearance readiness and cleans up on disable/unload. Native chauffeur appearance remains owned by the private-driver service.
- Crash deformation uses Amarok-style cached working vertex buffers, mesh/vehicle matrices, prepared contact radii/directions, and distant-part rejection. Conservative bounds include the complete allowed dent envelope of small lamp parts; no full mesh bounds recalculation occurs during impact. Native saved impact records and deterministic replay/repair remain intact.
- Braking torque: 4531.25 Nm, exactly 25% above the previous 3625 Nm. Power, torque, gear ratios and the latest acceleration calibration remain unchanged.
- Rear damage reduced a further 15%: per-impact cap 0.21675 m, depth slope 0.00663 m per excess m/s, minimum 0.0204 m and cumulative cap 0.3825 m. Front and side settings retained.
- Center of mass lowered from 0.55 to 0.35 m in vehicle coordinates. Roll inertia increased from 1800 to 3200 kg m², with pitch/yaw inertia retained; axle anti-roll force increased from 8000 to 12000 N. No rotation axes are frozen.
- Steering keeps 35° lock through 25 km/h and 160°/s response. Available lock at 35/54/180 km/h is 80/50/25% of full lock (previously 74/40/20%), with a monotonic curve.
- Automated checks cover three pooled driver disable/enable cycles, actual four-wheel parking trigger positions and illegal/handicap rejection, native/runtime chassis and brake parity, all exterior/lamp damage regions, saved replay, repair, and conservative bounds. A successful build/install is distinct from in-game verification; the reported parking, drivers, crash lag, reverse stability and driving feel need another observed run.


## Feedback correction — 2026-10-13, front damage and native car wash

- User confirmed auto-parking, absence of crash lag, brakes, steering and rear deformation satisfactory. Those settings and the optimized crash path are retained. NPC driver visibility and rollover stability were not explicitly confirmed.
- Front deformation reduced another 20% from the last installed build: cap 0.306 -> 0.2448 m, slope 0.0102 -> 0.00816 m per excess m/s, minimum 0.034 -> 0.0272 m, cumulative cap 0.51 -> 0.408 m. Complete front exterior/lamp/beam bindings remain unified; rear and side tuning retained.
- Applied the Kia_Ceed_Sportswagon branch car-wash fix from b6d70cf: bind an instance-owned inactive wash effect fallback when CarController.washVfx is missing. Dodge prefab washVfx was null and Player.log showed CarController.WashCoroutine failing before completion. Native wash progress, audio, dirt/save update, navigation block release and unfreeze retain native ownership; no state polling or replacement wash routine.
- Wash diagnostics use the existing global flag plus WashDebugEnabled, both disabled by default. Missing native fields/audio remain actionable warnings. The new fallback is an inert native lifecycle target, not a replacement particle spray effect.
- Bundle checks cover actual wash audio/clips, safe start/end SetActive calls, absent/lost bindings, repeated setup, active effect preservation and the complete front/rear damage replay/repair matrix. In-game wash completion and reduced front damage require a new run after installing/restarting.


## Feedback correction — 2026-10-13, wash sound and suspension release

- User confirmed final deformation satisfactory; all damage, braking and steering settings retained.
- Runtime log showed both native wash clips missing despite Editor stub references. Resolve missing clips once from the actually loaded vanilla HonzaMimic prefab's generic wash audio; do not instantiate or retain donor vehicle geometry. A dedicated, mixer-routed wash AudioSource prevents washing from sharing or overwriting an engine source. The native coroutine still starts its loop and ending clip.
- The washVfx enable/disable lifecycle now suspends the four cached wheel solvers and makes the stationary frozen body kinematic during washing. At native completion, a one-frame handoff restores captured wheel/kinematic states, clears residual velocities and resets wheel angular/low-speed state plus suspension/damper history. No repeated state discovery, broad impulse clamp, teleport or damage change. Entry activation respects native isWashing. Losing player control during the transition must not wake a parked vehicle.
- Owned audio/effect bindings and suspension state are released on game unload/mod shutdown. Wash diagnostics remain gated by global and wash flags (both disabled by default); an optional Config/wash-diagnostics.enabled marker enables the focused trace.
- Expanded review simulates the actual missing-clip runtime condition through the native prefab cache and runs three complete native WashCoroutine instances. It checks saved dirt clearing, loop/end clip switching, native freeze/navigation cleanup, exact disabled-wheel/kinematic-state preservation, zero release velocity and reset damper history. Audio fixture clips contain nonzero PCM. The actual in-game audibility and backing out without launch still require another observed test.


## Feedback correction — 2026-10-14, missed frontal damage, wash-bay door and sleeping

- User confirmed washing itself works. Confirmed deformation, braking, steering and wash coroutine/audio/suspension bindings remain unchanged.
- Inspected diagnostics `vehicle-diagnostics-20261014-130219.csv` and `vehicle-diagnostics-20261014-130536.csv` from the native DeveloperTools VehicleDiagnostics directory. In the frontal run, a zero-impulse pole contact at 13.751 s precedes a 39317.55 N s contact at 13.765 s, but saved and physics damage remain unchanged. A second zero/later-impulse pair occurs at 20.545/20.559 s. A frontal tree collision at 54.378 km/h is excluded by the native broad ground-layer filter.
- The Dodge visual damage owner now owns numerical contact admission as well. It suspends only this instance's automatic DamageHandler receiver and calls the native public Enqueue method for accepted impacts, preserving the game's impulse equation and wheel/engine/transmission damage. A zero or negligible impulse (delta-v below 0.1 m/s) consumes neither cooldown nor visual record. Enter and Stay can admit the later real impulse; the existing 0.5 s visual cooldown prevents duplicate application. Saved damage, last native collision and its time are synchronized. The 18 km/h minimum and disabled-damage option remain respected. Horizontal contacts with named native trees are admitted despite the ground-layer classification; roads, ground and vertical tree contacts remain excluded. Native broken-car notification and variable-change events are retained for the tree fallback. Original receiver state is restored on unload/destruction.
- The exit run records repeated Door01ColliderBlocker contacts, impulses up to 158930 N s and airborne height above 6 m. Native GarageDoor adds a car once but removes it on the first qualifying collider exit. Previously all four body boxes independently qualified. The primary Vehicle-tagged chassis now owns the complete 2.10 x 5.80 m horizontal footprint; the other three original physical boxes move to identity child transforms tagged VehicleAntiRocketCollider, which native garage triggers explicitly ignore. All four collider file IDs and asset GUIDs are preserved. Runtime setup, separation, bounds and NPC conversion enumerate the child boxes. No global garage patch, collision disabling, teleport or launch-velocity clamp is added.
- A bundled early-lifecycle component binds the native Car sleep environment from the known vanilla HonzaMimic configuration before saved VehicleInstance checks. The native private config field is resolved through its generic base class. Existing valid configuration is preserved, the runtime entry path also repairs missing bindings, and cached references release on game unload. No donor vehicle geometry or serialized donor prefab reference is introduced. Sleep diagnostics require both global and SleepDebugEnabled flags, disabled by default.
- Unity 2022.3.62f2 bundle review executes the actual native garage entry/exit methods for three compound-contact sequences, native numerical damage Enqueue with the recorded zero/later frontal impulse and tree impulse, native receiver duplicate rejection, ground/vertical/low-speed exclusions, saved-damage parity and receiver-state restoration. Sleep review covers the bundled component, no saved instance, native Car type and repeat setup. Existing native wash coroutine, traffic preparation, parking, material, drivetrain, chassis, steering, hand, light-state and six regional repair/replay checks also pass.
- These checks verify the compiled paths and bundle structure, not observed gameplay. After restart, validate repeated frontal impacts against poles and trees, slow forward/reverse wash-bay exits without a blocker strike or launch, and sleeping from the Dodge's native interaction UI. The two recordings predate this candidate.


## Final requested shift adjustment — 2026-10-14

- User confirmed the preceding frontal-damage, wash-bay exit and sleep corrections work in game. Requested the final automatic downshift threshold at 3100 RPM instead of 2800 RPM. Runtime configuration, prefab and editor generation agree; upshift remains 5800 RPM and other confirmed tuning stays unchanged.
- Bundle review now checks authored/runtime 3100 RPM parity and every adjacent forward-gear downshift stays below the upshift/limiter range. The final threshold still needs a short user drive after installation/restart.
- Runtime inventory: dealer and developer spawning; main-menu/city/save registration; native entry/exit and warehouse recovery; instance paint/glass/material ownership; all lighting modes; V8/horn/wash audio; native wash/sleep; numerical and complete exterior damage, replay/repair; automatic/legal parking; traffic with seated drivers and private-driver integration; cleanup and bounded lifecycle recovery are implemented. The bundle review exercises these structures and owned transitions. No mandatory donor-mod dependency remains. Native game/SDK configuration assets are intentional dependencies.
- Release inventory: game manifest, English locale, thumbnail, audio, bundle and model attribution are present. All global/feature debug defaults are disabled and the source Config directory contains no diagnostic enable markers. Workshop preparation is still pending: private uploader manifest is absent at the standard Dodge_Ram_1500 path and the mod is not yet registered in tools/release/public-mods.json. After final shift confirmation, merge the existing PR, rebuild/stage from clean current main, prepare Workshop metadata/source registration, run wrapper preflight, and publish only on an explicit Workshop request. No publication was requested in this turn.


## User acceptance and supplied thumbnail — 2026-10-14

- User confirmed the final 3100 RPM downshift in game. The requested runtime corrections and final shift tuning are accepted.
- Replaced the generated thumbnail with the user's exact E:/Downloads/thumbnail.png (752 x 752 PNG); existing Unity meta/GUID retained. No crop, resampling or image modification. The regular isolated build now also invokes the existing PNG size/content validation.
- Workshop publication remains a separate pending action; supplied thumbnail is included in the normal local install payload.


## Reload paint correction — 2026-10-14

- Follow-up bug: one of two owned Dodge trucks appears black/colorless after save reload. Ported the relevant player paint lifecycle from Audi_R8 branch commit 919b4cc, without changing Audi or depending on its assembly.
- First initialization and re-enable prefer the saved vehicleColorName over a provisional native live Black. Pending saved-color resolution is retained until native live and saved names agree. Once restored, normal live repaint previews remain authoritative. Legitimately saved black stays black.
- The new fixture also exposed a Unity native-object field-initializer failure for MaterialPropertyBlock. Allocate the per-instance block lazily during the main-thread apply lifecycle; no Unity native allocation occurs in the MonoBehaviour constructor.
- Each owned player paint controller subscribes idempotently to the native vehicle-repainter:color-restored event. The exact event forces slot PropertyBlocks even if color names/tints have not changed, and works without a selected vehicle or entry event. Disable/destruction release subscriptions. No recurring scan, new palette implementation or Repainter mod assembly reference.
- Explicit ambient/chauffeur color remains per instance and outside player saved-color precedence. Original material-slot classification, fixed trim, glass, lamps, wheels, source materials, confirmed dynamics/damage and supplied thumbnail are retained. Existing global/PaintDebugEnabled gates remain false by default; diagnostics include saved/live names and decision state.
- Isolated bundle review reproduces two parked inactive unoccupied trucks with distinct saved colors and provisional Black; custom colors absent from native registry restored via the event; unchanged-color cleared blocks; normal preview; disable/re-enable; actual saved black; explicit traffic/chauffeur paint and immutable shared material colors. In-game verification requires a cold save reload with two different colors, inspect both before entry, then repaint and repeat reload. This correction is not yet user-confirmed.


## Reload paint follow-up — 2026-10-15

- User reports the installed 6670d2a candidate still loads dark trucks while the R8 works. Installed DLL hash matches that candidate. Player.log shows the R8 initializing three provisional Black/custom saved colors and then receiving the native supplemental-color restore event. The Dodge has early deferred lifecycle passes; its original scene pass stops before appearance setup if layouts are still loading, and its global event handler ignores an unselected vehicle. Thus the previous component-only fix could miss trucks whose paint controller did not yet exist.
- Appearance setup now has an independent target-only lifecycle path. The existing owned prefab activity component emits one readiness event one frame after Start, after native saved-instance/CarFeatures assignment. The runtime configures that individual truck's materials and paint, without waiting for dealer/layout/traffic readiness or player selection. No extra prefab dependency, polling, recurring coroutine or scene search.
- The runtime also handles the exact native supplemental restore event before its selected-vehicle guard. It initializes missing paint controllers and forces saved color on the cached native owned-vehicle collection. The scene lifecycle appearance pass runs before the layout readiness gate. Full driving/physics/dealer setup retains its existing ownership and timing; disable/unload releases readiness subscriptions.
- The regression now starts WITHOUT any paint controllers, runs the actual runtime event dispatcher with two unselected vehicles, and runs the actual bundled prefab readiness coroutine before list registration. It also executes the scene initialization coroutine with loadingLayouts=true, checking that both colors are restored while full physics/dealer configuration remains deferred. The previous direct paint-component tests alone did not exercise this missing initialization order.
- Runtime validation still required after restart: cold-load both differently colored parked trucks, view them before entering either, including optional repainter colors. The new Editor checks and build do not constitute an observed in-game result.


## Engine volume follow-up — 2026-10-15

- User confirmed the reload paint correction works in game; PR #40 merged after rechecking the unchanged candidate.
- Engine loops receive 1.5x PCM gain at load (+3.52 dB), retaining source-volume headroom and the existing throttle/RPM blend. Dedicated idle source gain increases from 0.50 to 0.60, giving 1.8x total idle amplitude (+5.11 dB). Original WAV files and generation metadata remain unchanged.
- PCM gain is validated and clipping is rejected before creating the owned clip. Gain applies only to the seven engine/idle voices. Existing V8 pitch, load character, mixer/master/mute, spatial falloff, direct fallback, horn and exhaust texture remain as configured. Existing globally gated initialization diagnostics report both gains.
- The isolated review verifies actual loaded PCM peaks for all six loops, unchanged source-volume bounds, 1001 RPM crossfade positions, and a conservative simultaneous engine/idle/dual-horn/exhaust peak bound of 0.701. Build verification is complete; the perceived level still requires user listening in game after the pending install completes and the game restarts.


## Audio readiness correction — 2026-10-15

- User still hears very quiet idle and driving sound in every camera perspective. Installed DLL hash matches the previous audio build; the available Player.log has no audio load/playback evidence because those diagnostics were disabled. Runtime audibility is not established by the earlier PCM gain check.
- A parked truck can configure its custom voices before native NWH audio sources exist, permanently selecting the direct fallback at 0.16 gain. Subscribe to that instance's native physics onEnable event and run one two-frame readiness handoff to rebind already loaded voices when native sources/mixer become available. Prefer routed auxiliary/child sources over an unrouted engine source. Restore previous native mute ownership before rebinding. No recurring discovery or duplicate loop loading.
- The isolated project also omitted the native SDK VehicleAudioMixer referenced by the prefab. Import that exact configuration and its existing meta/GUID; the bundle review now requires this mixer reference. No example vehicle or donor mod code is included.
- New fixture loads all nine WAVs through the actual controller, then reproduces parked fallback, late nested auxiliary routing, and late native engine routing without changing clip/source ownership. Existing PCM/mix/pitch checks remain.
- New AudioDebugEnabled sub-flag and global flag default false. Optional local Config/audio-diagnostics.enabled enables focused clip sample/gain/peak/RMS, routing revision, and one idle/drive playback snapshot per entry. This marker must stay out of committed source/release configuration. Missing post-activation routing remains an actionable warning. Next in-game run must confirm actual playing/mute/master/mixer/fallback states and perceived volume.


## Audio level calibration from runtime evidence — 2026-10-15

- User still hears no difference with fddd5d8. Latest Player.log now proves actual EngineLow/Mid/High and Load clips loaded at 1.50 gain (peak about 0.279, RMS about 0.157); idle and driving voices playing, mute=false, master/listener=1.00, mixer Engine and fallback=false. A newly spawned truck also successfully transitions from direct fallback to native routing. Thus the readiness correction worked, but did not resolve the reported volume.
- Native engine sources use a 1 m minimum distance and 500 m maximum distance. Owned idle/driving sources now use an explicit logarithmic engine profile with a minimum reference of 4 m, retaining native mixer, master/mute, spatial blend and far-distance settings. Apply the calibration on initial creation and every native rebind. Horn/exhaust profiles and native template objects retain their settings.
- Engine PCM gain increases from 1.50 to 2.00 (+2.50 dB relative to the last build). Dedicated idle source volume increases from 0.60 to 0.75, for +4.44 dB total idle amplitude relative to the last build before distance attenuation. This is a direct gain/distance correction; existing V8 pitch and RPM/load mapping remain.
- Isolated review passes 1001 RPM positions and actual amplified PCM; conservative simultaneous owned engine/idle/dual-horn/exhaust peak is 0.858, below the 0.90 safety bound. Lifecycle fixture asserts that late auxiliary/native routing retains the four-metre engine reference while leaving horn and native source distances untouched.
- Retain local audio diagnostics for listening validation. Playback snapshots additionally report mixer engine/attenuation dB and actual rolloff/minimum distance. Defaults remain disabled in source; the enable marker is local-only. Build checks do not establish the user's perceived in-game level.


## User-authored Blender lamp selections — 2026-10-15

- User accepted the motor sound in game. Audio PR #41 merged after update/build verification; local audio diagnostic marker removed. Audio/dynamics/damage strengths are retained for this lighting task.
- Selection authority: E:/Blender/DodgeRamLightOverlays.blend, SHA256 f22255568fce402318e26a67bde2326a97c1de0a692fa07f1179bf586d780da7. Its original Light_Geo_lodA_RAM_1500TRX_2021LightA_Material_0 exactly matches the production GLB: 14,956 vertices, 16,416 triangles, identical polygon/index order and zero vertex-position delta. All selected vertices participate in complete selected faces.
- ExportBlenderLights.py checks this compatibility and produces material-free DodgeRamLightOverlays.glb plus a hashed selection manifest. Local mesh coordinates and smooth-face flags are copied; the working object/world matrix and exported subset hierarchy are never used to position Unity lights. Generated overlays are identity children of the production light renderer; setup/review validate every triangle against the production local geometry, front/rear/mirror/side bounds, exact counts and only the existing 0.5 mm anti-z-fighting offset. Existing asset GUIDs are preserved.

| Blender group | Complete faces | Runtime use |
| --- | ---: | --- |
| Headlamps | 526 | 263 left + 263 right headlamp emitters |
| DRL | 483 | 242 left + 241 right permanent white DRLs |
| DRL_Indicator_left / right | 305 / 299 | Independent white DRL and amber turn overlays per side |
| Mirror_Indicator_left / right | 47 / 47 | Amber mirror indicators |
| Brake_Lights | 276 | 108 left + 108 right brakes, 60 center high brake |
| Rear_Driving_Lights | 216 | 108 left + 108 right dim red running lamps |
| Reverse_Lights | 34 | 17 left + 17 right neutral-white reverse emitters |
| Indicator_RL / RR | 88 / 88 | Separate amber rear indicators |

- Rear running and brake groups share exactly the same side faces, so only the dim or bright overlay is enabled at a time. Rear indicators are disjoint from them and remain independent during braking, reverse and hazards/off phase. The base white DRLs remain on; only the shared DRL/indicator strips hand off per side. Preserved the supported fog, front/rear marker and plate lamps lacking replacement groups, plus real road beams. Total 24 state-controlled renderers, including 19 overlays from the eleven Blender groups.
- All 24 overlays use the existing per-instance visual damage ownership. Bundle review verifies NPC preparation, day/night, both indicators, hazards/off phase, running/brake/reverse combinations, player/uncontrolled cleanup and six regional impacts with immutable source meshes, deterministic replay, beam movement and complete repair. Isolated editor tests do not establish in-game visual acceptance; restart and inspect all states and damage after installation.
- Geometry/selection follows the supplied working model. Factory design context: https://www.mideast.ramtrucks.com/en/test/vlp3.html (2021 TRX headlamps/identification lighting); no new lamps or new performance tuning were inferred from later model years.

- Repeated authoring is byte-stable for the complete prefab and every lamp mesh; existing and new overlay instances/GUIDs are reused. The supplied Blender file hash remains unchanged. Source/release diagnostics remain disabled.


## macOS AssetBundle — 2026-10-16

- The manifest and setup defaults target Windows + Mac (flags 3), matching current Audi_R8 and McLaren_Senna. The SDK produces separate native-platform bundles at AssetBundles/Windows/dodgeram1500trx.unity3d and AssetBundles/Mac/dodgeram1500trx.unity3d. Runtime registration retains the flat AssetBundles/dodgeram1500trx.unity3d key; the game loader selects the platform folder.
- The isolated build enables canonical installed-game DLL imports for OSXUniversal as well as Editor/Win64, retaining their existing GUIDs. Following R8's asset build, Newtonsoft.Json is present only as a disabled API-Updater resolver input; the existing Unity package remains the compiler provider. No new runtime assembly dependency or other mod change is required.
- Verification now requires both platform flags, both bundle files/manifests and matching native Unity CRCs for Windows and Mac. The regular Windows bundle structural/lifecycle review remains in place, including the current 24-surface Blender lamp candidate. Normal external install copies the full AssetBundles tree, including Mac.
- This is a Windows-host Unity cross-build. A successful bundle build and CRC check do not establish in-game macOS rendering or gameplay. No Workshop upload or merge of the still-unconfirmed Blender lamp PR is authorized by this request.

- Cross-build verified with Unity 2022.3.62f2: Windows CRC 114360311, Mac CRC 1319227697; native macOS bundle size 28,819,856 bytes. Existing Windows review passed (24 lamp surfaces, instance systems, damage/replay/repair). External build succeeded with zero compiler warnings/errors; full Windows+Mac install payload queued while the game is running. macOS in-game verification remains pending.


## Native crash sound restoration — 2026-10-18

- User reports inaudible collisions. The previous Windows bundle inspection confirms zero resolved native crash clips; the prefab references seven SDK Example-Vehicle GUIDs that are absent from the isolated project.
- Own unchanged SDK Car_Crash_01_alt2.wav through Car_Crash_07_alt2.wav as Config/Audio/Crash01.wav through Crash07.wav with independent GUIDs. Preserve SDK MIT license and attribution. Bind the existing NWH crash component; retain its native collision event, spatial/mixer routing, volume, pitch randomization, velocity scaling and 80ms cooldown.
- Targeted CrashSoundSetup.AuthorAndBuild edits only the existing prefab audio bindings. Both platform sidecars must list all seven owned clips; the Windows bundle must resolve each nonempty AudioClip to the exact bound asset. Standard future build and full generation paths enforce these bindings.
- Observe the native collision event for support only. Missing clips/source produce one actionable warning per instance; detailed impact traces require both global DebugEnabled and CrashAudioDebugEnabled (defaults false), capped at twelve valid player impacts. Subscribe/unsubscribe with the existing instance lifecycle; no extra playback, searches, polling or physics/damage changes.
- Build verification is distinct from the pending in-game listening check: front/rear/side impacts, repeated crashes after cooldown, save reload and repair, interior/exterior perspective, and game master-volume/mute behavior.

- Unity 2022.3.62f2 rebuild and complete existing vehicle review passed. Windows CRC 2572603854; Mac CRC 1899486586. Each platform sidecar includes all seven clips; Windows bundle resolves all seven correctly. External runtime build: zero warnings/errors. DLL and both bundles installed directly into ModsLocal and SHA-256 matched to build outputs. In-game crash audibility remains awaiting user confirmation.
