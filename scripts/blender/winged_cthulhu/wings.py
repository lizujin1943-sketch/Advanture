"""Bat wing fans with curved load-bearing fingers and scalloped, cambered membranes."""
import math
from mathutils import Vector
from .geometry import Mesh,P,spline

def transform(x,y,side):
    if side=='L':return x,y
    # Deliberate reference asymmetry, not an exact mirror.
    width=.78+.12*max(0,min(1,(y-50)/120)) if y<=170 else .9-.16*min(1,(y-170)/305)
    return 1135+(457-x)*width, y+(.035*(x-450))

def wing_surface(side,col,m):
    tr=lambda x,y:transform(x,y,side)
    origin=Vector(tr(457,67))
    # Four membrane lobes, each ends in a long finger. Scallop points are inset.
    lobes=[
        [(148,182),(181,164),(215,163),(230,146),(201,204),(143,273),(76,366),(7,476)],
        [(7,476),(43,407),(87,358),(128,348),(153,389),(169,377),(191,447)],
        [(191,447),(225,373),(279,322),(324,300),(356,322),(374,349),(390,339),(402,402)],
        [(402,402),(430,352),(462,325),(493,323),(510,339),(536,318),(579,337),(638,361)]
    ]
    g=Mesh()
    for ix,edge in enumerate(lobes):
        ee=spline([Vector(tr(x,y)) for x,y in edge],8)
        # A triangular fan, sampled radially: zero-area centre quads avoided.
        rows=[];n=len(ee);center=g.vert(P(origin.x,origin.y,1.13),(.5,1))
        for j in range(1,33):
            t=j/32;row=[]
            for i,e in enumerate(ee):
                s=i/(n-1)
                q=origin.lerp(e,t)
                # Membrane bows behind ribs; radial tension folds are subdued.
                depth=1.14+.38*math.sin(math.pi*s)*math.sin(math.pi*t)
                depth+=.035*math.sin(s*math.pi*7)*math.sin(math.pi*t)**2
                row.append(g.vert(P(q.x,q.y,depth),(s,t)))
            rows.append(row)
        for i in range(n-1):g.face((center,rows[0][i],rows[0][i+1]))
        for a,b in zip(rows,rows[1:]):
            for i in range(n-1):g.face((a[i],b[i],b[i+1],a[i+1]))
    ob=g.finish('CR_WingedCthulhu_WingMembrane_'+side,col,[m['membrane']])
    solid=ob.modifiers.new('Living membrane thickness','SOLIDIFY');solid.thickness=.045;solid.offset=0
    # The leading edge follows the silhouette, its four ray fingers share the wrist.
    rays=[[(463,69,21),(412,65,17),(351,82,12),(269,109,9),(204,134,6),(148,182,.5)],
          [(458,68,20),(397,122,14),(324,192,10),(237,267,7),(147,352,4.5),(65,411,2.5),(7,476,.4)],
          [(455,72,21),(421,119,16),(367,181,12),(307,258,9),(244,348,6),(206,417,3),(191,447,.5)],
          [(460,74,20),(447,145,15),(426,229,11),(413,310,7),(402,402,.6)],
          [(462,77,20),(481,138,17),(509,207,16),(557,227,18),(600,269,20),(637,319,17)]]
    for i,ray in enumerate(rays):
        gg=Mesh();knots=[(*tr(x,y),1.06,r) for x,y,r in ray]
        gg.tube(knots,28,12,ripple=.028)
        gg.finish('CR_WingedCthulhu_WingFinger_%s_%d'%(side,i),col,[m['skin']])
    # Hooked talons rising above the wrist.
    claw=[(452,75,23),(468,28,22),(492,18,19),(527,22,15),(552,34,9),(565,56,.7)]
    gg=Mesh();gg.tube([(*tr(x,y),1.02,r) for x,y,r in claw],32,10,ripple=.022)
    gg.finish('CR_WingedCthulhu_WingClaw_'+side,col,[m['ridge']])
    # Fine structural veins branch from the rays; not random floating surface cables.
    veins=[[(422,118),(363,172),(296,196),(243,206)],
           [(363,181),(302,224),(244,242),(198,273)],
           [(307,258),(263,290),(221,308),(192,342)],
           [(426,225),(385,257),(350,285),(328,303)],
           [(413,305),(390,314),(376,336)],
           [(484,144),(493,219),(515,276),(543,308)]]
    gg=Mesh()
    for v in veins:
        gg.tube([(*tr(x,y),1.125,1.9*(1-i/len(v))+.35) for i,(x,y) in enumerate(v)],12,7,ripple=0)
    gg.finish('CR_WingedCthulhu_WingVeins_'+side,col,[m['membrane']])

def build(col,m):
    for side in ('L','R'):wing_surface(side,col,m)
