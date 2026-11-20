"""Reimport the actual FBXs, check geometry/axes/materials, and light the art proofs."""
import argparse
import hashlib
import json
from math import pi
from pathlib import Path
import sys
import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
from atlas_layout import GAME, PROJECT, PROOFS, SOURCE, TEXTURES
from build import surface
from exterior import PIVOTS, carry_point
from hands import CARRY_FINGERS, fit_ready_hand
from mesh_tools import blender_point


def game_point(v):
    return [v.x,v.z,v.y]


def rounded(values):
    return [round(float(x),6) for x in values]


def inspect_editable_source():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'AK47.blend'))
    layer=bpy.context.view_layer
    hidden=[name for name in ('FiringHand','SupportHand','SupportCarry')
            if layer.layer_collection.children['Art | '+name].hide_viewport]
    viewport=next(area.spaces.active for area in bpy.context.screen.areas if area.type=='VIEW_3D')
    packed={}
    for name in TEXTURES:
        image=bpy.data.images[name]
        if not image.packed_file:
            raise ValueError('Missing packed texture in artist source: '+name)
        digest=hashlib.sha256(bytes(image.packed_file.data)).hexdigest()
        if digest!=hashlib.sha256((GAME/name).read_bytes()).hexdigest():
            raise ValueError('Packed texture differs from game texture: '+name)
        packed[name]=digest
    if len(hidden)!=3 or viewport.region_3d.view_perspective!='ORTHO':
        raise ValueError('The artist source must reopen in its exterior inspection view')
    return {'file':'AK47.blend','viewLayer':layer.name,'hiddenHandCollections':hidden,
            'orthographic':True,'packedTextureSha256':packed}


def read_fbx(stem):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(GAME/(stem+'.fbx')),use_image_search=False,use_anim=False)
    return {o.name:o for o in bpy.context.scene.objects}


def audit(stem,objects):
    assert set(PIVOTS)==set(objects),(set(PIVOTS),set(objects))
    data={}
    all_points=[]
    for name,obj in objects.items():
        mat=obj.matrix_world
        origin=game_point(mat.translation)
        x,f,h=PIVOTS[name]
        if max(abs(a-b) for a,b in zip(origin,(x,h,f)))>.0001:
            raise ValueError(f'{stem} {name} bind moved to {origin}, expected {(x,h,f)}')
        info={'pivotUnity':rounded(origin),'scale':rounded(mat.to_scale())}
        if obj.type=='EMPTY':
            # FBX declares -Z forward / Y up before the project importer bakes the axes.
            orient=mat.to_3x3()
            forward=game_point((orient@Vector((0,0,-1))).normalized())
            up=game_point((orient@Vector((0,1,0))).normalized())
            if max(abs(a-b) for a,b in zip(forward+up,[0,0,1,0,1,0]))>.0001:
                raise ValueError(stem+' has a rotated animation socket: '+name)
            info.update(forwardUnity=rounded(forward),upUnity=rounded(up))
        if obj.type=='MESH':
            mesh=obj.data
            info['materials']=[s.material.name for s in obj.material_slots]
            assert info['materials']==['AK47_Surface'],info['materials']
            points=[game_point(mat@vert.co) for vert in mesh.vertices]
            all_points.extend(points)
            info['boundsUnity']=[rounded(np.min(points,axis=0)),rounded(np.max(points,axis=0))]
            bm=bmesh.new()
            bm.from_mesh(mesh)
            info['triangles']=sum(len(p.verts)-2 for p in bm.faces)
            info['openEdges']=sum(not e.is_manifold for e in bm.edges)
            info['inconsistentEdges']=sum(e.is_manifold and not e.is_contiguous for e in bm.edges)
            info['degenerateTris']=sum(f.calc_area()<1e-12 for f in bm.faces)
            bm.free()
            mesh.calc_loop_triangles()
            uv=mesh.uv_layers.active
            coords=np.array([v.uv[:] for v in uv.data])
            uv_tri=coords[np.array([t.loops[:] for t in mesh.loop_triangles])]
            a,b=uv_tri[:,1]-uv_tri[:,0],uv_tri[:,2]-uv_tri[:,0]
            info['zeroUvArea']=int(np.count_nonzero(abs(a[:,0]*b[:,1]-a[:,1]*b[:,0])<1e-13))
            info['uvOutsideUnitSquare']=int(np.count_nonzero((coords<0)|(coords>1)))
            info['oneUVSet']=len(mesh.uv_layers)==1
            for check in ('openEdges','inconsistentEdges','degenerateTris','zeroUvArea','uvOutsideUnitSquare'):
                if info[check]:
                    raise ValueError(f'{stem} {name}: {check}={info[check]}')
            if not info['oneUVSet'] or max(abs(s-1) for s in info['scale'])>1e-4:
                raise ValueError(stem+' has an invalid UV set or bind scale on '+name)
        data[name]=info
    total=sum(o.get('triangles',0) for o in data.values())
    return {'file':stem+'.fbx','residentMeshGroups':sum(o.type=='MESH' for o in objects.values()),
            'renderersPerPose':sum(o.type=='MESH' for o in objects.values())-1,
            'residentTriangles':total,'readyTriangles':total-data['SupportCarry']['triangles'],
            'carryTriangles':total-data['SupportHand']['triangles'],
            'boundsUnity':[rounded(np.min(all_points,axis=0)),rounded(np.max(all_points,axis=0))],
            'groups':data}


