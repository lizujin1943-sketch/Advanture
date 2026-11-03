"""重新打开动画源文件检查真实骨骼运动、分区、循环与断肢独立性。"""
from pathlib import Path
import json
import math
import sys
import bpy
from mathutils import Matrix

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'source-assets/creatures/abyssal-watcher'
OUT=ROOT/'artifacts/abyssal-watcher'
LIMBS=('A01','A02','A03','A04','S01','S02','S03','S04')

def curves(action):
    return [f for layer in action.layers for strip in layer.strips
            for bag in strip.channelbags for f in bag.fcurves]

def verify():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'AbyssalWatcher.blend'))
    required=['AW_Idle','AW_Emerge','AW_Ink','AW_Hit','AW_Enrage','AW_EnragedIdle','AW_Death']
    required += ['AW_'+move+'_'+k for move in ('Slam','Sweep') for k in LIMBS[:4]]
    required += ['AW_Sever_'+k for k in LIMBS]
    missing=[n for n in required if bpy.data.actions.get(n) is None]
    assert not missing, '缺少动作：'+', '.join(missing)
    assert not [a.name for a in bpy.data.actions if a.name.startswith('AW_') and any(w in a.name for w in ('Grab','Grip','Throw'))], '本轮不得制作抓取、卷起或抛掷玩家'
    sys.path.insert(0,str(ROOT/'scripts/blender'))
    from animate_abyssal_watcher import activate_clip, set_limb_present
    manifest=json.loads((SOURCE/'exports/animation-manifest.json').read_text())
    rig=bpy.data.objects['AW_Rig'];scene=bpy.context.scene
    assert manifest['excluded_actions']==['player_grab','player_lift','player_throw']
    def matrices():return {p.name:p.matrix.copy() for p in rig.pose.bones}
    def max_error(a,b):return max(abs(x-y) for aa,bb in zip(a,b) for x,y in zip(aa,bb))
    def evaluate(frame):scene.frame_set(frame);bpy.context.view_layer.update()
    for name in required:
        action=bpy.data.actions[name];assert curves(action),name+' 没有关键帧'
        data=manifest['clips'][name]
        assert data['frame_end']>1
        for event in data.get('events',[]):
            assert 1<=event['frame']<=data['frame_end']
            assert abs(event['seconds']-(event['frame']-1)/30)<1e-5
        assert not any('present_' in f.data_path for f in curves(action)),name+' 不应修改触手存在状态'
    # 实际播放后的属性和形态键都必须有中间值，F-curve 存在不代表没有整数化。
    activate_clip('AW_Emerge');evaluate(73)
    assert isinstance(rig['eye_glow'],float) and .2<rig['eye_glow']<.5,'渐亮被整数属性量化'
    activate_clip('AW_Idle');evaluate(74)
    blink=rig['blink_Center'];shape=bpy.data.objects['AW_Head'].data.shape_keys.key_blocks['Close_Center'].value
    assert .2<blink<.8 and abs(shape-blink)<1e-5,('眨眼没有连续过渡',blink,shape)
    activate_clip('AW_Enrage');evaluate(91)
    assert abs(rig['eye_glow']-2.6)<1e-4,'狂暴发光强度被截断'
    emission=next(n for n in bpy.data.objects['AW_Eye_Center'].data.materials[0].node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Emission Strength'].default_value
    assert abs(emission-1.82)<1e-4,('眼睛材质未跟随渐亮',emission)
    loop_errors={}
    for name in ('AW_Idle','AW_EnragedIdle'):
        activate_clip(name);evaluate(1);start=matrices()
        evaluate(manifest['clips'][name]['frame_end']);end=matrices()
        err=max(max_error(start[k],end[k]) for k in start)
        assert err<1e-4,(name,'循环首尾跳变',err)
        loop_errors[name]=err
        evaluate(46);assert max(max_error(start[k],p.matrix) for k,p in rig.pose.bones.items())>.03, '待机实际没有运动'
    reach={}
    for move in ('Slam','Sweep'):
        for k in LIMBS[:4]:
            name='AW_'+move+'_'+k
            assert all(f.data_path.startswith('pose.bones["DEF_'+k+'_') for f in curves(bpy.data.actions[name])), name+' 覆盖了其他部件'
            activate_clip(name);evaluate(1);before=matrices()
            frame=46 if move=='Slam' else 49;evaluate(frame)
            for p in rig.pose.bones:
                if not p.name.startswith('DEF_'+k+'_'):
                    assert max_error(before[p.name],p.matrix)<1e-4,(name,'干扰其他骨骼',p.name)
            points=[p.tail for p in rig.pose.bones if p.name.startswith('DEF_'+k+'_')]
            furthest=max((p.x*p.x+p.y*p.y)**.5 for p in points)
            assert furthest>19.5,(name,'无法覆盖平台外侧',furthest)
            reach[name]=round(furthest,3)
            assert max(max_error(before[p.name],p.matrix) for p in rig.pose.bones)>.5,name+' 实际没有攻击运动'
    for k in LIMBS:
        activate_clip('AW_Sever_'+k);evaluate(1)
        attached=bpy.data.objects['AW_Arm_'+k];debris=bpy.data.objects['AW_Detached_'+k]
        assert not attached.hide_render
        evaluate(10);assert attached.hide_render, k+' 没有从本体消失'
        assert debris.parent is None,k+' 断肢仍挂在本体上'
        original=debris.matrix_world.copy()
        rig.pose.bones['ROOT'].location.y+=5;bpy.context.view_layer.update()
        assert max_error(original,debris.matrix_world)<1e-5,k+' 断肢继续跟着本体'
        evaluate(79);assert debris.location.z<-12,k+' 断肢没有下沉'
    activate_clip('AW_Death');evaluate(1)
    # 残缺状态由独立掩码保留；死亡片段不能重置为完整八腕。
    set_limb_present('A02',False)
    assert bpy.data.objects['AW_Arm_A02'].hide_render
    evaluate(181)
    assert bpy.data.objects['AW_Arm_A02'].hide_render,'死亡使断肢重新出现'
    assert rig.pose.bones['ROOT'].matrix.translation.z<-12,'死亡未沉入海面'
    assert rig['eye_glow']<.01,'死亡后眼睛仍发光'
    # 全部攻击腕损毁也不能让喷墨动作将其重新显示。
    for k in LIMBS[:4]:set_limb_present(k,False)
    activate_clip('AW_Ink',preserve_missing=True);evaluate(44)
    assert all(bpy.data.objects['AW_Arm_'+k].hide_render for k in LIMBS[:4])
    # 合集切段处的非攻击触手应连续；检验真实骨尾位置而非只看关键帧个数。
    activate_clip('AW_Showcase')
    transition_errors={}
    for a,b in [(180,181),(654,655)]:
        evaluate(a);pa=rig.pose.bones['DEF_A04_23'].tail.copy();evaluate(b)
        error=(rig.pose.bones['DEF_A04_23'].tail-pa).length
        assert error<.1,('合集切换发生跳动',a,b,error)
        transition_errors[str(b)]=error
    # 端点平滑不等于吸盘截面平滑；覆盖曾经跨越世界竖直轴而翻半圈的真实帧。
    roll_errors={}
    for name,bone,a,b in [('AW_Slam_A04','DEF_A04_12',73,74),
                          ('AW_Sweep_A02','DEF_A02_20',16,17),
                          ('AW_Sweep_A03','DEF_A03_20',16,17),
                          ('AW_Enrage','DEF_A01_17',14,15)]:
        activate_clip(name);evaluate(a);q=rig.pose.bones[bone].matrix.to_quaternion()
        evaluate(b);q2=rig.pose.bones[bone].matrix.to_quaternion()
        angle=math.degrees(2*math.acos(min(1,abs(q.dot(q2)))))
        assert angle<25,(name,bone,'截面突转',angle)
        roll_errors[name]=angle
    # 覆盖完整攻击过程，防止消除旧极点时在其他时间或其他腕引入新的翻转。
    max_attack_rotation={}
    for move in ('Slam','Sweep'):
        for k in LIMBS[:4]:
            name='AW_'+move+'_'+k;activate_clip(name);previous={};largest=0.
            for frame in range(1,manifest['clips'][name]['frame_end']+1):
                evaluate(frame)
                for p in rig.pose.bones:
                    if not p.name.startswith('DEF_'+k+'_'):continue
                    q=p.matrix.to_quaternion()
                    if p.name in previous:
                        angle=math.degrees(2*math.acos(min(1,abs(previous[p.name].dot(q)))))
                        largest=max(largest,angle)
                        assert angle<45,(name,frame,p.name,'攻击截面突转',angle)
                    previous[p.name]=q
            max_attack_rotation[name]=largest
    missing_images=[i.filepath for i in bpy.data.images if i.source=='FILE' and not i.packed_file and not Path(bpy.path.abspath(i.filepath)).exists()]
    assert not missing_images,missing_images
    report={'contract':'PASS','production_clips':len(required),'loop_matrix_error':loop_errors,
            'attack_reach_m':reach,'independent_detached_limbs':list(LIMBS),'showcase_transition_error_m':transition_errors,
            'continuous_face_controls':'PASS',
            'previous_roll_failure_rotation_degrees':roll_errors,
            'max_attack_frame_rotation_degrees':max_attack_rotation,
            'missing_textures':missing_images,'excluded_actions':manifest['excluded_actions'],
            'scope':'Blender 动作、分区和静态断肢演示；不包含正式游戏伤害、平台破坏或 FBX'}
    (OUT/'animation-verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
    print(json.dumps(report,ensure_ascii=False))

if __name__=='__main__':verify()
