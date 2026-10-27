"""Author the lightweight, rounded cartoon fisher and baked actions in background Blender.

Run: Blender --background --factory-startup --python scripts/blender/create_fisher_character.py
No existing user scene is opened or modified. Source and export are reproducible repo assets.
"""
from pathlib import Path
import math
import json
import bpy
from mathutils import Vector, Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "source-assets/characters/fisher"
EXPORT = ROOT / "samples/TacticalGame.Unity/Assets/HookIsland/Art/Characters/Fisher"
EVIDENCE = ROOT / "artifacts/fisher-goofy"
TRIANGLE_BUDGET = 5000
for folder in (SOURCE, EXPORT, EVIDENCE):
    folder.mkdir(parents=True, exist_ok=True)

# Author in game coordinates: X right, Y up, Z forward. Blender faces -Y, Z up.
def v(p):
    return Vector((p[0], -p[2], p[1]))

def enum(operator, key, value):
    values = {i.identifier for i in operator.get_rna_type().properties[key].enum_items}
    if value not in values:
        raise ValueError(f"{key}: {value} not in {values}")
    return value

for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
scene = bpy.context.scene
bpy.context.preferences.filepaths.save_version = 0
scene.render.fps = 30
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0

palette = {
    "Coat": (.08, .43, .70, 1), "Skin": (.95, .61, .34, 1),
    "Boots": (.065, .12, .17, 1), "Cream": (.96, .87, .64, 1),
    "Leather": (.28, .125, .05, 1), "Dark": (.025, .045, .055, 1),
    "Metal": (.56, .70, .74, 1), "Gold": (.95, .57, .08, 1),
    "Blush": (.96, .38, .31, 1),
}
materials = {}
for name, color in palette.items():
    mat = bpy.data.materials.new("Fisher_" + name)
    mat.diffuse_color = color
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = color
    bsdf.inputs['Roughness'].default_value = .78
    materials[name] = mat

arm = bpy.data.armatures.new("Fisher Skeleton")
rig = bpy.data.objects.new("Fisher Rig", arm)
scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode=enum(bpy.ops.object.mode_set, 'mode', 'EDIT'))
spec = {
    'Root': ((0,0,0),(0,.1,0),None),
    'Hips': ((0,.34,0),(0,.47,0),'Root'),
    'Spine': ((0,.47,0),(0,.68,0),'Hips'),
    'Head': ((0,.69,0),(0,.89,0),'Spine'),
}
for side, sign in [('L',-1),('R',1)]:
    spec.update({
        'UpperArm_'+side: ((sign*.21,.64,0),(sign*.30,.45,.03),'Spine'),
        'Forearm_'+side: ((sign*.30,.45,.03),(sign*.26,.25,.07),'UpperArm_'+side),
        'Hand_'+side: ((sign*.26,.25,.07),(sign*.26,.25,.15),'Forearm_'+side),
        'Thigh_'+side: ((sign*.095,.34,0),(sign*.095,.20,.01),'Hips'),
        'Shin_'+side: ((sign*.095,.20,.01),(sign*.095,.07,0),'Thigh_'+side),
        'Foot_'+side: ((sign*.095,.07,0),(sign*.095,.07,.12),'Shin_'+side),
    })
spec.update({
    'Rod': ((.16,.55,.23),(.16,.55,.78),'Hand_R'),
    'RodFlex': ((.16,.55,.78),(.16,.55,1.13),'Rod'),
    'LineGuide': ((.16,.55,1.13),(.16,.55,1.16),'RodFlex'),
    'Reel': ((.21,.51,.27),(.25,.51,.27),'Rod'),
})
for name,(head,tail,parent) in spec.items():
    bone = arm.edit_bones.new(name)
    bone.head, bone.tail = v(head), v(tail)
    if parent:
        bone.parent = arm.edit_bones[parent]
    bone.use_connect = False
bpy.ops.object.mode_set(mode=enum(bpy.ops.object.mode_set, 'mode', 'OBJECT'))
meshes = []