def contact_audit(tool_dir,objects):
    sys.path.insert(0,str(tool_dir))
    import qa_check
    evidence=qa_check.run(objects=[o.name for o in objects.values() if o.type=='MESH'],
                          profile='game',eps=.000006,tol=.0011,uv=True)
    return {k:evidence[k] for k in ('pass','errors','warnings','zfighting','floating','back_to_back')}


def place_at_socket(objects):
    glove=objects['SupportCarry']
    original=glove.matrix_world.copy()
    position=original.copy()
    position.translation=objects['CarrySocket'].matrix_world.translation
    glove.matrix_world=position
    bpy.context.view_layer.update()
    return original


def inspect_contact(objects):
    def tree(name):
        obj=objects[name]
        return BVHTree.FromPolygons([obj.matrix_world@v.co for v in obj.data.vertices],
                                    [p.vertices[:] for p in obj.data.polygons],all_triangles=True)
    original=place_at_socket(objects)
    carry,mag=tree('SupportCarry'),tree('CurvedMagazine')
    count=len(mag.overlap(carry))
    samples={'palm':(-.0268,-.01,.006),'thumb':(-.008,-.054,-.030)}
    samples.update({'digit'+str(i+1):(-.004,.052,d+.002) for i,(d,_) in enumerate(CARRY_FINGERS)})
    out={}
    for name,coords in samples.items():
        contact=mag.find_nearest(carry_point(*coords))[0]
        out[name]=round(carry.find_nearest(contact)[3],6)
    body=tree('Frame')
    def pad_distances(group,samples,right):
        glove=tree(group)
        out={}
        for key,sample in samples.items():
            bodypoint=body.find_nearest(fit_ready_hand(sample,right))[0]
            out[key]=round(glove.find_nearest(bodypoint)[3],6)
        return out
    thumbs=pad_distances('FiringHand',{'innerPad':(-.014,-.127,-.091),
        'tip':(-.021,-.118,-.093),'middleFinger':(-.010,-.083,-.104)},True)
    supporting=pad_distances('SupportHand',{'thumb':(-.043,.272,-.005),
        'index':(.0445,.245,-.027),'middle':(.045,.223,-.024),
        'ring':(.043,.203,-.028),'little':(.041,.185,-.032)},False)
    objects['SupportCarry'].matrix_world=original
    bpy.context.view_layer.update()
    if count==0 or max(out.values())>.016:
        raise ValueError(f'Carry hand missed its prop: {count} contacts {out}')
    if max(thumbs.values())>.014 or max(supporting.values())>.012:
        raise ValueError(f'Ready hands missed the exterior: {thumbs} / {supporting}')
    return {'carryTriangleIntersections':count,'carrySurfaceDistances':out,'gripSurfaceDistances':thumbs,
            'foreEndSurfaceDistances':supporting}


