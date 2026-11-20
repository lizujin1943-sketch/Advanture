"""Small modelling operations for closed, UV-mapped render shells.

Artists work in (camera right, along the silhouette, up). Export maps these
into Blender scene space in one place. All arguments are game artwork units.
"""
from math import cos, pi, sin
from mathutils import Vector
import bpy
import bmesh
from atlas_layout import remap


def blender_point(p):
    return Vector((-p[0],-p[1],p[2]))


def oval(a,b,count,power=1.):
    def power_signed(t):
        return (1 if t>=0 else -1)*abs(t)**power
    return [(a*power_signed(cos(i*2*pi/count)),b*power_signed(sin(i*2*pi/count))) for i in range(count)]


def rounded(a,b,ratio,count):
    rx,ry=a*ratio,b*ratio
    points=[]
    for i,(x,y) in enumerate(((a-rx,b-ry),(-a+rx,b-ry),(-a+rx,-b+ry),(a-rx,-b+ry))):
        for j in range(count+1):
            ang=(i+j/count)*pi/2
            points.append((x+rx*cos(ang),y+ry*sin(ang)))
    return points


def round_outline(points,radius,steps):
    pts=[Vector(v) for v in points]
    out=[]
    for i,p in enumerate(pts):
        prev,nxt=pts[i-1]-p,pts[(i+1)%len(pts)]-p
        cut=min(radius,prev.length*.29,nxt.length*.29)
        a,b=p+prev.normalized()*cut,p+nxt.normalized()*cut
        for k in range(steps+1):
            t=k/steps
            out.append(a*(1-t)**2+p*(2*t*(1-t))+b*t*t)
    return out


def inset(points,offset):
    area=sum(p.x*points[(i+1)%len(points)].y-p.y*points[(i+1)%len(points)].x for i,p in enumerate(points))
    sign=1 if area>=0 else -1
    result=[]
    for i,p in enumerate(points):
        a=(p-points[i-1]).normalized()
        b=(points[(i+1)%len(points)]-p).normalized()
        na,nb=Vector((-a.y*sign,a.x*sign)),Vector((-b.y*sign,b.x*sign))
        bisector=(na+nb).normalized()
        result.append(p+bisector*offset/max(.3,abs(bisector.dot(na))))
    return result


class Part:
    def __init__(self,name,group):
        self.name,self.group=name,group
        self.verts,self.faces,self.uv,self.smooth=[],[],[],[]

    def rings(self,rings,region,uv_span=(0,1,0,1),smooth=True,cap_region=None,flip_band=False,closed=False):
        rows=[[Vector(p) for p in row] for row in rings]
        offset=len(self.verts)
        n=len(rows[0])
        if any(len(r)!=n for r in rows):
            raise ValueError('Ring mismatch in '+self.name)
        self.verts.extend(p for row in rows for p in row)
        along=[0.]
        path=rows+[rows[0]] if closed else rows
        for a,b in zip(path,path[1:]):
            along.append(along[-1]+sum((x-y).length for x,y in zip(a,b))/n)
        around=[0.]
        mid=rows[len(rows)//2]
        for j in range(n):
            around.append(around[-1]+max(1e-8,(mid[j]-mid[(j+1)%n]).length))
        u0,u1,v0,v1=uv_span
        def uv_at(i,j):
            u=along[i]/max(along[-1],1e-12)
            v=around[j]/around[-1]
            if flip_band:
                u,v=v,u
            return remap(region,u0+(u1-u0)*u,v0+(v1-v0)*v)
        for i in range(len(path)-1):
            following=(i+1)%len(rows)
            for j in range(n):
                self.faces.append((offset+i*n+j,offset+i*n+(j+1)%n,offset+following*n+(j+1)%n,offset+following*n+j))
                self.uv.append((uv_at(i,j),uv_at(i,j+1),uv_at(i+1,j+1),uv_at(i+1,j)))
                self.smooth.append(smooth)
        if closed:
            return self
        for i in (0,len(rows)-1):
            row=rows[i]
            # Triangulate the outline without introducing a fan outside concave plates.
            from mathutils.geometry import tessellate_polygon
            tris=tessellate_polygon([row])
            normal=Vector((0,0,0))
            for j in range(1,len(row)-1):
                normal+=(row[j]-row[0]).cross(row[j+1]-row[0])
            drop=max(range(3),key=lambda k:abs(normal[k]))
            axis=[k for k in range(3) if k!=drop]
            # Choose screen-aligned axes for stencilled caps on the exterior cheeks.
            if cap_region=='mark':
                axis=[1,2]
            ext=[(min(p[k] for p in row),max(p[k] for p in row)) for k in axis]
            def co(j):
                result=[]
                for k,(lo,hi) in zip(axis,ext):
                    result.append((row[j][k]-lo)/max(1e-12,hi-lo))
                if cap_region=='mark' and row[0].x<0:
                    result[0]=1-result[0]
                return remap(cap_region or region,*result)
            for tri in tris:
                inds=list(reversed(tri)) if i==0 else tri
                self.faces.append(tuple(offset+i*n+j for j in inds))
                self.uv.append(tuple(co(j) for j in inds))
                self.smooth.append(False)
        return self

    def object(self,collection,material,pivot=(0,0,0)):
        mesh=bpy.data.meshes.new(self.name)
        center=blender_point(pivot)
        mesh.from_pydata([blender_point(v)-center for v in self.verts],[],self.faces)
        mesh.materials.append(material)
        uv=mesh.uv_layers.new(name='Surface')
        for face,coords,smooth in zip(mesh.polygons,self.uv,self.smooth):
            face.use_smooth=smooth
            for index,coord in zip(face.loop_indices,coords):
                uv.data[index].uv=coord
        mesh.update()
        bm=bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm,faces=bm.faces[:])
        # The bevel strips and rounded profiles shade continuously. Separate the
        # broad flats at large creases so thin stamped shells keep defined edges.
        for edge in bm.edges:
            if edge.is_manifold and edge.calc_face_angle() > .91:
                edge.smooth=False
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        obj=bpy.data.objects.new(self.name,mesh)
        collection.objects.link(obj)
        obj.location=center
        obj['ArtGroup']=self.group
        return obj


