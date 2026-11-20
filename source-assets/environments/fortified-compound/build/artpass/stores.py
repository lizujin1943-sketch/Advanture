"""Supply-room fit-out. Transit cases use the locations and sizes of the initial stock."""
import math
import re

import bpy


def chamfered(width,depth,inset):
    x,y=width/2,depth/2
    return [(-x+inset,-y),(x-inset,-y),(x,-y+inset),(x,y-inset),
            (x-inset,y),(-x+inset,y),(-x,y-inset),(-x,-y+inset)]


def transit_case(f,centre,size,angle,medical,long):
    x,y,z=centre
    width,depth,height=size
    cos,sin=math.cos(angle),math.sin(angle)
    origin=(x,y,z-height/2)

    def position(lx,ly,lz):
        return (x+cos*lx-sin*ly,y+sin*lx+cos*ly,origin[2]+lz)

    def block(name,lx,ly,lz,sizes,material,bevel):
        return f.box(name,position(lx,ly,lz),sizes,material,bevel,angle)

    def shell(name,extra,z0,z1,material,bevel):
        plan=chamfered(width+extra,depth+extra,.115)
        ob=f.prism(name,plan,z0,z1,origin,material,bevel)
        ob.rotation_euler.z=angle

    colour='teal' if medical else 'olive'
    lid_colour='ivory' if medical else 'casecap'
    lid_top=height if long else height-.029
    shell('transport shell',0,.067,height-.192,colour,.035)
    shell('impact boot',.043,0,.095,'casecap',.018)
    shell('compression seal',.035,height-.204,height-.146,'rubber',.009)
    shell('reinforced lid',.057,height-.157,lid_top,lid_colour,.023)

    if not long:
        for side in (-1,1):
            block('lid stacking rib',side*width*.32,0,height-.023,
                  (.128,depth-.22,.046),lid_colour,.010)
    # The hinged grip returns to its shallow front pocket, below the two catches.
    front=-depth*.5
    grip_z=height*.52
    block('handle well',0,front-.007,grip_z,(.39,.034,.205),'casecap',.007)
    for side in (-1,1):
        block('handle hinge',side*.129,front-.029,grip_z+.043,
              (.057,.083,.069),'galv',.010)
        f.beam('folding handle arm',position(side*.13,front-.048,grip_z+.048),
               position(side*.13,front-.094,grip_z-.047),.034,.035,'iron',.005)
    f.tube('folding grip',position(-.142,front-.094,grip_z-.047),
           position(.142,front-.094,grip_z-.047),.027,'rubber')
    for side in (-1,1):
        px=side*width*.31
        block('catch backing',px,front-.024,height-.244,(.105,.069,.276),'iron',.013)
        block('cam latch',px,front-.065,height-.208,(.078,.032,.137),'galv',.007)
        for face in (-1,1):
            block('moulded vertical bead',side*(width*.5-.185),face*(depth*.5+.002),
                  (height-.18)*.5,(.091,.041,height-.34),colour,.008)
    # Small paper inventory card in a closed side sleeve.
    block('stock-card sleeve',width*.5+.002,depth*.12,height*.53,
          (.036,.342,.161),'casecap',.007)
    block('stock card',width*.5+.018,depth*.12,height*.53,
          (.012,.284,.106),'ivory',.002)


def replace_cases(f,stem):
    body=re.compile(re.escape(stem)+r'(?:\.\d+)?$')
    dimensions=[]
    for obj in list(f.group.objects):
        if body.fullmatch(obj.name):
            vs=[v.co for v in obj.data.vertices]
            size=tuple(max(v[i] for v in vs)-min(v[i] for v in vs) for i in range(3))
            dimensions.append((tuple(obj.location),size,obj.rotation_euler.z))
        elif not obj.name.startswith(stem+' '):
            continue
        bpy.data.objects.remove(obj,do_unlink=True)
    for centre,size,angle in dimensions:
        w,d,h=size
        x,y,zc=centre
        bottom=zc-h/2
        if stem=='Ammunition crate':
            for layer in range(2):
                transit_case(f,(x,y,bottom+h*(.25+layer*.5)),(w,d,h/2),angle,False,False)
        elif f.prefix=='Medical':
            height=h*.72
            transit_case(f,(x,y,bottom+height*.5),(w,d,height),angle,True,False)
        else:
            transit_case(f,centre,size,angle,False,stem.startswith('Long'))


