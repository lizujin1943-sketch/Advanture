"""从已确认的静态模型生成可独立播放的动作；不重建或覆盖已确认的基础网格。"""
from pathlib import Path
import json
import math
import sys
import bpy
from mathutils import Quaternion

sys.path.insert(0,str(Path(__file__).resolve().parent))
from abyssal_common import ROOT, SOURCE, EVIDENCE, ARMS, direction, bone_name, count, aim
from abyssal_pose import pose_reference
from abyssal_actions import registry, owners, apply_motion, defaults, ease, FACE_PROPERTIES

FPS=30
SHOWCASE=[('AW_Emerge',4),('AW_Idle',2),('AW_Slam_A01',3),('AW_Sweep_A04',3.6),
          ('AW_Ink',3.2),('AW_Hit',1),('AW_Enrage',3),('AW_EnragedIdle',2),
          ('AW_Combination',3.6),('AW_Sever_A02',2.6),('AW_Death',6)]


def curves(action):
    return [f for layer in action.layers for strip in layer.strips
            for bag in strip.channelbags for f in bag.fcurves]


def assign(obj,action):
    """Blender 5 使用 Action Slot；显式选择，避免切换动作后仍绑定上个槽。"""
    obj.animation_data_create();obj.animation_data.action=None;obj.animation_data.action=action
    if action and action.slots:obj.animation_data.action_slot=action.slots[0]


def action_for(obj,name):
    action=bpy.data.actions.new(name);action.use_fake_user=True;assign(obj,action)
    return action


def polish(action):
    for curve in curves(action):
        # 骨骼密集采样用线性插值，断离时刻必须保持阶跃，不能提前半露半藏。
        mode='CONSTANT' if 'sever_' in curve.data_path else 'LINEAR'
        for point in curve.keyframe_points:point.interpolation=mode


def key_pose(rig,bones,properties,frame,previous):
    for name in bones:
        p=rig.pose.bones[name];q=p.rotation_quaternion.copy()
        if name in previous and previous[name].dot(q)<0:q.negate();p.rotation_quaternion=q
        previous[name]=q.copy()
        for path in ('location','rotation_quaternion','scale'):p.keyframe_insert(path,frame=frame,group=name)
    for prop in properties:rig.keyframe_insert('["'+prop+'"]',frame=frame,group='Face_and_state')


def clear_pose(rig):
    assign(rig,None);pose_reference(rig);defaults(rig)
    for key in ARMS:rig['sever_'+key]=0.
    rig.update_tag()


def bake_clip(rig,name,data):
    clear_pose(rig);action=action_for(rig,name);bones,props=owners(data);previous={}
    # 每帧采样保证快速下砸和眨眼有明确关键帧，后续游戏导出可按误差压缩。
    for frame in range(1,data['frame_end']+1):
        apply_motion(rig,data['kind'],(frame-1)/FPS,data.get('limb'))
        key_pose(rig,bones,props,frame,previous)
    for event in data['events']:action.pose_markers.new(event['name']).frame=event['frame']
    action['kind']=data['kind'];action['duration_seconds']=data['duration_seconds']
    action['channel_ownership']=' '.join(bones);polish(action)
    data['bones']=bones;data['properties']=props
    return action


def bake_detached(key,offset=0,name=None,total_end=79):
    obj=bpy.data.objects['AW_Detached_'+key];action=action_for(obj,name or 'AW_DetachedFall_'+key)
    obj.rotation_mode='QUATERNION';d=direction(key)
    for frame in range(1,total_end+1):
        t=max(0,min(2.3,(frame-offset-10)/FPS));f=t/2.3
        obj.location=(d.x*1.7*f,d.y*1.7*f,.85*math.sin(math.pi*f)-16*f*f)
        obj.rotation_quaternion=Quaternion((d.y,-d.x,0),.23*ease(f))@Quaternion((0,0,1),.10*ease(f))
        for p in ('location','rotation_quaternion'):obj.keyframe_insert(p,frame=frame)
    polish(action);assign(obj,None);obj.location=(0,0,0);obj.rotation_quaternion=Quaternion()


