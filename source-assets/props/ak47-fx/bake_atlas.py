"""Bake the original 4×2 RGBA shot sprite sheet. Run with Python 3, NumPy and Pillow."""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


CELL = 128
SAMPLE = CELL * 3
Y, X = np.mgrid[0:SAMPLE, 0:SAMPLE]
X = 2 * (X + 0.5) / SAMPLE - 1
Y = 1 - 2 * (Y + 0.5) / SAMPLE


def smooth(lo, hi, value):
    t = np.clip((value - lo) / (hi - lo), 0, 1)
    return t * t * (3 - 2 * t)


def noise(rng, cells):
    """Bicubic value noise sampled consistently across a tile."""
    coarse = Image.fromarray(rng.integers(0, 256, (cells, cells), dtype=np.uint8))
    image = coarse.resize((SAMPLE, SAMPLE), Image.Resampling.BICUBIC)
    return np.asarray(image) / 255.0 - 0.5


def rgba(rgb, alpha):
    channels = np.dstack((np.clip(rgb, 0, 1), np.clip(alpha, 0, 1)))
    return Image.fromarray(np.uint8(channels * 255 + 0.5), "RGBA")


def gradient(value, stops):
    keys, colours = zip(*stops)
    return np.stack([np.interp(value, keys, channel) for channel in zip(*colours)], axis=2)


def fire_colour(density, core):
    heat = np.clip(0.36 * density + 0.79 * core, 0, 1)
    rgb = gradient(heat, [
        (0.00, (0.94, 0.54, 0.32)), (0.27, (1.00, 0.69, 0.43)),
        (0.59, (1.00, 0.84, 0.61)), (0.82, (1.00, 0.92, 0.74)),
        (1.00, (1.00, 0.98, 0.88)),
    ])
    edge = 1 - smooth(0.90, 0.986, np.maximum(abs(X), abs(Y)))
    return rgba(rgb, (1 - np.exp(-2.00 * np.maximum(density - 0.012, 0))) * edge)


def ribbon(start, end, bend, width, warp, grain, phase):
    """A curved, tapering sheet with an interrupted outer edge."""
    dx, dy = np.subtract(end, start)
    length = np.hypot(dx, dy)
    dx, dy = dx / length, dy / length
    px, py = X - start[0], Y - start[1]
    along = px * dx + py * dy
    across = -px * dy + py * dx
    t = np.clip(along / length, 0, 1)
    drift = bend * np.sin(np.pi * t) + warp * (0.28 + t)
    thickness = 0.005 + width * (0.43 + 0.65 * np.sin(np.pi * t)) * (1 - 0.69 * t)
    profile = np.exp(-((across - drift) / thickness) ** 2)
    profile *= smooth(-0.04, 0.035, along) * (1 - smooth(0.88, 1, t))
    gaps = np.clip(0.51 + 2.28 * grain + 0.42 * np.sin(t * 27 + phase), 0.015, 1.0)
    return profile * (1 - smooth(0.24, 0.77, t) * (1 - gaps))


def crown(seed):
    rng = np.random.default_rng(seed)
    warp = noise(rng, 7) * 0.15 + noise(rng, 19) * 0.062
    grain = noise(rng, 12) * 0.70 + noise(rng, 32) * 0.30
    flame = 0.68 * np.exp(-((X + 0.018 + warp * 0.45) / 0.17) ** 2 -
                          ((Y - 0.025 + warp * 0.3) / 0.12) ** 2)
    core = np.exp(-((X + 0.025) / 0.070) ** 2 - ((Y - 0.018) / 0.052) ** 2)
    # The front view has an open sweep on one side. It has no radial symmetry.
    lobes = [((-0.12, -0.025), (0.75, 0.40), 0.13, 0.11, 0.92),
             ((0.035, 0.01), (0.22, 0.63), -0.09, 0.086, 0.66),
             ((-0.09, -0.04), (-0.44, -0.27), 0.085, 0.085, 0.56),
             ((0.16, 0.14), (0.65, 0.55), -0.11, 0.044, 0.38)]
    for start, end, bend, width, weight in lobes:
        tip = np.asarray(end) + rng.uniform(-0.14, 0.14, 2)
        body = ribbon(start, tip, bend, width, warp, grain, rng.uniform(0, 6))
        flame += body * weight
    for px, py, radius in [(0.68, 0.38, 0.030), (-0.37, -0.24, 0.023), (0.33, 0.58, 0.024)]:
        flame += 0.31 * np.exp(-((X - px) / radius) ** 2 - ((Y - py) / (radius * 0.6)) ** 2)
    return fire_colour(flame, core)


