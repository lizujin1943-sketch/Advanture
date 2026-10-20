#!/usr/bin/env python3
"""Export millimetre collision/fish data from the approved Blender coastline."""
import json
import math
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
manifest = json.loads((ROOT / "samples/HookIsland/Exports/export_manifest.json").read_text())
shore = manifest["shoreline_blender_xy"]
direction = (-0.62, -0.7846018)
side = (-direction[1], direction[0])
anchor = (-7.2666, -7.58693)
pier = [
    [anchor[0] + direction[0] * s + side[0] * w,
     anchor[1] + direction[1] * s + side[1] * w]
    for s, w in [(-1.4, -1.475), (-1.4, 1.475), (3.2, 1.475), (3.2, -1.475)]
]


def inside(p, polygon):
    x, z = p
    hit = False
    for a, b in zip(polygon, polygon[1:] + polygon[:1]):
        if (a[1] > z) != (b[1] > z):
            if x < (b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]) + a[0]:
                hit = not hit
    return hit


spots = []
for i in range(24):
    angle = 2 * math.pi * i / 24
    ray = (math.cos(angle), math.sin(angle))
    # Walk outwards from the central shop; remain clear of the pier as well.
    edge = max(r / 10 for r in range(5, 240)
               if inside([ray[0] * r / 10, ray[1] * r / 10], shore)
               or inside([ray[0] * r / 10, ray[1] * r / 10], pier))
    distance = edge + 2.4 + (i % 3) * .35
    spots.append([ray[0] * distance, ray[1] * distance])

spawns = [[-6.5, -6], [6.5, -5], [6, 6], [-7, 6]]
payload = bytearray(struct.pack("<i", 0x314D4948))
for points in [shore, pier, spawns, spots]:
    payload.extend(struct.pack("<i", len(points)))
    for x, z in points:
        payload.extend(struct.pack("<ii", round(x * 1000), round(z * 1000)))
output = ROOT / "samples/TacticalGame.Unity/Assets/HookIsland/Data/IslandMap.bytes"
output.parent.mkdir(parents=True, exist_ok=True)
output.write_bytes(payload)
print(f"{output}: {len(shore)} coastline vertices, 24 fish spots, {len(payload)} bytes")
