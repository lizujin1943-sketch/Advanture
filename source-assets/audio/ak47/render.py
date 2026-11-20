"""Render original game audio and a stereo listening sequence. Requires NumPy."""

import io
import os
import tempfile
import wave
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[3]
BANK = ROOT / "samples/TacticalGame.Unity/Assets/TacticalGame/Resources/Weapons/AK47/Audio"
PREVIEW = ROOT / "artifacts/fortified-compound-game/ak47/AK47_AudioPreview.wav"
RATE = 48000
LOCAL_GAIN, NEAR_GAIN, FAR_GAIN, HANDLING_GAIN = .62, .38, .63, .57
ORDER = (1, 3, 0, 4, 2, 4, 0, 2, 1, 3, 2, 4, 1, 3, 0, 3, 1, 4, 0, 2, 0, 2, 3, 1, 4)
# Seed, lower-body colour, upper-mid colour, reflection timing and level.
TAKES = (
    (88491, 1.00, .95, 1.03, .00),
    (45376, .92, 1.11, .97, -.45),
    (21790, 1.11, .91, 1.10, -.12),
    (32741, .96, 1.04, .92, -.70),
    (72930, 1.05, 1.00, 1.00, -.28),
)
CUES = ("ReloadShift", "ReloadContact", "ReloadSettle")
CLOSE_RANGE = ((0, 1), (3, 1), (9, .78), (18, .40), (32, .09), (48, 0), (220, 0))
FAR_RANGE = ((0, 0), (4, 0), (12, .20), (26, .58), (42, .56), (80, .37), (145, .21), (190, .09), (220, 0))


def timeline(seconds):
    return np.arange(round(RATE * seconds), dtype=np.float64) / RATE


def band_noise(rng, t, low, high):
    """Draw a fresh broad-band texture with gently rounded shoulders."""
    f = np.fft.rfftfreq(t.size, 1 / RATE)
    shape = 1 / np.sqrt(1 + (low / np.maximum(f, 1.0)) ** 6)
    shape /= np.sqrt(1 + (f / high) ** 8)
    shape[0] = 0.0
    shaped = np.fft.irfft(np.fft.rfft(rng.standard_normal(t.size)) * shape, n=t.size)
    return shaped / np.sqrt(np.mean(shaped * shaped))


def lowpass(signal, cutoff):
    """Short causal kernel, including its phase response on early reflections."""
    tau = RATE / (2 * np.pi * cutoff)
    kernel = np.exp(-np.arange(max(3, int(11 * tau))) / tau)
    return np.convolve(signal, kernel / kernel.sum(), mode="full")[:len(signal)]


def envelope(t, attack, decay):
    return -np.expm1(-t / attack) * np.exp(-t / decay)


def pressure(t, onset, width):
    u = np.maximum(t - onset, 0.0) / width
    return (-np.expm1(-10 * u)) * (1 - u) * np.exp(-u)


def add_at(target, source, time, gain=1.0):
    start = round(RATE * time)
    length = min(len(source), len(target) - start)
    target[start:start + length] += source[:length] * gain


def direct_strike(rng, weight, bite):
    t = timeline(.210)
    front = pressure(t, .00015, .00063 / bite)
    front -= .46 * pressure(t, rng.uniform(.0009, .0017), .00095 / bite)
    front += .33 * pressure(t, rng.uniform(.0028, .0041), .00070)
    grit = .46 * band_noise(rng, t, 1700, 9200 * bite) * envelope(t, .0006, .011)
    grit += .62 * band_noise(rng, t, 330, 2300 * bite) * envelope(t, .0014, .020)
    body = .43 * band_noise(rng, t, 62 * weight, 290 * weight) * envelope(t, .0025, .035)
    for hz, gain, decay in ((76, .72, .031), (118, .42, .024), (209, .24, .015)):
        bend = hz * weight * (t + .0019 * -np.expm1(-t / .004))
        body += gain * np.sin(2 * np.pi * bend) * envelope(t, .0013, decay)
    return 1.22 * front + grit + body


def rms(signal):
    return float(np.sqrt(np.mean(signal * signal)))