def finish(obj, name, mat, bone):
    obj.name = name
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(materials[mat])
    obj.vertex_groups.new(name=bone).add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
    mod = obj.modifiers.new('Skin', 'ARMATURE')
    mod.object = rig
    obj.parent = rig
    for face in obj.data.polygons:
        face.use_smooth = True
    meshes.append(obj)
    return obj

def box(name, pos, size, mat, bone, bevel=.012, bevel_segments=1):
    bpy.ops.mesh.primitive_cube_add(size=1, location=v(pos))
    obj = bpy.context.object
    obj.scale = (size[0], size[2], size[1])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new('Rounded silhouette', 'BEVEL')
        mod.width, mod.segments = bevel, bevel_segments
        bpy.ops.object.modifier_apply(modifier=mod.name)
        for face in obj.data.polygons:
            face.use_smooth = True
        normals = obj.modifiers.new('Weighted corner normals', 'WEIGHTED_NORMAL')
        normals.keep_sharp = True
        bpy.ops.object.modifier_apply(modifier=normals.name)
    return finish(obj, name, mat, bone)

def oval(name, pos, size, mat, bone, segments=12, rings=6):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=1, location=v(pos))
    obj = bpy.context.object
    obj.scale = (size[0], size[2], size[1])
    return finish(obj, name, mat, bone)

def segment(name, a, b, radius, mat, bone, end_radius=None):
    start, end = v(a), v(b)
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=radius,
        radius2=radius if end_radius is None else end_radius, depth=(end-start).length,
        location=(start+end)*.5)
    obj = bpy.context.object
    obj.rotation_mode = 'QUATERNION'
    obj.rotation_quaternion = Vector((0,0,1)).rotation_difference(end-start)
    return finish(obj, name, mat, bone)

def profile(name, levels, mat, bone, segments=16, lean=0):
    """A few elliptical rings give a rounded silhouette without subdivision."""
    vertices=[]
    for height,rx,rz,forward in levels:
        for i in range(segments):
            angle=math.tau*i/segments
            vertices.append(v((math.cos(angle)*rx+lean*(height-levels[0][0]),height,
                math.sin(angle)*rz+forward)))
    faces=[tuple(range(segments))]
    for ring in range(len(levels)-1):
        for i in range(segments):
            j=(i+1)%segments;a=ring*segments;b=(ring+1)*segments
            faces.append((a+i,b+i,b+j,a+j))
    faces.append(tuple((len(levels)-1)*segments+i for i in reversed(range(segments))))
    mesh=bpy.data.meshes.new(name+' surface');mesh.from_pydata(vertices,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);scene.collection.objects.link(obj)
    for selected in bpy.context.selected_objects:selected.select_set(False)
    obj.select_set(True)
    return finish(obj,name,mat,bone)

profile('Jacket',[(.395,.14,.105,.015),(.425,.178,.13,.02),(.49,.187,.145,.02),
    (.57,.176,.13,.015),(.625,.151,.103,.005),(.66,.10,.075,0)],'Coat','Spine')
box('Shirt',(0,.542,.156),(.15,.205,.027),'Cream','Spine',.026,3)
box('Belt',(0,.38,.015),(.325,.045,.24),'Leather','Hips',.008)
box('Buckle',(0,.38,.146),(.045,.035,.02),'Gold','Hips',.006)
box('Trousers',(0,.33,0),(.31,.12,.23),'Boots','Hips',.035,2)
for sign in (-1,1):
    box('Jacket pocket '+str(sign),(sign*.106,.466,.144),(.069,.058,.025),'Coat','Spine',.014)
    oval('Bib button '+str(sign),(sign*.05,.619,.173),(.012,.012,.007),'Gold','Spine',8,4)
