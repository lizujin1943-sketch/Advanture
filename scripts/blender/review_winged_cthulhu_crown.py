"""Same-scale review of the exposed posterior fork and a loaded attack ray."""
from pathlib import Path
import bpy,sys
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from create_winged_cthulhu import camera
from animate_abyssal_watcher import assign
out=Path(sys.argv[sys.argv.index('--')+1])
out.mkdir(exist_ok=True,parents=True)
s=bpy.context.scene;rig=bpy.data.objects.get('WC_Rig')
s.render.engine='CYCLES';s.cycles.samples=24;s.cycles.use_denoising=True
# Broad posterior fill shows the shape of the web in rest and attack,
# using one fixed material and lighting setup for both revisions.
s.view_settings.exposure=.48
s.render.resolution_x=1080;s.render.resolution_y=750;s.render.resolution_percentage=100
bpy.data.objects['STUDIO_Backdrop'].hide_render=True
for ob in bpy.data.objects:
    if ob.name.startswith('AW_Stump_'):ob.hide_render=True
for name,pos,target,size,clip,frame in [
    ('dorsal',(0,17,8.8),(0,.55,4.92),6.25,'AW_Idle',1),
    ('oblique',(9,12,12),(.25,.3,4.64),7.0,'AW_Idle',46),
    ('slam',(12,-6,7),(1.0,-.6,4.82),7.0,'AW_Slam_A04',37),
    ('severed',(0,13,8),(0,.70,4.62),6.5,'AW_Idle',1)]:
    if rig:
        assign(rig,bpy.data.actions[clip]);s.frame_set(frame)
        if name=='severed':
            bpy.data.objects['AW_Arm_S02'].hide_render=True
            bpy.data.objects['AW_Stump_S02'].hide_render=False
    elif name in ('slam','severed'):continue
    s.camera=camera('Review_'+name,pos,target,size)
    s.render.filepath=str(out/(name+'.png'));bpy.ops.render.render(write_still=True)
