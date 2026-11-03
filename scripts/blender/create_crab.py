"""Build the authored shoreline crab in an isolated Blender process.
blender --background --factory-startup --python scripts/blender/create_crab.py
"""
import bpy
import math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'samples/HookIsland/Blender/Crab'
EXPORT = ROOT / 'samples/TacticalGame.Unity/Assets/HookIsland/Resources/Enemies'
PREVIEW = ROOT / 'artifacts/crabs'
for directory in (SOURCE, EXPORT, PREVIEW):
    directory.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

def material(name, color, roughness=.55):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Roughness'].default_value = roughness
    return m

shell = material('Crab_Coral', (.66, .13, .055))
ridge = material('Crab_Orange', (.95, .32, .075))
underside = material('Crab_Cream', (.95, .73, .39))
dark = material('Crab_Dark', (.035, .045, .052), .25)
white = material('Crab_Glint', (.98, .96, .85), .2)

def empty(name, position, parent=None):
    o = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(o)
    o.parent = parent
    o.location = position
    return o

root = empty('ShoreCrab', (0, 0, 0))

def ellipsoid(name, position, scale, mat, parent=root, subdivisions=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1)
    o = bpy.context.object
    o.name = name
    o.parent = parent
    o.location = position
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(mat)
    return o

def bone(name, a, b, width, mat, parent=root):
    midpoint = (Vector(a) + Vector(b)) / 2
    o = ellipsoid(name, midpoint, (width, width, (Vector(b)-Vector(a)).length/2+width/3), mat, parent)
    o.rotation_euler = (Vector(b)-Vector(a)).to_track_quat('Z', 'Y').to_euler()
    return o

ellipsoid('Carapace', (0, 0, .39), (.44, .34, .22), shell, subdivisions=2)
ellipsoid('TopPlate', (0, .025, .51), (.32, .26, .12), ridge, subdivisions=2)
ellipsoid('Belly', (0, 0, .29), (.36, .27, .1), underside, subdivisions=2)
for side, sign in [('L', -1), ('R', 1)]:
    for i in range(4):
        y = -.18 + i*.115
        leg = empty('Leg_%s_%d' % (side, i), (sign*.3, y, .34), root)
        end_y = (i-1.5)*.10
        bone('LegUpper', (0, 0, 0), (sign*.25, end_y, -.07), .047, shell, leg)
        bone('LegTip', (sign*.25, end_y, -.07), (sign*.33, end_y+.07, -.31), .034, ridge, leg)
    claw = empty('Claw_'+side, (sign*.29, -.20, .37), root)
    bone('ClawArm', (0, 0, 0), (sign*.18, -.17, .02), .073, shell, claw)
    ellipsoid('ClawPalm', (sign*.19, -.24, .065), (.15, .18, .13), ridge, claw, 2)
    for finger_sign, label in [(-1, 'Inner'), (1, 'Outer')]:
        finger = empty('Pincer_'+side+'_'+label, (sign*.19+finger_sign*.085, -.34, .065), claw)
        bone('PincerFinger', (0, 0, 0), (finger_sign*.05, -.15, 0), .063, shell, finger)
        bone('PincerTip', (finger_sign*.05, -.15, 0), (-finger_sign*.025, -.23, 0), .035, underside, finger)
    bone('EyeStalk', (sign*.16, -.21, .49), (sign*.20, -.27, .66), .033, shell)
    ellipsoid('Eye', (sign*.20, -.28, .67), (.064, .058, .068), dark, subdivisions=2)
    ellipsoid('EyeHighlight', (sign*.215, -.325, .69), (.017, .012, .018), white)
    for i in range(3):
        ellipsoid('ShellSpike', (sign*(.35-i*.015), .03+i*.09, .43), (.11, .045, .045), ridge)
bone('Mouth', (-.06, -.325, .37), (.06, -.325, .37), .015, dark)

# Export articulated transforms only; no game collision or damage event is embedded in this model.
bpy.ops.object.select_all(action='DESELECT')
for o in [root, *root.children_recursive]:
    o.select_set(True)
bpy.context.view_layer.objects.active = root
bpy.ops.export_scene.fbx(filepath=str(EXPORT/'ShoreCrab.fbx'), use_selection=True,
    object_types={'EMPTY','MESH'}, axis_forward='-Z', axis_up='Y',
    apply_unit_scale=True, bake_anim=False, add_leaf_bones=False)

# Neutral preview stage, excluded from the export.
floor = material('Preview_Background', (.035, .085, .10))
bpy.ops.mesh.primitive_plane_add(size=200)
bpy.context.object.data.materials.append(floor)
bpy.context.object.location.z=-.025
scene=bpy.context.scene
scene.render.engine='CYCLES'
scene.cycles.samples=32
scene.world.color=(.28,.28,.28)
def light(name, location, energy, size):
    data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.shape='DISK';data.size=size
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.location=location
    obj.rotation_euler=(Vector((0,0,.3))-obj.location).to_track_quat('-Z','Y').to_euler()
light('Key',(-3,-4,5),550,4)
light('Rim',(3,2,3),700,3)
light('Fill',(2,-3,2),140,3)
camera_data=bpy.data.cameras.new('PreviewCamera');camera=bpy.data.objects.new('PreviewCamera',camera_data)
scene.collection.objects.link(camera);camera.location=(1.8,-3.3,2.2)
camera.rotation_euler=(Vector((0,-.08,.32))-camera.location).to_track_quat('-Z','Y').to_euler()
camera_data.type='ORTHO';camera_data.ortho_scale=2.1;scene.camera=camera
scene.render.resolution_x=1000;scene.render.resolution_y=820;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.filepath=str(PREVIEW/'crab-preview.png')
scene.view_settings.view_transform='AgX'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'shore_crab.blend'))
bpy.ops.render.render(write_still=True)
print('CRAB_EXPORT_OK meshes=%d triangles=%d' % (sum(o.type=='MESH' for o in root.children_recursive),sum(len(o.data.polygons) for o in root.children_recursive if o.type=='MESH')))