def bake_showcase(rig,clips):
    clear_pose(rig);action=action_for(rig,'AW_Showcase');previous={};segments=[];offset=0
    bones=['ROOT','DEF_Head']+[bone_name(k,i) for k in ARMS for i in range(count(k))]
    props=FACE_PROPERTIES+['sever_'+k for k in ARMS];removed=set();last_pose=None;last_face=None
    for name,duration in SHOWCASE:
        data=clips[name];frames=round(duration*FPS)
        action.pose_markers.new(name.removeprefix('AW_')).frame=offset+1
        segments.append({'clip':name,'frame_start':offset+1,'frame_end':offset+frames,'duration_seconds':duration})
        for f in range(frames+1):
            pose_reference(rig);defaults(rig)
            for key in ARMS:rig['sever_'+key]=1. if key in removed else 0.
            apply_motion(rig,data['kind'],f/FPS,data.get('limb'))
            # 合集为了缩短观看时间截取部分待机。用 0.2 秒过渡连接被截断的姿态，
            # 单独攻击片段仍保持原事件时刻和独立骨骼范围。
            if last_pose is not None and f<6:
                w=ease(f/6)
                for bone in bones:
                    p=rig.pose.bones[bone];loc,rot,scale=last_pose[bone]
                    p.location=loc.lerp(p.location,w)
                    p.rotation_quaternion=rot.slerp(p.rotation_quaternion,w)
                    p.scale=scale.lerp(p.scale,w)
                for prop in FACE_PROPERTIES:rig[prop]=last_face[prop]*(1-w)+float(rig[prop])*w
            key_pose(rig,bones,props,offset+f+1,previous)
        last_pose={n:(rig.pose.bones[n].location.copy(),rig.pose.bones[n].rotation_quaternion.copy(),rig.pose.bones[n].scale.copy()) for n in bones}
        last_face={p:float(rig[p]) for p in FACE_PROPERTIES}
        if data['kind']=='Sever':removed.add(data['limb'])
        offset+=frames
    polish(action)
    sever=next(s for s in segments if s['clip']=='AW_Sever_A02')
    bake_detached('A02',sever['frame_start']-1,'AW_Showcase_Detached_A02',offset+1)
    return {'frame_start':1,'frame_end':offset+1,'duration_seconds':offset/FPS,'segments':segments}


def activate_clip(name,preserve_missing=False):
    """切换预览片段，同时切换独立断肢动作。可保留 present_* 验证残缺状态。

    单独在 Action 下拉菜单换动作不会自动重置其他部件或配置断肢副本；
    本入口是预览工具，游戏侧需由其自己的状态与动画系统进行同样的分区管理。
    """
    rig=bpy.data.objects['AW_Rig'];scene=bpy.context.scene
    clear_pose(rig)
    if not preserve_missing:
        for key in ARMS:rig['present_'+key]=1.
    for key in ARMS:
        obj=bpy.data.objects['AW_Detached_'+key];assign(obj,None)
        obj.location=(0,0,0);obj.rotation_quaternion=Quaternion()
    assign(rig,bpy.data.actions[name])
    if name.startswith('AW_Sever_'):
        key=name.removeprefix('AW_Sever_');assign(bpy.data.objects['AW_Detached_'+key],bpy.data.actions['AW_DetachedFall_'+key])
    if name=='AW_Showcase':
        assign(bpy.data.objects['AW_Detached_A02'],bpy.data.actions['AW_Showcase_Detached_A02'])
    end=round(bpy.data.actions[name].frame_range[1]);scene.frame_start=1;scene.frame_end=end
    for marker in list(scene.timeline_markers):scene.timeline_markers.remove(marker)
    for marker in bpy.data.actions[name].pose_markers:scene.timeline_markers.new(marker.name,frame=marker.frame)
    rig.update_tag();scene.frame_set(1);bpy.context.view_layer.update()


