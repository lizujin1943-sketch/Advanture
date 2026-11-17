"""Replace the frontal fan with a complete eight-arm crown. Saved source only."""
from pathlib import Path
import sys
import math
import json
import shutil
import bpy
import numpy as np
from mathutils import Vector

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from winged_cthulhu import radial_arms,sculpt
from winged_cthulhu.geometry import Mesh
from paint_winged_cthulhu import uv_and_coverage
from create_winged_cthulhu import camera

ROOT=HERE.parents[1];OUT=ROOT/'source-assets/creatures/winged-cthulhu'
ART=ROOT/'artifacts/winged-cthulhu'


def shorten_mantle(core):
    """Remove the hanging posterior stump while keeping front facial tendrils."""
    me=core.data;v=np.empty(len(me.vertices)*3,np.float32);me.vertices.foreach_get('co',v);v=v.reshape(-1,3)
    rear=np.clip((v[:,1]+.85)/.75,0,1);rear=rear*rear*(3-2*rear)
    height=np.clip((5.2-v[:,2])/1.5,0,1);height=height*height*(3-2*height)
    lateral=np.clip((2.6-np.abs(v[:,0]-.45))/.8,0,1)
    weight=rear*height*lateral
    v[:,2]+=np.maximum(0,4.02-v[:,2])*.88*weight
    me.vertices.foreach_set('co',v.ravel());me.update()
    uv_and_coverage(core)


def crown(col,skin,root):
    # Smooth saddle web between the eight arm bases, entirely surrounding the body.
    g=Mesh();nu=128;nv=28;rings=[]
    # The dorsal mantle is a continuous muscular saddle, not a straight collar.
    g.ellipsoid((.1,.2,5.55),(1.88,1.94,1.75),nu=96,nv=64,
        deform=lambda q,ph,a:q*(1+.018*math.cos(a*8)*math.sin(ph)**3))
    for j in range(nv+1):
        f=j/nv;row=[]
        for k in range(nu):
            a=math.tau*k/nu
            # Eight shallow lobes, not separate sphere-like root lumps.
            lobe=.5+.5*math.cos(8*a)
            r=(1.38+.26*math.sin(math.pi*f)+.52*f*f+.15*lobe*f*f)
            z=5.1-1.18*f-.12*(1-lobe)*f*f
            row.append(g.vert((r*math.sin(a),-r*math.cos(a),z),(k/nu,f)))
        rings.append(row)
    for a,b in zip(rings,rings[1:]):
        for k in range(nu):g.face((a[k],b[k],b[(k+1)%nu],a[(k+1)%nu]))
    for ring,z,rev in [(rings[0],5.1,True),(rings[-1],3.85,False)]:
        ix=g.vert((0,0,z))
        for k in range(nu):
            f=(ix,ring[k],ring[(k+1)%nu]);g.face(f[::-1] if rev else f)
    ob=g.finish('CR_WingedCthulhu_ArmCrown',col,[skin]);ob.parent=root;uv_and_coverage(ob)
    ob['inferred']='Circumferential webbed arm crown from multi-view design'
    return ob


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
    shorten_mantle(bpy.data.objects['CR_WingedCthulhu_OrganicCore'])
    mats=[bpy.data.materials[n] for n in ['PAINT_Skin_TealAndOlive','PAINT_Ventral_DustyMauve','PAINT_Suckers_RoseLip','PAINT_Suckers_MauveBowl']]
    web=crown(high,mats[0],root)
    # Fuse the new web into the existing mantle before attaching distinct limbs.
    # The resulting surface is continuous instead of a visible cylindrical collar.
    fused=sculpt.fuse_core(high,include=('CR_WingedCthulhu_OrganicCore',web.name))
    fused.parent=root;uv_and_coverage(fused)
    soften_dorsal_pattern()
    arms=[]
    for key in radial_arms.ANGLES:
        ob=radial_arms.create(key,high,mats);ob.parent=root;uv_and_coverage(ob);arms.append(ob)
        print('RADIAL_ARM',key,len(ob.data.vertices),flush=True)
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
    manifest={'main_arm_count':8,'facial_tendrils':'Separate front-only oral anatomy, excluded from count',
        'design':'ref/inferred-turnaround-v1.png','view_note':'Generated design is inferred; orthographic mesh renders and numeric paths govern spatial verification',
        'arms':[{'id':o['limb_id'],'azimuth':o['azimuth_degrees'],'root':o['centerline'][0][:],
                 'distal':o['centerline'][-1][:],'controls':[list(p) for p in o['control_points']]} for o in arms],
        'status':'Spatial model revision; rigging, animation, Unity replacement and push pending'}
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