def outfit(f):
    original=next(o for o in f.group.objects if o.name=='Hanging armor vest')
    x,y,z=original.location
    front=y-.08
    for ob in list(f.group.objects):
        if ob.name.startswith('Vest pouch'):
            bpy.data.objects.remove(ob,do_unlink=True)
    for side in (-1,1):
        f.beam('shoulder web', (x+side*.21,front-.007,z+1.22),
               (x+side*.30,front-.007,z+.73),.092,.032,'webbing',.007)
    for k in range(3):
        f.box('stitched web strip',(x,front-.002,z+.49+.102*k),
              (.78-k*.03,.030,.034),'webbing',.005)
    for side in (-1,1):
        px=x+side*.215
        flap=[(-.158,.047),(.158,.047),(.154,-.02),(.103,-.070),(-.115,-.070),(-.158,-.026)]
        f.box('canvas pocket',(px,front-.046,z+.29),(.308,.128,.241),'canvas',.025)
        cover=f.prism('folded pocket flap',flap,0,.034,(px,front-.102,z+.37),'webbing',.007)
        cover.rotation_euler.x=math.pi/2
    f.box('identification tab',(x+.16,front-.024,z+.832),(.164,.018,.054),'canvas',.003)


def medical_kit(f):
    kit=next(o for o in f.group.objects if o.name=='Single first aid case')
    x,y,z=kit.location
    for side in (-1,1):
        f.box('kit handle mount',(x+side*.17,y,z+.453),(.105,.128,.042),'iron',.008)
        f.beam('kit handle side',(x+side*.18,y,z+.455),(x+side*.16,y,z+.531),
               .055,.074,'rubber',.008)
    f.box('kit carry handle',(x,y,z+.535),(.35,.075,.054),'rubber',.009)

    # Sealed, lightweight consumables belong on the shallow wall shelf.
    sx,sy,sz=6.96,33.37,3.96
    for dx in (-1.30,1.30):
        f.box('medical shelf standard',(sx+dx,sy+.29,sz),(.10,.10,1.36),'teal',.013)
        for high in (0,.65):
            f.beam('medical shelf angle',(sx+dx,sy+.22,sz+high-.27),
                   (sx+dx,sy-.24,sz+high-.035),.059,.065,'galv',.008)
    for j in range(2):
        level=sz+j*.65
        f.box('folded medical shelf',(sx,sy,level),(3.04,.64,.076),'ivory',.013)
        for i,(w,h) in enumerate(((.71,.36),(.63,.31),(.65,.37)) if j==0 else ((.82,.31),(.70,.27))):
            dx=-.99+i*.82
            f.box('sealed dressing carton',(sx+dx,sy,level+.038+h/2),
                  (w,.48,h),'ivory' if (i+j)%3!=0 else 'wood',.016)
            f.box('carton paper seal',(sx+dx-.13,sy,level+.04+h),
                  (.082,.45,.012),'teal' if j==0 else 'canvas',.002)


def armory_display(f):
    for ob in list(f.group.objects):
        if ob.name=='Single rifle receiver' or ob.name.startswith('Rifle '):
            bpy.data.objects.remove(ob,do_unlink=True)
    # A single solid display silhouette lies on its broad side on the case lid.
    outline=[(-.97,-.117),(-.82,-.108),(-.53,-.048),(-.33,-.047),
             (-.38,-.24),(-.28,-.262),(-.185,-.089),(-.04,-.105),(.032,-.301),
             (.172,-.292),(.102,-.10),(.488,-.09),(.53,-.043),(.961,-.043),
             (.978,.018),(.505,.025),(.49,.106),(.147,.115),(.02,.156),
             (-.19,.145),(-.264,.108),(-.406,.105),(-.546,.072),(-.933,.108)]
    origin=(-28.54,-10.79,1.148)
    prop=f.prism('inert display prop',outline,0,.104,origin,'iron',.009)
    prop.rotation_euler.z=-.135
    prop['single_display']='armory'
    for name,profile in [
        ('stock wear panel',[(-.91,-.073),(-.576,-.013),(-.44,.046),(-.571,.052),(-.908,.074)]),
        ('forward wear panel',[(.141,-.054),(.432,-.055),(.472,-.014),(.460,.068),(.145,.078)])]:
        face=f.prism(name,profile,.101,.120,origin,'casecap',.004)
        face.rotation_euler.z=prop.rotation_euler.z


def build(scene,palette,Fabricator):
    for name,stems in [('Equipment',['Equipment case']),('Medical',['Medical supply case']),
                       ('Armory',['Ammunition crate','Long weapon transport case'])]:
        f=Fabricator(scene.objects[name+' floor'].users_collection[0],palette,name)
        for stem in stems:
            replace_cases(f,stem)
        if name=='Medical':
            medical_kit(f)
        elif name=='Equipment':
            outfit(f)
        elif name=='Armory':
            armory_display(f)
