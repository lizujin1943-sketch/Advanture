"""Anatomical volumes for a continuous voxel-sculpted Deep One study."""
import math
import random
from geometry import CreatureMesh


def volume(m, center, size, bone='Spine', folds=.015):
    m.surface(center, size, bone=bone, segments=36, rings=24, folds=folds)


def anatomy(materials):
    m = CreatureMesh('DeepOneSculpt', materials)
    m.bones['Spine'] = ((0, .78, -.08), (0, 1.46, -.02), 'Root')
    m.bones['Head'] = ((0, 1.56, .09), (0, 1.91, .19), 'Spine')
    # Ribcage, abdomen and pelvis overlap into one skin, with a pronounced hunched back.
    volume(m, (0, 1.25, -.04), (.245, .35, .17))
    volume(m, (0, 1.05, -.005), (.155, .24, .13))
    volume(m, (0, .84, -.055), (.20, .20, .16))
    volume(m, (.025, 1.49, -.095), (.235, .23, .185))
    m.tube([(0, 1.44, -.03), (.01, 1.62, .09), (.01, 1.76, .17)], [.13, .105, .11], sides=24, rings=42)
    volume(m, (.012, 1.83, .135), (.135, .21, .145), 'Head')
    volume(m, (.005, 1.62, .195), (.12, .14, .13), 'Head')
    m.tube([(-.015, 1.77, .275), (0, 1.91, .247), (.015, 2.015, .115)],
           [.022, .04, .027], bone='Head', sides=20, rings=32)
    # Two asymmetric cheek plates, trapezius, pectoral slopes and iliac crests.
    for s in (-1, 1):
        suffix = 'L' if s < 0 else 'R'
        volume(m, (s * .117, 1.73, .183), (.055, .145, .075), 'Head')
        m.tube([(s * .018, 1.858, .254), (s * .085, 1.889, .254), (s * .147, 1.911, .155)],
               [.012, .022, .004], bone='Head', sides=18, rings=32)
        m.tube([(s * .07, 1.66, .035), (s * .20, 1.51, -.02), (s * .32, 1.45, -.025)],
               [.07, .11, .09], sides=24, rings=36)
        volume(m, (s * .125, 1.36, .060), (.13, .125, .082))
        m.tube([(s * .19, 1.34, .035), (s * .17, 1.14, .075), (s * .095, .91, .07)],
               [.004, .022, .002], sides=20, rings=32)
        # Long digitigrade legs with calf volume, a thin ankle and flattened feet.
        leg = [(s * .135, .82, -.04), (s * .245, .48, .09),
               (s * .25, .115, -.07), (s * .265, .055, .27)]
        chain = m.chain('Leg' + suffix, leg, [.104, .083, .055, .028], 'leg', sides=20)
        volume(m, (s * .185, .69, -.005), (.105, .22, .10), chain[0])
        volume(m, (s * .26, .35, -.045), (.074, .17, .087), chain[1])
        volume(m, (s * .26, .07, .12), (.105, .056, .20), chain[-1])
        for j in range(3):
            x = s * .26 + (j - 1) * .077
            m.tube([(x, .075, .20), (x + s * .018, .060, .31),
                    (x + s * .035, .047, .39 - .035 * abs(j - 1))],
                   [.043, .035, .016], bone=chain[-1], sides=16, rings=24)
        # The right forearm is heavier; all shoulder/elbow connections fuse during sculpting.
        arm = [(s * .31, 1.46, -.015), (s * .48, 1.04, -.015),
               (s * .585, .64, .17), (s * .59, .49, .23)]
        arm_bones = m.chain('Arm' + suffix, arm, [.10, .075, .051, .054], 'arm', sides=24)
        volume(m, (s * .32, 1.43, -.015), (.12, .16, .115), arm_bones[0])
        volume(m, (s * .40, 1.24, .015), (.077, .215, .074), arm_bones[0])
        volume(m, (s * .52, .88, .07), (.068 if s < 0 else .087, .19, .076), arm_bones[1])
        volume(m, (s * .59, .54, .22), (.085, .125, .065), arm_bones[-1])
        for finger in range(4):
            x = s * .59 + (finger - 1.5) * .048
            length = [.18, .24, .26, .21][finger]
            points = [(x, .51, .235), (x + (finger - 1.5) * .015, .43, .26),
                      (x + (finger - 1.5) * .025, .51 - length, .30),
                      (x + (finger - 1.5) * .024, .48 - length, .36)]
            m.chain('Finger' + suffix + str(finger), points, [.028, .024, .019, .009],
                    'finger', parent=arm_bones[-1], sides=16)
        m.chain('Thumb' + suffix, [(s * .55, .58, .23), (s * .46, .51, .27), (s * .43, .42, .34)],
                [.04, .03, .011], 'finger', parent=arm_bones[-1], sides=18)
        # Low relief ribs and tendons become part of the continuous skin.
        for k in range(5):
            y = 1.29 - k * .045
            m.tube([(s * .045, y, .098), (s * .14, y + .015, .108), (s * .215, y + .06, .022)],
                   [.001, .009, .001], rings=32, sides=12, ribbing=0)
        for k in range(3):
            m.tube([(s * (.48 + k * .025), 1.02, .065), (s * (.54 + k * .018), .80, .17),
                    (s * (.57 + k * .018), .61, .22)], [.001, .006, .001], bone=arm_bones[1], rings=34, sides=10)
    return m


