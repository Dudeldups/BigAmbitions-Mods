# Camaro damage trace v1

Purpose: investigate visible deformation on normal roads while vehicle condition remains 100%.
This is an observation-only build, not a road-damage fix. No damage, mesh, physics,
collision-filter or deformation-queue writes are performed by the observer.

## Existing code findings

- The custom visual damage callback uses total collision.relativeVelocity.magnitude,
  a 5 m/s threshold, DamageHandler.IsCollisionValid, and a 0.5-second cooldown.
- It writes mesh vertices independently of vehicleInstance.damage and NWH Damage.
- Legacy deformation is disabled and NWH meshDeform is configured false. Neither
  alone prevents the Camaro custom visual callback from denting panels.
- AudiRS6RRoadDamageGuard distinguishes upward road contacts from obstacles and
  protects saved damage/deformation queues. Audi is a read-only reference here;
  its implementation was not copied as an active guard or changed.
- Road contacts are a plausible hypothesis, not yet confirmed by a Camaro trace.

## Automatic collection

The coordinator attaches only to the selected/entered Camaro by exact vehicle type.
There are no global vehicle scans and no dependency on audio initialization.
A drive session begins when controlledByPlayer is true and ends on exit/unload.

Player.log contains [CamaroDamageTrace] SESSION_START, BOUND, limited important
observations and five-second summaries. The existing first-six inward-dent logs
are enabled separately without enabling paint, traffic or other debug channels.
The new observer continues recording counters and mesh changes beyond those six.

Details are written under:

    <Application.persistentDataPath>/Chevrolet_Camaro_1967/Diagnostics/damage-trace-*.log

The session-start line prints the full filename. Filenames use UTC plus vehicle ID
and a unique suffix. Each drive gets a separate file; earlier files are preserved.

## Recorded evidence

- CONTACT_ENTER and sampled CONTACT_STAY: collider identity/path/layer/tag, native
  collision validity, contact points and normals, penetration separation, vehicle
  uprightness, total relative speed, normal/tangential speed components, impulse,
  pre-physics velocity, current velocity, frame/fixed time, raw damage and queues.
- roadCandidate uses Audi-style identity checks and all contact normals, rather
  than suppressing anything. It is a diagnostic classification only.
- STATE_CHANGE: raw saved/NWH damage, saved deformation count and the custom
  callback completion counter. A completed callback can change zero vertices;
  it is deliberately not labelled proof of a dent.
- MESH_CHANGED: actual vertex changes relative to the last observed copy, changed
  vertex count and maximum world-space displacement, with the observation interval
  and associated contact/callback counters. Correlation is not a captured call stack.
- MESH_REPLACED distinguishes mesh swaps (including possible repair/reinitialization)
  from ordinary vertex changes. Baselines are observed state, not pristine geometry.
- Unavailable reflected fields/queues are reported as -1, not zero.

## Overhead and limits

One mesh is inspected per rendered frame; buffers are reused, and complete passes
normally have a one-second gap. No mesh is modified by this inspection. Scans over
8 ms are reported as SCAN_COST. Short-lived changes between observations can be missed.
Details are capped at 20 lines/second and 8 MiB/session; skipped-line counters and
five-second summaries remain visible. Persistent contacts are sampled every 0.5 s.
Buffered files flush every second and on exit/unload. Capture Player.log as well
as all detail files from the test so any caps or unavailable bindings are visible.

## Test

After installing, enter the Camaro and idle for about five seconds to establish
mesh baselines. Drive straight and through ordinary bends without striking an
obstacle. Stop and exit. Provide Player.log plus the trace file(s) for that drive.
A later controlled obstacle test can distinguish a necessary road guard from
legitimate crash deformation; no obstacle damage is suppressed in this build.

## Audio scope and verification

Idle gain only: 0.64 -> 0.80 (+25% linear gain). EngineBaseVolume=0.29 and
EngineThrottleVolume=0.26 remain unchanged, as do the audio controller, generator,
waveforms, pitches, blend rules and the accepted burble mix.

Static source/diff review only. Local execution tools were unavailable during
implementation; Unity/net472 compilation and in-game validation were not run.
