"""Game skeleton and normalized skinning for eight arms, oral tendrils and wings."""
from pathlib import Path
import math,bisect
import bpy
import numpy as np
from mathutils import Vector,Matrix
from mathutils.kdtree import KDTree
from .radial_arms import ANGLES
from .anatomy import beard_strands
from .geometry import P,spline,Mesh

KEYS=list(ANGLES)
def count(key):return 24 if key.startswith('A') else 12
def name(key,i):return f'DEF_{key}_{i:02d}'

def resample(points,n):
    points=[Vector(p) for p in points];ds=np.r_[0,np.cumsum([(b-a).length for a,b in zip(points,points[1:])])]
    result=[]
    for t in np.linspace(0,ds[-1],n):
        j=min(len(ds)-2,max(0,bisect.bisect_right(ds,t)-1))
        result.append(points[j].lerp(points[j+1],float((t-ds[j])/max(1e-8,ds[j+1]-ds[j]))))
    return result,ds

def fitted(points):
    path=Path(__file__).resolve().parents[3]/'source-assets/creatures/winged-cthulhu/review/silhouette-fit-field.npz'
    data=np.load(path)
    out=[]
    for p in points:
        x=np.clip((p.x/15.6+.5)*768,0,766.999);y=np.clip((.5-(p.z-5.12)/10.4)*512,0,510.999)
        ix=int(x);iy=int(y);fx=x-ix;fy=y-iy
        def at(a):return a[iy,ix]*(1-fx)*(1-fy)+a[iy,ix+1]*fx*(1-fy)+a[iy+1,ix]*(1-fx)*fy+a[iy+1,ix+1]*fx*fy
        out.append(p+Vector((at(data['u'])*15.6/1536,0,-at(data['v'])*10.4/1024)))
    return out

def bind(ob,rig):
    ob.parent=rig;mod=ob.modifiers.new('Game skin','ARMATURE');mod.object=rig;mod.use_deform_preserve_volume=False

def weights(ob,values):
    groups={}
    for vi,entry in enumerate(values):
        entry=sorted(((k,v) for k,v in entry.items() if v>=.01),key=lambda p:-p[1])[:4]
        total=sum(v for _,v in entry);assert total>0,(ob.name,vi)
        for key,val in entry:
            if key not in groups:groups[key]=ob.vertex_groups.new(name=key)
            groups[key].add([vi],val/total,'REPLACE')

def interval_weights(f,n,prefix):
    x=max(0,min(n-1,f*n-.5));i=int(x);t=x-i
    return {prefix(i):1-t,prefix(min(i+1,n-1)):t} if i<n-1 else {prefix(i):1.}

