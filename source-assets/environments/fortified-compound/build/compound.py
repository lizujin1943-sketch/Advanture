"""Editable measured reconstruction; no model downloaded. Run in Blender 5.3.
All dimensions in meters. Front is -Y. Owns only EN_FortifiedCompound scene.
"""
import bpy, bmesh, math, random, json, sys
from pathlib import Path
from mathutils import Vector
from math import sin, cos, pi
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT.parents[2]/'artifacts/fortified-compound-reconstruction-20261119'
TAG='fortified_compound_20261119'
S=None; C=None; M={}; DETAIL=False
rng=random.Random(119)

def setup(detail=False):
 global S,C,M,DETAIL,rng
 DETAIL=detail; rng=random.Random(119)
 old=bpy.data.scenes.get('EN_FortifiedCompound')
 if old:
  for ob in list(old.objects):
   if ob.get('owner')==TAG:bpy.data.objects.remove(ob,do_unlink=True)
  bpy.data.scenes.remove(old)
 S=bpy.data.scenes.new('EN_FortifiedCompound'); bpy.context.window.scene=S
 S.unit_settings.system='METRIC';S.unit_settings.scale_length=1
 S.render.engine='CYCLES';S.cycles.samples=32;S.cycles.use_denoising=True
 S.render.resolution_x=1672;S.render.resolution_y=941;S.render.resolution_percentage=100
 S.render.image_settings.file_format='PNG';S.render.image_settings.color_mode='RGBA';S.render.film_transparent=True
 S.view_settings.view_transform='AgX';S.view_settings.look='AgX - Medium High Contrast'; S.view_settings.exposure=.35
 w=bpy.data.worlds.new('FC_Sky');w.use_nodes=True;w.node_tree.nodes.get('Background').inputs[0].default_value=(.68,.75,.85,1);w.node_tree.nodes.get('Background').inputs[1].default_value=.6;S.world=w
 C=collection('00_Ground')
 M={
 'concrete':mat('Concrete',(.46,.445,.405),.86,noise=True),
 'trim':mat('Coping',(.60,.585,.54),.83,noise=True),
 'darkconcrete':mat('Foundation',(.33,.33,.30),.95,noise=True),
 'ground':mat('Pavement',(.42,.405,.37),.93,noise=True),
 'roof':mat('Roofing',(.29,.305,.30),.93,noise=True),
 'metal':mat('Galvanized',(.33,.37,.36),.42,.6),
 'olive':mat('OliveDrab',(.19,.215,.15),.65,.25,noise=True),
 'dark':mat('Charcoal',(.045,.052,.046),.76),
 'glass':mat('SmokedGlass',(.055,.10,.105),.25,.5),
 'yellow':mat('FadedSafetyYellow',(.67,.42,.09),.62,.3),
 'sandbag':mat('Sandbags',(.40,.42,.40),.94,noise=True),
 'white':mat('DomeWhite',(.68,.67,.62),.58,.12,noise=True),
 'red':mat('FlagRed',(.36,.035,.023),.92),
 'medical':mat('MedicalCase',(.32,.055,.035),.65),
 'leaf':mat('Leaves',(.16,.18,.055),.93),
 'bark':mat('Bark',(.15,.12,.08),.96,noise=True),
 'rust':mat('Rust',(.22,.105,.045),.9),
 }

def collection(name):
 c=bpy.data.collections.new('FC_'+name);S.collection.children.link(c);return c

def use(name):
 global C
 C=collection(name)

