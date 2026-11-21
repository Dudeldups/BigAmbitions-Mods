# Bentley Continental GT Speed dynamics follow-up

## Evidence collected

The latest DeveloperTools telemetry is `C:\Users\Arne\AppData\LocalLow\Hovgaard Games\Big Ambitions\DeveloperTools\VehicleDiagnostics\vehicle-diagnostics-20261021-111915.csv` (392 samples). Across five 0–100 km/h runs, elapsed time averaged 3.54 s (3.50–3.60 s); the one complete 0–200 run took 9.55 s. RPM first reached 6,500 in first gear near 20 km/h, while the automatic transmission held first until about 55 km/h. Maximum powered-wheel slip was 1.0 in first and 0.451 in second; second-gear samples around 60–76 km/h include 0.289–0.451 slip. There is no steering input above 0.8, so this CSV does not establish a turning-radius baseline.

The same final flat-ground NWH fixture and measurement protocol produced these relevant comparisons:

| Setup | 0–100 km/h | 0–200 km/h | 1st/2nd time at >=6,370 rpm* | Turn median |
| --- | ---: | ---: | ---: | ---: |
| No TCS, 27° steering baseline | 3.18 s | 9.12 s | 1.28 / 0.72 s | 5.90 m |
| Native TCS 0.20, 31° steering | 3.50 s | 9.44 s | 0.28 / 0.40 s | 5.08 m |
| Native TCS 0.10, 32° steering | 3.58 s | 9.52 s | 0.22 / 0.28 s | 4.90 m |
| Native TCS 0.05, 32° steering | 3.74 s | 9.70 s | 0.20 / 0.12 s | 4.90 m |

*These dwell values use the fixture's >=6,370 rpm near-redline proxy, not NWH's native `revLimiterActive` flag; they do not establish exact time under limiter intervention. The final 0.10/32° result measured maximum RPM of 6,825/6,745, continuous 70 mph braking of 40.23 m, and turn median 4.90 m over 301 samples. The bounded 0.05/32° comparison measured max RPM 6,825/6,583, braking 40.23 m, and the same 4.90 m turn median. Compared with 0.10, threshold 0.05 reduced first-gear proxy time by 0.02 s and second-gear time by 0.16 s, but slowed 0–100/0–200 by 0.16/0.18 s. Select 0.10: it matches the native default and keeps both measured near-redline intervals below the 0.30 s regression limit, while retaining better acceleration. The no-TCS baseline had 1.26/0.74 s near-redline dwell. An earlier 59 m brake result was invalid because the old fixture teleported velocity and contaminated wheel state. These fixture measurements are not in-game runtime validation.

The 392-sample CSV includes native `rev_limiter` transition events: 10 in first gear and 5 in second across five acceleration runs. Its sampled `rev_limiter` field was true in 6 first-gear and 3 second-gear rows. Summing the next same-gear sample interval for each true row estimates about 1.29 s total in first and 0.41 s in second (about 0.26/0.08 s per run). This estimate is bounded by the roughly 0.20 s sample spacing, and the event rows record entry without exit timestamps; it is not an exact active-time measurement. The new fixture reads the native boolean each fixed step and provides the finer per-run measurement.

## Native drivetrain diagnosis and current test

The installed `ExternalPlugins.dll` native NWH implementation calculates automatic shift RPM from `Vehicle.LocalForwardVelocity`, average wheel radius, and current gear ratio (`CalculateNoSlipRPM`). `AutomaticShift` compares that road-speed estimate with the upshift target, not actual engine output RPM. The configured upshift target is 6,200 rpm and the limiter is 6,500 rpm, but wheelspin makes the road-speed estimate lag engine RPM. The measured RPM, slip, and prolonged first-gear dwell are consistent with delayed shifts. Lowering the shift threshold would hide the symptom without addressing the observed mismatch, so the upshift schedule, limiter, global power, and gear ratios remain unchanged.