def jet(seed):
    rng = np.random.default_rng(seed)
    warp = noise(rng, 8) * 0.15 + noise(rng, 23) * 0.064
    grain = noise(rng, 15) * 0.67 + noise(rng, 42) * 0.33
    flame = 0.65 * np.exp(-((X + 0.74) / 0.21) ** 2 - ((Y + warp * 0.52) / 0.10) ** 2)
    flame += 0.065 * np.exp(-((X + 0.30) / 0.61) ** 2 - ((Y + warp) / 0.23) ** 2)
    core = 1.11 * np.exp(-((X + 0.74) / 0.14) ** 2 - ((Y + warp * 0.4) / 0.058) ** 2)
    streams = [((-0.93, 0), (0.89, -0.08), 0.12, 0.16, 0.95),
               ((-0.81, 0.01), (0.63, 0.38), -0.105, 0.097, 0.82),
               ((-0.66, -0.025), (0.45, -0.39), 0.07, 0.094, 0.72),
               ((-0.31, 0.07), (0.71, 0.18), -0.09, 0.054, 0.52)]
    for start, end, bend, width, weight in streams:
        tip = np.asarray(end) + rng.uniform(-0.105, 0.105, 2)
        body = ribbon(start, tip, bend, width, warp, grain, rng.uniform(0, 6))
        flame += body * weight
    core += 0.25 * flame * np.exp(-((X + 0.58) / 0.33) ** 2 - (Y / 0.12) ** 2)
    return fire_colour(flame, core)


def cloud(seed, smoke):
    rng = np.random.default_rng(seed)
    coarse = noise(rng, 8)
    grain = noise(rng, 17) * 0.61 + noise(rng, 39) * 0.27 + noise(rng, 89) * 0.12
    w = X + coarse * 0.16
    h = Y + noise(rng, 7) * 0.16
    field = np.zeros_like(X)
    lobes = [(-0.25, -0.08, 0.35, 0.40), (0.10, -0.31, 0.36, 0.27),
             (0.26, 0.12, 0.31, 0.34), (-0.08, 0.34, 0.30, 0.25),
             (-0.46, 0.16, 0.18, 0.23), (0.42, -0.22, 0.20, 0.17)]
    for cx, cy, sx, sy in lobes:
        field += np.exp(-((w - cx) / sx) ** 2 - ((h - cy) / sy) ** 2)
    relief = np.clip(field * 0.60 + coarse * 0.48 + grain * 0.50, 0, 1)
    if smoke:
        # Narrow gaps and a curled translucent rim break up overlapping puffs.
        eddy = np.sqrt(((w + 0.06) / 0.40) ** 2 + ((h - 0.08) / 0.38) ** 2)
        field *= 1 - 0.44 * np.exp(-((eddy - 0.63) / 0.22) ** 2)
        light, dark = (0.83, 0.86, 0.87), (0.32, 0.36, 0.40)
    else:
        light, dark = (0.88, 0.81, 0.69), (0.43, 0.39, 0.35)
    shade = np.clip(0.27 + 0.56 * relief + 0.18 * (Y - X * 0.5) + grain * 0.28, 0, 1)
    alpha = smooth(0.035, 1.17, np.maximum(field, 0) * (0.94 + grain * 1.1))
    alpha *= 1 - smooth(0.78, 0.97, np.maximum(abs(X), abs(Y)))
    return rgba(gradient(shade, [(0, dark), (1, light)]), alpha)


def streak():
    t = (X + 1) * 0.5
    body = smooth(0.025, 0.62, t) * (1 - smooth(0.81, 0.977, t))
    width = 0.032 + 0.18 * t
    halo = body * np.exp(-(Y / (width * 2.7)) ** 2)
    filament = body * np.exp(-(Y / width) ** 2)
    heat = np.clip(0.20 * halo + 0.80 * filament + 0.17 * t, 0, 1)
    rgb = gradient(heat, [(0, (0.96, 0.47, 0.14)), (0.48, (1, 0.75, 0.38)),
                          (0.88, (1, 0.96, 0.77)), (1, (1, 0.99, 0.88))])
    return rgba(rgb, 0.26 * halo + 0.74 * filament)


def chip():
    result = Image.new("RGBA", (SAMPLE, SAMPLE))
    draw = ImageDraw.Draw(result)
    pts = [(0.16, 0.58), (0.25, 0.35), (0.65, 0.22), (0.82, 0.42),
           (0.69, 0.65), (0.39, 0.78)]
    verts = [(int(x * SAMPLE), int(y * SAMPLE)) for x, y in pts]
    draw.polygon(verts, fill=(140, 128, 111, 246))
    draw.polygon([verts[i] for i in (0, 1, 2, 3)], fill=(206, 191, 165, 252))
    draw.polygon([verts[i] for i in (0, 3, 5)], fill=(173, 159, 137, 249))
    return result.filter(ImageFilter.GaussianBlur(SAMPLE * 0.018))


def main():
    sheet = Image.new("RGBA", (CELL * 4, CELL * 2))
    tiles = [crown(734), jet(110), crown(651), jet(478),
             cloud(93, True), cloud(76, False), streak(), chip()]
    for index, tile in enumerate(tiles):
        tile = tile.resize((CELL, CELL), Image.Resampling.LANCZOS)
        sheet.paste(tile, (index % 4 * CELL, index // 4 * CELL))
    repo = Path(__file__).resolve().parents[3]
    output = repo / "samples/TacticalGame.Unity/Assets/TacticalGame/Resources/Weapons/AK47/FX/ShotAtlas.png"
    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output, optimize=True)


if __name__ == "__main__":
    main()
