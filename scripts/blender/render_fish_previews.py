"""Render the authored meshes, not generated concept art. Run after opening the blend."""
from pathlib import Path
import argparse
import json
import math
import sys
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser()
parser.add_argument('--animation',action='store_true')
parser.add_argument('--only',default='')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
source=Path(bpy.data.filepath).parent
manifest=json.loads((source/'manifest.json').read_text())
output=ROOT/'artifacts/fish-models'
if 'draft' in str(source):
    output=output/'draft'
output.mkdir(parents=True,exist_ok=True)
scene=bpy.context.scene
camera=scene.camera
meshes=[obj for obj in scene.objects if obj.type=='MESH']
for entry in manifest['species']:
    rig=bpy.data.objects[entry['rig']]
    rig.location=(0,0,0)
    rig.rotation_euler=(0,0,0)
for mesh in meshes:
    mesh.hide_render=True


def set_action(entry,name,t):
    rig=bpy.data.objects[entry['rig']]
    info=next(item for item in entry['clips'] if item['name']==name)
    rig.animation_data.action=bpy.data.actions[info['action']]
    return 1+t*(info['frames']-1)


def update(frame):
    scene.frame_set(int(frame),subframe=frame-int(frame))
    bpy.context.view_layer.update()


def frame_camera(objects,view=(5,-1.15,1.7),padding=1.16):
    depsgraph=bpy.context.evaluated_depsgraph_get()
    points=[]
    for obj in objects:
        evaluated=obj.evaluated_get(depsgraph)
        data=evaluated.to_mesh()
        points.extend(evaluated.matrix_world @ vertex.co for vertex in data.vertices)
        evaluated.to_mesh_clear()
    low=Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high=Vector(tuple(max(p[i] for p in points) for i in range(3)))
    center=(low+high)*.5
    camera.location=center+Vector(view).normalized()*max((high-low).length*4,1)
    camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
    right=camera.rotation_euler.to_matrix() @ Vector((1,0,0))
    up=camera.rotation_euler.to_matrix() @ Vector((0,1,0))
    w=max(p.dot(right) for p in points)-min(p.dot(right) for p in points)
    h=max(p.dot(up) for p in points)-min(p.dot(up) for p in points)
    camera.data.ortho_scale=max(w,h*scene.render.resolution_x/scene.render.resolution_y)*padding


if not args.animation:
    scene.render.resolution_x=640
    scene.render.resolution_y=480
    scene.cycles.samples=16
    wanted=set(args.only.split(',')) if args.only else None
    for entry in manifest['species']:
        if wanted is not None and entry['id'] not in wanted:
            continue
        mesh=bpy.data.objects[entry['mesh']]
        mesh.hide_render=False
        update(set_action(entry,'Idle',0))
        frame_camera([mesh])
        scene.render.filepath=str(output/(entry['id']+'.png'))
        bpy.ops.render.render(write_still=True)
        print('FISH_RENDERED',entry['id'],flush=True)
        if entry['id'] in ('fish_03','fish_04','fish_05','fish_08','fish_10','piranha'):
            for name,direction in [('top',(0,0,1)),('front',(0,-1,.15)),('side',(1,0,.08))]:
                frame_camera([mesh],direction)
                scene.render.filepath=str(output/(entry['id']+'_'+name+'.png'))
                bpy.ops.render.render(write_still=True)
        mesh.hide_render=True
else:
    chosen=[entry for entry in manifest['species'] if entry['id'] in ('fish_03','fish_05','piranha')]
    scene.render.resolution_x=840
    scene.render.resolution_y=320
    scene.cycles.samples=8
    directory=output/'animation-frames'
    directory.mkdir(exist_ok=True)
    for index,entry in enumerate(chosen):
        rig=bpy.data.objects[entry['rig']]
        rig.location=((index-1)*1.55,0,0)
        rig.rotation_euler.z=-math.pi/2
        rig.scale=(1/entry['length_m'],)*3
        bpy.data.objects[entry['mesh']].hide_render=False
    camera.location=(0,-8,3.4)
    camera.rotation_euler=(Vector((0,0,0))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=5.35
    clips=['Swim','Leap','Dive','Hooked','Flop','Bite']
    frames=[]
    for clip in clips:
        for step in range(8):
            t=step/7
            # All selected clips share phase; use each rig's own action frame range.
            frame=1+t*30
            for entry in chosen:
                bpy.data.objects[entry['rig']].rotation_euler.z=-math.pi/2+(.65 if clip=='Bite' and entry['id']=='piranha' else 0)
                action_name=clip if any(c['name']==clip for c in entry['clips']) else 'Idle'
                set_action(entry,action_name,t)
                action=bpy.data.actions[next(c['action'] for c in entry['clips'] if c['name']==action_name)]
                # Scale action playback using an NLA strip so different clip lengths
                # sample the same normalized phase in the one shared scene frame.
                rig=bpy.data.objects[entry['rig']]
                rig.animation_data.action=None
                track=rig.animation_data.nla_tracks.get('Preview')
                if track is None:
                    track=rig.animation_data.nla_tracks.new()
                    track.name='Preview'
                for strip in list(track.strips):
                    track.strips.remove(strip)
                strip=track.strips.new(action_name,1,action)
                strip.scale=30/(action.frame_range[1]-action.frame_range[0])
                strip.extrapolation='HOLD'
                track.mute=False
            update(frame)
            path=directory/f'{len(frames):03d}.png'
            scene.render.filepath=str(path)
            bpy.ops.render.render(write_still=True)
            frames.append({'path':path.name,'clip':clip,'phase':t})
        print('FISH_ANIMATION_RENDERED',clip,flush=True)
    (directory/'frames.json').write_text(json.dumps(frames,indent=2))
