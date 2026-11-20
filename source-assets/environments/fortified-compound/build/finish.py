def prepare_delivery():
 global C
 main=S.camera
 use('90_Inspection_Cameras')
 def review_camera(name,pos,target,scale):
  o=camera(name,pos,target,scale)
  for col in list(o.users_collection):col.objects.unlink(o)
  C.objects.link(o);return o
 review_camera('CAM_Top',(0,0,200),(0,0,0),130)
 review_camera('CAM_Front',(0,-200,10),(0,0,5),88)
 review_camera('CAM_Back',(0,200,10),(0,0,5),88)
 review_camera('CAM_Left',(-200,0,10),(0,0,5),125)
 review_camera('CAM_Right',(200,0,10),(0,0,5),125)
 review_camera('CAM_ThreeQuarter',(120,-150,150),(0,0,2),128)
 review_camera('CAM_C1_Access',(38,-28,29),(6,-44,5),36)
 review_camera('CAM_Depots',(-40,-5,32),(-7,30,4),64)
 S.camera=main;S.render.resolution_x=3840;S.render.resolution_y=2160;S.cycles.samples=128
 try:
  prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='METAL';prefs.get_devices()
  for d in prefs.devices:d.use=d.type=='METAL'
  S.cycles.device='GPU'
 except Exception:pass
 image=bpy.data.images.load(str(ROOT/'ref/target.png'),check_existing=True);image.pack()
 main.data.show_background_images=False;bg=main.data.background_images.new();bg.image=image;bg.alpha=.35
 S.render.filepath=str(OUT.parent/'fortified-compound-rounded-corners-20261120'/'fortified-compound-4k.png')
 txt=bpy.data.texts.get('README_FortifiedCompound') or bpy.data.texts.new('README_FortifiedCompound');txt.clear();txt.write((ROOT/'asset-brief.md').read_text())
 S['delivery']='Editable environment; 3840x2160 RGBA; procedural materials. Inspection cameras included.'
 S['qa_scope']='Rendering asset, no collision/LOD/UV bake/game-engine certification.'
