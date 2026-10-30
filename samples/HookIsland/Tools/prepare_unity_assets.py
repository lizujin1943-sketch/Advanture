"""Copy the baked export and derive a shoreline distance texture from its mesh."""
import json
from pathlib import Path
import shutil
import numpy as np
from PIL import Image

SAMPLE = Path(__file__).resolve().parents[1]
ASSETS = SAMPLE.parent / "TacticalGame.Unity/Assets/HookIsland/Art"
EXPORT = SAMPLE / "Exports"


def main():
    textures, models = ASSETS / "Textures", ASSETS / "Models"
    textures.mkdir(parents=True, exist_ok=True)
    models.mkdir(parents=True, exist_ok=True)
    for path in EXPORT.glob("*.png"):
        shutil.copy2(path, textures / path.name)
    shutil.copy2(EXPORT / "hook_island_v2.fbx", models / "hook_island_v2.fbx")
    outline = np.asarray(json.loads((EXPORT / "export_manifest.json").read_text())
                         ["shoreline_blender_xy"], dtype=np.float32)
    if len(outline) < 3:
        raise ValueError("No shoreline in the Blender export.")
    # Blender (x,y) becomes Unity (x,z). PNG rows go from north to south.
    size, extent, distance_range = 512, 80.0, 16.0
    coords = (np.arange(size, dtype=np.float32) + .5) / size * extent - extent / 2
    x, y = np.meshgrid(coords, coords[::-1])
    distance_squared = np.full_like(x, np.inf)
    inside = np.zeros(x.shape, dtype=bool)
    for a, b in zip(outline, np.roll(outline, -1, axis=0)):
        edge = b - a
        t = np.clip(((x-a[0])*edge[0] + (y-a[1])*edge[1]) /
                    max(float(edge @ edge), 1e-12), 0, 1)
        d2 = (x-a[0]-t*edge[0])**2 + (y-a[1]-t*edge[1])**2
        distance_squared = np.minimum(distance_squared, d2)
        if abs(float(edge[1])) > 1e-8:
            crosses = ((a[1] > y) != (b[1] > y)) & (
                x < (b[0]-a[0]) * (y-a[1]) / edge[1] + a[0])
            inside ^= crosses
    distance = np.sqrt(distance_squared) * np.where(inside, -1, 1)
    encoded = np.round(np.clip(distance / (2*distance_range)+.5, 0, 1)*65535).astype(np.uint16)
    rgb = np.zeros((size, size, 3), dtype=np.uint8)
    rgb[:, :, 0] = encoded >> 8
    rgb[:, :, 1] = encoded & 255
    Image.fromarray(rgb).save(textures / "ShoreDistance.png")
    # Quantization error bound; this texture uses linear data, no compression/mips.
    decoded = encoded.astype(np.float64)/65535*32-16
    valid = np.abs(distance) < 15.9
    error = float(np.max(np.abs(distance[valid]-decoded[valid])))
    assert error < .00025
    print(f"Copied baked assets; shoreline: {len(outline)} vertices, {size}², max encoding error {error:.6f} m.")


if __name__ == "__main__":
    main()
