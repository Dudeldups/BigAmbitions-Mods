# Bentley Continental GT Speed — implementation and test status

STATUS: UPDATED BUILD INSTALLED FOR REGRESSION RETEST. BUILD AND LOCAL INSTALL PASSED; GAMEPLAY HAS NOT BEEN VERIFIED.

## Implemented for the first test

- Imported the supplied 2025 Bentley Continental GT Speed GLB into a target-owned native vehicle prefab. The mod owns its 139 generated meshes, 33 materials, and 31 textures.
- Added dealer registration, vehicle type registration, player prefab setup, saved-vehicle configuration hooks, and bounded private-driver/traffic integration.
- Configured the Bentley's AWD, 8-speed NWH drivetrain, 2,459 kg mass target, 80 L fuel capacity, 335 km/h speed target, steering, suspension, tire dimensions, and braking. The game uses one NWH engine; the 575 kW output is a gameplay approximation of the published PHEV system power, not a simulated hybrid motor or battery.
- Added body paint, fixed trim, glass and wheel material setup. Body paint assignment excludes the calipers.
- Reused the existing McLaren Senna V8 loop assets already used by the Audi R8, with RPM/load-responsive mixing and a custom two-tone horn. The Bentley does not synthesize or alter engine clips; source and license are recorded in `Config/THIRD_PARTY_NOTICES.txt`.
- Added per-instance body deformation restricted to readable `Paint` and `OEMPaint` submeshes, with NWH collision gating and exact mesh restoration when repaired. Other submeshes in a shared mesh are protected.
- Built Windows and Mac AssetBundles with Unity 2022.3.62f2. Target-owned asset checks passed, the external runtime build succeeded, and the mod was installed to ModsLocal.
- Source model attribution and the CC BY-NC-SA 4.0 license are recorded in `ATTRIBUTION.md`. This model's non-commercial license rules out a commercial release; no Workshop publication was requested or attempted.

## Historical tuning (superseded by the October 20 root-cause corrections below)

- Saved body paint is reapplied after the game's renderer bindings are restored on save/load. Body paint metallic and smoothness now match the native vehicle paint material; the glass tint alpha is 0.08 to reduce its white cast.
- Wheel visuals use the native sibling hierarchy with `wheel.visualTransform` unset, matching working vehicle prefabs. The existing repair/build method now also persists the 0.67 acceleration calibration, 14,300 Nm NWH brake target, and an additional 8 cm lowering of the visual shell (16 cm total offset; physical wheel centers stay fixed) into the prefab before building.
- The latest supplied Developer Mod CSV recorded three 0–100 km/h runs around 2.12–2.15 seconds. The new curve scale targets Bentley's published 3.2 seconds. Three full-brake CSV stops reached roughly 2.7–2.8 g at the old serialized 32,000 Nm; the new torque target is sized for roughly 1.23 g (25% above the independent 70–0 mph test baseline). These are initial telemetry-based tuning values and need another in-game measurement after installation.
- First-entry drivetrain recovery now calls the game's native `CarController.StartEngine()` only when the selected Bentley is uninitialized or frozen, then confirms an active engine and engaged forward gear before reporting ready. Wash-time recovery remains eligible for a later vehicle-variable event.
- Auto Parking overlap uses the game's native 10 cm wheel contact offset. The exit safeguard now fixes the NavMesh obstacle geometry instead of teleporting the player: the serialized obstacle had raw world-sized extents on a visual helper scaled 95×, so the prefab could present an oversized carving volume before runtime normalization. The body collider also began 0.29 m above the vehicle root/road plane, leaving the carving box floating above the pedestrian surface. The obstacle is now serialized and normalized from enabled, active, non-trigger BodyCollider boxes, with only its lower edge extended to the root plane. Native exit markers are moved beyond the real body half-width by the player's NavMeshAgent radius plus a 0.25 m margin before the game's native second target update and warp. The former delayed recovery coroutine and extra player/NavMeshAgent warp were removed; the new one-shot observation is read-only and logs movement, capsule clearance, NavMesh path completeness, collider bounds and carving state only when both diagnostics flags are enabled.

## Known gaps before calling the vehicle complete

