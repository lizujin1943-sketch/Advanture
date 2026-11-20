"""B-2 communications pavilion with a gasketed geodesic radome and service bays."""
import math
import bpy
import bmesh
from mathutils import Vector


def upper_faces():
    sphere=bmesh.new()
    bmesh.ops.create_icosphere(sphere,subdivisions=3,radius=1)
    result=[]
    for face in sphere.faces:
        ring=[v.co.copy() for v in face.verts]
        for p in ring:
            if abs(p.z)<1.e-4:
                p.z=0.
        poly=[]
        for a,b in zip(ring,ring[1:]+ring[:1]):
            if a.z>=0:
                poly.append(a)
            if a.z*b.z<0:
                p=a.lerp(b,a.z/(a.z-b.z))
                p.z=0
                poly.append(p)
        if len(poly)>=3:
            result.append(poly)
    sphere.free()
    return result


def radome(f,centre):
    radius,height=3.66,4.00
    origin=(centre[0],centre[1],7.32)
    indices,skin,faces={},[],[]
    panels,panel_faces,materials=[],[],[]
    for k,polygon in enumerate(upper_faces()):
        poly=[Vector((p.x*radius,p.y*radius,p.z*height)) for p in polygon]
        normal=(poly[1]-poly[0]).cross(poly[2]-poly[0]).normalized()
        if normal.dot(sum(poly,Vector())/len(poly))<0:
            normal.negate()
            poly.reverse()
        ring=[]
        for p in poly:
            key=tuple(round(v,5) for v in p)
            if key not in indices:
                indices[key]=len(skin)
                skin.append(p)
            ring.append(indices[key])
        faces.append(tuple(ring))
        mid=sum(poly,Vector())/len(poly)
        footprint=[mid.lerp(p,.970) for p in poly]
        j,n=len(panels),len(poly)
        panels.extend(p+normal*depth for depth in (-.012,.025) for p in footprint)
        panel_faces.extend([tuple(j+i for i in reversed(range(n))),tuple(j+n+i for i in range(n))])
        panel_faces.extend((j+i,j+(i+1)%n,j+n+(i+1)%n,j+n+i) for i in range(n))
        tone=(1 if k%9 in (1,5) else 2 if k%15==8 else 0)
        materials.extend([0,tone]+[tone]*n)
    rim=sorted([i for i,v in enumerate(skin) if abs(v.z)<1.e-5],
               key=lambda i:math.atan2(skin[i].y,skin[i].x))
    faces.append(tuple(reversed(rim)))
    f.mesh('sealed backing shell',skin,faces,origin,'domejoint')
    tiles=f.mesh('composite triangular panels',panels,panel_faces,origin,'dome0')
    for mat in ('dome1','dome2'):
        tiles.data.materials.append(f.materials[mat])
    for face,index in zip(tiles.data.polygons,materials):
        face.material_index=index
    return len(faces)-1


def service_bays(f,centre):
    step=2*math.pi/16
    for i in range(16):
        if i in (11,12,13):
            continue
        a=(i+.5)*step
        left,right=a-.145,a+.145
        lower,upper=4.32,5.54
        louvre=i in (1,5,9,14)
        f.ring('service cassette backing',centre,6.96,7.075,lower-.105,upper+.13,
               'iron',left-.009,right+.009,6)
        f.ring('inset '+('air intake' if louvre else 'clerestory glass'),centre,7.056,7.09,
               lower+.025,upper-.025,'rubber' if louvre else 'glass',left,right,6,.003)
        f.ring('cassette rain hood',centre,6.985,7.255,upper+.075,upper+.173,'teal',left-.019,right+.019,6)
        f.ring('cassette sill',centre,7.012,7.151,lower-.11,lower+.018,'teal',left-.012,right+.012,6)
        for t in (left,right):
            f.ring('frame jamb',centre,7.029,7.136,lower+.012,upper+.082,'teal',t-.008,t+.008,1,.004)
        if louvre:
            for j in range(5):
                z=lower+.15+j*.225
                f.ring('vent blade',centre,7.068,7.175,z,z+.070,'iron',left+.007,right-.007,5,.008)
        else:
            f.ring('glazing mullion',centre,7.072,7.113,lower+.02,upper-.014,'teal',a-.005,a+.005,1,.003)


