"""One spatial skin field on both sides of each detachable seam."""
import math
from mathutils import Vector
from .radial_arms import ANGLES

def smooth(x):
    t=max(0.,min(1.,x));return t*t*(3-2*t)

def field(paths,count,name,interval):
    axes={k:math.radians(a) for k,a in ANGLES.items()}
    def tissue(p):
        rad=math.hypot(p.x,p.y);a=math.atan2(p.x,-p.y)
        # Distal skin contracts with its ray; the saddle between two arms
        # shares tension and the throat remains tied to the body.
        w=smooth((rad-.73)/1.18)*smooth((5.62-p.z)/.58)
        mixes=sorted(((max(0,math.cos(a-v))**44,k) for k,v in axes.items()),reverse=True)[:2]
        total=sum(v for v,k in mixes);out={'DEF_Body':1-w}
        for v,k in mixes:
            if v/total<.001:continue
            pts=paths[k]
            def sample(i):
                d=pts[i+1]-pts[i];u=max(0,min(1,(p-pts[i]).dot(d)/d.length_squared))
                return (p-pts[i]-d*u).length_squared,(i+u)/count(k)
            _,f=min(sample(i) for i in range(min(7,len(pts)-1)))
            for bone,x in interval(f,count(k),lambda i:name(k,i)).items():
                out[bone]=out.get(bone,0)+x*w*v/total
        return out
    return tissue