def mat(name,color,rough=.7,metal=0,noise=False):
 m=bpy.data.materials.new('FC_'+name);m.use_nodes=True;m.diffuse_color=(*color,1)
 n=m.node_tree.nodes; links=m.node_tree.links;p=next(x for x in n if x.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal
 if DETAIL and noise:
  tc=n.new('ShaderNodeTexCoord');tex=n.new('ShaderNodeTexNoise');tex.inputs['Scale'].default_value=2.5;tex.inputs['Detail'].default_value=4;tex.inputs['Roughness'].default_value=.7;links.new(tc.outputs['Object'],tex.inputs['Vector'])
  ramp=n.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].position=.15;ramp.color_ramp.elements[0].color=tuple(v*.78 for v in color)+(1,);ramp.color_ramp.elements[1].position=.85;ramp.color_ramp.elements[1].color=tuple(min(1,v*1.16) for v in color)+(1,);links.new(tex.outputs['Fac'],ramp.inputs[0]);links.new(ramp.outputs['Color'],p.inputs['Base Color'])
  fine=n.new('ShaderNodeTexNoise');fine.inputs['Scale'].default_value=48;fine.inputs['Detail'].default_value=2;links.new(tc.outputs['Object'],fine.inputs['Vector']);b=n.new('ShaderNodeBump');b.inputs['Strength'].default_value=.23;b.inputs['Distance'].default_value=.017;links.new(fine.outputs['Fac'],b.inputs['Height']);links.new(b.outputs[0],p.inputs['Normal'])
 return m

def mesh(name,verts,faces,material='concrete',loc=(0,0,0),bevel=0):
 me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
 bm=bmesh.new();bm.from_mesh(me);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=0.000001);bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=0.0000001);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
 o=bpy.data.objects.new(name,me);C.objects.link(o);o.location=loc;o['owner']=TAG;o.data.materials.append(M[material])
 if DETAIL and bevel:
  b=o.modifiers.new('Small worn edges','BEVEL');b.width=bevel;b.segments=2
 return o

def box(name,loc,size,material='concrete',bevel=.035,rot=0):
 x,y,z=[v/2 for v in size];v=[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),(-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
 o=mesh(name,v,[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)],material,loc,min(bevel,min(size)*.2));o.rotation_euler.z=rot;return o

def cyl(name,loc,r,h,material='concrete',n=32,top=None,rot=0):
 if top is None:top=r
 vs=[(rad*cos(2*pi*i/n+rot),rad*sin(2*pi*i/n+rot),z) for z,rad in [(-h/2,r),(h/2,top)] for i in range(n)]
 fs=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
 o=mesh(name,vs,fs,material,loc,min(.015,r*.15,h*.15) if n<16 else 0)
 if n>=16:
  for p in o.data.polygons[2:]:p.use_smooth=True
 return o

def rod(name,a,b,r=.04,material='metal',n=10):
 a,b=Vector(a),Vector(b);o=cyl(name,(a+b)*.5,r,(b-a).length,material,n);o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return o

def uvball(name,loc,scale,material='white',seg=24,rings=12):
 bm=bmesh.new();bmesh.ops.create_uvsphere(bm,u_segments=seg,v_segments=rings,radius=1)
 for v in bm.verts:v.co.x*=scale[0];v.co.y*=scale[1];v.co.z*=scale[2]
 me=bpy.data.meshes.new(name);bm.to_mesh(me);bm.free();o=bpy.data.objects.new(name,me);C.objects.link(o);o.location=loc;o['owner']=TAG;me.materials.append(M[material]);
 for p in me.polygons:p.use_smooth=True
 return o

def polygon_prism(name,xy,z0,z1,material='concrete'):
 n=len(xy);return mesh(name,[(x,y,z) for z in (z0,z1) for x,y in xy],[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],material,bevel=.025)

def profile(name,xz,depth,loc=(0,0,0),material='concrete',rot=0):
 n=len(xz);o=mesh(name,[(x,y,z) for y in (-depth/2,depth/2) for x,z in xz],[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],material,loc,.025);o.rotation_euler.z=rot;return o

def beam(name,a,b,width,height,z,material='concrete'):
 a,b=Vector(a),Vector(b);d=b-a;return box(name,((a.x+b.x)/2,(a.y+b.y)/2,z),(d.length,width,height),material,rot=math.atan2(d.y,d.x))

def railing(name,points,z,h=1,spacing=1.4):
 seen=set()
 for a,b in zip(points[:-1],points[1:]):
  a,b=Vector(a),Vector(b);d=b-a
  for high in [h*.48,h]:rod(name+' rail',(*a,z+high),(*b,z+high),.035,'yellow')
  count=max(1,math.ceil(d.length/spacing))
  for j in range(count+1):
   q=a+d*j/count;key=(round(q.x,5),round(q.y,5))
   if key not in seen:rod(name+' post',(*q,z),(*q,z+h),.045,'yellow');seen.add(key)