def reflections(dry, t, rng, scale, far=False):
    """Early discrete returns open into a band-limited diffuse decay."""
    if far:
        returns = ((.061, .27), (.111, -.16), (.177, .22), (.231, .12), (.306, -.11), (.379, .065))
        onset, decay, brightness, diffuse_ratio = .042, .147, 1380, .102
    else:
        returns = ((.027, .16), (.049, -.10), (.078, .12), (.109, .075), (.149, -.053))
        onset, decay, brightness, diffuse_ratio = .026, .079, 2900, .071
    wet = np.zeros_like(t)
    for i, (delay, gain) in enumerate(returns):
        time = delay * scale * rng.uniform(.90, 1.11)
        reflected = lowpass(dry, brightness / (1 + .26 * i))
        add_at(wet, reflected, time, gain * rng.uniform(.83, 1.13))

    age = np.maximum(t - onset, 0.0)
    ir = band_noise(rng, t, 150, brightness) * envelope(age, .010, decay)
    ir += .45 * band_noise(rng, t, 820, 1.8 * brightness) * envelope(age, .004, decay * .47)
    ir *= np.interp(t, t[::840], rng.uniform(.70, 1.16, len(t[::840])))
    nfft = 1 << (len(t) + len(dry) - 1).bit_length()
    diffuse = np.fft.irfft(np.fft.rfft(ir, nfft) * np.fft.rfft(dry, nfft), n=nfft)[:len(t)]
    reference = slice(round(RATE * .08), round(RATE * .16))
    wet += diffuse * (diffuse_ratio * rms(dry[:round(RATE * .05)]) / rms(diffuse[reference]))
    return wet


def master(signal, target_db, bass_cutoff, fade=.06):
    channels = np.asarray(signal).reshape(len(signal), -1).copy()
    for channel in channels.T:
        channel -= lowpass(channel, bass_cutoff)
    fade_count = round(RATE * fade)
    channels[-fade_count:] *= np.linspace(1, 0, fade_count)[:, None] ** 2
    channels[:8] *= np.linspace(0, 1, 8)[:, None]
    channels /= np.abs(channels).max()
    channels = .88 * np.tanh(1.35 * channels) + .12 * channels
    channels *= 10 ** (target_db / 20) / np.abs(channels).max()
    return channels if signal.ndim == 2 else channels[:, 0]


def shot_pair(recipe):
    seed, weight, bite, space, trim = recipe
    rng = np.random.default_rng(seed)
    dry = direct_strike(rng, weight, bite)
    t = timeline(.63 * space)
    center = np.zeros_like(t)
    add_at(center, dry, 0)
    # The short off-centre tick comes after the initial impact.
    tick_t = timeline(.060)
    tick = .062 * band_noise(rng, tick_t, 860, 6300) * envelope(tick_t, .0006, .005)
    tick += .075 * pressure(tick_t, .001, .0011)
    near_channels = []
    for delay, level in ((.029, .62), (.032, 1.00)):
        channel = center + reflections(dry, t, rng, space)
        add_at(channel, tick, delay * space, level)
        near_channels.append(channel)
    close = master(np.stack(near_channels, axis=1), -2.65 + trim, 32)

    t = timeline(1.02 * space)
    dry_t = timeline(.210)
    distant = lowpass(lowpass(dry, 2050 * bite), 2250 * bite) * .84
    contour = band_noise(rng, dry_t, 120 * weight, 620 * weight) * envelope(dry_t, .007, .052)
    add_at(distant, contour, .010, .35)
    distant = lowpass(distant, 3400)
    field = reflections(distant, t, rng, space, far=True)
    add_at(field, distant, .0012)
    return close, master(field, -4.65 + .65 * trim, 47, fade=.11)


def cloth(rng, length, brightness):
    t = timeline(length)
    position = t / length
    gesture = np.interp(position, (0, .10, .24, .43, .61, .77, 1), (0, .14, .87, .58, 1, .64, 0))
    spacing = rng.uniform(.002, .009, int(length / .002) + 2).cumsum()
    spacing = np.r_[0, spacing]
    fibres = np.interp(t, spacing, rng.uniform(.11, 1.18, len(spacing))) ** 1.8
    weave = band_noise(rng, t, 720, brightness) * fibres
    weave += .29 * band_noise(rng, t, 200, 1500)
    return gesture * weave


def tap(rng, scale, damping):
    t = timeline(.12)
    strike = .7 * pressure(t, .0002, .0008 / scale)
    strike += .34 * band_noise(rng, t, 880, 5800 * scale) * envelope(t, .0004, .004)
    for hz, gain, decay in ((380, .48, .009), (915, .24, .015), (1580, .20, .021), (3105, .12, .012)):
        frequency = hz * scale * rng.uniform(.95, 1.05)
        strike += gain * np.sin(2 * np.pi * frequency * t) * envelope(t, .00036, decay * damping)
    return strike


