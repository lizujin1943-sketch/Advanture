"""Connected eight-way mantle fork. Anatomical skin runs down into the arms.
Only the distal sleeve disappears on sever; the fork belongs to the mantle.
"""
import math
from mathutils import Vector
from .geometry import spline
from .radial_arms import ANGLES, control_points

SIDES=40
IDS=sorted(ANGLES,key=ANGLES.get)
TAU=math.tau

def sector_angles(i):
    a=math.radians(ANGLES[IDS[i]])
    left=math.radians(ANGLES[IDS[i-1]])
    right=math.radians(ANGLES[IDS[(i+1)%8]])
    return a, a-((a-left)%TAU)/2, a+((right-a)%TAU)/2

def direction(a):
    return Vector((math.sin(a),-math.cos(a),0))

def trunk_angles():
    for i in range(8):
        _,lo,hi=sector_angles(i)
        for j in range(SIDES//2):
            yield lo+(hi-lo)*j/(SIDES//2)

def seat(key):
    """Frame for the closed terminal part of the retained cuff."""
    cp=control_points(key)
    pts=spline([p for p,_ in cp],14);rs=spline([r for _,r in cp],14)
    j=21
    t=(pts[j+1]-pts[j-1]).normalized()
    down=Vector((0,0,-1));n=(down-t*down.dot(t)).normalized()
    return pts[j],t,n,t.cross(n).normalized(),rs[j]

def fork(g,upper):
    # Each sector shares its dorsal edge with the trunk and its two ventral
    # edges with its neighbours. All eight meet only at the central belly pole.
    quarter=SIDES//4;half=2*quarter
    hub=g.vert((0,0,3.91))
    borders=[]
    for i in range(8):
        rim=Vector(g.v[upper[i*half]])
        edge=[hub]
        for j in range(1,quarter):
            t=j/quarter
            p=direction(sector_angles(i)[1])*(1.50*math.sin(t*math.pi*.5))
            p.z=3.91+.31*t*t
            # A scalloped gusset, highest at the head and hollow between rays.
            p=p.lerp(rim,t**3)
            edge.append(g.vert(p))
        borders.append(edge+[upper[i*half]])
    for i,key in enumerate(IDS):
        rim=[upper[(i*half+j)%len(upper)] for j in range(half+1)]
        lo=borders[i];hi=borders[(i+1)%8]
        ring=hi[:quarter]+rim[::-1]+lo[quarter-1:0:-1]
        assert len(ring)==SIDES
        base=[Vector(g.v[v]) for v in ring]
        p,t,n,b,r=seat(key);out=direction(sector_angles(i)[0])
        for step in range(1,19):
            u=step/18;prev=ring;ring=[]
            for k,start in enumerate(base):
                a=TAU*k/SIDES
                radial=n*math.cos(a)+b*math.sin(a)
                end=p+radial*(r+.051)
                # Cubic endpoint directions: descend from mantle, travel
                # across the crotch, then turn parallel to the distal arm.
                top=max(0,-math.cos(a))
                if quarter<=k<=3*quarter:
                    d=Vector((0,-.12,-.70))-direction(math.atan2(start.x,-(start.y-.16)))*.21
                else:
                    q=abs(math.sin(a))
                    edge=direction(sector_angles(i)[2 if math.sin(a)>0 else 1])
                    inward=(out-edge*out.dot(edge)).normalized() if q>1e-5 else out
                    d=(out*(1-q)+inward*q)*(.55*(1-q*q))
                    d+=(-edge*.21+Vector((0,-.12,-.70)))*q**8
                # Dorsal tendons run all the way from the mantle into each ray.
                loc=(1-u)**3*start+3*(1-u)**2*u*(start+d)+3*(1-u)*u*u*(end-t*.50)+u**3*end
                loc.z+=.11*top**4*math.sin(math.pi*u)**2
                ring.append(g.vert(loc))
            for k in range(SIDES):g.face((prev[k],prev[(k+1)%SIDES],ring[(k+1)%SIDES],ring[k]),1)
        # Round the joint inward, under the detachable skin. A severed
        # sleeve is closed and carries the matching wound on its end face.
        for advance,scale in ((.10,1.015),(.18,.935),(.21,.75),(.22,.40)):
            prev=ring;ring=[g.vert(p+t*advance+(n*math.cos(TAU*k/SIDES)+b*math.sin(TAU*k/SIDES))*r*scale) for k in range(SIDES)]
            for k in range(SIDES):g.face((prev[k],prev[(k+1)%SIDES],ring[(k+1)%SIDES],ring[k]),1)
        cap=g.vert(p+t*.223)
        for k in range(SIDES):g.face((ring[k],ring[(k+1)%SIDES],cap),1)
