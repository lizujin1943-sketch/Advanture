"""Authored phase and limb clips. Times match existing 30 Hz combat events."""
import math
from mathutils import Vector,Matrix,Quaternion
from .rig import KEYS,count,name,resample
from .radial_arms import ANGLES
from .geometry import spline
from abyssal_actions import registry,ease,blend,pulse

def init(rig):
    ref={k:[Vector(p) for p in rig['arm_reference'][k]] for k in KEYS}
    oral={int(k):[Vector(p) for p in pts] for k,pts in rig['oral_reference'].items()}
    return ref,oral

def arm_pose(rig,key,points,reference):
    for i,(a,b) in enumerate(zip(points,points[1:])):
        pb=rig.pose.bones[name(key,i)];base=pb.bone.matrix_local
        delta=b-a;old=reference[i+1]-reference[i]
        q=old.normalized().rotation_difference(delta.normalized())@base.to_quaternion()
        desired=Matrix.LocRotScale(a,q,Vector((1,delta.length/old.length,1)))
        pb.matrix_basis=base.inverted()@desired

def path(key,knots):
    a=math.radians(ANGLES[key]);out=Vector((math.sin(a),-math.cos(a),0));side=Vector((math.cos(a),math.sin(a),0))
    pts=[out*r+side*s+Vector((0,0,z)) for r,z,s in knots]
    return resample(spline(pts,20),count(key)+1)[0]

def targets(key):
    windup=path(key,[(.8,4.55,0),(2.6,5.2,0),(5,7,0),(9,10,0),(13,13,.1),(17,13.1,.2),(19.8,10,0)])
    impact=path(key,[(.8,4.55,0),(2.5,3.6,0),(5,1.8,0),(9,1.1,0),(15,1,0),(19,.8,0),(21.4,.55,0)])
    sweep=path(key,[(.8,4.55,0),(3,3,0),(6,1.8,0),(12,1.2,.6),(17,1,2),(20.5,.9,1),(19,1,-1.5),(17.3,1.4,-1)])
    return windup,impact,sweep

def turn(points,angle):
    q=Quaternion((0,0,1),angle);r=points[0];return [r+q@(p-r) for p in points]

def attack(key,kind,t,reference):
    windup,impact,sw=targets(key)
    if kind=='Slam':
        if t<1.2:return blend(reference,windup,ease(t/1.2))
        if t<1.5:return blend(windup,impact,((t-1.2)/.3)**2)
        if t<1.8:return impact
        return blend(impact,reference,ease((t-1.8)/1.2))
    if t<1.2:return blend(reference,turn(sw,-.35),ease(t/1.2))
    if t<2:return turn(sw,-.35+.7*ease((t-1.2)/.8))
    return blend(turn(sw,.35),reference,ease((t-2)/1.6))

def owned(rig,spec):
    if spec['kind'] in ('Slam','Sweep','Sever'):return [name(spec['limb'],i) for i in range(count(spec['limb']))]
    if spec['kind'] in ('Ink','Hit'):return [b.name for b in rig.pose.bones if b.name=='DEF_Head' or b.name.startswith(('DEF_Mouth','DEF_Eye'))]
    return [b.name for b in rig.pose.bones]