# Large soft cheeks, a low-set dot gaze and a tiny open mouth read at gameplay scale.
oval('Face',(0,.813,.018),(.198,.171,.168),'Skin','Head',20,12)
oval('Nose',(0,.776,.186),(.030,.022,.025),'Skin','Head')
for sign in (-1,1):
    oval('Ear '+str(sign),(sign*.192,.796,.005),(.030,.040,.028),'Skin','Head')
    oval('Eye '+str(sign),(sign*.074,.808+sign*.002,.177),(.011,.014,.007),'Dark','Head')
    oval('Eye glint '+str(sign),(sign*.074-.002,.813+sign*.002,.183),(.0025,.003,.002),'Cream','Head',8,4)
    oval('Cheek blush '+str(sign),(sign*.119,.773,.154),(.031,.016,.007),'Blush','Head')
oval('Little open mouth',(0,.735,.171),(.012,.016,.005),'Dark','Head')
box('Little tooth',(0,.744,.176),(.011,.006,.004),'Cream','Head',.001,1)
profile('Hat brim',[(.928,.196,.174,0),(.933,.228,.199,0),(.945,.236,.205,0),
    (.954,.20,.178,0)],'Coat','Head',20,.12)
profile('Hat crown',[(.943,.184,.162,0),(.963,.195,.171,0),
    (1.007,.18,.16,0),(1.045,.143,.129,0),(1.063,.065,.06,0)],'Coat','Head',20,.18)
profile('Hat band',[(.956,.195,.172,.001),(.973,.196,.173,.001)],'Cream','Head',20,.18)
box('Hat badge',(.093,.973,.158),(.032,.032,.013),'Gold','Head',.009)
box('Backpack',(0,.525,-.172),(.245,.25,.145),'Leather','Spine',.04,3)
box('Backpack flap',(0,.63,-.195),(.255,.07,.125),'Cream','Spine',.025)
for sign in (-1,1):
    box('Bag strap '+str(sign),(sign*.125,.56,-.06),(.028,.19,.17),'Leather','Spine',.004)

for side,sign in [('L',-1),('R',1)]:
    shoulder, elbow, _ = spec['UpperArm_'+side]
    _, wrist, _ = spec['Forearm_'+side]
    segment('Sleeve '+side,shoulder,elbow,.071,'Coat','UpperArm_'+side,.06)
    oval('Elbow '+side,elbow,(.059,.059,.059),'Cream','Forearm_'+side)
    segment('Forearm '+side,elbow,wrist,.048,'Skin','Forearm_'+side,.04)
    oval('Left hand' if side=='L' else 'Right hand',wrist,(.052,.049,.048),'Skin','Hand_'+side)
    thigh,knee,_=spec['Thigh_'+side]
    _,ankle,_=spec['Shin_'+side]
    segment('Trouser leg '+side,thigh,knee,.075,'Boots','Thigh_'+side,.065)
    segment('Boot cuff '+side,knee,ankle,.061,'Boots','Shin_'+side,.058)
    box('Left boot' if side=='L' else 'Right boot',(sign*.095,.065,.045),(.145,.125,.225),'Boots','Foot_'+side,.035,3)
    box('Boot sole '+side,(sign*.095,.018,.048),(.15,.032,.235),'Leather','Foot_'+side,.009)

# Individually toggleable equipment meshes; all follow the same animation skeleton.
box('Equipped cap guard',(0,.992,.177),(.18,.05,.028),'Metal','Head',.012)
box('Equipped reinforced vest',(0,.52,.181),(.25,.19,.025),'Leather','Spine',.025)
for side,sign in [('L',-1),('R',1)]:
    box('Equipped light shoe '+side,(sign*.095,.055,.155),(.145,.06,.035),'Cream','Foot_'+side,.012)

rod_meshes=[]
rod_meshes.append(segment('Rod Grip',(.16,.55,.14),(.16,.55,.36),.029,'Leather','Rod',.026))
rod_meshes.append(segment('Rod Shaft',(.16,.55,.34),(.16,.55,.78),.016,'Leather','Rod',.011))
rod_meshes.append(segment('Rod Tip Shaft',(.16,.55,.78),(.16,.55,1.13),.011,'Leather','RodFlex',.005))
rod_meshes.append(oval('Rod Guide',(.16,.55,1.13),(.016,.016,.016),'Gold','LineGuide'))
rod_meshes.append(segment('Reel Spool',(.19,.51,.27),(.235,.51,.27),.047,'Metal','Reel'))
rod_meshes.append(box('Reel Handle',(.24,.49,.29),(.025,.018,.045),'Gold','Reel',.005))

