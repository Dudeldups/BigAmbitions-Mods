# 1967 Chevrolet Camaro RS/SS "Hidden Jewel" implementation targets

This mod uses the supplied `1967 Chevy Camaro SS Hidden Jewel` model as the visual
reference. Vehicle physics are based on documented 1967 Camaro SS 350 data plus the
specific Hidden Jewel drivetrain where that car is documented to differ from stock.

## Supplied car / Hidden Jewel

Primary feature:
https://www.motortrend.com/features/1004clt-1967-chevy-camaro-rs-ss/

- Original Chevrolet 350 small-block retained and rebuilt 0.030-inch over.
- Trick Flow aluminum heads, Weiand intake, Holley 750 cfm carburetor, Comp cam and
  Hooker headers are documented, but the article publishes no dyno horsepower.
- Muncie M22 four-speed manual.
- 12-bolt rear axle, 4.11 gears and Positraction.
- Original Rally wheels retained.
- Original interior retained.

Because no measured output is published for the modified Hidden Jewel engine, the
initial game tune deliberately uses the documented stock L48 SS 350 rating rather
than inventing a tuned horsepower figure.

## 1967 Camaro SS 350 baseline

Period road test (Car and Driver archive):
https://www.caranddriver.com/reviews/a15141314/1967-chevrolet-camaro-ss-archived-instrumented-test/

- 350 cu in / 5.7 L naturally aspirated V8.
- 295 hp SAE gross at 4,800 rpm; 380 lb-ft at 3,200 rpm.
- 4-speed manual, rear-wheel drive.
- Tested curb weight: 3,269 lb / about 1,483 kg.
- 0-60 mph: 7.8 s; quarter mile: 16.1 s at 86.5 mph.

1967 Chevrolet Camaro brochure / AMA data:
https://www.motorologist.com/wp-content/uploads/1967-Chevrolet-Camaro-brochure1.pdf
https://over-drive-magazine.com/wp-content/uploads/2024/03/1967-CHEVROLET_Camaro-I-6_230-CID-V-8-327-CID-210-HP-1-26.pdf

- Length 184.7 in / 4.691 m.
- Width 72.5 in / 1.842 m.
- Height 51.0 in / 1.295 m.
- Wheelbase 108.0 in / 2.743 m.
- Track 59.0 in front / 58.9 in rear (1.499 / 1.496 m).
- Usable luggage capacity 8.3 cu ft.
- Curb-to-curb turning diameter approximately 37.0-37.4 ft depending on specification sheet.

1967 Chevrolet chassis service manual:
https://www.carmanualsonline.info/chevrolet-camaro-1967-1-g-chassis-workshop-manual/?srch=fuel+tank+capacity

- Camaro fuel tank capacity: approximately 18.5 US gal (about 70 L).

M22 ratios (Camaro Research Group):
https://www.camaros.org/trans.shtml

- 1st 2.20, 2nd 1.64, 3rd 1.28, 4th 1.00.
- The runtime uses a 2.26 reverse ratio and the documented Hidden Jewel 4.11 final drive.
- Big Ambitions has no player-facing clutch/manual-shift workflow, so the game performs
  the shifts automatically while retaining the M22 ratios and four forward gears.

## Supplied-model-derived wheel target

The supplied GLB contains four independent tire/rim/rotor assemblies. After normalizing
the GLB to the documented body dimensions, the tire geometry measures approximately
0.64 m outside diameter and 0.236 m width. The initial wheel-controller target therefore
uses a 0.320 m radius and 0.236 m width instead of copying a modern donor vehicle's tires.

## Game-facing targets

- Engine power: 220 kW (295 hp baseline).
- Mass: 1,483 kg.
- Fuel: 70 L.
- Cargo capacity: 5 units. The previous 8-slot gameplay capacity was too generous for the Camaro trunk.
- Speed limiter: 190 km/h safety ceiling; gearing remains the primary top-speed constraint.
- Price: $54,900 City Cars retail price; chosen as a plausible non-round collector-market value
  for a good 1967 RS/SS while keeping the car below the modern luxury-dealer tier.
- Dealer: City Cars provisionally, pending owner confirmation if the restored show car
  should instead be sold through the luxury dealers.

## Physics calibration note

- The VehicleType keeps the documented 1967 L48 rating of **295 hp SAE gross (~220 kW)**.
- NWH runtime `maxPower` is calibrated to **140 kW** with
  `engineLossPercent = 0.32`. The previous 155 kW tune produced quicker-than-period
  acceleration in repeated diagnostics; 140 kW is the next measured calibration step
  toward the 7.8 s 0-60 mph period target while keeping the documented 220 kW
  VehicleType catalogue rating unchanged.
- Rear longitudinal grip is set to **0.30** to reproduce the documented launch
  wheelspin of the period SS-350 rather than modern-tire traction.
- Brake torque is calibrated to **1060**. The later diagnostic set measured roughly
  **52–54 m from 60 mph to near-zero** with the 950 setting, so 1060 is the next
  calibration step toward the period **156 ft / 47.55 m** result.

## Shift calibration

- Normal upshift target after first gear: **4750 rpm**
- Downshift target: **2800 rpm**
- Shift duration: **0.30 s**
- First-to-second is handled separately to prevent launch wheelspin from triggering
  an implausibly early automatic shift. While in first, NWH's native upshift threshold
  is temporarily raised; the controlled 1->2 shift requires **>=4700 rpm** and
  **>=55 km/h real vehicle speed**. Afterward the 2->1 threshold is temporarily lowered
  for 0.90 s so the transmission cannot immediately bounce back into first.
