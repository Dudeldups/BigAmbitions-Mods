# McLaren Senna vehicle setup

The setup uses the 2019 road-going McLaren Senna specifications and the supplied Senna GLB.

| Setting | Value used | Basis |
| --- | ---: | --- |
| Engine | 3,994 cc twin-turbo V8 | Manufacturer data |
| Maximum power | 588 kW / 800 PS at 7,250 rpm | 2019 technical specification |
| Maximum torque | 800 Nm from 5,500 to 6,700 rpm | 2019 technical specification |
| Rev limit | 8,500 rpm | Manufacturer specification |
| Transmission | 7-speed seamless shift gearbox, rear-wheel drive | Manufacturer specification |
| Forward gear ratios and final drive | 3.982, 2.612, 1.905, 1.479, 1.160, 0.906, 0.686; final drive 3.308 | Senna owner's handbook |
| Reverse gear ratio | 2.800 | Approximation used for the game physics; the published handbook does not list a reverse ratio |
| Kerb mass | 1,309 kg | Manufacturer specification |
| Top speed | 335 km/h | Manufacturer data |
| 0â€“100 km/h | 2.8 s | Manufacturer data |
| Length Ã— width Ã— height | 4,744 Ã— 2,153 Ã— 1,195 mm | Manufacturer data |
| Wheelbase | 2,670 mm | Senna owner's handbook |
| Tyres | 245/35 ZR19 front; 315/30 ZR20 rear | 2019 technical specification |
| Track | 1,645 mm front; 1,610 mm rear | Measured from the supplied model's wheel anchors |
| Game purchase price | $958,966 | Original U.S. launch price |

The NWH power curve follows the published torque plateau and 7,250 rpm power peak. Suspension travel, tyre friction, brake torque, collider shape, and centre of mass are game-physics tuning values; they are calibrated against the vehicle mass, dimensions, staggered tyre sizes, and the RS6R, M4 G82, and 911 GT3 RS integration patterns.

The model has separate front and rear wheel groups plus independent caliper and badge groups. These are scaled and attached to the target wheel controllers at the factory tyre dimensions. Paint, carbon, windows, lamps, interior, and wheel materials are kept in their authored GLB groups.

## Sources