- The source GLB does not independently identify the precise DRL, headlight, brake, reverse, indicator, and hazard surfaces. Those functions are not wired yet; do not use the vehicle to validate lighting behavior.
- The in-game stance, mirrors, wheel fit, driver alignment, dealer purchase, saved reload, paint changes, audio, damage, repair, and private-driver behavior still need actual gameplay validation. No in-game behavior is claimed as verified.
- The NWH drivetrain represents the PHEV's combined output through one engine. Its 8-speed ratios, RPM limit, game price, suspension, and grip are mod tuning targets where the manufacturer does not publish equivalent game parameters.
- The external builder reported no thumbnail for the new mod. This does not prevent local installation or testing.

## Regression retest

1. Restart Big Ambitions and load a save with ModsLocal enabled.
2. Buy the Bentley from a luxury car dealership and check that it moves immediately after purchase without exiting and re-entering.
3. Check that all four wheels rotate, front wheels steer, the body color stays consistent while orbiting the camera, and the windows remain clear.
4. Auto-park it and confirm the parking status is legal. Exit and walk away to confirm there is no invisible collision barrier.
5. Measure a repeat 0–100 km/h acceleration run and a level 70–0 mph brake stop; include the Developer Mod CSV and a short Player.log excerpt if either target is missed.
6. Wash the Bentley, confirm the wash completes and its sound plays, then drive away without exiting and re-entering.
7. Save and reload; confirm body color and vehicle control persist. Listen for the reused V8 sound during idle and acceleration.

Record any failure from the in-game log with the action and vehicle state. After this regression retest, wire the vehicle-specific lights once their Blender regions are marked before describing the mod as feature-complete.

## Verification record

- Unity isolated authoring and target-asset ownership checks: passed on 2026-10-20. Exit obstacle/body-collider alignment and native marker clearance checks passed.
- Windows and Mac AssetBundle builds: passed on 2026-10-20.
- External runtime assembly build (0 warnings, 0 errors) and ModsLocal installation: passed on 2026-10-20.
- In-game launch, dealership purchase, driving, lighting, damage and repair, save/reload, and private-driver verification: `not_run`.
- NavMesh exit behavior after manual parking/Auto Parking, walking to adjacent sidewalks, and post-save/reload: `not_run`; requires in-game retest.

All other contract checks that have not been directly observed remain `not_run`.

## October 20 root-cause corrections

### Paint

`SH_Vehicle` obtains metal/AO/smoothness, diffuse/mask and normals from custom
`Texture2D_*` graph properties. The Bentley materials had those three inputs
unbound. Previous changes to `_Metallic` and `_Smoothness` did not control these
graph outputs. Authoring and runtime now bind mod-owned neutral textures to the
actual graph inputs, with metallic R=0, AO G=1, neutral diffuse/mask and a flat
normal. Native CarFeatures tint ownership is authored directly to the six Bentley
paint renderers. Previously full regeneration restored donor renderer references,
which were corrected only by runtime setup; authoring now prevents that regression.
Asset validation checks actual texture bindings, not merely unused PBR floats.

### Wheels

Source wheel/caliper bones were transform-only nodes inside body-bound skinned
meshes. The native `3DWheel` references contained empty disabled renderers;
changing their pivots could not animate the visible geometry. Authoring now
extracts wheel-weighted triangles into the four NWH rolling roots, extracts
calipers into the upright roots, and removes these triangles from the static
body meshes. Checks require real visible geometry at every corner, no wheel
triangles left in the static body, and measured tire-center/radius agreement.
Chauffeur clones include these sibling assemblies too.

### Parking

The recorded native park completed once at the captured Murray Hill spot.
The front suspension mounts were at Y=0.90 m, with query centers near 0.80 m
and half-height 0.752 m: the overlap boxes missed the zero-height lane collider.
Rear queries intersected it. Ground alignment pitched the vehicle about 4.93
degrees; the mount is not the physical tire center. NWH IL confirms that
`WheelPosition` includes spring travel. Captured Auto Park reconciliation now
queries from that actual wheel center, retaining native offset/extents/layers
and requiring four non-handicap lane hits. Entry reconciliation uses that probe
only for persisted Legal vehicles; saved Illegal uses the existing native probe.
No unconditional Legal assignment, scene lane mutation or new timer was added.
Gated logs include mount/physical centers, spring length, grounding and all hits.

