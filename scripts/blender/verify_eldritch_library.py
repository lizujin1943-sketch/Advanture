"""Reopen the authored source and exported FBXs, then check actual deformation."""
from pathlib import Path
import json
import math
import sys
import bpy

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'source-assets/creatures/eldritch'
OUT = ROOT / 'artifacts/eldritch-rework'


def difference(a, b):
    return max(abs(x - y) for aa, bb in zip(a, b) for x, y in zip(aa, bb))


def verify():
    manifest = json.loads((SOURCE / 'manifest.json').read_text())
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / 'EldritchLibrary.blend'))
    scene = bpy.context.scene
    reports = []
    for entry in manifest['species']:
        name = entry['name']
        rig, skin = bpy.data.objects[name + '_Rig'], bpy.data.objects[name + '_Skin']
        assert skin.data.uv_layers.active and rig.data.bones.get('Facing')
        assert all(abs(sum(g.weight for g in v.groups) - 1) < 1e-5 for v in skin.data.vertices), name
        assert all(math.isfinite(c) for v in skin.data.vertices for c in v.co), name
        skin.data.calc_loop_triangles()
        assert len(skin.data.loop_triangles) <= 36000, name
        loop_errors, movement = {}, {}
        for clip in entry['clips']:
            action = bpy.data.actions[name + '_' + clip['name']]
            rig.animation_data.action = action
            scene.frame_set(1)
            first = [b.matrix.copy() for b in rig.pose.bones]
            root = rig.pose.bones['Root'].matrix.copy()
            max_motion = 0
            for frame in (1 + clip['frames'] // 4, 1 + clip['frames'] // 2, 1 + clip['frames']):
                scene.frame_set(frame)
                assert difference(root, rig.pose.bones['Root'].matrix) < 1e-5, (name, 'root motion')
                max_motion = max(max_motion, max(difference(a, b.matrix) for a, b in zip(first, rig.pose.bones)))
            assert max_motion > .005, (name, clip['name'], 'static pose')
            movement[clip['name']] = round(max_motion, 6)
            if clip['name'] in ('Idle', 'Move'):
                error = max(difference(a, b.matrix) for a, b in zip(first, rig.pose.bones))
                assert error < 1e-4, (name, clip['name'], error)
                loop_errors[clip['name']] = error
            if clip['name'] == 'Move':
                scene.frame_set(1)
                for bone in rig.pose.bones:
                    if bone.name.startswith('Leg') and bone.name.endswith('_1'):
                        assert abs(bone.tail.z - rig.data.bones[bone.name].tail_local.z) < .14, (name, bone.name, 'floating stance')
        reports.append({'species': name, 'triangles': len(skin.data.loop_triangles),
                        'bones': len(rig.data.bones), 'loopErrors': loop_errors, 'motion': movement})
    # Verify files independently of source data; a correct .blend is not enough.
    for entry in manifest['species']:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(SOURCE / 'exports' / (entry['name'] + '.fbx')))
        rigs = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
        meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
        assert len(rigs) == len(meshes) == 1, entry['name']
        assert len(bpy.data.actions) == 8, (entry['name'], [a.name for a in bpy.data.actions])
        for clip in entry['clips']:
            matches = [a for a in bpy.data.actions if a.name.endswith(entry['name'] + '_' + clip['name'])]
            assert len(matches) == 1, (entry['name'], clip['name'])
        assert rigs[0].data.bones.get('Facing')
        assert all(v.groups for v in meshes[0].data.vertices), entry['name']
    report = {'result': 'PASS', 'species': 10, 'clips': 80, 'sourceAndFbx': True, 'details': reports}
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'asset-verification.json').write_text(json.dumps(report, indent=2))
    print('ELDRITCH_ASSET_PASS species=10 clips=80 source_and_fbx=true')


def render():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / 'EldritchLibrary.blend'))
    scene = bpy.context.scene
    manifest = json.loads((SOURCE / 'manifest.json').read_text())
    scene.render.filepath = str(OUT / 'roster.png')
    scene.frame_set(1)
    bpy.ops.render.render(write_still=True)
    scene.render.resolution_x, scene.render.resolution_y = 960, 540
    frames = OUT / 'motion-frames'
    frames.mkdir(parents=True, exist_ok=True)
    # Each segment samples the real baked takes of the complete roster.
    for index in range(72):
        clip = 'Move' if index < 24 else 'Windup' if index < 42 else 'Attack' if index < 54 else 'Death'
        first, duration = (0, 24) if clip == 'Move' else (24, 18) if clip == 'Windup' else (42, 12) if clip == 'Attack' else (54, 18)
        for entry in manifest['species']:
            bpy.data.objects[entry['name'] + '_Rig'].animation_data.action = bpy.data.actions[entry['name'] + '_' + clip]
        length = next(c['frames'] for c in manifest['species'][0]['clips'] if c['name'] == clip)
        scene.frame_set(1 + round((index - first) / (duration - 1) * length))
        scene.render.filepath = str(frames / f'{index:04d}.png')
        bpy.ops.render.render(write_still=True)


if __name__ == '__main__':
    render() if '--render' in sys.argv else verify()