def aim(obj,eye,target,screen_right=None):
    pos,point=blender_point(eye),blender_point(target)
    obj.location=pos
    direction=(point-pos).normalized()
    if screen_right:
        z=-direction
        rx=blender_point(screen_right)
        rx=(rx-z*rx.dot(z)).normalized()
        ry=z.cross(rx)
        obj.rotation_euler=Matrix((rx,ry,z)).transposed().to_euler()
    else:
        obj.rotation_euler=direction.to_track_quat('-Z','Y').to_euler()


def studio(scene,objects):
    mat=surface()
    for obj in objects.values():
        if obj.type=='MESH':
            obj.data.materials[0]=mat
    world=bpy.data.worlds.new('Softbox fill')
    scene.world=world
    bg=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND')
    bg.inputs['Color'].default_value=(.52,.57,.63,1)
    bg.inputs['Strength'].default_value=.67
    def strip(name,eye,power,size,height,color):
        data=bpy.data.lights.new(name,'AREA')
        data.shape='RECTANGLE'; data.energy=power; data.size=size; data.size_y=height; data.color=color
        obj=bpy.data.objects.new(name,data)
        scene.collection.objects.link(obj)
        aim(obj,eye,(0,.12,0))
    strip('Overhead silk',(.38,-.22,.88),41,.88,.28,(1,.89,.76))
    strip('Broad cool window',(-.76,-.20,.55),32,.85,.63,(.77,.88,1))
    strip('Long side reflection',(.66,.59,.44),38,.87,.16,(.87,.94,1))
    strip('Warm grazing light',(-.42,.63,.34),22,.72,.18,(1,.74,.54))
    strip('Low bounce',(.50,-.14,-.29),8,.79,.45,(.82,.88,1))
    camdata=bpy.data.cameras.new('Inspection lens')
    camdata.clip_start=.001
    camera=bpy.data.objects.new('Review camera',camdata)
    scene.collection.objects.link(camera)
    scene.camera=camera
    scene.render.engine='CYCLES'
    scene.cycles.samples=18
    scene.cycles.use_denoising=True
    scene.cycles.max_bounces=3
    scene.render.film_transparent=True
    scene.render.image_settings.file_format='PNG'
    scene.render.image_settings.color_mode='RGBA'
    scene.render.resolution_percentage=100
    scene.view_settings.view_transform='AgX'
    scene.view_settings.look='AgX - Medium High Contrast'
    scene.view_settings.exposure=-1.95
    return camera


SHOTS={
    'hero':((1.44,-.49,.52),(0,.111,-.065),1.17,(1540,725),False,None),
    'profile':((2,.126,.00),(0,.126,-.060),1.18,(1060,510),False,None),
    'top':((0,.126,2),(0,.126,0),1.18,(1060,332),False,(0,1,0)),
    'front':((.015,1.6,.075),(0,.12,-.075),.49,(460,460),False,None),
    'back':((-.015,-1.35,.060),(0,.12,-.075),.49,(460,460),False,None),
    'receiver':((.82,-.245,.35),(0,.035,.010),.488,(864,460),False,None),
    'walnut':((.54,.66,.51),(0,.245,.018),.387,(864,460),False,None),
    'hands':((-.62,-.40,.16),(-.007,.071,-.062),.71,(1090,570),True,None),
    'dorsal':((.76,.40,-.24),(.004,.025,-.092),.65,(900,510),True,None),
    'carry':((-.60,-.29,.10),(-.009,.078,-.163),.37,(870,660),True,None),
    'carry-reverse':((.54,.52,-.05),(-.008,.074,-.163),.36,(870,660),True,None),
    'wire':((1.44,-.49,.52),(0,.111,-.065),1.17,(1060,510),False,None),
}


