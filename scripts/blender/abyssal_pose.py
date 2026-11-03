"""已确认参考姿态、制作骨架和骨链姿态换算；动作关键帧由独立制作入口生成。"""
import bisect
import math
import bpy
from mathutils import Vector, Matrix, Quaternion
from abyssal_common import *
from abyssal_geometry import EYES, MOUTH

def catmull(points,n=220):
    """对创作控制点平滑采样，随后按弧长取骨头位置，避免稀密控制点压扁骨节。"""
    points=[Vector(x) for x in points];dense=[]
    for j in range(n+1):
        f=j/n*(len(points)-1);i=min(len(points)-2,int(f));t=f-i
        p0=points[max(0,i-1)];p1=points[i];p2=points[i+1];p3=points[min(len(points)-1,i+2)]
        dense.append((2*p1+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t)*.5)
    return dense

def resample(points,fractions):
    dense=catmull(points);dist=[0.0]
    for a,b in zip(dense,dense[1:]):dist.append(dist[-1]+(b-a).length)
    out=[]
    for t in fractions:
        target=t*dist[-1];j=min(len(dist)-2,max(0,bisect.bisect_right(dist,target)-1))
        out.append(dense[j].lerp(dense[j+1],(target-dist[j])/max(.00001,dist[j+1]-dist[j])))
    return out

def world_path(key,points,azimuth=0):
    d=direction(key);s=side(key);rot=Matrix.Rotation(azimuth,3,'Z')
    result=[rot@(d*r+s*off)+Vector((0,0,z)) for r,z,off in points]
    result[0]=root(key)
    return result

def idle_path(key,phase=0):
    raised=key in ('A01','A04')
    if raised:
        pts=[(2.15,1.85,0),(4,3.0,0),(6.4,3.8,.2),(9,5.7,.5),(10.6,8.9,.4),
             (10.4,10.7,0),(8.8,10.8,-.3),(8.1,9.5,-.4),(8.8,8.7,-.2),(9.8,9.15,0)]
        if key=='A04':pts=[(r,z*.85 if j>1 else z,o+.3*math.sin(j)) for j,(r,z,o) in enumerate(pts)]
    elif key in FRONT:
        pts=[(2.15,1.85,0),(4.3,2.5,0),(7.2,3.7,.5),(10.3,3.8,1),(12.8,2.1,1.3),
             (13.2,.1,1),(12.0,-.5,-.2),(10.4,.1,-1.2),(10.6,1.7,-2.1),(12,2.1,-2.3)]
    else:
        pts=[(2.15,1.45,0),(4.1,1.8,.3),(6.7,2.2,.7),(9.5,1.2,.2),(11,.3,-.6),
             (11.1,1.1,-1.7),(10,3,-2),(8.9,3.1,-1.7),(8.9,1.9,-1)]
    idx=list(ARMS).index(key)
    # sin(phase) 在首尾相同，且根部位置固定。
    return world_path(key,[(r,z+math.sin(phase+idx*.7)*.12*(j/(len(pts)-1)),o) if j else (r,z,o) for j,(r,z,o) in enumerate(pts)])

def sample_curve(key,points):return resample(points,[x/length(key) for x in bone_edges(key)])

def create_rig(collection,objects):
    data=bpy.data.armatures.new('AW_Skeleton');rig=link_object('AW_Rig',data,collection)
    bpy.context.view_layer.objects.active=rig;rig.select_set(True)
    bpy.ops.object.mode_set(mode=enum(bpy.ops.object.mode_set,'mode','EDIT'))
    def bone(name,head,tail,parent=None,deform=True):
        b=data.edit_bones.new(name);b.head=head;b.tail=tail;b.use_deform=deform
        if parent:b.parent=data.edit_bones[parent]
        return b
    bone('ROOT',(0,0,0),(0,0,1),deform=False)
    bone('DEF_Body',(0,0,1),(0,0,3),'ROOT')
    bone('DEF_Head',(0,0,3),(0,0,7),'DEF_Body')
    for key in ARMS:
        r=root(key);d=direction(key);edges=bone_edges(key)
        bone('CTRL_'+key,r,r+Vector((0,0,.6)),'ROOT',False)
        bone('SOCKET_Sever_'+key,r,r+d*.4,'DEF_Body',False)
        for i in range(count(key)):
            bone(bone_name(key,i),r+d*edges[i],r+d*edges[i+1],'CTRL_'+key if i==0 else bone_name(key,i-1))
        if key in FRONT:bone('SOCKET_Grip_'+key,d*14.3+Vector((0,0,1.17)),d*14.3+Vector((0,0,1.57)),'ROOT',False)
    bone('SOCKET_Ink',MOUTH,MOUTH+Vector((0,-1,0)),'DEF_Head',False)
    for key,(c,w,h,angle) in EYES.items():
        bone('DEF_Eye_'+key,c,c+Vector((0,0,.4)),'DEF_Head')
        for sign in [-1,1]:bone(f'DEF_Lid_{key}_{sign}',c,c+Vector((0,0,.4)),'DEF_Head')
    for i in range(12):
        a=math.tau*i/12;rad=Vector((math.cos(a),0,math.sin(a)))
        bone(f'DEF_Petal_{i:02d}',MOUTH+rad*1.08,MOUTH+rad*.3,'DEF_Head')
    bpy.ops.object.mode_set(mode=enum(bpy.ops.object.mode_set,'mode','OBJECT'))
    for b in data.bones:
        if b.name.startswith('DEF_A') or b.name.startswith('DEF_S'):b.inherit_scale='NONE'
    for o in objects:bind(o,rig)
    for p in rig.pose.bones:p.rotation_mode='QUATERNION'
    rig.show_in_front=True;rig['front_axis']='-Y';rig['units']='meters'
    return rig

def set_arm(rig,key,points,rotations=None):
    """父到子换算姿态。静态摆姿沿用原截面；动画传入连续截面方向，避免竖直翻腕。"""
    edges=bone_edges(key)
    parent_matrix=rig.data.bones['CTRL_'+key].matrix_local
    for i,(a,b) in enumerate(zip(points,points[1:])):
        delta=b-a
        rot=(rotations[i] if rotations is not None else delta.to_track_quat('Y','Z')).to_matrix().to_4x4()
        stretch=delta.length/(edges[i+1]-edges[i])
        matrix=Matrix.Translation(a)@rot@Matrix.Diagonal((1,stretch,1,1))
        pb=rig.pose.bones[bone_name(key,i)]
        pb.matrix_basis=pb.bone.convert_local_to_pose(matrix,pb.bone.matrix_local,
            parent_matrix=parent_matrix,parent_matrix_local=pb.parent.bone.matrix_local,invert=True)
        parent_matrix=matrix

def reset(rig):
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)

def pose_reference(rig):
    """按原画摆放八条腕，仅修改静态姿态，不创建动画。"""
    reset(rig)
    for key in ARMS:
        set_arm(rig,key,sample_curve(key,idle_path(key,0)))
