"""Editable fish skeletons and in-place, baked cosmetic animation clips."""
import math
import bpy
from mathutils import Matrix, Quaternion, Vector
from fish_geometry import v, radius_at


def make_rig(spec,collection):
    data=bpy.data.armatures.new(spec['id']+'_Skeleton')
    rig=bpy.data.objects.new(spec['id']+'_Rig',data)
    collection.objects.link(rig)
    for obj in bpy.context.selected_objects:
        obj.select_set(False)
    rig.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode='EDIT')
    tail=spec['profile'][0][0]
    count=7 if spec['id']=='fish_10' else 2
    xs=[-.11+(tail+.11)*i/count for i in range(count)]
    centres=[('Body',.10)]+[(f'Spine_{i+1:02d}',x) for i,x in enumerate(xs)]+[('Tail',tail)]
    spec['_centres']=centres
    bones=[('Root',(0,0,0),(.12,0,0),None),
           ('Body',(.1,0,0),(-.11,0,0),'Root'),
           ('Head',(.1,0,0),(spec['profile'][-1][0],0,0),'Body'),
           ('Jaw',(spec['profile'][-1][0]-.09,0,-.085),
                   (spec['profile'][-1][0]+.06,0,-.085),'Head')]
    parent='Body'
    for i,x in enumerate(xs):
        end=xs[i+1] if i+1<count else tail
        bones.append((f'Spine_{i+1:02d}',(x,0,0),(end,0,0),parent))
        parent=f'Spine_{i+1:02d}'
    bones.extend([
        ('Tail',(tail,0,0),(tail-.23,0,0),parent),
        ('Dorsal',(-.06,0,radius_at(spec,-.06)[1]*.80),(-.26,0,.52),'Body'),
        ('Ventral',(-.04,0,-radius_at(spec,-.04)[1]*.78),(-.24,0,-.46),'Body'),
    ])
    for sign,side in [(1,'L'),(-1,'R')]:
        y=radius_at(spec,.10)[0]*.82
        bones.append(('Pectoral_'+side,(.10,sign*y,-.05),
                      (-.18,sign*(y+.20),-.15),'Body'))
    for name,head,tip,parent in bones:
        bone=data.edit_bones.new(name)
        bone.head,bone.tail=v(head),v(tip)
        if parent:
            bone.parent=data.edit_bones[parent]
        bone.use_connect=False
        bone.use_deform=True
    if '_reference' in spec:
        from fish_reference_geometry import coord, curve_samples
        art=spec['_reference']
        # Hinge at the cheek, rather than at the tip of the snout. This makes
        # the broad piranha jaw open without swinging the lips off the face.
        jaw=data.edit_bones['Jaw']
        jaw.head=v(coord(art,art['jaw']))
        jaw.tail=v(coord(art,(art['jaw'][0]-45,art['jaw'][1]+2)))
        for side,sign in [('L',1),('R',-1)]:
            first=next(p for p in art['parts'] if p['paired'])
            px,py=first['points'][0]
            from fish_reference_geometry import depth_at
            head=coord(art,(px,py),sign*depth_at(art,px,py))
            bone=data.edit_bones['Pectoral_'+side]
            bone.head=v(head)
            bone.tail=v((head[0]-.18,head[1]+sign*.18,head[2]-.11))
        if art.get('ribbon'):
            samples=curve_samples(art,10)
            chain=['Head','Body']+[f'Spine_{i:02d}' for i in range(1,8)]+['Tail']
            for i,name in enumerate(chain):
                bone=data.edit_bones[name]
                bone.head=v(coord(art,samples[i][:2]))
                if i+1<len(chain):
                    bone.tail=v(coord(art,samples[i+1][:2]))
                else:
                    bone.tail=bone.head+v((-.04,0,-.02))
            # The head must not parent the first body joint; the complete
            # chain follows the reference S from Body through Tail.
            data.edit_bones['Head'].parent=data.edit_bones['Body']
    bpy.ops.object.mode_set(mode='OBJECT')
    rig.show_in_front=True
    rig['species_id']=spec['id']
    rig['display_name']=spec['name']
    rig['forward']='Blender -Y / Unity +Z'
    rig['root_motion']=False
    return rig


def local_rotation(rig,name,axis,angle):
    """Convert an intuitive fish-space rotation axis into the bone's rest axes."""
    bone=rig.data.bones[name]
    local_axis=bone.matrix_local.to_quaternion().inverted() @ v(axis)
    return Quaternion(local_axis.normalized(),math.radians(angle))


