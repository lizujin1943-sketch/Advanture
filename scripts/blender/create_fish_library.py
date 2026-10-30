"""Build the complete fish asset library in a separate background Blender process.

Blender --background --factory-startup --python scripts/blender/create_fish_library.py
Use -- --only fish_03,piranha --draft for a disposable visual check.
Never opens, modifies or saves the user's active Blender document.
"""
from pathlib import Path
import argparse
import json
import math
import sys
import bpy
from mathutils import Matrix, Vector

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from fish_species import SPECIES
from fish_reference_layout import bind_reference
from fish_reference_geometry import create_mesh, make_material
from fish_animation import make_rig, actions_for

ROOT=HERE.parents[1]
args=argparse.ArgumentParser()
args.add_argument('--only',default='')
args.add_argument('--draft',action='store_true')
options=args.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
SOURCE=(ROOT/'artifacts/fish-models/draft') if options.draft else (ROOT/'source-assets/creatures/fish')
EXPORT=SOURCE/'exports'
TEXTURES=EXPORT/'textures'
for path in (SOURCE,EXPORT,TEXTURES):
    path.mkdir(parents=True,exist_ok=True)


def check_enum(operator,key,value):
    values={item.identifier for item in operator.get_rna_type().properties[key].enum_items}
    # Callback enums (including glTF format) are empty until operator invocation.
    # The operator will validate those using its live context.
    if values and value not in values:
        raise RuntimeError(f'{key}: {value} not among {values}')
    return value


def select_only(rig,mesh):
    for obj in bpy.context.selected_objects:
        obj.select_set(False)
    rig.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active=rig


def export_model(spec,rig,mesh):
    select_only(rig,mesh)
    rig.animation_data.action=None
    for track in rig.animation_data.nla_tracks:
        track.mute=False
    path=EXPORT/f"{spec['id']}_{spec['slug']}"
    bpy.ops.export_scene.fbx(filepath=str(path.with_suffix('.fbx')),use_selection=True,
        object_types={'ARMATURE','MESH'},add_leaf_bones=False,
        use_mesh_modifiers=False,mesh_smooth_type='FACE',use_tspace=True,
        bake_anim=True,bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=True,bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,bake_anim_step=1,bake_anim_simplify_factor=0,
        path_mode='COPY',embed_textures=True,global_scale=1,apply_unit_scale=True,
        axis_forward=check_enum(bpy.ops.export_scene.fbx,'axis_forward','-Z'),
        axis_up=check_enum(bpy.ops.export_scene.fbx,'axis_up','Y'))
    bpy.ops.export_scene.gltf(filepath=str(path.with_suffix('.glb')),
        export_format=check_enum(bpy.ops.export_scene.gltf,'export_format','GLB'),
        use_selection=True,export_animations=True,export_skins=True,
        export_animation_mode=check_enum(bpy.ops.export_scene.gltf,'export_animation_mode','NLA_TRACKS'),
        export_force_sampling=True,export_frame_range=False,export_frame_step=1,
        export_apply=False,export_cameras=False,export_lights=False,
        export_yup=True,export_extras=True)
    for track in rig.animation_data.nla_tracks:
        track.mute=True
    rig.animation_data.action=spec['_actions']['Swim']
    return path


def aim(obj,target):
    obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()


def setup_studio(scene):
    studio=bpy.data.collections.new('Preview studio (not exported)')
    scene.collection.children.link(studio)
    def link(obj):
        for collection in list(obj.users_collection):
            collection.objects.unlink(obj)
        studio.objects.link(obj)
    bpy.ops.object.camera_add(location=(0,-5,7))
    camera=bpy.context.object
    camera.name='Fish library camera'
    camera.data.type='ORTHO'
    camera.data.ortho_scale=3.35
    camera.location=(0,-5,1.6)
    aim(camera,(0,0,.08))
    link(camera)
    scene.camera=camera
    for name,location,power,size in [('Key',(1,-3,6),750,5),('Fill',(-4,-1,3),500,5),
                                      ('Rim',(1,4,5),900,4)]:
        bpy.ops.object.light_add(type=check_enum(bpy.ops.object.light_add,'type','AREA'),location=location)
        obj=bpy.context.object
        obj.name=name
        obj.data.energy=power
        obj.data.shape='DISK'
        obj.data.size=size
        aim(obj,(0,0,0))
        link(obj)
    scene.world.use_nodes=True
    background=next(node for node in scene.world.node_tree.nodes if node.type=='BACKGROUND')
    background.inputs['Color'].default_value=(.17,.21,.28,1)
    background.inputs['Strength'].default_value=.4
    scene.render.engine='CYCLES'
    scene.cycles.device='CPU'
    scene.cycles.samples=16
    scene.cycles.use_denoising=True
    scene.render.threads_mode='FIXED'
    scene.render.threads=8
    scene.view_settings.view_transform=check_enum_from_type(scene.view_settings,'view_transform','Standard')
    scene.view_settings.look='None'
    scene.view_settings.exposure=-.6
    scene.render.film_transparent=True
    scene.render.resolution_x=960
    scene.render.resolution_y=720
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'


