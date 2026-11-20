"""Visible architectural detailing. Evaluated in compound.py namespace."""
def text_label(text,loc,size=1.25,material='dark',rotation=(pi/2,0,0)):
 cu=bpy.data.curves.new('Stencil '+text,'FONT');cu.body=text;cu.size=size;cu.extrude=.001;cu.align_x='LEFT';o=bpy.data.objects.new('Faded stencil '+text,cu);C.objects.link(o);o['owner']=TAG;o.location=loc;o.rotation_euler=rotation;cu.materials.append(M[material]);return o

def door(name,x,y,z,w,h):
 box(name+' dark recess',(x,y,z+h/2),(w+.22,.09,h+.12),'dark')
 box(name+' olive leaf',(x,y-.065,z+h/2),(w,.09,h),'olive')
 for xx in [x-w/2-.06,x+w/2+.06]:box(name+' frame',(xx,y-.10,z+h/2),(.09,.13,h+.16),'metal')
 box(name+' lintel',(x,y-.11,z+h+.06),(w+.22,.13,.12),'metal')
 rod(name+' handle',(x+w*.32,y-.16,z+h*.46),(x+w*.32,y-.16,z+h*.54),.025,'dark')
 for zz in [z+h*.46,z+h*.54]:rod(name+' handle mount',(x+w*.32,y-.16,zz),(x+w*.32,y-.075,zz),.025,'dark')
 box(name+' kick plate',(x,y-.12,z+.14),(w-.08,.04,.22),'metal')

def window(name,x,y,z,w,h):
 box(name+' black reveal',(x,y,z),(w+.25,.12,h+.25),'dark')
 box(name+' glass',(x,y-.07,z),(w,.07,h),'glass')
 for xx in [x-w/2,x+w/2]:box(name+' concrete jamb',(xx,y-.15,z),(.13,.22,h+.24),'trim')
 for zz in [z-h/2,z+h/2]:box(name+' concrete sill',(x,y-.18,zz),(w+.26,.28,.13),'trim')
 for j in range(1,6):rod(name+' bars',(x-w/2+w*j/6,y-.2,z-h/2),(x-w/2+w*j/6,y-.2,z+h/2),.025,'metal',8)
 for zz in [z-h/4,z,z+h/4]:rod(name+' bars',(x-w/2,y-.2,zz),(x+w/2,y-.2,zz),.023,'metal',8)

def hvac(x,y,z,w=1.1,h=1.4):
 box('HVAC plinth',(x,y,z+.12),(w+.25,.9,.24),'dark')
 box('HVAC steel casing',(x,y,z+.24+h/2),(w,.7,h),'metal')
 for i in range(10):box('HVAC louvers',(x-w/2+.06+i*(w-.12)/9,y-.37,z+.24+h/2),(.025,.08,h-.15),'dark',.005)
 box('HVAC top cap',(x,y,z+h+.28),(w+.08,.8,.1),'trim')

def sandbag(x,y,z,rot=0):
 o=uvball('Canvas sandbag',(x,y,z),(.58,.28,.18),'sandbag',16,8);o.rotation_euler.z=rot
 # stitched seam supported on fabric surface
 rod('Sandbag seam',(x-.43*cos(rot),y-.43*sin(rot),z+.13),(x+.43*cos(rot),y+.43*sin(rot),z+.13),.011,'darkconcrete',6)

