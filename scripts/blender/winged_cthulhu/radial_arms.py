"""Eight genuine spatial arm chains, inferred from the approved front concept.

Angles share the existing game's four attack lanes and four posterior supports.
The ventral frame follows the curve by parallel transport, not a camera vector.
"""
import math
import numpy as np
from mathutils import Vector
from .geometry import Mesh, spline

ANGLES={'A01':-65,'A02':-24,'A03':24,'A04':65,
        'S01':-115,'S02':-158,'S03':158,'S04':115}


def control_points(key):
    angle=ANGLES[key];sign=1 if angle>0 else -1
    a=math.radians(angle);out=Vector((math.sin(a),-math.cos(a),0));side=Vector((math.cos(a),math.sin(a),0))
    front=abs(angle)<40; lateral=40<abs(angle)<100; rear=abs(angle)>140
    # Radius, tangential sweep, height, cross-section radius in metres.
    # The front coils flare sideways; posterior pairs retain genuine +Y depth.
    reach=1.0
    # Posterior coils open away from the sagittal plane instead of converging.
    spread=-1.0 if rear else 1.0
    if front:
        knots=[(.8,0,4.55,.75),(1.7,0,4.35,.82),(2.8,.35,3.9,.87),
               (3.8,1.6,3.9,.94),(4.15,2.75,3.25,.8),(4.4,3.15,2.15,.61),
               (4.65,2.8,1.22,.43),(4.7,1.95,1.0,.29),(4.6,1.4,1.5,.2),
               (4.4,1.5,2.05,.12),(4.2,1.95,2.15,.055),(4.1,2.2,1.94,.008)]
    elif lateral:
        knots=[(.8,0,4.55,.68),(1.7,0,4.45,.74),(2.9,.1,4.7,.75),
               (4.3,.2,5.25,.69),(5.5,.45,5.1,.59),(6.35,.8,4.2,.46),
               (6.4,1.1,3.15,.34),(5.85,1.4,2.8,.24),(5.2,1.3,3.25,.17),
               (5.1,.9,3.85,.1),(5.45,.65,4.03,.045),(5.7,.55,3.8,.008)]
    elif rear:
        knots=[(.8,0,4.55,.66),(1.7,0,4.25,.75),(2.8,.15,3.7,.77),
               (4,.5,3.6,.73),(5.0,1.1,3.2,.62),(5.55,1.7,2.3,.47),
               (5.35,2.0,1.35,.34),(4.55,1.9,1.05,.24),(3.95,1.45,1.45,.16),
               (3.9,.95,2.04,.105),(4.2,.8,2.2,.05),(4.5,.9,2.0,.008)]
    else:
        knots=[(.8,0,4.55,.64),(1.7,0,4.4,.7),(2.8,.1,4.25,.68),
               (4.0,.45,4.1,.6),(5.25,1.1,4.25,.49),(5.9,1.8,4.8,.37),
               (5.7,2.55,5.2,.26),(5.0,2.8,4.95,.18),(4.55,2.4,4.4,.12),
               (4.75,1.95,4.15,.065),(5.1,1.9,4.35,.008)]
    proximal=(.65,.65,.73,.86,.95)
    result=[]
    for i,(r,s,z,radius) in enumerate(knots):
        p=out*(r*(reach if i>1 else 1))+side*(s*spread*sign)
        # Deliberate mild left/right asymmetry; roots still use the contract angle.
        p.z=z+(0.14*math.sin(i*.8) if sign>0 and i>2 else 0)
        result.append((p,radius*(proximal[i] if i<len(proximal) else 1)))
    return result