The installed native NWH TCS wrapper registers an engine power modifier. It checks powered grounded wheel longitudinal slip, skips during shifts, and sharply limits engine output to 1% while slip exceeds `slipThreshold`. The wrapper has no configurable TCS enable flag in this build; its `active` field is runtime telemetry, while `lowerSpeedThreshold` is not read by the limiter. A bounded comparison against observed 0.13–0.45 second-gear slip found 0.05 gave only a small first-gear near-redline reduction and a 0.16/0.18 s acceleration penalty. Keep 0.10, the native default, which holds both near-redline proxy durations below 0.30 s with the better 3.58/9.52 s times. Do not raise the RPM limit or global power.

NWH's `EngineComponent.CalculateTorqueICE` starts a coroutine at the limiter RPM. The coroutine yields one frame before setting `revLimiterActive`, invokes `onRevLimiter`, then waits for `revLimiterCutoffDuration` before clearing the flag. The configured cutoff duration is zero. Editor fixture polling observed zero active-flag duration even when the engine reached 6,825 rpm, so this transient flag cannot be used as a reliable dwell assertion in that fixture. Do not interpret zero sampled flag time as proof that no limiter cut occurred. The fixture instead reports the explicit near-redline proxy (engine RPM >= 6,370) and bounds it to 0.30 s per gear; this distinguishes the 0.22/0.28 s intervals on the selected 0.10 setup from the 1.26/0.74 s baseline plateaus without claiming exact limiter-active time. The 0.05 comparison produced 0.20/0.12 s, but its acceleration penalty was too large for the small first-gear improvement. The 32° turn result meets the radius goal.

## Steering proxy

Bentley describes all-wheel steering on the MY25 GT Speed, but the reviewed public MY25 material does not provide its rear-steer angle or turning-circle figure. Bentley's older official press release describes opposite-phase rear steering at low and medium speeds for the previous generation, which is not a verified MY25 angle. NWH's prefab has no rear-axle steering model. The current 32° front lock is therefore a modest low-speed gameplay proxy, increased from 27°; it does not reproduce the OEM rear-steering control law. The 31° test reached a 5.08 m median, just above the 5.00 m target. The assigned 32° run checks whether the small front-lock increase closes that gap without changing tire grip.

Bentley's official data for the fourth-generation GT Speed lists 782 PS, 1,000 Nm, an eight-speed dual-clutch transmission, AWD, 0–100 km/h in 3.2 s, and 335 km/h top speed. Existing system-power, braking, tire-grip, and ratio targets remain unchanged. No exact model-year-2025 GT Speed gear-ratio set was verified.

## Code changes and verification state

- `Editor/BentleyContinentalGTSpeedDrivingSetup.cs` selects native NWH TCS at 0.10 and 32° front steering; corresponding `VehicleType.turnRadius` proxy matches the steering angle. Power, brake torque, tire grip, and transmission ratios/shift schedule remain unchanged.
- `Editor/BentleyContinentalGTSpeedAssetChecks.cs` verifies the serialized 0.10 threshold and matching 32° steering/catalogue values.
- `Editor/BentleyContinentalGTSpeedPhysicsFixture.cs` reports per-gear time >=6,370 rpm as a near-redline proxy and requires <=0.30 s per gear, plus a settled low-speed full-lock radius median no greater than 5.00 m. Braking remains continuous from 200 km/h with an interpolated 70 mph measurement start.

The 0.05/32° experiment is complete and 0.10/32° is selected for the final setup. The parent will rerun the corrected fixture on 0.10 before build/install/commit; the fixture does not claim to measure exact native limiter-cut duration. No build, installation, in-game test, or commit was performed by this dynamics worktree.

## Source references

