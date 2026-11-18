"""Ten distinct silhouettes, anatomical details and deformation chains."""
import math
from geometry import CreatureMesh

NAMES = ['DeepOne', 'ReefRam', 'ShellWarden', 'RiftEye', 'DrownedBell',
         'BlightSac', 'ChoirPriest', 'BroodMother', 'RiftStalker', 'StarHerald']
LABELS = ['鳃民拾骨者', '礁角奔袭兽', '圣螺守卫', '裂瞳观测者', '溺钟引路者',
          '腐潮囊', '无面唱诗者', '千嗣母巢', '折影行者', '星骸执钟者']
PALETTES = [(.12, .24, .20), (.20, .18, .13), (.16, .22, .22), (.21, .12, .20),
            (.10, .22, .24), (.24, .25, .12), (.18, .16, .20), (.22, .12, .14),
            (.10, .16, .20), (.20, .18, .24)]


def tendrils(m, count, height, radius, length, prefix='Tendril', forward=0, parent='Spine'):
    for i in range(count):
        a = i * math.tau / count + .2
        x, z = math.cos(a), math.sin(a)
        points = [(x * radius, height, z * radius + forward),
                  (x * (radius + .13), height - length * .3, z * (radius + .16) + forward),
                  (x * (radius + .18), height - length * .64, z * (radius + .10) + forward),
                  (x * (radius + .28), height - length * .9, z * radius + forward),
                  (x * (radius + .42), height - length, z * (radius - .12) + forward)]
        thickness = .035 if length < .4 else .075
        chain = m.chain(prefix + str(i), points, [thickness, thickness * .85, thickness * .43, .004], parent=parent)
        # Underside suckers, grouped on the corresponding deform segment.
        for k in range(1, 4):
            p = points[k]
            m.surface((p[0], p[1], p[2] + .025), (.034, .042, .023), 3,
                      chain[min(k, len(chain) - 1)], segments=10, rings=6, folds=0)


def biped(m, tall=False):
    hip = .65 if tall else .56
    shoulder = 1.35 if tall else 1.08
    for side in (-1, 1):
        suffix = 'L' if side == -1 else 'R'
        legs = [(side * .17, hip, -.04), (side * .23, .29, .10),
                (side * .22, .09, -.07), (side * .26, .045, .27)]
        radii = [.075, .065, .042, .03] if tall else [.12, .115, .07, .055]
        chain = m.chain('Leg' + suffix, legs, radii, 'leg')
        for toe in range(3):
            x = side * .26 + (toe - 1) * .065
            m.spine((x, .055, .19), (x + side * .025, .025, .39), .03, chain[-1])
        arms = [(side * .29, shoulder, 0), (side * (.57 if tall else .48), .77, -.10),
                (side * (.68 if tall else .52), .41, .24), (side * .57, .32, .39)]
        radii = [.08, .065, .04, .02] if tall else [.13, .115, .08, .045]
        chain = m.chain('Arm' + suffix, arms, radii, 'arm')
        if tall:
            m.tube([arms[-2], (side * .66, .14, .49), (side * .48, .06, .72)],
                   [.065, .045, .001], 2, chain[-1], rings=24, sides=10)
            continue
        fingers = []
        for i in range(4):
            x = arms[-1][0] + side * ((i - 1.5) * .045)
            points = [(x, .35, .30), (x + side * .015, .26 - i * .012, .43),
                      (x + side * .035, .19 - i * .015, .50)]
            m.tube(points, [.03, .025, .005], 0, chain[-1], rings=14, sides=8)
            m.spine(points[-2], points[-1], .017, chain[-1])
            fingers.append(points[1])
        m.membrane([arms[-1], *fingers], chain[-1])


