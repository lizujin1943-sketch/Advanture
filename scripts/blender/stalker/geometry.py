"""Closed curved shell plates and load-bearing tendon bundles, in game coordinates."""
import math
import random
from mathutils import Vector, noise
from eldritch.geometry import CreatureMesh, catmull


def lerp(values, t):
    q = min(len(values) - 1.000001, max(0, t) * (len(values) - 1))
    i = int(q)
    return values[i] * (1 - q + i) + values[i + 1] * (q - i)


class StalkerMesh(CreatureMesh):
    def __init__(self, materials):
        super().__init__('AbyssalStalker', materials)
        self.shell_edge = []
        self.plates = []

    def vertex(self, point, uv, weights):
        index = super().vertex(point, uv, weights)
        self.shell_edge.append(0)
        return index


def frame(points, t, guide):
    c = catmull(points, t)
    tangent = (catmull(points, min(1, t + .001)) - catmull(points, max(0, t - .001))).normalized()
    n = Vector(guide); n -= tangent * n.dot(tangent)
    if n.length < .01:
        n = Vector((1, 0, 0)); n -= tangent * n.dot(tangent)
    n.normalize()
    return c, tangent.cross(n).normalized(), n


def shell(m, label, points, widths, heights, bone, guide=(0, 1, 0), seed=0, density=1):
    """A curved thickness-bearing plate. Worn boundaries use an explicit material mask."""
    rows, cols = int(42 * density), int(32 * density)
    start = len(m.vertices); size = (rows + 1) * (cols + 1)
    def position(t, u, inside=False):
        shaped = max(0, min(1, t - t ** 6 * .17 * abs(u) ** 1.5 + (1 - t) ** 6 * .08 * abs(u)))
        c, side, normal = frame(points, shaped, guide)
        outline = 1 + .042 * math.sin(shaped * 21 + seed) + .025 * math.sin(shaped * 57 + seed * 3)
        width = lerp(widths, shaped) * outline
        dome = lerp(heights, shaped) * max(0, 1 - u * u) ** .58
        keel = .034 * math.exp(-(u / .21) ** 2) * math.sin(math.pi * t)
        growth = .024 * math.exp(-((t - .30) / .13) ** 2) * math.exp(-(u / .30) ** 2)
        folds = (.005 * math.sin(t * 83 + abs(u) * 7 + seed) + .002 * math.sin(t * 171 + u * 21)) * abs(u) ** .5
        h = dome + keel + growth + folds - (.016 if inside else 0)
        p = c + side * u * width + normal * h
        if not inside: p += normal * noise.noise(p * 47) * .002
        return p, normal
    for layer in range(2):
        for j in range(rows + 1):
            t = j / rows
            for i in range(cols + 1):
                u = i / cols * 2 - 1
                p, _ = position(t, u, layer == 1)
                index = m.vertex(p, (i / cols, t), {bone: 1})
                margin = max(abs(u), 1 - t * 12, (t - .90) * 10)
                irregular = .06 * noise.noise(p * 130)
                m.shell_edge[index] = max(0, min(.75, (margin + irregular - .90) * 7))
        for j in range(rows):
            for i in range(cols):
                a = start + layer * size + j * (cols + 1) + i
                face = (a, a + 1, a + cols + 2, a + cols + 1)
                m.face(face if layer == 0 else face[::-1], 1)
    border = list(range(cols + 1))
    border += [j * (cols + 1) + cols for j in range(1, rows + 1)]
    border += [rows * (cols + 1) + i for i in range(cols - 1, -1, -1)]
    border += [j * (cols + 1) for j in range(rows - 1, 0, -1)]
    for a, b in zip(border, border[1:] + border[:1]):
        m.face((start + a, start + b, start + b + size, start + a + size), 2)
    m.plates.append({'name': label, 'bone': bone, 'first_vertex': start, 'vertex_count': len(m.vertices) - start})
    # Subtle branching ridges are shaped along the plate, not straight ribs or added spikes.
    if density >= .8:
        for s in (-1, 1):
            for branch in range(3):
                ts = [.18 + branch * .19 + q * .23 for q in (0, .25, .5, .75, 1)]
                pts = [position(min(.98, t), s * (.08 + q * .72))[0] + position(min(.98, t), s * (.08 + q * .72))[1] * .001
                       for t, q in zip(ts, (0, .25, .5, .75, 1))]
                m.tube(pts, [.002, .004, .003, .0008], 1, bone, rings=26, sides=8, ribbing=0)
    return position


def bundle(m, points, radii, bones, seed, count=11):
    rng = random.Random(seed)
    for i in range(count):
        angle = math.tau * i / count + rng.uniform(-.08, .08)
        pts = []
        for j in range(15):
            t = j / 14
            c, side, n = frame(points, t, (0, 0, 1))
            a = angle + .18 * math.sin(t * 4 + seed)
            radius = lerp(radii, t) * (.92 + .025 * math.sin(t * 33 + i))
            pts.append(c + radius * (side * math.cos(a) + n * math.sin(a)))
        thickness = rng.uniform(.008, .014)
        m.tube(pts, [.002, thickness, thickness * .70, .002], 0, chain=bones,
               rings=65, sides=10, ribbing=.025)


def cup(m, center, normal, radius, bone, seed):
    n = Vector(normal).normalized(); side = n.cross(Vector((0, 0, 1)))
    if side.length < .1: side = n.cross(Vector((1, 0, 0)))
    side.normalize(); up = n.cross(side); c = Vector(center)
    rows = []
    for r, h in ((.7, -.003), (1, .002), (.87, .012), (.55, .014), (.4, .003)):
        row = []
        for k in range(16):
            a = k * math.tau / 16
            f = 1 + .07 * math.sin(a * 5 + seed)
            p = c + radius * r * f * (side * math.cos(a) + up * math.sin(a)) + n * h
            row.append(m.vertex(p, (k / 16, r), {bone: 1}))
        rows.append(row)
    for a, b in zip(rows, rows[1:]):
        for k in range(16):
            j = (k + 1) % 16; m.face((a[k], a[j], b[j], b[k]), 7)
    m.face(tuple(rows[-1]), 4); m.face(tuple(reversed(rows[0])), 7)
