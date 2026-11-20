"""Cast-concrete corner geometry for the compound. All paths use world metres."""
from itertools import pairwise
from mathutils import Matrix


# Front-left, front-right, back-right, back-left; the smaller A1/B1 corner
# has a roof entrance next to it. The opening and stair centreline stay fixed.
WALL_RADII = {'A1':(1.03,.415,1.03,1.03), 'B1':(1.12,.76,1.12,1.12),
              'C1':(.94,.94,1.22,1.22)}
PLAZA_RADIUS = 2.32


def left_normal(u):
    return Vector((-u.y,u.x))


def corner_arc(vertex,u,v,radius,steps=18):
    angle=math.atan2(u.x*v.y-u.y*v.x,u.dot(v))
    sign=1 if angle>0 else -1
    normal=left_normal(u)
    start=vertex-u*(radius*math.tan(abs(angle)/2))
    centre=start+normal*(sign*radius)
    result=[]
    for j in range(steps+1):
        t=angle*j/steps
        n=Vector((normal.x*cos(t)-normal.y*sin(t),normal.x*sin(t)+normal.y*cos(t)))
        result.append((centre-n*(sign*radius),n))
    return result


def fillet_path(points,radii,closed=False):
    points=[Vector(p) for p in points]
    result=[]
    for i,p in enumerate(points):
        if not closed and i==0:
            result.append((p,left_normal((points[1]-p).normalized())))
        elif not closed and i==len(points)-1:
            result.append((p,left_normal((p-points[i-1]).normalized())))
        else:
            u=(p-points[i-1]).normalized()
            v=(points[(i+1)%len(points)]-p).normalized()
            if radii[i]>0:
                result.extend(corner_arc(p,u,v,radii[i]))
            else:
                result.append((p,left_normal((u+v).normalized())))
    return result


def rectangle(lo,hi):
    return [Vector((lo[0],lo[1])),Vector((hi[0],lo[1])),
            Vector((hi[0],hi[1])),Vector((lo[0],hi[1]))]


def prism_form(outline,z0,z1):
    n=len(outline)
    vertices=[(*p,z) for z in (z0,z1) for p in outline]
    faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
    return vertices,faces


def band_form(samples,outside,inside,z0,top):
    """Sweep a solid wall about a path. Left-normal offsets may be asymmetric."""
    vertices,faces=[],[]
    for i,(p,n) in enumerate(samples):
        z1=top(p,n) if callable(top) else top
        outer,inner=p+n*outside,p+n*inside
        vertices.extend([(*outer,z0),(*inner,z0),(*outer,z1),(*inner,z1)])
        if i:
            k=(i-1)*4
            faces.extend([(k,k+4,k+6,k+2),(k+1,k+3,k+7,k+5),
                          (k+2,k+6,k+7,k+3),(k,k+1,k+5,k+4)])
    k=len(vertices)-4
    faces.extend([(0,2,3,1),(k,k+1,k+3,k+2)])
    return vertices,faces


def write_form(obj,form,edge):
    verts,faces=form
    old=obj.data
    materials=list(old.materials)
    data=bpy.data.meshes.new(obj.name+' form')
    inv=obj.matrix_world.inverted()
    data.from_pydata([inv@Vector(p) for p in verts],[],faces)
    data.update()
    bm=bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    for mat in materials:
        data.materials.append(mat)
    for face in data.polygons:
        face.use_smooth=abs(face.normal.z)<.2
    data.set_sharp_from_angle(angle=math.radians(28))
    obj.data=data
    if not old.users:
        bpy.data.meshes.remove(old)
    bevel=next((m for m in obj.modifiers if m.type=='BEVEL'),None)
    if not bevel:
        bevel=obj.modifiers.new('Cast edge','BEVEL')
    bevel.width=edge
    bevel.segments=3
    bevel.harden_normals=True
    bevel.limit_method='ANGLE'
    obj['cast_corner']=True
    return obj


def new_form(collection,name,form,mat,edge):
    obj=collection.objects.get(name)
    if obj is None:
        obj=bpy.data.objects.new(name,bpy.data.meshes.new(name))
        collection.objects.link(obj)
        verts=form[0]
        centre=[(min(v[i] for v in verts)+max(v[i] for v in verts))*.5 for i in range(3)]
        obj.matrix_world=Matrix.Translation(centre)
        obj.data.materials.append(mat)
        obj['owner']=TAG
    return write_form(obj,form,edge)


