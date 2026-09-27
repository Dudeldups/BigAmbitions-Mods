# Current engine source

DieselIdle.wav: bus engine by brunobegot (Freesound), uploaded via freesound_community.
Source: https://pixabay.com/sound-effects/city-bus-engine-47297/
License: Pixabay Content License, https://pixabay.com/service/license-summary/
Source and license checked 2026-09-27. Adapted as an integrated vehicle sound;
not licensed for standalone redistribution as a stock sound.
User-supplied MP3: freesound_community-bus-engine-47297.mp3.
Processing: mono downmix, source 0.25–9.25s, 300ms raised-cosine wrap crossfade,
DC removal and peak normalization to 0.72. Result: 8.7s, 24kHz PCM16 loop.
One voice for idle/driving with mild RPM pitch and throttle gain.

The burner and legacy unused engine layers are original procedural sound designs.
The legacy engine loops use smoothly pulsed broadband combustion/exhaust noise instead of
sustained tonal resonators. `Editor/GenerateBusAudio.py` regenerates the three
8-second legacy engine clips; do not run it over the current supplied DieselIdle.wav.

RadioSquelch1.wav derives from the 1.933-second user-supplied recording
from https://www.youtube.com/watch?v=xX8MtQDr-ag (2026-09-27), mono
conversion and peak gain only. Redistribution permission is not documented.

## User-supplied horn reference

Horn.wav and HornLong.wav are excerpts of the file supplied by the user on
2026-09-27 from https://www.youtube.com/watch?v=9euN85mTwW8: “All Battle Bus
Horn Sounds (Fortnite) - Sound Effects for editing”.
Source intervals: 0-3.1 seconds and 6.3-16.9 seconds; mono conversion, gain
normalization and 15 ms edge fades only. Fortnite sound attribution: Epic Games.
These are third-party recordings, not the procedural mod sounds. A license or
redistribution permission is not documented; confirm it before Workshop release.