### Navigation

The user reports the invisible wall appears resolved. The existing body-derived
obstacle and native exit behavior remain; authoring now preserves this geometry
during a full model regeneration. Asset checks compare obstacle/body footprint
and exit-marker clearance. NPC navigation and walking after parking still need
in-game confirmation for this rebuilt artifact.

### Telemetry

Input: `DeveloperTools/VehicleDiagnostics/vehicle-diagnostics-20261020-111656.csv`
in the game's persistent-data directory. Three complete 0–100 runs were
4.312–4.382 s (median 4.352); two 0–200 runs were 16.755–16.787 s.
Peak generated power was about 267 kW: the old 0.465 curve multiplier had
removed over half the 575 kW system-power target at every RPM. That global
multiplier is removed. Launch/traction must be verified separately from power.
Full-brake CSV stops indicate roughly 26 m from 70 mph, substantially stronger
than requested. The instrumented OEM baseline is 167 ft / 50.90 m; 25% greater
deceleration targets 40.72 m. Torque is retuned from 5600 Nm using measured
stopping distance, with a headless NWH fixture for follow-up measurements.
Source: https://www.caranddriver.com/reviews/a63363832/2025-bentley-continental-gt-speed-first-edition-test/

The stock arcade tire preset has D=2 and native NWH multiplies longitudinal
load by 1.35. With the old 1.05 grip, the contact-force cap was about 2.84 times
load. Final longitudinal grip 0.46 limits that cap near 1.24 times load; lateral
grip is unchanged. Final brake torque is 3600 Nm. A native NWH AerodynamicsModule
provides quadratic frontal drag (dimensions 1.966 x 1.397 x 4.895 m, Cd 0.35;
explicit game tuning, not OEM wind-tunnel data), without added downforce or
lateral drag. Thus full engine power is retained while launch is contact-limited
and high-speed acceleration includes air resistance.

Standalone Unity 2022.3.62f2 NWH fixture, final pass: 0–100 3.16 s, 0–200 9.10 s,
70–0 mph 39.76 m / 2.56 s. Mass 2459 kg, four grounded wheels, generated power
peaked at 575 kW. Rigidbody and native NWH speed agreed; drag at 200 km/h was
1541.5 N. Bound rolling visuals changed by 50.1 degrees and front steering by
14.4 degrees. These are fixed-step flat-ground fixture observations, not in-game
times or proof of exact OEM equivalence. Missing game bootstrap assets and the
game-dependent sound mixer produce standalone initialization/shutdown errors;
the fixture explicitly reports measured NWH state and cannot validate audio,
parking, saves or player navigation. In-game CSV remains the acceptance source.

This section records source/telemetry evidence. A successful build or isolated
physics test is not proof of in-game paint, parking, save/reload or navigation.

Final artifact verification (2026-10-20): tightened Unity asset checks and both
Windows/Mac bundle builds passed. External runtime build: zero warnings/errors;
ModsLocal installation completed immediately. SHA256 matches for DLL and both
installed bundles were verified. Local Config marker files enable appearance,
drivetrain, Auto Park and exit diagnostics for the next gameplay retest; all
source diagnostic flags still default to false. No game launch or gameplay test
was performed for this artifact. Existing PR #51 stays open pending user tests.

## Follow-up: preview, fit, wash and exterior damage (2026-10-21)

User gameplay confirmation for 079e638: saved/reloaded paint is correct, Auto Park
works, and wheels roll. Those paths are retained.

- Repaint preview: Vehicle Repainter calls native `CarFeatures.SetColor` and then
  emits `vehicle-repainter:color-preview`. Bentley's generic game-event setup
  immediately restored the saved color. The shared saved-color restore now skips
  exactly that preview source, covering both direct prefab-binding and later
  restore calls. Cancel/reset and committed/reloaded colors keep normal behavior.
- Fit: front wheel controllers and rolling/upright assemblies move forward 0.01 m;
  shell grounding offset changes from 0.16 to 0.14 m, raising the shell 0.02 m.
  Tire height, radii and rear axle stay fixed. Asset checks assert absolute values
  so repeated repair/build runs cannot accumulate these offsets.
