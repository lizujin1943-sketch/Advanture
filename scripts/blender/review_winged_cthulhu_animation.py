"""Inspect the actual skinned game mesh, including extreme poses and loop seams."""
from pathlib import Path
import sys,json
import bpy
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(Path(__file__).resolve().parent))
from animate_abyssal_watcher import assign
from create_winged_cthulhu import camera
OUT=ROOT/'source-assets/creatures/winged-cthulhu/review'
s=bpy.context.scene;rig=bpy.data.objects['WC_Rig']
for ob in bpy.data.objects:
    if ob.name.startswith('AW_Stump_'):ob.hide_render=True
s.render.engine='BLENDER_WORKBENCH';s.render.resolution_x=1280;s.render.resolution_y=900;s.render.resolution_percentage=100
s.display.shading.light='STUDIO';s.display.shading.color_type='MATERIAL';s.display.shading.show_cavity=True
camera('CAM_GameAction',(26,-35,20),(0,-5,4),32)
shots=[('AW_Idle',1,'CAM_RadialHero'),('AW_Idle',60,'CAM_RadialBack'),('AW_Slam_A02',37,'CAM_GameAction'),('AW_Slam_A02',46,'CAM_GameAction'),('AW_Sweep_A04',48,'CAM_GameAction'),('AW_Ink',44,'CAM_RadialFront'),('AW_Enrage',46,'CAM_RadialHero'),('AW_Death',65,'CAM_RadialHero')]
for clip,frame,cam in shots:
    assign(rig,bpy.data.actions[clip]);s.frame_set(frame);s.camera=bpy.data.objects[cam]
    s.render.filepath=str(OUT/f'animation-{clip}-{frame}.png');bpy.ops.render.render(write_still=True)
assign(rig,bpy.data.actions['AW_Idle']);s.frame_set(1)
s.render.engine='CYCLES';s.cycles.device='CPU';s.cycles.samples=40;s.cycles.use_denoising=True
s.render.resolution_x=1536;s.render.resolution_y=1536
for cam,filename in [('CAM_RadialHero','game-material-hero'),('CAM_RadialFront','game-material-front')]:
    s.camera=bpy.data.objects[cam];s.render.filepath=str(OUT/(filename+'.png'));bpy.ops.render.render(write_still=True)
if bpy.data.objects.get('CAM_SculptPortrait'):
    from sculpt_winged_cthulhu_review import studio
    for cam,filename,width,height in [('CAM_SculptPortrait','game-portrait',1650,1130),
                                      ('CAM_SculptProfile','game-profile',1280,1100)]:
        s.camera=bpy.data.objects[cam]
        studio(s,s.camera)
        s.render.resolution_x,s.render.resolution_y=width,height
        s.render.filepath=str(OUT/(filename+'.png'))
        bpy.ops.render.render(write_still=True)
    clay=bpy.data.materials.new('Review neutral clay')
    clay.diffuse_color=(.33,.33,.33,1)
    surface=next(n for n in clay.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    surface.inputs['Base Color'].default_value=clay.diffuse_color
    surface.inputs['Roughness'].default_value=.59
    bpy.context.view_layer.material_override=clay
    s.camera=bpy.data.objects['CAM_SculptPortrait']
    studio(s,s.camera)
    s.render.resolution_x,s.render.resolution_y=1650,1130
    s.render.filepath=str(OUT/'game-clay-portrait.png')
    bpy.ops.render.render(write_still=True)
print('ANIMATION_REVIEW_DONE',flush=True)
