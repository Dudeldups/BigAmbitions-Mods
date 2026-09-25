# Ferrari SF90 Spider vehicle calibration

The Ferrari SF90 Spider uses a runtime-created `VehicleType` and a deterministic
Ferrari prefab setup. Public vehicle metadata keeps the real combined system
rating, while the NWH drivetrain values are road-test calibrations chosen to
match the target acceleration and braking envelope in Big Ambitions.

| Property | Calibration |
| --- | --- |
| Target length / mirror span / height | 4.704 m / 2.220 m / 1.191 m |
| Published wheelbase reference | 2.649 m |
| Front / rear tire | 255/35 ZR20 / 315/30 ZR20 |
| Front / rear simulated tire radius | 0.34325 m / 0.34850 m |
| Vehicle mass calibration | 1,670 kg |
| Public combined system power | 735 kW |
| NWH road calibration | 510 kW effective max power |
| Fuel capacity | 68 L |
| Cargo capacity | 2 |
| Maximum speed | 340 km/h |
| Transmission | 8-speed dual-clutch |
| Gear ratios | R 3.45; 1 3.45; 2 2.26; 3 1.65; 4 1.29; 5 1.03; 6 0.84; 7 0.67; 8 0.48 |
| Final drive | 4.51 |
| Upshift / downshift calibration | 7,500 / 4,300 rpm |
| Shift duration | 0.065 s |
| Front / rear longitudinal grip | 0.49 / 0.65 |
| NWH brake torque | 2,450 |
| Period price used in-game | USD 558,000 |
| Acceleration calibration target | approximately 2.5 s 0-100 km/h; 7.0 s 0-200 km/h |
| Braking calibration target | below 29.5 m from 100-0 km/h |
| Final road-test result | approximately 2.5 s 0-100; approximately 7.0 s 0-200; approximately 29.0 m 100-0 |

The published 2.649 m wheelbase remains the documentation/physics reference.
The imported model's authored origin and wheel-arch placement require visually
calibrated wheel-controller Z positions, so diagnostics can report a slightly
different controller-to-controller wheelbase. This is intentional and keeps the
visible wheels centered in the supplied model.

The public 735 kW value and the internal 510 kW NWH value serve different
purposes. NWH applies its `maxPower` setting more directly than a real combined
hybrid system rating, so the lower solver value is retained to reproduce the
measured road-performance targets rather than to redefine the vehicle's rated
output.
