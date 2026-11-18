"""A continuous arm skin with a recessed ventral channel and inset sucker bowls."""
from bisect import bisect_left
import math

import numpy as np

from .geometry import Mesh
from .crown import SIDES


def channel(angle):
    # Paired muscular rails flank a broad trough. Angle zero is its floor.
    a=abs((angle+math.pi)%math.tau-math.pi)
    groove=(.5+.5*math.cos(math.pi*min(a/1.10,1)))**1.35
    rail=math.exp(-((a-.89)/.17)**2)
    return -.45*groove+.085*rail


def sleeve(frames, first, total, name, collection, materials):
    mesh=Mesh()
    parameters=[]
    bake_uv=[]
    distance=[0.]
    for left,right in zip(frames,frames[1:]):
        distance.append(distance[-1]+(right[0]-left[0]).length)

    def surface(frame,angle,travel):
        p,t,n,b,r=frame
        ease=min(1,max(0,(travel-.19)/.68))
        ease=ease*ease*(3-2*ease)
        return p+r*(n*(math.cos(angle)+ease*channel(angle))+b*math.sin(angle))

    def vertex(co, paint, u, travel, axial):
        index=mesh.vert(co,paint)
        parameters.append(axial)
        bake_uv.append(((u+.5)%1,.075+.92*travel/distance[-1]))
        return index

    # Each footprint replaces a rectangular patch of skin. The perimeter,
    # rolled lip, inner wall and blind floor belong to the same mesh.
    patches=[]
    omitted=set()
    inside=set()
    for lane in (-1,1):
        k0,k1=(-7,-2) if lane<0 else (-1,7)
        j=bisect_left(distance,1.03+(.34 if lane>0 else 0))
        while j<len(frames)-4 and frames[j][4]>.065:
            radius=frames[j][4]
            end=max(j+2,bisect_left(distance,distance[j]+radius*(.82 if lane<0 else 1.08)))
            if end>=len(frames)-2: break
            patches.append((j,end,k0,k1))
            omitted.update((a,b%SIDES) for a in range(j,end) for b in range(k0,k1))
            inside.update((a,b%SIDES) for a in range(j+1,end) for b in range(k0+1,k1))
            j=max(end+1,bisect_left(distance,distance[end]+radius*.12))

    rings=[]
    for j,frame in enumerate(frames):
        row=[]
        f=(first+j)/(total-1)
        for k in range(SIDES):
            if (j,k) in inside:
                row.append(None)
                continue
            angle=math.tau*k/SIDES
            row.append(vertex(surface(frame,angle,distance[j]),(k/SIDES,f),k/SIDES,distance[j],f))
        rings.append(row)
        if j:
            for k in range(SIDES):
                if (j-1,k) not in omitted:
                    q=(k+1)%SIDES
                    mesh.face((rings[-2][k],row[k],row[q],rings[-2][q]),
                              int(math.cos(math.tau*(k+.5)/SIDES)>.42))
    for j in (0,len(frames)-1):
        p,t,n,b,r=frames[j]
        f=(first+j)/(total-1)
        cap=vertex(p+(t*.145 if j==0 else 0*t),(.5,.5),0,distance[j],f)
        for k in range(SIDES):
            mesh.face((rings[j][k],rings[j][(k+1)%SIDES],cap))

    # Radius fractions of the footprint and axial relief in units of local
    # arm radius. The bowl sits below the surrounding ventral tissue.
    profile=[(.99,0,1),(.94,.035,1),(.89,.087,2),(.82,.104,2),
             (.73,.072,2),(.64,-.015,2),(.45,-.11,3),(.27,-.161,3),(.13,-.20,3)]
    for j0,j1,k0,k1 in patches:
        outline=([(j,k0) for j in range(j0,j1+1)]+[(j1,k) for k in range(k0+1,k1+1)]+
                 [(j,k1) for j in range(j1-1,j0-1,-1)]+[(j0,k) for k in range(k1-1,k0,-1)])
        boundary=[rings[j][k%SIDES] for j,k in outline]
        length=(distance[j1]-distance[j0])*.5
        middle=distance[j0]+length
        width=(k1-k0)*math.pi/SIDES
        center=(k0+k1)*math.pi/SIDES
        angles=[math.atan2((math.tau*k/SIDES-center)/width,(distance[j]-middle)/length) for j,k in outline]
        def inner(rad,h,angle):
            travel=middle+length*rad*math.cos(angle)
            a=center+width*rad*math.sin(angle)
            i=max(1,min(len(frames)-1,bisect_left(distance,travel)))
            blend=(travel-distance[i-1])/max(1e-8,distance[i]-distance[i-1])
            one,two=frames[i-1],frames[i]
            base=surface(one,a,travel).lerp(surface(two,a,travel),blend)
            n=one[2].lerp(two[2],blend).normalized()
            radius=one[4]*(1-blend)+two[4]*blend
            f=(first+i-1+blend)/(total-1)
            return vertex(base+n*(h*radius),(.5+.45*rad*math.cos(angle),.5+.45*rad*math.sin(angle)),
                          a/math.tau,travel,f)
        for rad,h,mat in profile:
            ring=[inner(rad,h,a) for a in angles]
            for i in range(len(ring)):
                q=(i+1)%len(ring)
                mesh.face((boundary[i],boundary[q],ring[q],ring[i]),mat)
            boundary=ring
        bottom=inner(0,-.213,0)
        for i in range(len(boundary)):
            mesh.face((boundary[i],boundary[(i+1)%len(boundary)],bottom),3)
    ob=mesh.finish(name,collection,materials)
    attr=ob.data.attributes.new('ArmParameter','FLOAT','POINT')
    attr.data.foreach_set('value',np.array(parameters,np.float32))
    uv=ob.data.uv_layers.new(name='ArmBakeLayout')
    # The cylindrical seam runs along the dorsal surface, away from the cups.
    for face in ob.data.polygons:
        coords=[bake_uv[i] for i in face.vertices]
        wrap=max(u for u,v in coords)-min(u for u,v in coords)>.5
        for li,(u,v) in zip(face.loop_indices,coords):
            uv.uv[li].vector=(u+int(wrap and u<.5),v)
    ob.data.uv_layers['SurfaceUV'].active_render=True
    ob['sucker_count']=len(patches)
    return ob