# Combine rigid-weighted pieces into a few skinned renderers.
def join_parts(name, objects):
    for obj in bpy.context.selected_objects:obj.select_set(False)
    for obj in objects:obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.join()
    result=bpy.context.object
    result.name=name
    return result

equipment=[o for o in meshes if o.name.startswith('Equipped ')]
bag_parts=[o for o in meshes if o.name.startswith(('Backpack','Bag strap'))]
base_parts=[o for o in meshes if o not in equipment+bag_parts+rod_meshes]
base=join_parts('Fisher Body',base_parts)
bag=join_parts('Backpack',bag_parts)
details=join_parts('Rod Details',[o for o in rod_meshes if o.name not in ('Rod Shaft','Rod Tip Shaft')])
meshes=[base,bag,details]+equipment+[bpy.data.objects['Rod Shaft'],bpy.data.objects['Rod Tip Shaft']]
triangles=sum(len(p.vertices)-2 for obj in meshes for p in obj.data.polygons)
if triangles>TRIANGLE_BUDGET:
    raise ValueError(f'Character exceeds its triangle budget: {triangles}/{TRIANGLE_BUDGET}')

rest = {bone.name:bone.matrix_local.copy() for bone in arm.bones}
hip = v((0,.39,0))
def rot(pitch=0,yaw=0,roll=0):
    return (Quaternion(v((0,1,0)), math.radians(yaw)) @
            Quaternion(v((1,0,0)), math.radians(pitch)) @
            Quaternion(v((0,0,1)), math.radians(roll)))

def set_segment(name,a,b,transverse=None):
    pb=rig.pose.bones[name]
    direction=b-a
    # A shortest-arc Y-to-direction rotation flips its roll when the rod crosses
    # -Y. Use a stable transverse axis so child bones interpolate smoothly in FBX.
    y_axis=direction.normalized()
    transverse=Vector((1,0,0)) if transverse is None else transverse
    if abs(y_axis.dot(transverse))>.99:
        transverse=Vector((0,0,1))
    x_axis=(transverse-y_axis*transverse.dot(y_axis)).normalized()
    z_axis=x_axis.cross(y_axis).normalized()
    q=Matrix((x_axis,y_axis,z_axis)).transposed().to_quaternion()
    pb.matrix=Matrix.Translation(a) @ q.to_matrix().to_4x4() @ Matrix.Diagonal((1,direction.length/arm.bones[name].length,1,1))
    bpy.context.view_layer.update()

def pose_arm(side,shoulder,hand,body_rotation):
    upper=(v(spec['UpperArm_'+side][1])-v(spec['UpperArm_'+side][0])).length
    lower=(v(spec['Forearm_'+side][1])-v(spec['Forearm_'+side][0])).length
    delta=hand-shoulder
    if delta.length>=upper+lower:
        raise ValueError(f'{side} hand target exceeds arm reach: {delta.length:.4f} m')
    distance=delta.length
    axis=delta.normalized()
    along=(upper*upper-lower*lower+distance*distance)/(2*distance)
    # Elbows lead outward and forward. A rearward pole folds the supporting
    # forearm through the jacket even when its hand reaches the grip correctly.
    hint=body_rotation @ v((-1.5 if side=='L' else 1.5,-.3,.7))
    bend=(hint-axis*hint.dot(axis)).normalized()
    elbow=shoulder+axis*along+bend*math.sqrt(max(0,upper*upper-along*along))
    # The arm may point sideways during the backswing. Its bend-plane normal
    # supplies a continuous roll even when a fixed world axis would be singular.
    normal=(elbow-shoulder).cross(hand-elbow).normalized()
    set_segment('UpperArm_'+side,shoulder,elbow,normal)
    set_segment('Forearm_'+side,elbow,hand,normal)
    set_segment('Hand_'+side,hand,hand+body_rotation@v((0,0,.08)))

