"""用 Blender 生成「七号转运站」简单低多边形地图。

只在独立的后台 Blender 进程运行，不修改正在使用的 Blender 场景。
单位为米，Blender 中 X/Y 是地面、Z 向上。地图没有游戏逻辑或引擎依赖。
运行方法见同目录上一级 README.md。模型、碰撞体、标记和预览分别管理。
"""
from __future__ import annotations
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
BLEND = ROOT / 'Blender/yard_07.blend'
COLLIDERS = []
COLLECTIONS = {}


def choose(owner, prop, desired):
    """先读取本机 Blender 的枚举，避免使用已被版本移除的设置值。"""
    items = {item.identifier for item in owner.bl_rna.properties[prop].enum_items}
    if items and desired not in items:
        raise ValueError(f'{prop}: {desired!r} is not supported: {items}')
    return desired


def collection(name):
    c = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(c)
    COLLECTIONS[name] = c
    return c


def move_to(obj, name):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    COLLECTIONS[name].objects.link(obj)
    return obj


def material(name, hex_color, roughness=0.78, metallic=0.0):
    # 色板使用网页常见的 sRGB 色值，写入着色节点前换算为线性颜色。
    rgb = [int(hex_color[i:i+2], 16) / 255 for i in (0, 2, 4)]
    linear = [c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4 for c in rgb]
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*linear, 1)
    m.use_nodes = True
    node = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    node.inputs['Base Color'].default_value = (*linear, 1)
    node.inputs['Roughness'].default_value = roughness
    node.inputs['Metallic'].default_value = metallic
    return m


def mesh(name, vertices, faces, mat, group='01_Architecture'):
    data = bpy.data.meshes.new(name + '_Mesh')
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    COLLECTIONS[group].objects.link(obj)
    if mat:
        data.materials.append(mat)
    # 采用平面着色，保留棱面；装饰边缘只做一段倒角。
    for p in data.polygons:
        p.use_smooth = False
    return obj


def box(name, center, size, mat, group='01_Architecture', bevel=.025, solid=False):
    x, y, z = (v / 2 for v in size)
    vertices = [(-x,-y,-z), (x,-y,-z), (x,y,-z), (-x,y,-z),
                (-x,-y,z), (x,-y,z), (x,y,z), (-x,y,z)]
    faces = [(0,3,2,1), (0,1,5,4), (1,2,6,5), (2,3,7,6), (3,0,4,7), (4,5,6,7)]
    o = mesh(name, vertices, faces, mat, group)
    o.location = center
    if bevel:
        mod = o.modifiers.new('Small_Flat_Edge', 'BEVEL')
        mod.width = min(bevel, min(size) / 3)
        mod.segments = 1
    if solid:
        collider(name, center, size)
    return o


def collider(name, center, size):
    # 与渲染模型分开，接入引擎时可以直接用这些尺寸创建 BoxCollider。
    o = box('COL_' + name, center, size, None, '90_Collision', bevel=0)
    o.display_type = choose(o, 'display_type', 'WIRE')
    o.hide_render = True
    o.hide_set(True)
    o['purpose'] = 'box_collider'
    COLLIDERS.append({'name': o.name, 'center': list(center), 'size': list(size)})
    return o


def cylinder(name, center, radius, depth, mat, vertices=12, group='03_Props'):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=center)
    o = bpy.context.object
    o.name = name
    o.data.materials.append(mat)
    return move_to(o, group)


def text(name, words, center, size, mat, rotation=(0,0,0), group='04_Markings'):
    data = bpy.data.curves.new(name + '_Text', 'FONT')
    data.body = words
    data.size = size
    data.align_x = choose(data, 'align_x', 'CENTER')
    data.align_y = choose(data, 'align_y', 'CENTER')
    data.extrude = .001
    data.resolution_u = 2
    obj = bpy.data.objects.new(name, data)
    COLLECTIONS[group].objects.link(obj)
    obj.location = center
    obj.rotation_euler = rotation
    data.materials.append(mat)
    return obj


