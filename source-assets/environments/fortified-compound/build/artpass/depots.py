"""Open-sided depots share a cast shell, sheltered stock and a visible loading apron."""
import math
from random import Random
import bpy
from mathutils import Vector


def canopy(f,name,x,y,w,d,h,ground):
    rear=d*.5+.13
    left=-w*.5-.13
    right=w*.5+.13-(2.9 if name=='Armory' else 0)
    depths={
        'Equipment':[(0,3.95),(.24,3.95),(.29,3.62),(.31,3.68),(.33,2.27),
                     (.36,2.35),(.39,2.01),(.52,2.01),(.54,1.76),(.59,1.86),(.64,1.66),(1,1.66)],
        'Medical':[(0,1.67),(.39,1.67),(.48,1.78),(.51,1.69),(.55,2.07),(.63,2.07),
                   (.68,2.37),(.70,2.29),(.73,3.29),(.78,3.42),(1,3.42)],
        'Armory':[(0,7.2),(.28,7.2),(.34,6.85),(.35,5.0),(.40,5.13),(.44,4.77),
                  (.50,4.77),(.57,2.62),(.61,2.71),(.66,2.27),(.77,2.27),(1,2.08)]}[name]
    front=[(left+t*(right-left),rear-depth) for t,depth in depths]
    rng=Random({'Equipment':503,'Medical':602,'Armory':748}[name])
    leading=[front[0]]
    wear=[0.]
    for i,(a,b) in enumerate(zip(front,front[1:])):
        fractured=i in range(1,len(front)-2)
        count=max(1,math.ceil(math.dist(a,b)/.42)) if fractured else 1
        for j in range(1,count+1):
            t=j/count
            leading.append((a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1])+
                            (rng.uniform(-.082,.074) if fractured and j<count else 0)))
            wear.append(rng.uniform(.045,.13) if fractured else 0)
    # Round the intact outer corners by three short chords. The fractured face
    # has a stepped break and an aggregate material independent of the weather skin.
    poly=[*leading,(right,rear-.34),(right-.07,rear-.14),(right-.20,rear-.035),
          (right-.34,rear),(left+.42,rear),(left+.19,rear-.055),(left+.055,rear-.20),(left,rear-.44)]
    soffit=ground+h+.10
    top=soffit+.31
    n=len(poly)
    wear.extend([0.]*(n-len(wear)))
    verts=[(px,py+chip*shift,z+(rng.uniform(-.042,.042) if chip and z==.14 else 0))
           for z,shift in ((0,.18),(.14,1.),(.31,.40)) for (px,py),chip in zip(poly,wear)]
    faces=[tuple(reversed(range(n))),tuple(range(2*n,3*n))]
    faces.extend((row*n+i,row*n+(i+1)%n,(row+1)*n+(i+1)%n,(row+1)*n+i)
                 for row in range(2) for i in range(n))
    slab=f.mesh('retained concrete canopy',verts,faces,(x,y,soffit),'cast',.008)
    for key in ('roof','broken','cap'):
        slab.data.materials.append(f.materials[key])
    for face in slab.data.polygons:
        if face.normal.z>.9:
            face.material_index=1
        elif face.index>=2:
            segment=(face.index-2)%n
            face.material_index=2 if segment<len(leading)-1 and (wear[segment] or wear[segment+1]) else 3
    f.box('rear weather flashing',(x+(left+right)*.5,y+rear-.18,top+.015),
          (right-left-.84,.18,.066),'teal',.012)
    front_joints=[.09,.23,.48,.77,.92]
    for t in front_joints:
        u=left+t*(right-left)
        k=next(i for i in range(1,len(leading)) if leading[i][0]>=u)
        p,q=leading[k-1],leading[k]
        edge=p[1]+(q[1]-p[1])*(u-p[0])/(q[0]-p[0])
        forward=y+edge+.18
        back=y+rear-.12
        length=back-forward
        f.box('canopy channel',(x+u,(back+forward)*.5,soffit-.122),(.105,length,.262),'iron',.012)
        f.box('channel lower flange',(x+u,(back+forward)*.5,soffit-.255),(.23,length-.014,.043),'teal',.007)
        if length>3.:
            # The long return in the armory bears on its west-wall girder.
            run=min(length*.52,1.72)
            f.beam('wall corbel',(x+u,y+rear-.34,soffit-1.03),
                   (x+u,y+rear-.34-run,soffit-.25),.095,.11,'iron')
    f.box('rear wall ledger',(x+(left+right)*.5,y+rear-.36,soffit-.235),
          (right-left-.70,.18,.32),'teal',.016)
    if name=='Armory':
        f.box('west wall girder',(x+left+.40,y+rear-3.65,soffit-.25),
              (.29,6.92,.49),'iron',.018)


