"""Bind the baked game mesh, create the 24 game clips, and sample the same rig."""
from pathlib import Path
import sys,json,hashlib
import bpy,numpy as np
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(Path(__file__).resolve().parent))
from winged_cthulhu import rig as skeleton,motion
from animate_abyssal_watcher import assign,curves
OUT=ROOT/'source-assets/creatures/winged-cthulhu';EXPORT=OUT/'exports/unity'

def unity(p):return (p.x,p.z,p.y)

def main():
    assert bpy.app.background and bpy.data.objects.get('AW_Head')
    EXPORT.mkdir(parents=True,exist_ok=True);rig=skeleton.build();ref,oral=motion.init(rig)
    scene=bpy.context.scene;scene.render.fps=30
    clips=motion.registry();allbones=[b.name for b in rig.pose.bones];manifest=[];joint_data=[];body_data=[]
    loop_errors={};eye=bpy.data.objects['AW_Eye_Center']
    # Proxy bounds follow only the physical mantle/head, excluding wings and oral whiskers.
    mantle_rear=bpy.data.objects['AW_Head'].get('mantle_rear',2.4)
    corners=[Vector((x,y,z)) for x in (-2.25,2.25) for y in (-1.45,mantle_rear) for z in (4.1,10.02)]
    for cname,spec in clips.items():
        action=bpy.data.actions.new(cname);action.use_fake_user=True;assign(rig,action)
        owners=motion.owned(rig,spec);previous={};first=None;channels=[];joint_frames=[];body_frames=[]
        for fi in range(spec['frame_end']):
            scene.frame_set(fi+1)
            motion.apply(rig,spec['kind'],fi/30,spec['limb'],ref,oral)
            values=[]
            for bn in allbones:
                pb=rig.pose.bones[bn];q=pb.rotation_quaternion.copy()
                if bn in previous and previous[bn].dot(q)<0:q.negate();pb.rotation_quaternion=q
                previous[bn]=q.copy();values.append([*pb.location,*q,*pb.scale])
                for prop in ('location','rotation_quaternion','scale'):pb.keyframe_insert(prop,frame=fi+1,group=bn)
            channels.append(values);bpy.context.view_layer.update()
            joints=[]
            for key in skeleton.KEYS:
                points=[rig.pose.bones[skeleton.name(key,i)].head for i in range(skeleton.count(key))]
                points.append(rig.pose.bones[skeleton.name(key,skeleton.count(key)-1)].tail)
                for p,r in zip(points,rig['arm_radii'][key]):joints.append([*(round(c*1000) for c in unity(p)),round(float(r)*1.16*1000)])
            joint_frames.append(joints)
            hm=rig.pose.bones['DEF_Head'].matrix@rig.data.bones['DEF_Head'].matrix_local.inverted()
            bounds=np.asarray([unity(hm@p) for p in corners]);ev=eye.evaluated_get(bpy.context.evaluated_depsgraph_get())
            coords=np.asarray([unity(ev.matrix_world@v.co) for v in ev.data.vertices]);ec=(coords.min(axis=0)+coords.max(axis=0))*.5;radius=(coords.max(axis=0)-coords.min(axis=0)).max()*.5
            body_frames.append(np.round(np.r_[bounds.min(axis=0),bounds.max(axis=0),ec,radius]*1000).astype('<i4'))
        for curve in curves(action):
            for point in curve.keyframe_points:point.interpolation='LINEAR'
        for e in spec['events']:action.pose_markers.new(e['name']).frame=e['frame']
        if spec['loop']:
            error=float(np.max(np.abs(np.array(channels[0])-np.array(channels[-1]))));loop_errors[cname]=error;assert error<1e-5,error
        joint_data.append(np.asarray(joint_frames,dtype='<i4'));body_data.append(np.asarray(body_frames,dtype='<i4'))
        spec['bones']=owners
        manifest.append({'name':cname,'kind':spec['kind'],'limb':spec['limb'] or '', 'frameCount':spec['frame_end'],'durationSeconds':spec['duration_seconds'],'loop':spec['loop'],'bones':owners,'events':spec['events']})
        print('ANIMATED',cname,spec['frame_end'],flush=True)
    assign(rig,bpy.data.actions['AW_Idle']);scene.frame_set(1);scene.frame_start=1;scene.frame_end=181
    joints=np.concatenate(joint_data);body=np.concatenate(body_data);joints.tofile(EXPORT/'collision-joints.bin');body.tofile(EXPORT/'collision-body.bin')
    offset=0;limbs=[]
    for k in skeleton.KEYS:
        limbs.append({'id':k,'pointOffset':offset,'pointCount':skeleton.count(k)+1});offset+=skeleton.count(k)+1
    jo=bo=0;cs=[]
    for clip in manifest:
        cs.append(dict(clip,offsetBytes=jo,bodyOffsetBytes=bo));jo+=clip['frameCount']*2432;bo+=clip['frameCount']*40
    collision={'schema':1,'fps':30,'jointCount':152,'frameStrideBytes':2432,'bodyFrameStrideBytes':40,'limbs':limbs,'clips':cs,'radiusApproximation':'Parametric tube radii × 1.16; conservative capsule, independent of render triangles'}
    (EXPORT/'collision-manifest.json').write_text(json.dumps(collision,indent=2))
    meshes=[o for o in bpy.data.objects if o.type=='MESH' and o.parent==rig]
    for ob in meshes:ob.data.calc_loop_triangles()
    result={'schema':1,'asset':'WingedCthulhu','fps':30,'boneCount':len(rig.data.bones),'meshCount':len(meshes),'meshTriangles':sum(len(o.data.loop_triangles) for o in meshes),'clips':manifest,
        'controlRootWidth':(ref['A04'][0]-ref['A01'][0]).length,'forwardSocket':'SOCKET_Forward','centerSocket':'SOCKET_Center',
        'meshes':[{'name':o.name,'materials':[m.name for m in o.data.materials]} for o in meshes], 'loopMaxChannelErrors':loop_errors,
        'contract':'30 Hz confirmed-state sampling; independent severable limbs and per-limb ownership; no root motion animator'}
    (EXPORT/'unity-manifest.json').write_text(json.dumps(result,indent=2))
    # Do not embed abandoned high-mesh copies and unused baking materials in delivery.
    # All clips have fake users; active skins, studio and packed runtime textures survive.
    portrait=bpy.data.objects.get('CAM_SculptPortrait')
    if portrait:
        scene.camera=portrait
        scene.render.resolution_x,scene.render.resolution_y=1650,1130
        for ob in meshes:
            if ob.name.startswith('AW_Stump_'):
                ob.hide_set(True)
        for screen in bpy.data.screens:
            for area in screen.areas:
                if area.type=='VIEW_3D':
                    view=area.spaces.active
                    view.overlay.show_overlays=False
                    view.shading.type='MATERIAL'
                    view.region_3d.view_perspective='CAMERA'
                    view.region_3d.view_camera_zoom=5
    bpy.data.orphans_purge(do_recursive=True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CR_WingedCthulhu_animated.blend'),compress=True)
    print('ANIMATION_READY',len(manifest),len(rig.data.bones),result['meshTriangles'],flush=True)

if __name__=='__main__':main()
