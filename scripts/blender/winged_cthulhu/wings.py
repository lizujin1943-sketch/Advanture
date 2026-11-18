"""Articulated wing fingers and a web that shares their curved edges."""
import math
from bisect import bisect_left

from .geometry import Mesh, P, spline


def transform(x, y, side):
    if side == 'L':
        return x, y
    width = (.78 + .12 * max(0, min(1, (y-50)/120)) if y <= 170
             else .90 - .16 * min(1, (y-170)/305))
    return 1135 + (457-x)*width, y + .035*(x-450)


# The short leading digit, outer spar, two interior digits and the arm-side
# attachment. Each control encodes a visible knuckle or a narrowing shaft.
DIGITS = [
    [(455,65,15),(433,48,15),(421,44,17),(404,53,12),(374,61,10),(353,62,12),(333,72,9),
     (275,90,8),(215,112,8),(204,119,9),(177,145,6),(148,182,.8)],
    [(456,66,14),(426,73,13),(399,87,9),(348,119,7),(282,149,6),(232,165,7),
     (181,199,7),(171,206,8),(139,230,5.5),(127,243,8),(91,292,6),(47,367,5),(17,440,3),(7,476,.8)],
    [(456,68,15),(437,76,14),(419,89,11),(382,111,8.5),(334,147,8),(319,158,11),
     (310,169,8),(273,215,7.5),(248,246,8),(240,255,11.5),(233,271,7.5),(211,338,6.8),
     (193,406,5.8),(188,432,3.7),(191,451,.8)],
    [(458,70,15),(447,90,13),(441,105,9),(422,155,8),(405,211,7),(397,247,7),
     (396,258,10),(395,269,7),(394,315,7),(396,368,5.8),(402,406,.8)],
    [(459,69,17),(473,122,17),(493,174,14),(506,204,19),(520,213,17),(553,226,13),
     (586,269,10),(616,318,7),(638,361,3)]
]


def digit_knots(side,digit):
    controls=DIGITS[digit]
    knots = []
    for i,(x,y,r) in enumerate(controls):
        u=i/(len(controls)-1)
        # Fan opens aft in depth. All five paths meet at the same carpus.
        depth = 1.07 + (.38-.105*digit)*math.sin(math.pi*u) + (.28-.07*digit)*u
        knots.append((*transform(x,y,side),depth,r))
    return knots


def wing_surface(side, collection, materials):
    mesh = Mesh()
    paths = []
    steps = 60
    for digit in range(len(DIGITS)):
        knots=digit_knots(side,digit)
        if digit < 4:
            bone=Mesh()
            bone.tube(knots,28,5,ripple=.024,flatten=.87)
            bone.finish(f'CR_WingedCthulhu_WingFinger_{side}_{digit}',collection,[materials['ridge']])
        curve=spline([P(*k[:3]) for k in knots],6)
        distance=[0.]
        for a,b in zip(curve,curve[1:]):
            distance.append(distance[-1]+(b-a).length)
        def sample(u,curve=curve,distance=distance):
            at=u*distance[-1]
            j=max(1,min(len(curve)-1,bisect_left(distance,at)))
            return curve[j-1].lerp(curve[j],(at-distance[j-1])/(distance[j]-distance[j-1]))
        paths.append(sample)

    # One uninterrupted sheet. First/last columns of adjacent lobes share
    # vertices along the bones. Trimming radial length leaves scalloped margins.
    columns=30
    previous=None
    root=mesh.vert(P(*transform(457,68,side),1.12),(.5,0))
    for j in range(1,steps+1):
        t=j/steps
        row=[]
        for k in range(4*columns+1):
            lobe=min(k//columns,3)
            s=k/columns-lobe
            arch=max(0,math.sin(math.pi*s))
            cut=(.29,.255,.245,.23)[lobe]*arch**.83
            if lobe in (0,1,3):
                cut+=.038*math.exp(-((s-.68)/.045)**2)*arch
            u=t*(1-cut)
            p=paths[lobe](u).lerp(paths[lobe+1](u),s)
            # Front is -Y. Web nestles into the back of the knuckles, with
            # maximum billow between them and taut rib-facing edges.
            p.y+=.02+.29*arch**1.2*math.sin(.91*math.pi*t)
            p.y+=.037*math.sin(3*math.pi*s+.5*t)*arch*t
            row.append(mesh.vert(p,(k/(4*columns),t)))
        for k in range(len(row)-1):
            if previous is None:
                mesh.face((root,row[k],row[k+1]))
            else:
                mesh.face((previous[k],row[k],row[k+1],previous[k+1]))
        previous=row
    web=mesh.finish('CR_WingedCthulhu_WingMembrane_'+side,collection,[materials['membrane']])
    solid=web.modifiers.new('Web thickness','SOLIDIFY')
    solid.thickness=.032
    solid.offset=0
    claw=[(451,80,20),(459,60,23),(475,31,22),(487,24,18),(503,22,16),
          (528,24,12),(548,34,8),(559,45,4),(565,56,.65)]
    hook=Mesh()
    hook.tube([(*transform(x,y,side),.99,r) for x,y,r in claw],32,6,flatten=.9)
    hook.finish('CR_WingedCthulhu_WingClaw_'+side,collection,[materials['ridge']])


def build(collection, materials):
    for side in ('L','R'):
        wing_surface(side,collection,materials)
