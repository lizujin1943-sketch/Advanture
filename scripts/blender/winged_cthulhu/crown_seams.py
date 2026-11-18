"""Close each independently severable sleeve on one common, tangent-matched ring.

Recut after decimation: independent reducers cannot preserve vertex coincidence.
The cut is confined to the proximal sleeve and the corresponding crown tissue.
"""
import math,bmesh
from collections import deque
from mathutils import Vector
from .crown import seat,SIDES
from .geometry import Mesh
from .crown import fork,trunk_angles

def ring_order(vs,origin,n,b):
    vs=set(vs)
    angle=lambda v:math.atan2((v.co-origin).dot(b),(v.co-origin).dot(n))%math.tau
    first=min(vs,key=angle)
    border=lambda v:[e.other_vert(v) for e in v.link_edges if e.is_boundary and e.other_vert(v) in vs]
    adj=border(first)
    if not adj:
        assert not any(v.link_edges for v in vs)
        return sorted(vs,key=angle)
    assert len(adj)==2
    # A voxel cut need not be star-shaped. Sorting all its vertices by
    # azimuth creates shortcuts across small concavities and leaves holes.
    loop=[first,adj[0]]
    while True:
        neighbors=border(loop[-1]);assert len(neighbors)==2
        nxt=next(v for v in neighbors if v!=loop[-2])
        if nxt==first:break
        loop.append(nxt);assert len(loop)<=len(vs)
    assert len(loop)==len(vs),(len(loop),len(vs))
    pts=[v.co-origin for v in loop]
    if sum(x.cross(y).dot(n.cross(b)) for x,y in zip(pts,pts[1:]+pts[:1]))<0:
        loop=[first]+loop[:0:-1]
    return loop

def stitch(bm,a,b,origin,n,side,material):
    a=ring_order(a,origin,n,side);b=ring_order(b,origin,n,side)
    angle=lambda v:math.atan2((v.co-origin).dot(side),(v.co-origin).dot(n))%math.tau
    # Walk the two circular contours, preferring short cross edges.
    i=j=0
    while i<len(a) or j<len(b):
        ni=angle(a[(i+1)%len(a)])+(math.tau if i+1>=len(a) else 0) if i<len(a) else math.inf
        nj=angle(b[(j+1)%len(b)])+(math.tau if j+1>=len(b) else 0) if j<len(b) else math.inf
        if abs(ni-nj)<1e-4:
            verts=(a[i%len(a)],a[(i+1)%len(a)],b[(j+1)%len(b)],b[j%len(b)]);i+=1;j+=1
        elif ni<nj:
            verts=(a[i%len(a)],a[(i+1)%len(a)],b[j%len(b)]);i+=1
        else:
            verts=(a[i%len(a)],b[(j+1)%len(b)],b[j%len(b)]);j+=1
        f=bm.faces.new(verts);f.material_index=material;f.smooth=True

def replace_lower_mantle(ob):
    # Keep the fine hand-built saddles out of voxel remesh entirely. Splice
    # to the fused upper head at a simple trunk section, behind the beard.
    bm=bmesh.new();bm.from_mesh(ob.data)
    tag=bm.verts.layers.float.new('CrownTissue')
    z=5.75;up=Vector((0,0,1));origin=Vector((0,.19,z))
    bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
                            plane_no=up,plane_co=origin,dist=1e-6)
    seed=min((f for f in bm.faces if abs(f.calc_center_median().x)<.2 and abs(f.calc_center_median().y)<.2),
             key=lambda f:f.calc_center_median().z)
    remove={seed};q=[seed]
    while q:
        for e in q.pop().edges:
            for f in e.link_faces:
                if f not in remove and f.calc_center_median().z<z-1e-6:
                    remove.add(f);q.append(f)
    bmesh.ops.delete(bm,geom=list(remove),context='FACES')
    rim=[v for v in bm.verts if v.is_boundary]
    assert rim and all(abs(v.co.z-z)<.003 for v in rim),'Unexpected opening during crown graft'
    bmesh.ops.remove_doubles(bm,verts=rim,dist=.0015)
    rim=[v for v in bm.verts if v.is_boundary]
    g=Mesh();angles=list(trunk_angles())
    upper=[g.vert((1.25*math.sin(a),.16-1.25*math.cos(a),5.56)) for a in angles]
    fork(g,upper)
    mat=next(i for i,m in enumerate(ob.data.materials) if m.name.startswith('Skin'))
    verts=[bm.verts.new(co) for co in g.v]
    for v in verts:v[tag]=1
    for indices in g.f:
        f=bm.faces.new([verts[i] for i in indices]);f.material_index=mat;f.smooth=True
    stitch(bm,rim,[verts[i] for i in upper],origin,Vector((1,0,0)),Vector((0,1,0)),mat)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free();ob.data.update()

