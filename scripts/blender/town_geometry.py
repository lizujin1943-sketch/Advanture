"""沉钟镇的低多边形构件。坐标为 Blender 的 X / Y 地面、Z 高度，单位米。

一个 MeshBatch 对应一栋房屋或一个街区。直接积累网格，避免把每块砖导出成
独立 GameObject；编辑源文件时仍可按对象和材质选择屋顶、墙面及细节。
"""
import math

import bpy
from mathutils import Vector


class MeshBatch:
    def __init__(self, name, collection, materials):
        self.name = name
        self.collection = collection
        self.materials = materials
        self.vertices = []
        self.faces = []
        self.colors = []

    def geometry(self, vertices, faces, material):
        offset = len(self.vertices)
        self.vertices.extend(tuple(v) for v in vertices)
        self.faces.extend(tuple(offset + i for i in face) for face in faces)
        self.colors.extend([material] * len(faces))

    def quad(self, vertices, material):
        self.geometry(vertices, [(0, 1, 2, 3)], material)

    def box(self, center, size, material, angle=0, bevel=0):
        x, y, z = center
        w, d, h = (s / 2 for s in size)
        c, s = math.cos(angle), math.sin(angle)
        if bevel:
            b = min(bevel, w * .45, d * .45)
            outline = [(-w + b, -d), (w - b, -d), (w, -d + b), (w, d - b),
                       (w - b, d), (-w + b, d), (-w, d - b), (-w, -d + b)]
        else:
            outline = [(-w, -d), (w, -d), (w, d), (-w, d)]
        count = len(outline)
        vertices = [(x + u * c - v * s, y + u * s + v * c, z + level)
                    for level in (-h, h) for u, v in outline]
        faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
        faces += [(i, (i + 1) % count, (i + 1) % count + count, i + count) for i in range(count)]
        self.geometry(vertices, faces, material)

    def beam(self, start, end, width, material, depth=None):
        start, end = Vector(start), Vector(end)
        delta = end - start
        if delta.length < .00001:
            return
        rotation = delta.to_track_quat('Z', 'Y').to_matrix()
        midpoint = (start + end) / 2
        w, d, h = width / 2, (depth or width) / 2, delta.length / 2
        vertices = [midpoint + rotation @ Vector(v) for v in
                    [(-w, -d, -h), (w, -d, -h), (w, d, -h), (-w, d, -h),
                     (-w, -d, h), (w, -d, h), (w, d, h), (-w, d, h)]]
        self.geometry(vertices, [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4),
                                 (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], material)

    def cylinder(self, start, end, radius, material, sides=10, end_radius=None):
        start, end = Vector(start), Vector(end)
        delta = end - start
        if delta.length < .00001:
            return
        rotation = delta.to_track_quat('Z', 'Y').to_matrix()
        r2 = radius if end_radius is None else end_radius
        vertices = [point + rotation @ Vector((r * math.cos(i * math.tau / sides),
                                              r * math.sin(i * math.tau / sides), 0))
                    for point, r in [(start, radius), (end, r2)] for i in range(sides)]
        faces = [tuple(reversed(range(sides))), tuple(range(sides, sides * 2))]
        faces += [(i, (i + 1) % sides, (i + 1) % sides + sides, i + sides) for i in range(sides)]
        self.geometry(vertices, faces, material)

    def ring(self, center, radius, thickness, material, normal=(0, 0, 1), segments=16,
             start=0, end=math.tau):
        center = Vector(center)
        rotation = Vector(normal).to_track_quat('Z', 'Y').to_matrix()
        for i in range(segments):
            a, b = start + (end - start) * i / segments, start + (end - start) * (i + 1) / segments
            points = [center + rotation @ Vector((r * math.cos(t), r * math.sin(t), 0))
                      for r, t in [(radius - thickness, a), (radius, a), (radius, b), (radius - thickness, b)]]
            self.quad(points, material)

    def roof(self, x, y, width, depth, base, rise, material, trim):
        """六点山墙屋顶；分行瓦片让同色的大坡面也有尺度感。"""
        w, d = width / 2, depth / 2
        vertices = [(x - w, y - d, base), (x + w, y - d, base), (x, y - d, base + rise),
                    (x - w, y + d, base), (x + w, y + d, base), (x, y + d, base + rise)]
        self.geometry(vertices, [(2, 1, 0), (3, 4, 5), (0, 3, 5, 2), (2, 5, 4, 1), (1, 4, 3, 0)], material)
        for side in (-1, 1):
            for row in range(6):
                a, b = row / 6, (row + 1) / 6
                for column in range(max(3, round(depth / 1.25))):
                    count = max(3, round(depth / 1.25))
                    y0, y1 = y - d + column * depth / count + .025, y - d + (column + 1) * depth / count - .025
                    self.quad([(x + side * w * a, y0, base + rise * (1 - a) + .035),
                               (x + side * w * b, y0, base + rise * (1 - b) + .035),
                               (x + side * w * b, y1, base + rise * (1 - b) + .035),
                               (x + side * w * a, y1, base + rise * (1 - a) + .035)],
                              material + ('Light' if (row + column) % 4 == 0 else ''))
            for yy in (y - d, y + d):
                self.beam((x + side * w, yy, base), (x, yy, base + rise), .16, trim)
            self.beam((x + side * w, y - d, base), (x + side * w, y + d, base), .18, trim)
        self.cylinder((x, y - d - .1, base + rise + .07), (x, y + d + .1, base + rise + .07), .14, trim, 8)

    def finish(self):
        if not self.faces:
            return None
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        obj = bpy.data.objects.new(self.name, mesh)
        self.collection.objects.link(obj)
        used = list(dict.fromkeys(self.colors))
        for key in used:
            mesh.materials.append(self.materials[key])
        slots = {key: i for i, key in enumerate(used)}
        for polygon, key in zip(mesh.polygons, self.colors):
            polygon.material_index = slots[key]
        return obj


def make_material(name, rgb, emission=0):
    mat = bpy.data.materials.new('Town_' + name)
    mat.diffuse_color = (*rgb, 1)
    mat.use_nodes = True
    node = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    node.inputs['Base Color'].default_value = (*rgb, 1)
    node.inputs['Roughness'].default_value = .82
    if emission:
        socket = node.inputs.get('Emission Color') or node.inputs.get('Emission')
        socket.default_value = (*rgb, 1)
        strength = node.inputs.get('Emission Strength')
        if strength:
            strength.default_value = emission
    return mat


def collection(name, parent=None):
    result = bpy.data.collections.new(name)
    (parent or bpy.context.scene.collection).children.link(result)
    return result


def camera(name, at, target, scale, scene):
    data = bpy.data.cameras.new(name)
    data.type = 'ORTHO'
    data.ortho_scale = scale
    data.clip_end = 1200
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = at
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    return obj
