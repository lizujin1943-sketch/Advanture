"""独立重新打开资产并检查结构契约；数值报告不替代真实多角度观察。"""
from pathlib import Path
from collections import Counter
import json
import sys
import bpy

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'source-assets/creatures/abyssal-watcher'
OUT = ROOT / 'artifacts/abyssal-watcher'

def verify():
    # 动画阶段使用不可变的已确认模型验证造型基础；动画另有真实播放检查。
    path = SOURCE / 'AbyssalWatcher_ModelApproved.blend'
    if not path.exists():path=SOURCE/'AbyssalWatcher.blend'
    assert path.is_file(), '尚未生成 AbyssalWatcher.blend'
    bpy.ops.wm.open_mainfile(filepath=str(path))
    rig = bpy.data.objects.get('AW_Rig')
    assert rig is not None and rig.type == 'ARMATURE', '缺少可编辑骨架'
    limbs = ['A01','A02','A03','A04','S01','S02','S03','S04']
    meshes = []
    for key in limbs:
        obj = bpy.data.objects.get('AW_Arm_' + key)
        assert obj is not None, f'缺少独立触手 {key}'
        assert obj.get('limb_id') == key
        assert len(obj.data.vertices) > 1000, f'{key} 缺少形体/吸盘结构'
        assert any(m.type == 'ARMATURE' and m.object == rig for m in obj.modifiers)
        assert all(v.groups for v in obj.data.vertices), f'{key} 存在未绑定顶点'
        assert bpy.data.objects.get('AW_Stump_' + key), f'{key} 缺少独立身体残端'
        meshes.append(obj)
    required = []
    assert not [a for a in bpy.data.actions if a.name.startswith('AW_')], '静态基线不应混入动作'
    for name in ['AW_Head','AW_Eye_Center','AW_MouthCavity','AW_Reef']:
        assert bpy.data.objects.get(name), f'缺少外观特征 {name}'
    missing = [i.filepath for i in bpy.data.images if i.source == 'FILE' and not i.packed_file
               and not Path(bpy.path.abspath(i.filepath)).exists()]
    assert not missing, f'贴图丢失 {missing}'
    manifest = json.loads((SOURCE / 'exports/manifest.json').read_text())
    assert manifest['arena']['outer_radius_m'] == 20
    assert manifest['status'] in ('model_review_pending','model_approved')
    cavity = bpy.data.objects['AW_MouthCavity']
    assert cavity.dimensions.y > 1.5, '口部应有真实纵深，而非圆环加平面牙齿'
    assert len([o for o in bpy.data.objects if o.name.startswith('AW_MouthPetal_')]) == 12
    head = bpy.data.objects['AW_Head']
    assert head.get('surface_role') == 'cuttlefish_inspired_swept_mantle'
    # 头部和口腔为同一连续曲面的两个可编辑区。分件后必须共享整个边界，
    # 不允许表面平滑了却留下裂口；无需把本验证扩展成无关的游戏回归。
    def boundary(obj):
        counts = Counter(key for p in obj.data.polygons for key in p.edge_keys)
        assert max(counts.values()) <= 2, f'{obj.name} 存在多重共用边'
        def position(index):
            return tuple(round(v, 6) for v in obj.data.vertices[index].co)
        return {tuple(sorted((position(a), position(b))))
                for (a,b), count in counts.items() if count == 1}
    head_edge, mouth_edge = boundary(head), boundary(cavity)
    assert head_edge and head_edge == mouth_edge, '头部与口腔的分件边界不吻合'
    for name in ('AW_Eye_Center', 'AW_Eye_L', 'AW_Eye_R'):
        eye = bpy.data.objects[name]
        assert all(p.normal.y < 0 for p in eye.data.polygons if len(p.vertices) <= 4), f'{name} 角膜朝向错误'
    for obj in (head, cavity):
        assert obj.data.color_attributes.get('TissueWeight'), f'{obj.name} 缺少口内渐变数据'
    asset_objects = list(bpy.data.collections['01_AbyssalWatcher_EXPORT'].objects)
    animated = [o.name for o in asset_objects if o.animation_data and
                (o.animation_data.action or o.animation_data.nla_tracks or o.animation_data.drivers)]
    assert not animated, f'静态基线不应混入动画驱动：{animated}'
    report = {'blender':bpy.app.version_string,'limbs':limbs,'bones':len(rig.data.bones),
              'actions':required,'missing_textures':missing,
              'arm_vertices':{o.name:len(o.data.vertices) for o in meshes},
              'head_vertices':len(head.data.vertices),
              'head_dimensions_m':[round(v,3) for v in head.dimensions],
              'cavity_depth_m':round(cavity.dimensions.y,3),
              'matching_mouth_boundary_edges':len(head_edge),
              'eye_front_normals':'PASS', 'head_shape':manifest['head_shape'],
              'validation_scope':'静态结构、贴图路径、头口边界；不包含动画或游戏集成',
              'contract':'PASS'}
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT/'structure-verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
    print(json.dumps(report,ensure_ascii=False))

if __name__ == '__main__':
    verify()