def crate(name, x, y, w, d, h, mats):
    group = '03_Props'
    box(name, (x,y,h/2), (w,d,h), mats['Wood'], group, .055, True)
    # 木箱的横条、角条均为简单几何，不依赖贴图。
    for z in (.12, h-.12):
        for sy in (-1,1):
            box(name+'_Band', (x,y+sy*(d/2+.018),z), (w,.045,.14), mats['WoodLight'], group, .009)
    for sx in (-1,1):
        for sy in (-1,1):
            box(name+'_Corner', (x+sx*(w/2-.08),y+sy*(d/2+.025),h/2), (.13,.05,h), mats['WoodLight'], group, .008)
    for sx in (-1,1):
        box(name+'_SteelStrap',(x+sx*w*.27,y,h+.009),(.07,d,.02),mats['Dark'],group,0)
    text(name+'_Stamp','07',(x,y-d/2-.046,h*.57),min(w,h)*.33,mats['Ink'],(math.pi/2,0,0))


def container(name, x, y, mat, mats, serial):
    group='02_Containers'
    length, width, height = 8., 2.8, 2.65
    box(name+'_Body',(x,y,height/2),(length,width,height),mat,group,.055,True)
    for sy in (-1,1):
        for i in range(19):
            box(name+'_Rib',(x-3.7+i*7.4/18,y+sy*1.418,1.31),(.075,.07,2.34),mat,group,.012)
        for z in (.12,2.53):
            box(name+'_Rail',(x,y+sy*1.425,z),(8.03,.10,.13),mats['Dark'],group,.012)
    for sx in (-1,1):
        for sy in (-1,1):
            box(name+'_Corner',(x+sx*3.94,y+sy*1.35,1.325),(.15,.15,2.68),mats['Dark'],group,.012)
    for i in range(11):
        box(name+'_RoofRib',(x-3.6+i*.72,y,2.67),(.065,2.6,.04),mat,group,.008)
    # 门锁杆与标识帮助集装箱在远处也有可辨认的轮廓。
    for yy in (-.7,.7):
        box(name+'_DoorLock',(x+4.026,y+yy,1.325),(.045,.055,2.26),mats['LightMetal'],group,.009)
    box(name+'_LabelPlate',(x-2.5,y-1.468,1.72),(1.7,.035,.68),mats['Ivory'],group,.018)
    text(name+'_Label',serial,(x-2.5,y-1.493,1.72),.38,mats['Dark'],(math.pi/2,0,0))


def warehouse(mats):
    # 仓库是封闭的边界建筑，没有室内和可到达屋顶。
    box('Warehouse',(0,14,2.2),(12,4,4.4),mats['Sand'],solid=True)
    vertices=[(-6.3,11.7,4.4),(6.3,11.7,4.4),(-6.3,16.3,4.4),(6.3,16.3,4.4),(-6.3,14,5.3),(6.3,14,5.3)]
    mesh('Warehouse_GableRoof',vertices,[(0,1,5,4),(4,5,3,2),(0,4,2),(1,3,5),(0,2,3,1)],mats['Roof'])
    for x in (-5.7,0,5.7):
        box('Warehouse_Pillar',(x,11.94,2.2),(.20,.18,4.4),mats['Ivory'])
    for x in (-3,3):
        box('Warehouse_ClosedDoor',(x,11.88,1.45),(4.5,.10,2.85),mats['Teal'])
        for i in range(10):
            box('Warehouse_DoorSlat',(x,11.815,.15+i*.28),(4.4,.025,.035),mats['Dark'],bevel=0)
        box('Warehouse_Lintel',(x,11.76,2.96),(4.8,.25,.18),mats['Ivory'])
        box('Warehouse_Lamp',(x,11.70,3.34),(.62,.25,.14),mats['WarmLight'])
    box('Warehouse_Sign',(0,11.77,3.89),(8,.12,.65),mats['Dark'])
    text('Warehouse_Title','DEPOT  /  07',(0,11.695,3.88),.44,mats['Ivory'],(math.pi/2,0,0))
    # 少量屋顶通风口：不增加地图路线，也不参与碰撞。
    for x in (-3.4,3.4):
        box('Roof_Vent',(x,14.8,5.10),(1.0,1.0,.6),mats['LightMetal'])
        box('Roof_VentCap',(x,14.8,5.43),(1.2,1.2,.12),mats['Dark'])