- Wash: removed procedural oscillator/noise clips. Bentley-owned WAVs now contain
  exact byte copies of canonical SDK `CarWashLoop.wav` / `CarWash_Ending.wav`,
  retaining Bentley GUIDs and bundle ownership. SHA256 and imported native audio
  metadata are checked; Unity decodes them to 48 kHz / 154539 and 86859 frames.
  Native coroutine, pitch 1, spatial blend 1 and native mixer routing are retained.
  Missing-source fallback creates a dedicated source instead of grabbing engine
  audio. Attribution is recorded in Config/THIRD_PARTY_NOTICES.txt.
- Damage: the old mask admitted only paint. Explicit fixed exterior material
  slots now include grilles, badges, split trim, housings and plates. Glass uses
  a triangle mask in the nearest vehicle's coordinates, including bone/bind-pose
  transforms: all three vertices must be at a vehicle end and lamp height;
  triangles and vertices shared with cabin glass stay protected. This also covers
  mixed glass mesh Object_51 without deforming its cabin portion. Unity checks
  counted 25469 front trim, 26480 rear trim, 16429 side trim and 4099 lamp-glass
  eligible vertices; protected categories include wheels, brakes, interior and
  cabin glass. Per-instance clone/source-baseline ownership is checked.

### Latest real driving CSV

Automatically found input: `DeveloperTools/VehicleDiagnostics/vehicle-diagnostics-20261020-210639.csv`
under the game's persistent-data directory (439 data rows, 358 samples).
Four 0–100 runs: 3.451, 3.464, 3.465, 3.475 s (median 3.4645 s). Two 0–200 runs:
9.479 and 9.580 s. Top recorded speed 333.128 km/h. Four straight full-brake runs
give approximately 31.72–31.82 m from 100 km/h; two reach 70 mph before stopping
and give 40.17 / 40.23 m to near-zero speed. Four wheels remain grounded, steering
is zero and height varies by about 2 mm; no collisions were recorded.

The measured 70-mph stops are close to the existing 40.72-m target derived from
the instrumented 50.90-m stock result and requested 25% deceleration increase.
Brake torque and tire grip therefore remain at 3600 Nm and 0.46. Do not increase
them solely from perceived length at very high entry speed. The isolated fixture
starts with first gear engaged; real CSV launches start from idle/neutral, so its
slightly faster launch is not a direct identical-protocol comparison. Do not use
the CSV's instantaneous peak-g fields: batched fixed steps can distort those;
threshold crossings and travelled distance are the measurements used here.

### Exit evidence from the user's game log

For vehicle -1985546, `after-native-exit-warp` and
`after-obstacle-stationary-carve-window` both report an enabled agent on NavMesh,
free capsule, no blocking collider and a complete outward path. Player, character
and agent positions agree; the player moves approximately 3.91 m between those
observations. The obstacle footprint remains 1.990 x 4.580 m and reaches the road
plane. This supports the recorded exit as successful; it is not proof of every
parking location or sidewalk. Earlier `capsule-shape-invalid` entries occur while
the capsule is disabled inside the car, before the native exit warp.

The new preview, wash sound and impact appearance still require gameplay
acceptance after installation. No new in-game test is claimed for this follow-up.

Follow-up verification: final Windows/Mac bundle build and tightened asset,
fit, wash-import and damage-mask/clone checks passed. The final NWH regression
fixture observed rolling and steering, four grounded wheels, 575 kW peak power,
0–100 3.18 s, 0–200 9.12 s and 70–0 mph 39.76 m. External runtime build passed
with zero warnings/errors; immediate ModsLocal installation and SHA256 equality
for DLL and both bundles passed. Appearance, wash and damage diagnostics are
enabled only through local marker files. The previously validated exit marker
was removed locally to stop repeated diagnostic output; source debug defaults
remain disabled. PR #51 remains open pending gameplay acceptance of the new fixes.

## Follow-up: steering, launch slip, trunk backing and badge alpha (2026-10-21)

