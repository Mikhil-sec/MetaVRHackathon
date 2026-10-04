"""In-game music bed for RICOCHET, synthesized (original, no licensed audio).

Two stems of identical length that loop seamlessly and stay beat-locked (MusicBed starts both on the same DSP time):
  bed.ogg   - always on, quiet: a dark pad cycling pentatonic voicings plus sparse rising plucks (the combo voice).
  pulse.ogg - faded in for the boss and Fever: sub pulse, soft kit, a low pluck arpeggio.
A minor pentatonic (A C D E G) is the same pitch set as the game's C major pentatonic combo notes, so every hit
lands in key. 80 BPM, 16 bars (48 s). Loop seam: render with a tail, fold the tail back onto the start.

Run: python music_bed.py   (needs numpy, scipy, ffmpeg on PATH). Writes ../bed.ogg and ../pulse.ogg.
"""
import os, subprocess, tempfile, wave
import numpy as np
from scipy.signal import fftconvolve, lfilter

SR = 48000
BPM = 80.0
BEAT = 60.0 / BPM
BARS = 16
LOOP = BARS * 4 * BEAT          # 48 s
TAIL = 6.0
rng = np.random.default_rng(11)
A2 = 110.0


def hz(semi):
    return A2 * 2 ** (semi / 12)


PENTA = [0, 3, 5, 7, 10]        # A C D E G


def penta(i, octave=0):
    return hz(PENTA[i % 5] + 12 * (i // 5 + octave))


def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR)
    return lfilter([1 - a], [1, -a], x).astype(np.float32)


def highpass(x, cutoff):
    return (x - lowpass(x, cutoff)).astype(np.float32)


_PLUCKS = {}


def pluck(f, dur=2.4, bright=0.45, decay=0.997):
    """Karplus-Strong, like the game's ChimeSynth plucks (memoized: phrases repeat)."""
    key = (round(f, 3), dur, bright, decay)
    if key not in _PLUCKS:
        _PLUCKS[key] = _pluck(f, dur, bright, decay)
    return _PLUCKS[key]


def _pluck(f, dur, bright, decay):
    n = int(dur * SR)
    p = max(2, int(SR / f))
    buf = rng.uniform(-1, 1, p).astype(np.float32)
    for _ in range(int((1 - bright) * 3)):
        buf = 0.5 * (buf + np.roll(buf, 1))
    out = np.empty(n, np.float32)
    idx = 0
    for i in range(n):
        out[i] = buf[idx]
        nxt = (idx + 1) % p
        buf[idx] = decay * 0.5 * (buf[idx] + buf[nxt])
        idx = nxt
    t = np.arange(n) / SR
    out += 0.4 * np.sin(2 * np.pi * f * t) * np.exp(-t * 2.0)
    return out * np.exp(-t * 0.9)


def pad(freqs, dur, attack=2.0, release=2.5, detune=0.005):
    n = int(dur * SR)
    t = np.arange(n) / SR
    out = np.zeros(n, np.float32)
    for f in freqs:
        for d in (-detune, 0, detune):
            ph = rng.uniform(0, 2 * np.pi)
            for h in range(1, 6):
                out += (np.sin(2 * np.pi * f * (1 + d) * h * t + ph * h) / h ** 1.8).astype(np.float32)
    out /= max(1e-6, np.abs(out).max())
    # A slow filter-like swell: brighter in the middle of the chord.
    e = np.minimum(1, np.minimum(t / attack, (dur - t) / release)).clip(0, 1)
    return lowpass(out, 1400) * e


def kick(dur=0.45, f0=110, f1=40):
    n = int(dur * SR)
    t = np.arange(n) / SR
    f = f1 + (f0 - f1) * np.exp(-t * 30)
    return (np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 9)).astype(np.float32)


def hat(dur=0.06):
    n = int(dur * SR)
    t = np.arange(n) / SR
    return highpass(rng.uniform(-1, 1, n).astype(np.float32), 7000) * np.exp(-t * 70)


def sub(f, dur):
    n = int(dur * SR)
    t = np.arange(n) / SR
    e = np.minimum(1, np.minimum(t / 0.02, (dur - t) / 0.25)).clip(0, 1)
    return (np.sin(2 * np.pi * f * t) * e).astype(np.float32)


