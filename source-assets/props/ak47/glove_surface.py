"""Join the sewn glove volume and fit raised protective panels to its surface."""
from math import cos, pi, sin
import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from atlas_layout import remap


def sew_glove(w,start,label):
    # Voxel union is restricted to the glove liner, leaving the sleeve, cuff and
    # protective pads independently editable. Work in art space in this scratch mesh.
    verts,faces=[],[]
    for part in w.parts[start:]:
        offset=len(verts)
        verts.extend(part.verts)
        faces.extend(tuple(i+offset for i in face) for face in part.faces)
    del w.parts[start:]
    source=bpy.data.meshes.new('Glove volume guides')
    source.from_pydata(verts,[],faces)
    bm=bmesh.new()
    bm.from_mesh(source)
    bmesh.ops.recalc_face_normals(bm,faces=bm.faces[:])
    bm.to_mesh(source)
    bm.free()
    obj=bpy.data.objects.new('Temporary glove union',source)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active=obj
    obj.select_set(True)
    source.remesh_voxel_size=.00185 if w.detailed else .00325
    bpy.ops.object.voxel_remesh()
    smooth=obj.modifiers.new('Continuous cloth transitions','SMOOTH')
    smooth.factor=.72
    smooth.iterations=3
    target=2300 if w.detailed else 750
    reduced=obj.modifiers.new('Game surface','DECIMATE')
    reduced.ratio=min(1,target/(2*len(obj.data.polygons)))
    bpy.context.view_layer.update()
    graph=bpy.context.evaluated_depsgraph_get()
    mesh=bpy.data.meshes.new_from_object(obj.evaluated_get(graph),depsgraph=graph)
    mesh.calc_loop_triangles()
    glove=w.part(label+' | continuous five-finger glove')
    glove.verts=[v.co.copy() for v in mesh.vertices]
    # A continuous oblique projection keeps the subtle nap coherent over rounded
    # knuckles. Its two axes favour the broad visible cheeks of the hand.
    a,b=Vector((.23,1,.11)),Vector((.33,-.08,1))
    coords=[(p.dot(a),p.dot(b)) for p in glove.verts]
    ext=[(min(p[k] for p in coords),max(p[k] for p in coords)) for k in range(2)]
    for tri in mesh.loop_triangles:
        ids=tri.vertices[:]
        ps=[glove.verts[i] for i in ids]
        centroid=(ps[0]+ps[1]+ps[2])/3
        # Dark palm leather wraps over the finger pads. The dorsal textile and thumb
        # share a continuous surface at every joint.
        region='rubber' if ((label=='Support' and centroid.x>.003)
                           or (label=='Firing' and centroid.x<.009)) else 'leather'
        glove.faces.append(ids)
        glove.uv.append([remap(region,*[(coords[i][k]-ext[k][0])/(ext[k][1]-ext[k][0]) for k in range(2)]) for i in ids])
        glove.smooth.append(True)
    tree=BVHTree.FromPolygons(glove.verts,glove.faces,all_triangles=True)
    bpy.data.meshes.remove(mesh)
    remeshed=obj.data
    bpy.data.objects.remove(obj,do_unlink=True)
    if remeshed.users==0:
        bpy.data.meshes.remove(remeshed)
    return tree


def panel(w,tree,name,centre,outward,length_dir,long,wide,depth,region='rubber'):
    centre=Vector(centre)
    normal=Vector(outward).normalized()
    along=Vector(length_dir)
    along=(along-normal*normal.dot(along)).normalized()
    cross=normal.cross(along).normalized()
    def on_surface(p):
        loc,n,_,_=tree.ray_cast(p+.072*normal,-normal,.144)
        if loc is None:
            loc,n,_,_=tree.find_nearest(p)
        return loc,n
    centre,_=on_surface(centre)
    samples=20 if w.detailed else 10
    rings=[]
    for scale,height in ((.90,-.00135),(1.,-.00031),(.96,.00064),(.80,.88*depth),(.32,depth)):
        ring=[]
        for i in range(samples):
            a=2*pi*i/samples
            u,v=cos(a),sin(a)
            u=(1 if u>0 else -1)*abs(u)**.76
            v=(1 if v>0 else -1)*abs(v)**.76
            sample=centre+scale*(along*(long*u)+cross*(wide*v))
            loc,n=on_surface(sample)
            ring.append(loc+(normal*.55+n*.45).normalized()*height)
        rings.append(ring)
    return w.part(name).rings(rings,region,(.18,.68,.19,.78))