User confirms the previous repaint preview, requested wheel/body placement and
native wash sound. Auto Park and the previously validated exit geometry are
retained. No new game launch or gameplay observation is claimed for this update.

### Dynamics evidence and selected configuration

Latest actual DeveloperTools input is `vehicle-diagnostics-20261021-111915.csv`
(392 samples), automatically found in the game's persistent-data diagnostics
folder. Its launches average about 3.54 s to 100 km/h (3.50–3.60 s); the one
complete 0–200 run is 9.55 s. First gear reaches 6500 rpm near 20 km/h and remains
in gear until about 55 km/h, with measured longitudinal slip up to 1.0. Second
gear slip reaches about 0.45. This is a valid straight-line data source but has
no useful turning manoeuvre or Bentley collision evidence. The neighbouring
111352 file belongs to a Lamborghini and was not used to tune the Bentley.

Installed native NWH IL establishes the cause: automatic shifting compares
`CalculateNoSlipRPM()` from road speed/radius/current ratio against the shift
threshold, while the spinning powertrain reaches its limiter earlier. The
supported native `TCSModuleWrapper` now limits wheelspin at threshold 0.10;
shift schedule, 6500-rpm limiter, ratios, power curve, grip and brake torque are
unchanged. Front lock is 32 degrees, up from 27; this is a measured game steering
proxy, not a reproduction of Bentley's rear-steer control law.

A matched flat-ground fixture gives a stationary full-lock body-centre median
radius of 5.90 m before and 4.90 m after (301 samples per arc). The earlier
minimum-only 5.56-m result was a transient and is superseded. The selected 0.10
TCS run measured 3.58/9.52 s to 100/200 km/h, and time >=6370 rpm fell from about
1.26/0.74 s to 0.22/0.28 s in first/second. A stricter 0.05 comparison measured
3.74/9.70 s and 0.20/0.12 s near redline; 0.10 retains better acceleration.
See `DYNAMICS_FOLLOWUP.md` for the bounded comparison and source references.

The fixture now brakes continuously from 200 km/h and interpolates the 70-mph
measurement start. Its original velocity reset left wheel angular velocities
inconsistent and produced invalid 59–60-m braking results in some TCS trials;
those results are rejected. Continuous baseline and selected runs both give
about 40.23 m from 70 mph. Brake torque remains 3600 Nm.

Time >=6370 rpm is explicitly a near-redline proxy, not exact limiter activity.
The native `revLimiterActive` flag is a coroutine pulse with a configured zero
cutoff duration and was missed even on the headless baseline. A zero sampled
flag is not evidence of zero intervention. Fixture acceptance uses bounded
near-redline time and steady turning, not that unreliable flag observation.
Actual in-game shift feel and sound still need a new gameplay drive/CSV.

### Exterior geometry and alpha

- Disabled exactly EngineA renderers Object_49 and Object_179 after confirming
  their material, mesh ownership and under-hood bounds. Their transforms/bones
  stay intact; no vehicle collider or NavMesh obstacle was changed.
- Trunk paint Object_242 was already selected and deformable. Its separate
  TrunkInterior Object_238 (2473 vertices) was rejected by the broad interior
  material protection and remained as a rigid backing. A fail-closed exact
  renderer/material/mesh-bounds rule now includes it. Both panels have matching
  47-bone/bindpose palettes and use weighted bone 35. Mixed ChassisInterior
  Object_76 remains protected. Exact ChassisGlassInside Object_55 is protected
  completely; its former 21 eligible lower-window vertices were a false positive.
- Actual controller Initialize, surface cloning, bone matrices, pending impact
  worker and repair are exercised on Object_51/53/86/234/238/240/242 at four rear
  contact heights. Main and trunk light parts move; protected Object_51 cabin
  vertices stay unchanged; repair restores exact baselines. At 0.8-m contacts,
  all 1609 selected rear Object_51 vertices and 10180 rear Object_86 vertices move.
  Gated per-impact logs now include contacts, influence, skipped transforms and
  changed counts for these named rear targets. No damage radius was inflated.
