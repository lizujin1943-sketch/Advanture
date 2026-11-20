import bpy,sys,math,json
from pathlib import Path
from mathutils import Vector
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
out=Path(args[0]);w=int(args[1]) if len(args)>1 else 1672;samples=int(args[2]) if len(args)>2 else 32
s=bpy.data.scenes['EN_FortifiedCompound'];bpy.context.window.scene=s
s.render.resolution_x=w;s.render.resolution_y=2160 if w==3840 else round(w*941/1672);s.render.resolution_percentage=100
s.cycles.samples=samples
try:
 prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='METAL';prefs.get_devices()
 for d in prefs.devices:d.use=d.type=='METAL'
 s.cycles.device='GPU'
except Exception as e:print('GPU fallback',e)
if len(args)>3:
 view=args[3];c=s.camera;t=Vector((0,0,3))
 vec={'top':(0,0,200),'front':(0,-200,30),'back':(0,200,30),'left':(-200,0,35),'right':(200,0,35)}[view]
 c.location=t+Vector(vec);c.rotation_euler=(t-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=130 if view in ['top','right','left'] else 88
 if view=='top':s.render.resolution_y=round(w*1.3)
 elif view in ['right','left']:s.render.resolution_y=round(w*.45)
out.parent.mkdir(parents=True,exist_ok=True);s.render.filepath=str(out);bpy.ops.render.render(write_still=True,scene=s.name)
print('RENDER_DONE',str(out))
