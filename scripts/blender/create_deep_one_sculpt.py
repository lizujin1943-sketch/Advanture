"""Continuous anatomical sculpt study, kept separate from the shipped bestiary."""
from pathlib import Path
import json
import math
import sys
import bpy
from mathutils import Vector, kdtree, noise

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent / 'eldritch'))
from geometry import vector
import sculpt_deep_one
import sculpt_materials
import sculpt_animation

SOURCE = ROOT / 'source-assets/creatures/eldritch/sculpt-study'
OUT = ROOT / 'artifacts/eldritch-sculpt-study'
SOURCE.mkdir(parents=True, exist_ok=True)
OUT.mkdir(parents=True, exist_ok=True)


def apply(obj, modifier):
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def mesh_part(model, rig, collection, start, end, label):
    data = bpy.data.meshes.new(label)
    faces = [(tuple(v - start for v in face), slot) for face, slot in zip(model.faces, model.slots)
             if all(start <= v < end for v in face)]
    data.from_pydata(model.vertices[start:end], [], [f for f, _ in faces]); data.update()
    obj = bpy.data.objects.new(label, data); collection.objects.link(obj); obj.parent = rig
    for mat in model.materials: data.materials.append(mat)
    for polygon, (_, slot) in zip(data.polygons, faces):
        polygon.material_index = slot; polygon.use_smooth = True
    return obj


def carve(obj, center, size, name, material=0):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=40, ring_count=28, location=vector(center))
    cutter = bpy.context.object; cutter.name = name
    for mat in obj.data.materials: cutter.data.materials.append(mat)
    for face in cutter.data.polygons: face.material_index = material
    cutter.scale = (size[0], size[2], size[1])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    modifier = obj.modifiers.new(name, 'BOOLEAN'); modifier.operation = 'DIFFERENCE'
    modifier.solver = 'EXACT'; modifier.object = cutter
    apply(obj, modifier); bpy.data.objects.remove(cutter, do_unlink=True)


def skin_attributes(obj):
    rest = obj.data.attributes.new('RestPosition', 'FLOAT_VECTOR', 'POINT')
    for vertex, attr in zip(obj.data.vertices, rest.data): attr.vector = vertex.co
    colors = obj.data.color_attributes.new('Tissue', 'FLOAT_COLOR', 'POINT')
    for vertex, attr in zip(obj.data.vertices, colors.data):
        x, z, y = vertex.co.x, -vertex.co.y, vertex.co.z
        belly = max(0, min(1, (z - .08) / .10)) * max(0, 1 - abs(y - 1.10) / .55)
        mouth = math.exp(-((x / .13) ** 2 + ((y - 1.65) / .18) ** 2) * 2) * max(0, min(1, (z - .12) / .08))
        dorsal = max(0, min(1, (-z + .01) / .20))
        base = Vector((.70, .76, .66)).lerp(Vector((1, .72, .52)), belly)
        base = base.lerp(Vector((.19, .29, .28)), dorsal * .80)
        base = base.lerp(Vector((.30, .15, .11)), mouth)
        attr.color = (*base, 1)


def weight_surface(obj, model, start=0, direct=False, reference_end=None):
    groups = {name: obj.vertex_groups.new(name=name) for name in model.bones}
    tree = None
    if not direct:
        # Secondary fins and barbels must never capture nearby body-skin weights.
        end = reference_end if reference_end is not None else len(model.vertices)
        tree = kdtree.KDTree(end)
        for index in range(end): tree.insert(model.vertices[index], index)
        tree.balance()
    for vertex in obj.data.vertices:
        if direct:
            weights = model.weights[start + vertex.index]
        else:
            weights = {}
            for point, index, distance in tree.find_n(vertex.co, 6):
                strength = 1 / max(.00001, distance * distance)
                for name, value in model.weights[index].items():
                    weights[name] = weights.get(name, 0) + value * strength
            weights = dict(sorted(weights.items(), key=lambda p: p[1], reverse=True)[:2])
            total = sum(weights.values()); weights = {name: value / total for name, value in weights.items()}
        for name, value in weights.items():
            if value > .00001: groups[name].add([vertex.index], value, 'REPLACE')