def pallet(f,x,y,z,width,depth):
    # Cases rest on the boards; the two deep openings expose the fork pockets.
    for py in (-.38,0,.38):
        f.box('pallet cross bearer',(x,y+depth*py,z+.04),(width-.065,.145,.105),'wood',.012)
    count=max(4,round(width/.36))
    for i in range(count):
        px=x-width/2+width*(i+.5)/count
        f.box('pallet top board',(px,y,z+.122),(width/count-.028,depth,.085),'wood',.008)


def transit_bin(f,x,y,z,w,d,h,paint):
    f.box('stacking container',(x,y,z+h/2),(w,d,h),paint,.036)
    f.box('sealed lid',(x,y,z+h+.025),(w+.034,d+.024,.065),'iron',.014)
    f.box('label holder',(x-w*.22,y-d/2-.007,z+h*.59),(w*.26,.018,.077),'ivory',.004)
    for dx in (-.32,.32):
        f.box('recessed catch',(x+w*dx,y-d/2-.019,z+h-.035),(.065,.037,.10),'galv',.004)


def backplate(f,x,y,z,w,h,face):
    f.box('folded backing plate',(x,y,z),(w,.10,h),'iron',.024)
    f.box('inset panel',(x,y-.053,z),(w-.11,.034,h-.11),face,.016)


def equip_fitout(f,x,y,g):
    # The original single vest stays at its existing hook. The panel bridges
    # the gap to the concrete and allows the shoulder straps to read in silhouette.
    px=x+3.3
    backplate(f,px,y+3.15,g+2.31,2.18,2.40,'teal')
    for dx in (-.70,.70):
        f.box('mount rail',(px+dx,y+3.08,g+2.35),(.052,.053,1.98),'galv',.006)
    f.tube('hanger rail',(px-.79,y+3.01,g+3.03),(px+.79,y+3.01,g+3.03),.033,'galv')
    for side in (-1,1):
        f.beam('shoulder hanger',(px,y+3.015,g+3.03),(px+side*.31,y+3.015,g+2.84),
               .046,.046,'wood',.006)
    f.box('packing shelf',(px,y+2.83,g+.98),(2.34,.78,.12),'wood',.025)
    for dx in (-.80,.80):
        f.beam('shelf bracket',(px+dx,y+3.18,g+.53),(px+dx,y+2.50,g+.93),.07,.07,'iron')
    for j in range(2):
        transit_bin(f,px-.48+j*.94,y+2.86,g+1.04,.77,.46,.25,'olive' if j==0 else 'canvas')


