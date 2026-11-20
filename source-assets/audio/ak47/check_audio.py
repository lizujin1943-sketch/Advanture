"""Check the shipped WAVs, including a two-player sustained-fire mix."""

import itertools
import wave

import numpy as np

from render import BANK, CLOSE_RANGE, CUES, FAR_GAIN, FAR_RANGE, LOCAL_GAIN, NEAR_GAIN, ORDER, PREVIEW, RATE, TAKES
from render import add_at, range_gain, rms


def read_pcm(path, channels):
    with wave.open(str(path), "rb") as clip:
        assert clip.getparams()[:3] == (channels, 3, RATE), f"Wrong format: {path}"
        assert clip.getcomptype() == "NONE"
        raw = np.frombuffer(clip.readframes(clip.getnframes()), np.uint8).reshape(-1, 3)
    value = raw.astype(np.int32)
    sample = value[:, 0] | (value[:, 1] << 8) | (value[:, 2] << 16)
    sample = ((sample ^ 8388608) - 8388608).reshape(-1, channels) / 8388607
    assert np.max(np.abs(sample[[0, -1]])) == 0.0, f"Nonzero file boundary: {path}"
    return sample if channels > 1 else sample[:, 0]


def interpolation_phases(signal):
    """Four sample grids, offset by quarter samples using windowed-sinc interpolation."""
    channels = signal.reshape(len(signal), -1)
    yield channels
    taps = np.arange(-24, 25, dtype=float)
    for fraction in (.25, .5, .75):
        kernel = np.sinc(taps + fraction) * np.blackman(len(taps))
        kernel /= kernel.sum()
        yield np.column_stack([np.convolve(channel, kernel, mode="same") for channel in channels.T])


def true_peak(signal):
    return max(float(np.max(np.abs(phase))) for phase in interpolation_phases(signal))


def db(value):
    return 20 * np.log10(max(value, 1e-12))


def spectrum(signal):
    # A one-sided taper retains the onset. A centred Hann would nearly discard it.
    n = round(RATE * .065)
    window = np.cos(np.linspace(0, np.pi / 2, n)) ** 2
    energy = np.abs(np.fft.rfft(signal[:n] * window)) ** 2
    energy /= energy.sum()
    freq = np.fft.rfftfreq(n, 1 / RATE)
    return float(np.sum(freq * energy)), energy[(freq > 50) & (freq < 260)].sum()


def part(signal, start, end):
    return signal[round(RATE * start):round(RATE * end)]


def check_pair(number, near, far):
    center = near.mean(axis=1)
    near_color, low_share = spectrum(center)
    far_color, _ = spectrum(far)
    near_peak, far_peak = db(true_peak(near)), db(true_peak(far))
    assert -4.5 < near_peak < -1.0 and -6.0 < far_peak < -2.8, number
    assert .20 < low_share < .70 and far_color < .55 * near_color, number
    assert len(far) > 1.4 * len(near), number
    assert np.corrcoef(part(near, .001, .021).T)[0, 1] > .98, number
    assert np.corrcoef(part(near, .030, .220).T)[0, 1] < .94, number
    near_decay = db(rms(part(near, .09, .21)) / rms(part(near, 0, .05)))
    far_decay = db(rms(part(far, .09, .21)) / rms(part(far, 0, .05)))
    assert -26 < near_decay < -12 and far_decay > near_decay + 3, number
    print(f"{number:02}   {len(near)/RATE:.3f}/{len(far)/RATE:.3f} s   "
          f"{near_peak:5.2f}/{far_peak:5.2f} dBTP   "
          f"{near_color:4.0f}/{far_color:3.0f} Hz   {near_decay:5.1f}/{far_decay:5.1f} dB")


def check_variation(pairs):
    for perspective in (0, 1):
        signals = [part(pair[perspective], 0, .10).reshape(round(.10 * RATE), -1).mean(axis=1)
                   for pair in pairs]
        correlations = [abs(np.corrcoef(a, b)[0, 1]) for a, b in itertools.combinations(signals, 2)]
        assert max(correlations) < .65, "Near-identical attacks"
        print(f"Highest attack correlation ({'far' if perspective else 'close'}): {max(correlations):.3f}")
    assert all(a != b for a, b in zip(ORDER, ORDER[1:] + ORDER[:1]))


def burst(pairs, first):
    shots = 30
    length = round(RATE * (shots * .10 + 1.2))
    direct, distant = np.zeros((length, 2)), np.zeros(length)
    for i in range(shots):
        close, far = pairs[ORDER[(first + i) % len(ORDER)]]
        add_at(direct, close, i * .10)
        add_at(distant, far, i * .10)
    return direct, distant


def check_mix(pairs):
    # The remote emitter goes fully into one ear here to exercise the largest pan gain.
    # Sweep listener distances and the time and sequence offsets between both streams.
    local, _ = burst(pairs, 1)
    local_phases = tuple(interpolation_phases(local * LOCAL_GAIN))
    local_peak = max(np.abs(phase).max() for phase in local_phases)
    worst_peak = local_peak
    for start in (1, 3, 6, 12):
        near, far = burst(pairs, start)
        for distance in (2, 9, 18, 26, 42, 80, 145, 190):
            remote = NEAR_GAIN * range_gain(CLOSE_RANGE, distance) * near.mean(axis=1)
            remote += FAR_GAIN * range_gain(FAR_RANGE, distance) * far
            if distance == 145:
                assert db(np.abs(remote).max()) > -26, "Far field inaudible in the mix"
            for source, listener in zip(interpolation_phases(remote), local_phases):
                for delay in (0, .0028, .008, .022, .05, .087):
                    start_frame = round(RATE * delay)
                    count = len(listener) - start_frame
                    combined = listener[start_frame:] + source[:count]
                    worst_peak = max(worst_peak, np.abs(combined).max())
    assert db(local_peak) < -4.5 and db(worst_peak) < -1.5, "Sustained mix exceeds headroom"
    print(f"30 rounds, 100 ms spacing: local {db(local_peak):.2f} dBTP, "
          f"loudest of 192 two-player mixes {db(worst_peak):.2f} dBTP")


def main():
    pairs = []
    print("Take   close/far lengths   4x true peaks       onset centroids   relative 90–210 ms tails")
    for number in range(1, len(TAKES) + 1):
        pair = (read_pcm(BANK / f"ShotClose{number:02}.wav", 2),
                read_pcm(BANK / f"ShotFar{number:02}.wav", 1))
        check_pair(number, *pair)
        pairs.append(pair)
    check_variation(pairs)
    for name in CUES:
        cue = read_pcm(BANK / f"{name}.wav", 1)
        peak = db(true_peak(cue))
        assert .15 * RATE < len(cue) < .4 * RATE and -11 < peak < -6
        print(f"{name}: {len(cue)/RATE:.3f} s, {peak:.2f} dBTP")
    check_mix(pairs)
    preview = read_pcm(PREVIEW, 2)
    assert .25 < true_peak(preview) < .80
    print(f"Stereo preview: {len(preview)/RATE:.2f} s, {db(true_peak(preview)):.2f} dBTP")


if __name__ == "__main__":
    main()
