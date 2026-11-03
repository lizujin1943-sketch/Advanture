"""精细软体口器：连续口周、带褶皱的深腔和不等长内收齿瓣。"""
import math
from mathutils import Vector
from abyssal_common import MeshBuilder

def outline(a):
    width=1-.12*math.sin(a)+.035*math.sin(3*a+.4)
    return Vector((.98*width*math.cos(a),0,1.10*math.sin(a)))

def create_mouth(collection,mats,center,front_surface):
    result=[]

    for k in range(12):
        # 长短、弯曲与位置稍有不对称；齿瓣根部圆厚，末端转入喉腔。
        a=math.tau*k/12+.055*math.sin(k*3.7);p=outline(a)
        tangent=Vector((-math.sin(a),0,math.cos(a)));normal=Vector((0,-1,0))
        b=MeshBuilder();rows=[];w={f'DEF_Petal_{k:02d}':1}
        reach=.43+.095*math.sin(k*2.5);width=.17+.035*math.sin(k*1.8)
        for j in range(25):
            t=j/24;rad=.94-(.94-reach)*t
            c=Vector((p.x*rad,center.y+.10+.94*t+.20*t*t,center.z+p.z*rad))
            c+=tangent*(.07*math.sin(math.pi*t+k*.6)*math.sin(math.pi*t))
            thickness=.13*(1-t)**.65+.008
            breadth=width*(1-t)**.80+.006
            rows.append([b.vertex(c+tangent*breadth*math.cos(math.tau*i/24)+normal*thickness*math.sin(math.tau*i/24),(i/24,t),w) for i in range(24)])
        b.face(tuple(reversed(rows[0])))
        for j,(aa,bb) in enumerate(zip(rows,rows[1:])):
            for i in range(24):n=(i+1)%24;b.face((aa[i],aa[n],bb[n],bb[i]),1 if j>=21 else 0)
        b.face(rows[-1],1)
        result.append(b.build(f'AW_MouthPetal_{k:02d}',collection,[mats['MouthFlesh'],mats['MouthTip']]))
    return result
