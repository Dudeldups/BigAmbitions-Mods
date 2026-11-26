# Lamborghini Aventador SVJ Coupe — technical targets

The supplied GLB identifies a 2019 Aventador SVJ Coupe (LP 770-4) in its node and material names. Its vehicle geometry bounds resolve to approximately 4.94 m long, 2.27 m wide including mirrors and 1.12 m tall after correcting its embedded 0.01 model scale. This agrees with the manufacturer dimensions closely enough to establish the intended scale; wheel and anchor offsets still need to be measured in Unity.

## Road-car specifications

| Specification | Target | Source / use |
| --- | --- | --- |
| Model year and variant | 2019 Aventador SVJ Coupe, LP 770-4 | Embedded GLB names; Lamborghini SVJ model page |
| Overall length | 4,943 mm | 2019 manufacturer product information |
| Overall width | 2,098 mm without mirrors; 2,273 mm with mirrors | 2019 manufacturer product information |
| Overall height | 1,136 mm | 2019 manufacturer product information |
| Wheelbase | 2,700 mm | 2019 manufacturer product information |
| Front / rear track | 1,720 / 1,680 mm | 2019 manufacturer product information |
| Front tyre | 255/30 ZR20 92Y | Pirelli P Zero Corsa, 2019 product information |
| Rear tyre | 355/25 ZR21 107Y | Pirelli P Zero Corsa, 2019 product information |
| Dry mass | 1,525 kg | 2019 manufacturer product information |
| In-game physics mass target | 1,770 kg | Approximate operating mass, cross-checked against 3,902 lb published curb weight; use as initial NWH mass |
| Weight distribution | 43% front / 57% rear | 2019 manufacturer product information; initial center-of-mass target only |
| Engine | Naturally aspirated 6,498 cm³ V12 | Lamborghini SVJ specifications |
| Maximum power | 566 kW / 770 CV at 8,500 rpm (759 SAE hp) | Lamborghini SVJ specifications |
| Maximum torque | 720 Nm at 6,750 rpm | 2019 manufacturer product information |
| Redline target | 8,700 rpm | SVJ engine technical data; keep the NWH limiter at or just above the 8,500 rpm power peak |
| Transmission | 7-speed ISR automated manual; AWD, Haldex generation IV | 2019 manufacturer product information |
| Gear ratios (1st–7th) | 3.909, 2.438, 1.810, 1.458, 1.185, 0.967, 0.844 | 2019 manufacturer product information |
| Reverse ratio | 2.929 | 2019 manufacturer product information |
| Final drive (rear / front) | 2.867 / 3.273 | 2019 manufacturer product information |
| Top speed | Greater than 350 km/h | Lamborghini SVJ specifications |
| 0–100 km/h | 2.8 s | Lamborghini SVJ specifications |
| 0–200 km/h | 8.6 s | 2019 manufacturer product information |
| 100–0 km/h braking | 30 m | 2019 manufacturer product information |
| Seats / front luggage / fuel capacity | 2 / 140 L / 85 L | 2019 manufacturer product information |
| Game purchase price | $608,358 | Mod economy choice aligned with the Lamborghini Revuelto catalog reference; it is not a real-world MSRP conversion |

Tyre free radii implied by nominal sizing are approximately 0.331 m front and 0.355 m rear. Measure the imported GLB wheel centers and loaded ride height before finalizing controller positions. The operating-mass target is a physics starting point; actual in-game acceleration, braking and suspension still require telemetry and tuning.

## Sources

- [Automobili Lamborghini — Aventador SVJ specifications and systems](https://www.lamborghini.com/en-en/history/aventador-svj): displacement, maximum power and rpm, top speed, acceleration, AWD, four-wheel steering and active suspension.
- [Lamborghini Aventador SVJ 2019 product information PDF](https://www.auto-brochures.com/makes/Lamborghini/aventador/Lamborghini_int%20Aventador_2019-svj.pdf): factory ratios, dimensions, tracks, mass, weight distribution, tyres, capacity, 0–200 km/h and braking table.
- [Kelley Blue Book — 2019 Aventador prices and specs](https://www.kbb.com/lamborghini/aventador/2019/?bodystyle=coupe): North American SVJ Coupe original MSRP of $528,460 and 759 hp / 531 lb-ft context.
- [Lamborghini Media Information — Aventador SVJ Roadster](https://downloads.regulations.gov/NHTSA-2023-0022-0065/attachment_7.pdf): drivetrain, gears and torque cross-check. Roadster mass and performance values are not used for this Coupe.

Reuse these model-specific values for prefab calibration; do not copy another vehicle's wheel positions, physics, price, or final ratios.
