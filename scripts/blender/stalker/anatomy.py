"""The approved low quadrupedal silhouette; no humanoid trunk or attached human hands."""
import math
import random
from mathutils import Vector
from eldritch.geometry import catmull
from stalker.geometry import StalkerMesh, shell, bundle, cup, frame


def torso(m):
    m.bones['Spine'] = ((0, .83, -.80), (0, 1.18, -.05), 'Root')
    m.bones['Head'] = ((0, 1.15, .25), (0, .94, 1.12), 'Spine')
    m.surface((0, 1.02, -.22), (.33, .30, .66), 0, 'Spine', 60, 40, .018)
    m.surface((0, .86, -.77), (.27, .25, .32), 0, 'Spine', 48, 32, .018)
    m.tube([(0, 1.1, -.02), (0, 1.06, .42), (0, .96, .91)], [.29, .26, .18], 0, 'Head', rings=72, sides=32, ribbing=.025)
    m.surface((0, 1.015, .85), (.235, .155, .23), 0, 'Head', 52, 32, .018)
    # Interlocking rib-like connective bands curve under the whole thorax.
    for s in (-1, 1):
        for j in range(11):
            z = -.73 + j * .095
            y = 1.05 + .10 * math.sin(j / 10 * math.pi)
            pts = [(s * .14, y + .14, z), (s * .30, y, z + .035),
                   (s * .28, y - .18, z + .13), (s * .13, y - .25, z + .20)]
            m.tube(pts, [.005, .020, .017, .003], 0, rings=36, sides=12, ribbing=.035)
            if 5 <= j <= 8:
                m.tube([(p[0] * 1.015, p[1] - .008, p[2] + .028) for p in pts[1:]],
                       [.004, .006, .001], 5, rings=24, sides=8, ribbing=0)
    # Large plates define the silhouette. Narrower abdomen plates overlap towards the tail.
    shell(m, 'Central thorax vault', [(0, 1.10, -.87), (0, 1.42, -.49), (0, 1.46, -.08)],
          [.23, .38, .35], [.10, .15, .075], 'Spine', seed=11, density=1.3)
    shell(m, 'Scapular collar', [(0, 1.29, -.40), (0, 1.51, -.09), (0, 1.36, .27)],
          [.32, .47, .32], [.06, .12, .065], 'Spine', seed=24, density=1.3)
    for j in range(3):
        z = -.91 + j * .19
        y = .99 + j * .13
        shell(m, 'Abdominal plate ' + str(j), [(0, y - .06, z - .20), (0, y + .08, z), (0, y + .04, z + .20)],
              [.19 + j * .025, .28 + j * .03, .26 + j * .03], [.04, .08, .04], 'Spine', seed=39 + j)
    for s in (-1, 1):
        for j in range(4):
            z = -.69 + j * .21
            y = 1.02 + .12 * math.sin(j / 3 * math.pi)
            shell(m, f'Flank overlapping shell {s} {j}',
                  [(s * .22, y + .20, z - .16), (s * .37, y + .05, z), (s * .35, y - .13, z + .18)],
                  [.15, .19, .10], [.035, .055, .018], 'Spine', (s, .5, 0), 60 + j * 3 + s)


def head(m):
    top = shell(m, 'Eyeless wedge head shield', [(0, 1.38, .12), (0, 1.40, .49), (0, 1.12, 1.20), (0, 1.04, 1.34)],
                [.34, .38, .23, .018], [.09, .14, .055, .008], 'Head', seed=100, density=1.6)
    rng = random.Random(845)
    for i in range(15):
        t = rng.uniform(.10, .62); u = rng.choice((-1, 1)) * rng.uniform(.60, .91)
        p, n = top(t, u)
        cup(m, p + n * .001, n, rng.uniform(.008, .022), 'Head', i)
    # Head has lateral cheek armor, not a human face with brows and eyes.
    for s in (-1, 1):
        shell(m, 'Cheek and jaw hinge ' + str(s),
              [(s * .19, 1.19, .42), (s * .30, 1.10, .72), (s * .205, .97, 1.10)],
              [.10, .15, .045], [.04, .07, .014], 'Head', (s * .7, .4, .2), 110 + s)
        m.tube([(s * .18, 1.10, .43), (s * .25, 1.00, .70), (s * .17, .89, 1.05)],
               [.12, .10, .045], 0, 'Head', rings=50, sides=24, ribbing=.08)
        shell(m, 'Underslung mandible ' + str(s),
              [(s * .15, .99, .83), (s * .22, .87, 1.01), (s * .10, .86, 1.23)],
              [.07, .08, .006], [.065, .07, .003], 'Head', (s, .2, .1), 120 + s)
        m.tube([(s * .14, 1.00, .78), (s * .20, .90, .99), (s * .12, .87, 1.18)],
               [.072, .05, .006], 0, 'Head', rings=40, sides=18, ribbing=.05)
    # A recessed, closed oral bowl under the nose, with teeth anchored in its perimeter.
    center = Vector((0, .91, 1.105)); rows = []; count = 64
    across = Vector((1, 0, 0)); vertical = Vector((0, .86, .51)); inward = Vector((0, .51, -.86))
    for j in range(17):
        r = 1 - j / 17
        row = []
        for i in range(count):
            a = i * math.tau / count
            p = center + across * .145 * math.cos(a) * r + vertical * .070 * math.sin(a) * r + inward * .20 * (1 - r * r)
            row.append(m.vertex(p, (i / count, r), {'Head': 1}))
        rows.append(row)
    for j, (aa, bb) in enumerate(zip(rows, rows[1:])):
        for i in range(count):
            k = (i + 1) % count
            m.face((aa[i], aa[k], bb[k], bb[i]), 0 if j < 2 else 4)
    m.face(tuple(rows[-1]), 4)
    for i in range(19):
        a = (i + .27) * math.tau / 19
        root = center + across * .137 * math.cos(a) + vertical * .065 * math.sin(a)
        tip = center + across * .10 * math.cos(a) + vertical * (.025 + .015 * rng.random()) * math.sin(a) - inward * .012
        m.tube([root, root.lerp(tip, .5) - inward * .01, tip], [.007, .004, .0006],
               6, 'Head', rings=16, sides=10, ribbing=.01)
    for s in (-1, 1):
        m.chain('Feeler' + ('L' if s < 0 else 'R'), [(s * .09, .87, 1.13), (s * .13, .73, 1.24),
                (s * .15, .57, 1.31), (s * .13, .47, 1.37), (s * .10, .41, 1.34)],
                [.011, .009, .006, .003, .0007], 'feeler', parent='Head', sides=12)