def bagwall(x,y,z):
 for k in range(3):
  for j in range(4-k//2):sandbag(x+(j-1.5)*.72+(.3 if k%2 else 0),y+k*.15,z+.16+k*.22)

def lamp(x,y,z):
 rod('Wall lamp bracket',(x,y+.45,z+.2),(x,y-.28,z+.38),.04,'dark')
 cyl('Lamp shade',(x,y-.3,z+.15),.25,.18,'metal',24,top=.1)
 uvball('Lamp bulb',(x,y-.3,z+.08),(.105,.105,.10),'white',16,8)
 # Deliberately restrained functional pool, not neon contour lighting.
 l=bpy.data.lights.new('Warm wall sconce','POINT');l.energy=12;l.color=(1,.75,.42);l.shadow_soft_size=.15;o=bpy.data.objects.new('Warm wall sconce',l);C.objects.link(o);o['owner']=TAG;o.location=(x,y-.38,z)

def facade_detail(name,x,y,w,h):
 # Main formwork lifts, tie holes and very slight localized weather streaks.
 for z in [1.0,3.1,5.0,7.0]:
  if z<h:box(name+' casting seam',(x,y-.005,z),(w,.008,.018),'darkconcrete',0)
 for j in range(int(w/2.2)):
  xx=x-w/2+.7+j*2.2
  for z in [.7,2.4,4.2,6.3,8.0]:
   if z<h-.2:
    o=cyl('Formwork tie recess',(xx,y-.017,z),.028,.012,'darkconcrete',8);o.rotation_euler.x=pi/2


def remove_objects(prefix):
 for o in list(C.objects):
  if o.name.startswith(prefix):bpy.data.objects.remove(o,do_unlink=True)

def decorate_a1(h):
 y=43.6
 for x in [-10.5,-7.5,-4.5]:window('A1 barred window',x,y-.08,2.05,1.7,1.25)
 door('A1 service entry',1.0,y-.08,0,1.4,2.5)
 text_label('A-1',(-14.1,y-.17,1.95),1.2)
 for x in [-10.5,-1.2]:hvac(x,48.2,h,1.15,1.2)
 for x in [-6.5,2.2]:bagwall(x,46.5,h+.06)
 remove_objects('03_A1 parapet side');box('A1 west parapet',(-15.5,46.6,4.05),(.35,6.5,.65),'trim')
 facade_detail('A1',-3,y-.14,25,3.5)
 # Roof front parapet split at stair access on right.
 remove_objects('03_A1 parapet long')
 box('A1 rear parapet',(-3,49.6,4.05),(25.4,.35,.65),'trim')

def decorate_b1(h):
 y=8.7
 text_label('B-1',(22.3,y-.2,3.85),1.4)
 # Recessed equipment bay with a horizontal pressure vessel.
 box('B1 equipment inset',(19.8,y-.16,2.15),(3.7,.15,3.4),'darkconcrete')
 for x in [18.0,21.6]:box('B1 equipment surround',(x,y-.27,2.15),(.12,.25,3.5),'metal')
 box('B1 equipment surround',(19.8,y-.27,3.9),(3.7,.25,.13),'metal')
 tank('B1 utility tank',(19.8,y-.7,2.25),.65,2.8,'X')
 hvac(18.2,15.8,h,1.3,1.5)
 remove_objects('04_B1 parapet side')
 box('B1 west parapet',(14.55,13.1,6.05),(.35,9.3,.65),'trim')
 facade_detail('B1',21.2,y-.14,13.3,5.5)

def decorate_c1(h):
 y=-54.5
 # The large opening is represented by panelled industrial gate; real recess between jambs.
 door('C1 main gate',-2.5,y-.13,.06,6.3,7.0)
 for z in [.3,1.55,2.8,4.05,5.3,6.55]:box('C1 gate horizontal channel',(-2.5,y-.29,z),(6.2,.11,.11),'metal')
 box('C1 gate centre mullion',(-2.5,y-.32,3.6),(.13,.12,7.0),'metal')
 for xx in [-5.87,.87]:
  box('Gate heavy concrete jamb',(xx,y-.25,3.8),(.26,.38,7.6),'trim')
  hazard_panel(xx,y-.46,.0,.45,2.7)
 box('Gate concrete lintel',(-2.5,y-.25,7.5),(7.1,.38,.28),'trim')
 text_label('C-1',(3.0,y-.20,4.7),1.3)
 box('C1 ventilation frame',(-11.3,y-.2,5.0),(2.1,.18,1.65),'trim')
 box('C1 vent black inset',(-11.3,y-.32,5.0),(1.9,.1,1.45),'dark')
 for z in [4.37+i*.12 for i in range(11)]:box('C1 vent slat',(-11.3,y-.39,z),(1.85,.12,.045),'metal')
 lamp(-6.8,y-.3,7.05);lamp(1.7,y-.3,7.05)
 for x in [-9.6,1.2]:bagwall(x,-51.6,h+.08)
 hvac(-3.8,-48.4,h,1.55,1.6)
 facade_detail('C1',-3.8,y-.14,20.7,9.1)
 # Replace rear parapet with a deliberate access gap at upper landing.
 remove_objects('05_C1 parapet long')
 box('C1 front parapet',(-3.8,-54.5,9.65),(21.1,.35,.65),'trim')

def hazard_panel(x,y,z,w,h):
 box('Hazard backing',(x,y,z+h/2),(w,.045,h),'yellow',0)
 # Stripes clipped to a rectangular jamb face.
 for b in [i*.45-h for i in range(int((h+w)*3)+3)]:
  poly=[(-w/2,0),(w/2,0),(w/2,h),(-w/2,h)]
  for A,B,keep in [(1,-1,b),( -1,1,-b-.18)]:
   out=[]
   for p,q in zip(poly,poly[1:]+poly[:1]):
    fp=A*p[0]+B*p[1]-keep;fq=A*q[0]+B*q[1]-keep
    if fp>=0:out.append(p)
    if (fp>=0)!=(fq>=0):t=fp/(fp-fq);out.append((p[0]+(q[0]-p[0])*t,p[1]+(q[1]-p[1])*t))
   poly=out
   if not poly:break
  if len(poly)>2:mesh('Diagonal hazard paint',[(x+a,y-.026,z+c) for a,c in poly],[tuple(range(len(poly)))],'dark')

def tank(name,loc,r,length,axis='Y'):
 x,y,z=loc;o=cyl(name,loc,r,length,'metal',32)
 if axis=='Y':o.rotation_euler.x=pi/2;unit=Vector((0,1,0))
 else:o.rotation_euler.y=pi/2;unit=Vector((1,0,0))
 for d in [-1,1]:
  p=Vector(loc)+unit*length/2*d
  uvball(name+' end cap',p,(r,r*.25,r) if axis=='Y' else (r*.25,r,r),'metal')
 for t in [-.3,.3]:
  p=Vector(loc)+unit*length*t
  # Tank strap represented as a thin raised ring.
  ring(name+' band',p,r+.025,.06,'darkconcrete',axis)
 for t in [-.3,.3]:
  p=Vector(loc)+unit*length*t;box(name+' saddle',(p.x,p.y,z-r-.2),(.9,.6,.45),'darkconcrete')
 rod(name+' pipe',(x,y,z+r),(x,y,z+r+.3),.065,'metal')

def ring(name,loc,r,width,material='metal',axis='Z',n=64):
 # thin closed annulus, wall width .035
 vs=[];fs=[]
 for z,rr in [(-width/2,r),(width/2,r),(width/2,r-.045),(-width/2,r-.045)]:
  for i in range(n):a=i*2*pi/n;vs.append((rr*cos(a),rr*sin(a),z))
 for k in range(4):
  for i in range(n):fs.append((k*n+i,k*n+(i+1)%n,((k+1)%4)*n+(i+1)%n,((k+1)%4)*n+i))
 o=mesh(name,vs,fs,material,loc)
 if axis=='Y':o.rotation_euler.x=pi/2
 if axis=='X':o.rotation_euler.y=pi/2
 return o

def dish(name,loc,r,direction):
 # Real concave paraboloid with back thickness, tilted as a rigid assembly.
 vs=[];fs=[];n=40;rows=9
 for back in [0,1]:
  for j in range(rows):
   rr=r*j/(rows-1)
   for i in range(n):a=i*2*pi/n;vs.append((rr*cos(a),rr*sin(a),.32*rr*rr/r-back*.055))
 for k in [0,1]:
  off=k*n*rows
  for j in range(rows-1):
   for i in range(n):a=off+j*n+i;b=off+j*n+(i+1)%n;fs.append((a,b,b+n,a+n) if k==0 else (a+n,b+n,b,a))
 for i in range(n):a=(rows-1)*n+i;b=(rows-1)*n+(i+1)%n;fs.append((a,b,b+n*rows,a+n*rows))
 o=mesh(name,vs,fs,'white',loc);q=Vector(direction).normalized().to_track_quat('Z','Y');o.rotation_euler=q.to_euler()
 for p in o.data.polygons:p.use_smooth=True
 centre=Vector(loc);tip=centre+q@Vector((0,0,r*.8));rod(name+' feed arm',centre+q@Vector((0,-r,.32*r)),tip,.032,'metal');uvball(name+' receiver',tip,(.08,.08,.14),'dark',12,6)
 base=Vector((loc[0],loc[1],loc[2]-r*.85));rod(name+' mount',base,centre,.1,'metal')
 for side in [-1,1]:rod(name+' tripod',base+Vector((side*r*.5,0,-.2)),centre,.055,'metal')

def decorate_b2(x,y):
 for i in range(16):
  a=2*pi*i/16;xx=x+7.02*cos(a);yy=y+7.02*sin(a)
  o=box('B2 structural rib',(xx,yy,3.62),(.30,.35,5.85),'trim',rot=a)
  box('B2 pier base',(xx,yy,.95),(.45,.48,.8),'trim',rot=a)
 pts=[(x+7.0*cos(2*pi*i/32),y+7.0*sin(2*pi*i/32)) for i in range(33)];railing('B2 roof safety',pts,6.95,.85,1.3)
 door('B2 access',x,y-7.08,.7,1.75,3.1);lamp(x,y-7.2,4.4)
 # text tangent to curved facade towards southeast, readable from main camera
 text_label('B-2',(x+4.0,y-5.85,4.2),1.25,rotation=(pi/2,0,.48))
 for az in range(0,360,45):
  a=math.radians(az);pts=[]
  for j in range(17):b=pi/2*j/16;pts.append((x+3.615*cos(b)*cos(a),y+3.615*cos(b)*sin(a),6.905+4.415*sin(b)))
  for aa,bb in zip(pts[:-1],pts[1:]):rod('Radome meridian seam',aa,bb,.014,'darkconcrete',6)
 for b in [pi/6,pi/3]:ring('Radome latitude seam',(x,y,6.905+4.415*sin(b)),3.615*cos(b),.025,'darkconcrete')
 dish('B2 west uplink',(x-4.8,y+.1,8.5),1.4,(-.5,-.7,1))
 dish('B2 east uplink',(x+4.9,y+.2,8.4),1.2,(.8,-.2,1))


def crate(name,x,y,z,w=1.8,d=1.1,h=1.25,rot=0):
 # Each crate is its own editable group with shallow reinforced corners.
 parts=[]
 parts.append(box(name,(x,y,z+h/2),(w,d,h),'olive',.045))
 for dz in [.08,h-.08]:parts.append(box(name+' rim',(x,y,z+dz),(w+.055,d+.055,.09),'metal',.015))
 for xx in [x-w/2+.06,x+w/2-.06]:
  for yy in [y-d/2-.018,y+d/2+.018]:parts.append(box(name+' corner',(xx,yy,z+h/2),(.1,.06,h-.1),'metal',.008))
 for xx in [x-w*.26,x+w*.26]:parts.append(box(name+' clasp',(xx,y-d/2-.06,z+h*.7),(.1,.15,.2),'metal',.008))
 parts.append(box(name+' handle',(x,y-d/2-.07,z+h*.6),(.38,.18,.06),'dark',.008))
 if rot:
  c,s=cos(rot),sin(rot)
  for o in parts:
   dx,dy=o.location.x-x,o.location.y-y;o.location.x=x+c*dx-s*dy;o.location.y=y+s*dx+c*dy;o.rotation_euler.z=rot
 return parts

def rifle(x,y,z):
 # One inert display prop; no functional internal mechanism.
 box('Single rifle receiver',(x,y,z+.11),(.45,.13,.18),'dark',.025)
 box('Rifle buttstock',(x-.48,y,z+.10),(.50,.14,.20),'olive',.04)
 box('Rifle foregrip',(x+.40,y,z+.10),(.38,.14,.14),'olive',.02)
 rod('Rifle barrel',(x+.58,y,z+.12),(x+1.05,y,z+.12),.028,'dark')
 o=box('Rifle magazine',(x+.12,y-.04,z+.02),(.16,.12,.30),'dark',.02);o.rotation_euler.y=-.15
 rod('Rifle sights',(x+.57,y,z+.15),(x+.57,y,z+.24),.018,'dark')

def vest(x,y,z):
 # hanging on inner back wall; tapered shoulder profile, straps and pouches.
 prof=[(-.48,0),(.48,0),(.48,.65),(.29,.95),(.27,1.26),(.11,1.26),(.08,.94),(-.08,.94),(-.11,1.26),(-.27,1.26),(-.29,.95),(-.48,.65)]
 profile('Hanging armor vest',prof,.16,(x,y,z),'olive')
 for xx in [x-.29,x,x+.29]:box('Vest pouch',(xx,y-.16,z+.4),(.24,.16,.32),'olive',.035)
 rod('Armor wall hook',(x,y+.2,z+1.32),(x,y-.03,z+1.38),.035,'metal')

def decorate_warehouse(name,x,y,w,d,h):
 # Rough surviving front edge of roof, not loose floating ruin.
 edgey=y+d*.20
 for j in range(round(w/.55)):
  xx=x-w/2+j*.55+.25;sz=rng.uniform(.22,.48)
  box(name+' chipped roof edge',(xx,edgey-rng.uniform(.0,.18),h+.08),(.54,sz,.28),'trim',.05,rot=rng.uniform(-.06,.06))
 facade_detail(name,x,y+d/2-.32,w,h)
 lamp(x-w*.1 if name=='Armory' else x+w*.24,y+d/2-.4,h-.5)
 if name=='Equipment':
  for xx,yy,zz in [(x-2,y+2,0.3),(x+.1,y+2,.3),(x-1.2,y+2,1.55)]:crate('Equipment case',xx,yy,zz)
  vest(x+3.3,y+d/2-.48,1.7)
  # Subtle crossed-tools mark on forward pier, without large signs.
  for sign in [-1,1]:rod('Equipment crossed tool',(x-w/2-.25,y-d/2-.46,2.1),(x-w/2+.25,y-d/2-.46,2.7),.065,'darkconcrete') if sign==1 else rod('Equipment crossed tool',(x-w/2+.25,y-d/2-.46,2.1),(x-w/2-.25,y-d/2-.46,2.7),.065,'darkconcrete')
 elif name=='Medical':
  crate('Medical supply case',x-2.9,y+1.8,.3,1.8,1.3,1.35);crate('Medical supply case',x-.8,y+2.1,.3,1.6,1.3,1.5)
  box('Medical bench top',(x+3,y+1.8,1.4),(2.2,1,.14),'bark')
  for dx in [-.9,.9]:
   for dy in [-.35,.35]:box('Medical bench leg',(x+3+dx,y+1.8+dy,.8),(.12,.12,1.3),'bark')
  box('Single first aid case',(x+3,y+1.8,1.91),(.95,.5,.9),'medical',.08)
  box('Case white cross horizontal',(x+3,y+1.53,1.91),(.48,.025,.12),'white',.0);box('Case white cross vertical',(x+3,y+1.52,1.91),(.12,.025,.48),'white',.0)
  box('Medical faded panel',(x+w/2,y-d/2-.445,2.7),(.73,.02,.94),'olive',0)
  box('Medical wall cross',(x+w/2,y-d/2-.46,2.7),(.53,.02,.13),'white',0);box('Medical wall cross',(x+w/2,y-d/2-.46,2.7),(.13,.02,.62),'white',0)
 else:
  # Clear exit along the east/north edge, isolated weapon presentation.
  for xx,yy,zz in [(x-2.8,y+2.0,.3),(x-.7,y+2.0,.3),(x-2.8,y+2,1.55),(x-.7,y+2,1.55),(x-3.1,y-1.3,.3)]:crate('Ammunition crate',xx,yy,zz)
  crate('Long weapon transport case',x+.6,y-3.8,.3,4.5,1.35,.85,rot=.10)
  rifle(x+.35,y-3.8,1.18)
  # Open northern egress is obtained by removing the back-right wall bay.
  remove_objects('Armory rear wall')
  box('Armory rear wall retained',(x-1.5,y+d/2,h/2),(w-3,.6,h))

def fractured_end(name,pos,height,side=1,angle=0):
 # Jagged intact side thickens toward base; exposed depth reads as broken concrete.
 xz=[(0,0),(side*2.1,0),(side*1.4,.55),(side*1.65,1.1),(side*.7,1.7),(side*1.25,2.4),(side*.45,3.0),(side*.8,3.65),(side*.25,4.15),(side*.55,4.7),(side*.2,height),(0,height)]
 o=profile(name,xz,1.0,pos,'concrete',angle);o.modifiers.clear();o.data.materials.append(M['trim'])
 for face in o.data.polygons:
  if face.index>2 and face.index<len(o.data.polygons)-1:face.material_index=1
 for j in range(9):
  z=rng.uniform(1,height-.2);off=.08;p=Vector((side*off,0,z));p.rotate(__import__('mathutils').Euler((0,0,angle)));p+=Vector(pos)
  v=Vector((side*rng.uniform(.9,1.7),rng.uniform(-.25,.25),rng.uniform(-.35,.25)));v.rotate(__import__('mathutils').Euler((0,0,angle)));rod('Exposed broken rebar',p,p+v,.027,'rust',8)

def rock(name,loc,scale):
 bm=bmesh.new();bmesh.ops.create_icosphere(bm,subdivisions=1,radius=1)
 for v in bm.verts:
  f=rng.uniform(.78,1.15);v.co.x*=scale[0]*f;v.co.y*=scale[1]*f;v.co.z*=scale[2]*f
  if v.co.z<-.6*scale[2]:v.co.z=-.6*scale[2]
 me=bpy.data.meshes.new(name);bm.to_mesh(me);bm.free();o=bpy.data.objects.new(name,me);C.objects.link(o);o.location=loc;o['owner']=TAG;me.materials.append(M['trim' if rng.random()>.35 else 'darkconcrete']);return o

def debris(cx,cy,wx,wy,count=45):
 for i in range(count):
  x=cx+rng.uniform(-wx/2,wx/2);y=cy+rng.uniform(-wy/2,wy/2);a=rng.uniform(.16,.65);b=rng.uniform(.15,.48);h=rng.uniform(.14,.38)
  rock('Collapsed concrete',(x,y,h*.6),(a,b,h))

def tree(x,y):
 use('12_Tree')
 cyl('Tree soil bed',(x,y,.025),1.5,.05,'bark',32)
 rod('Tree trunk',(x,y,0),(x+.15,y+.1,5.1),.23,'bark',10)
 for k in range(18):
  a=k*2.4;h=3.0+k*.24;base=Vector((x+.1,y,h));end=Vector((x+cos(a)*rng.uniform(2.2,3.7),y+sin(a)*rng.uniform(2.2,3.5),h+rng.uniform(1.6,3.3)))
  rod('Tree branch',base,end,.075,'bark',8)
  for j in range(4):
   tip=end+Vector((rng.uniform(-1.2,1.2),rng.uniform(-1.1,1.1),rng.uniform(-.3,1.1)));rod('Tree twig',end-(tip-end).normalized()*(.05+j*.015),tip,.025,'bark',6)
   # clustered individual leaves, not solid spheres
   vs=[];fs=[]
   for l in range(65):
    q=tip+Vector((rng.uniform(-.65,.65),rng.uniform(-.65,.65),rng.uniform(-.45,.45)));a=rng.uniform(0,2*pi);size=rng.uniform(.10,.20);d=Vector((cos(a)*size,sin(a)*size,rng.uniform(-.06,.06)));p=Vector((-sin(a)*size*.4,cos(a)*size*.4,.02));n=len(vs);vs.extend([q-d,q+p,q+d,q-p]);fs.append((n,n+1,n+2,n+3))
   mesh('Olive tree foliage',vs,fs,'leaf')

def extras():
 use('11_Damage')
 fractured_end('North breach west',(-25.5,54,0),6.2,1)
 fractured_end('North breach east',(-17,54,0),6.2,-1)
 fractured_end('South breach west',(-28,-55,0),5.6,1)
 fractured_end('South breach east',(-15,-55,0),5.6,-1)
 fractured_end('East breach south',(37.2,-5,0),6.2,1,pi/2)
 fractured_end('East breach north',(36.6,10,0),6.2,-1,pi/2)
 for args in [(-24,53.5,3.8,3.5,35),(-18.7,53.5,3.8,3.5,35),(-26.5,-54.5,4,3,35),(-16.8,-54.5,4,3,35),(36.5,-2,3,4.5,40),(36.2,8,3,4.5,40)]:debris(*args)
 use('10_UtilityTank');tank('Warehouse horizontal tank',(-33.5,12.3,1.35),1.0,3.4,'Y')
 tree(17.1,52)
 # Sparse expansion joints; no decorative noise or road markings.
 use('13_Surface')
 for yy in [-36,-17,3,25,40]:
  # Sections in open courtyard only; clipping avoids crossing raised structures.
  for xa,xb in [(-35,-25),(13,35)]:box('Pavement expansion joint',((xa+xb)/2,yy,.006),(xb-xa,.018,.009),'darkconcrete',0)
 # Small functional drain grates at base of stairs.
 for x,y in [(-5,-16),(12,1),(15,-49)]:
  box('Drain recess',(x,y,.012),(1.6,.4,.023),'dark',0)
  for j in range(9):box('Drain grate slat',(x-.72+j*.18,y,.035),(.06,.38,.025),'metal',.002)
