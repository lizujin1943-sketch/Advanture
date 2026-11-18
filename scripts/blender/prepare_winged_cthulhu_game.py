"""Make a reduced, uniquely unwrapped, baked copy of the radial source.

Run against the painted reference sculpt in a background Blender process.
The high source is never overwritten. No external add-ons are required.
"""
from pathlib import Path
import sys,json,math
import bpy
import numpy as np
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from winged_cthulhu.surface_cleanup import relieve

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'source-assets/creatures/winged-cthulhu'
TEX=OUT/'runtime-textures'

def active(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects:ob.hide_set(False);ob.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]

def join(objects,name):
    active(objects)
    if len(objects)>1:bpy.ops.object.join()
    ob=bpy.context.object;ob.name=name;return ob

def reduce(ob,ratio):
    active([ob]);before=len(ob.data.vertices)
    for mod in list(ob.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
    verts=[v.co.copy() for v in ob.data.vertices]
    old=BVHTree.FromPolygons(verts,[p.vertices[:] for p in ob.data.polygons])
    mod=ob.modifiers.new('Runtime reduction','DECIMATE');mod.ratio=ratio;mod.use_collapse_triangulate=True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    surf=BVHTree.FromPolygons([v.co for v in ob.data.vertices],[p.vertices[:] for p in ob.data.polygons])
    errors=[surf.find_nearest(v)[3] for v in verts[::max(1,len(verts)//5000)]]
    errors += [old.find_nearest(ob.data.vertices[i].co)[3] for i in range(0,len(ob.data.vertices),max(1,len(ob.data.vertices)//5000))]
    assert max(errors)<.055,(ob.name,max(errors))
    return {'name':ob.name,'sourceVertices':before,'vertices':len(ob.data.vertices),'maxSurfaceErrorMeters':max(errors)}

def unwrap(ob,tile,cols,rows):
    active([ob]);me=ob.data
    uv=me.uv_layers.new(name='RuntimeUV');me.uv_layers.active=uv;uv.active_render=True
    bpy.ops.object.mode_set(mode='EDIT')
    try:
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.008,area_weight=.2,correct_aspect=True,scale_to_bounds=True)
    finally:bpy.ops.object.mode_set(mode='OBJECT')
    # Edit-mode unwrap reallocates loop custom data; reacquire the RNA layer.
    me=ob.data;uv=me.uv_layers['RuntimeUV']
    arr=np.empty(len(me.loops)*2,np.float32);uv.uv.foreach_get('vector',arr);arr=arr.reshape(-1,2)
    arr[:,0]=(arr[:,0]*.98+.01+tile%cols)/cols
    arr[:,1]=(arr[:,1]*.98+.01+tile//cols)/rows
    uv.uv.foreach_set('vector',arr.ravel())

def bake_group(objects,name,res):
    copies=[]
    for ob in objects:
        cp=ob.copy();cp.data=ob.data.copy();bpy.context.scene.collection.objects.link(cp);cp.parent=None;copies.append(cp)
    target=join(copies,'BAKE_'+name)
    for ob in bpy.data.objects:
        if ob.type=='MESH':ob.hide_render=ob!=target
    active([target]);scene=bpy.context.scene
    scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=8
    scene.render.bake.use_selected_to_active=False;scene.render.bake.use_clear=True
    scene.render.bake.margin=12;scene.render.bake.normal_space='TANGENT'
    scene.render.bake.target='IMAGE_TEXTURES';scene.cycles.use_denoising=False
    images={}
    mats=set(target.data.materials)
    for kind in ('BaseColor','Roughness','Normal'):
        data=kind!='BaseColor'
        im=bpy.data.images.new('T_WC_'+name+'_'+kind,res,res,alpha=False,is_data=data)
        im.colorspace_settings.name='Non-Color' if data else 'sRGB'
        nodes=[];undo=[]
        for mat in mats:
            ns=mat.node_tree.nodes;ln=mat.node_tree.links
            node=ns.new('ShaderNodeTexImage');node.image=im;ns.active=node;nodes.append((ns,node))
            if kind=='BaseColor':
                p=next(n for n in ns if n.type=='BSDF_PRINCIPLED');output=next(n for n in ns if n.type=='OUTPUT_MATERIAL')
                prev=output.inputs['Surface'].links[0].from_socket
                em=ns.new('ShaderNodeEmission')
                if p.inputs['Base Color'].is_linked:ln.new(p.inputs['Base Color'].links[0].from_socket,em.inputs['Color'])
                else:em.inputs['Color'].default_value=p.inputs['Base Color'].default_value
                ln.new(em.outputs[0],output.inputs['Surface']);undo.append((mat,em,output,prev))
        try:bpy.ops.object.bake(type={'BaseColor':'EMIT','Roughness':'ROUGHNESS','Normal':'NORMAL'}[kind])
        finally:
            for mat,em,output,prev in undo:mat.node_tree.nodes.remove(em);mat.node_tree.links.new(prev,output.inputs['Surface'])
            for ns,node in nodes:ns.remove(node)
        im.filepath_raw=str(TEX/(im.name+'.png'));im.file_format='PNG';im.save();im.pack();images[kind]=im
        print('BAKED',name,kind,res,flush=True)
    mat=bpy.data.materials.new('WC_'+name);ns=mat.node_tree.nodes;ln=mat.node_tree.links
    p=next(n for n in ns if n.type=='BSDF_PRINCIPLED');p.inputs['Specular IOR Level'].default_value=.24
    uv=ns.new('ShaderNodeUVMap');uv.uv_map='RuntimeUV'
    for kind,im in images.items():
        n=ns.new('ShaderNodeTexImage');n.image=im;ln.new(uv.outputs['UV'],n.inputs['Vector'])
        if kind=='Normal':
            normal=ns.new('ShaderNodeNormalMap');normal.uv_map='RuntimeUV';ln.new(n.outputs['Color'],normal.inputs['Color']);ln.new(normal.outputs['Normal'],p.inputs['Normal'])
        else:ln.new(n.outputs['Color'],p.inputs['Base Color' if kind=='BaseColor' else 'Roughness'])
    for ob in objects:
        ob.data.materials.clear();ob.data.materials.append(mat)
        for p in ob.data.polygons:p.material_index=0
        ob.hide_render=False
    bpy.data.objects.remove(target,do_unlink=True)
    return mat

def main():
    assert bpy.app.background and bpy.data.objects['CR_WingedCthulhu'].get('radial_revision')
    TEX.mkdir(parents=True,exist_ok=True)
    obs=[o for o in bpy.data.collections['HIGH'].objects if o.type=='MESH']
    report=[]
    for ob in obs:
        ratio=.085 if 'OrganicCore' in ob.name else .20 if '_Arm_' in ob.name else .28 if 'Membrane' in ob.name else .4
        report.append(reduce(ob,ratio))
    # Collect memberships before joins invalidate their source RNA objects.
    body_parts=[o for o in obs if any(s in o.name for s in ('OrganicCore','CheekFin','Socket_'))]
    eye_parts=[o for o in obs if '_Eye_' in o.name]
    wing_parts={side:[o for o in obs if o.name.endswith('_'+side) and ('WingMembrane' in o.name or 'WingVeins' in o.name)] for side in ('L','R')}
    arms=sorted([o for o in obs if '_Arm_' in o.name],key=lambda o:o['limb_id'])
    from winged_cthulhu.crown_seams import close_junctions
    from paint_winged_cthulhu import uv_and_coverage,paint_dorsal_marks,paint_mantle_and_cups
    core=next(o for o in body_parts if 'OrganicCore' in o.name)
    if core.get('portrait_paint'):
        paint_dorsal_marks()
        paint_mantle_and_cups()
    close_junctions(core,arms)
    for ob in [core]+arms:uv_and_coverage(ob)
    body=join([core]+[o for o in body_parts if o!=core],'AW_Head');eyes=join(eye_parts,'AW_Eye_Center')
    wings=[join(wing_parts[side],'WC_Wing_'+side) for side in ('L','R')]
    for ob in arms:ob.name='AW_Arm_'+ob['limb_id']
    relief=[relieve(ob) for ob in [body]+wings+arms]
    unwrap(eyes,0,1,1)
    groups=[([body],'Body',4096,1,1),(arms,'Arms',4096,4,2),(wings,'Wings',2048,2,1)]
    for objects,name,res,cols,rows in groups:
        for i,ob in enumerate(objects):unwrap(ob,i,cols,rows)
        bake_group(objects,name,res)
    for ob in [body,eyes]+wings+arms:
        ob.hide_render=False;ob.parent=None
        # Only the baked game UV is exported. Original authoring UV stays in high source.
        uv=ob.data.uv_layers.get('RuntimeUV')
        if uv:
            for other in list(ob.data.uv_layers):
                if other!=uv:ob.data.uv_layers.remove(other)
        for attr in list(ob.data.color_attributes):ob.data.color_attributes.remove(attr)
        ob.data.calc_loop_triangles()
    eyes.data.materials[0].name='WC_Eyes'
    bpy.data.objects['STUDIO_Backdrop'].hide_render=True
    total=sum(len(o.data.loop_triangles) for o in [body,eyes]+wings+arms)
    (OUT/'runtime-geometry.json').write_text(json.dumps({'triangles':total,'reduction':report,'surfaceRelief':relief,'textureGroups':['Body4096','Arms4096','Wings2048'],'uv':'Unique atlas RuntimeUV; no procedural Blender nodes required in engine'},indent=2))
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CR_WingedCthulhu_game_mesh.blend'),compress=True)
    print('GAME_MESH_SAVED',total,flush=True)

if __name__=='__main__':main()