def build(index, materials):
    m = CreatureMesh(NAMES[index], materials)
    if index == 0:
        m.surface((0, .83, -.08), (.32, .43, .25), folds=.045)
        m.surface((0, 1.06, -.10), (.38, .30, .26), folds=.04)
        m.bones['Head'] = ((0, 1.10, .07), (0, 1.44, .22), 'Spine')
        m.surface((0, 1.31, .15), (.24, .32, .27), bone='Head', folds=.065)
        m.surface((0, 1.14, .30), (.25, .14, .18), 3, 'Head')
        m.maw((0, 1.18, .46), (.22, .095), teeth=18)
        for s in (-1, 1):
            m.eye((s * .18, 1.39, .35), .060)
            m.tube([(s * .04, 1.48, .30), (s * .16, 1.44, .36), (s * .27, 1.43, .20)],
                   [.035, .055, .015], 1, 'Head', rings=22, sides=9)
            for i in range(4):
                m.tube([(s * .25, 1.35 - i * .055, .10), (s * .31, 1.32 - i * .055, .0),
                        (s * .27, 1.28 - i * .055, -.08)], [.016, .008], 3, 'Head', rings=12, sides=6)
            m.membrane([(s * .23, 1.30, -.05), (s * .44, 1.48, -.14),
                        (s * .39, 1.15, -.18), (s * .25, 1.13, -.05)], 'Head')
        biped(m)
        for i in range(6):
            m.spine((0, .80 + i * .10, -.28), (0, .87 + i * .10, -.47 + i * .01), .06)
        tendrils(m, 4, 1.12, .10, .30, 'Barbel', .38, 'Head')
    elif index == 1:
        m.bones['Spine'] = ((0, .48, -.30), (0, .60, .25), 'Root')
        m.bones['Head'] = ((0, .60, .20), (0, .58, .64), 'Spine')
        m.surface((0, .56, -.15), (.40, .32, .63))
        m.surface((0, .65, .37), (.32, .30, .34), 1, 'Head', folds=.075)
        m.maw((0, .48, .68), (.21, .11), teeth=12)
        for s in (-1, 1):
            m.eye((s * .23, .71, .58), .065)
            m.tube([(s * .22, .76, .48), (s * .30, 1.05, .54), (s * .25, .98, .99)], [.12, .07, .002], 2, 'Head', rings=28)
            for k in range(2):
                z = -.53 + .68 * k
                chain = m.chain('Leg' + str(s) + str(k), [(s * .30, .55, z), (s * .50, .28, z - .1),
                               (s * .46, .09, z + .02), (s * .47, .04, z + .23)], [.15, .12, .07, .04], 'leg')
                for toe in (-1, 1):
                    m.spine((s * .47 + toe * .045, .06, z + .12), (s * .47 + toe * .055, .025, z + .30), .033, chain[-1])
        for k in range(5):
            z = -.59 + k * .18
            m.surface((0, .78, z), (.34 - k * .015, .13, .14), 1, segments=24, rings=12, folds=.04)
            m.spine((0, .84, z), (.035, 1.10, z - .16), .08)
        m.chain('Tail', [(0, .55, -.65), (0, .45, -.89), (.16, .40, -1.1), (.23, .50, -1.20)], [.10, .06, .008])
    elif index == 2:
        m.surface((0, .69, -.12), (.40, .51, .33))
        m.surface((0, 1.04, .07), (.54, .60, .29), 1, segments=40, rings=26, folds=.045)
        spiral = []
        for i in range(90):
            a = i * .15
            r = .49 * (1 - i / 105)
            spiral.append((math.cos(a) * r, 1.04 + math.sin(a) * r, .28 + i * .0016))
        m.tube(spiral, [.047, .018], 2, rings=130, sides=9)
        for i in range(16):
            a = i * math.tau / 16
            m.spine((.47 * math.cos(a), 1.04 + .51 * math.sin(a), .10),
                    (.61 * math.cos(a), 1.04 + .67 * math.sin(a), .14), .045)
        for s in (-1, 1):
            m.chain('Leg' + str(s), [(s * .23, .46, 0), (s * .38, .23, -.08), (s * .38, .07, .20)], [.16, .12, .055], 'leg')
            chain = m.chain('Arm' + str(s), [(s * .38, 1.00, -.07), (s * .63, .66, .03),
                                          (s * .60, .43, .44)], [.18, .15, .10], 'arm')
            m.surface((s * .61, .44, .47), (.19, .19, .26), 1, chain[-1], 26, 16, .065)
            m.spine((s * .62, .54, .60), (s * .50, .49, .88), .08, chain[-1])
            m.chain('Eyestalk' + str(s), [(s * .16, 1.39, -.02), (s * .23, 1.63, .12),
                                        (s * .22, 1.67, .25)], [.055, .025], parent='Head')
            m.eye((s * .22, 1.66, .28), .075, 'Eyestalk' + str(s) + '_1')
    elif index == 3:
        m.bones['Head'] = ((0, .70, 0), (0, 1.05, .05), 'Spine')
        m.surface((0, .99, -.02), (.46, .43, .33), 1, 'Head', 40, 24, .035)
        m.eye((0, 1.02, .28), .31)
        for s in (-1, 1):
            for k in range(3):
                m.spine((s * (.31 - k * .04), 1.22 + k * .07, -.02),
                        (s * (.51 - k * .10), 1.45 + k * .09, -.15), .07, 'Head')
        tendrils(m, 7, .77, .21, .68)
        for i in range(5):
            m.surface((.27 * math.cos(i * 1.5), .94 + .19 * math.sin(i * 1.5), -.28), (.065, .065, .05), 5, 'Head', 16, 10)
    elif index == 4:
        m.bones['Head'] = ((0, .9, 0), (0, 1.3, 0), 'Spine')
        m.surface((0, 1.10, 0), (.52, .53, .43), 0, 'Head', 40, 26, .09)
        # Raised bell ribs, a ragged lip and an exposed luminous clapper.
        for i in range(10):
            a = i * math.tau / 10
            x, z = math.cos(a), math.sin(a)
            m.tube([(x * .09, 1.59, z * .08), (x * .32, 1.45, z * .27),
                    (x * .50, 1.04, z * .42), (x * .55, .77, z * .47)], [.025, .04, .027], 2, 'Head', rings=32, sides=7)
        m.surface((0, .83, 0), (.47, .12, .39), 3, 'Head', folds=.1)
        m.surface((0, .69, 0), (.14, .22, .14), 5, 'Spine', folds=.08)
        tendrils(m, 10, .82, .32, .78)
        m.eye((0, 1.20, .40), .11)
    elif index == 5:
        m.surface((0, .72, 0), (.48, .59, .39), folds=.11)
        m.bones['Head'] = ((0, .95, .1), (0, 1.19, .2), 'Spine')
        m.maw((0, 1.09, .30), (.19, .17), teeth=9)
        for i in range(13):
            a = i * 2.399
            y = .33 + i % 4 * .23
            x, z = math.cos(a) * .39, math.sin(a) * .33
            m.surface((x, y, z), (.11, .13, .09), 3 if i % 3 else 5, segments=20, rings=12, folds=.085)
            m.tube([(x * .8, y + .12, z * .8), (x * 1.1, y, z * 1.04), (x * .85, y - .15, z * 1.04)], [.018, .01], 4, rings=15, sides=6)
        for s in (-1, 1):
            m.eye((s * .17, 1.02, .31), .04)
            for k in range(2):
                z = -.22 + k * .40
                m.chain('Leg' + str(s) + str(k), [(s * .33, .42, z), (s * .53, .19, z),
                                               (s * .61, .04, z + .16)], [.09, .07, .012], 'leg')
    elif index == 6:
        m.surface((0, .94, -.04), (.23, .54, .22), folds=.07)
        m.bones['Head'] = ((0, 1.27, -.01), (0, 1.66, .01), 'Spine')
        m.surface((0, 1.55, 0), (.29, .36, .25), 1, 'Head', folds=.035)
        m.surface((0, 1.55, .225), (.145, .25, .06), 4, 'Head', 28, 16, .03)
        for s in (-1, 1):
            m.tube([(s * .11, 1.79, .17), (s * .18, 1.57, .23), (s * .10, 1.30, .23)],
                   [.04, .045, .022], 0, 'Head', rings=28, sides=9)
            for k in range(4):
                m.spine((s * .095, 1.38 + k * .07, .28), (s * .035, 1.32 + k * .07, .29), .013, 'Head')
        for i in range(9):
            a = i * math.tau / 9
            x, z = math.cos(a), math.sin(a)
            m.chain('Robe' + str(i), [(x * .15, 1.20, z * .16), (x * .24, .77, z * .23),
                                    (x * .34, .35, z * .31), (x * .42, .10, z * .39)], [.105, .12, .09, .008], 'robe', sides=12)
        for s in (-1, 1):
            chain = m.chain('Arm' + str(s), [(s * .23, 1.25, 0), (s * .50, .99, .0),
                                          (s * .59, 1.19, .24)], [.085, .065, .04], 'arm')
            for k in range(4):
                m.tube([(s * .59 + k * .025, 1.19, .24), (s * .65 + k * .025, 1.36, .29),
                        (s * .62 + k * .025, 1.43, .38)], [.022, .012, .001], 2, chain[-1], rings=16, sides=6)
        for k in range(5):
            a = k * math.pi / 4
            m.spine((math.cos(a) * .23, 1.73, 0), (math.cos(a) * .30, 2.03 - abs(math.cos(a)) * .12, -.10), .045, 'Head')
        m.surface((0, 1.06, .23), (.06, .10, .03), 5, segments=16, rings=10)
    elif index == 7:
        m.bones['Spine'] = ((0, .46, -.22), (0, .76, -.04), 'Root')
        m.bones['Head'] = ((0, .63, .25), (0, .73, .48), 'Spine')
        m.surface((0, .67, -.22), (.48, .39, .50), 1, folds=.07)
        m.surface((0, .43, -.18), (.40, .27, .41), 3, folds=.10)
        m.surface((0, .70, .35), (.30, .25, .26), bone='Head')
        m.maw((0, .61, .57), (.19, .12), teeth=16)
        for s in (-1, 1):
            for k in range(3):
                z = -.48 + k * .38
                chain = m.chain('Leg' + str(s) + str(k), [(s * .32, .69, z), (s * .66, .92, z - .14),
                                                       (s * .87, .45, z + .03), (s * .94, .035, z + .15)], [.10, .075, .042, .005], 'spider')
                m.spine((s * .67, .88, z - .12), (s * .83, 1.06, z - .29), .05, chain[1])
            for k in range(3):
                m.eye((s * (.10 + k * .075), .79 + k * .025, .53 - k * .05), .05 - k * .007)
        for i in range(14):
            a = i * 2.399
            m.surface((math.cos(a) * .37, .64 + .25 * math.sin(a), -.36 + (i % 3) * .17),
                      (.075, .09, .065), 3 if i % 4 else 5, segments=16, rings=10, folds=.045)
    elif index == 8:
        m.surface((0, .95, -.04), (.19, .51, .18), folds=.075)
        m.surface((0, 1.24, -.07), (.29, .24, .18), 1, folds=.07)
        m.bones['Head'] = ((0, 1.34, .01), (0, 1.65, .10), 'Spine')
        m.surface((0, 1.57, .07), (.19, .30, .20), 1, 'Head', folds=.055)
        m.maw((0, 1.50, .24), (.085, .16), teeth=16)
        for s in (-1, 1):
            m.eye((s * .12, 1.66, .21), .065)
            m.spine((s * .11, 1.76, 0), (s * .27, 2.04, -.18), .055, 'Head')
            for k in range(5):
                m.tube([(s * .04, 1.22 - k * .09, .14), (s * .18, 1.20 - k * .09, .16),
                        (s * .22, 1.12 - k * .09, .03)], [.025, .012], 2, rings=14, sides=7)
        biped(m, tall=True)
        tendrils(m, 3, 1.36, .17, .80, 'BackTendril', -.23)
    else:
        m.bones['Head'] = ((0, .89, 0), (0, 1.36, 0), 'Spine')
        m.surface((0, 1.12, 0), (.36, .43, .24), 1, 'Head', folds=.055)
        m.eye((0, 1.17, .22), .20)
        for i in range(5):
            a = math.pi * .5 + i * math.tau / 5
            x, y = math.cos(a), math.sin(a)
            m.chain('StarRay' + str(i), [(x * .26, 1.12 + y * .29, -.01),
                                       (x * .54, 1.12 + y * .56, -.05),
                                       (x * .73, 1.12 + y * .78, -.18),
                                       (x * .69, 1.12 + y * .94, -.26)], [.13, .11, .06, .002], 'ray', 2, 'Head')
        tendrils(m, 7, .82, .20, .72)
        for i in range(7):
            a = i * math.tau / 7
            m.surface((math.cos(a) * .27, 1.13 + math.sin(a) * .33, .19), (.045, .055, .033), 5, 'Head', 14, 8)
    return m