def transfer_layout(ob, source, target):
    """The repaired collar and cut disk occupy a separate strip below the sleeve."""
    from .crown import seat
    p,t,n,b,r=seat(ob['limb_id'])
    from mathutils import Vector
    points=[Vector(point) for point in ob['centerline']]
    tip=points[-1]
    tangent=(tip-points[-2]).normalized()
    x_axis=tangent.orthogonal().normalized()
    y_axis=tangent.cross(x_axis)
    radius=max(.006,float(ob['centerline_radii'][-1]))
    axial=ob.data.attributes['ArmParameter']
    for face in ob.data.polygons:
        loops=list(face.loop_indices)
        chart=[tuple(source.uv[i].vector) for i in loops]
        area=sum(a[0]*q[1]-q[0]*a[1] for a,q in zip(chart,chart[1:]+chart[:1]))
        if abs(area)<1.e-10 or any(v<.002 for u,v in chart):
            vertices=[ob.data.vertices[i].co for i in face.vertices]
            if min(axial.data[i].value for i in face.vertices)>.94:
                chart=[(.81+.06*(q-tip).dot(x_axis)/radius,.024+.014*(q-tip).dot(y_axis)/radius) for q in vertices]
            elif not face.use_smooth:
                chart=[(.32+.29*(q-p).dot(n)/r,.023+.019*(q-p).dot(b)/r) for q in vertices]
            else:
                chart=[((math.atan2((q-p).dot(b),(q-p).dot(n))/math.tau+.5)%1,
                        .049+.021*(q-p).dot(t)/.39) for q in vertices]
                if max(u for u,v in chart)-min(u for u,v in chart)>.5:
                    chart=[(u+int(u<.5),v) for u,v in chart]
        # Leave a narrow gutter for triangles straddling the cylindrical seam.
        # Clamping wrapped loops to U=1 collapses the last triangle in a ring.
        for i,(u,v) in zip(loops,chart):
            target.uv[i].vector=(.018+.935*u,v)
