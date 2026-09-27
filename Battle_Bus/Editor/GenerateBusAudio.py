"""Original procedural Battle Bus sound design; no sampled game or third-party recordings.
Run with Python + NumPy. Writes deterministic mono PCM loops and radio transients.
"""
from pathlib import Path
import wave
import numpy as np
# Historical synthesis reference; never overwrite the supplied engine recording.
OUT=Path(__file__).resolve().parents[1]/'Audio'/'ProceduralReference'
OUT.mkdir(exist_ok=True)
RATE=24000
rng=np.random.default_rng(230927)
def noise(n,low,high):
    frequencies=np.fft.rfftfreq(n,1/RATE)
    spectrum=np.fft.rfft(rng.normal(size=n))
    shape=np.minimum(1,(frequencies/max(low,1))**2)/(1+(frequencies/high)**4)
    shaped=np.fft.irfft(spectrum*shape,n)
    return shaped/max(np.std(shaped),1e-6)
def write(name,signal,peak=.65):
    signal=signal-np.mean(signal)
    signal=signal/max(np.max(np.abs(signal)),1e-6)*peak
    with wave.open(str(OUT/(name+'.wav')),'wb') as w:
        w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE)
        w.writeframes((np.clip(signal,-1,1)*32767).astype('<i2').tobytes())
n=RATE*8;t=np.arange(n)/RATE
# Broadband combustion/exhaust texture, gated by smooth firing envelopes.
# No sustained sine-wave resonators: those sounded like organ notes in game.
for name,firing in [('DieselIdle',34),('DieselLow',58),('DieselHigh',88)]:
    phase=np.mod(t*firing+.018*np.sin(2*np.pi*1.5*t),1)
    envelope=np.sin(np.pi*phase)**2*np.exp(-phase*5)
    envelope/=max(envelope)
    exhaust=noise(n,65,850)
    combustion=noise(n,280,1900)
    signal=(.22+.78*envelope)*(.60*exhaust+.20*combustion)
    # Broad low-frequency body instead of a pitched sub-bass oscillator.
    signal+=.12*noise(n,45,180)
    spectrum=np.fft.rfft(signal);freq=np.fft.rfftfreq(n,1/RATE)
    signal=np.fft.irfft(spectrum/(1+(freq/2200)**6),n)
    write(name,signal,.68)
# Burner.wav is retained unchanged from the accepted mix.
# Horn and HornLong are derived from the user-supplied recording.
# Existing radio clips are retained unchanged.
print('Generated three engine clips in',OUT)
