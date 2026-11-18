"""Reopen the study, inspect real skin deformation, and render review views."""
from pathlib import Path
import json
import math
import sys
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'source-assets/creatures/eldritch/sculpt-study/DeepOneSculpt.blend'
OUT = ROOT / 'artifacts/eldritch-sculpt-study'


def use(rig, clip, frame=1):
    rig.animation_data.action = bpy.data.actions['DeepOneSculpt_' + clip]
    bpy.context.scene.frame_set(frame)


def sample(mesh):
    evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    data = evaluated.to_mesh()
    result = [data.vertices[i].co.copy() for i in range(0, len(data.vertices), 253)]
    evaluated.to_mesh_clear()
    return result


def delta(a, b):
    return max((x - y).length for x, y in zip(a, b))


def verify(rig, meshes):
    report = {'scope': 'Blender art study only; not a game-ready asset', 'clips': {}}
    for mesh in meshes:
        assert all(v.groups and abs(sum(g.weight for g in v.groups) - 1) < 1e-5 for v in mesh.data.vertices)
        if mesh.name == 'Continuous living skin':
            forbidden = {g.index for g in mesh.vertex_groups if '_Fin' in g.name or g.name.startswith('Barbel')}
            assert not any(g.group in forbidden for v in mesh.data.vertices for g in v.groups), 'Secondary motion captured body skin'
    for clip in ('Idle', 'Move', 'Windup', 'Attack', 'Recover', 'Hit', 'Emerge', 'Death'):
        use(rig, clip)
        end = int(rig.animation_data.action.frame_range[1])
        start = [sample(mesh) for mesh in meshes]
        root = rig.pose.bones['Root'].matrix.copy()
        motion = 0
        for frame in (end // 3, end * 2 // 3, end):
            bpy.context.scene.frame_set(frame)
            assert max(abs(root[i][j] - rig.pose.bones['Root'].matrix[i][j]) for i in range(4) for j in range(4)) < 1e-6
            for mesh, points in zip(meshes, start):
                posed = sample(mesh)
                assert all(math.isfinite(c) for p in posed for c in p)
                motion = max(motion, delta(points, posed))
        assert motion > .001, (clip, motion)
        record = {'sampled_surface_motion_m': motion}
        if clip in ('Idle', 'Move'):
            error = max(delta(points, sample(mesh)) for mesh, points in zip(meshes, start))
            assert error < 1e-4, (clip, error)
            record['loop_error_m'] = error
        report['clips'][clip] = record
    for before, after in [('Windup', 'Attack'), ('Attack', 'Recover')]:
        use(rig, before)
        bpy.context.scene.frame_set(int(rig.animation_data.action.frame_range[1]))
        previous = [sample(mesh) for mesh in meshes]
        use(rig, after)
        error = max(delta(points, sample(mesh)) for mesh, points in zip(meshes, previous))
        assert error < 1e-4, (before, after, error)
        report[before + '_to_' + after + '_surface_error_m'] = error
    report['result'] = 'PASS'
    (OUT / 'study-verification.json').write_text(json.dumps(report, indent=2))
    print('STUDY_VERIFIED', json.dumps(report), flush=True)


def aim(camera, location, target, scale):
    camera.location = location
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.ortho_scale = scale


def render(rig):
    scene = bpy.context.scene
    camera = scene.camera
    use(rig, 'Idle')
    views = [('front', (0, -6, 2.05), (0, 0, 1.05), 2.5),
             ('side', (5, 0, 2.05), (0, 0, 1.05), 2.5),
             ('head-detail', (2, -5, 2.5), (0, -.12, 1.68), .93)]
    for name, position, target, scale in ([] if '--motion-only' in sys.argv else views):
        aim(camera, position, target, scale)
        scene.render.filepath = str(OUT / (name + '.png'))
        bpy.ops.render.render(write_still=True)
    if '--motion' not in sys.argv and '--motion-only' not in sys.argv: return
    scene.render.resolution_x, scene.render.resolution_y = 900, 720
    aim(camera, (3.4, -5.8, 2.8), (.08, 0, 1.05), 3.7)
    folder = OUT / 'motion-frames'; folder.mkdir(exist_ok=True)
    index = 0
    for clip, count in [('Idle', 12), ('Move', 28), ('Windup', 15), ('Attack', 9), ('Recover', 15)]:
        use(rig, clip)
        last = int(rig.animation_data.action.frame_range[1])
        for k in range(count):
            use(rig, clip, 1 + round(k / (count - 1) * (last - 1)))
            scene.render.filepath = str(folder / f'{index:04d}.png')
            bpy.ops.render.render(write_still=True)
            index += 1


def compare(rig):
    """Both real meshes, at equal height and under the same lights."""
    scene = bpy.context.scene
    with bpy.data.libraries.load(str(SOURCE.parent.parent / 'EldritchLibrary.blend'), link=False) as (src, dst):
        dst.objects = ['DeepOne_Rig', 'DeepOne_Skin']
    for obj in dst.objects: scene.collection.objects.link(obj)
    previous = next(o for o in dst.objects if o.type == 'ARMATURE')
    previous.animation_data.action = bpy.data.actions['DeepOne_Idle']
    previous.location = (0, 0, 0); previous.scale = (1, 1, 1)
    previous.rotation_euler = (0, 0, 0)
    use(rig, 'Idle')
    bpy.context.view_layer.update()
    old_skin = next(o for o in previous.children if o.type == 'MESH')
    evaluated = old_skin.evaluated_get(bpy.context.evaluated_depsgraph_get())
    points = [evaluated.matrix_world @ Vector(p) for p in evaluated.bound_box]
    height = max(p.z for p in points) - min(p.z for p in points)
    previous.scale *= 2.04 / height
    previous.location = (-.86, 0, -min(p.z for p in points) * 2.04 / height)
    rig.location = (.86, 0, 0)
    for model in (previous, rig): model.rotation_euler.z = math.radians(-18)
    scene.render.resolution_x, scene.render.resolution_y = 1500, 1000
    aim(scene.camera, (0, -6, 2.3), (0, 0, 1.04), 3.85)
    scene.render.filepath = str(OUT / 'before-after.png')
    bpy.ops.render.render(write_still=True)


if __name__ == '__main__':
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    rig = bpy.data.objects['DeepOneSculpt_Rig']
    meshes = [o for o in rig.children if o.type == 'MESH']
    if '--compare' in sys.argv:
        compare(rig)
    elif '--motion-only' in sys.argv:
        render(rig)
    else:
        verify(rig, meshes)
        if '--render' in sys.argv: render(rig)