def details(m):
    """Separate eyes/teeth, curved fins, keratin growth and flexible barbels."""
    # Head shape stays narrow; the eyes are small, recessed, reflective organs.
    for s in (-1, 1):
        m.surface((s * .100, 1.863, .246), (.024, .012, .013), 5, 'Head', 40, 28, 0)
        m.surface((s * .102, 1.864, .257), (.003, .005, .0015), 6, 'Head', 24, 16, 0)
        # Three nested gill arches, each with thin dark tissue between raised skin lips.
        for k in range(3):
            y = 1.735 - k * .042
            x = s * .14
            pts = [(x * .76, y + .023, .244 - k * .003), (x, y, .225 - k * .004),
                   (x * 1.07, y - .018, .181 - k * .003)]
            m.tube(pts, [.004, .008, .001], 3, 'Head', rings=28, sides=10)
            m.tube([(p[0], p[1] + .008, p[2] + .002) for p in pts], [.003, .006, .001],
                   0, 'Head', rings=28, sides=12)
        # Fan fins use a curved surface with scalloped trailing margins, not flat triangles.
        fin(m, (s * .13, 1.66, .10), s, .18, .30, 'Head')
        fin(m, (s * .52, .80, .04), s, .12, .24, 'Arm' + ('L' if s < 0 else 'R') + '_1')
    # The mouth is a Boolean-carved recess. Teeth grow from irregular gum positions.
    rng = random.Random(451)
    for row in (-1, 1):
        for k in range(11):
            x = (k - 5) * .015 + rng.uniform(-.003, .003)
            y = 1.655 + row * (.126 - .045 * abs(x) / .078) + rng.uniform(-.008, .008)
            z = .277 + rng.uniform(-.006, .007)
            length = rng.uniform(.025, .065)
            tip = (x * .76 + .006 * math.sin(k), y - row * length, z + .018)
            m.tube([(x, y, z), (x * .92, y - row * length * .42, z + .022), tip],
                   [.0055, .0038, .0005], 2, 'Head', rings=20, sides=10, ribbing=.025)
    # Irregular soft lip bridges the carved face and the embedded tooth roots.
    lip = [(math.cos(a) * (.087 + .008 * math.sin(a * 5)), 1.655 + math.sin(a) * .128,
            .276 + .012 * math.sin(a * 3)) for a in [math.tau * i / 64 for i in range(65)]]
    m.tube(lip, [.006, .009, .005], 3, 'Head', rings=100, sides=12, ribbing=.10)
    # Uneven trailing barbels have several changes of curvature and taper continuously.
    for k in range(5):
        x = (k - 2) * .047
        length = .25 + (k % 3) * .09
        points = [(x, 1.55, .20), (x, 1.48, .26), (x * 1.4, 1.43, .31), (x * 1.8, 1.50 - length * .7, .38),
                  (x * 1.9 + .045, 1.50 - length, .40), (x * 1.5 + .07, 1.48 - length, .34)]
        m.chain('Barbel' + str(k), points, [.026, .022, .018, .012, .006, .0015], sides=16, parent='Head')
    # Asymmetry comes from staggered dorsal plates and crusted shoulder growth.
    for k in range(11):
        y = 1.03 + k * .069
        x = math.sin(k * 1.8) * .025
        reach = .13 + .08 * math.sin(k / 10 * math.pi)
        m.tube([(x, y, -.12), (x + .02, y + .05, -.26), (x - .025, y + .11, -.20 - reach)],
               [.030, .022, .002], 1, rings=26, sides=14)
    for k in range(17):
        a = k * 2.399
        center = (-.28 + .09 * math.cos(a), 1.48 + .13 * math.sin(a), -.12 - .04 * (k % 3))
        barnacle(m, center, .025 + .008 * (k % 3))
    # Curved keratin nails continue the long digits; skin folds are sculpted into the skin.
    for s in (-1, 1):
        for finger in range(4):
            length = [.18, .24, .26, .21][finger]
            x = s * .59 + (finger - 1.5) * .072
            m.tube([(x, .48 - length, .35), (x, .465 - length, .38), (x - s * .01, .47 - length, .415)],
                   [.012, .009, .0006], 1, 'Finger' + ('L' if s < 0 else 'R') + str(finger) + '_2', rings=26, sides=12, ribbing=.02)


