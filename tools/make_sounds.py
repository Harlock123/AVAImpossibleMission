#!/usr/bin/env python3
"""Synthesises AVAImpossibleMission's sound effects and tunes into ../src/AVAImpossibleMission/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions. The spoken welcome comes from the formant voice in speech.py, the way 8-bit machines
talked, so no recording or speech engine is needed. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAImpossibleMission", "Sounds")
RNG = random.Random(1984)


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




def drums(bpm, bars):
    """A marching snare: a roll on the first beat of each bar, single taps on the rest, and a bass drum under it."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars):
        for b in range(4):
            if b == 0:
                hit = cat(*[tone(beat / 4, 1800, 900, "noise", attack=0.001, release=1.5, gain=0.5) for _ in range(4)])
            else:
                hit = cat(tone(beat / 2, 2000, 700, "noise", attack=0.001, release=2.5, gain=0.6), silence(beat / 2))
            kick = cat(tone(beat / 2, 110, 45, "sine", attack=0.002, release=1.5, gain=0.9), silence(beat / 2)) if b % 2 == 0 else silence(beat)
            out.extend(mix(hit, kick)[: int(RATE * beat)])
    return out



def echoed(dry, taps=((0.07, 0.35), (0.16, 0.2), (0.3, 0.1))):
    """A metal room's short echo."""
    out = dry + [0.0] * int(RATE * 0.5)
    for delay, gain in taps:
        k = int(RATE * delay)
        for i, v in enumerate(dry):
            out[i + k] += v * gain
    return out


# The professor's lines, written out as phonemes for the formant voice in speech.py.
WELCOME = "G R IY1 T IH NG Z , EY1 JH AH N T ... M EY1 K Y ER S EH1 L F AE T HH OW1 M ... P ER1 M AH N AH N T L IY !"
DEFEAT = "N OW1 ! ... M AA1 IY P L AE1 N Z !"


def professor(words, pitch=96):
    """The professor: a cold, measured voice, with a ring of the machine about it."""
    voice = say(words, rate=RATE, pitch=pitch, speed=0.85, size=1.04)
    ring = say(words, rate=RATE, pitch=pitch * 2, speed=0.85, size=1.04, seed=9)
    return echoed([a + 0.12 * b for a, b in zip(voice, ring)])