def medical_fitout(f,x,y,g):
    for ob in f.group.objects:
        if ob.name.startswith('Medical bench top'):
            ob.material_slots[0].material=f.materials['ivory']
        elif ob.name.startswith('Medical bench leg'):
            ob.material_slots[0].material=f.materials['teal']
    px=x+3
    f.box('underbench shelf',(px,y+1.80,g+.64),(2.06,.83,.075),'teal',.013)
    for dx in (-.52,.42):
        transit_bin(f,px+dx,y+1.80,g+.678,.77,.60,.45,'ivory')
    # Wall-fixed first-aid cabinet above and left of the red transport kit.
    px=x+1.26
    f.box('wall cabinet',(px,y+3.0,g+2.40),(1.50,.58,1.34),'teal',.045)
    f.box('cabinet enamel door',(px,y+2.69,g+2.40),(1.39,.085,1.22),'ivory',.036)
    f.box('cabinet latch',(px+.49,y+2.64,g+2.42),(.07,.07,.23),'iron',.014)
    # A single extruded cross has no overlapping centre polygons.
    cross=[(-.075,.265),(.075,.265),(.075,.075),(.265,.075),(.265,-.075),(.075,-.075),
           (.075,-.265),(-.075,-.265),(-.075,-.075),(-.265,-.075),(-.265,.075),(-.075,.075)]
    mark=f.prism('cabinet cross',cross,0,.017,(px-.15,y+2.654,g+2.43),'teal',.003)
    mark.rotation_euler.x=math.pi/2


def armory_fitout(f,x,y,g):
    # Closed issue cabinet anchored against the surviving rear wall.
    cx=x+1.09
    f.box('issue cupboard',(cx,y+5.75,g+1.47),(1.74,.86,2.4),'teal',.045)
    for sign in (-1,1):
        dx=sign*.417
        f.box('cupboard leaf',(cx+dx,y+5.30,g+1.47),(.81,.073,2.26),'olive',.02)
        for j in range(3):
            f.box('stamped locker vent',(cx+dx,y+5.254,g+2.19+j*.10),(.49,.045,.041),'iron',.006)
        f.box('cupboard latch',(cx+sign*.112,y+5.245,g+1.49),(.055,.075,.22),'galv',.007)
    # Two protective skids seat the long transport case on the receiving floor.
    for dx in (-1.62,1.62):
        cx=x+.6+dx*math.cos(.10)
        cy=y-3.8+dx*math.sin(.10)
        f.box('transport skid',(cx,cy,g+.283),(.18,1.33,.058),'rubber',.009,.10)


def maker_plate(f,x,y,z,colour):
    f.box('enamel store marker',(x,y,z),(.62,.055,.63),colour,.043)
    if f.prefix=='Medical':
        f.box('marker horizontal',(x,y-.028,z),(.35,.023,.092),'ivory',.005)
        for sign in (-1,1):
            f.box('marker return',(x,y-.03,z+sign*.105),(.092,.02,.119),'ivory',.003)
    else:
        # Two simple inventory bars share the same plate design as the medical sign.
        for i in range(2):
            f.box('inventory mark',(x-.11+i*.22,y-.03,z),(.091,.022,.255),'ivory',.005)


def loading_apron(f,terrain,x,edge,floor_z,width,run,direction):
    toe=edge+direction*run
    vertices=[]
    for px in (-width/2,width/2):
        hit,point,_,_=terrain.ray_cast((x+px,toe,30),(0,0,-1))
        if not hit:
            raise ValueError('Loading apron extends beyond the compound terrain')
        low=point.z
        vertices.extend([(px,0,-.19),(px,toe-edge,low-floor_z-.060),
                         (px,toe-edge,low-floor_z+.008),(px,0,0)])
    faces=[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]
    f.mesh('north exit apron' if direction>0 else 'receiving apron',vertices,faces,
           (x,edge,floor_z),'apron',.005)


