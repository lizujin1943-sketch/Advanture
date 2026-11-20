"""Editable metric parts. Repeated rectilinear parts within a building share meshes."""
import math
import bmesh
import bpy
from mathutils import Vector


class Fabricator:
    def __init__(self,collection,palette,prefix):
        self.group=collection
        self.materials=palette
        self.prefix=prefix
        self.cubes={}

    def object(self,name,data,location,bevel):
        obj=bpy.data.objects.new(self.prefix+' | '+name,data)
        self.group.objects.link(obj)
        obj.location=location
        obj['art_component']=self.prefix
        if bevel:
            mod=obj.modifiers.new('Manufactured edge','BEVEL')
            mod.width=bevel
            mod.segments=2
            mod.harden_normals=True
        return obj

    def mesh(self,name,vertices,faces,location,material,bevel=0):
        data=bpy.data.meshes.new(self.prefix+' '+name)
        data.from_pydata(vertices,[],faces)
        data.update()
        bm=bmesh.new()
        bm.from_mesh(data)
        bmesh.ops.recalc_face_normals(bm,faces=bm.faces[:])
        bm.to_mesh(data)
        bm.free()
        data.materials.append(self.materials[material])
        return self.object(name,data,location,bevel)

    def box(self,name,centre,size,material,bevel=.012,rotation=0):
        bevel=min(bevel,min(size)*.22)
        key=tuple(round(a,5) for a in size)+(material,)
        data=self.cubes.get(key)
        if data:
            obj=self.object(name,data,centre,bevel)
        else:
            x,y,z=(v/2 for v in size)
            verts=[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),
                   (-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
            faces=[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
            obj=self.mesh(name,verts,faces,centre,material,bevel)
            self.cubes[key]=obj.data
        obj.rotation_euler.z=rotation
        return obj

    def beam(self,name,a,b,width,depth,material,bevel=.01):
        a,b=Vector(a),Vector(b)
        obj=self.box(name,(a+b)*.5,(width,depth,(b-a).length),material,bevel)
        obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
        return obj

    def tube(self,name,a,b,radius,material,n=12):
        a,b=Vector(a),Vector(b)
        h=(b-a).length*.5
        verts=[(radius*math.cos(i*2*math.pi/n),radius*math.sin(i*2*math.pi/n),z)
               for z in (-h,h) for i in range(n)]
        faces=[tuple(reversed(range(n))),tuple(range(n,n*2))]
        faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
        obj=self.mesh(name,verts,faces,(a+b)*.5,material)
        obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
        for p in obj.data.polygons:
            p.use_smooth=len(p.vertices)==4 and n>6
        return obj

    def prism(self,name,outline,base,top,origin,material,bevel=.015):
        n=len(outline)
        verts=[(x,y,z) for z in (base,top) for x,y in outline]
        faces=[tuple(reversed(range(n))),tuple(range(n,n*2))]
        faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
        return self.mesh(name,verts,faces,origin,material,bevel)

    def ring(self,name,centre,inner,outer,z0,z1,material,start=0,end=2*math.pi,segments=72,bevel=.009):
        closed=abs(end-start-2*math.pi)<1e-6
        count=segments if closed else segments+1
        verts=[]
        for i in range(count):
            a=start+(end-start)*i/segments
            for r,z in ((inner,z0),(outer,z0),(outer,z1),(inner,z1)):
                verts.append((r*math.cos(a),r*math.sin(a),z))
        faces=[]
        for i in range(segments):
            k,l=4*i,4*((i+1)%count)
            faces.extend((k+j,k+(j+1)%4,l+(j+1)%4,l+j) for j in range(4))
        if not closed:
            faces.extend([(3,2,1,0),tuple(range(4*(count-1),4*count))])
        ob=self.mesh(name,verts,faces,centre,material,bevel)
        for p in ob.data.polygons:
            if abs(p.normal.z)<.15 and p.index<4*segments:
                p.use_smooth=True
        ob.data.set_sharp_from_angle(angle=math.radians(30))
        return ob
