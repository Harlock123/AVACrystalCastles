#!/usr/bin/env python3
"""Synthesises AVACrystalCastles's sound effects and tunes into ../src/AVACrystalCastles/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVACrystalCastles", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Gems, jumps, squashes.
    write("Gem.wav", tone(0.05, 2600 + 0, 3400, "tri", release=1.0), 0.22)
    write("Jump.wav", tone(0.2, 300, 900, "pulse", release=0.6), 0.3)
    write("Land.wav", tone(0.06, 220, 140, "square", release=1.0), 0.25)
    write("Squash.wav", mix(tone(0.25, 600, 120, "square", release=0.8), tone(0.2, 1500, 300, "noise", release=0.9, gain=0.5)), 0.45)
    write("Munch.wav", cat(*[tone(0.05, f, f * 0.7, "square", release=0.8) for f in [500, 420, 500, 420]]), 0.3)
    write("Hat.wav", cat(*[tone(0.07, hz(n), kind="sine", release=0.3) for n in ["C6", "E6", "G6", "B6", "D7", "G7"]]), 0.4)
    write("HatOff.wav", cat(*[tone(0.08, hz(n), kind="sine", release=0.3) for n in ["G6", "D6", "B5", "G5"]]), 0.35)
    write("Honey.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["E6", "G6", "C7", "E7"]]), 0.38)
    write("Bees.wav", tone(1.2, 240, 260, "saw", release=0.3, vibrato=0.06, vib_rate=40), 0.3)
    write("Witch.wav", cat(tone(0.25, 900, 1400, "square", release=0.3, vibrato=0.05, vib_rate=12), tone(0.4, 1400, 600, "square", release=0.5, vibrato=0.06, vib_rate=14)), 0.35)
    write("LastGem.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6", "C7", "E7", "G7"]], tone(0.5, hz("C8"), kind="pulse", release=0.6)), 0.42)
    write("Death.wav", cat(*[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["E5", "D5", "C5", "B4", "A4"]], tone(0.5, hz("E4"), kind="tri", release=0.5)), 0.45)
    write("LevelStart.wav", cat(*[tone(0.1, hz(n), kind="pulse", release=0.4) for n in ["E5", "G5", "B5"]], tone(0.4, hz("E6"), kind="pulse", release=0.7)), 0.38)
    write("LevelClear.wav", tune(170, [("E5", 0.5), ("G#5", 0.5), ("B5", 0.5), ("E6", 0.5), ("B5", 0.5), ("E6", 0.5), ("G#6", 2)], [("E3", 1), ("B2", 1), ("E3", 2)], kind="pulse"), 0.45)
    write("ExtraLife.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["E6", "G#6", "B6", "E7"]]), 0.4)

    # The castles: a twinkling waltz in E minor over a slow bass (new). Loops.
    arps = []
    for chord in [["E3", "B3", "G4"], ["E3", "B3", "G4"], ["C3", "G3", "E4"], ["D3", "A3", "F#4"], ["E3", "B3", "G4"], ["A2", "E3", "C4"], ["B2", "F#3", "D#4"], ["B2", "F#3", "D#4"]] * 2:
        arps += [(chord[0], 1), (chord[1], 1), (chord[2], 1)]
    lead = [("B4", 2), ("E5", 1), ("G5", 2), ("F#5", 1), ("E5", 2), ("D5", 1), ("E5", 3),
            ("G5", 2), ("A5", 1), ("B5", 2), ("G5", 1), ("E5", 2), ("F#5", 1), ("D#5", 3),
            ("B4", 2), ("E5", 1), ("G5", 2), ("B5", 1), ("C6", 2), ("A5", 1), ("B5", 3),
            ("A5", 2), ("G5", 1), ("F#5", 2), ("D#5", 1), ("E5", 3), ("-", 3)]
    bass = seq(168, arps, kind="tri", gain=0.42, release=0.5)
    top = seq(168, lead, kind="sine", gain=0.32, release=0.4)
    write("Music.wav", mix(bass, top), 0.42)

    write("TitleTune.wav", tune(112,
                                [("E5", 1), ("G5", 0.5), ("B5", 1.5), ("A5", 0.5), ("G5", 0.5), ("F#5", 1), ("D#5", 1), ("E5", 2),
                                 ("B4", 1), ("E5", 0.5), ("G5", 1.5), ("F#5", 0.5), ("E5", 0.5), ("D#5", 1), ("B4", 1), ("E5", 2)],
                                [("E3", 2), ("C3", 2), ("A2", 2), ("B2", 2), ("E3", 2), ("A2", 2), ("B2", 2), ("E3", 2)], kind="tri"), 0.45)
    write("StartTune.wav", tune(150, [("B4", 0.5), ("E5", 0.5), ("G5", 0.5), ("B5", 1.5), ("G5", 0.5), ("B5", 2)], [("E3", 2), ("B2", 1), ("E3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(80, [("B5", 1), ("A5", 1), ("G5", 1), ("F#5", 1), ("E5", 3)], [("E4", 2), ("C4", 2), ("B3", 1), ("E3", 3)], kind="tri"), 0.45)

if __name__ == "__main__":
    main()
