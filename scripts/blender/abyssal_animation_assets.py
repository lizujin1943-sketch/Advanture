"""动画表现附件：脸部驱动、断肢副本和喷墨占位预览，不包含游戏结算。"""
import math
import bpy
from mathutils import Vector, Matrix
from abyssal_common import ARMS, MeshBuilder, material_node
from abyssal_face import EYE_LAYOUT, base_surface, mouth_metric, MOUTH
from abyssal_actions import sever_pose, set_arm
from abyssal_stage import simple_mat


def driver(owner, path, rig, properties, expression, index=None):
    """所有表现驱动只读骨架属性；present_* 从不写入动作，交给外部状态管理。"""
    curve=owner.driver_add(path) if index is None else owner.driver_add(path,index)
    d=curve.driver;d.type='SCRIPTED';d.expression=expression
    for alias,prop in properties.items():
        v=d.variables.new();v.name=alias;v.type='SINGLE_PROP'
        v.targets[0].id=rig;v.targets[0].data_path='["'+prop+'"]'
    return curve


def setup_controls(rig):
    for key in ARMS:
        rig['present_'+key]=1.0;rig['sever_'+key]=0.0
        rig.id_properties_ui('present_'+key).update(min=0,max=1,description='此腕是否仍存在；不由动画恢复')
        for prefix in ('AW_Arm_','AW_Membrane_'):
            obj=bpy.data.objects[prefix+key]
            for path in ('hide_render','hide_viewport'):
                driver(obj,path,rig,{'present':'present_'+key,'sever':'sever_'+key},'present < 0.5 or sever > 0.5')
    for prop,val in [('eye_glow',1.),('mouth_open',.08),('ink_fire',0.),('blink_Center',0.),('blink_L',0.),('blink_R',0.)]:
        rig[prop]=val;rig.id_properties_ui(prop).update(min=0,max=3 if prop=='eye_glow' else 1)
    rig['animation_note']='30 fps 美术片段；攻击事件仅作时间标记，不结算伤害。无抓取或抛掷玩家动作。'


def setup_face(rig):
    """在已确认的基础网格上加形态键，原始 Basis 不改动；头口边界使用同一函数。"""
    head=bpy.data.objects['AW_Head']
    for obj in (head,bpy.data.objects['AW_MouthCavity']):
        obj.shape_key_add(name='Basis')
        key=obj.shape_key_add(name='Ink_Aperture')
        for v,k in zip(obj.data.vertices,key.data):
            x,y,z=v.co
            if y>1 or abs(x)>2.1 or not 1.6<z<5.15:continue
            r,_=mouth_metric(x,z)
            w=math.exp(-(r/1.23)**8)
            k.co.x+=x*.23*w;k.co.z+=(z-3.4)*.20*w
        driver(key,'value',rig,{'v':'mouth_open'},'v')
    for eye,(ex,ez,w,h,angle) in EYE_LAYOUT.items():
        key=head.shape_key_add(name='Close_'+eye)
        c=math.cos(angle);s=math.sin(angle)
        for v,k in zip(head.data.vertices,key.data):
            x,y,z=v.co
            if y>0 or abs(x-ex)>w*1.8 or abs(z-ez)>h*1.8:continue
            u=c*(x-ex)-s*(z-ez);q=s*(x-ex)+c*(z-ez)
            r=math.sqrt((u/w)**2+(q/h)**2)
            f=math.exp(-(r/.98)**8)
            k.co.y+=(base_surface(x,z)-.035-y)*f
        driver(key,'value',rig,{'v':'blink_'+eye},'v')
        # 角膜退入闭合的皮肤，不留下悬在眼睑前面的发光面。
        p=rig.pose.bones['DEF_Eye_'+eye]
        delta=p.bone.matrix_local.to_3x3().inverted()@Vector((0,.65,0))
        for i in range(3):driver(p,'location',rig,{'v':'blink_'+eye},f'v*{delta[i]}',i)
    for i in range(12):
        a=math.tau*i/12;p=rig.pose.bones[f'DEF_Petal_{i:02d}']
        delta=p.bone.matrix_local.to_3x3().inverted()@Vector((math.cos(a)*.25,.12,math.sin(a)*.25))
        for j in range(3):driver(p,'location',rig,{'v':'mouth_open'},f'v*{delta[j]}',j)
    mat=bpy.data.objects['AW_Eye_Center'].data.materials[0]
    node=material_node(mat)
    driver(node.inputs['Emission Strength'],'default_value',rig,{'v':'eye_glow'},'.70*v')


