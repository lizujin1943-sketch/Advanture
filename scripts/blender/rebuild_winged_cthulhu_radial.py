"""Replace the frontal fan with a complete eight-arm crown. Saved source only."""
from pathlib import Path
import sys
import json
import shutil
import bpy
import numpy as np

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from winged_cthulhu import radial_arms
from paint_winged_cthulhu import uv_and_coverage
from create_winged_cthulhu import camera

ROOT=HERE.parents[1];OUT=ROOT/'source-assets/creatures/winged-cthulhu'
ART=ROOT/'artifacts/winged-cthulhu'


def soften_dorsal_pattern():
    """Break up hidden-side chromatophores using domain warp and varied sizes."""
    mat=bpy.data.materials['PAINT_Cranium_SlateChromatophores']
    ns=mat.node_tree.nodes;ln=mat.node_tree.links
    for node in ns:
        if node.type=='VECT_MATH' and node.operation=='SCALE' and abs(node.inputs['Scale'].default_value-.085)<.01:
            node.inputs['Scale'].default_value=.38
        if node.type=='TEX_NOISE' and abs(node.inputs['Scale'].default_value-9)<.1:
            node.inputs['Scale'].default_value=3.4
    vor=next((n for n in ns if n.type=='TEX_VORONOI'),None)
    if vor:
        tex=next(n for n in ns if n.type=='TEX_COORD')
        warp=ns.new('ShaderNodeTexNoise');warp.inputs['Scale'].default_value=3.1;warp.inputs['Detail'].default_value=3.0
        ln.new(tex.outputs['Object'],warp.inputs['Vector'])
        amp=ns.new('ShaderNodeVectorMath');amp.operation='SCALE';amp.inputs['Scale'].default_value=.42
        ln.new(warp.outputs['Color'],amp.inputs[0])
        add=ns.new('ShaderNodeVectorMath');add.operation='ADD'
        ln.new(tex.outputs['Object'],add.inputs[0]);ln.new(amp.outputs[0],add.inputs[1]);ln.new(add.outputs[0],vor.inputs['Vector'])
        scale=ns.new('ShaderNodeMath');scale.operation='MULTIPLY_ADD'
        scale.inputs[1].default_value=.8;scale.inputs[2].default_value=.62
        noise=ns.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=1.4
        tex=next(n for n in ns if n.type=='TEX_COORD')
        ln.new(tex.outputs['Object'],noise.inputs['Vector']);ln.new(noise.outputs['Fac'],scale.inputs[0])
        multiply=ns.new('ShaderNodeMath');multiply.operation='MULTIPLY'
        consumers=list(vor.outputs['Distance'].links)
        ln.new(vor.outputs['Distance'],multiply.inputs[0]);ln.new(scale.outputs[0],multiply.inputs[1])
        for link in consumers:ln.new(multiply.outputs[0],link.to_socket)


def check_facial_symmetry(high):
    """Check the final fitted world-space surfaces, including eye depth and tilt."""
    errors={}
    for part in ('Eye','Socket','CheekFin'):
        points=[]
        for side in ('L','R'):
            ob=high.objects['CR_WingedCthulhu_'+part+'_'+side]
            v=np.empty(len(ob.data.vertices)*3,np.float32);ob.data.vertices.foreach_get('co',v)
            transform=np.array(ob.matrix_world)
            points.append(v.reshape(-1,3)@transform[:3,:3].T+transform[:3,3])
        left,right=points
        if left[:,0].mean()>-.2 or right[:,0].mean()<.2:raise ValueError(part+' orbital axis misplaced')
        left[:,0]*=-1
        errors[part]=float(np.linalg.norm(left-right,axis=1).max())
        if errors[part]>.00002:raise ValueError(f'{part} mirror error {errors[part]} m')
    return errors


