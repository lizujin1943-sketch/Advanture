"""Iteration 3: match projected elevations, silhouette and access continuity."""
def refine_geometry():
 global C
 # The reference's northern yard is slightly raised relative to the south.
 # This resolves facade occlusion without compressing the functional building heights.
 for c in S.collection.children:
  if c.name.startswith(('FC_03_A1','FC_07_Equipment','FC_08_Medical')):
   for ob in c.objects:ob.location.z+=1.8
  if c.name.startswith('FC_02_Plaza'):
   for ob in c.objects:
    if ob.name.startswith('Flag'):ob.location.z+=1.8
    elif ob.name.startswith(('Single flagpole','Red cloth')):ob.location.z+=1.2
  if c.name.startswith('FC_05_C1'):
   for ob in c.objects:ob.location.z+=.45
 # continuous raised northern terrain, smoothly meeting the main yard
 C=next(c for c in S.collection.children if c.name.startswith('FC_00_Ground'))
 old=S.objects.get('Compound foundation');bpy.data.objects.remove(old,do_unlink=True)
 def height(x,y):
  z=1.8*max(0,min(1,(y-21)/6))
  # damaged north entrance remains a low depression with a natural incline
  if y>46 and -26<x<-16:z*=max(0,min(1,(54-y)/7))
  return z
 # Row widths follow chamfered perimeter; terrain is a closed shell.
 rows=[(-56.5,33),(-55,33),(-35,36.2),(-20,39.2),(0,38.35),(21,37.5),(25,37.3),(27,36.85),(40,34),(46,32.7),(52,31.5),(55.5,31)]
 vs=[];fs=[];N=40
 for y,w in rows:
  for i in range(N+1):x=-w+2*w*i/N;vs.append((x,y,height(x,y)))
 for j in range(len(rows)-1):
  for i in range(N):a=j*(N+1)+i;fs.append((a,a+1,a+N+2,a+N+1))
 boundary=list(range(N+1))+[j*(N+1)+N for j in range(1,len(rows))]+list(range((len(rows)-1)*(N+1)+N-1,(len(rows)-1)*(N+1)-1,-1))+[j*(N+1) for j in range(len(rows)-2,0,-1)]
 n=len(vs)
 for i in boundary:vs.append((vs[i][0],vs[i][1],-.4))
 for k,i in enumerate(boundary):fs.append((i,boundary[(k+1)%len(boundary)],n+(k+1)%len(boundary),n+k))
 fs.append(tuple(reversed(range(n,n+len(boundary)))))
 mesh('Compound terrain with northern terrace',vs,fs,'ground')
 # Rear wall coping matches northern court while towers retain reference skyline.
 for ob in S.objects:
  if ob.type=='MESH' and (ob.name.startswith(('Wall 06','Wall 07','Wall coping 06','Wall coping 07'))):ob.location.z+=.7
 # A distinct heavier structural frame around the south gate.
 C=next(c for c in S.collection.children if c.name.startswith('FC_05_C1'))
 for xx in [-14.15,-9.1,2.3,6.55]:
  box('C1 reinforced facade pier',(xx,-54.78,4.87),(.84,.55,9.66),'concrete')
  box('C1 pier foot',(xx,-54.95,.78),(1.05,.8,1.6),'trim')
  box('C1 pier coping',(xx,-54.78,9.86),(1.1,.95,.42),'trim')
 # Tower shafts: chamfered shoes and corner pilasters, preserving compact caps.
 C=next(c for c in S.collection.children if c.name.startswith('FC_01_Perimeter'))
 shafts=[o for o in C.objects if 'shaft' in o.name]
 for shaft in shafts:
  x,y=shaft.location.x,shaft.location.y
  for dx in [-1.48,1.48]:
   for dy in [-1.48,1.48]:box('Tower corner pilaster',(x+dx,y+dy,3.6),(.23,.23,7.12),'trim')
  for a in [0,pi/2,pi,3*pi/2]:
   p=(x+1.50*cos(a),y+1.50*sin(a),-.05)
   profile('Tower splayed foundation',[(0,0),(.7,0),(.18,2.8),(0,2.8)],2.5,p,'trim',a)
 # North tree is grounded in the raised yard.
 for c in S.collection.children:
  if c.name.startswith('FC_12_Tree'):
   for ob in c.objects:ob.location.z+=1.8
 # Sun direction from the reference's upper right, shadows cast left into yard.
 sun=next(o for o in S.objects if o.type=='LIGHT' and o.data.type=='SUN')
 sun.rotation_euler=Vector((-.65,.8,-1.5)).to_track_quat('-Z','Y').to_euler()
 S.world.node_tree.nodes.get('Background').inputs[0].default_value=(.78,.77,.72,1)
 S.world.node_tree.nodes.get('Background').inputs[1].default_value=.45
 S.view_settings.exposure=.2

 # Real, irregular retained roof triangles for the three semi-open depots.
 for cname,name,x,y,w,d,h,base in [('FC_07_Equipment','Equipment',-15.8,30.5,12,7,4.4,1.8),('FC_08_Medical','Medical',9.3,30.5,11,7,4.4,1.8),('FC_09_Armory','Armory',-29.1,-7,10.8,13,5.6,0)]:
  C=next(c for c in S.collection.children if c.name.startswith(cname))
  for ob in list(C.objects):
   if ob.name.startswith((name+' roof rear',name+' chipped roof edge')):bpy.data.objects.remove(ob,do_unlink=True)
   elif name=='Armory' and ob.name.startswith((name+' pier',name+' pier shoe')) and ob.location.x>x and ob.location.y<y:bpy.data.objects.remove(ob,do_unlink=True)
  a=Vector((x-w/2,y-d/2));b=Vector((x+w/2,y+d/2-1.0));line=b-a
  poly=[(x-w/2-.18,y+d/2+.18),(x+w/2+.18,y+d/2+.18)]
  jag=[]
  for i in range(19):
   p=a+line*i/18+Vector((0,rng.uniform(-.2,.12)));jag.append(tuple(p))
  poly+=list(reversed(jag))
  polygon_prism(name+' jagged retained roof',poly,base+h-.05,base+h+.26,'trim')
  for i in range(18):
   p=a+line*(i+.5)/18;box(name+' fractured slab aggregate',(*p,base+h+.10),(.44,.38,.27),'concrete',.035,rot=math.atan2(line.y,line.x)+rng.uniform(-.13,.13))
 # Painted identifiers do not cast duplicate text shadows.
 for ob in S.objects:
  if ob.type=='FONT':ob.visible_shadow=False;ob.data.extrude=0
 # Lower-contrast metal avoids white outlines around olive gates.
 p=next(n for n in M['metal'].node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 p.inputs['Base Color'].default_value=(.22,.235,.22,1);p.inputs['Roughness'].default_value=.60
 # Long, faint weather streaks layered into the concrete; scale in meters.
 for key in ['concrete','trim','darkconcrete']:
  m=M[key];nt=m.node_tree;nd=nt.nodes;lk=nt.links;p=next(n for n in nd if n.type=='BSDF_PRINCIPLED');old=p.inputs['Base Color'].links[0].from_socket
  tc=nd.new('ShaderNodeTexCoord');v=nd.new('ShaderNodeVectorMath');v.operation='MULTIPLY';v.inputs[1].default_value=(2.0,2.0,.075);lk.new(tc.outputs['Object'],v.inputs[0])
  tex=nd.new('ShaderNodeTexNoise');tex.inputs['Scale'].default_value=1.8;tex.inputs['Detail'].default_value=3;lk.new(v.outputs['Vector'],tex.inputs['Vector'])
  ramp=nd.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].position=.22;ramp.color_ramp.elements[0].color=(.65,.63,.59,1);ramp.color_ramp.elements[1].position=.60;ramp.color_ramp.elements[1].color=(1,1,1,1);lk.new(tex.outputs['Fac'],ramp.inputs[0])
  mix=nd.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=.30;lk.new(old,mix.inputs[1]);lk.new(ramp.outputs[0],mix.inputs[2]);lk.new(mix.outputs[0],p.inputs['Base Color'])

 # Clean geometrical contacts discovered by the mesh audit.
 tower_xy=[(o.location.x,o.location.y) for o in S.objects if 'shaft' in o.name]
 for ob in list(S.objects):
  if ob.name.startswith(('Wall pier','Wall buttress')) and any((ob.location.x-x)**2+(ob.location.y-y)**2<2.4**2 for x,y in tower_xy):bpy.data.objects.remove(ob,do_unlink=True);continue
  if ob.name.startswith('Ramp curb'):
   for v in ob.data.vertices:v.co.x*=1.01
   ob.location.y-=.06
 # Condensed painted identifiers match the supplied military architecture.
 font=bpy.data.fonts.load('/System/Library/Fonts/Supplemental/DIN Condensed Bold.ttf',check_existing=True)
 try:font.pack()
 except Exception:pass
 M['paint']=mat('FadedIdentifier',(.115,.12,.105),.93)
 for ob in S.objects:
  if ob.type=='FONT':ob.data.font=font;ob.data.materials.clear();ob.data.materials.append(M['paint']);ob.data.size*=1.80
 # Mild dirt gathering near concrete foundations, rather than uniform vertical striping.
 for key in ['concrete','trim']:
  nt=M[key].node_tree;nd=nt.nodes;lk=nt.links;p=next(n for n in nd if n.type=='BSDF_PRINCIPLED');old=p.inputs['Base Color'].links[0].from_socket
  tc=nd.new('ShaderNodeTexCoord');sep=nd.new('ShaderNodeSeparateXYZ');lk.new(tc.outputs['Generated'],sep.inputs[0]);r=nd.new('ShaderNodeMapRange');r.inputs['From Min'].default_value=0;r.inputs['From Max'].default_value=.12;r.inputs['To Min'].default_value=.18;r.inputs['To Max'].default_value=0;lk.new(sep.outputs['Z'],r.inputs['Value'])
  mx=nd.new('ShaderNodeMixRGB');mx.inputs[2].default_value=(.19,.17,.125,1);lk.new(r.outputs[0],mx.inputs[0]);lk.new(old,mx.inputs[1]);lk.new(mx.outputs[0],p.inputs['Base Color'])

 # Match the reference canopy height without cropping the tree at the image edge.
 bpy.context.view_layer.update()
 for c in S.collection.children:
  if c.name.startswith('FC_12_Tree'):
   for ob in c.objects:
    if ob.type=='MESH':
     mw=ob.matrix_world.copy();inv=mw.inverted()
     for v in ob.data.vertices:
      q=mw@v.co;q.z=1.8+(q.z-1.8)*.84;v.co=inv@q

 # Raised gate threshold has a solid foundation and an apron inside the perimeter.
 C=next(c for c in S.collection.children if c.name.startswith('FC_05_C1'))
 box('C1 continuous foundation',(-3.8,-48.7,.225),(20.7,11.6,.45),'darkconcrete')
 profile('C1 gate apron',[(0,0),(2,0),(2,.49)],7,(-2.5,-56.5,0),'ground',pi/2)

 for ob in S.objects:
  if ob.type=='MESH' and ob.name.startswith('Ramp curb'):
   for v in ob.data.vertices:
    if abs(v.co.z)<1e-5:v.co.z=-.10
  if ob.type=='MESH' and ob.name.startswith('03_A1 facade pier'):
   for v in ob.data.vertices:
    if v.co.z<0:v.co.z-=.10