- [McLaren Automotive: Senna specifications and performance](https://cars.mclaren.com/gl_en/ultimate-models/mclaren-senna)
- [McLaren Senna owner's handbook](https://mclaren-web.s3.eu-west-2.amazonaws.com/files/Senna%20-%20Owner%20Manual%20-%20P15%20-%20APAC%20-%20English%20-%2015QA026CP.pdf)
- [2019 McLaren Senna technical specification](https://asset.moto.it/pricelist/auto/0b9a80156b709210e015540cd351bf71/scheda-tecnica-2019.pdf)
- [McLaren's Senna launch specification and performance figures](https://www.senna.com/en/mclaren-senna-novos-fatos-estatisticas-e-detalhes-revelados-antes-do-salao-do-automovel-de-genebra/)

## Runtime feedback calibration — 2026-10-03, 17:12 test drive

The reproducible report is `tools~/drive-analysis-20261003-171222.json`.
The analyzer qualifies each run separately after repair, rejects valid collision
contacts, damage above 0.5%, steering/cornering, pedal interruptions and lost
wheel contact. Ground-plane contacts marked invalid for damage are ignored.

- Qualified 0–100 runs: 2.309, 2.270, 2.220 and 2.303 s (target 2.8 s).
- Native generated power reached about 499.5 kW: the 85% driveline modifier was
  active. An additional native engine power modifier of 0.81 now calibrates the
  game's longitudinal force response while preserving the 588 kW crankshaft
  specification, 800 Nm torque curve, actual mass and factory ratios. This is
  an empirical game factor, not a claim about the real car's mechanical losses.
  New acceleration timings need a fresh clean CSV; they are not yet confirmed.
- Qualified 100–0 distances: 23.317, 24.264, 24.247 and 23.565 m. Their average
  is 23.848 m, close to the 23.6 m target (29.5 / 1.25). Brake calibration stays
  at 2,100 Nm through 100 km/h and rises to 2,800 Nm at 200 km/h. The observed
  88.970 m stop from 200 involved steering and is not a qualified baseline.
- Recorded RPM stayed at or below the manufacturer's 8,500 rpm limit. Brief
  limiter contacts occur around launch/upshift; no new limiter or shift setting
  is inferred from a damaged or turning high-speed run.
- Front impacts at 153.646 and 200.115 km/h relative speed had impulses of
  55,875.78 and 72,303.51 Ns. The old native damage intensity 1.0 (internally
  capped at .99) saturated health to 100% in a single contact. Native intensity
  is now .45, matching this mod's vehicle asset. Damage remains native, saved,
  impulse-based and capable of destroying the vehicle in severe/repeated hits.
- Rear contacts and visual/caliper centers move 5 mm forward; front longitudinal
  positions remain fixed. Front wheel support is lowered 10 mm relative to the
  body to lift the front chassis while preserving the rear support height.
- The GLB wheel groups contain a 1.5-degree authored camber. Their intrinsic
  tire cross sections are circular; previous body fitting and spin about a
  different axis produced wobble. Four owned meshes now bake intrinsic geometry
  around the native axle, with uniform radial scaling, centered rims/tires and
  unit-scale rolling transforms. Original UVs and material atlas are preserved.
- Driver grips move another 20 mm rearward and yaw outward by opposite 5-degree
  rotations. Animation restoration prevents accumulated pose changes.
- The bounded front dent field includes the low carbon splitter even for a
  high contact. The fixture checks its deformation alongside lenses and spoiler,
  source/instance isolation and exact repair restoration.
- A dedicated synthesized `EngineIdle.wav` adds flat-plane firing harmonics and
  exhaust pulsation. Loaded sound levels, master/mute routing and the previous
  high-RPM pitch compression remain intact. The four-second idle loop peaks at
  .86 with no clipping; its components and modulation complete whole cycles.

Unity fixtures and successful build/install verification establish authoring
and package correctness. Wheel clearance, grip appearance, crash feel, idle
character and actual updated acceleration still require an in-game test.

## Runtime feedback — 2026-10-03, 21:15 test drive and crash screenshots

- The front reservoirs/fasteners and rear silver supports belong to the imported
  EngineA mesh, which was previously excluded with the central engine. Its 490
  welded triangle islands are now partitioned into front supports (1,342
  vertices), rear supports (14,260) and central engine (5,724). No triangle is
  dropped or duplicated; UV channels, original normals/tangents and material
  ownership are retained. Whole islands avoid cutting open a support or pipe.
  Only the front/rear support meshes join the instance-owned dent field; the
  central engine remains unchanged. Repair restores every clone exactly.
- Attachment classification is cached before damage so shrinking panels cannot
  switch into rigid-detail mode after repeated hits. A cumulative 0.50 m limit
  also applies after rigid-detail averaging; native health damage is unchanged.
  The fixture exercises six further severe front/rear impacts, source isolation,
  central-engine exclusion and exact mesh/binding/normal/tangent restoration.
- Driving engine volume is multiplied by .88 (12% lower, about -1.1 dB) at all
  loads. The dedicated idle bed, pitch mapping, master/mute and horn are unchanged.
- Report: `tools~/drive-analysis-20261003-211556.json`. The 21:07 CSV is a Dodge
  Ram 1500 TRX session and is excluded. In the Senna drive no valid collision was
  recorded; gradual wear grows from 0 to 4.2%. The analyzer now distinguishes
  this small wear from abrupt damage instead of discarding all later driving.
- Observed 0–100 timings repeat around 2.13 s; observed 0–200 around 6.18 s.
  Track steering corrections (typically .10–.15) make these unqualified strict
  straight baselines. Native generated power peaks at 404.523 kW, confirming the
  existing .81 game calibration is active. Low-speed longitudinal acceleration
  nevertheless reaches about 1.4 g. Forward full-throttle launch torque is now
  additionally limited to .65 through 80 km/h, smoothly restored through 120.
  Gentle throttle, idle, neutral and reverse retain the previous calibration.
  This is an empirical native torque/power modifier, not a velocity multiplier
  or a change to the published engine specification. Updated timings require
  a clean straight drive; the real 2.8 s / 6.8 s targets are not yet confirmed.
- Observed full-pedal stops: 100–0 about 23.65–24.54 m; 200–0 about 85.42 m.
  They also contain steering corrections, so they remain observations. The
  earlier qualified 100–0 baseline remains the basis for unchanged brakes.
- RPM is bounded at 8,500; four limiter events appear in the whole session.

The bundle/source checks include deformation of both newly owned support
regions and restoration after repeated severe impacts. In-game protrusion,
crash feel, driving loudness and the new launch timing remain user verification.


## Runtime feedback - 2026-10-11

See `Config/DRIVE_TUNING_20261011.txt` for the measured qualified runs, sources,
calibration estimates and next runtime checks. The latest CSV is saved as
`tools~/drive-analysis-20261011-121821.json`. Headlight cones move 12 cm forward,
maximum steering increases from 35 to 37 degrees, native acceleration/braking
are calibrated toward 2.8/6.8/17.5 s and 23.6/80 m. The rolling wear source is
the optional Speedometer's 1%/km damage; a McLaren-owned event guard removes only
that contribution while retaining real impact/explicit damage and repair.
Amarok's cached arrays/matrices/contact fields/bounds rejection and preserved
authored shading remove synchronous mesh rescans from the collision path. Full
exterior coverage and exact repair are retained. Achieved timings, brake distance,
headlight alignment, steering and crash hitch require installed in-game checks.


## Runtime feedback - 2026-10-12

The user confirms the turning circle and disappearance of ordinary-driving
damage. The new clean CSV confirms 2.811/6.802 s to 100/200 and mean stopping
distances 23.490/80.428 m, close to the 23.6/80 m targets. These calibrations
are retained. Only the high-speed native torque envelope is adjusted after
18.168 s to 300, with a new 17.49 s estimate pending an installed drive. Front
visual dents are reduced 10%, rear 15%, including depth floors/caps. Native
health damage stays intact. A complete Mac bundle and its seven required NWH
references are added using the same dependency correction as private PR 32.
See `Config/DRIVE_TUNING_20261012.txt` and the latest JSON report for details.


## Wash bay / intended wear correction - 2026-10-12

The mileage-wear exception is removed at the user's request: normal Speedometer
wear remains intended. Native wash VFX was missing, aborting the native wash
coroutine after it started audio and blocked driving. Target-owned wash effects
and validated native audio/VFX bindings restore the original native completion
path. See `Config/WASH_AND_WEAR_20261012.txt` and the 13:28 CSV report. The latest
2.809/6.803/17.398 s and 23.507/79.607 m means support unchanged performance.
Actual wash/drive-away behavior still needs the installed in-game test.

## Parked purchase paint / accepted driving - 2026-10-12

The user confirms a complete wash followed by driving away and accepts driving
and performance as complete. The established dynamics calibration is retained.

Purchased vehicles previously relied on later game/entry callbacks to initialize
the McLaren's HDRP paint slots. Native CarFeatures color targets different shader
properties, so the parked purchase kept the authored color until first entry.
The target prefab now includes a one-shot Start appearance initializer, after
native VehicleInstance assignment and shader remapping and before rendering.
It uses the same instance-owned material/glass/caliper/paint setup as entry.
The saved ordered color also takes precedence over a stale native preview color.
No recurring scan, polling, or paint watcher is introduced. Paint diagnostics
require both existing global and paint flags, disabled by default.

The Editor fixture checks two parked ordered colors, stale native preview state,
instance isolation, unchanged shared trim, repeated setup and repaint/reentry.
The new installed dealer purchase still requires the user's in-game confirmation.

## NPC traffic / Private Driver correction - 2026-10-12

The user confirms correct dealer purchase paint before entry. NPC traffic and
chauffeur spawning were missing because the dealer layout-loading early return
also skipped MainScene's pre-initialization traffic-pool registration window.
Traffic preparation now precedes that dealer-only gate. Contract eligibility
requires a prepared pool, and late live-pool append remains prohibited.
The chauffeur/NPC visual set includes all four fixed calipers; native collision
cache references point to the active target body after template-body replacement.
The native Gley allocator fixture verifies two complete pooled Senna instances,
repeat initialization, late-append refusal and foreign cache/pool preservation.
See Config/TRAFFIC_PRIVATE_DRIVER_20261012.txt for evidence and cold-load checks.
Actual traffic and chauffeur arrival/departure require installed in-game testing.

## NPC paint / wheel ownership correction - 2026-10-12

The user now confirms ambient Senna spawn, but reports gray NPC paint and some
sunken wheels. Ambient paint explicitly selects from the game's weighted palette
on activation; saved chauffeur paint remains authoritative. Four centered target
hubs become the native Gley wheelGraphics, with measured front/rear radii and
recomputed circumference/raycast/axle metadata. The separate rolling-wheel
baseline correction is removed. Calipers retain position/steering animation.
The wheel-only Opel 3808301889 comparison and other-mod pattern audit are recorded
in Config/NPC_VISUALS_20261012.txt. Only McLaren is changed. Native Gley job and
palette fixtures pass; varied NPC colors/road contact still need game confirmation.
