"""Two gloved hands modelled in pose, each with its own rigid animation pivot."""
from math import atan2, cos, pi, sin
from mathutils import Vector
from mesh_tools import oval
from glove_surface import panel, sew_glove
from exterior import CARRY_WRIST, PIVOTS, carry_point, mag_path


def sleeve(w,label,wrist,elbow,right):
    wrist,elbow=Vector(wrist),Vector(elbow)
    direction=(wrist-elbow).normalized()
    across=Vector((1,0,0))
    across=(across-direction*across.dot(direction)).normalized()
    vertical=direction.cross(across).normalized()
    if w.detailed:
        stations=[(-1.28,.079),(-1.22,.080),(-1.08,.077),(-.93,.075),(-.77,.077),
                  (-.65,.073),(-.52,.068),(-.39,.069),(-.26,.065),(-.13,.063),
                  (0,.059),(.045,.063),(.11,.063),(.20,.059),(.26,.061),
                  (.32,.059),(.39,.054),(.45,.058),(.51,.054),(.57,.049),(.62,.052),
                  (.68,.049),(.72,.044),(.76,.046),(.80,.041),(.86,.037),(.91,.032),(.97,.030)]
    else:
        stations=[(.82,.039),(.86,.039),(.90,.033),(.96,.029)]
    n=24 if w.detailed else 10
    rings=[]
    for i,(t,base) in enumerate(stations):
        c=elbow.lerp(wrist,t)
        c.z-=max(-t,0)**1.55*.032
        row=[]
        for j in range(n):
            angle=2*pi*j/n
            fold=1+.045*cos(3*angle+4*t+right)+.033*sin(5*angle-6*t)
            # An oblique seam and broad tension creases track the taper of the arm.
            axial=(.0045 if w.detailed else .0018)*sin(2*angle+t*4)*sin(t*pi)
            row.append(c+across*(base*cos(angle)*fold)+vertical*(base*.84*sin(angle)*fold)+direction*axial)
        rings.append(row)
    w.part(label+' | gathered twill sleeve').rings(rings,'cloth')
    stations=[(-.048,.038),(-.043,.039),(-.036,.036),(-.018,.033),(-.006,.030),(.002,.029)]
    rings=[[wrist+direction*dist+across*x+vertical*y for x,y in oval(r,r*.84,n,.89)] for dist,r in stations]
    w.part(label+' | knit cuff').rings(rings,'rubber',(.10,.76,.13,.82))
    for i,(dist,rad) in enumerate([(-.041,.039),(-.033,.0356),(-.019,.0329)]):
        if not w.detailed and i!=0:
            continue
        rings=[[wrist+direction*(dist+a)+across*x+vertical*y for x,y in oval(rad+b,(rad+b)*.84,n,.89)]
               for a,b in ((-.0018,-.0005),(-.001,.00045),(0,.0007),(.001,.00045),(.0018,-.0005))]
        w.part(label+' | knitted welt '+str(i)).rings(rings,'leather')
    outside=Vector((right,-.08,.22))
    angle=atan2(outside.dot(vertical),outside.dot(across))
    def cuff_point(dist,ang,offset):
        radius=.030+max(0,-dist-.006)*.258+offset
        return wrist+direction*dist+across*(radius*cos(ang))+vertical*(radius*.84*sin(ang))
    rings=[]
    levels=((-.040,.53),(-.038,.67),(-.034,.72),(-.015,.65),(-.012,.61),(-.010,.46))
    steps=10 if w.detailed else 5
    for distance,span in levels:
        vals=[angle-span+2*span*i/steps for i in range(steps+1)]
        rings.append([cuff_point(distance,a,.0028) for a in vals]+
                     [cuff_point(distance,a,-.0009) for a in reversed(vals)])
    w.part(label+' | angled cuff fastening').rings(rings,'cloth',(.24,.62,.15,.83))
    if w.detailed:
        for j,dist in enumerate((-.035,-.014)):
            for i in range(5):
                a=angle-.44+i*.18
                pts=[cuff_point(dist,a,.0030),cuff_point(dist,a+.095,.0031)]
                w.stroke(label+' | cuff stitches %d.%d'%(j,i),pts,[.00055,.00055],
                         'thread',6,guide=tuple(direction))
    if w.detailed:
        # A soft double seam down the outward face of each sleeve, with the
        # larger wrinkles carried by the underlying cloth silhouette.
        for line in (-1,1):
            pts=[]
            for t,r in stations_smooth():
                ang=(-.80 if right>0 else 2.45)+.1*t+line*.048
                c=elbow.lerp(wrist,t)
                pts.append(c+across*((r-.00065)*cos(ang))+vertical*((r-.00065)*.84*sin(ang)))
            w.stroke(label+' | sleeve seam '+str(line),pts,[.0014]*len(pts),'cloth',6,guide=tuple(vertical),flatten=.65)