def door_approach(f,x,y):
    # The upper tread lands above the concentric plinths on the existing sill.
    points=[(-9.48,-.062),(-7.15,-.062),(-7.15,.703),(-8.28,.703),
            (-8.28,.53),(-8.68,.53),(-8.68,.353),(-9.08,.353),(-9.08,.177),(-9.48,.177)]
    verts=[(dx,dy,z) for dx in (-1.50,1.50) for dy,z in points]
    n=len(points)
    faces=[tuple(reversed(range(n))),tuple(range(n,n*2))]
    faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
    f.mesh('entry steps',verts,faces,(x,y,0),'cap',.015)
    for sx in (-1,1):
        px=x+sx*1.32
        f.beam('entry canopy bracket',(px,y-7.02,3.57),(px,y-8.07,4.35),.082,.105,'teal')
    f.box('entry rain canopy',(x,y-7.54,4.43),(3.10,1.20,.17),'teal',.033)
    f.box('canopy drip nose',(x,y-8.134,4.40),(3.13,.075,.22),'iron',.017)


def build(scene,palette,Fabricator):
    group=scene.objects['B2 drum'].users_collection[0]
    f=Fabricator(group,palette,'B2')
    x,y=21.1,-22.
    centre=(x,y,0)
    for ob in list(group.objects):
        if ob.name=='B2 radome' or ob.name.startswith(('Radome meridian seam','Radome latitude seam')):
            bpy.data.objects.remove(ob,do_unlink=True)
    f.ring('equipment belt',centre,6.965,7.108,5.80,6.14,'teal')
    f.ring('drip course',centre,7.016,7.19,6.116,6.19,'iron')
    f.ring('radome projecting flange',centre,3.40,3.91,6.865,7.025,'iron')
    f.ring('radome curb',centre,3.49,3.76,7.014,7.31,'teal')
    f.ring('compression rim',centre,3.42,3.835,7.285,7.355,'galv',segments=96)
    count=radome(f,centre)
    for j in range(24):
        a=(j+.5)*2*math.pi/24
        px,py=x+3.815*math.cos(a),y+3.815*math.sin(a)
        f.tube('flange fastener',(px,py,6.996),(px,py,7.075),.045,'galv',6)
    # A curb raises the personnel hatch above the roof membrane.
    hx,hy=x-.60,y-5.12
    f.box('roof hatch upstand',(hx,hy,7.005),(1.54,1.24,.232),'iron',.037)
    f.box('roof access hatch',(hx,hy,7.142),(1.62,1.32,.096),'teal',.020)
    for dx in (-.49,.49):
        f.tube('hatch hinge',(hx+dx-.105,hy+.58,7.18),(hx+dx+.105,hy+.58,7.18),.046,'galv')
    for dx in (-.134,.134):
        f.box('hatch pull foot',(hx+dx,hy-.45,7.211),(.048,.047,.079),'galv',.005)
    f.tube('hatch pull',(hx-.154,hy-.45,7.246),(hx+.154,hy-.45,7.246),.024,'galv')
    service_bays(f,centre)
    door_approach(f,x,y)
    mark=next(o for o in group.objects if o.type=='FONT' and o.data.body=='B-2')
    angle=math.radians(-56)
    mark.data.align_x='CENTER'
    mark.data.size*=.73
    mark.location=(x+7.06*math.cos(angle),y+7.06*math.sin(angle),3.22)
    mark.rotation_euler=(math.pi/2,0,angle+math.pi/2)
    conform=mark.modifiers.new('Painted onto the curved concrete','SHRINKWRAP')
    conform.target=scene.objects['B2 drum']
    conform.offset=.012
    scene['geodesic_panels']=count