def limbs(m):
    for front in (True, False):
        for s in (-1, 1):
            label = ('Front' if front else 'Hind') + ('L' if s < 0 else 'R')
            if front:
                points = [(s * .33, 1.16, .05), (s * .65, .70, .02), (s * .74, .21, .72), (s * .76, .08, 1.00)]
                radii = [.20, .15, .08, .05]
            else:
                points = [(s * .25, .91, -.74), (s * .48, .62, -.99), (s * .42, .20, -.73), (s * .43, .07, -.42)]
                radii = [.17, .12, .060, .04]
            bones = m.chain(label, points, radii, 'leg', parent='Spine', sides=28)
            bundle(m, points, radii, bones, 230 + (10 if front else 0) + s, count=13)
            # Joint sockets are enclosed under these overlapping caps; no exposed cut ends.
            root, elbow, wrist, toe = map(Vector, points)
            m.surface(root, (radii[0] * 1.08, radii[0] * 1.05, radii[0] * 1.10), 0, bones[0], 36, 24, .01)
            n = Vector((s * .7, .65, .25))
            shell(m, label + ' shoulder armor', [root + Vector((-s * .06, .08, -.10)), root + Vector((s * .07, .07, 0)), root.lerp(elbow, .69)],
                  [.14, .26 if front else .21, .12], [.13, .23 if front else .18, .11], bones[0], n, 201 + s + 10 * front, density=1.1)
            shell(m, label + ' lower armor', [elbow.lerp(wrist, .06), elbow.lerp(wrist, .47), elbow.lerp(wrist, .91)],
                  [.15, .16 if front else .13, .075], [.15, .17 if front else .14, .085], bones[1], (s * .15, .7, .75), 220 + s + 10 * front)
            shell(m, label + ' wrist shield', [wrist + Vector((0, .025, -.015)), wrist.lerp(toe, .55) + Vector((0, .045, 0)), toe],
                  [.055, .095, .035], [.018, .035, .01], bones[2], (0, 1, .1), 250 + s + 10 * front)
            for j in range(3):
                offset = (j - 1) * .078
                p = toe + Vector((offset, .015, -.075))
                claw = [p, p + Vector((offset * .3, .045, .10)), p + Vector((offset * .5, .01, .19)), p + Vector((offset * .6, -.06, .215))]
                m.tube(claw, [.031, .027, .014, .0008], 3, bones[-1], rings=36, sides=16, ribbing=.018)
                m.tube([wrist + Vector((offset * .5, .02, .01)), p], [.017, .027], 0, bones[-1], rings=22, sides=12, ribbing=.05)


def tail(m):
    controls = [(0, .79, -.95), (.03, .68, -1.20), (.18, .59, -1.54), (.46, .63, -1.95),
                (.61, .88, -2.29), (.54, 1.15, -2.51), (.30, 1.36, -2.58)]
    points = [catmull(controls, i / 18) for i in range(19)]
    radii = [.13 * (1 - i / 18) ** .80 + .005 for i in range(19)]
    bones = m.chain('Tail', points, radii, 'tail', parent='Spine', sides=24)
    for j in range(25):
        t = j / 26
        span = .065
        pts = [catmull(controls, min(.999, t + q * span)) for q in (0, .5, 1)]
        r = .13 * (1 - t) ** .80 + .006
        bone = bones[min(17, int((t + .03) * 18))]
        shell(m, 'Tail dorsal scute ' + str(j), pts, [r * .75, r * 1.16, r * .78], [r * .82, r * 1.12, r * .60],
              bone, (0, .7, .35), seed=350 + j, density=.48)
        if j < 10:
            for s in (-1, 1):
                shell(m, f'Tail lateral {j} {s}', pts, [r * .5, r * .72, r * .40], [r * .7, r, r * .65],
                      bone, (s, 0, 0), seed=400 + j + s, density=.40)


def build(materials):
    m = StalkerMesh(materials)
    torso(m); limbs(m); tail(m); head(m)
    return m