def stairs(name,start,end,width,z0,z1,rail=False):
 a,b=Vector(start),Vector(end);d=b-a;L=d.length;cnt=max(1,round((z1-z0)/.18));xz=[(0,0),(L,0),(L,z1-z0)]
 for i in range(cnt-1,-1,-1):xz.extend([(L*i/cnt,(z1-z0)*(i+1)/cnt),(L*i/cnt,(z1-z0)*i/cnt)])
 # Remove duplicate corner vertices
 clean=[]
 for p in xz:
  if not clean or (Vector(p)-Vector(clean[-1])).length>1e-7:clean.append(p)
 if clean[-1]==clean[0]:clean.pop()
 o=profile(name,clean,width,(*a,z0),'concrete',math.atan2(d.y,d.x));o.modifiers.clear()
 if DETAIL:
  perp=Vector((-d.y,d.x)).normalized()
  for i in range(1,cnt+1):
   p=a+d*(i-1)/cnt;beam(name+' tread nosing',p-perp*(width/2-.03),p+perp*(width/2-.03),.09,.035,z0+(z1-z0)*i/cnt,'trim')
  if rail:
   for side in [-1,1]:
    aa=a+perp*width/2*side;bb=b+perp*width/2*side
    for dz in [.55,1.05]:rod(name+' handrail',(*aa,z0+dz),(*bb,z1+dz),.035,'yellow')
    for t in [i/6 for i in range(7)]:
     p=aa+(bb-aa)*t;z=z0+(z1-z0)*t;rod(name+' upright',(*p,z+(.03 if t<.99 else -.03)),(*p,z+1.05),.04,'yellow')
 return o

def perimeter():
 use('01_Perimeter')
 pts=[(-32,-55),(32,-55),(38,-20),(36,25),(30,54),(-30,54),(-36,25),(-38,-20)]
 # Clockwise continuous runs split only at the three observed breaches.
 runs=[((-32,-55),(-28,-55)),((-15,-55),(-14.15,-55)),((6.55,-55),(32,-55)),((32,-55),(38,-20)),((38,-20),(37.2,-5)),((36.6,10),(36,25)),((36,25),(30,54)),((30,54),(-17,54)),((-25.5,54),(-30,54)),((-30,54),(-36,25)),((-36,25),(-38,-20)),((-38,-20),(-32,-55))]
 for k,(a,b) in enumerate(runs):
  h=5.6 if a[1]==-55 else 6.2
  beam('Wall %02d'%k,a,b,1.0,h+.08,(h-.08)/2)
  beam('Wall coping %02d'%k,a,b,1.28,.30,h+.15,'trim')
  aa,bb=Vector(a),Vector(b);d=bb-aa;count=max(1,round(d.length/5.8));n=Vector((-d.y,d.x)).normalized()
  for i in range(count+1):
   p=aa+d*i/count
   if abs(p.x)<15 and p.y<-50:continue
   box('Wall pier',(*p,h/2),(1.05,1.7,h+.38),'trim',rot=math.atan2(d.y,d.x))
   # outward facing tapered buttress
   if DETAIL:
    q=p+n*1.0;profile('Wall buttress',[(0,0),(1.3,0),(.4,h*.78),(0,h*.78)],.85,(*q,0),'concrete',math.atan2(n.y,n.x))
  if DETAIL:
   for z in [1.1,3.1]:beam('Horizontal form joint',aa+n*.506,bb+n*.506,.014,.015,z,'darkconcrete')
 for i,(x,y) in enumerate([(-32,-55),(32,-55),(-38,-20),(38,-20),(-36,25),(36,25),(-30,54),(-8,54),(2,54),(30,54)]):tower(i,x,y)

