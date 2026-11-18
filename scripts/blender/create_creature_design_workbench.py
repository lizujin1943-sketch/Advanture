"""Design references and distinct material studies, NOT finished creature models.

Run in a fresh background Blender. Does not modify the live editor or Unity assets.
"""
from pathlib import Path
import json
import math
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'source-assets/creatures/eldritch-redesign'
OUT = ROOT / 'artifacts/eldritch-redesign'


def linear(hex_color):
    values = [int(hex_color[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in values) + (1,)


def material(spec, soft=False):
    mat = bpy.data.materials.new(spec['id'] + ('_ConnectiveStudy' if soft else '_SurfaceStudy'))
    mat.use_nodes = True
    mat.use_fake_user = True
    mat.asset_mark()
    mat['scope'] = 'Blender material study; no baked game textures yet'
    mat['pattern'] = spec['pattern']
    mat['design'] = spec['surface']
    mat.diffuse_color = linear(spec['soft'] if soft else spec['base'])
    n, link = mat.node_tree.nodes, mat.node_tree.links
    bs = next(node for node in n if node.type == 'BSDF_PRINCIPLED')
    bs.location = (600, 180)
    coord = n.new('ShaderNodeTexCoord'); coord.location = (-1100, 100)
    grain = n.new('ShaderNodeTexNoise'); grain.location = (-880, -220)
    grain.inputs['Scale'].default_value = 115
    grain.inputs['Detail'].default_value = 5
    link.new(coord.outputs['Generated'], grain.inputs['Vector'])
    broad = n.new('ShaderNodeTexNoise'); broad.location = (-880, 100)
    broad.inputs['Scale'].default_value = 5 if spec['pattern'] == 'algae' else 11
    broad.inputs['Detail'].default_value = 6
    broad.inputs['Roughness'].default_value = .72
    link.new(coord.outputs['Generated'], broad.inputs['Vector'])
    pattern = spec['pattern']
    vor = n.new('ShaderNodeTexVoronoi'); vor.location = (-880, 420)
    vor.feature = 'DISTANCE_TO_EDGE'
    vor.inputs['Scale'].default_value = 24 if pattern in ('veins', 'jelly', 'blight') else 45
    link.new(coord.outputs['Generated'], vor.inputs['Vector'])
    ramp = n.new('ShaderNodeValToRGB'); ramp.location = (-380, 160)
    color = linear(spec['soft'] if soft else spec['base'])
    accent = linear(spec['accent'])
    ramp.color_ramp.elements[0].position = .28
    ramp.color_ramp.elements[0].color = tuple(c * .46 for c in color[:3]) + (1,)
    ramp.color_ramp.elements[1].position = .76
    ramp.color_ramp.elements[1].color = accent if not soft else color
    middle = ramp.color_ramp.elements.new(.52); middle.color = color
    link.new(broad.outputs['Fac'], ramp.inputs[0])
    color_out = ramp.outputs['Color']
    height = grain.outputs['Fac']
    if pattern in ('veins', 'jelly', 'blight', 'rust') and not soft:
        mask = n.new('ShaderNodeMath'); mask.operation = 'LESS_THAN'; mask.location = (-600, 460)
        mask.inputs[1].default_value = .022 if pattern == 'rust' else .036
        link.new(vor.outputs['Distance'], mask.inputs[0])
        mix = n.new('ShaderNodeMixRGB'); mix.location = (0, 200)
        mix.inputs[2].default_value = linear(spec['soft'])
        link.new(mask.outputs[0], mix.inputs[0]); link.new(color_out, mix.inputs[1])
        color_out = mix.outputs[0]
        height = vor.outputs['Distance']
    elif pattern == 'algae' and not soft:
        patches = n.new('ShaderNodeValToRGB'); patches.location = (-600, 460)
        patches.color_ramp.elements[0].position = .50
        patches.color_ramp.elements[1].position = .60
        link.new(broad.outputs['Fac'], patches.inputs[0])
        mix = n.new('ShaderNodeMixRGB'); mix.location = (0, 200)
        mix.inputs[2].default_value = accent
        link.new(patches.outputs[0], mix.inputs[0]); link.new(color_out, mix.inputs[1])
        color_out = mix.outputs[0]
    elif pattern == 'star' and not soft:
        spots = n.new('ShaderNodeTexVoronoi'); spots.location = (-850, 680)
        spots.inputs['Scale'].default_value = 15
        link.new(coord.outputs['Generated'], spots.inputs['Vector'])
        mask = n.new('ShaderNodeMath'); mask.operation = 'LESS_THAN'; mask.inputs[1].default_value = .19
        mask.location = (-400, 600); link.new(spots.outputs['Distance'], mask.inputs[0])
        mix = n.new('ShaderNodeMixRGB'); mix.location = (0, 200)
        mix.inputs[2].default_value = accent
        link.new(mask.outputs[0], mix.inputs[0]); link.new(color_out, mix.inputs[1])
        color_out = mix.outputs[0]
    elif pattern in ('chitin', 'chalk', 'bone') and not soft:
        pits = n.new('ShaderNodeTexVoronoi'); pits.inputs['Scale'].default_value = 90 if pattern == 'chitin' else 38
        pits.location = (-600, -500); link.new(coord.outputs['Generated'], pits.inputs['Vector'])
        height = pits.outputs['Distance']
    elif pattern == 'iridescent' and not soft:
        facing = n.new('ShaderNodeLayerWeight'); facing.location = (-600, 600)
        film = n.new('ShaderNodeValToRGB'); film.location = (-360, 600)
        film.color_ramp.elements[0].color = linear('#142f2c')
        film.color_ramp.elements[1].color = linear('#1b1423')
        film.color_ramp.elements.new(.46).color = linear('#432747')
        link.new(facing.outputs['Facing'], film.inputs[0]); color_out = film.outputs[0]
        if 'Thin Film Thickness' in bs.inputs:
            bs.inputs['Thin Film Thickness'].default_value = 410
    bump = n.new('ShaderNodeBump'); bump.location = (360, -120)
    bump.inputs['Strength'].default_value = .2 if soft or pattern in ('veins', 'jelly', 'iridescent') else .65
    bump.inputs['Distance'].default_value = .007 if soft or pattern in ('veins', 'jelly', 'iridescent') else .028
    link.new(height, bump.inputs['Height']); link.new(bump.outputs[0], bs.inputs['Normal'])
    link.new(color_out, bs.inputs['Base Color'])
    bs.inputs['Roughness'].default_value = .35 if soft else spec['roughness']
    bs.inputs['Transmission Weight'].default_value = 0 if soft else spec['transmission']
    bs.inputs['Subsurface Weight'].default_value = .16 if soft else spec['subsurface']
    bs.inputs['Subsurface Scale'].default_value = .04
    bs.inputs['Coat Weight'].default_value = .08 if soft else (.18 if pattern in ('jelly', 'veins', 'iridescent') else .02)
    bs.inputs['Coat Roughness'].default_value = .18
    return mat


def aim(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()


def text_object(text, loc, size, material, font):
    curve = bpy.data.curves.new(text, 'FONT'); curve.body = text; curve.size = size
    curve.align_x = 'CENTER'; curve.font = font
    obj = bpy.data.objects.new(text, curve); bpy.context.scene.collection.objects.link(obj)
    obj.location = loc; obj.rotation_euler.x = math.pi / 2
    curve.materials.append(material)
    return obj


def main():
    SOURCE.mkdir(parents=True, exist_ok=True); OUT.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    scene = bpy.context.scene; scene.name = 'Material studies — NOT creature models'
    specs = json.loads((SOURCE / 'material-specs.json').read_text())
    refs = json.loads((SOURCE / 'designs.json').read_text())
    paths = {'DeepOne': 'DeepOne/concept-v1.png', **{s['id']: s['image'] for s in refs['species']}}
    font = bpy.data.fonts.load('/System/Library/Fonts/STHeiti Light.ttc')
    font.pack()
    label_mat = bpy.data.materials.new('Label'); label_mat.diffuse_color = (.8, .76, .66, 1)
    label_mat.use_nodes = True
    label_bs = next(n for n in label_mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    label_bs.inputs['Base Color'].default_value = (.8, .76, .66, 1)
    image_scene = bpy.data.scenes.new('Ten creature concepts — 2D references')
    for i, spec in enumerate(specs):
        x, z = (i % 5 - 2) * 2.4, (1 - i // 5) * 2.7
        main_mat, soft_mat = material(spec), material(spec, soft=True)
        for j, (radius, mat) in enumerate(((.70, main_mat), (.23, soft_mat))):
            bpy.ops.mesh.primitive_uv_sphere_add(segments=96, ring_count=64, radius=radius, location=(x + (0 if j == 0 else .60), -.08 if j == 0 else -.35, z + .85))
            obj = bpy.context.object; obj.name = spec['id'] + ('_SurfaceSample' if j == 0 else '_ConnectiveSample')
            obj['scope'] = 'Material sample sphere; NOT a creature model'
            obj.data.materials.append(mat)
            for poly in obj.data.polygons: poly.use_smooth = True
        text_object(spec['name'], (x, -.15, z - .12), .21, label_mat, font)
        text_object(spec['pattern'], (x, -.15, z - .42), .14, label_mat, font)
        ref = bpy.data.images.load(str(SOURCE / paths[spec['id']])); ref.pack()
        empty = bpy.data.objects.new(spec['id'] + '_2D_Concept', None)
        empty.empty_display_type = 'IMAGE'; empty.data = ref; empty.empty_display_size = 4.6
        empty.location = ((i % 2) * 5, 0, -(i // 2) * 3)
        empty.rotation_euler.x = math.pi / 2
        empty['scope'] = 'AI-generated 2D reference; NOT a 3D creature render'
        image_scene.collection.objects.link(empty)
        # Textured boards also display in rendered view, where image empties are hidden.
        mesh = bpy.data.meshes.new(spec['id'] + '_ReferenceBoard')
        width, height = 4.6, 4.6 * ref.size[1] / ref.size[0]
        mesh.from_pydata([(-width / 2, 0, -height / 2), (width / 2, 0, -height / 2),
                          (width / 2, 0, height / 2), (-width / 2, 0, height / 2)], [], [(0, 1, 2, 3)])
        uv = mesh.uv_layers.new(name='ReferenceUV')
        for loop, value in zip(uv.data, [(0, 0), (1, 0), (1, 1), (0, 1)]): loop.uv = value
        board = bpy.data.objects.new(spec['id'] + '_2D_ReferenceBoard', mesh)
        board.location = empty.location
        board['scope'] = 'Flat 2D concept reference board; NOT a reconstructed creature'
        image_scene.collection.objects.link(board)
        image_mat = bpy.data.materials.new(spec['id'] + '_ReferenceImage'); image_mat.use_nodes = True
        nodes, links = image_mat.node_tree.nodes, image_mat.node_tree.links; nodes.clear()
        tex = nodes.new('ShaderNodeTexImage'); tex.image = ref
        emit = nodes.new('ShaderNodeEmission'); output = nodes.new('ShaderNodeOutputMaterial')
        links.new(tex.outputs['Color'], emit.inputs['Color']); links.new(emit.outputs[0], output.inputs[0])
        mesh.materials.append(image_mat)
        empty.hide_viewport = True
    camera_data = bpy.data.cameras.new('Concept overview camera')
    reference_camera = bpy.data.objects.new('Concept overview camera', camera_data)
    image_scene.collection.objects.link(reference_camera)
    reference_camera.location = (2.5, -20, -5.65); aim(reference_camera, (2.5, 0, -5.65))
    # Portrait camera fits its vertical sensor: frame the complete five-row board.
    camera_data.type = 'ORTHO'; camera_data.ortho_scale = 16.5
    image_scene.camera = reference_camera
    image_scene.render.resolution_x, image_scene.render.resolution_y = 1600, 2500
    image_scene.render.resolution_percentage = 100
    image_scene.view_settings.view_transform = 'Standard'
    image_scene.world = bpy.data.worlds.new('Concept sheet background'); image_scene.world.color = (.03, .03, .03)
    title = text_object('十种怪物 · 二维设定参考', (2.5, -.01, 1.86), .29, label_mat, font)
    scene.collection.objects.unlink(title); image_scene.collection.objects.link(title)
    title_mat = bpy.data.materials.new('Concept heading'); title_mat.use_nodes = True
    nodes = title_mat.node_tree.nodes; nodes.clear()
    emit = nodes.new('ShaderNodeEmission'); emit.inputs['Color'].default_value = (.82, .78, .68, 1)
    output = nodes.new('ShaderNodeOutputMaterial'); title_mat.node_tree.links.new(emit.outputs[0], output.inputs[0])
    title.data.materials.clear(); title.data.materials.append(title_mat)
    text_object('材质试样 · 不是怪物模型', (0, 0, 5.4), .32, label_mat, font)
    text_object('程序化表面方向 / 游戏贴图尚未烘焙', (0, 0, -.95), .20, label_mat, font)
    bpy.ops.object.camera_add(location=(0, -18, 3.1)); camera = bpy.context.object
    aim(camera, (0, 0, 2.4)); camera.data.type = 'ORTHO'; camera.data.ortho_scale = 13
    scene.camera = camera
    for pos, power, size in [((-4, -5, 8), 1600, 7), ((5, -3, 4), 1000, 5), ((0, 3, 6), 1400, 6)]:
        bpy.ops.object.light_add(type='AREA', location=pos); light = bpy.context.object
        light.data.energy = power; light.data.shape = 'DISK'; light.data.size = size
        aim(light, (0, 0, 2))
    scene.world = bpy.data.worlds.new('Neutral review world'); scene.world.use_nodes = True
    bg = next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs['Color'].default_value = (.055, .045, .04, 1); bg.inputs['Strength'].default_value = .6
    scene.render.resolution_x, scene.render.resolution_y = 1800, 1000
    scene.render.resolution_percentage = 100
    scene['scope'] = 'Design and material workbench only; no reconstructed creatures, rigs, clips or game-ready textures'
    scene.render.filepath = str(OUT / 'material-studies.png')
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'CreatureDesignWorkbench.blend'), compress=True)
    bpy.ops.render.render(write_still=True)
    image_scene.render.filepath = str(OUT / 'ten-concepts-overview.png')
    bpy.ops.render.render(write_still=True, scene=image_scene.name)
    report = {'concept_images': len(specs), 'material_studies': len(specs) * 2,
              'finished_new_models': 0, 'game_textures_baked': False,
              'source': str(SOURCE / 'CreatureDesignWorkbench.blend')}
    (OUT / 'workbench-build.json').write_text(json.dumps(report, indent=2))
    print('DESIGN_WORKBENCH_COMPLETE', json.dumps(report), flush=True)


if __name__ == '__main__':
    main()
