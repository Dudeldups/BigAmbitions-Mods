# Bentley Continental GT Speed auto-parking analysis

## Native state transitions

The installed game's `VehicleParkingHelper.ParkInClosestSpot()` consumes the selected `AutoParkSpot` before its first yield, fades, teleports the car to the spot transform, sets `VehicleInstance.parkingState` to `Legal`, then immediately calls `CarController.UpdateParkingZone()`.

`CarController.UpdateParkingZone()` does not update `VehicleInstance`. It probes every native wheel using `wheelUAPI.transform`, `wheel.Radius * 2`, and `LayerHelper.parkingAreaLayerMask`; a missing overlap or a handicap lane makes the HUD state `Illegal`. Its lane lookup is `GetComponent<ParkingLaneGenerator>()` on the first hit, not a parent lookup. `VehicleController.ExitVehicle()` then copies the HUD state into the saved instance, so a failed native probe can overwrite the earlier `Legal` assignment. `CarController.EnterVehicle()` runs the native probe again after loading.

## Bentley evidence and code differences

The current Bentley guard already used `GetComponentInParent<ParkingLaneGenerator>(true)`; the suspected direct lookup was not in that guard. The direct lookup is in the game's native `CarController.UpdateParkingZone()`.

The last available runtime log (2026-10-19 22:39) reported two wheel probes with no lane-collider hit and two hits on `Indicator_70-ParkingLane (1)`. That log predates the installed Bentley DLL (written 23:48), so it cannot establish the wheel results for the build currently being tested. It does show why an unconditional `Legal` assignment would be incorrect. The prefab wheel centers/radii are plausible and its `VariableCenterOfMass.dimensions.z` is 4.584719 m; with the native 0.25 m padding on each side, a selected AutoParkSpot must accept at least 5.084719 m. There is no static evidence that justifies moving the wheel or body colliders.

## Changes in this revision

- Removed the custom fade/teleport fallback, including its premature `Legal` assignment. The native game coroutine is the only parking operation; a per-vehicle in-flight guard rejects duplicates.
- Reduced reconciliation to the native completion and the following stopped-vehicle frame instead of repeatedly calling `UpdateParkingZone()` for three seconds.
- Made the validation query match the native wheel transform and Unity's global trigger-query setting. The guard resolves a lane on the hit or its parent, rejects missing lanes and handicap lanes, and no longer adds a cross-wheel neighborhood equality requirement that vanilla does not enforce.
- Reconcile once after entering a vehicle as well, because vanilla recalculates the HUD state there. `Legal` is written only after all four wheel probes resolve to non-handicap parking lanes.
- Added opt-in diagnostics at `ModsLocal/Bentley_Continental_GT_Speed/Config/auto-parking-diagnostics.enabled`. They record the captured spot, its bounds and lane colliders, then each wheel's world center, radius, every overlap collider and its direct/parent lane, the HUD state, and the saved state.

## Verification boundary

The source and DLL audit establish the state-overwrite path and the Bentley/vanilla query differences. The previous log is from an older installed build; the current lane collider bounds and current wheel hits must be read from the new diagnostic run before changing vehicle geometry. Build/install verification is separate from an in-game auto-park, exit, save, and reload test.