cast_keys=[
    # Time, torso, right-hand grip, rod pitch, tip flex, support-hand release.
    (0, (0,0,0),(0,.55,.33),-24,0,0),
    (.17,(-7,-20,-4),(.25,.72,.10),-116,16,1),
    (.38,(12,15,4),(.10,.60,.29),7,-26,1),
    (.55,(8,10,2),(.06,.51,.30),12,13,.75),
    (.78,(1,-3,0),(0,.56,.33),-29,-4,0),
    (1,(0,0,0),(0,.55,.33),-24,0,0),
]
def blend_cast(t):
    for i in range(len(cast_keys)-1):
        a,b=cast_keys[i],cast_keys[i+1]
        if t<=b[0]:
            w=max(0,min(1,(t-a[0])/(b[0]-a[0])))
            w=w*w*(3-2*w)
            return [Vector(a[k]).lerp(Vector(b[k]),w) if k in (1,2) else a[k]+(b[k]-a[k])*w for k in (1,2,3,4,5)]
    return [Vector(cast_keys[-1][1]),Vector(cast_keys[-1][2]),-24,0,0]

def pose(kind,t):
    for pb in rig.pose.bones:
        pb.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    torso=Vector((0,0,0));grip=Vector((0,.55,.33));pitch=-24;flex=0;release=0
    breath=math.sin(t*math.tau)*.003
    if kind=='Cast':
        torso,grip,pitch,flex,release=blend_cast(t);breath=0
    elif kind=='Reel':
        torso=Vector((-5+math.sin(t*math.tau)*1.5,0,-2))
        pitch=-32+math.sin(t*math.tau)*3;flex=-5;grip.z=.30
    q=rot(*torso)
    body_matrix=Matrix.Translation(hip+v((0,breath,0))) @ q.to_matrix().to_4x4() @ Matrix.Translation(-hip)
    rig.pose.bones['Spine'].matrix=body_matrix @ rest['Spine']
    bpy.context.view_layer.update()
    rig.pose.bones['Head'].matrix=body_matrix @ rest['Head']
    bpy.context.view_layer.update()
    rp=body_matrix @ v(grip)
    rq=q@rot(pitch)
    # The short supporting arm cannot follow a grip beside the opposite ear.
    # Release it for the wind-up, counterbalance beside the body, then regrip
    # in front of the chest after the forward swing.
    lh=(rp+rq@v((-.035,-.008,-.10))).lerp(body_matrix@v((-.27,.47,.20)),release)
    pose_arm('R',body_matrix@v(spec['UpperArm_R'][0]),rp,q)
    pose_arm('L',body_matrix@v(spec['UpperArm_L'][0]),lh,q)
    set_segment('Rod',rp,rp+rq@v((0,0,.55)))
    tip_base=rp+rq@v((0,0,.55));tq=rq@rot(flex)
    set_segment('RodFlex',tip_base,tip_base+tq@v((0,0,.35)))
    tip=tip_base+tq@v((0,0,.35))
    set_segment('LineGuide',tip,tip+tq@v((0,0,.03)))
    reel_pos=rp+rq@v((.05,-.04,.04))
    set_segment('Reel',reel_pos,reel_pos+rq@v((.04,0,0)))
    if kind=='Walk':
        for side,sign in [('L',1),('R',-1)]:
            thigh=v(spec['Thigh_'+side][0])
            knee=thigh+rot(math.sin(t*math.tau)*28*sign)@(v(spec['Thigh_'+side][1])-thigh)
            shin=rot(math.sin(t*math.tau)*28*sign-max(0,-math.sin(t*math.tau)*sign)*25)
            ankle=knee+shin@(v(spec['Shin_'+side][1])-v(spec['Shin_'+side][0]))
            set_segment('Thigh_'+side,thigh,knee)
            set_segment('Shin_'+side,knee,ankle)
            set_segment('Foot_'+side,ankle,ankle+rot(math.sin(t*math.tau)*12*sign)@v((0,0,.12)))
    bpy.context.view_layer.update()

