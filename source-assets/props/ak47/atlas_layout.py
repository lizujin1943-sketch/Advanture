"""Paths and authored trim regions. Coordinates use Blender's lower-left UV origin."""
from pathlib import Path

SOURCE = Path(__file__).resolve().parent
PROJECT = SOURCE.parents[2]
GAME = PROJECT / 'samples/TacticalGame.Unity/Assets/TacticalGame/Resources/Weapons/AK47'
PROOFS = PROJECT / 'artifacts/fortified-compound-game/ak47'
SIZE = 2048
REGIONS = {
    'stock': (0, .49, .53, 1), 'walnut': (0, .24, .53, .49),
    'grip': (0, 0, .24, .24), 'cloth': (.24, 0, .53, .24),
    'steel': (.53, .55, 1, 1), 'mag': (.53, .32, .83, .55),
    'edge': (.83, .32, 1, .55), 'leather': (.53, 0, .76, .32),
    'rubber': (.76, 0, .90, .32), 'black': (.90, 0, 1, .10),
    'thread': (.90, .10, 1, .16), 'oxide': (.90, .16, .935, .194),
    'mark': (.90, .194, 1, .239), 'blued': (.90, .239, 1, .32),
    'well': (.935, .16, 1, .194),
}
TEXTURES = ('AK47_BaseColor.png', 'AK47_MetalSmooth.png', 'AK47_Normal.png')


def remap(region, u, v):
    x0, y0, x1, y1 = REGIONS[region]
    # Leave enough texels for bilinear filtering at distance in the game camera.
    pad = 6 / SIZE
    return (x0 + pad + u * (x1 - x0 - 2 * pad),
            y0 + pad + v * (y1 - y0 - 2 * pad))
