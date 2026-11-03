"""可复现制作入口。只在 --factory-startup 后台新工程中运行，不清空用户打开的文件。

Blender --background --factory-startup --python-exit-code 1 --python scripts/blender/create_abyssal_watcher.py
当前阶段只制作静态模型。用户确认造型之前不创建动作。
"""
from pathlib import Path
import sys
import json
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from abyssal_common import *
from abyssal_materials import build_materials
from abyssal_geometry import build_geometry
from abyssal_pose import create_rig,pose_reference
from abyssal_stage import setup,set_view

def main():
    if bpy.data.filepath:raise RuntimeError('请在独立 --factory-startup 进程中运行，不能覆盖正在编辑的场景')
    for p in [SOURCE,SOURCE/'exports',EVIDENCE,REFERENCES]:p.mkdir(parents=True,exist_ok=True)
    for obj in list(bpy.data.objects):bpy.data.objects.remove(obj,do_unlink=True)
    bpy.context.preferences.filepaths.save_version=0
    scene=bpy.context.scene;scene.name='AbyssalWatcher_Asset'
    scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    collection=bpy.data.collections.new('01_AbyssalWatcher_EXPORT');scene.collection.children.link(collection)
    print('AW: textures',flush=True);mats=build_materials()
    print('AW: geometry',flush=True);objects=build_geometry(collection,mats)
    print('AW: rig',flush=True);rig=create_rig(collection,objects)
    print('AW: stage',flush=True);setup(scene)
    pose_reference(rig)
    for action in list(bpy.data.actions):
        if action.name.startswith('AW_'):bpy.data.actions.remove(action)
    scene.frame_end=1
    bpy.context.view_layer.update();scene.frame_set(1)
    manifest={'asset':'AbyssalWatcher','version':3,'style':'organic_high_detail','head_shape':'cuttlefish_inspired_swept_mantle','revision_focus':['head','eyes','mouth'],'units':'meters','forward':'-Y','fps':FPS,
        'limbs':{k:{'mesh':'AW_Arm_'+k,'stump':'AW_Stump_'+k,'role':'attack' if k in FRONT else 'support',
                    'bones':count(k),'uncoiled_length_m':length(k)} for k in ARMS},
        'status':'model_review_pending','animation_status':'awaiting_user_model_approval',
        'clips':{},'grab':{'shooting_allowed':False,'reference':'Fisher.blend','height_m':1.061,
                           'release_on_limb_destroyed':True,'landing_radius_m':23.5},
        'arena':{'inner_radius_m':12,'outer_radius_m':20,'deck_height_m':.57},
        'integration':'当前为静态模型供造型确认；尚不交付动作或游戏逻辑。'}
    (SOURCE/'exports/manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2))
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'AbyssalWatcher.blend'))
    bpy.ops.file.make_paths_relative()
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'AbyssalWatcher.blend'))
    set_view(scene,'Hero');bpy.ops.render.render(write_still=True)
    set_view(scene,'Face');bpy.ops.render.render(write_still=True)
    set_view(scene,'FaceThreeQuarter');bpy.ops.render.render(write_still=True)
    set_view(scene,'HeadSide');bpy.ops.render.render(write_still=True)
    set_view(scene,'MouthDetail');bpy.ops.render.render(write_still=True)
    print('AW: source and first renders saved',flush=True)

if __name__=='__main__':main()
