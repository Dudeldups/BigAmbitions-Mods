# 2018 Dodge Challenger SRT Demon HPE1200

The supplied model identifies itself as a **2018 Dodge Challenger SRT Demon HPE1200**. The mod display name remains **2018 Dodge Challenger**, as requested.

## Vehicle baseline — 2018 SRT Demon

- Engine: supercharged 6.2 L HEMI V8
- Drivetrain: rear-wheel drive, limited-slip rear differential
- Transmission: TorqueFlite 8HP90 8-speed automatic
- Gear ratios: reverse 3.32; 1st 4.71; 2nd 3.14; 3rd 2.10; 4th 1.67; 5th 1.29; 6th 1.00; 7th 0.84; 8th 0.67
- Final drive: 3.09
- Wheelbase: 2.9504 m
- Track: 1.6714 m front / 1.6678 m rear
- Length: 5.0156 m
- Width: 2.0015 m body / 2.1787 m including mirrors
- Height: 1.459 m
- Ground clearance: 0.1135 m
- Drag coefficient: 0.376
- Curb mass: 4,280 lb / approximately 1,941 kg
- Weight distribution: 58% front / 42% rear
- Fuel: 18.5 US gal / 70.0 L
- Wheels: 18 x 11 in
- Tires: P315/40R18 on all four corners
- Tire radius used by the setup: 0.3546 m
- Factory drag-radial speed rating: 168 mph / approximately 270 km/h
- Mod top-speed target / limiter: **350 km/h**. This intentionally represents an HPE1200 on suitable high-speed tires rather than preserving the factory drag-radial restriction. A largely stock 840 hp Demon has independently exceeded 200 mph when the tire/limiter constraint was removed, including documented runs above 211 mph (~340 km/h).
- Turning circle: approximately 38.0 ft / 11.59 m

## Hennessey HPE1200 configuration represented by the supplied model

- HPE1200 rating: approximately 1,200 hp at the crank
- Engine-power value used by Big Ambitions: 895 kW
- Verified chassis dyno: 1,013 rear-wheel hp and 954 rear-wheel lb-ft
- 4.5 L Whipple supercharger, replacing the stock 2.7 L unit
- Forged engine internals
- Long-tube headers
- High-flow injectors and upgraded fuel system
- HPE engine management / dyno calibration
- HPE1200 package documented at $84,950 on a real example
- 2018 Demon MSRP: $84,995 including gas-guzzler tax, before destination
- Mod vehicle price: **$169,945** (MSRP + documented HPE1200 package)

No HPE1200-specific verified maximum-speed result was found. The 350 km/h cap is therefore a conservative game calibration rather than a claimed Hennessey test result. It is deliberately only slightly above documented 840 hp Demon top-speed runs once the factory tire/limiter constraint is removed. Acceleration should still be validated in-game against the documented power, gearing, mass, tires, dimensions and RWD layout.

## References

- FCA / Dodge: 2018 Dodge Challenger SRT Demon Specifications.
- FCA / Dodge: 2018 Challenger / Challenger SRT specifications and overview.
- Hennessey Performance: HPE1200 Dodge Demon Dyno Testing / Validation Testing.
- Hennessey HPE1200 build receipt/specification reproduced with a documented 2018 HPE1200 example.


## Physics calibration

The VehicleType continues to report the real HPE1200 crank rating of approximately
1,200 hp / 895 kW. NWH vehicle physics is calibrated with **755 kW effective power**
because the package was independently chassis-dyno tested at **1,013 rear-wheel hp**
and **954 rear-wheel lb-ft (approximately 1,293 Nm)**. NWH does not model a separate
drivetrain-loss stage for this setup, so feeding the measured wheel output avoids
putting the full crankshaft rating directly at the tires.

The power curve is shaped to keep peak wheel torque near the documented dyno result
instead of the previous ~1,700 Nm simulation peak.

Braking is calibrated around the stock Demon brake/tire package. The previous
2,550 torque setting produced roughly 1.55-1.62 g sustained deceleration in the
DeveloperTools test. The revised value is 2,000, targeting approximately 1.2-1.3 g,
consistent with reported ~97 ft 60-0 mph stopping performance.

Full-throttle automatic upshift request: 5,350 rpm with 0.055 s shift duration.
The lower request compensates for NWH shift-actuation delay so the engine should
complete shifts around the useful upper power band instead of repeatedly hitting
the 6,500 rpm limiter.

### Validation references

- Hennessey HPE1200 chassis dyno: 1,013 rwhp / 954 rwtq.
- 2018 Demon factory: 8HP90, 3.09 final drive, 315/40R18 drag radials.
- Dodge factory / period testing: 2.3-2.6 s 0-60 mph under drag-strip conditions;
  peak claimed launch acceleration about 1.8 g.