def fin(m, origin, side, width, length, bone):
    x, y, z = origin
    rows, columns = 24, 40
    start = len(m.vertices)
    def point(u, t, layer=0):
        angle = -.60 + 2.4 * t
        edge = .79 + .21 * math.cos(math.tau * 5 * t)
        r = u * edge
        return (x + side * width * r * math.cos(angle),
                y + length * r * math.sin(angle),
                z - .14 * r + .045 * math.sin(math.pi * u) + .014 * math.sin(t * 17) * u + layer)
    names = [bone + ('_FinL' if side < 0 else '_FinR') + str(i) for i in range(3)]
    for i, name in enumerate(names):
        m.bones[name] = (point(i / 3, .5), point((i + 1) / 3, .5), bone if i == 0 else names[i - 1])
    m.chains.append({'name': names[0], 'bones': names, 'role': 'fin', 'points': [point(i / 3, .5) for i in range(4)]})
    def weights(u):
        q = max(0, min(2, u * 3 - .5))
        index = min(1, int(q)); f = q - index
        return {names[index]: 1 - f, names[index + 1]: f}
    # Two offset surfaces with joined borders: actual membrane thickness, no coplanar duplicates.
    layer_size = (rows + 1) * (columns + 1)
    for layer in (-.001, .001):
        for j in range(rows + 1):
            for i in range(columns + 1):
                m.vertex(point(j / rows, i / columns, layer), (j / rows, i / columns), weights(j / rows))
    for layer in range(2):
        for j in range(rows):
            for i in range(columns):
                a = start + layer * layer_size + j * (columns + 1) + i
                face = (a, a + 1, a + columns + 2, a + columns + 1)
                m.face(face if layer == 0 else face[::-1], 7)
    border = [j * (columns + 1) for j in range(rows + 1)]
    border += [rows * (columns + 1) + i for i in range(1, columns + 1)]
    border += [j * (columns + 1) + columns for j in range(rows - 1, -1, -1)]
    border += list(range(columns - 1, 0, -1))
    for a, b in zip(border, border[1:] + border[:1]):
        m.face((start + a, start + b, start + b + layer_size, start + a + layer_size), 7)
    for k in range(6):
        pts = [point(u, k / 5, -.002) for u in (0, .25, .5, .75, 1)]
        m.tube(pts, [.008, .006, .003, .0006], 0, chain=names, rings=32, sides=10, ribbing=0)


def barnacle(m, center, radius):
    x, y, z = center
    # Fluted broken cups with visible cavities, not solid balls.
    points = [(x + radius * math.cos(a), y + radius * math.sin(a), z)
              for a in [i * math.tau / 20 for i in range(21)]]
    m.tube(points, [radius * .26, radius * .19], 2, rings=36, sides=8)
    m.surface((x, y, z + .003), (radius * .71, radius * .71, .008), 4, segments=20, rings=10, folds=.02)
