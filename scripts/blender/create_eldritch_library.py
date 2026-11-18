"""Create ten rigged deep-sea enemies and their eight animation takes in Blender.

Run with --background --factory-startup --python, or call start() via the Blender bridge.
Outputs live under source-assets/creatures/eldritch; nothing changes gameplay state.
"""
from pathlib import Path
import json
import math
import sys
import traceback
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent / 'eldritch'))
import geometry
import species
import materials
import animation

SOURCE = ROOT / 'source-assets/creatures/eldritch'
EXPORT = SOURCE / 'exports'
TEXTURES = EXPORT / 'textures'
EVIDENCE = ROOT / 'artifacts/eldritch-rework'
for directory in (SOURCE, EXPORT, TEXTURES, EVIDENCE):
    directory.mkdir(parents=True, exist_ok=True)


def state(status, **values):
    (EVIDENCE / 'build-status.json').write_text(json.dumps({'status': status, **values}, indent=2))


def setup():
    # Only the scene created for this library is replaced on a rebuild.
    scene = bpy.data.scenes.get('Eldritch Library')
    if scene:
        for obj in list(scene.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        # Rebuilding must preserve exact FBX take names, not append .001 suffixes.
        for datablocks in (bpy.data.actions, bpy.data.armatures, bpy.data.meshes,
                           bpy.data.materials, bpy.data.collections, bpy.data.images):
            for block in list(datablocks):
                generated = any(block.name.startswith(name) for name in species.NAMES)
                generated |= block.name.startswith(('AbyssSkin_', 'Preview charcoal'))
                if generated and (block.users == 0 or isinstance(block, (bpy.types.Action, bpy.types.Collection))):
                    datablocks.remove(block)
    else:
        scene = bpy.data.scenes.new('Eldritch Library')
    bpy.context.window.scene = scene
    scene.render.fps = 30
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1
    scene.frame_start, scene.frame_end = 1, 91
    bpy.context.preferences.filepaths.save_version = 0
    return materials.textures(TEXTURES)


def make_species(index, maps):
    name = species.NAMES[index]
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    mats = materials.create(name, species.PALETTES[index], maps, index)
    model = species.build(index, mats)
    rig, mesh = model.finish(collection)
    actions = animation.bake(rig, model, index)
    rig.animation_data.action = None
    for action in actions:
        track = rig.animation_data.nla_tracks.new()
        track.name = action.name
        track.strips.new(action.name, 1, action)
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = rig
    for prop, wanted in [('axis_forward', '-Z'), ('axis_up', 'Y')]:
        allowed = [v.identifier for v in bpy.ops.export_scene.fbx.get_rna_type().properties[prop].enum_items]
        if wanted not in allowed:
            raise ValueError(prop + ': ' + str(allowed))
    bpy.ops.export_scene.fbx(filepath=str(EXPORT / (name + '.fbx')), use_selection=True,
        object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, axis_forward='-Z', axis_up='Y',
        bake_anim=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
        bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True,
        bake_anim_step=1, bake_anim_simplify_factor=0, path_mode='AUTO')
    for track in rig.animation_data.nla_tracks:
        track.mute = True
    rig.animation_data.action = actions[0]
    # The editable source presents the entire roster at human-readable spacing.
    rig.location = geometry.vector(((index % 5 - 2) * 2.6, 0, -(index // 5) * 3.5))
    bpy.context.scene.frame_set(1)
    mesh.data.calc_loop_triangles()
    triangles = len(mesh.data.loop_triangles)
    if triangles > 36000:
        raise ValueError(name + ' exceeds 36k triangle budget: ' + str(triangles))
    return {'kind': index, 'name': name, 'label': species.LABELS[index], 'triangles': triangles,
            'vertices': len(mesh.data.vertices), 'bones': len(rig.data.bones),
            'color': list(species.PALETTES[index]),
            'clips': [{'name': n, 'frames': f, 'seconds': f / 30} for n, f in animation.CLIPS]}


def studio():
    scene = bpy.context.scene
    def aim(obj, point):
        obj.rotation_euler = (Vector(point) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.ops.mesh.primitive_plane_add(size=200)
    floor = bpy.context.object
    floor.name = 'Preview floor (source only)'
    mat = bpy.data.materials.new('Preview charcoal')
    mat.use_nodes = True
    shader = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    shader.inputs['Base Color'].default_value = (.018, .024, .027, 1)
    shader.inputs['Roughness'].default_value = .65
    floor.data.materials.append(mat)
    bpy.ops.object.camera_add(location=(8, -15, 10))
    camera = bpy.context.object
    camera.name = 'Roster camera'
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 15
    aim(camera, (0, 1.5, .7))
    scene.camera = camera
    for name, location, power, color, size in [
        ('Cold key', (-4, -6, 8), 2000, (.65, .85, 1), 8),
        ('Sea rim', (2, 7, 6), 2300, (.23, .65, .58), 7),
        ('Face fill', (5, -3, 4), 1300, (1, .65, .42), 6)]:
        bpy.ops.object.light_add(type='AREA', location=location)
        light = bpy.context.object
        light.name = name
        light.data.energy, light.data.color, light.data.shape, light.data.size = power, color, 'DISK', size
        aim(light, (0, 1, .7))
    world = bpy.data.worlds.new('Deep sea studio')
    world.use_nodes = True
    background = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
    background.inputs['Color'].default_value = (.08, .11, .14, 1)
    background.inputs['Strength'].default_value = .35
    scene.world = world
    scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 1800, 1000, 100
    for area in bpy.context.screen.areas:
        if area.type == 'VIEW_3D':
            area.spaces.active.region_3d.view_perspective = 'CAMERA'
            area.spaces.active.shading.type = 'MATERIAL'


def finish(entries):
    studio()
    manifest = {'schema': 1, 'revision': 'deep-sea-2026-11-11', 'fps': 30,
                'species': entries, 'animationAuthority': 'visual only, in-place, phase sampled by confirmed combat state'}
    (SOURCE / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    bpy.context.scene.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'EldritchLibrary.blend'))
    state('complete', species=len(entries), clips=len(entries) * len(animation.CLIPS))


def start():
    maps = setup()
    entries = []
    def tick():
        try:
            if len(entries) == len(species.NAMES):
                finish(entries)
                return None
            index = len(entries)
            state('building', index=index, name=species.NAMES[index])
            entries.append(make_species(index, maps))
            return .1
        except Exception:
            state('failed', traceback=traceback.format_exc())
            print(traceback.format_exc())
            return None
    bpy.app.timers.register(tick, first_interval=.1)


if __name__ == '__main__':
    maps = setup()
    finish([make_species(index, maps) for index in range(len(species.NAMES))])