def render_set(objects,selected,fast):
    scene=bpy.context.scene
    # Return the reimported release mesh to the artist's turntable coordinate frame.
    turn=Matrix.Rotation(pi,4,'Z')
    for obj in objects.values():
        obj.matrix_world=turn@obj.matrix_world
    cam=studio(scene,objects)
    paint=objects['Frame'].data.materials[0]
    clay=bpy.data.materials.new('Grey form proof')
    shader=next(n for n in clay.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    shader.inputs['Base Color'].default_value=(.25,.28,.27,1)
    shader.inputs['Roughness'].default_value=.50
    dest=PROJECT/'.build/ak47-art/renders'
    dest.mkdir(parents=True,exist_ok=True)
    shots=dict(SHOTS)
    for key in ('profile','top','front','back','dorsal'):
        shots['clay-'+key]=SHOTS[key]
    reset_pose=objects['SupportCarry'].matrix_world.copy()
    for name,(eye,target,scale,size,hands,right) in shots.items():
        if selected and name not in selected:
            continue
        show_carry=name in ('carry','carry-reverse')
        objects['SupportCarry'].matrix_world=reset_pose
        bpy.context.view_layer.update()
        if show_carry:
            place_at_socket(objects)
        for group,obj in objects.items():
            if obj.type!='MESH':
                continue
            if show_carry:
                obj.hide_render=group not in ('CurvedMagazine','SupportCarry')
            else:
                obj.hide_render=group=='SupportCarry' or (group in ('FiringHand','SupportHand') and not hands)
        for obj in objects.values():
            if obj.type=='MESH':
                obj.data.materials[0]=clay if name.startswith('clay-') else paint
        aim(cam,eye,target,right)
        cam.data.type='ORTHO'
        cam.data.ortho_scale=scale
        scene.render.resolution_x,scene.render.resolution_y=size
        scene.render.resolution_percentage=55 if fast else 100
        scene.cycles.samples=5 if fast else 18
        if name=='wire':
            wire=bpy.data.materials.new('Triangle proof')
            nodes=wire.node_tree.nodes
            b=next(x for x in nodes if x.type=='BSDF_PRINCIPLED')
            wires=nodes.new('ShaderNodeWireframe')
            wires.use_pixel_size=True
            wires.inputs[0].default_value=.75
            mix=nodes.new('ShaderNodeMixRGB')
            mix.inputs[1].default_value=(.24,.28,.26,1)
            mix.inputs[2].default_value=(.016,.023,.023,1)
            wire.node_tree.links.new(wires.outputs[0],mix.inputs[0])
            wire.node_tree.links.new(mix.outputs[0],b.inputs['Base Color'])
            for o in objects.values():
                if o.type=='MESH':
                    o.data.materials[0]=wire
        scene.render.filepath=str(dest/((name+'-draft' if fast else name)+'.png'))
        bpy.ops.render.render(write_still=True)


def main():
    args=argparse.ArgumentParser()
    args.add_argument('--shot',nargs='*')
    args.add_argument('--audit-only',action='store_true')
    args.add_argument('--asset-qa',type=Path,help='Optional directory containing the Blender asset QA script')
    args.add_argument('--fast',action='store_true')
    opt=args.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    proof={'packing':{'base':'sRGB','metallic':'R','smoothness':'A','normal':'tangent OpenGL +Y'},
           'textureSize':[2048,2048],'textureFiles':TEXTURES,'editable':inspect_editable_source()}
    extra={}
    for stem in ('AK47_World','AK47_View'):
        objects=read_fbx(stem)
        proof[stem]=audit(stem,objects)
        proof[stem]['poseContact']=inspect_contact(objects)
        if opt.asset_qa:
            ready={name:o for name,o in objects.items() if name!='SupportCarry'}
            first=contact_audit(opt.asset_qa,ready)
            original=place_at_socket(objects)
            second=contact_audit(opt.asset_qa,{name:objects[name] for name in ('SupportCarry','CurvedMagazine')})
            objects['SupportCarry'].matrix_world=original
            bpy.context.view_layer.update()
            extra[stem]={'pass':first['pass'] and second['pass'],'ready':first,'carry':second}
    PROOFS.mkdir(parents=True,exist_ok=True)
    (PROOFS/'ModelQA.json').write_text(json.dumps(proof,indent=2)+'\n')
    if extra:
        extra['reviewedInterfaces']=[
            'Surface UV islands deliberately reference shared regions of the trim atlas.',
            'One opposed triangle pair is hidden under the world glove cuff fastening.',
            'The model origin is the animated view bind, above the magazine foot.']
        (PROOFS/'SurfaceQA.json').write_text(json.dumps(extra,indent=2)+'\n')
        if any(not extra[s]['pass'] for s in ('AK47_World','AK47_View')):
            raise ValueError('Inspect SurfaceQA.json before accepting this export')
    if not opt.audit_only:
        render_set(objects,opt.shot,opt.fast)


if __name__=='__main__':
    main()
