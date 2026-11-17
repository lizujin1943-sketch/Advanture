"""Relieve almost coplanar contacts left by reduction without opening the mesh.

Cup feet and adjacent wing panels can collapse onto another surface. Move the
smaller connected shell inward; for a folded single shell use a local falloff.
This is a geometric correction before UV baking, not a QA exception.
"""
from collections import defaultdict
from mathutils import Vector
from mathutils.bvhtree import BVHTree

def contacts(me, eps=.0002):
    me.calc_loop_triangles()
    verts=[v.co.copy() for v in me.vertices]
    tris=[t.vertices[:] for t in me.loop_triangles]
    tree=BVHTree.FromPolygons(verts,tris);found={}
    for i,t in enumerate(tris):
        a,b,c=[verts[x] for x in t];n=(b-a).cross(c-a).normalized();center=(a+b+c)/3
        for p in (center,center.lerp(a,.6),center.lerp(b,.6),center.lerp(c,.6)):
            for _,_,j,_ in tree.find_nearest_range(p,eps):
                if j is None or j==i or set(t)&set(tris[j]):continue
                aa,bb,cc=[verts[x] for x in tris[j]];nn=(bb-aa).cross(cc-aa).normalized()
                if n.dot(nn)<.999:continue
                q=p-nn*nn.dot(p-aa)
                if all((v-u).cross(q-u).dot(nn)>=-1e-9 for u,v in ((aa,bb),(bb,cc),(cc,aa))):
                    pair=tuple(sorted((i,j)))
                    found.setdefault(pair,(t,tris[j],n))
    return list(found.values())

def relieve(ob):
    me=ob.data;parent=list(range(len(me.vertices)));neighbors=defaultdict(set)
    def root(i):
        while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
        return i
    for e in me.edges:
        a,b=e.vertices;parent[root(a)]=root(b);neighbors[a].add(b);neighbors[b].add(a)
    groups=defaultdict(list)
    for i in range(len(parent)):groups[root(i)].append(i)
    original=[v.co.copy() for v in me.vertices];counts=[]
    for iteration in range(6):
        pairs=contacts(me);counts.append(len(pairs))
        if not pairs:break
        offsets={}
        for a,b,n in pairs:
            ga,gb=root(a[0]),root(b[0])
            if ga!=gb and not ob.name.startswith('WC_Wing_'):
                ids=groups[ga] if len(groups[ga])<len(groups[gb]) else groups[gb]
                for i in ids:offsets[i]=-n*.003
            else:
                # One ring support keeps a soft, watertight relief at a folded seam.
                for i in b:
                    for j in neighbors[i]:offsets.setdefault(j,-n*.0015)
                    offsets[i]=-n*.003
        for i,offset in offsets.items():me.vertices[i].co+=offset
        me.update()
    assert not contacts(me),('Unresolved surface contacts',ob.name,counts)
    deviation=max((v.co-original[i]).length for i,v in enumerate(me.vertices))
    assert deviation<.025,(ob.name,deviation)
    return {'object':ob.name,'contactCounts':counts,'maxReliefMeters':deviation}