def tower(i,x,y):
 # floor0, main shaft7.2; recessed gallery, broad octagonal coping.
 box(f'Tower {i:02} plinth',(x,y,.2),(3.55,3.55,.6),'trim')
 box(f'Tower {i:02} shaft',(x,y,3.7),(3.15,3.15,7.0))
 cyl('Tower neck',(x,y,7.25),2.05,.45,'trim',8,top=2.35,rot=pi/8)
 cyl('Tower glazing',(x,y,7.8),2.04,.7,'glass',8,top=2.35,rot=pi/8)
 cyl('Tower gallery base',(x,y,8.4),2.65,.5,'trim',8,rot=pi/8)
 cyl('Tower deck',(x,y,8.68),2.48,.08,'roof',8,rot=pi/8)
 if not DETAIL:return
 for a in [2*pi*j/8+pi/8 for j in range(8)]:rod('Gallery mullion',(x+2.13*cos(a),y+2.13*sin(a),7.35),(x+2.40*cos(a),y+2.40*sin(a),8.2),.09,'metal')
 pts=[(x+2.43*cos(2*pi*j/8+pi/8),y+2.43*sin(2*pi*j/8+pi/8)) for j in range(9)];railing('Gallery',pts,8.75,.85,2)
 box('Tower radio cabinet',(x+.25,y+.15,9.15),(1.0,.7,.85),'metal')
 for j in range(3):
  a=(x-.8+j*.55,y+.35,8.8);h=2.2+j*.4;rod('Antenna mast',a,(a[0],a[1],8.8+h),.045,'metal')
  for z in [9.5,10.2]:rod('Antenna crossbar',(a[0]-.18,a[1],z),(a[0]+.18,a[1],z),.025,'metal',8)
 if i in [0,1,4,5]:
  rod('Twin light pole',(x+.65,y-.3,8.8),(x+.65,y-.3,11.1),.05,'metal')
  for d in [-.23,.23]:uvball('Lamp globe',(x+.65+d,y-.3,11.1),(.21,.21,.24),'white',16,8)
 else:dish('Tower dish',(x+.7,y-.7,9.0),.5,(-.3,-1,.6))

def flatbuilding(name,x,y,w,d,h):
 use(name)
 box(name+' body',(x,y,h/2),(w,d,h))
 box(name+' foundation',(x,y,.2),(w+.25,d+.2,.4),'darkconcrete')
 box(name+' flat roof',(x,y,h+.10),(w+.5,d+.5,.25),'trim')
 box(name+' roof finish',(x,y,h+.24),(w-.4,d-.4,.045),'roof')
 for side in [-1,1]:
  if (name,side) not in [('03_A1',1),('04_B1',1)]:
   box(name+' parapet side',(x+side*w/2,y,h+.55),(.35,d+.5,.65),'trim')
  if (name,side) not in [('03_A1',-1),('05_C1',1)]:
   box(name+' parapet long',(x,y+side*d/2,h+.55),(w+.4,.35,.65),'trim')
 if DETAIL:
  for xx in [x-w/2,x-w/4,x,x+w/4,x+w/2]:box(name+' facade pier',(xx,y-d/2-.12,h/2),(.35,.25,h),'trim')
 return h+.26