- BadgeB's embedded RGB atlas has no alpha and was authored opaque. The authoring
  helper resolves the exact material-to-image reference in the target GLB and
  extracts that PNG because imported textures are non-readable. It preserves RGB
  and dark enclosed logo details while masking only dark background connected
  to the atlas perimeter: 607304 transparent pixels, 49852 preserved dark pixels.
  The persistent target-owned alpha texture uses HDRP cutout at threshold 0.5.
  Other badge, paint and glass materials keep their existing roles.

The new visual appearance and driving behaviour require user in-game acceptance
of the installed final artifact. Headless verification does not validate every
parking location, save transition, collision shape or sound observation.

Final verification (2026-10-21): Unity 2022.3.62f2 full asset/ownership, exact
BadgeB alpha, hidden EngineA, seven rear-surface impact/repair and protected-glass
checks passed. Windows bundle is 20268371 bytes; Mac bundle is 20754857 bytes.
The final corrected physics fixture passed: 0–100 3.58 s, 0–200 9.52 s, 70-mph
stop 40.23 m / 2.60 s, steady body-centre radius 4.90 m (301 samples), four
wheels grounded, peak 575 kW. Final near-redline samples were 0.24 / 0.26 s;
the earlier selected run's 0.22 / 0.28 s differs by one fixed tick per gear.
This measures the >=6370-rpm proxy, not exact native limiter intervention.

External runtime build passed with zero compiler warnings/errors; immediate
ModsLocal installation succeeded. DLL and Windows/Mac bundle SHA256 equality
was checked. Installed hashes: DLL
8CC77530B2ED0F27A3B8006996CB89AC3D517577271C9B45348E5E55193B6F86;
Windows A72590E7F1C0AFF11A9C7690CB9DA700F67AAEA7E9C2C13FAD7FE57B049DD95E;
Mac 1172F1400D3CA54D2F185C2550CAE52986B7630C306F11918D549EE2F3B46E60.
Source debug/sub-flags default off. Only local damage/drivetrain diagnostics
remain enabled for the new gameplay test; accepted appearance/wash/parking
markers were removed. PR #51 stays open. In-game driving, badge edges and visible
rear crash/repair appearance have not been observed on this final build.

## Latest feedback: calipers, braking, steering and native condition

User confirms rear badge transparency and rear deformation are correct.
Keep those paths intact. Bind only the exact CaliperAZonePaint zones on the four
active upright assemblies to native SH_Vehicle/bodyMeshes tint. Fixed caliper
material, rotor and badges stay independent. Use per-instance material ownership
and test native SetColor for two vehicles, preview and cancel restoration.

The latest CSV and controlled related-parameter experiments are documented in
DYNAMICS_FOLLOWUP.md. The native condition intensity .5, present since initial
authoring, halves the native impulse-driven damage increment; synchronize it to
.99 across editor, prefab, type and loaded runtime prefab. Retain visual damage
ownership and add focused globally/sub-flag gated condition diagnostics.

Final verification for this follow-up: Unity 2022.3.62f2 full asset/ownership,
eight active caliper bindings, actual native SetColor with distinct inactive
instances/preview/cancel, condition configuration/signature/arithmetic and
accepted seven-surface rear deformation/repair checks passed. Initial doubled
caliper selection was rejected: eight disabled source calipers remain for source
ownership and are excluded; only eight upright renderers enter native bodyMeshes.
Root-inactive templates are supported by checking activity within each upright,
not activeInHierarchy across the intentionally inactive vehicle owner. Generation
binds paint after wheel extraction; repair prepares the vehicle root with exact
caliper-only sibling inclusion. Runtime catalogue steering is synchronized to34.

Windows/Mac bundles: 20269895 / 20755384 bytes. Final unchanged physical setup
measured 0-100 3.26 s, 0-200 9.20 s, 70-mph stop 36.54 m / 2.37 s, steady turning
radius 4.58 m / 301 samples, near-redline proxy .24/.26 s. Small .04 s variation
to the selected run is retained as measurement variation, not a new tune.

