"""深渊凝视者的稳定编号、尺寸与 bpy 小工具；坐标为 Z 上、-Y 正面。"""
from pathlib import Path
import math
import bpy
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'source-assets/creatures/abyssal-watcher'
EVIDENCE = ROOT / 'artifacts/abyssal-watcher'
REFERENCES = ROOT / 'docs/concepts/abyssal-watcher'
ARMS = {'A01':-65,'A02':-24,'A03':24,'A04':65,
        'S01':-115,'S02':-158,'S03':158,'S04':115}
FRONT = tuple(k for k in ARMS if k.startswith('A'))
FPS = 30

def enum(op, field, value):
    """查询本机 RNA，避免随 Blender 版本猜测枚举。"""
    values = {i.identifier for i in op.get_rna_type().properties[field].enum_items}
    if value not in values:
        raise ValueError(f'{field}={value}, supported={values}')
    return value

def direction(key):
    a = math.radians(ARMS[key])
    return Vector((math.sin(a),-math.cos(a),0))

def side(key):
    d = direction(key)
    return Vector((-d.y,d.x,0))

def root(key):
    return direction(key)*2.15 + Vector((0,0,1.85 if key in FRONT else 1.45))

def count(key):
    return 24 if key in FRONT else 12

def length(key):
    return 24.0 if key in FRONT else 18.0

def bone_edges(key):
    # 前半段承重，后半段加密以保持细末梢卷曲；沿用已确认模型的骨链，不包含抓取动作。
    if key in FRONT:return [1.55*i for i in range(13)]+[18.6+.45*i for i in range(1,13)]
    return [length(key)*i/count(key) for i in range(count(key)+1)]

def bone_name(key,index):
    return f'DEF_{key}_{index:02d}'

def radius(t):
    # 粗壮根部、长中段和细卷尖；不是等粗管。
    return .055 + .91 * (1-t)**.83 * (1+.14*math.sin(math.pi*t))

def aim(obj,target):
    obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()

def link_object(name,data,collection):
    obj=bpy.data.objects.new(name,data)
    collection.objects.link(obj)
    return obj

def material_node(mat):
    return next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')

def bind(obj,rig,bone=None):
    obj.parent=rig
    if bone:
        group=obj.vertex_groups.new(name=bone)
        group.add(list(range(len(obj.data.vertices))),1,'REPLACE')
    mod=obj.modifiers.new('Skin deformation','ARMATURE')
    mod.object=rig
    mod.use_deform_preserve_volume=True

class MeshBuilder:
    """一次构造多片封闭表面，避免数千次 bpy 操作；权重与 UV 随顶点保存。"""
    def __init__(self):
        self.vertices=[];self.faces=[];self.face_mats=[];self.uv=[];self.weights=[]

    def vertex(self,co,uv=(0,0),weights=None):
        self.vertices.append(tuple(co));self.uv.append(uv);self.weights.append(weights or {})
        return len(self.vertices)-1

    def face(self,ids,material=0):
        self.faces.append(tuple(ids));self.face_mats.append(material)

    def build(self,name,collection,materials,smooth=True):
        mesh=bpy.data.meshes.new(name+'Mesh');mesh.from_pydata(self.vertices,[],self.faces);mesh.update()
        obj=link_object(name,mesh,collection)
        for m in materials:mesh.materials.append(m)
        uv=mesh.uv_layers.new(name='UVMap')
        for p,mi in zip(mesh.polygons,self.face_mats):
            p.material_index=mi;p.use_smooth=smooth
            for li in p.loop_indices:uv.data[li].uv=self.uv[mesh.loops[li].vertex_index]
        groups={}
        for vi,weights in enumerate(self.weights):
            for name,value in weights.items():
                if value<.00001:continue
                if name not in groups:groups[name]=obj.vertex_groups.new(name=name)
                groups[name].add([vi],value,'REPLACE')
        return obj

    def ellipsoid(self,center,scale,material=0,weights=None,nu=24,nv=12,rotation=None):
        """封闭椭球，独立极点避免重合的整圈零面积面。"""
        c=Vector(center);rot=rotation or Matrix.Identity(3)
        top=self.vertex(c+rot@Vector((0,0,scale[2])),(.5,1),weights)
        rings=[]
        for j in range(1,nv):
            phi=math.pi*j/nv;row=[]
            for i in range(nu):
                a=math.tau*i/nu
                p=Vector((scale[0]*math.sin(phi)*math.cos(a),scale[1]*math.sin(phi)*math.sin(a),scale[2]*math.cos(phi)))
                row.append(self.vertex(c+rot@p,(i/nu,1-j/nv),weights))
            rings.append(row)
        bottom=self.vertex(c-rot@Vector((0,0,scale[2])),(.5,0),weights)
        for i in range(nu):
            n=(i+1)%nu
            self.face((top,rings[0][i],rings[0][n]),material)
            for a,b in zip(rings,rings[1:]):self.face((a[i],b[i],b[n],a[n]),material)
            self.face((bottom,rings[-1][n],rings[-1][i]),material)

    def tube(self,points,radii,material=0,weights=None,sides=12,cap=True):
        rows=[]
        for j,p in enumerate(points):
            p=Vector(p)
            tangent=Vector(points[min(j+1,len(points)-1)])-Vector(points[max(0,j-1)])
            tangent.normalize();axis=Vector((0,0,1))
            if abs(tangent.dot(axis))>.95:axis=Vector((0,1,0))
            x=tangent.cross(axis).normalized();y=tangent.cross(x).normalized()
            rows.append([self.vertex(p+radii[j]*(x*math.cos(math.tau*i/sides)+y*math.sin(math.tau*i/sides)),(i/sides,j/(len(points)-1)),weights) for i in range(sides)])
        for a,b in zip(rows,rows[1:]):
            for i in range(sides):n=(i+1)%sides;self.face((a[i],a[n],b[n],b[i]),material)
        if cap:self.face(tuple(reversed(rows[0])),material);self.face(rows[-1],material)

    def cup(self,center,normal,r,material=0,weights=None,segments=14):
        """真实吸盘：底座→外缘→厚唇→内壁→凹底，闭合网格。"""
        normal=Vector(normal).normalized();x=normal.cross(Vector((0,1,0)))
        if x.length<.1:x=normal.cross(Vector((1,0,0)))
        x.normalize();y=normal.cross(x).normalized();c=Vector(center)
        rows=[]
        for rad,h in [(0.65,0),(1,.18),(1.08,.38),(.87,.55),(.59,.42),(.43,.12)]:
            rows.append([self.vertex(c+r*(rad*(x*math.cos(math.tau*i/segments)+y*math.sin(math.tau*i/segments))+normal*h),(i/segments,rad*.8),weights) for i in range(segments)])
        self.face(tuple(reversed(rows[0])),material)
        for a,b in zip(rows,rows[1:]):
            for i in range(segments):n=(i+1)%segments;self.face((a[i],a[n],b[n],b[i]),material)
        bottom=self.vertex(c+normal*r*.10,(.5,.5),weights)
        for i in range(segments):self.face((rows[-1][i],rows[-1][(i+1)%segments],bottom),material)