def round_box(obj,radius,edge=.035,heights=None):
    lo,hi=access_bounds(obj)
    radii=(radius,)*4 if isinstance(radius,(float,int)) else radius
    line=fillet_path(rectangle(lo,hi),radii,True)
    z0,z1=heights or (lo[2],hi[2])
    return write_form(obj,prism_form([p for p,n in line],z0,z1),edge)


def split_band(samples,distance):
    """Split in plan by arc length, using the same section on both sides."""
    for i,((a,n),(b,m)) in enumerate(pairwise(samples)):
        length=(b-a).length
        # Snap within two centimetres of a cast station, to avoid a sliver
        # face where the jamb's bevel ends against the first curved segment.
        if i and distance<=.020:
            return samples[:i+1],samples[i:]
        if abs(distance-length)<=.020:
            return samples[:i+2],samples[i+1:]
        if distance<length:
            t=distance/length
            cut=(a.lerp(b,t),n.lerp(m,t).normalized())
            return [*samples[:i+1],cut],[cut,*samples[i+1:]]
        distance-=length
    raise ValueError('End post exceeds roof perimeter')


def roof_parapet(scene,plan,code):
    body=scene.objects[code+' body']
    group=body.users_collection[0]
    mat=scene.objects[code+' flat roof'].active_material
    for obj in list(group.objects):
        if obj.get('owner')==TAG and 'parapet' in obj.name.lower():
            bpy.data.objects.remove(obj,do_unlink=True)
    lo,hi=access_bounds(body)
    rect=rectangle(lo,hi)
    side={'A1':0,'B1':1,'C1':2}[plan.building]
    d=(rect[(side+1)%4]-rect[side]).normalized()
    entry=plan.nodes[-1].xy.copy()
    entry[1-plan.mouth_axis]=plan.face
    ids=[(side+j)%4 for j in range(1,5)]
    points=[entry+d*(plan.width/2),*(rect[i] for i in ids),entry-d*(plan.width/2)]
    line=fillet_path(points,[0,*(WALL_RADII[plan.building][i] for i in ids),0])
    start,rest=split_band(line,.245)
    end,mid=split_band(list(reversed(rest)),.245)
    z=plan.nodes[-1].z-.0375
    def cast_end(name,part,half,height,edge):
        obj=new_form(group,name,band_form(part,-half,half,z,z+height),mat,edge)
        # At the tangent next to an opening the bevel can converge on the same
        # point twice. Merge these coincident end vertices after evaluation.
        weld=obj.modifiers.get('Weld cast ends') or obj.modifiers.new('Weld cast ends','WELD')
        weld.merge_threshold=.000025
    for part in (start,list(reversed(end))):
        p=part[0][0] if part is start else part[-1][0]
        high=p[plan.mouth_axis]>entry[plan.mouth_axis]
        label=(('east' if high else 'west') if plan.mouth_axis==0 else ('north' if high else 'south'))+' jamb'
        cast_end(plan.building+' access | '+label,part,.20,1.15,.038)
    cast_end(code+' rounded roof parapet',list(reversed(mid)),.175,.65,.045)


def quarter_jacket(rect,radii,index,extension):
    p=rect[index]
    u=(p-rect[index-1]).normalized()
    v=(rect[(index+1)%4]-p).normalized()
    reach=radii[index]+extension
    return fillet_path([p-u*reach,p,p+v*reach],[0,radii[index],0])