def build():
    scene=bpy.context.scene;data=bpy.data.armatures.new('WC_Skeleton');rig=bpy.data.objects.new('WC_Rig',data);scene.collection.objects.link(rig)
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode='EDIT')
    def bone(n,a,b,parent=None,deform=True):
        eb=data.edit_bones.new(n);eb.head=a;eb.tail=b;eb.use_deform=deform
        if parent:eb.parent=data.edit_bones[parent]
        return eb
    bone('ROOT',(0,0,0),(0,0,1),deform=False)
    bone('DEF_Body',(0,0,4.4),(0,0,6),'ROOT')
    bone('DEF_Head',(.45,.2,5.8),(.45,.2,8),'DEF_Body')
    bone('SOCKET_Ink',(.45,-1.8,6.25),(.45,-2.1,6.25),'DEF_Head',False)
    bone('SOCKET_Center',(0,0,5),(0,0,5.3),'ROOT',False)
    bone('SOCKET_Forward',(0,-2,5),(0,-2,5.3),'ROOT',False)
    arms={};radii={}
    for key in KEYS:
        ob=bpy.data.objects['AW_Arm_'+key];points,dist=resample(ob['centerline'],count(key)+1)
        arms[key]=points;radii[key]=np.interp(np.linspace(0,dist[-1],count(key)+1),dist,np.array(ob['centerline_radii'])).tolist()
        bone('CTRL_'+key,points[0],points[0]+Vector((0,0,.5)),'ROOT',False)
        for i,(a,b) in enumerate(zip(points,points[1:])):bone(name(key,i),a,b,'CTRL_'+key)
    oral={}
    for index,(_,knots) in enumerate(beard_strands()):
        pts=fitted(spline([P(*k[:3]) for k in knots],14));pts,_=resample(pts,8);oral[index]=pts
        for i,(a,b) in enumerate(zip(pts,pts[1:])):bone(f'DEF_Mouth_{index:02d}_{i:02d}',a,b,'DEF_Head')
    for side,sign in [('L',-1),('R',1)]:
        wrist=Vector((sign*3.2,1.05,9.5));shoulder=Vector((sign*1.8,.9,6.4))
        bone('DEF_Wing_'+side,shoulder,wrist,'DEF_Body')
        bone('DEF_WingTip_'+side,wrist,(sign*6.7,1.2,6.2),'DEF_Wing_'+side)
    eye=bpy.data.objects['AW_Eye_Center'];c=sum((v.co for v in eye.data.vertices),Vector())/len(eye.data.vertices)
    bone('DEF_Eye_Center',c,c+Vector((0,0,.35)),'DEF_Head')
    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in rig.pose.bones:pb.rotation_mode='QUATERNION'
    # Eight independent skin meshes preserve authoritative limb severing.
    for key in KEYS:
        ob=bpy.data.objects['AW_Arm_'+key];dense=np.array(ob['centerline']);dist=np.r_[0,np.cumsum(np.linalg.norm(np.diff(dense,axis=0),axis=1))]
        attr=ob.data.attributes['ArmParameter'];params=np.empty(len(ob.data.vertices),np.float32);attr.data.foreach_get('value',params)
        f=np.interp(params,np.linspace(0,1,len(dist)),dist)/dist[-1]
        weights(ob,[interval_weights(float(t),count(key),lambda i:name(key,i)) for t in f]);bind(ob,rig)
    # Nearest authored oral centre lines transfer smoothly onto the fused sculpt.
    samples=[]
    for idx,pts in oral.items():
        dense,_=resample(pts,100)
        samples += [(p,idx,j/99) for j,p in enumerate(dense)]
    tree=KDTree(len(samples))
    for i,(p,_,_) in enumerate(samples):tree.insert(p,i)
    tree.balance()
    def wing_weight(p,side):
        sign=-1 if side=='L' else 1;wrist=Vector((sign*3.2,1.05,9.5))
        t=max(0,min(1,(p-wrist).length/3.5))*.72
        return {'DEF_Wing_'+side:1-t,'DEF_WingTip_'+side:t}
    body=bpy.data.objects['AW_Head'];values=[]
    for v in body.data.vertices:
        p=v.co
        if abs(p.x)>2.05 and p.y>.48 and p.z>5.3:
            entry=wing_weight(p,'L' if p.x<0 else 'R')
            w=max(0,min(1,(abs(p.x)-2.05)/.75));entry={k:val*w for k,val in entry.items()};entry['DEF_Head']=1-w
        else:
            _,idx,d=tree.find(p);_,strand,f=samples[idx]
            w=max(0,min(1,(.55-d)/.22))*max(0,min(1,f/.16)) if p.y<-.8 and p.z<6.8 else 0
            entry=interval_weights(f,7,lambda i:f'DEF_Mouth_{strand:02d}_{i:02d}') if w>0 else {}
            entry={k:val*w for k,val in entry.items()}
            h=max(0,min(1,(p.z-4.7)/1.2));entry['DEF_Head']=(1-w)*h;entry['DEF_Body']=(1-w)*(1-h)
        values.append(entry)
    weights(body,values);bind(body,rig)
    for side in ('L','R'):
        ob=bpy.data.objects['WC_Wing_'+side];weights(ob,[wing_weight(v.co,side) for v in ob.data.vertices]);bind(ob,rig)
    weights(eye,[{'DEF_Eye_Center':1} for v in eye.data.vertices]);bind(eye,rig)
    stumpmat=bpy.data.materials.new('WC_Stump');p=next(n for n in stumpmat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value=(.21,.07,.12,1);p.inputs['Roughness'].default_value=.43
    for key in KEYS:
        a=math.radians(ANGLES[key]);g=Mesh();g.ellipsoid((math.sin(a)*1.72,-math.cos(a)*1.72,4.34),(.54,.54,.54),24,16)
        ob=g.finish('AW_Stump_'+key,scene.collection,[stumpmat]);weights(ob,[{'DEF_Body':1} for v in ob.data.vertices]);bind(ob,rig);ob.hide_render=True
    rig['arm_reference']={k:[list(p) for p in pts] for k,pts in arms.items()}
    rig['arm_radii']=radii;rig['oral_reference']={str(k):[list(p) for p in pts] for k,pts in oral.items()}
    rig['coordinate_system']='metres, Z up, -Y front; game is (x,z,y)'
    return rig
