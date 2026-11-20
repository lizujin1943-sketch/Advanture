"""Build the editable model and two isolated engine exports with Blender in background mode."""
from pathlib import Path
from math import pi
import sys
import bpy
import bmesh
from mathutils import Matrix
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
from atlas_layout import GAME, SOURCE, TEXTURES
from exterior import PIVOTS, fore_end, frame, magazine, stock
from hands import carry, firing, supporting
from mesh_tools import Part, Workshop, blender_point


def surface():
    mat=bpy.data.materials.new('AK47_Surface')
    n,link=mat.node_tree.nodes,mat.node_tree.links.new
    bsdf=next(node for node in n if node.type=='BSDF_PRINCIPLED')
    bsdf.location=(470,100)
    outputs=next(node for node in n if node.type=='OUTPUT_MATERIAL')
    outputs.location=(780,100)
    src=[]
    for i,name in enumerate(TEXTURES):
        image=bpy.data.images.load(str(GAME/name),check_existing=True)
        image.colorspace_settings.name='sRGB' if i==0 else 'Non-Color'
        if i==1:
            image.alpha_mode='CHANNEL_PACKED'
        tex=n.new('ShaderNodeTexImage')
        tex.image=image
        tex.label=('Walnut, steel and twill','R metal / alpha gloss','Tangent-space grain')[i]
        tex.location=(-530,390-i*300)
        src.append(tex)
    link(src[0].outputs['Color'],bsdf.inputs['Base Color'])
    sep=n.new('ShaderNodeSeparateColor')
    sep.location=(-185,120)
    link(src[1].outputs['Color'],sep.inputs[0])
    link(sep.outputs['Red'],bsdf.inputs['Metallic'])
    rough=n.new('ShaderNodeMath')
    rough.operation='SUBTRACT'
    rough.inputs[0].default_value=1.
    rough.location=(30,-35)
    link(src[1].outputs['Alpha'],rough.inputs[1])
    link(rough.outputs[0],bsdf.inputs['Roughness'])
    normal=n.new('ShaderNodeNormalMap')
    normal.location=(10,-240)
    normal.inputs['Strength'].default_value=.79
    link(src[2].outputs['Color'],normal.inputs['Color'])
    link(normal.outputs['Normal'],bsdf.inputs['Normal'])
    mat.diffuse_color=(.13,.10,.075,1)
    return mat


def authored(high):
    factory=Workshop(high)
    frame(factory)
    stock(factory)
    fore_end(factory)
    magazine(factory)
    supporting(factory)
    carry(factory)
    firing(factory)
    return factory.parts


def empty(name,collection,point):
    obj=bpy.data.objects.new(name,None)
    collection.objects.link(obj)
    obj.empty_display_size=.025
    obj.empty_display_type='PLAIN_AXES'
    obj.location=blender_point(point)
    return obj


def export(parts,scene,mat,stem):
    stage=bpy.data.scenes.new('Export '+stem)
    bpy.context.window.scene=stage
    objs=[]
    # Verified in this project's Unity ModelImporter with Bake Axis Conversion.
    # Keep every local frame neutral and turn vertex positions and bind pivots in
    # the export stage, so artist-space orientation in the editable file is kept.
    to_unity=Matrix.Rotation(pi,4,'Z')
    for group,pivot in PIVOTS.items():
        if group in ('Muzzle','CarrySocket'):
            socket=empty(group,stage.collection,pivot)
            socket.location=to_unity@socket.location
            objs.append(socket)
            continue
        merged=Part(group,group)
        for p in parts:
            if p.group!=group:
                continue
            start=len(merged.verts)
            merged.verts.extend(p.verts)
            merged.faces.extend(tuple(i+start for i in face) for face in p.faces)
            merged.smooth.extend(p.smooth)
            merged.uv.extend(p.uv)
        obj=merged.object(stage.collection,mat,pivot)
        obj.location=to_unity@obj.location
        obj.data.transform(to_unity)
        bm=bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.triangulate(bm,faces=bm.faces[:],quad_method='BEAUTY',ngon_method='BEAUTY')
        bm.to_mesh(obj.data)
        bm.free()
        objs.append(obj)
    for obj in objs:
        obj.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]
    bpy.context.view_layer.update()
    result=bpy.ops.export_scene.fbx(filepath=str(GAME/(stem+'.fbx')),use_selection=True,
        axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        bake_space_transform=True,object_types={'MESH','EMPTY'},mesh_smooth_type='FACE',
        use_triangles=True,use_tspace=True,bake_anim=False,add_leaf_bones=False,
        use_custom_props=False,path_mode='STRIP',embed_textures=False)
    if 'FINISHED' not in result:
        raise RuntimeError('Could not export '+stem)
    for obj in objs:
        mesh=obj.data
        bpy.data.objects.remove(obj,do_unlink=True)
        if mesh:
            bpy.data.meshes.remove(mesh)
    bpy.context.window.scene=scene
    bpy.data.scenes.remove(stage)


def main():
    # This script only runs in a new headless Blender process.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    scene=bpy.context.scene
    scene.name='AK47 | walnut exterior'
    scene.unit_settings.system='METRIC'
    scene['Purpose']='Costumed game prop for the fortified compound.'
    mat=surface()
    hero=authored(True)
    export(hero,scene,mat,'AK47_View')
    export(authored(False),scene,mat,'AK47_World')
    for group,point in PIVOTS.items():
        coll=bpy.data.collections.new('Art | '+group)
        scene.collection.children.link(coll)
        rig=empty(group,coll,point)
        rig.hide_set(True)
        for p in hero:
            if p.group!=group:
                continue
            obj=p.object(coll,mat,point)
            obj.parent=rig
            obj.location=(0,0,0)
        if group in ('FiringHand','SupportHand','SupportCarry'):
            bpy.context.view_layer.layer_collection.children[coll.name].hide_viewport=True
        if group=='SupportCarry':
            coll.hide_render=True
    bpy.context.view_layer.name='Model | exterior'
    bpy.context.view_layer.update()
    for area in bpy.context.screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active
            space.shading.type='MATERIAL'
            space.overlay.show_extras=False
            space.region_3d.view_perspective='ORTHO'
            space.region_3d.view_distance=1.39
            target=blender_point((0,.125,-.060))
            camera=blender_point((1.90,-.255,.225))
            space.region_3d.view_location=target
            space.region_3d.view_rotation=(target-camera).to_track_quat('-Z','Y')
        elif area.type=='OUTLINER':
            region=next((r for r in area.regions if r.type=='WINDOW'),None)
            if region:
                with bpy.context.temp_override(area=area,region=region):
                    if bpy.ops.outliner.show_one_level.poll():
                        for _ in range(3):
                            bpy.ops.outliner.show_one_level(open=False)
    for image in bpy.data.images:
        if image.source=='FILE' and image.filepath:
            image.pack()
            image.filepath=bpy.path.relpath(bpy.path.abspath(image.filepath),start=str(SOURCE))
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'AK47.blend'),compress=True)


if __name__=='__main__':
    main()