def round_building(scene,code,building):
    body=scene.objects[code+' body']
    group=body.users_collection[0]
    lo,hi=access_bounds(body)
    rect=rectangle(lo,hi)
    radii=WALL_RADII[building]
    outline=fillet_path(rect,radii,True)
    foundation_top=access_bounds(scene.objects[code+' foundation'])[1][2]
    skins=[(code+' body',.055),(code+' flat roof',.042),(code+' roof finish',.007),
           (code+' foundation',.035)]
    if building=='C1' and scene.objects.get('C1 continuous foundation'):
        skins.append(('C1 continuous foundation',.030))
    for name,edge in skins:
        obj=scene.objects[name]
        bottom,top=access_bounds(obj)
        dx=lo[0]-bottom[0]
        dy=lo[1]-bottom[1]
        poly=[Vector((p.x-dx*n.x,p.y-dy*n.y)) for p,n in outline]
        z0=foundation_top if obj==body else bottom[2]
        write_form(obj,prism_form(poly,z0,top[2]),edge)

    for obj in list(group.objects):
        if obj.get('owner')!=TAG:
            continue
        name=obj.name
        if name.startswith(building+' casting seam'):
            # Cast joints terminate on the tangent, instead of extending into air.
            za,zb=access_bounds(obj)
            p=(lo[0]+radii[0],lo[1]-.003)
            q=(hi[0]-radii[1],lo[1]+.003)
            write_form(obj,prism_form(rectangle(p,q),za[2],zb[2]),.0006)
        if name.startswith('Formwork tie recess'):
            x=obj.location.x
            left=x<lo[0]+radii[0]
            right=x>hi[0]-radii[1]
            if left or right:
                r=radii[0] if left else radii[1]
                cx=lo[0]+r if left else hi[0]-r
                ny=-math.sqrt(max(0,1-((x-cx)/r)**2))
                n=Vector(((x-cx)/r,ny,0))
                obj.location.y=lo[1]+r+ny*(r+.002)
                obj.rotation_euler=n.to_track_quat('Z','Y').to_euler()
        if not name.startswith((code+' facade pier','C1 reinforced facade pier','C1 pier foot','C1 pier coping')):
            continue
        index=0 if abs(obj.location.x-lo[0])<.10 else 1 if abs(obj.location.x-hi[0])<.10 else None
        if index is None:
            continue
        if building=='C1' and name.startswith(code+' facade pier'):
            bpy.data.objects.remove(obj,do_unlink=True)
            continue
        za,zb=access_bounds(obj)
        depth=.22 if name.startswith(code+' facade pier') else (.49 if 'foot' in name or 'coping' in name else .36)
        line=quarter_jacket(rect,radii,index,.24 if building=='C1' else .12)
        z0,z1=za[2],zb[2]
        if name.startswith(code+' facade pier'):
            z0=foundation_top
        elif name.startswith('C1 reinforced facade pier'):
            suffix=name[len('C1 reinforced facade pier'):]
            z0=access_bounds(group.objects['C1 pier foot'+suffix])[1][2]
            z1=access_bounds(group.objects['C1 pier coping'+suffix])[0][2]
        write_form(obj,band_form(line,-depth,.04,z0,z1),.026)
    if building=='C1':
        # Short cast joints keep the original adjoining enclosure tied into the
        # curved gatehouse up to wall height, on either side of the main gate.
        for sign,x in [(-1,lo[0]),(1,hi[0])]:
            p=(min(x+sign*.22,x-sign*1.15),lo[1]-.91)
            q=(max(x+sign*.22,x-sign*1.15),lo[1]+.33)
            line=fillet_path(rectangle(p,q),[.19]*4,True)
            name='C1 enclosure joint '+('west' if sign<0 else 'east')
            new_form(group,name,prism_form([pos for pos,n in line],-.06,5.56),body.active_material,.04)


def trim_joint(obj,rect,radius):
    lo,hi=access_bounds(obj)
    axis=0 if hi[0]-lo[0]>hi[1]-lo[1] else 1
    other=1-axis
    p,q=rect[0],rect[2]
    cross=max(p[other]+.038,min(q[other]-.038,obj.location[other]))
    dist=min(cross-p[other],q[other]-cross)
    cut=radius-math.sqrt(max(0,radius**2-max(0,radius-dist)**2)) if dist<radius else 0
    start,end=[0.,0.],[0.,0.]
    start[axis],end[axis]=p[axis]+cut+.04,q[axis]-cut-.04
    start[other],end[other]=cross-.0125,cross+.0125
    write_form(obj,prism_form(rectangle(start,end),lo[2],hi[2]),.001)