def stations_smooth():
    return ((.12,.063),(.20,.059),(.26,.061),(.32,.059),(.39,.054),(.45,.058),
            (.51,.054),(.57,.049),(.62,.052),(.68,.049),(.72,.044),(.76,.046),(.80,.041))


CARRY_CENTRE=carry_point(0,0,0)
CARRY_BIND=Vector(PIVOTS['SupportCarry'])
CARRY_FINGERS=((.030,.009),(.011,.010),(-.008,.0104),(-.028,.0097))


def carry(w):
    w.group='SupportCarry'
    sleeve(w,'Carry',CARRY_BIND,(-.232,-.092,-.202),-1)
    start=len(w.parts)
    def posed(x,a,h):
        return carry_point(x,a,h)-CARRY_WRIST+CARRY_BIND
    f,h,ef,eh=mag_path(.43)
    down=(0,eh,-ef)
    forward=(0,ef,eh)
    station=[(-.061,-.066,.020),(-.055,-.054,.016),(-.050,-.041,.011),
             (-.041,-.029,.007),(-.037,-.011,.004),(-.035,.006,.001),
             (-.034,.016,0)]
    w.stroke('Carry | palm guide',[posed(*p) for p in station],
             [.024,.028,.031,.035,.038,.037,.028],'leather',
             20 if w.detailed else 10,guide=down,flatten=.49)
    pads=[]
    for i,(d,r) in enumerate(CARRY_FINGERS):
        pts=[(-.035,.008,d),(-.035,.022,d+.001),(-.033,.037,d+.0015),
             (-.028,.0465,d+.002),(-.017,.054,d+.0025),(-.002,.055,d+.002),
             (.010,.0515,d+.001)]
        points=[posed(*p) for p in pts]
        finger(w,'Carry digit '+str(i),points,r,down)
        pads.append((points,r))
    pts=[(-.053,-.039,-.008),(-.047,-.043,-.018),(-.040,-.0465,-.027),
         (-.032,-.051,-.030),(-.022,-.054,-.031),(-.009,-.0555,-.030),
         (.004,-.051,-.027)]
    points=[posed(*p) for p in pts]
    finger(w,'Carry thumb',points,.0119,down)
    pads.append((points,.0119))
    tree=sew_glove(w,start,'Carry')
    # Closed patches follow the same rounded end profile as the continuous glove.
    # Their raised edge reads as a sewn fingertip from both sides of the prop.
    for i,(points,r) in enumerate(pads):
        tip=points[-1]
        delta=tip-points[-2]
        direction=delta.normalized()
        dome=min(r*.83,delta.length*.82)
        tail=[tip-direction*dome*t for t in (1.46,1.30,.96,.59,.20,-.035)]
        w.stroke('Carry | sewn fingertip '+str(i),tail,
                 [r*t for t in (.63,.855,.895,.828,.575,.16)],'rubber',
                 12 if w.detailed else 8,guide=down,flatten=.89)
    outward=(-1,-.04,-.09)
    panel(w,tree,'Carry | dorsal backing',posed(-.044,-.025,.005),outward,
          forward,.019,.016,.0018,'cloth')
    for i,(d,r) in enumerate(CARRY_FINGERS):
        if w.detailed or i in (1,2):
            panel(w,tree,'Carry | knuckle guard '+str(i),posed(-.036,.007,d),outward,
                  forward,.008,.0045,.0024)
    panel(w,tree,'Carry | thumb welt',posed(-.041,-.046,-.024),(-.91,-.38,.06),
          down,.0075,.0045,.0015)


def finger(w,label,points,radius,guide):
    seg=12 if w.detailed else 7
    tip,previous=Vector(points[-1]),Vector(points[-2])
    toward=(tip-previous).normalized()
    dome=min(radius*.83,(tip-previous).length*.82)
    points=points[:-1]+[tip-toward*dome,tip-toward*dome*.59,tip-toward*dome*.20,tip]
    vals=[.80,1.,.94,.91,.90,.86,.84,.76,.51,.19]
    w.stroke(label+' | finger guide',points,[radius*t for t in vals],'leather',seg,guide=guide,flatten=.86)


def fit_ready_hand(point,right):
    # Fade the contact correction toward the fixed wrist so either sleeve keeps its bind.
    x,f,h=point
    if right:
        t=max(0.,min(1.,(f+.172)/.044))
        t=t*t*(3-2*t)
        return Vector((x*(1-.11*t),f+.016*t,h+(.006+.015*h)*t))
    t=max(0.,min(1.,(f-.151)/.041))
    t=t*t*(3-2*t)
    return Vector((x*(1-.225*t),f+.027*t,h+(.012-.32*h)*t))


def seat_ready_glove(parts,right):
    for part in parts:
        part.verts=[fit_ready_hand(p,right) for p in part.verts]


