"""Reference-calibrated closed surfaces. Image X/Z with genuine authored depth in metres."""
import math
import random
import bpy
import bmesh
from mathutils import Vector
from mathutils.noise import noise_vector

S = .01
FRONT = Vector((0, -1, 0))

def P(x, v, depth=0):
    return Vector(((x - 768)*S, depth, (1024-v)*S))

def spline(values, subdivisions=9):
    """Catmull-Rom for anatomical centre lines and radii, with endpoint preservation."""
    out=[]
    for i in range(len(values)-1):
        a=values[max(0,i-1)]; b=values[i]; c=values[i+1]; d=values[min(len(values)-1,i+2)]
        for k in range(subdivisions):
            t=k/subdivisions
            out.append(.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t))
    out.append(values[-1])
    return out

class Mesh:
    def __init__(self):
        self.v=[];self.f=[];self.mi=[];self.uv=[];self.colors=[]

    def vert(self,p,uv=(0,0),color=(1,1,1,1)):
        self.v.append(tuple(p));self.uv.append(uv);self.colors.append(color)
        return len(self.v)-1

    def face(self,ids,mat=0):
        self.f.append(tuple(ids));self.mi.append(mat)

    def finish(self,name,collection,mats):
        me=bpy.data.meshes.new(name+'Mesh');me.from_pydata(self.v,[],self.f);me.update()
        for m in mats:me.materials.append(m)
        ob=bpy.data.objects.new(name,me);collection.objects.link(ob)
        uv=me.uv_layers.new(name='SurfaceUV')
        for p,mi in zip(me.polygons,self.mi):
            p.material_index=mi;p.use_smooth=True
            for li in p.loop_indices:uv.data[li].uv=self.uv[me.loops[li].vertex_index]
        # Correct closed-shell orientation per connected surface, before saving.
        bm=bmesh.new();bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
        return ob

    def tube(self,knots,sides=32,steps=10,belly=False,belly_angle=0,ripple=.012,flatten=1):
        """knots: (image x, image y, depth, radius in image px). Returns frames for suckers."""
        pts=spline([P(*k[:3]) for k in knots],steps)
        rr=spline([k[3]*S for k in knots],steps)
        rows=[];frames=[]
        for j,(p,r) in enumerate(zip(pts,rr)):
            r=max(.002,r)
            t=(pts[min(j+1,len(pts)-1)]-pts[max(0,j-1)]).normalized()
            n=FRONT-t*FRONT.dot(t)
            if n.length<.01:n=Vector((1,0,0))-t*t.x
            n.normalize();b=t.cross(n).normalized()
            a=belly_angle(j/(len(pts)-1)) if callable(belly_angle) else belly_angle
            n,b=n*math.cos(a)+b*math.sin(a),b*math.cos(a)-n*math.sin(a)
            frames.append((p,t,n,b,r))
            row=[]
            for i in range(sides):
                ang=math.tau*i/sides
                # Restrained longitudinal muscle ridges; no cylindrical faceting.
                wav=1+ripple*math.sin(ang*5+j*.13)+ripple*.35*math.sin(ang*11-j*.6)
                q=p+r*wav*(n*math.cos(ang)*flatten+b*math.sin(ang))
                row.append(self.vert(q,(i/sides,j/(len(pts)-1))))
            rows.append(row)
        for j,(a,b) in enumerate(zip(rows,rows[1:])):
            for i in range(sides):
                ang=math.tau*(i+.5)/sides
                mat=1 if belly and math.cos(ang)>.78 else 0
                self.face((a[i],b[i],b[(i+1)%sides],a[(i+1)%sides]),mat)
        for row,p,reverse in [(rows[0],pts[0],True),(rows[-1],pts[-1],False)]:
            vi=self.vert(p,(.5,.5))
            for i in range(sides):
                ids=(vi,row[i],row[(i+1)%sides])
                self.face(ids[::-1] if reverse else ids)
        return frames

    def cup(self,center,normal,tangent,r,aspect=.9,segments=32):
        """Solid sucker with a fleshy lip, recessed funnel and closed mounting foot."""
        n=Vector(normal).normalized();x=Vector(tangent).normalized();y=n.cross(x).normalized()
        rows=[]
        profile=[(.65,-.16),(.84,-.07),(.98,.08),(1.025,.20),(1,.28),(.90,.30),(.80,.25),(.66,.12),(.43,.05),(.18,.045)]
        for j,(rad,h) in enumerate(profile):
            row=[]
            for i in range(segments):
                a=math.tau*i/segments
                wav=1+.018*math.sin(a*9)
                p=center+r*(rad*wav*(x*math.cos(a)*aspect+y*math.sin(a))+n*h)
                row.append(self.vert(p,(.5+.45*rad*math.cos(a),.5+.45*rad*math.sin(a))))
            rows.append(row)
        for j,(a,b) in enumerate(zip(rows,rows[1:])):
            for i in range(segments):self.face((a[i],b[i],b[(i+1)%segments],a[(i+1)%segments]),2 if j<7 else 3)
        for row,p,rev in [(rows[0],center-n*r*.16,True),(rows[-1],center+n*r*.043,False)]:
            ix=self.vert(p,(.5,.5))
            for i in range(segments):
                f=(ix,row[i],row[(i+1)%segments]);self.face(f[::-1] if rev else f,3)

    def suckers(self,frames,spacing=1.75,rows=1,offset=.4,start=.08,end=.98):
        distance=0;last=frames[0][0];next_d=0
        for j,(p,t,n,b,r) in enumerate(frames):
            distance+=(p-last).length;last=p
            f=j/(len(frames)-1)
            if f<start or f>end or r<.025:continue
            if distance<next_d:continue
            for k in range(rows):
                shift=(k-(rows-1)/2)*.67+offset
                nn=(n*math.sqrt(max(.01,1-shift*shift))+b*shift).normalized()
                cr=r*(.41 if rows==2 else .49)
                self.cup(p+nn*r*1.025,nn,t,cr)
            next_d=distance+max(.05,r*spacing*.53)

    def ellipsoid(self,center,scale,nu=96,nv=64,deform=None,mat=0):
        c=Vector(center);top=self.vert(c+Vector((0,0,scale[2])),(.5,1));rings=[]
        for j in range(1,nv):
            ph=math.pi*j/nv;row=[]
            for i in range(nu):
                a=math.tau*i/nu
                q=Vector((scale[0]*math.sin(ph)*math.cos(a),scale[1]*math.sin(ph)*math.sin(a),scale[2]*math.cos(ph)))
                if deform:q=deform(q,ph,a)
                row.append(self.vert(c+q,(i/nu,1-j/nv)))
            rings.append(row)
        bot=self.vert(c-Vector((0,0,scale[2])),(.5,0))
        for i in range(nu):
            k=(i+1)%nu;self.face((top,rings[0][i],rings[0][k]),mat)
            for a,b in zip(rings,rings[1:]):self.face((a[i],b[i],b[k],a[k]),mat)
            self.face((bot,rings[-1][k],rings[-1][i]),mat)

    def patch(self,boundary,center,depth,bulge=.15,steps=6,rings=18,thickness=.06,mat=0):
        """Closed domed anatomical plate / fin, sharp authored outline, smooth surface."""
        # Catmull interpolation around the closed outline.
        v=[Vector((x,y)) for x,y in boundary];ext=v[-1:]+v+v[:2]
        edge=spline(ext,steps)[steps:-steps]
        if (edge[-1]-edge[0]).length<.001:edge=edge[:-1]
        c=Vector(center);nr=len(edge);top=[];bottom=[]
        for side,allrows in [(0,top),(1,bottom)]:
            center_index=self.vert(P(*c,depth-bulge if side==0 else depth+thickness),(.5,.5))
            for j in range(1,rings+1):
                f=j/rings;row=[]
                for i,e in enumerate(edge):
                    q=c.lerp(e,f)
                    y=depth-bulge*(1-f*f) if side==0 else depth+thickness*(1-f*f)
                    row.append(self.vert(P(q.x,q.y,y),(q.x/1536,1-q.y/1024)))
                allrows.append(row)
            for i in range(nr):
                self.face((center_index,allrows[0][i],allrows[0][(i+1)%nr]),mat)
            for a,b in zip(allrows,allrows[1:]):
                for i in range(nr):self.face((a[i],b[i],b[(i+1)%nr],a[(i+1)%nr]),mat)
        # Offset perimeter very slightly, so a closed lip has nonzero thickness.
        for vi in bottom[-1]:self.v[vi]=(self.v[vi][0],self.v[vi][1]+thickness*.18,self.v[vi][2])
        for i in range(nr):self.face((top[-1][i],bottom[-1][i],bottom[-1][(i+1)%nr],top[-1][(i+1)%nr]),mat)