rig.animation_data_create()
actions=[]
for name,length in [('Idle',60),('Walk',24),('Reel',30),('Cast',18)]:
    action=bpy.data.actions.new('Fisher_'+name)
    action.use_fake_user=True
    rig.animation_data.action=action
    previous_rotations={}
    for frame in range(1,length+2):
        scene.frame_set(frame)
        pose(name,(frame-1)/length)
        for pb in rig.pose.bones:
            pb.rotation_mode='QUATERNION'
            if pb.name in previous_rotations and pb.rotation_quaternion.dot(previous_rotations[pb.name])<0:
                pb.rotation_quaternion.negate()
            previous_rotations[pb.name]=pb.rotation_quaternion.copy()
            pb.keyframe_insert('location',frame=frame,group=pb.name)
            pb.keyframe_insert('rotation_quaternion',frame=frame,group=pb.name)
            pb.keyframe_insert('scale',frame=frame,group=pb.name)
    actions.append(action)

rig.animation_data.action=actions[-1]
scene.frame_start,scene.frame_end=1,19
scene.frame_set(1)
for obj in bpy.context.selected_objects:obj.select_set(False)
rig.select_set(True)
for obj in meshes:obj.select_set(True)
bpy.context.view_layer.objects.active=rig
export_args=dict(filepath=str(EXPORT/'Fisher.fbx'),use_selection=True,
    object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=True,
    bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,bake_anim_force_startend_keying=True,
    bake_anim_step=1,bake_anim_simplify_factor=0,global_scale=1,
    axis_forward=enum(bpy.ops.export_scene.fbx,'axis_forward','-Z'),
    axis_up=enum(bpy.ops.export_scene.fbx,'axis_up','Y'))
bpy.ops.export_scene.fbx(**export_args)

# A source-only studio for inspecting and editing the action; not exported to Unity.
def aim(obj,target):obj.rotation_euler=(target-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=v((1.65,1.25,2.45)))
camera=bpy.context.object;camera.name='Preview camera';camera.data.type='ORTHO';camera.data.ortho_scale=1.85
aim(camera,v((0,.58,.18)));scene.camera=camera
for name,pos,power,size in [('Key',(-2,3,3),450,4),('Fill',(3,2,1),250,3),('Rim',(0,2,-3),400,3)]:
    bpy.ops.object.light_add(type=enum(bpy.ops.object.light_add,'type','AREA'),location=v(pos))
    light=bpy.context.object;light.name=name;light.data.energy=power;light.data.shape='DISK';light.data.size=size;aim(light,v((0,.5,0)))
scene.world.color=(.17,.21,.24)
scene.render.resolution_x=700;scene.render.resolution_y=700;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
try:scene.render.engine='CYCLES'
except TypeError as error:raise RuntimeError(str(error))
scene.cycles.samples=24
scene.render.film_transparent=True
scene.view_settings.view_transform='Standard'
scene.view_settings.exposure=-1.25
for obj in equipment:obj.hide_render=True
scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'Fisher.blend'))
scene.render.filepath=str(EVIDENCE/'fisher-blender.png')
bpy.ops.render.render(write_still=True)
summary={'blender':bpy.app.version_string,'height_m':1.063,'bones':len(arm.bones),
    'mesh_objects':len(meshes),'triangles':triangles,'triangle_budget':TRIANGLE_BUDGET,
    'actions':[{ 'name':a.name,'frames':list(a.frame_range)} for a in actions],
    'source':str(SOURCE/'Fisher.blend'),'fbx':str(EXPORT/'Fisher.fbx')}
(EVIDENCE/'blender-export.json').write_text(json.dumps(summary,indent=2))
print(json.dumps(summary))
