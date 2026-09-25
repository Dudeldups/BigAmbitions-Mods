# Cadillac Escalade vehicle calibration

The replacement model depicts a standard-wheelbase 2021 Cadillac Escalade
Premium Luxury 4WD. Its body, drivetrain metadata, and physics calibration
follow the fifth-generation 6.2 L 4WD vehicle. NWH applies its `maxPower` value
more directly than crank-rated output, so runtime wheel-power and brake-torque
values are solver calibrations derived from the recorded acceleration and
braking tests.

| Property | Calibration |
| --- | --- |
| Generated length / mirror span / height | 5.382 m / 2.450 m / 1.948 m |
| Generated wheelbase | 3.064 m |
| Generated front / rear track | 1.775 m / 1.775 m |
| Curb mass | 2,738 kg / 6,036 lb tested vehicle |
| Engine | 6.2 L naturally aspirated V8 |
| Rated power | 313 kW / 420 hp at 5,600 rpm |
| NWH road calibration | 232 kW effective power |
| Torque | 623 Nm / 460 lb-ft at 4,100 rpm |
| Idle / limiter calibration | 600 / 6,000 rpm |
| Drivetrain | 4WD, represented in-game as 40:60 front/rear AWD |
| Transmission | Hydra-Matic 10L80 ten-speed automatic |
| Gear ratios | R 4.87; 1 4.70; 2 2.99; 3 2.15; 4 1.80; 5 1.52; 6 1.28; 7 1.00; 8 0.85; 9 0.69; 10 0.64 |
| Final drive | 3.23 |
| Tires | P275/50R22; 0.408 m loaded simulation radius |
| Fuel tank | 91 L / 24 US gal |
| Governed top speed | 193 km/h / 120 mph |
| Acceleration target | approximately 6.1-6.2 seconds 0-60 mph |
| Braking target | approximately 35.7-37.2 m / 117-122 ft from 60 mph |
| Roadholding target | approximately 0.71 g |
| Period MSRP target | USD 86,195 for Premium Luxury 4WD |

Sources used for calibration:

- Cadillac 2021 Escalade specifications (L87, 10L80, 3.23 axle):
  https://s3.observador.pt/wp-content/uploads/2022/02/09190421/2021-escalade-specifications-chart-update-060321.pdf
- Official Cadillac 2021 Escalade brochure:
  https://www.cadillac.com/content/dam/cadillac/na/us/english/index/downloads/vehiclebrochures/brochures/2021/my2021-escalade-pdf-brochure-v2.pdf
- Official Cadillac 2021 Escalade trim walk:
  https://media.cadillac.com/content/dam/Media/design/brands/Cadillac/escalade/Escalade-Trim-Walk.pdf
- 2021 Premium Luxury 4WD Monroney label (USD 86,195 base MSRP):
  https://assets.ctfassets.net/x8mrk904j4k6/4Jc9UDy8qtllhGQWfxJxuP/a45bdc59b35dff03c7842d084528879d/window-sticker.pdf
- MotorTrend standard-wheelbase acceleration and mass comparison:
  https://www.motortrend.com/features/2021-cadillac-escalade-esv-pros-cons-vs-bmw-mercedes
- MotorTrend Escalade/Navigator instrumented comparison:
  https://www.motortrend.com/reviews/2021-cadillac-escalade-vs-lincoln-navigator-comparison-test-review
- Car and Driver 2021 Escalade ESV instrumented test (top-speed and 70-0 cross-check):
  https://www.caranddriver.com/reviews/a34396205/2021-cadillac-escalade-esv-by-the-numbers/
