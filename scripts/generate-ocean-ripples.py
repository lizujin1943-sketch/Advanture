#!/usr/bin/env python3
"""Create an original, seamless ocean slope map. No downloaded artwork or dependencies."""
import math
from pathlib import Path
import random
import struct
import zlib

SIZE = 512
DESTINATION = Path(__file__).resolve().parents[1] / "samples/TacticalGame.Unity/Assets/HookIsland/Art/Textures/OceanRipples.png"


def generate():
    rng = random.Random(71803)
    slope_x = [0.0] * (SIZE * SIZE)
    slope_z = [0.0] * (SIZE * SIZE)
    elevation = [0.0] * (SIZE * SIZE)
    for _ in range(40):
        # Integer frequencies make both edges periodic. Crests share a prevailing wind.
        kx, kz = rng.randint(-9, 9), rng.randint(5, 30)
        length = math.hypot(kx, kz)
        phase = rng.random() * math.tau
        weight = rng.uniform(.7, 1.3) / math.sqrt(length)
        cos_x = [math.cos(math.tau * kx * x / SIZE + phase) for x in range(SIZE)]
        sin_x = [math.sin(math.tau * kx * x / SIZE + phase) for x in range(SIZE)]
        for z in range(SIZE):
            angle = math.tau * kz * z / SIZE
            cz, sz = math.cos(angle), math.sin(angle)
            for x in range(SIZE):
                i = z * SIZE + x
                derivative = (cos_x[x] * cz - sin_x[x] * sz) * weight
                slope_x[i] += derivative * kx / length
                slope_z[i] += derivative * kz / length
                elevation[i] += (sin_x[x] * cz + cos_x[x] * sz) * weight / length

    # Bounded, shared normalization retains wind direction and avoids clipped crests.
    slope_range = max(max(map(abs, slope_x)), max(map(abs, slope_z))) * 1.03
    height_range = max(map(abs, elevation)) * 1.03
    raw = bytearray()
    for z in range(SIZE):
        raw.append(0)  # PNG filter: none.
        for x in range(SIZE):
            i = z * SIZE + x
            raw.extend((round(127.5 + slope_x[i] / slope_range * 127.5),
                        round(127.5 + slope_z[i] / slope_range * 127.5),
                        round(127.5 + elevation[i] / height_range * 127.5), 255))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    DESTINATION.parent.mkdir(parents=True, exist_ok=True)
    DESTINATION.write_bytes(png)
    print(f"Wrote {DESTINATION} ({len(png)} bytes; linear RG slopes, B elevation)")


if __name__ == "__main__":
    generate()