def studio():
    scene = bpy.context.scene
    def aim(obj, point): obj.rotation_euler = (Vector(point) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.ops.mesh.primitive_plane_add(size=200)
    floor = bpy.context.object; floor.name = 'Neutral studio floor'
    floor.data.materials.append(sculpt_materials.material('Studio', (.025, .030, .031), .77)[0])
    bpy.ops.object.camera_add(location=(3.4, -5.8, 2.8))
    scene.camera = bpy.context.object; scene.camera.name = 'Sculpt review camera'
    scene.camera.data.type = 'ORTHO'; scene.camera.data.ortho_scale = 2.65
    aim(scene.camera, (0, 0, 1.08))
    for name, location, energy, color, size in [
        ('Neutral key', (-3, -4, 5), 800, (.87, .94, 1), 3.5),
        ('Soft face fill', (3, -3, 2.5), 450, (1, .83, .67), 3),
        ('Back silhouette', (1.0, 3, 3.5), 1000, (.48, .75, .82), 2.5)]:
        bpy.ops.object.light_add(type='AREA', location=location)
        light = bpy.context.object; light.name = name
        light.data.energy, light.data.color, light.data.size = energy, color, size
        aim(light, (0, 0, 1.0))
    world = bpy.data.worlds.new('Neutral review world'); world.use_nodes = True
    node = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
    node.inputs['Color'].default_value = (.12, .15, .17, 1)
    node.inputs['Strength'].default_value = .30; scene.world = world
    scene.render.resolution_x, scene.render.resolution_y = 1000, 1200
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.fps = 30; scene.frame_start, scene.frame_end = 1, 91


def main():
    # This script is run in a fresh background Blender, not over a user's working scene.
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    collection = bpy.data.collections.new('Deep One — continuous anatomical study')
    bpy.context.scene.collection.children.link(collection)
    mats = sculpt_materials.create()
    model = sculpt_deep_one.anatomy(mats); split = len(model.vertices)
    sculpt_deep_one.details(model)
    rig, proxy = model.finish(collection)
    proxy.name = 'Weight transfer reference'; proxy.hide_render = True
    body = mesh_part(model, rig, collection, 0, split, 'Continuous living skin')
    remesh = body.modifiers.new('Fused anatomical volumes', 'REMESH')
    remesh.mode = 'VOXEL'; remesh.voxel_size = .0055; remesh.use_smooth_shade = True
    apply(body, remesh)
    print('Fused anatomy', len(body.data.vertices), flush=True)
    smooth = body.modifiers.new('Sculpted transitions', 'SMOOTH'); smooth.factor = .82; smooth.iterations = 16
    apply(body, smooth)
    carve(body, (0, 1.655, .365), (.105, .15, .20), 'Recessed mouth cavity', 4)
    for side in (-1, 1):
        carve(body, (side * .10, 1.863, .268), (.029, .018, .033), 'Sunken orbital cavity', 3)
    subdiv = body.modifiers.new('Sculpt surface resolution', 'SUBSURF'); subdiv.levels = 1
    apply(body, subdiv)
    # Spatially correlated surface displacement creates dermal folds, with subdued facial amplitude.
    body.data.update()
    normals = [v.normal.copy() for v in body.data.vertices]
    for vertex, normal in zip(body.data.vertices, normals):
        p = vertex.co.copy()
        amplitude = .0010 if p.z > 1.5 else .0018
        value = noise.noise_vector(p * 34).x * .65 + noise.noise_vector(p * 87).y * .35
        x, y, z = p.x, p.z, -p.y
        wrinkle = 0
        # Fine radial compression around the mouth, broad neck folds, and joint skin creases.
        if y > 1.48 and z > .11:
            angle = math.atan2((y - 1.655) / .15, x / .10)
            radius = math.sqrt((x / .10) ** 2 + ((y - 1.655) / .15) ** 2)
            mask = math.exp(-((radius - 1.25) / .6) ** 2)
            wrinkle += .0025 * mask * math.sin(angle * 27 + radius * 4)
            wrinkle += .0014 * math.sin(y * 220 + x * 12) * math.exp(-((y - 1.51) / .12) ** 2)
        elbow = math.exp(-(((abs(x) - .48) / .09) ** 2 + ((y - 1.04) / .12) ** 2))
        knee = math.exp(-(((abs(x) - .245) / .09) ** 2 + ((y - .48) / .10) ** 2))
        wrinkle += .0012 * (elbow + knee * .5) * math.sin(y * 137 + z * 28 + noise.noise_vector(p * 15).z * 3)
        # Tendon direction determines thin forearm striations rather than arbitrary lumps.
        forearm = math.exp(-(((abs(x) - .54) / .09) ** 2 + ((y - .81) / .20) ** 2))
        wrinkle += .0018 * forearm * math.sin(x * 245 + y * 44 + z * 15)
        vertex.co += normal * (value * amplitude + wrinkle)
    body.data.update()
    detail = mesh_part(model, rig, collection, split, len(model.vertices), 'Gills fins dentition and parasites')
    weight_surface(body, model, reference_end=split)
    weight_surface(detail, model, split, True)
    print('Continuous skin weighted', flush=True)
    for obj in (body, detail):
        skin_attributes(obj)
        for polygon in obj.data.polygons: polygon.use_smooth = True
    # Bake while dense skins have no armature modifiers to avoid repeated heavy evaluation.
    bpy.data.objects.remove(proxy, do_unlink=True)
    sculpt_animation.bake(rig, model)
    print('Eight sculpt study actions baked', flush=True)
    for obj in (body, detail):
        arm = obj.modifiers.new('Anatomical deformation', 'ARMATURE'); arm.object = rig
    studio(); bpy.context.scene.frame_set(1)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'DeepOneSculpt.blend'))
    stats = {}
    for obj in (body, detail):
        obj.data.calc_loop_triangles()
        stats[obj.name] = {'vertices': len(obj.data.vertices), 'triangles': len(obj.data.loop_triangles)}
    (OUT / 'build.json').write_text(json.dumps({'status': 'built', 'meshes': stats, 'bones': len(rig.data.bones),
                                             'clips': len(bpy.data.actions), 'scope': 'one-species art study, not installed into gameplay'}, indent=2))
    bpy.context.scene.render.filepath = str(OUT / 'three-quarter.png')
    bpy.ops.render.render(write_still=True)
    print('SCULPT_STUDY_BUILT', json.dumps(stats))


if __name__ == '__main__': main()