def supporting(w):
    w.group='SupportHand'
    sleeve(w,'Support',(-.086,.154,-.086),(-.232,-.092,-.202),-1)
    start=len(w.parts)
    path=[(-.087,.151,-.087),(-.083,.161,-.082),(-.072,.177,-.072),(-.061,.198,-.060),
          (-.047,.216,-.050),(-.035,.229,-.047),(-.024,.234,-.049)]
    w.stroke('Support | shaped palm',path,[.025,.028,.032,.039,.041,.036,.027],'leather',
             20 if w.detailed else 10,guide=(0,1,0),flatten=.56)
    for i,(f,r,extra) in enumerate(((.188,.0097,-.007),(.207,.0102,0),(.227,.0109,.006),(.248,.0104,.004))):
        points=[(-.042,f,-.050),(-.027,f+.004,-.055),(-.008,f+.005,-.057),
                (.014,f+.003,-.055),(.034+extra*.25,f+.001,-.047),(.0435+extra*.22,f-.002,-.034),
                (.0475+extra*.18,f-.005,-.020+extra*.60)]
        finger(w,'Support digit %d'%(i+1),points,r,(0,1,0))
    points=[(-.071,.187,-.061),(-.066,.203,-.047),(-.057,.218,-.030),(-.051,.232,-.017),
            (-.046,.248,-.008),(-.044,.264,-.004),(-.043,.279,-.006)]
    finger(w,'Support thumb',points,.013,(1,0,0))
    tree=sew_glove(w,start,'Support')
    outward=(-.98,-.08,-.14)
    panel(w,tree,'Support | dorsal suede reinforcement',(-.061,.199,-.061),outward,
          (.35,.88,.28),.026,.016,.0017,'cloth')
    for i,(f,hh) in enumerate(((.192,-.064),(.207,-.061),(.223,-.057))):
        if w.detailed or i==1:
            panel(w,tree,'Support | moulded protector '+str(i),(-.061,f,hh),outward,
                  (.25,.19,.92),.009,.0046,.0031)
    panel(w,tree,'Support | thumb knuckle shield',(-.055,.230,-.019),(-1,0,.14),
          (0,.8,.56),.010,.0061,.0021)
    if w.detailed:
        panel(w,tree,'Support | wrist binding',(-.078,.171,-.077),outward,
              (.30,.90,.31),.009,.011,.0018)
        for i,f in enumerate((.197,.212,.229,.250)):
            panel(w,tree,'Support | finger leather '+str(i),(.025,f,-.044),(.5,0,-.86),
                  (.82,0,.55),.006,.005,.0014,'leather')
    seat_ready_glove(w.parts[start:],False)


def firing(w):
    w.group='FiringHand'
    sleeve(w,'Firing',(.064,-.172,-.132),(.155,-.420,-.215),1)
    start=len(w.parts)
    path=[(.065,-.174,-.133),(.061,-.164,-.126),(.056,-.148,-.119),(.048,-.134,-.111),
          (.043,-.119,-.105),(.039,-.105,-.103),(.032,-.095,-.104)]
    w.stroke('Firing | shaped palm',path,[.024,.027,.030,.034,.038,.034,.024],'leather',
             20 if w.detailed else 10,guide=(0,0,1),flatten=.56)
    for i,(h,r) in enumerate(((-.139,.0091),(-.121,.0104),(-.101,.0109))):
        off=(2-i)*.007
        points=[(.042,-.120-off,h),(.042,-.105-off,h+.002),(.036,-.089-off,h+.003),
                (.024,-.077-off,h+.003),(.008,-.073-off,h+.001),(-.006,-.078-off,h-.002),
                (-.011,-.085-off,h-.004)]
        finger(w,'Firing digit %d'%(i+1),points,r,(0,0,1))
    points=[(.044,-.120,-.084),(.045,-.102,-.074),(.045,-.086,-.062),(.042,-.070,-.051),
            (.040,-.054,-.044),(.038,-.040,-.041),(.037,-.032,-.041)]
    finger(w,'Firing index',points,.0098,(0,0,1))
    points=[(.044,-.128,-.097),(.034,-.131,-.092),(.023,-.134,-.089),(.010,-.137,-.086),
            (-.005,-.135,-.088),(-.014,-.128,-.091),(-.020,-.117,-.093)]
    finger(w,'Firing thumb',points,.0123,(0,1,0))
    tree=sew_glove(w,start,'Firing')
    outward=(.91,-.17,.36)
    panel(w,tree,'Firing | dorsal suede reinforcement',(.053,-.142,-.108),outward,
          (-.22,.96,.12),.0165,.017,.0017,'cloth')
    for i,h in enumerate((-.131,-.116,-.100,-.086)):
        if w.detailed or i in (1,2):
            panel(w,tree,'Firing | moulded protector '+str(i),(.049,-.112,h),outward,
                  (0,1,0),.008,.0048,.0026)
    if w.detailed:
        panel(w,tree,'Firing | index shield',(.043,-.078,-.058),(1,0,.25),(0,.8,.6),
              .011,.0058,.0016)
    seat_ready_glove(w.parts[start:],True)
