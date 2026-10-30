"""Shared mesh construction, fish coordinates and skin weights for reference fish."""
import math
import bpy
import bmesh
from mathutils import Vector


def v(point):
    """Fish space: X nose, Y width, Z up; Blender: -Y nose, Z up."""
    x, y, z = point
    return Vector((y, -x, z))


def swatch(index):
    return ((index % 16 + .5)/16, (index//16+.5)/8)


class Surface:
    def __init__(self):
        self.vertices=[]
        self.faces=[]
        self.uvs=[]
        self.weights=[]

    def vertex(self, point, weights):
        self.vertices.append(tuple(v(point)))
        self.weights.append(weights if isinstance(weights,dict) else {weights:1.0})
        return len(self.vertices)-1

    def face(self, indices, uv=None, color=None):
        self.faces.append(tuple(indices))
        self.uvs.append(uv if uv is not None else [swatch(color if color is not None else 0)]*len(indices))

    def ellipsoid(self, center, radii, bone, color, segments=12, rings=7):
        cx,cy,cz=center
        top=self.vertex((cx,cy,cz+radii[2]),bone)
        grid=[]
        for i in range(1,rings):
            phi=math.pi*i/rings
            row=[]
            for j in range(segments):
                theta=math.tau*j/segments
                row.append(self.vertex((cx+radii[0]*math.sin(phi)*math.cos(theta),
                    cy+radii[1]*math.sin(phi)*math.sin(theta),cz+radii[2]*math.cos(phi)),bone))
            grid.append(row)
        bottom=self.vertex((cx,cy,cz-radii[2]),bone)
        for j in range(segments):
            k=(j+1)%segments
            self.face((top,grid[0][j],grid[0][k]),color=color)
            self.face((bottom,grid[-1][k],grid[-1][j]),color=color)
        for i in range(len(grid)-1):
            for j in range(segments):
                k=(j+1)%segments
                self.face((grid[i][j],grid[i+1][j],grid[i+1][k],grid[i][k]),color=color)

    def tube(self, points, radius, bone, color, sides=6):
        rows=[]
        for i,point in enumerate(points):
            axis=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])
            axis.normalize()
            tangent=axis.cross(Vector((0,0,1)))
            if tangent.length<.01:
                tangent=axis.cross(Vector((0,1,0)))
            tangent.normalize()
            other=axis.cross(tangent).normalized()
            row=[]
            for j in range(sides):
                offset=radius*(tangent*math.cos(math.tau*j/sides)+other*math.sin(math.tau*j/sides))
                row.append(self.vertex(Vector(point)+offset,bone))
            rows.append(row)
        self.face(tuple(reversed(rows[0])),color=color)
        self.face(rows[-1],color=color)
        for a,b in zip(rows,rows[1:]):
            for j in range(sides):
                k=(j+1)%sides
                self.face((a[j],a[k],b[k],b[j]),color=color)

    def cone(self, base, tip, radius, bone, color, sides=6):
        axis=(Vector(tip)-Vector(base)).normalized()
        u=axis.cross(Vector((0,0,1)))
        if u.length<.01:
            u=axis.cross(Vector((0,1,0)))
        u.normalize()
        w=axis.cross(u).normalized()
        ring=[self.vertex(Vector(base)+radius*(math.cos(math.tau*j/sides)*u+
                     math.sin(math.tau*j/sides)*w),bone) for j in range(sides)]
        end=self.vertex(tip,bone)
        self.face(tuple(reversed(ring)),color=color)
        for j in range(sides):
            self.face((ring[j],ring[(j+1)%sides],end),color=color)

    def object(self, name, collection, material, rig):
        mesh=bpy.data.meshes.new(name+'_Geometry')
        mesh.from_pydata(self.vertices,[],self.faces)
        mesh.update()
        uv=mesh.uv_layers.new(name='PixelUV')
        for polygon,coords in zip(mesh.polygons,self.uvs):
            for loop,coord in zip(polygon.loop_indices,coords):
                uv.data[loop].uv=coord
            polygon.use_smooth=True
        bm=bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bmesh.ops.triangulate(bm,faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        obj=bpy.data.objects.new(name,mesh)
        collection.objects.link(obj)
        obj.data.materials.append(material)
        for name in sorted({k for weights in self.weights for k in weights}):
            group=obj.vertex_groups.new(name=name)
            for index,weights in enumerate(self.weights):
                if weights.get(name,0)>.000001:
                    group.add([index],weights[name],'REPLACE')
        obj.parent=rig
        modifier=obj.modifiers.new('Fish skin','ARMATURE')
        modifier.object=rig
        return obj


def radius_at(spec,x):
    profile=spec['profile']
    for a,b in zip(profile,profile[1:]):
        if x<=b[0]:
            t=max(0,min(1,(x-a[0])/(b[0]-a[0])))
            return tuple(a[i]*(1-t)+b[i]*t for i in (1,2,3))
    return tuple(profile[-1][i] for i in (1,2,3))


def body_weights(spec,x):
    if x>=.25:
        return {'Head':1.0}
    if x>.06:
        t=(x-.06)/.19
        return {'Head':t,'Body':1-t}
    centres=spec['_centres']
    if x>=centres[0][1]:
        return {centres[0][0]:1.0}
    for (name1,a),(name2,b) in zip(centres,centres[1:]):
        if x>=b:
            t=(a-x)/(a-b)
            return {name1:1-t,name2:t}
    return {centres[-1][0]:1.0}