def apply(rig,kind,t,key,ref,oral):
    for pb in rig.pose.bones:pb.matrix_basis=Matrix.Identity(4)
    if kind in ('Slam','Sweep','Sever'):
        if kind=='Sever':
            pts=[p+Vector((0,0,.3*ease(t/.3)*math.sin(math.pi*i/(len(ref[key])-1)))) for i,p in enumerate(ref[key])]
        else:pts=attack(key,kind,t,ref[key])
        arm_pose(rig,key,pts,ref[key]);return
    duration=5 if kind=='EnragedIdle' else 6
    phase=math.tau*t/duration;power=1.65 if kind=='EnragedIdle' else 1
    envelope=1 if kind in ('Idle','EnragedIdle') else math.sin(math.pi*min(1,t/({'Emerge':4,'Ink':3.2,'Hit':1,'Enrage':3,'Death':6,'Combination':3.6}[kind])))
    # Overlap is periodic and computed directly from time, so random-access sampling is stable.
    h=rig.pose.bones['DEF_Head'];h.rotation_quaternion=Quaternion((1,0,0),.018*math.sin(phase)*power)@Quaternion((0,1,0),.022*math.sin(phase)*power)
    h.scale=(1+.008*math.sin(phase)*power,1,1+.008*math.sin(phase)*power)
    for k in KEYS:
        idx=KEYS.index(k);pts=[]
        for i,p in enumerate(ref[k]):
            f=i/(len(ref[k])-1);v=Vector((math.sin(phase+f*2+idx)-math.sin(f*2+idx),math.sin(phase+idx*.8)-math.sin(idx*.8),math.sin(phase+idx*.5+f*3)-math.sin(idx*.5+f*3)))
            pts.append(p+v*(.12*f*power))
        if kind=='Emerge':pts=[p+Vector((0,0,-1.1*(1-ease(t/4))*i/(len(pts)-1))) for i,p in enumerate(ref[k])]
        elif kind=='Death':pts=blend(ref[k],[Vector((p.x,p.y,p.z*(1-.7*i/(len(ref[k])-1)))) for i,p in enumerate(ref[k])],ease(t/2.5))
        elif kind=='Enrage':pts=[p+Vector((0,0,.7*envelope*i/(len(pts)-1))) for i,p in enumerate(pts)]
        arm_pose(rig,k,pts,ref[k])
    # Wings have shoulder motion and a delayed distal membrane response.
    for side,sign in [('L',-1),('R',1)]:
        angle=.055*math.sin(phase)*power
        tip=.035*(math.sin(phase-.65)-math.sin(-.65))*power
        if kind=='Emerge':angle+=.5*(1-ease(t/4))
        if kind=='Enrage':angle-=.25*envelope
        if kind=='Death':angle+=.75*ease(t/3);tip+=.25*ease(t/3)
        rig.pose.bones['DEF_Wing_'+side].rotation_quaternion=Quaternion((0,1,0),angle*sign)
        rig.pose.bones['DEF_WingTip_'+side].rotation_quaternion=Quaternion((0,1,0),tip*sign)
    for idx,pts in oral.items():
        moved=[]
        for i,p in enumerate(pts):
            f=i/(len(pts)-1);offset=.10*power*f*(math.sin(phase-2*f+idx*.6)-math.sin(-2*f+idx*.6))
            opening=.38*f*ease(t/.95)*(1-ease((t-1.9)/1.3)) if kind in ('Ink','Combination') else 0
            moved.append(p+Vector((offset+(p.x-1.25)*opening,offset*.4,-.04*math.sin(phase)*f)))
        for i,(a,b) in enumerate(zip(moved,moved[1:])):
            pb=rig.pose.bones[f'DEF_Mouth_{idx:02d}_{i:02d}'];base=pb.bone.matrix_local
            old=pts[i+1]-pts[i];delta=b-a;q=old.normalized().rotation_difference(delta.normalized())@base.to_quaternion()
            pb.matrix_basis=base.inverted()@Matrix.LocRotScale(a,q,Vector((1,delta.length/old.length,1)))
    blink=pulse(t,duration*.42,.17) if kind in ('Idle','EnragedIdle') else 0
    if kind=='Emerge':
        root=rig.pose.bones['ROOT'];root.location=root.bone.matrix_local.to_3x3().inverted()@Vector((0,0,-13*(1-ease(t/4))))
    elif kind in ('Ink','Combination'):
        opening=ease(t/.95)*(1-ease((t-1.9)/1.3));h.rotation_quaternion=Quaternion((1,0,0),-.055*opening+.065*pulse(t,1.45,.4));h.scale=(1,1,1)
    elif kind=='Hit':h.rotation_quaternion=Quaternion((1,0,0),-.055*pulse(t,.18,.18));blink=pulse(t,.18,.18)*.7
    elif kind=='Enrage':h.rotation_quaternion=Quaternion((1,0,0),-.055*envelope)@Quaternion((0,0,1),.018*math.sin(t*18)*envelope)
    elif kind=='Death':
        root=rig.pose.bones['ROOT'];root.location=root.bone.matrix_local.to_3x3().inverted()@Vector((0,0,-14*ease((t-2.3)/3.7)))
        h.rotation_quaternion=Quaternion((1,0,0),.22*ease(t/2.5));blink=ease(t/1.7)
    rig.pose.bones['DEF_Eye_Center'].scale=(1,max(.04,1-.96*blink),1)
    if kind=='Combination':
        arm_pose(rig,'A01',attack('A01','Slam',min(3,t),ref['A01']),ref['A01'])
        arm_pose(rig,'A03',attack('A03','Sweep',t,ref['A03']),ref['A03'])