def plaza():
 use('02_Plaza')
 x,y,w,d,h=-5.25,7.4,31.5,35.8,2.0
 box('Raised square plaza',(x,y,(h-.15)/2),(w,d,h+.15),'concrete')
 box('Paved plaza',(x,y,h+.025),(w,.0+d,.05),'ground')
 # Split front retaining coping for centered staircase.
 for xa,xb in [(-21,-10),(0,10.5)]:box('Plaza front coping',((xa+xb)/2,-10.0,h+.16),(xb-xa,.55,.30),'trim')
 stairs('Plaza south stairs',(-5,-15.2),(-5,-10.5),10,0,h)
 stairs('Plaza east stairs',(12.45,7),(12.45,18.4),3.9,0,h)
 # west side ramp rising north, no wall obstructing its ends
 profile('Plaza west ramp',[(0,0),(12,0),(12,h)],4.2,(-22.4,6,0),'ground',pi/2)
 for xx in [-24.5,-20.3]:profile('Ramp curb',[(0,0),(12,0),(12,2.45),(0,.45)],.35,(xx,6,0),'trim',pi/2)
 if DETAIL:
  for yy in [-10,7.4,24.8]:box('Plaza expansion joint',(x,yy,h+.055),(w,.025,.009),'darkconcrete',0)
  for xx in [-21,-5.25,10.5]:box('Plaza expansion joint',(xx,y,h+.055),(.025,d,.009),'darkconcrete',0)
  for xx in [-10.4,.4]:box('Plaza stair pier',(xx,-10.4,1.4),(.7,1.0,2.8),'trim')
  for xx in [-21,10.5]:
   for ya,yb in [(-10.6,17.4),(20.8,25.4)]:box('Plaza edge wall',(xx,(ya+yb)/2,1.315),(.45,yb-ya,2.77),'trim')
  box('Plaza east upper landing',(12.1,19.5,1.85),(5.1,2.4,.3),'trim')
  box('Plaza west upper landing',(-22.4,19.3,1.85),(4.4,2.8,.3),'trim')
 # raised flagcourt connects without an artificial roadway
 for i,(w,d,z) in enumerate([(7.2,5.2,.22),(6.2,4.3,.22),(5.2,3.3,.22)]):box('Flag base step',(-3,32,(i+.5)*z),(w,d,z),'trim')
 box('Flag plinth',(-3,32,.85),(2,1.8,.4),'trim')
 cyl('Flag pedestal',(-3,32,1.12),.43,.4,'metal')
 rod('Single flagpole',(-3,32,1.3),(-3,32,8.1),.062,'metal')
 if DETAIL:
  vs=[];fs=[]
  for j in range(7):
   for i in range(16):
    t=i/15;s=j/6;vs.append((-2.93+t*2.2,32+sin(t*7+s)*.22*t,7.8-s*1.55-.26*t+sin(t*4)*.09))
  for j in range(6):
   for i in range(15):a=j*16+i;fs.append((a,a+1,a+17,a+16))
  mesh('Red cloth flag',vs,fs,'red')

def basic_buildings():
 h=flatbuilding('03_A1',-3,46.6,25,6.0,3.5)
 if DETAIL:decorate_a1(h)
 h=flatbuilding('04_B1',21.2,13.1,13.3,8.8,5.5)
 if DETAIL:decorate_b1(h)
 h=flatbuilding('05_C1',-3.8,-48.7,20.7,11.6,9.1)
 if DETAIL:decorate_c1(h)
 use('06_B2')
 x,y=21.1,-22
 for r,z,h in [(8.25,.18,.36),(7.8,.5,.35)]:cyl('B2 foundation',(x,y,z),r,h,'trim',64)
 cyl('B2 drum',(x,y,3.55),7.0,5.8,'concrete',64)
 cyl('B2 roof cornice',(x,y,6.55),7.45,.55,'trim',64)
 cyl('B2 roof deck',(x,y,6.85),7.25,.09,'roof',64)
 # lathed hemisphere above roof
 vs=[];fs=[];n=64;rows=17
 for j in range(rows):
  a=(pi/2)*j/(rows-1);r=3.6*cos(a)
  for i in range(n):b=2*pi*i/n;vs.append((x+r*cos(b),y+r*sin(b),6.9+4.4*sin(a)))
 for j in range(rows-1):
  for i in range(n):a=j*n+i;b=j*n+(i+1)%n;fs.append((a,b,b+n,a+n))
 ob=mesh('B2 radome',vs,fs,'white')
 for p in ob.data.polygons:p.use_smooth=True
 if DETAIL:decorate_b2(x,y)
 use('07_Equipment');warehouse('Equipment',-15.8,30.5,12,7,4.4)
 use('08_Medical');warehouse('Medical',9.3,30.5,11,7,4.4)
 use('09_Armory');warehouse('Armory',-29.1,-7,10.8,13,5.6)
 use('10_Sheds');shed('Utility shed',-31.2,25,5.6,6,4.9);shed('Supply shed',-27.2,18.7,4.5,4.4,4.5)

