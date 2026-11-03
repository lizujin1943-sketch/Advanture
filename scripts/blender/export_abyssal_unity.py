"""将已批准的深渊凝视者导出为 Unity 资源；不写回任何 .blend 源文件。

Blender --background --factory-startup --python scripts/blender/export_abyssal_unity.py
-- --collision-only 可先生成确定性射线需要的毫米骨链采样。
-- --geometry 使用已有采样继续生成 FBX；--verify 重新导入最终 FBX 验证。
"""
from pathlib import Path
import argparse
import hashlib
import json
import math
import shutil
import sys
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from abyssal_common import ROOT, SOURCE, ARMS, FRONT, bone_name, bone_edges, count, direction, length, radius
from abyssal_actions import FACE_PROPERTIES
from animate_abyssal_watcher import activate_clip, assign, curves, clear_pose

OUT = SOURCE / 'exports/unity'
ART = ROOT / 'samples/TacticalGame.Unity/Assets/HookIsland/Art/AbyssalWatcher'
FPS = 30
BASIS = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')


def atomic_copy(source, destination):
    """Unity 自动导入只看到完整文件；点号临时文件不会被当作导入资产。"""
    temporary = destination.with_name('.' + destination.name + '.tmp')
    shutil.copy2(source, temporary)
    temporary.replace(destination)


def write_face_manifest():
    """Unity 显式烘焙 driver 的形态权重，绕过 FBX 原始 Key 数据采样常数的问题。"""
    path = OUT / 'unity-manifest.json'
    manifest = json.loads(path.read_text())
    metadata = json.loads((OUT / 'pose-samples.json').read_text())
    samples = np.load(OUT / 'pose-samples.npz')
    names = {'mouthOpen': 'mouth_open', 'blinkCenter': 'blink_Center', 'blinkL': 'blink_L', 'blinkR': 'blink_R'}
    for clip in manifest['clips']:
        values = samples[clip['name'] + '_properties']
        for field, source in names.items():
            clip[field] = values[:, metadata['properties'].index(source)].round(7).tolist()
    manifest['blendShapeAnimation'] = 'FBX stores shape geometry; Unity .anim weights are explicitly baked from source mouthOpen/blinkCenter/blinkL/blinkR arrays. Do not use raw FBX shape curves.'
    write_json(path, manifest)
    atomic_copy(path, ART / path.name)


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_hashes():
    return {p.name: sha256(p) for p in (SOURCE / 'AbyssalWatcher.blend', SOURCE / 'AbyssalWatcher_ModelApproved.blend')}


def load_source():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / 'AbyssalWatcher.blend'))
    rig = bpy.data.objects['AW_Rig']
    # 只保留正式本体；不把舞台、断肢预览副本、喷墨占位、摄像机带进导出。
    keep = {rig} | {o for o in bpy.data.objects if o.type == 'MESH' and o.parent == rig}
    for obj in list(bpy.data.objects):
        if obj not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)
    # activate_clip 需要预览断肢对象，正式导出直接执行同等的本体重置。
    return rig, sorted((o for o in keep if o.type == 'MESH'), key=lambda o: o.name)


def activate(rig, name):
    clear_pose(rig)
    for key in ARMS:
        rig['present_' + key] = 1.0
    assign(rig, bpy.data.actions[name])
    rig.update_tag()
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()


def unity(v):
    return (float(v[0]), float(v[2]), float(v[1]))


def mm(v):
    return [int(round(x * 1000)) for x in unity(v)]


def box(obj, depsgraph):
    evaluated = obj.evaluated_get(depsgraph)
    points = np.asarray([unity(evaluated.matrix_world @ Vector(c)) for c in evaluated.bound_box])
    return points.min(axis=0), points.max(axis=0)


