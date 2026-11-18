"""Editable, skinned organic surfaces in game coordinates (X right, Y up, Z forward)."""
import math
import bpy
from mathutils import Vector


def vector(point):
    return Vector((point[0], -point[2], point[1]))


def catmull(points, t):
    q = min(t * (len(points) - 1), len(points) - 1.000001)
    index = int(q)
    u = q - index
    a, b, c, d = [Vector(points[min(max(i, 0), len(points) - 1)])
                   for i in (index - 1, index, index + 1, index + 2)]
    return .5 * ((2 * b) + (c - a) * u + (2 * a - 5 * b + 4 * c - d) * u * u
                 + (-a + 3 * b - 3 * c + d) * u * u * u)


class CreatureMesh:
    def __init__(self, name, materials):
        self.name = name
        self.materials = materials
        self.vertices, self.faces, self.uvs, self.weights, self.slots = [], [], [], [], []
        self.bones = {'Root': ((0, 0, 0), (0, .18, 0), None),
                      'Facing': ((0, 0, .3), (0, 0, .5), 'Root'),
                      'Spine': ((0, .38, 0), (0, .95, 0), 'Root'),
                      'Head': ((0, .95, 0), (0, 1.28, .1), 'Spine')}
        self.chains = []

    def vertex(self, point, uv, weights):
        self.vertices.append(vector(point))
        self.uvs.append(uv)
        self.weights.append(weights)
        return len(self.vertices) - 1

    def face(self, indices, material):
        self.faces.append(indices)
        self.slots.append(material)

    def surface(self, center, size, material=0, bone='Spine', segments=32, rings=20, folds=.035):
        """Continuous mottled skin surface with longitudinal creases and asymmetric lobes."""
        start = len(self.vertices)
        for j in range(rings + 1):
            lat = math.pi * j / rings
            for i in range(segments + 1):
                angle = math.tau * i / segments
                wave = 1 + folds * (math.sin(angle * 5 + lat * 3) + .4 * math.sin(angle * 11 - lat * 9))
                p = (center[0] + size[0] * math.sin(lat) * math.cos(angle) * wave,
                     center[1] + size[1] * math.cos(lat),
                     center[2] + size[2] * math.sin(lat) * math.sin(angle) * wave)
                self.vertex(p, (i / segments, j / rings), {bone: 1})
        for j in range(rings):
            for i in range(segments):
                a = start + j * (segments + 1) + i
                self.face((a, a + 1, a + segments + 2, a + segments + 1), material)

    def tube(self, points, radii, material=0, bone='Spine', chain=None, rings=30, sides=10, ribbing=.035):
        start = len(self.vertices)
        previous_u = None
        for j in range(rings + 1):
            t = j / rings
            center = catmull(points, t)
            tangent = (catmull(points, min(1, t + .001)) - catmull(points, max(0, t - .001))).normalized()
            # Parallel transport avoids a sudden cross-section twist when a limb
            # passes through vertical. Switching reference axes here pinches elbows.
            if previous_u is None:
                reference = Vector((0, 1, 0)) if abs(tangent.y) < .9 else Vector((1, 0, 0))
                u = tangent.cross(reference).normalized()
            else:
                u = (previous_u - tangent * previous_u.dot(tangent)).normalized()
            previous_u = u
            v = tangent.cross(u).normalized()
            r = t * (len(radii) - 1)
            k = min(int(r), len(radii) - 2)
            radius = radii[k] * (1 - (r - k)) + radii[k + 1] * (r - k)
            weights = {bone: 1}
            if chain:
                q = max(0, min(len(chain) - 1, t * len(chain) - .5))
                k = min(int(q), len(chain) - 1)
                f = q - k
                weights = {chain[k]: 1 - f}
                if f > 0:
                    weights[chain[k + 1]] = f
            for i in range(sides + 1):
                angle = math.tau * i / sides
                ripple = 1 + ribbing * math.sin(t * math.pi * 34)
                p = center + radius * ripple * (u * math.cos(angle) + v * math.sin(angle))
                self.vertex(p, (i / sides, t), weights)
        for j in range(rings):
            for i in range(sides):
                a = start + j * (sides + 1) + i
                self.face((a, a + 1, a + sides + 2, a + sides + 1), material)
        self.face(tuple(start + i for i in reversed(range(sides))), material)
        end = start + rings * (sides + 1)
        self.face(tuple(end + i for i in range(sides)), material)

    def chain(self, name, points, radii, role='tentacle', material=0, parent='Spine', sides=12):
        names = []
        for i in range(len(points) - 1):
            key = name + '_' + str(i)
            self.bones[key] = (points[i], points[i + 1], parent if i == 0 else names[-1])
            names.append(key)
        self.chains.append({'name': name, 'bones': names, 'role': role, 'points': points})
        self.tube(points, radii, material, chain=names, rings=max(24, len(points) * 7), sides=sides)
        return names

    def spine(self, start, tip, radius=.06, bone='Spine', material=2):
        a, b = Vector(start), Vector(tip)
        middle = a.lerp(b, .55) + Vector((0, .035, -.025))
        self.tube([a, middle, b], [radius, radius * .65, .001], material, bone, rings=10, sides=8)

    def membrane(self, points, bone='Spine', material=3):
        center = sum((Vector(p) for p in points), Vector()) / len(points)
        origin = self.vertex(center, (.5, .5), {bone: 1})
        indices = [self.vertex(p, (.5 + .5 * math.cos(i * math.tau / len(points)),
                                   .5 + .5 * math.sin(i * math.tau / len(points))), {bone: 1})
                   for i, p in enumerate(points)]
        for i, a in enumerate(indices):
            b = indices[(i + 1) % len(indices)]
            self.face((origin, a, b), material)
            self.face((origin, b, a), material)

    def eye(self, position, radius, bone='Head'):
        x, y, z = position
        tall = .92 if radius > .2 else .56
        self.surface((x, y, z), (radius * 1.28, radius * tall, radius * .42), 4, bone, 24, 12)
        self.surface((x, y, z + radius * .32), (radius * .62, radius * tall * .75, radius * .17), 5, bone, 28, 14, .01)
        self.surface((x, y, z + radius * .49), (radius * .13, radius * tall * .68, radius * .04), 4, bone, 16, 10, 0)
        for side in (-1, 1):
            points = [(x + math.cos(a) * radius * 1.13, y + math.sin(a) * radius * tall * side, z + radius * .20)
                      for a in [math.pi * i / 8 for i in range(9)]]
            self.tube(points, [radius * .25, radius * .14], 0, bone, rings=24, sides=8)

    def maw(self, center, size, bone='Head', teeth=14):
        x, y, z = center
        rx, ry = size
        self.surface(center, (rx, ry, .07), 4, bone, 28, 14, .04)
        rim = [(x + math.cos(a) * rx, y + math.sin(a) * ry, z + .035)
               for a in [math.tau * i / 32 for i in range(33)]]
        self.tube(rim, [.017, .023], 3, bone, rings=48, sides=8)
        for i in range(teeth):
            a = math.tau * (i + .3) / teeth
            root = (x + math.cos(a) * rx * .92, y + math.sin(a) * ry * .92, z + .07)
            reach = .26 + .11 * (i % 3)
            tip = (x + math.cos(a) * rx * .64, y + math.sin(a) * ry * reach, z + .08)
            self.spine(root, tip, .011 + .004 * (i % 3), bone)

    def finish(self, collection):
        armature = bpy.data.armatures.new(self.name + 'Skeleton')
        rig = bpy.data.objects.new(self.name + '_Rig', armature)
        collection.objects.link(rig)
        bpy.context.view_layer.objects.active = rig
        rig.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT')
        for name, (head, tail, parent) in self.bones.items():
            b = armature.edit_bones.new(name)
            b.head, b.tail = vector(head), vector(tail)
            if parent:
                b.parent = armature.edit_bones[parent]
        bpy.ops.object.mode_set(mode='OBJECT')
        mesh = bpy.data.meshes.new(self.name + 'Surface')
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        obj = bpy.data.objects.new(self.name + '_Skin', mesh)
        collection.objects.link(obj)
        obj.parent = rig
        for material in self.materials:
            mesh.materials.append(material)
        for polygon, slot in zip(mesh.polygons, self.slots):
            polygon.material_index = slot
            polygon.use_smooth = True
        uv = mesh.uv_layers.new(name='SurfaceUV')
        for loop in mesh.loops:
            uv.data[loop.index].uv = self.uvs[loop.vertex_index]
        groups = {name: obj.vertex_groups.new(name=name) for name in self.bones}
        for index, weights in enumerate(self.weights):
            for name, weight in weights.items():
                if weight > 0:
                    groups[name].add([index], weight, 'REPLACE')
        deform = obj.modifiers.new('Skin deformation', 'ARMATURE')
        deform.object = rig
        rig['species'] = self.name
        rig['forward_axis'] = '-Y in Blender, +Z in Unity'
        return rig, obj