def warehouse(name,x,y,w,d,h):
 # Roof is deliberately a retained rear strip; open towards camera and north access.
 box(name+' floor',(x,y,.14),(w-.65,d-.65,.28),'darkconcrete')
 box(name+' rear wall',(x,y+d/2,h/2),(w,.6,h))
 box(name+' west wall',(x-w/2,y,h/2),(.6,d,h))
 box(name+' east wall',(x+w/2,y+d*.25,h/2),(.6,d*.5,h))
 box(name+' roof rear',(x,y+d*.35,h+.1),(w+.4,d*.3,.28),'concrete')
 for xx,yy in [(x-w/2,y-d/2),(x+w/2,y-d/2),(x-w/2,y+d/2),(x+w/2,y+d/2)]:
  box(name+' pier',(xx,yy,h/2),(.85,.85,h+.4),'trim');box(name+' pier shoe',(xx,yy,.4),(1,1,.8),'trim')
 if DETAIL:decorate_warehouse(name,x,y,w,d,h)

def shed(name,x,y,w,d,h):
 box(name,(x,y,h/2),(w,d,h),'metal')
 box(name+' corrugated roof',(x,y,h+.1),(w+.6,d+.6,.2),'metal')
 if DETAIL:
  for j in range(int(w/.21)):box('Corrugation',(x-w/2+j*.21,y-d/2-.035,h/2),(.035,.07,h),'trim',0)
  for j in range(int(w/.21)):box('Roof rib',(x-w/2+j*.21,y,h+.24),(.03,d+.5,.08),'metal',0)
  door(name+' door',x,y-d/2-.08,0,1.4,2.6)

def camera(name,pos,target,ortho=83.6):
 data=bpy.data.cameras.new(name);o=bpy.data.objects.new(name,data);S.collection.objects.link(o);o['owner']=TAG;o.location=pos;o.rotation_euler=(Vector(target)-Vector(pos)).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=ortho;data.lens=50;S.camera=o;return o

def light_camera():
 elev=math.radians(18);target=(0,0,4.97);camera('CAM_Reference',(0,-200*cos(elev),4.97+200*sin(elev)),target)
 l=bpy.data.lights.new('Sun','SUN');l.energy=2.2;l.angle=.06;o=bpy.data.objects.new('Sun',l);S.collection.objects.link(o);o['owner']=TAG;o.rotation_euler=(math.radians(28),math.radians(-23),math.radians(-35))
 for area in bpy.context.screen.areas:
  if area.type=='VIEW_3D':area.spaces.active.region_3d.view_perspective='CAMERA'

def build(detail=False):
 setup(detail)
 pts=[(-33,-56.5),(33,-56.5),(39.2,-20),(37.3,25),(31,55.5),(-31,55.5),(-37.3,25),(-39.2,-20)]
 polygon_prism('Compound foundation',pts,-.4,0,'ground')
 perimeter();plaza();basic_buildings()
 if detail:extras()
 light_camera();
 if detail:refine_geometry()
 soften_compound(S,detailed=detail)
 if detail:prepare_delivery()
 S['reference']='ref/target.png';S['inference']='See asset-brief.md; hidden geometry inferred; no model extraction.'
 bpy.context.view_layer.update();OUT.mkdir(parents=True,exist_ok=True)
 bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'EN_FortifiedCompound.blend'))
 print(json.dumps({'objects':len(S.objects),'phase':'detail' if detail else 'blockout','master':str(ROOT/'EN_FortifiedCompound.blend')}))

# Detail functions are loaded separately, keeping calibration and art passes reproducible.
exec(compile((ROOT/'build/details.py').read_text(),str(ROOT/'build/details.py'),'exec'),globals()) if (ROOT/'build/details.py').exists() else None
exec(compile((ROOT/'build/refine.py').read_text(),str(ROOT/'build/refine.py'),'exec'),globals()) if (ROOT/'build/refine.py').exists() else None
exec(compile((ROOT/'build/finish.py').read_text(),str(ROOT/'build/finish.py'),'exec'),globals()) if (ROOT/'build/finish.py').exists() else None
exec(compile((ROOT/'build/stair_access.py').read_text(),str(ROOT/'build/stair_access.py'),'exec'),globals())
exec(compile((ROOT/'build/architectural_corners.py').read_text(),str(ROOT/'build/architectural_corners.py'),'exec'),globals())
if __name__=='__main__':build('--detail' in sys.argv)
