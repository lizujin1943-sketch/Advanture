import bpy,sys,json
from pathlib import Path
s=bpy.data.scenes['EN_FortifiedCompound'];bpy.context.window.scene=s
out=Path(bpy.path.abspath(s.render.filepath)).parent
out.mkdir(parents=True,exist_ok=True)
try:
 prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='METAL';prefs.get_devices()
 for d in prefs.devices:d.use=d.type=='METAL'
 s.cycles.device='GPU'
except Exception as e:print(e)
# Camera directions are persistent in the source file; these renders are true geometry.
jobs=[('CAM_Reference','fortified-compound-4k.png',3840,2160,128),('CAM_Top','view-top.png',1000,1300,32),('CAM_Front','view-front.png',1400,450,32),('CAM_Back','view-back.png',1400,450,32),('CAM_Right','view-right.png',1600,450,32),('CAM_Left','view-left.png',1600,450,32),('CAM_ThreeQuarter','view-three-quarter.png',1600,1200,48),('CAM_C1_Access','detail-c1-access.png',1300,1000,48),('CAM_Depots','detail-depots.png',1500,900,48)]
for camera,name,w,h,samples in jobs:
 s.camera=bpy.data.objects[camera];s.render.resolution_x=w;s.render.resolution_y=h;s.render.resolution_percentage=100;s.cycles.samples=samples;s.render.filepath=str(out/name)
 bpy.ops.render.render(write_still=True,scene=s.name);print('DELIVERED',name,flush=True)