class Workshop:
    def __init__(self,detailed=True):
        self.detailed=detailed
        self.parts=[]
        self.group='Frame'
        self.corners=4 if detailed else 2
        self.rounds=24 if detailed else 12

    def part(self,name):
        piece=Part(name,self.group)
        self.parts.append(piece)
        return piece

    def along(self,name,rows,region,shape=None,uv_span=(0,1,0,1)):
        # Each section has forward position, centre height, half-width and half-height.
        shape=shape or (lambda a,b:oval(a,b,self.rounds))
        rings=[[(x,f,h+z) for x,z in shape(a,b)] for f,h,a,b in rows]
        return self.part(name).rings(rings,region,uv_span)

    def side(self,name,points,x,half,region,bevel=.0012,rounding=.003,cap=None):
        out=round_outline(points,rounding,2 if self.detailed else 1)
        bevel=min(bevel,half*.7)
        rings=[]
        cuts=((-half,bevel),(-half+.65*bevel,.18*bevel),(-half+bevel,0),
              (half-bevel,0),(half-.65*bevel,.18*bevel),(half,bevel)) if self.detailed else (
              (-half,bevel),(-half+bevel,0),(half-bevel,0),(half,bevel))
        for loc,shrink in cuts:
            rings.append([(x+loc,p.x,p.y) for p in inset(out,shrink)])
        return self.part(name).rings(rings,region,cap_region=cap)

    def stroke(self,name,path,radii,region,segments=None,guide=(1,0,0),flatten=1,profile=None,uv_span=(0,1,0,1)):
        points=[Vector(x) for x in path]
        seg=segments or (12 if self.detailed else 8)
        axis=Vector(guide).normalized()
        rings=[]
        for i,(p,radius) in enumerate(zip(points,radii)):
            tangent=(points[min(len(points)-1,i+1)]-points[max(0,i-1)]).normalized()
            right=axis-axis.dot(tangent)*tangent
            if right.length<.01:
                right=tangent.cross(Vector((0,1,0)))
            right.normalize()
            normal=tangent.cross(right).normalized()
            prof=profile(radius,radius*flatten) if profile else oval(radius,radius*flatten,seg)
            rings.append([p+right*a+normal*b for a,b in prof])
        return self.part(name).rings(rings,region,uv_span)

    def pin(self,name,x,f,h,r,width,region='edge'):
        s=1 if x>0 else -1
        profile=[(0,.86),(.16,1),(.57,1),(.88,.82),(1,.55)]
        rings=[[(x+s*t*width,f+a,h+b) for a,b in oval(r*rr,r*rr,16 if self.detailed else 8)] for t,rr in profile]
        return self.part(name).rings(rings,region)

    def case(self,name,stations,region,ratio=.26,span=(0,1,0,1)):
        return self.along(name,stations,region,lambda a,b:rounded(a,b,ratio,self.corners),span)