def recess_loading_bay(f,floor,x,front,width,depth):
    # Preserve the rounded slab perimeter. Only notch the middle of its front
    # edge so that the ramp descends inside the building footprint.
    cap=max((p for p in floor.data.polygons if p.normal.z>.9),key=lambda p:p.area)
    matrix=floor.matrix_world
    inverse=matrix.inverted()
    ring=[matrix@floor.data.vertices[i].co for i in cap.vertices]
    outline=[]
    found=False
    for a,b in zip(ring,ring[1:]+ring[:1]):
        outline.append((a.x,a.y))
        if max(abs(a.y-front),abs(b.y-front))<.006 and abs(a.x-b.x)>width:
            side=1 if a.x<b.x else -1
            near=x-side*width/2
            far=x+side*width/2
            outline.extend([(near,front),(near,front+depth),(far,front+depth),(far,front)])
            found=True
    if not found:
        raise ValueError('The depot floor has no straight loading edge')
    local=[inverse@Vector((px,py,ring[0].z)) for px,py in outline]
    bounds=[v.co.z for v in floor.data.vertices]
    former=floor.data
    replacement=f.prism('floor with recessed loading bay',[(p.x,p.y) for p in local],
                        min(bounds),max(bounds),(0,0,0),'shopfloor',0)
    floor.data=replacement.data
    bpy.data.objects.remove(replacement,do_unlink=True)
    if not former.users:
        bpy.data.meshes.remove(former)
    floor.data.set_sharp_from_angle(angle=math.radians(28))
    for face in floor.data.polygons:
        face.use_smooth=abs(face.normal.z)<.15


def build(scene,palette,Fabricator):
    specs=[('Equipment',-15.8,30.5,12,7,4.4,1.8),('Medical',9.3,30.5,11,7,4.4,1.8),
           ('Armory',-29.1,-7,10.8,13,5.6,0)]
    for name,x,y,w,d,h,g in specs:
        group=scene.objects[name+' floor'].users_collection[0]
        f=Fabricator(group,palette,name)
        retired=(name+' fractured slab aggregate',name+' jagged retained roof',
                 'Equipment crossed tool','Medical faded panel','Medical wall cross')
        crate_prefix={'Equipment':'Equipment case','Medical':'Medical supply case','Armory':'Ammunition crate'}[name]
        for ob in list(group.objects):
            if ob.name.startswith(retired):
                bpy.data.objects.remove(ob,do_unlink=True)
            elif ob.name.startswith(crate_prefix):
                ob.location.z+=.1445
            elif name!='Armory' and ob.name.startswith(name+' pier') and 'shoe' not in ob.name:
                if ob.location.x>x and ob.location.y<y-d*.40:
                    # Only the shortened end of the forecourt wall survives here.
                    inv=ob.matrix_world.inverted()
                    for vertex in ob.data.vertices:
                        q=ob.matrix_world@vertex.co
                        q.z=g+.8+(q.z-g-.8)*.26
                        vertex.co=inv@q
                    ob.data.update()
        canopy(f,name,x,y,w,d,h,g)
        terrain=scene.objects['Compound terrain with northern terrace']
        width=min(w*.56,6.3)
        front=y-d*.5+.325
        if name!='Armory':
            recess_loading_bay(f,scene.objects[name+' floor'],x,front,width,1.70)
            loading_apron(f,terrain,x,front+1.70,g+.28,width,1.88,-1)
        else:
            loading_apron(f,terrain,x,front,g+.28,width,2.1,-1)
        if name=='Armory':
            loading_apron(f,terrain,x+w*.5-1.78,y+d*.5-.325,g+.28,2.16,2.20,1)
        if name!='Armory':
            f.box('forecourt return',(x+w*.5,y-d*.5+.99,g+.65),(.46,1.97,1.34),'foot',.041)
            f.box('return coping',(x+w*.5,y-d*.5+1.14,g+1.34),(.52,1.61,.14),'cap',.031)
        maker_plate(f,x-w*.5,y-d*.5-.439,g+2.36,{'Equipment':'teal','Medical':'teal','Armory':'oxide'}[name])
        if name=='Equipment':
            pallet(f,x-.95,y+2.00,g+.28,4.10,1.38)
            equip_fitout(f,x,y,g)
        elif name=='Medical':
            pallet(f,x-1.87,y+1.95,g+.28,4.10,1.76)
            medical_fitout(f,x,y,g)
        else:
            pallet(f,x-1.77,y+2,g+.28,4.02,1.49)
            pallet(f,x-3.10,y-1.30,g+.28,2.0,1.36)
            armory_fitout(f,x,y,g)
