"""Create the approved carapace-and-sinew creature in a fresh background Blender."""
from pathlib import Path
import json
import sys
import bpy
from mathutils import Vector

BASE = Path(__file__).resolve().parent
sys.path.insert(0, str(BASE))
from stalker import materials, anatomy, motion

ROOT = BASE.parents[1]
SOURCE = ROOT / 'source-assets/creatures/abyssal-stalker'
OUT = ROOT / 'artifacts/abyssal-stalker'


def aim(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()


def studio():
    scene = bpy.context.scene; scene.name = 'Abyssal Stalker — approved creature study'
    bpy.ops.mesh.primitive_plane_add(size=200)
    floor = bpy.context.object; floor.name = 'Studio floor — not an export mesh'
    floor.data.materials.append(materials.make('Studio', (.070, .062, .053), .85)[0])
    bpy.ops.object.camera_add(location=(4.6, -6.6, 3.0))
    camera = bpy.context.object; camera.name = 'Creature review camera'; scene.camera = camera
    camera.data.type = 'ORTHO'; camera.data.ortho_scale = 4.7
    aim(camera, (0, .45, .82))
    for name, pos, energy, color, size in [
        ('Large soft key', (-3.2, -4.5, 5.2), 850, (1, .91, .81), 4),
        ('Front fill', (4.0, -3.2, 2.9), 350, (.76, .85, 1), 3),
        ('Shell rim', (1.2, 3.8, 4.5), 950, (.64, .80, 1), 3)]:
        bpy.ops.object.light_add(type='AREA', location=pos)
        light = bpy.context.object; light.name = name
        light.data.energy, light.data.color, light.data.size = energy, color, size
        aim(light, (0, .3, .85))
    world = bpy.data.worlds.new('Warm neutral studio'); world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs['Color'].default_value = (.17, .17, .17, 1); bg.inputs['Strength'].default_value = .30
    scene.world = world
    scene.render.resolution_x, scene.render.resolution_y = 1500, 1000
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.fps = 30; scene.frame_start = 1; scene.frame_end = 91


def main():
    SOURCE.mkdir(parents=True, exist_ok=True); OUT.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    collection = bpy.data.collections.new('Abyssal Stalker'); bpy.context.scene.collection.children.link(collection)
    model = anatomy.build(materials.build())
    print('ANATOMY', len(model.vertices), 'vertices', len(model.plates), 'curved plates', flush=True)
    rig, skin = model.finish(collection)
    for modifier in list(skin.modifiers): skin.modifiers.remove(modifier)
    rest = skin.data.attributes.new('RestPosition', 'FLOAT_VECTOR', 'POINT')
    for vertex, value in zip(skin.data.vertices, rest.data): value.vector = vertex.co
    edge = skin.data.attributes.new('ShellEdge', 'FLOAT', 'POINT')
    edge.data.foreach_set('value', model.shell_edge)
    print('SKIN_BOUND', flush=True)
    actions = motion.bake(rig, model)
    modifier = skin.modifiers.new('Creature deformation', 'ARMATURE'); modifier.object = rig
    modifier.use_deform_preserve_volume = True
    print('MOTION_BAKED', len(actions), flush=True)
    studio(); bpy.context.scene.frame_set(1)
    reference = bpy.data.images.load(str(SOURCE / 'approved-concept.png'), check_existing=True); reference.pack()
    rig['reference'] = 'approved-concept.png — user approved: 接近，就按这张做模型'
    rig['asset_scope'] = 'Blender creature source and animations; Unity installation pending'
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'AbyssalStalker.blend'), compress=True)
    skin.data.calc_loop_triangles()
    manifest = {'name': '潮壳猎手', 'source': 'AbyssalStalker.blend', 'reference': 'approved-concept.png',
                'vertices': len(skin.data.vertices), 'triangles': len(skin.data.loop_triangles), 'bones': len(rig.data.bones),
                'plates': len(model.plates), 'clips': [{'name': n, 'frames': f} for n, f in motion.CLIPS],
                'status': 'source built; render review pending; not installed into Unity'}
    (SOURCE / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2))
    (OUT / 'build.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2))
    bpy.context.scene.render.filepath = str(OUT / 'hero.png')
    bpy.ops.render.render(write_still=True)
    print('STALKER_BUILT', json.dumps(manifest), flush=True)


if __name__ == '__main__': main()