def main():
    if not bpy.app.background:raise RuntimeError('Isolated source edit only')
    root=bpy.data.objects['CR_WingedCthulhu']
    if root.get('radial_revision'):raise RuntimeError('Start from the textured source, not the radial result')
    backup=ART/'pre-radial-textured.blend'
    if not backup.exists():shutil.copy2(bpy.data.filepath,backup)
    high=bpy.data.collections['HIGH']
    old=[o for o in high.objects if o.type=='MESH' and '_Arm_' in o.name]
    assert len(old)==10,len(old)
    for ob in old:bpy.data.objects.remove(ob,do_unlink=True)
    if root.get('anatomy_revision')!=3:raise ValueError('Rebuild the continuous mantle master and its painted source first')
    mats=[bpy.data.materials[n] for n in ['PAINT_Skin_TealAndOlive','PAINT_Ventral_DustyMauve','PAINT_Suckers_RoseLip','PAINT_Suckers_MauveBowl']]
    soften_dorsal_pattern()
    arms=[]
    for key in radial_arms.ANGLES:
        ob=radial_arms.create(key,high,mats);ob.parent=root;uv_and_coverage(ob);arms.append(ob)
        print('RADIAL_ARM',key,len(ob.data.vertices),flush=True)
    from winged_cthulhu.crown_seams import close_junctions
    core=high.objects['CR_WingedCthulhu_OrganicCore']
    close_junctions(core,arms)
    for ob in [core]+arms:uv_and_coverage(ob)
    root['radial_revision']='Eight circumferential arms; four front / four back; oral beard separate'
    root['inferred']='Posterior arm anatomy, mantle web, hidden suckers and wing attachment inferred from multi-view design'
    bpy.data.objects['STUDIO_Backdrop'].hide_render=True
    s=bpy.context.scene
    # True orthographic review cameras, including the formerly missing directions.
    for name,pos,target,width in [
        ('CAM_RadialFront',(0,-32,5.1),(0,0,5.1),17.2),
        ('CAM_RadialBack',(0,32,5.1),(0,0,5.1),17.2),
        ('CAM_RadialSide',(32,0,5.1),(0,0,5.1),16.0),
        ('CAM_RadialTop',(0,0,36),(0,0,0),17.0),
        ('CAM_RadialHero',(18,-27,17),(0,0,4.9),20.0),
    ]:camera(name,pos,target,width)
    s.camera=bpy.data.objects['CAM_RadialHero']
    s.render.resolution_x=1536;s.render.resolution_y=1024;s.render.resolution_percentage=100
    s.render.film_transparent=False;s.world.color=(.08,.1,.09)
    for screen in bpy.data.screens:
        for a in screen.areas:
            if a.type=='VIEW_3D':a.spaces.active.region_3d.view_camera_zoom=15
    manifest={'main_arm_count':8,'anatomy_revision':3,'mirror_error_m':check_facial_symmetry(high),'facial_tendrils':'Separate front-only oral anatomy, excluded from count',
        'design':'ref/inferred-turnaround-v1.png','view_note':'Generated design is inferred; orthographic mesh renders and numeric paths govern spatial verification',
        'arms':[{'id':o['limb_id'],'azimuth':o['azimuth_degrees'],'root':o['centerline'][0][:],
                 'distal':o['centerline'][-1][:],'controls':[list(p) for p in o['control_points']]} for o in arms],
        'status':'High sculpt; see exports/unity and review for runtime verification'}
    (OUT/'radial-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf8')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CR_WingedCthulhu_radial.blend'))
    s.render.engine='BLENDER_WORKBENCH';s.render.resolution_x=960;s.render.resolution_y=960
    s.display.shading.light='STUDIO';s.display.shading.color_type='SINGLE';s.display.shading.single_color=(.53,.53,.53)
    s.display.shading.show_cavity=True;s.display.shading.show_shadows=True
    for view in ('Front','Back','Side','Top','Hero'):
        s.camera=bpy.data.objects['CAM_Radial'+view]
        s.render.filepath=str(OUT/'review'/('radial-clay-'+view.lower()+'.png'));bpy.ops.render.render(write_still=True)
    print('RADIAL_MODEL_SAVED',flush=True)


if __name__=='__main__':main()
