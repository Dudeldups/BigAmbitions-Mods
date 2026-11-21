# Bentley Continental GT Speed — vehicle specifications

## Identity and sources

- Model: 2025 Bentley Continental GT Speed, fourth-generation coupe (MY25).
- Official sources: [Bentley Continental GT Speed](https://www.bentleymotors.com/us/en/models/continental-gt/continental-gt-speed.html) and Bentley's [2025 Continental GT Customer Book (English, October 2024)](https://cdn.bentleymotors.com/downloads/en/brochure/global/continental/25MY_ContinentalGT_CustomerBook%28English%29%28Oct24%29.pdf).
- Source model: `Models/2025_bentley_continental_gt_speed.glb`; SHA-256 `8BFBD933CABA2B4A1559C50C09F8FB54ABE1D8A4B7539EA0589283B8E3D1CBE5`.

## Published vehicle specifications

| Specification | Published value | Evidence |
| --- | --- | --- |
| Powertrain | 4.0 L twin-turbocharged V8 plug-in hybrid | Bentley MY25 model page and customer book |
| Combined system output | 782 PS (575 kW) | Bentley MY25 model page and customer book |
| Combined system torque | 1,000 Nm | Bentley MY25 model page and customer book |
| V8 output | 600 PS (441 kW) at 6,250 rpm | Bentley MY25 model page and customer book |
| V8 torque | 800 Nm at 2,500–4,500 rpm | Bentley MY25 model page and customer book |
| Electric motor output | 190 PS (140 kW) | Bentley MY25 model page and customer book |
| Transmission / driven wheels | 8-speed dual-clutch transmission / AWD | Bentley MY25 model page and customer book |
| Maximum speed | 335 km/h | Bentley MY25 model page and customer book |
| 0–100 km/h | 3.2 s | Bentley MY25 model page and customer book |
| Tested 70–0 mph braking distance | 167 ft (50.9 m) | [Car and Driver instrumented test/specifications](https://www.caranddriver.com/bentley/continental-gt-2025) |
| EU unladen mass | 2,459 kg | Bentley MY25 customer book; retain its EU unladen definition |
| Overall dimensions (L × W × H) | 4,895 × 2,187 × 1,397 mm; width includes mirrors | Bentley MY25 customer book |
| Wheelbase | 2,851 mm | Bentley MY25 customer book |
| Tyres | Front 275/35 ZR22; rear 315/30 ZR22 | Bentley MY25 customer book |
| Fuel tank | 80 L | Bentley MY25 customer book |
| Luggage volume | 260 L | Bentley MY25 customer book |
| Weight distribution | 49:51 | Bentley MY25 customer book |
| High-voltage battery | 25.9 kWh | Bentley MY25 model page and customer book |

Bentley publishes the V8's peak output and torque but does not provide an engine RPM limiter in the cited material. No gearbox ratios, axle track widths, or vehicle price have been verified from these sources. Car and Driver's 70–0 mph measurement is an independent instrumented test, not a Bentley manufacturer specification. Treat unpublished game values as clearly labeled implementation tuning targets.

## Source-model geometry observations

These values are approximate measurements of the supplied GLB, not Bentley homologation dimensions. Axes in the source file are X left/right, Y up, and Z forward. The model is left-hand drive and has separate front driver and passenger seat geometry.

| Measurement / observation | GLB result | Evidence and follow-up |
| --- | --- | --- |
| Overall length | approximately 4.872 m | Measured from source-model geometry; compare after Unity import |
| Overall width | approximately 2.204 m | Measured from source-model geometry; visually validate mirror extent and model scaling against Bentley's 2.187 m mirrors-inclusive figure |
| Overall height | approximately 1.281 m | Measured from source-model geometry; compare after Unity import |
| Wheelbase | approximately 2.828 m | Measured between model axle centers; compare after Unity import |
| Steering layout | Left-hand drive | Observed in source-model geometry |
| Front seating | Driver and passenger seats present | Observed in source-model geometry |
| Source axes | X left/right, Y up, Z forward | Read from GLB scene orientation |

Model geometry remains an import/setup reference and has not yet been visually validated in Unity or in game. All contract evidence remains `not_run` until the corresponding verification is performed.