class Mix:
    def __init__(self):
        self.L = np.zeros((int((LOOP + TAIL) * SR), 2), np.float32)
        self.send = np.zeros(len(self.L), np.float32)

    def add(self, sig, at, gain=1.0, pan=0.0, rev=0.0):
        i = int(at * SR)
        sig = sig[: len(self.L) - i]
        l, r = np.sqrt(0.5 * (1 - pan)), np.sqrt(0.5 * (1 + pan))
        self.L[i:i + len(sig), 0] += sig * gain * l
        self.L[i:i + len(sig), 1] += sig * gain * r
        if rev:
            self.send[i:i + len(sig)] += sig * gain * rev

    def finish(self, wet=1.0, decay=3.2, peak=0.5):
        n = int(decay * SR)
        t = np.arange(n) / SR
        for ch in range(2):
            ir = rng.uniform(-1, 1, n) * np.exp(-t * 6.9 / decay)
            ir = lowpass(ir.astype(np.float32), 6000)
            ir /= np.sqrt((ir ** 2).sum())
            self.L[:, ch] += fftconvolve(self.send, ir)[: len(self.L)] * wet
        # Fold the tail past the loop end back onto the start: a seamless loop.
        loop = self.L[: int(LOOP * SR)].copy()
        tail = self.L[int(LOOP * SR):]
        loop[: len(tail)] += tail
        return loop * (peak / max(1e-6, np.abs(loop).max()))


# Two-bar voicings from the pentatonic set (semitones above A2), 8 chords = 16 bars.
CHORDS = [
    [0, 12, 15, 19, 22],      # Am7       A C E G
    [3, 15, 22, 26, 31],      # Cadd9     C G D E
    [5, 17, 24, 27, 31],      # Dm9(no 3) D A C E
    [7, 19, 22, 24, 29],      # Em7sus4   E G A D
    [0, 12, 19, 22, 27],      # Am(add11)
    [10, 22, 26, 29, 33],     # G6sus     G D E A
    [3, 15, 19, 22, 26],      # C6/9
    [7, 17, 19, 24, 31],      # E7sus4    E D A
]


def bed():
    m = Mix()
    bar = 4 * BEAT
    for k, ch in enumerate(CHORDS):
        at = k * 2 * bar
        freqs = [hz(s) for s in ch]
        m.add(pad(freqs, 2 * bar + 2.5), at, 0.30, 0.0, rev=0.35)
        m.add(sub(hz(ch[0] - 12), 2 * bar), at, 0.10)
    # Sparse plucks: a short rising pentatonic phrase every two bars, placed on off-beats, alternating sides.
    for k in range(BARS // 2):
        start = k * 2 * bar + BEAT * (1.5 if k % 2 == 0 else 2.5)
        root = [0, 2, 3, 4, 0, 4, 2, 3][k]
        for j in range(3 if k % 4 != 3 else 4):
            f = penta(root + j * (1 if k % 2 == 0 else 2), 2)
            m.add(pluck(f), start + j * BEAT * 0.5, 0.20 * (0.85 ** j), pan=-0.4 if k % 2 == 0 else 0.4, rev=0.6)
    return m.finish(peak=0.42)


def pulse():
    m = Mix()
    bar = 4 * BEAT
    for b in range(BARS):
        root = CHORDS[b // 2][0]
        for beat in range(4):
            at = b * bar + beat * BEAT
            if beat in (0, 2):
                m.add(kick(), at, 0.55)
            m.add(hat(), at + BEAT * 0.5, 0.10, pan=0.3)
            m.add(sub(hz(root - 12), BEAT * 0.9), at, 0.22)
        # Low pluck arpeggio in eighths: root, fifth, octave, ... from the chord.
        ch = CHORDS[b // 2]
        for e in range(8):
            f = hz(ch[[0, 2, 1, 3, 4, 3, 1, 2][e]] - 12 + 12)
            m.add(pluck(f, 0.9, 0.35, 0.995), b * bar + e * BEAT * 0.5, 0.11, pan=0.25 * np.sin(e), rev=0.25)
    return m.finish(wet=0.6, peak=0.45)


def write_ogg(path, x):
    with tempfile.TemporaryDirectory() as d:
        wav = os.path.join(d, "t.wav")
        pcm = (np.clip(x, -1, 1) * 32767).astype(np.int16)
        with wave.open(wav, "wb") as w:
            w.setnchannels(2)
            w.setsampwidth(2)
            w.setframerate(SR)
            w.writeframes(pcm.tobytes())
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-c:a", "libvorbis", "-q:a", "5", path], check=True)


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.normpath(os.path.join(here, ".."))
    for name, fn in (("bed", bed), ("pulse", pulse)):
        x = fn()
        write_ogg(os.path.join(out, name + ".ogg"), x)
        rms = float(np.sqrt((x ** 2).mean()))
        print(f"{name}.ogg  {len(x) / SR:.1f} s  peak {np.abs(x).max():.2f}  rms {rms:.3f}")