def reload_cues():
    rng = np.random.default_rng(51821)
    shift = np.zeros_like(timeline(.32))
    add_at(shift, cloth(rng, .205, 4900), .001, .39)
    add_at(shift, tap(rng, .86, .65), .049, .55)
    add_at(shift, cloth(rng, .116, 3200), .140, .19)
    contact = np.zeros_like(timeline(.27))
    add_at(contact, cloth(rng, .124, 3500), .001, .18)
    add_at(contact, tap(rng, .67, .86), .052, 1.00)
    add_at(contact, tap(rng, 1.35, .72), .064, .45)
    add_at(contact, cloth(rng, .103, 3000), .110, .13)
    settle = np.zeros_like(timeline(.24))
    add_at(settle, tap(rng, 1.18, .79), .008, .74)
    add_at(settle, tap(rng, .88, .63), .040, .43)
    add_at(settle, cloth(rng, .139, 3900), .055, .20)
    return (master(shift, -9.2, 95, .04), master(contact, -6.9, 85, .04), master(settle, -10, 105, .04))


def range_gain(curve, distance):
    for (start, a), (end, b) in zip(curve, curve[1:]):
        if distance <= end:
            u = np.clip((distance - start) / (end - start), 0.0, 1.0)
            return a + (b - a) * u * u * (3 - 2 * u)
    return curve[-1][1]


def listening_sequence(pairs, cues):
    mix = np.zeros((round(RATE * 10.45), 2))
    for seq, time in enumerate((.26, 1.20, 2.36, 2.46, 2.56, 2.66, 2.76, 8.92, 9.02), 1):
        add_at(mix, pairs[ORDER[seq]][0], time, LOCAL_GAIN)
    for seq, (time, distance, pan) in enumerate(((3.70, 33, -.65), (4.50, 70, .72),
                                                   (4.60, 70, .72), (9.25, 48, -.43)), 1):
        close, far = pairs[ORDER[seq]]
        remote = FAR_GAIN * range_gain(FAR_RANGE, distance) * far.copy()
        add_at(remote, close.mean(axis=1), 0, NEAR_GAIN * range_gain(CLOSE_RANGE, distance))
        angle = (pan + 1) * np.pi / 4
        add_at(mix, remote[:, None] * np.array([np.cos(angle), np.sin(angle)]), time)
    for time, cue in zip((5.87, 6.90, 7.62), cues):
        add_at(mix, cue[:, None], time, HANDLING_GAIN)
    return mix


def write_wav(path, signal):
    """Write dithered PCM24; keep unchanged asset files and their import timestamps intact."""
    samples = signal.reshape(len(signal), -1)
    dither = np.random.default_rng(2120)
    fraction = dither.random(samples.shape) - dither.random(samples.shape)
    pcm = np.rint(samples * 8388607 + fraction).astype(np.int32)
    pcm[[0, -1]] = 0
    packed = np.stack((pcm, pcm >> 8, pcm >> 16), axis=-1).astype(np.uint8)
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as output:
        output.setparams((samples.shape[1], 3, RATE, len(samples), "NONE", "not compressed"))
        output.writeframes(packed.tobytes())
    data = buffer.getvalue()
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and path.read_bytes() == data:
        return
    with tempfile.NamedTemporaryFile(dir=path.parent, prefix=".render-", delete=False) as out:
        out.write(data)
        temporary = out.name
    os.chmod(temporary, 0o644)
    os.replace(temporary, path)


def main():
    pairs = [shot_pair(recipe) for recipe in TAKES]
    for number, (close, far) in enumerate(pairs, 1):
        write_wav(BANK / f"ShotClose{number:02}.wav", close)
        write_wav(BANK / f"ShotFar{number:02}.wav", far)
    cues = reload_cues()
    for name, signal in zip(CUES, cues):
        write_wav(BANK / f"{name}.wav", signal)
    write_wav(PREVIEW, listening_sequence(pairs, cues))
    print(f"{len(pairs) * 2 + len(cues)} game clips in {BANK}\nListening sequence: {PREVIEW}")


if __name__ == "__main__":
    main()
