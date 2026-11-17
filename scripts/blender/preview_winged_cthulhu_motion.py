"""Render actual skinned motion for review (not an image-generated animation)."""
from pathlib import Path
import bpy,sys,json
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(Path(__file__).resolve().parent))
from animate_abyssal_watcher import assign
from create_winged_cthulhu import camera
out=ROOT/'artifacts/winged-cthulhu/motion-frames';out.mkdir(parents=True,exist_ok=True)
s=bpy.context.scene;rig=bpy.data.objects['WC_Rig']
for ob in bpy.data.objects:
    if ob.name.startswith('AW_Stump_'):ob.hide_render=True
s.render.engine='BLENDER_WORKBENCH';s.render.resolution_x=960;s.render.resolution_y=640;s.render.resolution_percentage=100
s.display.shading.light='STUDIO';s.display.shading.color_type='MATERIAL';s.display.shading.show_cavity=True
camera('CAM_MotionWide',(26,-35,20),(0,-5,4),32)
shots=[('AW_Idle',181,'CAM_RadialHero'),('AW_Slam_A02',91,'CAM_MotionWide'),('AW_Sweep_A04',109,'CAM_MotionWide'),('AW_Ink',97,'CAM_RadialFront'),('AW_Death',181,'CAM_RadialHero')]
index=0;chapters=[]
for clip,end,cam in shots:
    assign(rig,bpy.data.actions[clip]);s.camera=bpy.data.objects[cam]
    chapters.append({'clip':clip,'startSeconds':index/15})
    for frame in range(1,end,2):
        s.frame_set(frame);s.render.filepath=str(out/f'{index:04d}.png');bpy.ops.render.render(write_still=True);index+=1
(out.parent/'motion-preview-manifest.json').write_text(json.dumps({'frames':index,'fps':15,'chapters':chapters},indent=2))
print('MOTION_FRAMES_READY',index,flush=True)