def round_plaza(scene,detailed):
    base=scene.objects['Raised square plaza']
    group=base.users_collection[0]
    trim=scene.objects['03_A1 flat roof'].active_material
    lo,hi=access_bounds(base)
    rect=rectangle(lo,hi)
    round_box(base,PLAZA_RADIUS,.06)
    paving=scene.objects['Paved plaza']
    round_box(paving,PLAZA_RADIUS,.009)
    paved_z=access_bounds(paving)[1][2]
    for obj in list(group.objects):
        if obj.get('owner')!=TAG:
            continue
        if obj.name.startswith(('Plaza front coping','Plaza edge wall')):
            bpy.data.objects.remove(obj,do_unlink=True)
        elif obj.name.startswith('Plaza expansion joint'):
            trim_joint(obj,rect,PLAZA_RADIUS)
        elif obj.name.startswith('Plaza stair pier'):
            round_box(obj,.19,.06)
        elif obj.name.startswith('Flag base step'):
            round_box(obj,.45+(obj.dimensions.x-5.2)*.20,.032)
        elif obj.name=='Flag plinth':
            round_box(obj,.24,.045)
        elif obj.name in ('Plaza east upper landing','Plaza west upper landing'):
            east=obj.name=='Plaza east upper landing'
            a,b=access_bounds(obj)
            approach=scene.objects['Plaza east stairs' if east else 'Plaza west ramp']
            seam_y=access_bounds(approach)[1][1]
            left,right=(hi[0],b[0]) if east else (a[0],lo[0])
            shape=rectangle((left,seam_y),(right,b[1]))
            radii=(0,.20,.78,0) if east else (.075,0,0,.78)
            form=fillet_path(shape,radii,True)
            write_form(obj,prism_form([p for p,n in form],a[2],paved_z),.018)
        elif obj.name.startswith(('Plaza south stairs','Plaza east stairs','Plaza west ramp','Ramp curb')):
            # Bring the three approaches up to the surface of the paving course.
            # Scale in world Z, including each separate tread's nosing.
            if obj.get('plaza_walk_level')!=paved_z:
                mw=obj.matrix_world.copy()
                transform=mw.inverted()@Matrix.Diagonal((1.,1.,paved_z/hi[2],1.))@mw
                obj.data.transform(transform)
                obj['plaza_walk_level']=paved_z
    inset=.23
    r=PLAZA_RADIUS-inset
    front,rear=lo[1]+inset,hi[1]-inset
    for east in (False,True):
        x=hi[0]-inset if east else lo[0]+inset
        outward=1 if east else -1
        mouth=0 if east else -10
        lower=fillet_path([(mouth,front),(x,front),(x,17.4)],[0,r,0])
        side='east' if east else 'west'
        def rim_height(p,n):
            # Match the low front curb to the side parapet around the same arc.
            t=abs(n.x)
            return 2.32+.38*t*t*(3-2*t) if detailed else 2.32
        new_form(group,'Plaza south '+side+' curved coping',band_form(lower,-.215,.215,2.027,rim_height),trim,.045)
        upper=fillet_path([(x,20.8),(x,rear),(x-outward*(r+.52),rear)],[0,r,0])
        new_form(group,'Plaza north '+side+' curved coping',band_form(upper,-.215,.215,2.027,rim_height),trim,.045)


def retained_roof(obj,rear_y):
    """Round the two cast corners; retain every vertex of the broken roof edge."""
    if 'cast_roof_original' not in obj:
        cap=max((f for f in obj.data.polygons if f.normal.z>.99),key=lambda f:f.area)
        points=[obj.matrix_world@obj.data.vertices[i].co for i in cap.vertices]
        obj['cast_roof_original']=[v for p in points for v in (p.x,p.y)]
    values=list(obj['cast_roof_original'])
    polygon=[Vector(p) for p in zip(values[::2],values[1::2])]
    mid=sum(p.x for p in polygon)/len(polygon)
    radii=[(.82 if p.x<mid else .39) if p.y>rear_y+.14 else 0 for p in polygon]
    line=fillet_path(polygon,radii,True)
    lo,hi=access_bounds(obj)
    write_form(obj,prism_form([p for p,n in line],lo[2],hi[2]),.022)