def light(name, kind, location, energy, color, size=None):
    kind = choose(bpy.types.Light, 'type', kind)
    data=bpy.data.lights.new(name, kind)
    data.energy=energy
    data.color=color
    if size is not None:
        data.shape=choose(data,'shape','DISK')
        data.size=size
    obj=bpy.data.objects.new(name,data)
    COLLECTIONS['99_Presentation'].objects.link(obj)
    obj.location=location
    obj.rotation_euler=(Vector((0,1,0))-obj.location).to_track_quat('-Z','Y').to_euler()
    return obj


def camera(name, location, target, orthographic=False, scale=48, lens=30):
    data=bpy.data.cameras.new(name)
    data.type=choose(data,'type','ORTHO' if orthographic else 'PERSP')
    data.ortho_scale=scale
    data.lens=lens
    data.clip_end=300
    o=bpy.data.objects.new(name,data)
    COLLECTIONS['99_Presentation'].objects.link(o)
    o.location=location
    o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
    return o


def prepare_scene():
    if not bpy.app.background or bpy.data.filepath:
        raise RuntimeError('请使用独立后台 Blender 和 --factory-startup，避免覆盖已有场景。')
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj,do_unlink=True)
    scene=bpy.context.scene
    scene.name='Yard_07_Simple_Map'
    scene.unit_settings.system=choose(scene.unit_settings,'system','METRIC')
    scene.unit_settings.scale_length=1
    for name in ('00_Ground','01_Architecture','02_Containers','03_Props','04_Markings','80_GameMarkers','90_Collision','99_Presentation'):
        collection(name)
    palette={'Ground':'738183','Concrete':'C4BCAB','Sand':'C8BA9A','Roof':'526674','Teal':'378E8C','Rust':'C5684B',
             'Wood':'BF9457','WoodLight':'DEC494','Dark':'293E49','Ivory':'EEE7D6','LightMetal':'90A9AB',
             'Ink':'61513C','Yellow':'E3B856','WarmLight':'FFF0C8','Backdrop':'D7DFDA','SpawnBlue':'467F92','SpawnRed':'B8654F'}
    mats={n:material(n,c) for n,c in palette.items()}
    box('Foundation',(0,2,-.48),(33.4,29.4,.8),mats['Concrete'],'00_Ground',.12)
    box('Playable_Ground',(0,0,-.06),(32,24,.12),mats['Ground'],'00_Ground',0,True)
    box('Warehouse_Apron',(0,14,-.04),(12.6,4.2,.08),mats['Concrete'],'00_Ground',0)
    # 两侧墙和南墙围出 32 × 24 米的活动区；北侧中间由仓库墙封闭。
    for name,center,size in [
        ('Boundary_W',(-16.15,0,1.2),(.30,24.0,2.4)),
        ('Boundary_E',(16.15,0,1.2),(.30,24.0,2.4)),
        ('Boundary_S',(0,-12.15,1.2),(32.6,.30,2.4)),
        ('Boundary_NW',(-11,12.15,1.2),(10,.30,2.4)),
        ('Boundary_NE',(11,12.15,1.2),(10,.30,2.4))]:
        box(name,center,size,mats['Sand'],solid=True)
        cap=(center[0],center[1],2.42)
        cap_size=(size[0]+.07, size[1]-.07 if name in ('Boundary_W','Boundary_E') else size[1]+.07, .12)
        box(name+'_Cap',cap,cap_size,mats['Ivory'])
    for x in (-15.9,-8,0,8,15.9):
        box('Boundary_S_Post',(x,-12.13,1.3),(.32,.40,2.6),mats['Ivory'])
    warehouse(mats)
    # 出生区有遮挡，但两端均可绕出；不设置单向门和复杂高差。
    for sign,x,mat in [('A',-13.5,mats['SpawnBlue']),('B',13.5,mats['SpawnRed'])]:
        box('Spawn_'+sign+'_Paint',(x,0,.009),(3.0,6,.018),mat,'04_Markings',0)
        text('Spawn_'+sign+'_Mark',sign,(x,0,.025),1.4,mats['Ivory'])
        wall_x= -10.6 if sign=='A' else 10.6
        box('Spawn_'+sign+'_Screen',(wall_x,0,1.4),(.48,5,2.8),mats['Concrete'],solid=True)
        box('Spawn_'+sign+'_Cap',(wall_x,0,2.83),(.58,5.08,.12),mat)
        marker=bpy.data.objects.new('Spawn_Team_'+sign,None)
        COLLECTIONS['80_GameMarkers'].objects.link(marker)
        marker.location=(x,0,0)
        marker.empty_display_type=choose(marker,'empty_display_type','ARROWS')
        marker.empty_display_size=1
        marker['team']=sign
        marker['facing_blender_xy']=[1 if sign=='A' else -1,0]
    container('Container_North',-4.5,4.5,mats['Teal'],mats,'N  /  01')
    container('Container_South',4.5,-4.5,mats['Rust'],mats,'S  /  02')
    crate('Cover_Center_High',-.95,.80,1.8,1.8,2.0,mats)
    crate('Cover_Center_Low',1.30,-1.30,1.4,1.4,1.05,mats)
    crate('Cover_North',7,8.4,2,2,1.6,mats)
    crate('Cover_South',-7,-8.4,2,2,1.6,mats)
    # 两组桶只布置在边缘，整体盒形碰撞包住桶组，减少后续引擎接入成本。
    for name,x,y in [('North',12.8,9.3),('South',-12.8,-9.3)]:
        for dx in (-.42,.42):
            cylinder('Barrel_'+name,(x+dx,y,.52),.36,1.04,mats['Yellow'])
            for z in (.13,.88):
                cylinder('Barrel_'+name+'_Ring',(x+dx,y,z),.372,.065,mats['Dark'])
            cylinder('Barrel_'+name+'_Top',(x+dx,y,1.045),.33,.025,mats['Dark'])
        collider('Barrels_'+name,(x,y,.54),(1.58,.76,1.08))
    # 中央方框仅标出交战区，不包含爆破玩法实现。
    for y in (-2.8,2.8):
        box('Center_Mark',(0,y,.018),(5.6,.10,.014),mats['Yellow'],'04_Markings',0)
    for x in (-2.8,2.8):
        box('Center_Mark',(x,0,.018),(.10,5.6,.014),mats['Yellow'],'04_Markings',0)
    for x in (-4.5,4.5):
        y=4.5 if x<0 else -4.5
        for side in (-1,1):
            box('Container_ParkingLine',(x,y+side*1.67,.012),(8.4,.08,.012),mats['Ivory'],'04_Markings',0)
    # 为几条路线保留清楚的空地。地面只用少量标线，不堆砌装饰。
    for x in (-9,0,9):
        for y in (-10.5,10.5):
            box('Yard_ParkingDash',(x,y,.013),(2.8,.07,.012),mats['Ivory'],'04_Markings',0)
    target=bpy.data.objects.new('Center_Reference',None)
    COLLECTIONS['80_GameMarkers'].objects.link(target)
    target.location=(0,-2.2,0)
    target['purpose']='reference_only_no_gameplay'
    box('Studio_Ground',(0,0,-1.04),(200,200,.20),mats['Backdrop'],'99_Presentation',0)
    text('Presentation_Title','YARD  /  07',(-8,-14.7,-.918),1.15,mats['Dark'],group='99_Presentation')
    text('Presentation_Subtitle','SIMPLE TACTICAL MAP',(7,-14.55,-.918),.36,mats['Dark'],group='99_Presentation')
    text('Presentation_Scale','32 x 24 m   /   LOW POLY',(7,-15.18,-.918),.29,mats['Dark'],group='99_Presentation')
    camera('Camera_Hero',(37,-47,43),(0,1.4,.4),True,51)
    camera('Camera_Top',(0,1,65),(0,1,0),True,39)
    camera('Camera_Eye',(-8.7,-2.1,1.65),(2,1,1.55),False,lens=22)
    light('Sun','SUN',(-14,-22,32),2.1,(1,.88,.72))
    light('Fill','AREA',(14,-3,27),2600,(.72,.85,1),22)
    light('Key','AREA',(-12,-14,22),3200,(1,.94,.82),18)
    world=scene.world
    world.use_nodes=True
    bg=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND')
    bg.inputs['Color'].default_value=(.55,.65,.75,1)
    bg.inputs['Strength'].default_value=.45
    # 优先使用真实光线渲染获得自然接触阴影；本机不支持时保留当前渲染器。
    current=scene.render.engine
    try:
        scene.render.engine='CYCLES'
        scene.cycles.samples=32
        scene.cycles.use_denoising=True
    except TypeError as e:
        print('Cycles unavailable; keeping',current,str(e))
        scene.render.engine=current
    scene.render.image_settings.file_format=choose(scene.render.image_settings,'file_format','PNG')
    scene.render.image_settings.color_mode=choose(scene.render.image_settings,'color_mode','RGB')
    scene.render.resolution_percentage=100
    scene.render.film_transparent=False
    scene.camera=bpy.data.objects['Camera_Hero']
    scene.render.resolution_x=1440
    scene.render.resolution_y=1120
    # 默认打开时显示彩色模型和整体布局。
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                space=area.spaces.active
                space.shading.color_type=choose(space.shading,'color_type','MATERIAL')
                space.region_3d.view_distance=52
                space.region_3d.view_location=(0,1,0)
                space.region_3d.view_rotation=scene.camera.rotation_euler.to_quaternion()
    scene['MapName']='七号转运站 / Yard 07'
    scene['PlayableSizeMeters']='32 x 24'
    scene['Scope']='Simple static map; no game logic; two team spawn markers.'
    scene['CollisionNote']='90_Collision is hidden. Data/yard_07.layout.json also contains exact boxes.'
    layout={'name':'Yard 07','version':1,'units':'meters','coordinate_system':'Blender: X/Y ground, Z up',
            'playable_bounds':{'min':[-16,-12],'max':[16,12]},
            'assumed_player':{'radius':.35,'standing_height':1.8,'eye_height':1.65},
            'spawns':[{'name':'Spawn_Team_A','position':[-13.5,0,0],'facing':[1,0,0]},
                      {'name':'Spawn_Team_B','position':[13.5,0,0],'facing':[-1,0,0]}],
            'route_checkpoints':{'north':[-1,8,0],'middle':[0,-2.2,0],'south':[1,-8,0]},
            'colliders':COLLIDERS}
    (ROOT/'Data/yard_07.layout.json').write_text(json.dumps(layout,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    return scene


def export_models():
    # 所有文字在导出前转为网格，导出文件不依赖字体。
    for obj in list(bpy.context.scene.objects):
        if obj.type=='FONT':
            bpy.ops.object.select_all(action='DESELECT')
            obj.select_set(True)
            bpy.context.view_layer.objects.active=obj
            target=choose(bpy.ops.object.convert.get_rna_type(),'target','MESH')
            bpy.ops.object.convert(target=target)
    visual_groups=('00_Ground','01_Architecture','02_Containers','03_Props','04_Markings','80_GameMarkers')
    bpy.ops.object.select_all(action='DESELECT')
    for g in visual_groups:
        for obj in COLLECTIONS[g].objects:
            obj.select_set(True)
    # 读取导出选项，随后由 Blender 自身校验动态枚举。
    gltf_rna=bpy.ops.export_scene.gltf.get_rna_type()
    choose(gltf_rna,'export_format','GLB')
    bpy.ops.export_scene.gltf(filepath=str(ROOT/'Exports/yard_07.glb'),export_format='GLB',use_selection=True,
        export_extras=True,export_apply=True,export_animations=False,export_cameras=False,export_lights=False)
    fbx_rna=bpy.ops.export_scene.fbx.get_rna_type()
    forward=choose(fbx_rna,'axis_forward','-Z')
    up=choose(fbx_rna,'axis_up','Y')
    scale_mode=choose(fbx_rna,'apply_scale_options','FBX_SCALE_UNITS')
    bpy.ops.export_scene.fbx(filepath=str(ROOT/'Exports/yard_07.fbx'),use_selection=True,
        object_types={'MESH','EMPTY'},apply_unit_scale=True,apply_scale_options=scale_mode,
        axis_forward=forward,axis_up=up,bake_anim=False,use_mesh_modifiers=True)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in COLLECTIONS['90_Collision'].objects:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(ROOT/'Exports/yard_07_collision.fbx'),use_selection=True,
        object_types={'MESH'},apply_unit_scale=True,apply_scale_options=scale_mode,
        axis_forward=forward,axis_up=up,bake_anim=False)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in COLLECTIONS['90_Collision'].objects:
        obj.hide_set(True)
    # 保存可编辑场景；模型按语义分组、独立物体，没有合并成单块。
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    depsgraph=bpy.context.evaluated_depsgraph_get()
    triangles=0
    meshes=0
    for g in visual_groups:
        for obj in COLLECTIONS[g].objects:
            if obj.type!='MESH':
                continue
            meshes+=1
            evaluated=obj.evaluated_get(depsgraph)
            data=evaluated.to_mesh()
            data.calc_loop_triangles()
            triangles+=len(data.loop_triangles)
            evaluated.to_mesh_clear()
    summary={'blender_version':bpy.app.version_string,'visual_mesh_objects':meshes,'visual_triangles':triangles,
             'box_colliders':len(COLLIDERS),'playable_meters':[32,24],'external_textures':0,
             'exports':['yard_07.blend','yard_07.glb','yard_07.fbx','yard_07_collision.fbx']}
    (ROOT/'Data/model-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    print('MODEL_SUMMARY',json.dumps(summary),flush=True)


def render(view):
    scene=bpy.context.scene
    names={'hero':'Camera_Hero','top':'Camera_Top','eye':'Camera_Eye'}
    scene.camera=bpy.data.objects[names[view]]
    scene.render.resolution_x=1440 if view!='top' else 1200
    scene.render.resolution_y=1120 if view=='hero' else (1200 if view=='top' else 810)
    scene.render.filepath=str(ROOT/f'Previews/yard_07_{view}.png')
    if view!='hero':
        for obj in scene.objects:
            if obj.name.startswith('Presentation_'):
                obj.hide_render=True
    bpy.ops.render.render(write_still=True)
    print('RENDER_SAVED',scene.render.filepath,flush=True)


def main():
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    parser=argparse.ArgumentParser()
    parser.add_argument('--stage',choices=('build','render'),default='build')
    parser.add_argument('--view',choices=('hero','top','eye'),default='hero')
    opt=parser.parse_args(args)
    if opt.stage=='build':
        prepare_scene()
        export_models()
    else:
        if Path(bpy.data.filepath)!=BLEND:
            raise RuntimeError('渲染时请显式加载本脚本生成的 yard_07.blend。')
        render(opt.view)


if __name__=='__main__':
    main()
