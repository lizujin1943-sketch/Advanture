"""只用于美术检查的海面、环台、真实角色与相机，不属于游戏资源导出。"""
import math
import bpy
from mathutils import Vector
from abyssal_common import *

def simple_mat(name,color,rough=.7,metal=0):
    m=bpy.data.materials.new(name);m.use_nodes=True;m.diffuse_color=(*color,1)
    p=material_node(m);p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal
    return m

def box(b,c,size,mi=0):
    c=Vector(c);x,y,z=[v*.5 for v in size]
    ids=[b.vertex(c+Vector((a*x,d*y,e*z))) for a,d,e in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    for face in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]:b.face([ids[i] for i in face],mi)

def setup(scene):
    scene.render.engine='BLENDER_EEVEE'
    scene.render.fps=FPS;scene.frame_start=1;scene.frame_end=181
    scene.render.resolution_x=1440;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.film_transparent=False
    scene.world=bpy.data.worlds.new('AW_MarineWorld');scene.world.use_nodes=True
    world=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
    world.inputs['Color'].default_value=(.075,.115,.14,1);world.inputs['Strength'].default_value=.40
    scene.view_settings.view_transform='AgX'
    stage=bpy.data.collections.new('02_Sea_and_scale_reference');scene.collection.children.link(stage)
    ring=bpy.data.collections.new('03_Arena_validation_12_to_20m');scene.collection.children.link(ring)
    water=simple_mat('Preview_Sea',(.022,.071,.083),.30,.20)
    b=MeshBuilder();rows=[];n=100
    for j in range(n+1):
        y=-55+110*j/n;row=[]
        for i in range(n+1):
            x=-55+110*i/n;z=-.22+.065*math.sin(x*.7+y*.3)+.04*math.sin(y*1.8-x*.2)
            row.append(b.vertex((x,y,z),(i/n,j/n)))
        rows.append(row)
    for a,c in zip(rows,rows[1:]):
        for i in range(n):b.face((a[i],a[i+1],c[i+1],c[i]))
    sea=b.build('Preview_Sea',stage,[water])
    foam=simple_mat('Preview_Foam',(.26,.43,.42),.62)
    b=MeshBuilder()
    for radius0 in [4.9,7.4,10.5,13.5]:
        for seg in range(9):
            a0=seg*math.tau/9;pts=[]
            for i in range(14):
                a=a0+i*.035;r=radius0+.18*math.sin(a*9)
                pts.append((r*math.cos(a),r*math.sin(a),-.085+.012*math.sin(a*11)))
            b.tube(pts,[.018+.01*math.sin(i*.4)**2 for i in range(14)],sides=5)
    b.build('Preview_WaterRipples',stage,[foam])
    wood=[simple_mat('Preview_Deck_'+str(i),(.18+i*.018,.15+i*.016,.10+i*.012)) for i in range(4)]
    b=MeshBuilder()
    for ix in range(-10,10):
        for iy in range(-10,10):
            x=ix*2+1;y=iy*2+1;r=math.hypot(x,y)
            if 12<=r<=20:
                for j in range(4):box(b,(x-.75+j*.5,y,.42),(.48,1.97,.30),(ix+iy+j)%4)
    deck=b.build('Preview_RingPlatform',ring,wood,False)
    bevel=deck.modifiers.new('Small plank edge bevel','BEVEL');bevel.width=.035;bevel.segments=1
    ring.hide_render=True;ring.hide_viewport=True
    # 主画面只有短小栈桥和真实角色，保持原画的巨大体量对照。
    b=MeshBuilder()
    for j in range(8):box(b,(-10,-12+j*.65,.42),(2.2,.62,.30),j%4)
    for x in [-11,-9]:
        for y in [-12,-8]:box(b,(x,y,-.25),(.22,.22,2.0),2)
    b.build('Preview_ScalePier',stage,wood,False)
    fisher=append_fisher(stage)
    fisher.location=(-10,-9,.60);fisher.rotation_euler.z=math.radians(-30)
    cameras={
        'Hero':((21,-36,18),(0,0,4),34),
        'Front':((0,-40,5),(0,0,4.3),30),
        'Back':((0,40,7),(0,0,4),30),
        'Left':((40,0,6),(0,0,4),31),
        'Right':((-40,0,6),(0,0,4),31),
        'Top':((0,0,48),(0,0,0),40),
        'Bottom':((0,0,-42),(0,0,2),40),
        'Face':((.5,-18,6.4),(0,-1.5,5.6),13),
        'FaceThreeQuarter':((11,-18,11),(0,1.3,5.8),15.1),
        'HeadSide':((-24,2.5,7),(0,2.5,5.4),15.3),
        'MouthDetail':((4.0,-15,5.4),(0,-2.0,3.6),6.7),
        'Arena':((33,-42,35),(0,0,1),54),
        'FirstPerson':((0,-12.8,1.68),(0,0,5.2),None),
    }
    for name,(pos,target,scale) in cameras.items():
        data=bpy.data.cameras.new('Camera_'+name);cam=link_object('Camera_'+name,data,stage)
        cam.location=pos;aim(cam,target)
        if scale:data.type='ORTHO';data.ortho_scale=scale
        else:data.type='PERSP';data.lens=28
        data.clip_end=300
    scene.camera=bpy.data.objects['Camera_Hero']
    for name,pos,power,color,size in [
        ('Key',(-12,-14,23),10500,(.78,.88,1),13),
        ('Fill',(15,-10,11),6400,(.46,.80,.78),10),
        ('Rim',(5,12,18),14500,(.82,.70,.47),11),
        ('Face',(-4,-14,9),1300,(.65,.84,.79),6)]:
        data=bpy.data.lights.new('Light_'+name,'AREA');data.energy=power;data.color=color;data.shape='DISK';data.size=size
        if name in ('Face','Fill'):data.specular_factor=.10
        obj=link_object('Light_'+name,data,stage);obj.location=pos;aim(obj,(0,0,4))
    return stage,ring,fisher

def append_fisher(collection):
    """追加仓库角色，保留原尺寸和真实背包轮廓；不生成替代小人。"""
    with bpy.data.libraries.load(str(ROOT/'source-assets/characters/fisher/Fisher.blend'),link=False) as (src,dst):
        dst.objects=[n for n in src.objects if n in ('Fisher Rig','Fisher Body','Backpack')]
    objects=[o for o in dst.objects if o]
    for o in objects:collection.objects.link(o);o.name='Preview_'+o.name
    rig=next(o for o in objects if o.type=='ARMATURE')
    rig.animation_data_clear()
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    for obj in objects:
        if obj!=rig and not obj.parent:obj.parent=rig
    rig['reference_height_m']=1.061
    return rig

def set_view(scene,name,isolated=False):
    scene.camera=bpy.data.objects['Camera_'+name]
    bpy.data.collections['03_Arena_validation_12_to_20m'].hide_render=name in ('Hero','Face','FaceThreeQuarter','HeadSide','MouthDetail','Front','Back','Left','Right','Top','Bottom')
    for n in ('Preview_Sea','Preview_WaterRipples','Preview_ScalePier'):
        bpy.data.objects[n].hide_render=isolated or name in ('Bottom',)
    for o in bpy.data.collections['02_Sea_and_scale_reference'].objects:
        if o.name.startswith('Preview_Fisher') or o.name=='Preview_Backpack':o.hide_render=isolated
    scene.render.filepath=str(EVIDENCE/(name.lower()+'.png'))