- Bentley Newsroom, [2024 new Continental GT Speed technical release](https://www.bentleymedia.com/en/newsitem/1601-the-new-continental-gt-speed-redefining-the-definitive-grand-tourer): system output, torque, transmission, AWD, and steering technology.
- Bentley Newsroom, [2025 awards release](https://www.bentleymedia.com/en/newsitem/1681-the-continental-gt-extends-its-trophy-cabinet-with-a-trio-of-prestigious-awards-in-europe): 0–100 km/h and top speed.
- Bentley Newsroom, [previous-generation GT Speed chassis technology](https://www.bentleymedia.com/en/newsitem/1200-the-most-dynamic-bentley-road-car-in-history-the-new-continental-gt-speed): low/medium speed rear-steering phase; context only, not an MY25 angle specification.
- NWH installed runtime source of truth: `D:\SteamLibrary\steamapps\common\Big Ambitions\Big Ambitions_Data\Managed\ExternalPlugins.dll` (read-only IL inspection of transmission and TCS implementation).
- Latest telemetry: the CSV path listed above; schema and output location are defined in `Assets/Mods/DeveloperTools/Scripts/DeveloperToolsVehicleDiagnosticsService.cs`.

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

## Follow-up: body-color calipers, braking, steering and condition (2026-10-21)

Latest Bentley telemetry: `vehicle-diagnostics-20261021-212947.csv` from the
DeveloperTools/VehicleDiagnostics directory under the game's LocalLow data.
The comparable full-brake runs stop from 70 mph in 40.46 and 40.32 m (mean
40.39 m); a separate Lamborghini capture is excluded. All four wheels remain
grounded. The capture starts at .742 saved damage and ends at .776 without
recorded collision contacts, so it cannot establish damage per impact.

Tune the related longitudinal group together: forward grip .46 -> .50 and
per-wheel base brake torque 3600 -> 4000 Nm, preserving front/rear brake
coefficients 1.0/.55. The .50/3600 experiment measured 38.32 m; .50/4000 measured
36.54 m / 2.37 s from 70 mph with continuous braking from 200 km/h, no artificial
wheel/body velocity reset. Front lock 32 -> 34 degrees gives settled body-center
radius 4.58 m versus 4.90 m before (301 samples). Downshift threshold rises only
200 RPM, 3600 -> 3800. Native TCS .10, ratios, power and limiter stay unchanged.
The same .50/4000 run measured 0-100 3.26 s and 0-200 9.24 s; near-redline proxy
>=6370 RPM lasted .24/.26 s in gears 1/2. These are headless fixture results,
not observed road-test results or exact limiter-cut dwell.

The installed ExternalPlugins.dll DamageHandler.HandleCollision gates accepted
hits using relativeVelocity.magnitude * 100 against threshold 200 plus collision
timeout .8 s. Enqueue adds:
`impulse.magnitude / (fixedDeltaTime * rigidbody.mass * 10) * Clamp(intensity,0,.99) * .005`,
then clamps cumulative damage to [0,1]. The impulse is solver output;
impulse/(dt*mass) has acceleration units. There is no remaining-health multiplier.
The original .5 scalar has been present since initial Bentley authoring, so no
recent source regression is established. Set native handler, VehicleType and
runtime prefab intensity to .99, giving 1.98 times the uncapped increment for
the same impulse. Do not add a second damage application or modify accepted
visual deformation. Editor arithmetic/signature/config checks do not substitute
for a native physics collision test. Gated one-shot collision logging records
impulse, intensity, native and saved damage around the next-frame synchronization;
callback order means its raw calculated increment is potential, not a guaranteed
before/after delta. Final in-game impact percentage and brake feel remain to test.

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

## Follow-up: latest driver telemetry (2026-10-22)

Read-only analysis of `vehicle-diagnostics-20261022-204624.csv` (60.0 s capture,
279 periodic samples, one Bentley segment; source schema is the DeveloperTools
vehicle diagnostics service). Startup captured the expected setup: 2459 kg,
575 kW configured power, 335 km/h configured maximum, 4000 Nm NWH brake torque,
3800 rpm downshift, 6200 rpm upshift, 0.50 front longitudinal grip (0.50 rear),
1.30/1.35 lateral grip, 34-degree steering/catalogue proxy in this pre-narrowing
capture, and four wheels configured. The four wheel records show radii 0.376 m
front and 0.374 m rear, widths 0.275 m front and 0.315 m rear.

### Straight-line performance and shift observations

- Three full-throttle starts reached 100 km/h in 3.339, 3.393, and 3.386 s
  (mean 3.373 s, median 3.386 s). The longer third run reached 200 km/h in
  9.400 s, so its 100–200 km/h interval was 6.014 s. It continued to
  333.902 km/h. The time from the start to 200 km/h uses the logged milestone
  timer; 100–200 is the difference between two milestones in that same run.
- Bentley's official MY25 reference already recorded above is 0–100 km/h in
  3.2 s, 335 km/h top speed, 782 PS / 575 kW and 1000 Nm. This capture is about
  0.17 s slower to 100 and peaks about 1.1 km/h below the published top speed.
  There is no verified MY25 0–200 benchmark in the stored research, so 9.40 s
  is a telemetry observation rather than an OEM comparison. The top-speed sample
  is a short player run, not a separate convergence test.
- All three runs reached first-to-second and second-to-third shifts. Each logged
  two `rev_limiter entered=true` events in gears 1 and 2; event RPM ranged from
  6374 to the 6500 rpm configured limit. The sampled `rev_limiter` field does
  not provide a reliable active duration. No need to lower power, alter ratios,
  or change the upshift/limiter setup on this evidence.
- Longitudinal slip peaked at 0.195, 0.170, and 0.286 within the three
  full-throttle sample windows; respective means were 0.062, 0.062, and 0.007. Each
  run remained 4/4 grounded. The overall capture's 0.789 slip peak is during
  hard braking at roughly 43 km/h, not launch wheelspin. Thus grip/TCS show no
  acceleration-side fault in this capture.
- Braking downshifts occurred without an obvious refusal: in the first two
  stops gear 3 changed to 2 at roughly 75 and 74 km/h, then gear 2 to 1 near
  47 and 43 km/h. During the high-speed deceleration the transmission stepped
  8→6→5→3→2→1. These events support retaining the accepted 3800 rpm threshold;
  this capture does not include a controlled low-speed coast-down/kickdown test.

### Braking and steering coverage

The longest continuous full-brake interval began at 332.4 km/h and remained
4/4 grounded through the stop. From the sampled speed-time slope, deceleration
was approximately 50.9 km/h/s (1.44 g equivalent) over 300–340 km/h, 50.6
km/h/s (1.43 g) over 200–300, 48.3 km/h/s (1.37 g) over 100–200, and 47.9
km/h/s (1.36 g) over 50–100. These are slopes across periodic samples, not a
brake dynamometer result; the logged longitudinal-g derivative has an isolated
2.61 g sample around 291 km/h while the neighboring speed slope remains near
1.4 g, so speed/time is the more stable summary. At the 70 mph crossing
(112.65 km/h, about 54.98 s), interpolated path distance to rest was about
37.1 m over 2.95 s. This is only a segment of the same stop after braking had
already been applied from 332 km/h for about 4.4 s, so it is not a fresh,
standalone 70 mph braking test and cannot replace the prior controlled 36.54 m
fixture result. The two earlier decelerations started at 106.6 and 102.5 km/h,
below 70 mph, and likewise do not supply an isolated 70 mph result. The
high-speed stop is consistent with firm braking; subjective pedal/response feel
is not captured by this CSV.

No turn measurement is present: `min_steered_turn_radius_m` is `n/a`, and all
279 periodic samples have zero steering input. Therefore this capture provides
no evidence for or against the desired slightly smaller turning circle and
should not drive steering tuning. The captured 34-degree setting predates the
current 36-degree source adjustment; the next comparison needs a deliberate
full-lock circle measurement.

This is in-game driver telemetry, unlike the prior headless fixture, but it is
not a controlled isolated brake/turn protocol. Vehicle diagnostics sample at
roughly 0.2 s, and the capture contains three separate acceleration attempts,
several full-brake decelerations, no recorded collision contacts, no steering
input, and one manual stop. No physics-source changes were made for this review.

## Follow-up 2026-10-23: braking, moderate damage and 4.15 m radius

User accepts upshifting; keep engine power/curve/ratios, native upshift6200,
limiter6500, downshift3800 and TCS.10 unchanged. Moderate native condition
intensity .99→.80 (-19.2% uncapped increment for identical impulse), synchronized
by authoring both the type and native handler plus runtime prefab defaults.
The native .99 clamp and accepted visual damage ownership are unchanged.

Controlled experiments: 4000Nm/.50 baseline70mph stop36.54m;4700Nm/.50 gives
36.38m, demonstrating tire limitation.4700Nm with .56 permanently gives32.48m
but speeds0-100 to2.90s, so that permanently higher launch grip is rejected.
Use the existing Audi_R8 native brake-event pattern: authored .50 launch grip,
.56 on brakes.onBrakesActivate, baseline restoration on brakes.onBrakesDeactivate;
rebind after native initialization, restore/detach on disable/unload. No input
polling, velocity changes, engine torque changes or shift schedule changes.

The fixture attaches the same component in Play Mode after domain reload;
pre-play listener setup was invalid and its first36.38m result is rejected as
a missing binding. Final event-path run: grip.56 while braking and restored.50,
0-1003.26s/0-2009.20s,70mph stop32.48m/2.10s, near-redline proxy.24/.26s,
all4wheels grounded. Stop ceiling now32.9m; acceleration windows3.1–3.5s and
9.0–9.6s catch unintended launch changes.

Native steering37.15° gives settled body-center radius4.15m (301samples).
VehicleType.turnRadius stores integers, so the catalogue/runtime proxy is37;
assert the native fractional angle independently. Fixture target4.15±.04m.
Neither this braking feel nor the new damage calibration is yet observed in game.

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

## Player wheel track and NPC chassis

All four player wheel centers move outward by 0.05 m, taking the steering,
rolling and upright assemblies with them. The front and rear tire meshes
had Y/Z radii of 381.45/379.02 mm and 379.14/377.13 mm. The authoring
pass equalizes the radial axes while retaining the original vertical radii,
tire widths and native NWH animation bindings.

The previous NPC setup shifted the Gley suspension contacts and their graphical
inputs around the Bentley model origin while keeping the donor front trigger
and chassis hierarchy in the native coordinate frame. The road-ray fixture
established road contact but did not exercise dynamic pitch. Bentley traffic
now uses the untouched authored Anselmo chassis, wheel rays, graphics pivots,
rigidbody and collision geometry. The body, lights and seat are aligned to
the native axle midpoint. Separate Bentley wheel meshes follow native
suspension, steering and spin, and the upright meshes omit spin.
This follows the visual/physics ownership used by the Porsche traffic mod.
The native chassis comparison is an editor test. NPC motion in live traffic
and the player wheel appearance in motion require in-game confirmation.

The widened chassis measured 3.95 m at the previous steering lock. At 35.80 degrees the final NWH fixture measured 4.11 m, within the existing 4.15 +/- 0.04 m target, and passed its acceleration, braking, RPM and wheel visual motion checks. The native Anselmo prefab comparison passed with four road-ray hits. The installed DLL and both platform bundles matched the build by SHA-256.

### Wheel and driver follow-up

The player track offset is now 40 mm per corner. A fit to the outer tire face
found mirrored static camber of 1.50 degrees at the front and 1.00 degree at the
rear in the rolling geometry. The inverse angles are authored on the rolling
mesh children to align the tire faces with the unmodified NWH spin axes.
The measured axial variation on the sampled tire faces fell from about
1.6 mm to 0.01 mm. The NWH fixture passed with a 4.15 m full-lock radius.

The NPC body, lamps and cabin anchors sit 40 mm higher; the native chassis
comparison still passed. The traffic occupant uses a visual-only humanoid
copy of the native pedestrian, aligned by the raised Bentley steering anchor.
The isolated Play Mode fixture loaded the production pedestrian Addressable,
verified a skinned seated driver at the expected hip position and checked
cleanup on disable. The build and installed DLL and bundles matched by hash.
Final road clearance and driver visibility through the glass need in-game
confirmation.

### NPC clearance and tuning check

The NPC visual body lift is 70 mm total relative to the player model, 30 mm
above the previous build. The native-prefab fixture verified the new offset
on the body, lamp mesh and steering anchor and zero vertical shift on all
four wheels. The current player NWH fixture passed with 3.26 s to 100 km/h,
9.20 s to 200 km/h and 32.48 m from 70 mph to rest during continuous braking.
The latest Bentley DeveloperTools CSV is from October 22 and predates the
current braking calibration. Its three 0–100 runs were 3.339–3.393 s and
its completed 0–200 run was 9.40 s. No powertrain or brake values were changed.