def sample():
    rig, meshes = load_source()
    source = json.loads((SOURCE / 'exports/animation-manifest.json').read_text())
    clips = source['clips']
    bones = [b.name for b in rig.pose.bones]
    properties = FACE_PROPERTIES + ['sever_' + k for k in ARMS]
    joint_count = sum(count(k) + 1 for k in ARMS)
    limb_info = []
    point_offset = 0
    for key in ARMS:
        d = unity(direction(key))
        radii = [int(round(radius(distance / length(key)) * 1.18 * 1000)) for distance in bone_edges(key)]
        limb_info.append({'id': key, 'pointOffset': point_offset, 'pointCount': count(key) + 1,
                          'boneRoot': 'DEF_' + key + '_00', 'control': 'CTRL_' + key,
                          'azimuthDegreesFromNegativeZ': ARMS[key],
                          'authoredDirectionPermilleXZ': [int(round(d[0] * 1000)), int(round(d[2] * 1000))],
                          'uncoiledLengthMm': int(length(key) * 1000), 'radiiMm': radii})
        point_offset += count(key) + 1
    result = {'schema': 1, 'fps': FPS, 'coordinateSystem': 'Unity local meters (x,z,y from Blender), forward -Z; binary values in millimeters',
              'worldTranslationIncluded': False, 'binaryFormat': 'raw little-endian int32, frame-major/limb-major/joint-major, each point x,y,z,radiusMm',
              'jointsFile': 'collision-joints.bin', 'bodyFile': 'collision-body.bin',
              'jointCount': joint_count, 'frameStrideBytes': joint_count * 4 * 4,
              'bodyFrameStrideBytes': 40, 'bodyRecord': ['headMinX','headMinY','headMinZ','headMaxX','headMaxY','headMaxZ','eyeX','eyeY','eyeZ','eyeRadius'],
              'radiusApproximation': 'authored cross-section radius × 1.18 covers skin/suckers; capsule endpoints use consecutive points',
              'limbs': limb_info, 'clips': []}
    poses = {}
    joint_offset = body_offset = 0
    with (OUT / 'collision-joints.bin').open('wb') as jf, (OUT / 'collision-body.bin').open('wb') as bf:
        for name, clip in clips.items():
            activate(rig, name)
            frames = clip['frame_end']
            channels = np.zeros((frames, len(bones), 10), dtype=np.float32)
            props = np.zeros((frames, len(properties)), dtype=np.float32)
            collision = np.zeros((frames, joint_count, 4), dtype='<i4')
            body = np.zeros((frames, 10), dtype='<i4')
            for fi in range(frames):
                bpy.context.scene.frame_set(fi + 1)
                bpy.context.view_layer.update()
                depsgraph = bpy.context.evaluated_depsgraph_get()
                for bi, bone in enumerate(rig.pose.bones):
                    location, rotation, scale = bone.matrix_basis.decompose()
                    channels[fi, bi] = (*location, *rotation, *scale)
                props[fi] = [float(rig[p]) for p in properties]
                for entry in limb_info:
                    key = entry['id']
                    points = [rig.matrix_world @ rig.pose.bones[bone_name(key, i)].head for i in range(count(key))]
                    points.append(rig.matrix_world @ rig.pose.bones[bone_name(key, count(key) - 1)].tail)
                    for ji, p in enumerate(points):
                        collision[fi, entry['pointOffset'] + ji] = (*mm(p), entry['radiiMm'][ji])
                lo, hi = box(bpy.data.objects['AW_Head'], depsgraph)
                eye_lo, eye_hi = box(bpy.data.objects['AW_Eye_Center'], depsgraph)
                eye = (eye_lo + eye_hi) * .5
                # 中央眼是高杏仁形；球代理半径覆盖最宽轴，仍小于头部。
                eye_radius = max(eye_hi - eye_lo) * .5
                body[fi] = np.round(np.concatenate((lo, hi, eye, [eye_radius])) * 1000).astype('<i4')
            jf.write(collision.tobytes())
            bf.write(body.tobytes())
            entry = {'name': name, 'frameCount': frames, 'offsetBytes': joint_offset,
                     'bodyOffsetBytes': body_offset, 'durationSeconds': clip['duration_seconds'], 'loop': clip['loop'],
                     'neutralEndpointsMm': {l['id']: collision[0, l['pointOffset'] + l['pointCount'] - 1, :3].tolist() for l in limb_info},
                     'headBoundsMm': body[0, :6].tolist(), 'eyeSphereMm': body[0, 6:].tolist()}
            if name.startswith('AW_Slam_'):
                limb = next(l for l in limb_info if l['id'] == clip['limb'])
                entry['impactEndpointMm'] = collision[45, limb['pointOffset'] + limb['pointCount'] - 1, :3].tolist()
            result['clips'].append(entry)
            poses[name + '_bones'] = channels
            poses[name + '_properties'] = props
            joint_offset += collision.nbytes
            body_offset += body.nbytes
            print('SAMPLED', name, frames, flush=True)
    result['sourceHashes'] = source_hashes()
    write_json(OUT / 'collision-manifest.json', result)
    np.savez_compressed(OUT / 'pose-samples.npz', **poses)
    write_json(OUT / 'pose-samples.json', {'bones': bones, 'properties': properties, 'clips': clips})
    print('COLLISION_READY', result['jointCount'], joint_offset, flush=True)