def tube_with_frames(g,controls,sides,steps,twist=0,ventral_out=None):
    points=spline([c[0] for c in controls],steps)
    radii=spline([c[1] for c in controls],steps)
    tangents=[(points[min(i+1,len(points)-1)]-points[max(i-1,0)]).normalized() for i in range(len(points))]
    normal=Vector((0,0,-1));normal=(normal-tangents[0]*normal.dot(tangents[0])).normalized()
    rings=[];frames=[];params=[]
    for j,(p,r,t) in enumerate(zip(points,radii,tangents)):
        if j:normal=tangents[j-1].rotation_difference(t)@normal
        normal=(normal-t*normal.dot(t)).normalized();binormal=t.cross(normal).normalized()
        f=j/(len(points)-1)
        rotation=twist*math.sin(math.pi*.8*f)
        if ventral_out is not None:
            target=ventral_out-t*ventral_out.dot(t)
            if target.length>.05:
                target.normalize()
                outward_angle=math.atan2(binormal.dot(target),normal.dot(target))
                w=min(1,max(0,(f-.12)/.3));w=w*w*(3-2*w)
                rotation=outward_angle*w
        n=normal*math.cos(rotation)+binormal*math.sin(rotation)
        b=t.cross(n).normalized()
        r=max(.006,r);frames.append((p,t,n,b,r))
        ring=[]
        for k in range(sides):
            a=math.tau*k/sides
            muscle=1+.012*math.sin(a*5+j*.1)
            ring.append(g.vert(p+r*muscle*(n*math.cos(a)+b*math.sin(a)),(k/sides,j/(len(points)-1))))
            params.append(j/(len(points)-1))
        rings.append(ring)
    for aa,bb in zip(rings,rings[1:]):
        for k in range(sides):
            mi=1 if math.cos(math.tau*(k+.5)/sides)>.64 else 0
            g.face((aa[k],bb[k],bb[(k+1)%sides],aa[(k+1)%sides]),mi)
    for ring,p,rev,t in [(rings[0],points[0],True,0),(rings[-1],points[-1],False,1)]:
        ix=g.vert(p);params.append(t)
        for k in range(sides):
            f=(ix,ring[k],ring[(k+1)%sides]);g.face(f[::-1] if rev else f)
    # Staggered twin rows. Each cup inherits its parent tissue's local frame.
    walked=0;next_at=2.05
    for j,(p,t,n,b,r) in enumerate(frames):
        if j:walked+=(p-frames[j-1][0]).length
        if walked<next_at or r<.035 or j>len(frames)-3:continue
        for row in (-1,1):
            jj=min(j+(1 if row>0 else 0),len(frames)-1)
            pp,tt,nn,bb,rr=frames[jj]
            cup_normal=(nn*.935+bb*(row*.355)).normalized()
            before=len(g.v)
            g.cup(pp+cup_normal*rr*1.006,cup_normal,tt,rr*.34,aspect=.92,segments=24 if sides>=32 else 12)
            params.extend([jj/(len(frames)-1)]*(len(g.v)-before))
        next_at=walked+max(.07,r*.68)
    return frames,params


def create(key,col,mats,sides=40,steps=14):
    g=Mesh();controls=control_points(key)
    sign=1 if ANGLES[key]>0 else -1
    a=math.radians(ANGLES[key]);outward=Vector((math.sin(a),-math.cos(a),0))
    frames,params=tube_with_frames(g,controls,sides,steps,ventral_out=outward)
    ob=g.finish('CR_WingedCthulhu_Arm_'+key,col,mats)
    # Mesh.finish recalculates normals via bmesh, retaining vertex order.
    a=ob.data.attributes.new('ArmParameter','FLOAT','POINT')
    a.data.foreach_set('value',np.asarray(params,np.float32))
    ob['limb_id']=key;ob['azimuth_degrees']=ANGLES[key]
    ob['ventral_frame']='Parallel transport, starts downward; two staggered sucker rows'
    ob['centerline']=[list(p) for p,_,_,_,_ in frames]
    ob['centerline_radii']=[float(r) for _,_,_,_,r in frames]
    ob['control_points']=[list(p)+[r] for p,r in controls]
    return ob