def round_depot(scene,name):
    floor=scene.objects[name+' floor']
    group=floor.users_collection[0]
    lo,hi=access_bounds(floor)
    xl,xr=lo[0]-.325,hi[0]+.325
    front,rear=lo[1]-.325,hi[1]+.325
    mid=(front+rear)/2
    rect=rectangle((xl,front),(xr,rear))
    radius=.78
    round_box(floor,.47,.03)
    previous=group.objects.get(name+' curved warehouse walls')
    west=group.objects.get(name+' west wall')
    source=west if west is not None else previous
    wall_top=access_bounds(source)[1][2]
    wall_base=lo[2]-.025
    mat=source.active_material
    if name=='Armory':
        points=[(xl,front),(xl,rear),(xr-3,rear)]
        radii=[0,radius,0]
    else:
        points=[(xl,front),(xl,rear),(xr,rear),(xr,mid)]
        radii=[0,radius,radius,0]
    for o in list(group.objects):
        if o.get('owner')!=TAG:
            continue
        if o.name in (name+' west wall',name+' rear wall',name+' rear wall retained') or (
                name!='Armory' and o.name==name+' east wall'):
            bpy.data.objects.remove(o,do_unlink=True)
        elif name=='Armory' and o.name==name+' east wall':
            round_box(o,.16,.035,(wall_base,access_bounds(o)[1][2]))
        elif o.name.startswith(name+' pier'):
            shoe='shoe' in o.name
            index=3 if o.location.x<(xl+xr)*.5 else 2
            z0=lo[2]-.052 if shoe else lo[2]+.80
            z1=access_bounds(o)[1][2]
            if abs(o.location.y-rear)<.1 and (index==3 or name!='Armory'):
                half=.50 if shoe else .425
                write_form(o,band_form(quarter_jacket(rect,[radius]*4,index,.12),
                                       -half,half,z0,z1),.04)
            else:
                round_box(o,.205 if shoe else .175,.04,(z0,z1))
        elif o.name==name+' jagged retained roof':
            retained_roof(o,rear)
        elif o.name==name+' roof rear':
            round_box(o,(.10,.10,.75,.75),.025)
    line=fillet_path(points,radii)
    new_form(group,name+' curved warehouse walls',band_form(line,-.30,.30,wall_base,wall_top),mat,.046)


def round_shed(scene,name):
    shed=scene.objects[name]
    group=shed.users_collection[0]
    lo,hi=access_bounds(shed)
    radius=.32
    round_box(shed,radius,.035)
    roof=scene.objects[name+' corrugated roof']
    roof_lo,roof_hi=access_bounds(roof)
    rr=radius+lo[0]-roof_lo[0]
    round_box(roof,rr,.025)
    for obj in group.objects:
        if obj.get('owner')!=TAG or not lo[0]-.1<=obj.location.x<=hi[0]+.1:
            continue
        x,y=obj.location.x,obj.location.y
        if obj.name.startswith('Corrugation') and abs(y-lo[1])<radius+.15:
            centre=max(lo[0]+radius,min(hi[0]-radius,x))
            dx=x-centre
            if abs(dx)>.0001:
                ny=-math.sqrt(max(0,1-(dx/radius)**2))
                obj.location.y=lo[1]+radius+ny*(radius+.019)
                obj.rotation_euler.z=math.atan2(dx/radius,-ny)
        elif obj.name.startswith('Roof rib') and lo[1]<y<hi[1]:
            dx=max(0,roof_lo[0]+rr-x,x-(roof_hi[0]-rr))
            trim=rr-math.sqrt(max(0,rr*rr-dx*dx))
            a,b=access_bounds(obj)
            corners=rectangle((a[0],roof_lo[1]+trim+.045),(b[0],roof_hi[1]-trim-.045))
            write_form(obj,prism_form(corners,a[2],b[2]),.005)


def soften_compound(scene,detailed=True):
    bpy.context.view_layer.update()
    rebuild_stair_access(scene,detailed=detailed)
    for code,building in [('03_A1','A1'),('04_B1','B1'),('05_C1','C1')]:
        round_building(scene,code,building)
    round_plaza(scene,detailed)
    for name in ('Equipment','Medical','Armory'):
        round_depot(scene,name)
    for name in ('Utility shed','Supply shed'):
        round_shed(scene,name)
    bpy.context.view_layer.update()
    scene['architectural_corners']='Tangent-arc concrete envelopes, roof access and plaza. 2026-11-20.'