def setup_detached(rig):
    """独立世界空间骨架保留断开瞬间参考弯曲；后续下沉不继承 BOSS 位置。"""
    collection=bpy.data.collections.new('04_DetachedLimbs_EXPORT');bpy.context.scene.collection.children.link(collection)
    for key in ARMS:
        detached=rig.copy();detached.data=rig.data.copy();detached.animation_data_clear()
        detached.name='AW_Detached_'+key;detached.parent=None;detached.matrix_world=Matrix.Identity(4)
        collection.objects.link(detached);detached.show_in_front=False
        # 只保留该腕的骨链和祖先。避免每个断肢都携带整套 189 根骨骼。
        keep={'ROOT','CTRL_'+key}|{b.name for b in detached.data.bones if b.name.startswith('DEF_'+key+'_')}
        bpy.context.view_layer.objects.active=detached;detached.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT')
        for bone in list(detached.data.edit_bones):
            if bone.name not in keep:detached.data.edit_bones.remove(bone)
        bpy.ops.object.mode_set(mode='OBJECT');detached.select_set(False)
        set_arm(detached,key,sever_pose(key))
        detached['limb_id']=key;detached['preview_boundary']='固定参考姿态断离；运行时需复制当前腕姿态'
        for prefix in ('AW_Arm_','AW_Membrane_'):
            original=bpy.data.objects[prefix+key];obj=original.copy();obj.animation_data_clear()
            obj.name='AW_Debris_'+('Membrane_' if 'Membrane' in prefix else 'Arm_')+key
            collection.objects.link(obj);obj.parent=detached;obj.matrix_parent_inverse=Matrix.Identity(4)
            for modifier in obj.modifiers:
                if modifier.type=='ARMATURE':modifier.object=detached
            for path in ('hide_render','hide_viewport'):
                driver(obj,path,rig,{'v':'sever_'+key},'v < 0.5')


def setup_ink(rig):
    """仅示意口器出口、喷射方向和启停时刻；不是游戏中的墨汁碰撞体。"""
    collection=bpy.data.collections.new('05_AnimationPreview_ONLY');bpy.context.scene.collection.children.link(collection)
    mat=simple_mat('Preview_Ink',(.009,.017,.025),.29)
    b=MeshBuilder()
    # 小团液滴沿前向喷流分布，末段受重力向下，避免烟柱穿过海面后还发光。
    for i in range(34):
        f=i/33;d=1.0+f*8.5
        c=MOUTH+Vector((math.sin(i*2.4)*(.12+f*.70),-d,.12-f*f*2.8))
        r=.10+f*.13
        b.ellipsoid(c,(r,r*(2.4-f),r*.8),nu=12,nv=8)
    obj=b.build('Preview_InkJet',collection,[mat]);obj['preview_only']=True
    # 整段只在吐墨阶段显示；不让预览粒子进入正式 BOSS 导出集合。
    for p in ('hide_render','hide_viewport'):driver(obj,p,rig,{'v':'ink_fire'},'v < 0.025')
    for i in range(3):driver(obj,'scale',rig,{'v':'ink_fire'},'0.85+0.15*v',i)
    return obj


def setup(rig):
    setup_controls(rig);setup_face(rig);setup_detached(rig);setup_ink(rig)