def pose(rig,spec,clip,t):
    for pb in rig.pose.bones:
        pb.matrix_basis=Matrix.Identity(4)
        pb.rotation_mode='QUATERNION'
    cycle=math.tau*t
    speed={'Idle':1,'Swim':1,'Burst':2,'Hooked':2,'Flop':2}.get(clip,1)
    amplitude={'Idle':3,'Swim':11,'Burst':16,'Hooked':21,'Flop':26}.get(clip,8)
    if clip in ('Leap','Dive','Bite'):
        envelope=math.sin(math.pi*t)**2
    else:
        envelope=1
    spines=[name for name,_ in spec['_centres'] if name!='Body']
    for i,name in enumerate(spines):
        angle=math.sin(cycle*speed-i*.77)*(amplitude*(.65+i*.20))*envelope
        if spec['id']=='fish_10':
            angle*=.62
        rig.pose.bones[name].rotation_quaternion=local_rotation(rig,name,(0,0,1),angle)
    body=rig.pose.bones['Body']
    body.rotation_quaternion=local_rotation(rig,'Body',(0,0,1),math.sin(cycle*speed)*amplitude*.10*envelope)
    for side,sign in [('L',1),('R',-1)]:
        name='Pectoral_'+side
        fin_angle=math.sin(cycle*speed+.4*sign)*(10 if clip=='Idle' else 19)*envelope
        rig.pose.bones[name].rotation_quaternion=local_rotation(rig,name,(1,0,0),sign*fin_angle)
    rig.pose.bones['Dorsal'].rotation_quaternion=local_rotation(rig,'Dorsal',(1,0,0),math.sin(cycle)*4*envelope)
    rig.pose.bones['Ventral'].rotation_quaternion=local_rotation(rig,'Ventral',(1,0,0),math.sin(cycle+.6)*6*envelope)
    jaw=0
    pitch=roll=0
    if clip=='Idle':
        jaw=2+math.sin(cycle)*2
    elif clip=='Burst':
        pitch=math.sin(cycle*2)*3
        jaw=4+math.sin(cycle)*2
    elif clip=='Leap':
        # Upright launch, a small corkscrew, then neutral at the state boundary.
        pitch=math.sin(math.pi*t)*-24
        roll=math.sin(cycle)*9*envelope
        jaw=9*envelope
    elif clip=='Dive':
        pitch=math.sin(math.pi*t)*28
        jaw=4*envelope
        for side,sign in [('L',1),('R',-1)]:
            name='Pectoral_'+side
            rig.pose.bones[name].rotation_quaternion=local_rotation(rig,name,(0,0,1),sign*19*envelope)
    elif clip=='Hooked':
        roll=math.sin(cycle*2)*16
        pitch=math.sin(cycle*2+.3)*9
        jaw=11+math.sin(cycle*2)*7
        if spec.get('spikes'):
            body.scale=(1.18,1.10,1.16)
    elif clip=='Flop':
        roll=61+math.sin(cycle*2)*23
        pitch=math.sin(cycle*2)*11
        jaw=12+math.sin(cycle*2+.6)*8
    elif clip=='Bite':
        # Clear anticipation, fast shut, a short overshoot, then recover.
        if t<.40:
            jaw=34*math.sin(math.pi*.5*t/.40)
        elif t<.60:
            jaw=34*(1-(t-.40)/.20)
        else:
            jaw=3*math.sin(math.pi*(t-.60)/.40)
        pitch=-8*math.sin(math.pi*t)
        rig.pose.bones['Head'].rotation_quaternion=local_rotation(rig,'Head',(0,1,0),-jaw*.15)
    body.rotation_quaternion=local_rotation(rig,'Body',(0,1,0),pitch) @ body.rotation_quaternion
    body.rotation_quaternion=local_rotation(rig,'Body',(1,0,0),roll) @ body.rotation_quaternion
    rig.pose.bones['Jaw'].rotation_quaternion=local_rotation(rig,'Jaw',(0,1,0),jaw)
    # Root transform is deliberately constant. World trajectories remain authoritative.
    bpy.context.view_layer.update()


def actions_for(rig,spec,scene):
    rig.animation_data_create()
    actions={}
    for clip,(length,loop) in spec['clips'].items():
        action=bpy.data.actions.new(f"{spec['id']}_{clip}")
        action.use_fake_user=True
        action['clip_name']=clip
        action['loop']=loop
        action['root_motion']=False
        rig.animation_data.action=action
        previous={}
        for frame in range(1,length+2):
            scene.frame_set(frame)
            pose(rig,spec,clip,(frame-1)/length)
            for pb in rig.pose.bones:
                if pb.name in previous and pb.rotation_quaternion.dot(previous[pb.name])<0:
                    pb.rotation_quaternion.negate()
                previous[pb.name]=pb.rotation_quaternion.copy()
                pb.keyframe_insert('location',frame=frame,group=pb.name)
                pb.keyframe_insert('rotation_quaternion',frame=frame,group=pb.name)
                pb.keyframe_insert('scale',frame=frame,group=pb.name)
        actions[clip]=action
    # Each named NLA strip is an independent export take, not a timeline montage.
    rig.animation_data.action=None
    for clip,action in actions.items():
        track=rig.animation_data.nla_tracks.new()
        track.name=clip
        strip=track.strips.new(clip,1,action)
        strip.name=clip
        strip.extrapolation='NOTHING'
        track.mute=True
    rig.animation_data.action=actions['Swim']
    scene.frame_set(1)
    return actions