def set_limb_present(key,present):
    """外部状态的预览入口。脚本直接赋自定义属性不会自动通知所有驱动依赖。"""
    if key not in ARMS:raise ValueError('未知触手编号：'+str(key))
    rig=bpy.data.objects['AW_Rig'];rig['present_'+key]=1. if present else 0.
    rig.update_tag();bpy.context.view_layer.update()


def build():
    baseline=SOURCE/'AbyssalWatcher_ModelApproved.blend'
    assert baseline.is_file(),'缺少已确认的静态模型；不能用重新生成的模型代替'
    bpy.ops.wm.open_mainfile(filepath=str(baseline))
    from abyssal_animation_assets import setup
    rig=bpy.data.objects['AW_Rig'];setup(rig);clips=registry()
    for name,data in clips.items():
        bake_clip(rig,name,data);print('BAKED',name,flush=True)
    for key in ARMS:bake_detached(key)
    showcase=bake_showcase(rig,clips)
    manifest={'asset':'AbyssalWatcher','version':1,'fps':FPS,'status':'animation_preview',
              'clips':clips,'showcase':showcase,
              'excluded_actions':['player_grab','player_lift','player_throw'],
              'missing_limb_state':'AW_Rig.present_<limb>, 0=不存在；不写入任何动作',
              'preview_sever_state':'sever_<limb> 控制固定参考姿态断离演示',
              'integration':'Blender 动作预览；游戏事件、运行时断肢、FBX/Unity 尚未接入'}
    (SOURCE/'exports/animation-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2))
    general=json.loads((SOURCE/'exports/manifest.json').read_text())
    general.update(version=4,status='model_approved',animation_status='animation_preview',
                   clips={name:{k:d[k] for k in ('frame_start','frame_end','duration_seconds','loop')} for name,d in clips.items()},
                   integration=manifest['integration'])
    general.pop('grab',None);general['excluded_actions']=manifest['excluded_actions']
    (SOURCE/'exports/manifest.json').write_text(json.dumps(general,ensure_ascii=False,indent=2))
    activate_clip('AW_Showcase');bpy.context.scene.frame_set(135)
    scene=bpy.context.scene;scene.camera=bpy.data.objects['Camera_Arena']
    scene.camera.location=(24,-42,29);aim(scene.camera,(0,-2,3.7));scene.camera.data.ortho_scale=49
    scene.camera.data.passepartout_alpha=1.
    ring=bpy.data.collections['03_Arena_validation_12_to_20m'];ring.hide_viewport=False;ring.hide_render=False
    scene.render.resolution_x=960;scene.render.resolution_y=640
    for obj in scene.objects:obj.select_set(False)
    bpy.context.view_layer.objects.active=rig;rig.show_in_front=False
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_perspective='CAMERA'
                area.spaces.active.region_3d.view_camera_zoom=20.
                area.spaces.active.overlay.show_overlays=False
    # 源文件直接打开可空格播放合集；单片段入口写进内部说明，便于离线查看。
    text=bpy.data.texts.new('READ_ME_动作查看')
    text.write('已确认模型的第一版动作。空格播放 AW_Showcase 合集。\n无抓取、卷起、举起、抛掷玩家动作。\n'
      '单独预览：在 Python Console 执行\n'
      f'import sys; sys.path.insert(0, {str(ROOT / "scripts/blender")!r})\n'
      'from animate_abyssal_watcher import activate_clip, set_limb_present\nactivate_clip("AW_Slam_A01")\n'
      '可选动作和事件见同目录 exports/animation-manifest.json。\n'
      '断肢/墨汁为 Blender 演示，游戏逻辑与正式特效另行接入。\n')
    scene['animation_preview']='AW_Showcase';scene.render.fps=FPS
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'AbyssalWatcher.blend'))
    print('ANIMATION_SAVED',len(clips),'clips + showcase',flush=True)


if __name__=='__main__':build()
