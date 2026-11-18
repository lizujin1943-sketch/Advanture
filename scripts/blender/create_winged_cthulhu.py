"""Reproducible isolated model build. Run in a fresh background Blender process."""
from pathlib import Path
import sys
import math
import json
import bpy
from mathutils import Vector

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from winged_cthulhu import materials,anatomy,wings,sculpt
from winged_cthulhu.geometry import P

ROOT=HERE.parents[1]
OUT=ROOT/'source-assets/creatures/winged-cthulhu'
REVIEW=OUT/'review'

def aim(ob,target):
    ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()

def area(name,loc,power,color,size,target):
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.color=color;d.shape='DISK';d.size=size
    ob=bpy.data.objects.new(name,d);bpy.context.scene.collection.objects.link(ob);ob.location=loc;aim(ob,target)

def camera(name,loc,target,ortho):
    d=bpy.data.cameras.new(name);d.type='ORTHO';d.ortho_scale=ortho;d.lens=60
    ob=bpy.data.objects.new(name,d);bpy.context.scene.collection.objects.link(ob);ob.location=loc;aim(ob,target);return ob

def configure():
    s=bpy.context.scene;s.unit_settings.system='METRIC';s.unit_settings.scale_length=1
    try:s.render.engine='CYCLES'
    except TypeError as e:raise RuntimeError('Cycles unavailable: '+str(e))
    s.cycles.samples=64;s.cycles.use_denoising=True
    s.cycles.device='CPU'
    s.render.resolution_x=1536;s.render.resolution_y=1024;s.render.resolution_percentage=100
    s.frame_end=1
    s.render.image_settings.file_format='PNG';s.render.image_settings.color_mode='RGBA'
    s.render.film_transparent=False
    s.world.color=(.08,.1,.09);s.world.use_nodes=True
    bg=next(n for n in s.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs[0].default_value=(.115,.15,.14,1);bg.inputs[1].default_value=.24
    s.view_settings.view_transform='AgX'
    target=P(770,510)
    area('Key softbox',(-6,-9,12),1550,(.85,.94,1),7,target)
    area('Warm fill',(7,-6,7),650,(1,.83,.77),6,target)
    area('Sea rim',(-3,5,12),1250,(.68,.85,.84),6,target)
    area('Lower bounce',(0,-6,1),250,(.75,.72,.8),5,target)
    s.view_settings.exposure=-.2
    # 1 image pixel = 0.01 m; horizontal ortho scale reproduces the source framing.
    s.camera=camera('CAM_Reference',(0,-32,5.12),(0,0,5.12),15.6)
    camera('CAM_ThreeQuarter',(17,-28,11),(0,0,5.12),17.5)
    camera('CAM_Side',(28,-3,8),(0,0,5.12),16)
    camera('CAM_Face',(1.2,-24,6.9),(1.2,-.2,6.9),5.9)
    # A separate studio backdrop, excluded from asset geometry and validation.
    d=bpy.data.meshes.new('StudioBackdropMesh')
    d.from_pydata([(-100,7,-100),(100,7,-100),(100,7,100),(-100,7,100)],[],[(0,1,2,3)])
    back=bpy.data.objects.new('STUDIO_Backdrop',d);bpy.context.scene.collection.objects.link(back)
    back.hide_set(True)
    mat=bpy.data.materials.new('Studio vignette');ns=mat.node_tree.nodes;ns.clear();ln=mat.node_tree.links
    out=ns.new('ShaderNodeOutputMaterial');em=ns.new('ShaderNodeEmission');em.inputs['Strength'].default_value=.7
    geo=ns.new('ShaderNodeNewGeometry');dist=ns.new('ShaderNodeVectorMath');dist.operation='DISTANCE';dist.inputs[1].default_value=(0,7,5.4)
    ln.new(geo.outputs['Position'],dist.inputs[0]);div=ns.new('ShaderNodeMath');div.operation='DIVIDE';div.inputs[1].default_value=10
    ln.new(dist.outputs['Value'],div.inputs[0]);r=materials.ramp(ns,[(0,'667975'),(.45,'424e4f'),(.85,'101719')])
    ln.new(div.outputs[0],r.inputs[0]);ln.new(r.outputs['Color'],em.inputs[0]);ln.new(em.outputs[0],out.inputs['Surface']);d.materials.append(mat)

def main():
    if not bpy.app.background:raise RuntimeError('Build only in an isolated background process.')
    OUT.mkdir(parents=True,exist_ok=True);REVIEW.mkdir(parents=True,exist_ok=True)
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    for c in list(bpy.data.collections):
        if c.name!='Collection':bpy.data.collections.remove(c)
    high=bpy.data.collections.get('Collection');high.name='HIGH'
    for name in ('REF','LOW','RIG_CTRL','RIG_DEF','COLLISION','SOCKETS','EXPORT'):
        c=bpy.data.collections.new(name);bpy.context.scene.collection.children.link(c)
    print('Building layered materials',flush=True);m=materials.build()
    print('Building cranial and mantle forms',flush=True);anatomy.build_body(high,m)
    print('Building webbed bat wings',flush=True);wings.build(high,m)
    print('Building coiled arms and concave suckers',flush=True);anatomy.build_arms(high,m)
    print('Building facial plates and fins',flush=True);anatomy.build_face(high,m)
    print('Building interlaced oral tentacles',flush=True);anatomy.build_beard(high,m)
    print('Fusing and relaxing anatomical connections',flush=True);sculpt.fuse_core(high)
    configure()
    # The source image is packed as an editor-only reference, never a renderable mesh.
    im=bpy.data.images.load(str(OUT/'ref/user-reference.png'));im.pack()
    e=bpy.data.objects.new('REF_UserConcept',None);e.empty_display_type='IMAGE';e.data=im;e.empty_display_size=15.36
    e.rotation_euler=(math.pi/2,0,0);e.location=(0,4,5.12);e.hide_render=True;e.hide_viewport=True
    bpy.data.collections['REF'].objects.link(e)
    root=bpy.data.objects.new('CR_WingedCthulhu',None);high.objects.link(root)
    root['anatomy_revision']=2
    root['asset_status']='Static high-resolution reference reconstruction; not runtime optimized'
    root['reference']='ref/user-reference.png';root['inferred']='Back anatomy, thickness, obscured arm roots'
    for ob in high.objects:
        if ob.type=='MESH':
            ob.parent=root
            used={p.material_index for p in ob.data.polygons}
            for i in reversed(range(len(ob.data.materials))):
                if i not in used:ob.data.materials.pop(index=i)
    # A useful opening view, with the authored camera and material preview.
    for screen in bpy.data.screens:
        for ar in screen.areas:
            if ar.type=='VIEW_3D':
                ar.spaces.active.region_3d.view_perspective='CAMERA'
                ar.spaces.active.shading.type='MATERIAL'
                ar.spaces.active.overlay.show_overlays=False
    meshes=[ob for ob in high.objects if ob.type=='MESH']
    report={'asset':'CR_WingedCthulhu','blender':bpy.app.version_string,'objects':len(meshes),'vertices':sum(len(o.data.vertices) for o in meshes),'triangles_base':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes),'status':'HIGH sculpt source; no runtime/animation claim','inferred':['back anatomy','body depth','hidden roots','wing attachment']}
    (OUT/'build-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf8')
    bpy.context.scene.render.filepath=str(REVIEW/'reference-view.png')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CR_WingedCthulhu_master.blend'))
    print(json.dumps(report),flush=True)
    bpy.ops.render.render(write_still=True)
    print('REFERENCE_RENDER_DONE',flush=True)

if __name__=='__main__':main()