def active(obj):
    for other in bpy.context.selected_objects:
        other.select_set(False)
    obj.hide_set(False)
    obj.hide_viewport = False
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def reduce_mesh(obj, ratio):
    """只简化当前内存中的副本；形态键位移作为顶点属性随折叠插值。"""
    active(obj)
    before = len(obj.data.vertices)
    shapes = []
    if obj.data.shape_keys:
        keys = obj.data.shape_keys
        base = np.empty(before * 3, dtype=np.float32)
        keys.key_blocks[0].data.foreach_get('co', base)
        for index, key in enumerate(keys.key_blocks[1:]):
            attribute_name = 'AW_ShapeDelta_' + str(index)
            delta = np.empty_like(base)
            key.data.foreach_get('co', delta)
            attribute = obj.data.attributes.new(attribute_name, 'FLOAT_VECTOR', 'POINT')
            attribute.data.foreach_set('vector', delta - base)
            shapes.append((key.name, attribute_name))
        obj.shape_key_clear()
    original_vertices = [v.co.copy() for v in obj.data.vertices]
    # BVH 验证是表面误差，不把细分顶点数量误当成轮廓质量。
    original_surface = BVHTree.FromPolygons(original_vertices, [p.vertices[:] for p in obj.data.polygons])
    modifier = obj.modifiers.new('Unity surface reduction', 'DECIMATE')
    modifier.ratio = ratio
    modifier.use_collapse_triangulate = True
    bpy.ops.object.modifier_move_up(modifier=modifier.name)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    base = np.empty(len(obj.data.vertices) * 3, dtype=np.float32)
    obj.data.vertices.foreach_get('co', base)
    if shapes:
        obj.shape_key_add(name='Basis')
        for name, attribute_name in shapes:
            attribute = obj.data.attributes.get(attribute_name)
            assert attribute is not None, '减面丢失形态键位移：' + name
            delta = np.empty_like(base)
            attribute.data.foreach_get('vector', delta)
            key = obj.shape_key_add(name=name)
            key.data.foreach_set('co', base + delta)
            obj.data.attributes.remove(attribute)
            # 原驱动函数只含简单控制量，导出器将其烘成标准 BlendShape 曲线。
            prop = 'mouth_open' if name == 'Ink_Aperture' else 'blink_' + name.removeprefix('Close_')
            fcurve = key.driver_add('value')
            driver = fcurve.driver
            driver.type = 'SCRIPTED'
            driver.expression = 'value'
            variable = driver.variables.new()
            variable.name = 'value'
            variable.type = 'SINGLE_PROP'
            variable.targets[0].id = bpy.data.objects['AW_Rig']
            variable.targets[0].data_path = '["' + prop + '"]'
    # 从原高模采样到简化表面，覆盖轮廓和开口边缘的最大误差。
    simplified_surface = BVHTree.FromPolygons([v.co for v in obj.data.vertices], [p.vertices[:] for p in obj.data.polygons])
    sample_step = max(1, len(original_vertices) // 6000)
    distances = [simplified_surface.find_nearest(p)[3] for p in original_vertices[::sample_step]]
    reduced_distances = [original_surface.find_nearest(obj.data.vertices[i].co)[3] for i in range(0, len(obj.data.vertices), max(1, len(obj.data.vertices) // 6000))]
    error = max(distances + reduced_distances)
    assert error < .085, (obj.name, '减面表面误差超限', error)
    return {'name': obj.name, 'sourceVertices': before, 'exportVertices': len(obj.data.vertices),
            'collapseRatio': ratio, 'sampledMaxSurfaceErrorMeters': error}


def bake_actions(rig, metadata, samples):
    # 导出前把每个片段未拥有的骨骼显式写回参考姿态，避免连续导出相互污染。
    # Unity 端随后剥离不属于片段的曲线，用同一套资产支持逐腕组合。
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    for name, clip in metadata['clips'].items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        assign(rig, action)
        channels = samples[name + '_bones']
        properties = samples[name + '_properties']
        for fi in range(len(channels)):
            for bi, bone_name_ in enumerate(metadata['bones']):
                bone = rig.pose.bones[bone_name_]
                value = channels[fi, bi]
                bone.location = value[:3]
                bone.rotation_quaternion = value[3:7]
                bone.scale = value[7:]
                for path in ('location', 'rotation_quaternion', 'scale'):
                    bone.keyframe_insert(path, frame=fi + 1, group=bone_name_)
            for pi, property_ in enumerate(metadata['properties']):
                rig[property_] = float(properties[fi, pi])
                rig.keyframe_insert('["' + property_ + '"]', frame=fi + 1)
        for curve in curves(action):
            for point in curve.keyframe_points:
                point.interpolation = 'LINEAR'
        print('UNITY_ACTION', name, len(channels), flush=True)
    activate(rig, 'AW_Idle')


def standardize_arm_hierarchy(rig, metadata, samples):
    """FBX 不支持 Blender 的 inherit_scale=NONE；消除长链父缩放产生的剪切。

    导出骨保留原名、绑定矩阵和权重，各节直接隶属该腕 CTRL。姿态先用
    源骨骼的真实继承规则算到模型空间，再换成新父级下的 TRS，避免近似剪切。
    """
    bone_indices = {name: i for i, name in enumerate(metadata['bones'])}
    data_bones = list(rig.data.bones)
    for clip_name in metadata['clips']:
        source = samples[clip_name + '_bones']
        converted = source.copy()
        for fi, frame in enumerate(source):
            world = {}
            for bone in data_bones:
                value = frame[bone_indices[bone.name]]
                basis = Matrix.LocRotScale(Vector(value[:3]), Quaternion(value[3:7]), Vector(value[7:]))
                if bone.parent:
                    world[bone.name] = bone.convert_local_to_pose(basis, bone.matrix_local,
                        parent_matrix=world[bone.parent.name], parent_matrix_local=bone.parent.matrix_local)
                else:
                    world[bone.name] = bone.convert_local_to_pose(basis, bone.matrix_local)
            for key in ARMS:
                control = rig.data.bones['CTRL_' + key]
                prefix = control.matrix_local @ world[control.name].inverted()
                for index in range(count(key)):
                    name = bone_name(key, index)
                    bone = rig.data.bones[name]
                    local = bone.matrix_local.inverted() @ prefix @ world[name]
                    location, rotation, scale = local.decompose()
                    rebuilt = Matrix.LocRotScale(location, rotation, scale)
                    assert max(abs(a-b) for row_a, row_b in zip(local, rebuilt) for a, b in zip(row_a, row_b)) < 2e-4, (name, '仍含不可导出的剪切')
                    converted[fi, bone_indices[name]] = (*location, *rotation, *scale)
        samples[clip_name + '_bones'] = converted
    active(rig)
    bpy.ops.object.mode_set(mode='EDIT')
    for key in ARMS:
        control = rig.data.edit_bones['CTRL_' + key]
        for index in range(count(key)):
            bone = rig.data.edit_bones[bone_name(key, index)]
            bone.use_connect = False
            bone.parent = control
            bone.inherit_scale = 'FULL'
    bpy.ops.object.mode_set(mode='OBJECT')


def export():
    metadata = json.loads((OUT / 'pose-samples.json').read_text())
    sampled_hashes = json.loads((OUT / 'collision-manifest.json').read_text())['sourceHashes']
    assert sampled_hashes == source_hashes(), '源文件已变化，必须先重新采样，不能混用旧碰撞与新模型'
    stored_samples = np.load(OUT / 'pose-samples.npz')
    samples = {name: stored_samples[name] for name in stored_samples.files}
    rig, meshes = load_source()
    clear_pose(rig)
    standardize_arm_hierarchy(rig, metadata, samples)
    for obj in meshes:
        # 可见性由游戏损伤状态驱动。动画只含骨骼/表情，绝不写 Renderer.enabled。
        obj.animation_data_clear()
        obj.hide_render = obj.hide_viewport = False
        active(obj)
        for modifier in list(obj.modifiers):
            if modifier.type == 'SOLIDIFY':
                bpy.ops.object.modifier_apply(modifier=modifier.name)
    geometry = []
    for obj in meshes:
        ratio = .25 if obj.name == 'AW_Head' else .6 if obj.name.startswith('AW_Arm_') else .5 if obj.name.startswith('AW_Eye_') else .65 if obj.name == 'AW_Reef' else 1
        if ratio < 1:
            geometry.append(reduce_mesh(obj, ratio))
        else:
            geometry.append({'name': obj.name, 'sourceVertices': len(obj.data.vertices), 'exportVertices': len(obj.data.vertices), 'collapseRatio': 1, 'sampledMaxSurfaceErrorMeters': 0})
        # 同一个材质的重复槽是制作时拆口腔的遗留；导出只保留真实子材质。
        unique, mapping = [], {}
        for index, material in enumerate(obj.data.materials):
            if material not in unique:
                unique.append(material)
            mapping[index] = unique.index(material)
        polygon_materials = [mapping[p.material_index] for p in obj.data.polygons]
        obj.data.materials.clear()
        for material in unique:
            obj.data.materials.append(material)
        for polygon, index in zip(obj.data.polygons, polygon_materials):
            polygon.material_index = index
        # FBX 使用 LINEAR 顶点颜色；Unity shader 将 TissueWeight 当作权重而非颜色。
        if obj.data.color_attributes.get('TissueWeight'):
            obj.data.color_attributes.active_color = obj.data.color_attributes['TissueWeight']
        obj.data.calc_loop_triangles()
        geometry[-1]['triangles'] = len(obj.data.loop_triangles)
        geometry[-1]['materials'] = [m.name for m in obj.data.materials]
        geometry[-1]['blendShapes'] = [k.name for k in obj.data.shape_keys.key_blocks[1:]] if obj.data.shape_keys else []
        print('UNITY_MESH', obj.name, len(obj.data.vertices), geometry[-1]['triangles'], flush=True)
    # 未使用的抓取插槽不进入正式资产；没有抓取、举起或抛掷玩家动作。
    active(rig)
    bpy.ops.object.mode_set(mode='EDIT')
    for bone in list(rig.data.edit_bones):
        if bone.name.startswith('SOCKET_Grip_'):
            rig.data.edit_bones.remove(bone)
    bpy.ops.object.mode_set(mode='OBJECT')
    # 从采样中删掉相应无蒙皮插槽通道。
    keep_bones = [i for i, name in enumerate(metadata['bones']) if name in rig.pose.bones]
    filtered = {name: samples[name][:, keep_bones] if name.endswith('_bones') else samples[name] for name in samples}
    metadata['bones'] = [metadata['bones'][i] for i in keep_bones]
    bake_actions(rig, metadata, filtered)
    for obj in bpy.context.selected_objects:
        obj.select_set(False)
    for obj in [rig] + meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    scene = bpy.context.scene
    scene.render.fps = FPS
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1
    # FBX 单位写为米，避免在骨架层叠加 100 倍缩放。Unity importer 的 fileScale 处理单位。
    fbx = OUT / 'AbyssalWatcher.fbx'
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True,
        object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=False,
        add_leaf_bones=False, use_armature_deform_only=False,
        bake_anim=True, bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, bake_anim_step=1, bake_anim_simplify_factor=0,
        global_scale=1, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
        axis_forward='-Z', axis_up='Y', bake_space_transform=False,
        mesh_smooth_type='FACE', use_tspace=True, use_triangles=True,
        colors_type='LINEAR', prioritize_active_color=True,
        path_mode='STRIP', embed_textures=False, use_custom_props=False)
    atomic_copy(fbx, ART / fbx.name)
    textures = ART / 'Textures'
    textures.mkdir(parents=True, exist_ok=True)
    for texture in sorted((SOURCE / 'textures').glob('*.png')):
        atomic_copy(texture, textures / texture.name)
    # 每片段的所有权会用于 Unity 导出 .anim 时剥离非所属骨链，而非仅记录在说明里。
    clips = []
    for name, data in metadata['clips'].items():
        full_body = data['kind'] not in ('Slam', 'Sweep', 'Sever', 'Ink', 'Hit')
        owned = list(data['bones'])
        if data['kind'] not in ('Slam', 'Sweep', 'Sever'):
            owned += ['DEF_Eye_' + eye for eye in ('Center', 'L', 'R')]
            owned += ['DEF_Petal_' + str(i).zfill(2) for i in range(12)]
        clips.append({'name': name, 'frameCount': data['frame_end'], 'durationSeconds': data['duration_seconds'],
                      'loop': data['loop'], 'limb': data['limb'] or '', 'kind': data['kind'],
                      'bones': sorted(set(owned)), 'face': data['kind'] not in ('Slam', 'Sweep', 'Sever'),
                      'fullBody': full_body, 'events': data['events'],
                      'eyeGlow': filtered[name + '_properties'][:, metadata['properties'].index('eye_glow')].round(6).tolist()})
    manifest = {'schema': 1, 'asset': 'AbyssalWatcher', 'fps': FPS,
                'unityForward': '-Z', 'unityUp': '+Y', 'metersPerUnit': 1,
                'unityCoordinateFromBlender': '(x,z,y)',
                'fbxAxisForward': '-Z', 'fbxAxisUp': 'Y', 'fbxFileUnits': 'meters',
                'prefabPath': 'Assets/HookIsland/Art/AbyssalWatcher/AbyssalWatcher.prefab',
                'animatorChild': 'Model', 'modelPath': 'Assets/HookIsland/Art/AbyssalWatcher/AbyssalWatcher.fbx',
                'armHierarchy': 'DEF_<limb>_<index> are siblings directly under CTRL_<limb>; names, bind poses, source world poses and skin weights preserved; avoids unsupported Blender inherit_scale=NONE',
                'sourceHashes': source_hashes(), 'boneCount': len(rig.data.bones), 'meshCount': len(meshes),
                'meshVertices': sum(m['exportVertices'] for m in geometry), 'meshTriangles': sum(m['triangles'] for m in geometry),
                'meshes': geometry, 'clips': clips,
                'limbs': [{'id': k, 'boneRoot': 'DEF_' + k + '_00', 'control': 'CTRL_' + k,
                           'arm': 'AW_Arm_' + k, 'membrane': 'AW_Membrane_' + k, 'stump': 'AW_Stump_' + k,
                           'socket': 'SOCKET_Sever_' + k} for k in ARMS],
                'excluded': ['AW_Showcase', 'AW_Showcase_Detached_A02', 'AW_DetachedFall_*', 'SOCKET_Grip_*',
                             'Preview_InkJet', 'arena', 'cameras', 'player_grab', 'player_lift', 'player_throw'],
                'integration': 'Skinned FBX + 24 baked actions; Unity installer makes masked .anim clips and art-only prefab. Source animation events are informational; damage is simulation-owned.',
                'materialNotes': 'Original external albedo, roughness, normal, emission textures; custom surface shader reproduces TissueWeight mouth gradient. Procedural submillimeter object-space grain approximated by normal map; Blender subsurface scattering is not recreated.'}
    write_json(OUT / 'unity-manifest.json', manifest)
    write_json(ART / 'unity-manifest.json', manifest)
    write_face_manifest()
    print('FBX_READY', fbx.stat().st_size, manifest['meshVertices'], manifest['meshTriangles'], flush=True)


def verify():
    manifest = json.loads((OUT / 'unity-manifest.json').read_text())
    collision = json.loads((OUT / 'collision-manifest.json').read_text())
    expected = np.memmap(OUT / 'collision-joints.bin', dtype='<i4', mode='r')
    expected_body = np.memmap(OUT / 'collision-body.bin', dtype='<i4', mode='r')
    pose_metadata = json.loads((OUT / 'pose-samples.json').read_text())
    source_samples = np.load(OUT / 'pose-samples.npz')
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT / 'AbyssalWatcher.fbx'), use_anim=True,
                             automatic_bone_orientation=False, ignore_leaf_bones=False)
    rigs = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    assert len(rigs) == 1, ('FBX 骨架数量', len(rigs))
    rig = rigs[0]
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    assert len(meshes) == manifest['meshCount'], ('FBX 网格数量', len(meshes))
    assert len(rig.data.bones) == manifest['boneCount']
    assert not any('Showcase' in a.name or 'Detached' in a.name or 'Grab' in a.name for a in bpy.data.actions)
    imported_names = [a.name for a in bpy.data.actions]
    # Blender 重导入把同一 FBX take 的骨架与形态键分成不同 Action；按 slot目标选择。
    def action_for(owner, clip_name):
        matches = []
        for action in bpy.data.actions:
            if not action.name.endswith(clip_name):
                continue
            for slot in action.slots:
                if slot.target_id_type == owner.id_type:
                    matches.append(action)
                    break
        if owner.id_type == 'OBJECT':
            matches = [a for a in matches if any(f.data_path.startswith('pose.bones') for f in curves(a))]
        return matches
    max_joint_error = 0
    max_shape_error = 0
    max_raw_fbx_shape_error = 0
    max_head_bounds_error = 0
    max_eye_center_error = 0
    clip_reports = []
    for clip in collision['clips']:
        matches = action_for(rig, clip['name'])
        assert len(matches) == 1, (clip['name'], [a.name for a in matches])
        assign(rig, matches[0])
        for object_name in ('AW_Head', 'AW_MouthCavity'):
            keys = bpy.data.objects[object_name].data.shape_keys
            key_actions = [a for a in action_for(keys, clip['name']) if a.name.startswith(keys.name + '|')]
            assert len(key_actions) == 1, (object_name, clip['name'], [a.name for a in key_actions])
            assign(keys, key_actions[0])
        frames = sorted({0, clip['frameCount'] // 2, clip['frameCount'] - 1, min(45, clip['frameCount'] - 1), min(73, clip['frameCount'] - 1)})
        local_max = 0
        for fi in frames:
            bpy.context.scene.frame_set(fi + 1)
            bpy.context.view_layer.update()
            values = []
            for limb in collision['limbs']:
                key = limb['id']
                points = [rig.matrix_world @ rig.pose.bones[bone_name(key, i)].head for i in range(count(key))]
                points.append(rig.matrix_world @ rig.pose.bones[bone_name(key, count(key) - 1)].tail)
                values += [mm(p) for p in points]
            start = (clip['offsetBytes'] + fi * collision['frameStrideBytes']) // 4
            target = expected[start:start + collision['jointCount'] * 4].reshape((-1, 4))[:, :3]
            # FBX不储存骨尾长度；末端tail从导入器补长，只验证每节head及非末端。
            heads = [limb['pointOffset'] + i for limb in collision['limbs'] for i in range(limb['pointCount'] - 1)]
            error = int(np.max(np.abs(np.asarray(values)[heads] - target[heads])))
            local_max = max(local_max, error)
            for object_name in ('AW_Head', 'AW_MouthCavity'):
                for shape in bpy.data.objects[object_name].data.shape_keys.key_blocks[1:]:
                    prop = 'mouth_open' if shape.name == 'Ink_Aperture' else 'blink_' + shape.name.removeprefix('Close_')
                    property_index = pose_metadata['properties'].index(prop)
                    target_value = source_samples[clip['name'] + '_properties'][fi, property_index]
                    max_raw_fbx_shape_error = max(max_raw_fbx_shape_error, abs(shape.value - float(target_value)))
                    field = {'mouth_open': 'mouthOpen', 'blink_Center': 'blinkCenter', 'blink_L': 'blinkL', 'blink_R': 'blinkR'}[prop]
                    manifest_clip = next(c for c in manifest['clips'] if c['name'] == clip['name'])
                    explicit_value = manifest_clip[field][fi]
                    max_shape_error = max(max_shape_error, abs(explicit_value - float(target_value)))
                    shape.value = explicit_value
            bpy.context.view_layer.update()
            lo, hi = box(bpy.data.objects['AW_Head'], bpy.context.evaluated_depsgraph_get())
            body_start = clip['bodyOffsetBytes'] // 4 + fi * 10
            target_bounds = expected_body[body_start:body_start + 6]
            max_head_bounds_error = max(max_head_bounds_error, float(np.max(np.abs(np.concatenate((lo, hi)) * 1000 - target_bounds))))
            eye_lo, eye_hi = box(bpy.data.objects['AW_Eye_Center'], bpy.context.evaluated_depsgraph_get())
            max_eye_center_error = max(max_eye_center_error, float(np.max(np.abs((eye_lo + eye_hi) * 500 - expected_body[body_start + 6:body_start + 9]))))
        assert local_max <= 4, (clip['name'], 'FBX 重导入骨链位置与源采样偏差毫米', local_max)
        max_joint_error = max(max_joint_error, local_max)
        clip_reports.append({'name': clip['name'], 'sampledFramesZeroBased': frames, 'maxJointCoordinateErrorMm': local_max,
                             'importedFrameRange': list(matches[0].frame_range)})
    assert max_shape_error < .0001, ('FBX 表情曲线与源控制量不一致', max_shape_error)
    assert max_head_bounds_error < 40, ('FBX 头部表情/边界偏差毫米', max_head_bounds_error)
    assert max_eye_center_error < 5, ('FBX 眼部驱动骨骼偏差毫米', max_eye_center_error)
    head = bpy.data.objects['AW_Head']
    cavity = bpy.data.objects['AW_MouthCavity']
    assert head.data.shape_keys and len(head.data.shape_keys.key_blocks) == 5
    assert cavity.data.shape_keys and len(cavity.data.shape_keys.key_blocks) == 2
    assert head.data.color_attributes, 'FBX 丢失口腔渐变顶点色'
    assert all(any(modifier.type == 'ARMATURE' for modifier in obj.modifiers) for obj in meshes)
    report = {'status': 'PASS', 'scope': 'Background Blender FBX roundtrip; Unity import and actual game rendering remain root task verification',
              'sourceHashesUnchanged': source_hashes() == manifest['sourceHashes'],
              'meshCount': len(meshes), 'boneCount': len(rig.data.bones), 'expectedClipCount': len(manifest['clips']),
              'maxJointCoordinateErrorMm': max_joint_error, 'clips': clip_reports,
              'maxBlendShapeWeightError': max_shape_error, 'maxHeadBoundsErrorMm': max_head_bounds_error,
              'maxEyeCenterErrorMm': max_eye_center_error, 'rawFbxBlendShapeWeightError': max_raw_fbx_shape_error,
              'blendShapeCurveSource': 'Explicit source samples in unity-manifest.json; Unity installer replaces static FBX shape curves',
              'headBlendShapes': [k.name for k in head.data.shape_keys.key_blocks],
              'headColorAttributes': [c.name for c in head.data.color_attributes],
              'sourceToUnityCoordinate': '(x,z,y)', 'allMeshesSkinned': True,
              'importedActions': imported_names,
              'limitations': ['Unity 实际导入/材质编译/运行表现须在编辑器验证', '碰撞半径是保守胶囊近似，非逐三角形碰撞', 'Blender 程序细颗粒与次表面散射以 Unity 贴图表面近似']}
    write_json(OUT / 'unity-export-verification.json', report)
    print('FBX_VERIFY_PASS', len(clip_reports), max_joint_error, flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--collision-only', action='store_true')
    parser.add_argument('--geometry', action='store_true')
    parser.add_argument('--verify', action='store_true')
    parser.add_argument('--face-manifest-only', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    OUT.mkdir(parents=True, exist_ok=True)
    ART.mkdir(parents=True, exist_ok=True)
    before = source_hashes()
    if args.face_manifest_only:
        write_face_manifest()
        assert before == source_hashes()
        return
    if not args.geometry and not args.verify:
        sample()
    if not args.collision_only:
        if args.verify:
            verify()
        else:
            export()
            verify()
    assert before == source_hashes(), '导出不得修改已批准的源模型'


if __name__ == '__main__':
    main()