def scream():
    """The long falling cry of an agent going down a shaft."""
    return echoed(say("AA1 AA AA AA AO AO", rate=RATE, pitch=300, speed=0.45, size=0.95, bend=lambda f: 1.05 - 0.6 * f ** 1.4))


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The agent: a somersault's swish and the landing; searching, and what's found.
    write("Flip.wav", tone(0.32, 900, 2600, "noise", attack=0.05, release=0.8, gain=0.5), 0.2)
    write("Land.wav", mix(tone(0.09, 140, 60, "sine", attack=0.002, release=1.2), tone(0.05, 900, 300, "noise", release=1.5, gain=0.3)), 0.26)
    write("Search.wav", cat(tone(0.03, 2400, 1600, "noise", attack=0.002, release=1.5, gain=0.4), silence(0.02), tone(0.03, 1800, 1200, "noise", attack=0.002, release=1.5, gain=0.3)), 0.16)
    write("Nothing.wav", tone(0.22, 160, 120, "square", release=0.7, gain=0.6), 0.2)
    write("Piece.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.4) for n in ["E5", "G5", "B5", "E6"]], tone(0.25, hz("B6"), kind="sine", vibrato=0.01, release=0.6)), 0.3)
    write("Code.wav", cat(tone(0.09, hz("A6"), kind="square", release=0.3), silence(0.03), tone(0.09, hz("E6"), kind="square", release=0.3), silence(0.03), tone(0.12, hz("A6"), kind="square", release=0.4)), 0.2)
    write("Snooze.wav", cat(tone(0.5, 1400, 200, "sine", vibrato=0.03, vib_rate=12, release=0.4), tone(0.4, 160, 140, "tri", vibrato=0.08, vib_rate=3, release=0.6, gain=0.6)), 0.3)
    # Lifts, the elevators, doors.
    write("LiftMove.wav", tone(0.8, 70, 90, "saw", attack=0.05, vibrato=0.04, vib_rate=30, release=0.3, gain=0.5), 0.22)
    write("Elevator.wav", mix(tone(0.6, 90, 130, "saw", attack=0.08, vibrato=0.03, vib_rate=25, release=0.3, gain=0.4), tone(0.6, 600, 900, "noise", attack=0.1, release=0.5, gain=0.15)), 0.22)
    write("ElevatorStop.wav", mix(tone(0.12, 120, 50, "square", attack=0.002, release=1.2, gain=0.6), tone(0.08, 1500, 400, "noise", release=1.5, gain=0.3)), 0.24)
    write("Door.wav", tone(0.3, 3000, 600, "noise", attack=0.01, release=1.2, gain=0.6), 0.2)
    # Robots: a bolt fired, and what it does.
    write("Zap.wav", mix(tone(0.25, 2400, 300, "square", vibrato=0.1, vib_rate=60, release=0.7, gain=0.5), tone(0.2, 5000, 1500, "noise", release=1.2, gain=0.3)), 0.24)
    write("Electrocute.wav", cat(mix(*[tone(0.9, 60 + 37 * i, 50 + 31 * i, "saw", vibrato=0.25, vib_rate=40 + 11 * i, release=0.5, gain=0.3) for i in range(3)],
                                    tone(0.9, 6000, 2000, "noise", release=0.6, gain=0.5))), 0.4)
    write("Scream.wav", scream(), 0.7)
    # The pocket computer.
    write("Computer.wav", cat(*[tone(0.05, hz(n), kind="square", release=0.2, gain=0.6) for n in ["C6", "G6", "C7", "G6", "E7"]]), 0.18)
    write("Click.wav", tone(0.02, 3000, 2000, "square", attack=0.001, release=2, gain=0.5), 0.14)
    write("Locked.wav", cat(tone(0.15, 220, kind="square", release=0.2, gain=0.6), silence(0.05), tone(0.25, 165, kind="square", release=0.4, gain=0.6)), 0.24)
    write("CardDone.wav", tune(180, [("E5", 0.5), ("G5", 0.5), ("B5", 0.5), ("E6", 1.5)], [("E3", 1), ("B2", 1), ("E3", 1)], kind="pulse"), 0.34)

    # The tunes (original compositions). The title: a spy's walking bass in E minor under a stealthy line.
    lead = [("-", 2), ("E5", 0.5), ("G5", 0.5), ("B5", 1), ("A#5", 0.5), ("B5", 0.5), ("-", 1),
            ("-", 2), ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("B5", 0.5), ("A5", 0.5), ("-", 1),
            ("-", 2), ("D5", 0.5), ("F#5", 0.5), ("A5", 1), ("G5", 0.5), ("F#5", 0.5), ("-", 1),
            ("E5", 1), ("D#5", 1), ("E5", 1), ("B4", 1), ("E5", 2), ("-", 2)]
    bass = [("E2", 0.5), ("E3", 0.5), ("G2", 0.5), ("E3", 0.5)] * 4 + [("C2", 0.5), ("C3", 0.5), ("E2", 0.5), ("C3", 0.5)] * 2 \
         + [("D2", 0.5), ("D3", 0.5), ("F#2", 0.5), ("D3", 0.5)] * 2 + [("B1", 0.5), ("B2", 0.5), ("D#2", 0.5), ("B2", 0.5)] * 2 + [("E2", 1), ("B1", 1), ("E2", 2)]
    write("TitleTune.wav", cat(tune(112, lead, bass, kind="pulse"), silence(0.6)), 0.36)
    write("Win.wav", tune(132, [("E5", 0.5), ("G5", 0.5), ("B5", 0.5), ("E6", 1), ("D6", 0.5), ("E6", 0.5), ("G6", 0.5), ("B6", 3)],
                          [("E3", 1), ("B2", 1), ("C3", 1), ("D3", 1), ("E3", 3)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(70, [("B4", 1), ("G4", 1), ("E4", 1), ("D#4", 1), ("E4", 4)], [("E3", 2), ("C3", 2), ("B2", 2), ("E2", 2)], kind="tri"), 0.4)

    # The professor's voice, made here: no recording and no speech engine.
    write("Welcome.wav", professor(WELCOME), 0.8)
    write("Defeat.wav", professor(DEFEAT, pitch=120), 0.8)


if __name__ == "__main__":
    main()