External runtime build passed with zero compiler warnings/errors. Immediate
ModsLocal installation succeeded (no queued install). SHA256 equality:
DLL F3339B623F39ACA6A10AC81811507CB88E690BEE3BCD629BE8EA465C7210FFC0;
Windows C32EC063047F6E98F58306676F5C5338CD014E10C7DDDB887E092C5CBD503629;
Mac C41509CF69D32B42CCC0E0C3DCACC51732A92C32AEA7F70B9C548981B919230C.
Source global and feature debug defaults remain false. Only local installed
appearance/damage/drivetrain markers are enabled for the next test. No game was
launched or observed for this artifact. Brake feel, native impact percentage,
caliper repaint/save/reload and the higher downshift need gameplay acceptance;
PR51 remains open. No navigation or AutoPark logic was changed in this follow-up.

## Follow-up 2026-10-22: NPC traffic construction and tighter steering

User accepts body-color calipers and downshift behavior; braking feels improved.
Latest DeveloperTools CSV vehicle-diagnostics-20261022-204624.csv is analyzed in
DYNAMICS_FOLLOWUP.md: mean 0-100 3.373 s, one complete 0-200 9.400 s, observed
333.902 km/h. No further power/brake/downshift tuning is justified by this run.
The capture has no steering inputs, so it cannot measure the turning circle.

Raise front lock and the runtime/serialized type proxy together from 34 to36°.
Controlled Unity fixture: steady body-center radius 4.30 m vs4.58 m previously
(301 samples); 0-1003.26s/0-2009.20s, continuous70mph stop36.54m/2.37s and
near-redline proxy .24/.26s remain unchanged. This is a gameplay steering proxy,
not a reproduction of Bentley's rear-axle steering law.

NPC root cause is proven against the installed game's actual asset, not guessed
from an inactive fixture. The current Player.log repeatedly reports traffic
prefab renderer-reference setup failure before CarType insertion. Loaded the
installed aa/catalog.json and Prefabs/Vehicles/AnselmoAF90.prefab in isolated
Unity using the game's Addressables settings. Its root contains CarFeatures,
Gley VehicleComponent/VehicleLightsComponent, Rigidbody, random color/dirtiness
and native sound/rescue helpers, but NO player VehicleController. Bentley's
ConfigureVehicleRenderers required that absent player component in a combined
gate and therefore rejected the valid NPC template before pool registration.

Keep CarFeatures and paint bindings mandatory; assign player-only controller
references only if that component exists. Preserve the native Gley ownership,
unique Bentley pool entry, inactive template and exact shared-state cleanup.
Split actionable failure messages by missing component/discovery/array assignment.
No additional polling, pool clearing or broad mutation is introduced.

Actual native-template CreateAiPrefab now passes: template stays inactive,
14 native paint bindings (six body/eight upright calipers), native Gley/audio
components retained, appearance helpers attached. A permanent Editor regression
fixture has native CarFeatures paint bindings but no player VehicleController, and asserts exactly
six body/eight caliper bindings with no hidden source calipers. It would fail the
old mandatory-controller gate. Live traffic spawning/paint/wheels and coexistence
still require a game restart and observation; an Editor prefab build is not an
ingame traffic test. Caliper activity/ownership logic is preserved.

Final verification: real native NPC template build PASS and final permanent
renderer fixture PASS (body6/calipers8/hidden0), all existing asset/paint/wash/
condition/rear deformation+repair checks PASS. The initial synthetic attempt
added a blank Gley component and triggered its Editor OnValidate because native
settings were absent; the minimal permanent renderer test no longer constructs
that invalid component. Actual Gley ownership is tested separately on the fully
initialized original game prefab, which builds without that error. Windows/Mac
bundles built and the 36° fixture passed as measured above.

External runtime build: zero compiler warnings/errors. Immediate ModsLocal
installation succeeded. SHA256 equality was verified for DLL and both bundles:
DLL E7A79103758C3620DDDAF6D3600E2B2275F75DC44A7BCF38216F3C473043DBE4;
Windows6DE0235A3E21E10EBD2900AA33EFBFE89ED79DC043C30981D68B330998C573E3;
Mac C8857215C59EB3F1EAAE53AA8A3BE05C61F5AC1FCB95FDE8C7D38BD11B0D58DC.
Source global/sub-debug defaults remain false. Added an opt-in installed
private-driver-diagnostics.enabled marker to use existing gated pool/binding
traces; local damage/drivetrain markers remain. Accepted appearance marker is
removed. No changed gameplay was observed in game; require restart and traffic
spawn/paint/wheel/coexistence and tighter-turn acceptance. Keep PR51 open.