def check_enum_from_type(owner,key,value):
    # Dynamic OCIO enums cannot always be enumerated through bl_rna. Assignment
    # itself is Blender's authoritative validation for the current installation.
    try:
        setattr(owner,key,value)
    except TypeError as error:
        raise RuntimeError(str(error))
    return value


for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj,do_unlink=True)
scene=bpy.context.scene
scene.name='Pixel Fish Library'
scene.render.fps=30
scene.unit_settings.system='METRIC'
scene.unit_settings.scale_length=1
bpy.context.preferences.filepaths.save_version=0
requested=set(options.only.split(',')) if options.only else None
entries=[]
created=[]
for spec in SPECIES:
    if requested is not None and spec['id'] not in requested:
        continue
    bind_reference(spec)
    collection=bpy.data.collections.new(spec['id']+' | '+spec['name'])
    scene.collection.children.link(collection)
    rig=make_rig(spec,collection)
    material,texture=make_material(spec,TEXTURES)
    mesh=create_mesh(spec,collection,material,rig)
    # Meshes are authored at convenient proportions, then normalized to metres.
    span=max(vertex.co.y for vertex in mesh.data.vertices)-min(vertex.co.y for vertex in mesh.data.vertices)
    factor=spec['length']/span
    for vertex in mesh.data.vertices:
        vertex.co*=factor
    rig.data.transform(Matrix.Scale(factor,4))
    mesh.data.update()
    spec['_scale']=factor
    spec['_actions']=actions_for(rig,spec,scene)
    triangles=sum(len(face.vertices)-2 for face in mesh.data.polygons)
    if triangles>10000:
        raise RuntimeError(f"{spec['id']} exceeds triangle budget: {triangles}")
    path=export_model(spec,rig,mesh)
    scene.frame_set(1)
    entry={
        'id':spec['id'],'name':spec['name'],'slug':spec['slug'],
        'rig':rig.name,'mesh':mesh.name,'length_m':spec['length'],
        'vertices':len(mesh.data.vertices),'triangles':triangles,'bones':len(rig.data.bones),
        'material_count':1,'texture':str(texture.relative_to(SOURCE)),
        'texture_size':[1024,512],'texture_filter':'nearest',
        'fbx':str(path.with_suffix('.fbx').relative_to(SOURCE)),
        'glb':str(path.with_suffix('.glb').relative_to(SOURCE)),
        'clips':[{'name':name,'action':action.name,'frames':int(spec['clips'][name][0]+1),
                  'fps':30,'duration_seconds':spec['clips'][name][0]/30,
                  'loop':spec['clips'][name][1],'root_motion':False}
                  for name,action in spec['_actions'].items()],
    }
    entries.append(entry)
    created.append((spec,rig,mesh))
    print('FISH_EXPORTED',spec['id'],triangles,'tris',len(spec['_actions']),'clips',flush=True)

setup_studio(scene)
for i,(spec,rig,mesh) in enumerate(created):
    # A source-only gallery arrangement. Exported model roots remain at zero.
    if i<8:
        rig.location=((i%4-1.5)*.8,0,.82-(i//4)*.75)
    else:
        rig.location=([-1.12,-.18,.78][i-8],0,-.70)
    rig.rotation_euler.z=-math.pi/2
    mesh['texture_filter']='Point / nearest'
    mesh['triangle_budget']=10000
scene.frame_start,scene.frame_end=1,31
scene.frame_set(1)
scene['asset_notes']='11 fish; pixel textures; baked in-place actions. Gallery layout is source-only.'
for area in bpy.context.screen.areas if bpy.context.screen else []:
    if area.type=='VIEW_3D':
        area.spaces.active.shading.type='MATERIAL'
        area.spaces.active.region_3d.view_distance=3.5
        area.spaces.active.region_3d.view_location=(0,0,.08)
        area.spaces.active.overlay.show_overlays=False
        area.spaces.active.region_3d.view_perspective='CAMERA'
select_only(created[0][1],created[0][2])
for image in bpy.data.images:
    if image.source=='FILE' and image.filepath:
        image.pack()
blend=SOURCE/'FishLibrary.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(blend))
manifest={'schema':2,'art_revision':'reference-anatomy-v2','blender':bpy.app.version_string,'source':blend.name,
          'fps':30,'units':'meters','forward_blender':'-Y','up_blender':'Z',
          'root_motion':False,'triangle_budget_per_fish':10000,'species':entries,
          'clip_count':sum(len(e['clips']) for e in entries),
          'scope':'Art assets only. Not installed into Unity gameplay.'}
(SOURCE/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
print('FISH_LIBRARY_SAVED',blend,flush=True)
