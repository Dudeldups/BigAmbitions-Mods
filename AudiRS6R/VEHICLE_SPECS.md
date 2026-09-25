# Audi RS6-R vehicle targets

The prefab and runtime tune target the ABT RS6-R based on the C8 Audi RS 6
Avant. Calibration is driven by bounded in-game acceleration and braking runs.

| Item | Target |
| --- | ---: |
| Length / width / height | 4,995 / 1,951 / 1,460 mm |
| Wheelbase | 2,929 mm |
| Fuel tank | 73 L |
| Engine | 4.0 L twin-turbo V8 |
| Power / torque | 544 kW (740 hp) / 920 Nm |
| Transmission | Eight-speed automatic, all-wheel drive |
| Top speed | Up to 320 km/h |
| 0–100 km/h calibration target | 3.3 s; ABT claim 3.2 s |
| 0–200 km/h calibration target | 10.4 s |
| 100–0 km/h calibration target | 37.5 m |

The vehicle catalog retains the published 544 kW output. The NWH physics engine
uses a separate 390 kW simulation value because its engine-loss field did not
change the measured acceleration response. Forced-induction multiplication
remains disabled to avoid counting turbo boost twice. The remaining calibration
uses a 40:60 front/rear torque split, 780 Nm clutch limit, 0.90 engine inertia,
and a bounded 2,050 Nm brake input. The v0.5.8 telemetry produced clean
3.14–3.28-second 0–100 km/h and 10.40–10.50-second 0–200 km/h runs, matching the
instrumented acceleration targets. Its approximately 40.8-metre equivalent
100–0 km/h stops motivated the isolated brake-torque correction in v0.5.9.
That correction measured consistently at approximately 38.0–38.4 metres, so
v0.5.10 adds a final 2.5% increase to favor slightly stronger braking. These
values are calibration controls rather than claims about the real car's
mechanical losses. The temporary rear longitudinal-grip reduction was reverted
after telemetry confirmed that the launch produced no measurable tire slip.

Primary references:

- Audi MediaCenter, RS 6 Avant technical data:
  https://www.audi-mediacenter.com/en/audi-rs-6-avant-12084/technical-data
- ABT Sportsline, RS6-R performance specification:
  https://www.abt-sportsline.com/tuning/products/abt-limited-editions/abt-rs6-r
- AUTO BILD RS6-R test and ABT 0–100 km/h claim:
  https://www.autobild.de/artikel/abt-rs6-r-test-16668155.html
- Auditography instrumented RS6-R acceleration run (3.277 s / 10.44 s):
  https://www.youtube.com/watch?v=FaYPx3yzWlw
- Car and Driver instrumented C8 RS 6 braking baseline:
  https://www.caranddriver.com/reviews/a34631077/2021-audi-rs6-avant-by-the-numbers/