## Follow-up 2026-10-23: brakes, damage, precise turn radius and NPC suspension

User requests shorter braking, slightly reduced condition damage, radius4.15m;
accepts upshifting. Subsequently clarifies jumping traffic affects only Bentley
NPCs, not vanilla/other mod cars. Keep existing task/worktree/PR51.

Native condition intensity .99→.80 (19.2% less uncapped damage at equal impulse).
Authoring now explicitly writes both VehicleType and native DamageHandler rather
than depending on a prior serialized scalar. Runtime defaults match. Native
clamp.99, impact thresholds and accepted visual deformation remain intact.

Braking experiments and acceptance windows are in DYNAMICS_FOLLOWUP.md. Torque
4700Nm alone gives36.38m vs36.54m before; permanently higher .56 grip gives32.48m
but unwanted2.90s launch. Selected native-brake-event pattern adapted from Audi_R8
uses authored.50 grip during propulsion, .56 while braking, restores on release/
disable/unload and rebinds after native lifecycle. No polling/shift/TCS changes.
Actual component in Play Mode:32.48m/2.10s,3.26s100/9.20s200; grip activated and
restored. Native front lock37.15° gives4.15m settled body-center radius/301samples;
integer catalogue/runtime proxy37. Engine curve, ratios, shifts, limiter and
TCS.10 unchanged. Initial editor-only fixture setup lost listeners at domain
reload and was rejected; final fixture sets up in Play Mode.

NPC suspension analysis used the installed native Addressables Anselmo template
and actual Gley IL. Wheel objects/transform references are clone-owned; no shared
vanilla traffic mutation was found. Gley.Initialize translates wheel origins UP
by radius+maxSuspension/2 and computes COM from the resulting mean positions.
Bentley previously retained donor ray-origin Y. A cached donor can already be
initialized; preserving that height makes the following initialization add it
again. Controlled native-template reproduction: raw-built beforeY0/after~.47;
initialized donor~.4626; old Bentley from it after~.934–.935, but ray lengths only
~.562–.564. Direct road-plane raycasts in that legacy state hit0/4. Current game
logs did not record donor Y, so that cache state is a reproduced failure mode,
not a directly sampled live-game cache value.

Construction now derives the pre-initialization origin from the target tire
bottom (center.y-radius), retaining native suspension travel and Gley ownership.
Raw and initialized-template cases both finish~.471–.472 and hit4/4 after native
Initialize. Existing gated NPC trace records donor and normalized Y. No damping
hack, gravity/velocity override or repeated position repair added. Permanent
Editor check verifies initialization-state independence plus actual road raycast;
disposable real-asset probe asserts raw4/4, initialized4/4, legacyinitialized0/4.
Full traffic motion/cessation of jumping still requires an in-game observation.

Final verification2026-10-23: Unity2022.3.62f2 all existing asset/paint/wash/
condition/rear-deformation+repair checks and new NPC ray-origin check passed.
Original-game prefab probe passed raw4/4/initialized4/4 ground hits; legacy
initialized comparison misses0/4. Windows20269897bytes/Mac20755386bytes built.
Final actual brake-event fixture PASS:32.48m/2.10s from70mph,3.26s100/9.20s200,
radius4.15m/301samples; grip.56 duringbrake and.50 restored; nearredline.24/.26s.
External DLL build0 compiler warnings/errors, immediate ModsLocal install.
Installed SHA256 equality: DLL3852DEDF27EF7D5401022220FE1F96C9948430BE91B144B18BB176FA2CD7DC01;
WindowsFE82FB28B15A91C497A45A64F3657AE766AD412DF290DCEA31E7502D486C783E;
MacBBC2842109B6BA3B285F1D8A9CA78B7078B5CE8C85C59CD722C5228C16D584BC.
Source global/sub-debug defaults off, local damage/drivetrain/private-driver
markers retained. No changed behavior observed ingame. Require restart and
acceptance for brake feel, condition loss,4.15mturn and absence of NPC bouncing.
PR51 remains open; no merge/publication.