def trim_and_loft(bm,key,arm,material,params):
    p,t,n,b,r=seat(key);sgn=1 if arm else -1
    plane=p+t*(.38 if arm else -.32)
    # Limit the cut to the intended wrist. Other arms and oral tendrils may
    # cross the infinite plane elsewhere in the sculpt.
    crown=bm.verts.layers.float.get('CrownTissue') if not arm else None
    if arm:
        # The floor and lips of each sucker are continuous with the arm.
        # Material boundaries can cross this oblique plane on curved wrists.
        axial=bm.verts.layers.float['ArmParameter']
        fs={f for f in bm.faces if min(v[axial] for v in f.verts)<params+.23}
        es={e for f in fs for e in f.edges}
        near={v for f in fs for v in f.verts}
    else:
        near={v for v in bm.verts if (v.co-plane).length_squared<1.05 and v[crown]>.5}
        es={e for v in near for e in v.link_edges};fs={f for e in es for f in e.link_faces}
    result=bmesh.ops.bisect_plane(bm,geom=list(near)+list(es)+list(fs),dist=.00001,plane_co=plane,plane_no=t)
    edges={e for e in result['geom_cut'] if isinstance(e,bmesh.types.BMEdge) and e.is_valid}
    # Walk closed cuts and select the loop surrounding this arm axis.
    loops=[]
    while edges:
        e=edges.pop();q=[e];loop={e}
        while q:
            for v in q.pop().verts:
                for nxt in v.link_edges:
                    if nxt in edges:edges.remove(nxt);loop.add(nxt);q.append(nxt)
        vs={v for e in loop for v in e.verts}
        if len(vs)==len(loop) and len(vs)>8:loops.append((loop,vs))
    if not loops:raise ValueError(('no wrist section',key,arm))
    boundary,vs=min(loops,key=lambda item:(sum((v.co for v in item[1]),Vector())/len(item[1])-plane).length)
    remove=set();q=deque(f for e in boundary for f in e.link_faces if (f.calc_center_median()-plane).dot(t)*sgn<0)
    while q:
        f=q.pop()
        if f in remove:continue
        remove.add(f)
        for e in f.edges:
            if e not in boundary:q.extend(other for other in e.link_faces if other not in remove)
    assert len(remove)<len(bm.faces)*.4,(key,arm,len(remove),len(bm.faces))
    bmesh.ops.delete(bm,geom=list(remove),context='FACES')
    # Cull nearly coincident vertices from a cut grazing a voxel corner.
    bmesh.ops.remove_doubles(bm,verts=list(vs),dist=.0015)
    # A swept wrist meets this plane obliquely. Cropping the open contour
    # to a fixed radius can discard part of its outer lip. Every other
    # sleeve has already been closed, so follow the actual open boundary.
    vs={v for v in bm.verts if v.is_boundary}
    assert vs and all(abs((v.co-plane).dot(t))<.001 for v in vs),(key,arm)
    controls=bm.verts.layers.float.get('ArmParameter') if arm else None
    tissue=bm.verts.layers.float.get('CrownTissue') if not arm else None
    last=vs
    for h in (.12*sgn,0):
        origin=p+t*h;ring=[]
        for k in range(SIDES):
            a=math.tau*k/SIDES
            v=bm.verts.new(origin+(n*math.cos(a)+b*math.sin(a))*(r+.085*h))
            if tissue:v[tissue]=1
            if controls:v[controls]=params
            ring.append(v)
        stitch(bm,last,ring,p,n,b,material);last=ring
    # Both closed fan caps recess into their own solids. Keep their normals
    # out of the smooth exterior, so no cap highlight travels round the wrist.
    center=bm.verts.new(p+t*.145*sgn)
    if tissue:center[tissue]=1
    if controls:center[controls]=params
    for k in range(SIDES):
        f=bm.faces.new((last[k],last[(k+1)%SIDES],center));f.material_index=material;f.smooth=False
        bm.edges.get((last[k],last[(k+1)%SIDES])).smooth=False

def close_junctions(core,arms):
    def load(ob):
        bm=bmesh.new();bm.from_mesh(ob.data);return bm
    def save(bm,ob):
        bmesh.ops.triangulate(bm,faces=[f for f in bm.faces if len(f.verts)>4])
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(ob.data);ob.data.update();bm.free()
    head=load(core)
    assert head.verts.layers.float.get('CrownTissue') is not None, 'Missing authored crown'
    skin=next(i for i,m in enumerate(core.data.materials) if 'Skin_Teal' in m.name)
    for arm in arms:
        key=arm['limb_id'];param=21/(len(arm['centerline'])-1)
        trim_and_loft(head,key,False,skin,None)
        bm=load(arm);trim_and_loft(bm,key,True,0,param);save(bm,arm)
    save(head,core)
