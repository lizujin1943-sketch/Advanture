"""Volumetric, reference-matched fish with native pixel UV projection.

Side silhouettes define elliptical cross sections, not image cards. Fins are
closed skinned solids; eyes, lips, jaws, teeth and spines have their own depth.
The ribbonfish uses a curved resting skeleton and swept sections in 3D.
"""
from pathlib import Path
import math
import shutil
import bpy
from mathutils import Vector
from mathutils.geometry import tessellate_polygon
from fish_geometry import Surface, body_weights

ROOT=Path(__file__).resolve().parents[2]
P=200


def coord(art,point,y=0):
    ox,oy=art['origin']
    return ((ox-point[0])/P,y,(oy-point[1])/P)


def uv(art,point,feature=True):
    return ((point[0]+(512 if feature else 0)+.5)/1024,1-(point[1]+.5)/512)


class ReferenceSurface(Surface):
    def __init__(self,art):
        super().__init__()
        self.art=art

    def face(self,indices,uv=None,color=None):
        if uv is None:
            uv=[(((color or 0)+.5)/32,1-448/512)]*len(indices)
        super().face(indices,uv=uv)

    def projected(self,indices,feature=True):
        ox,oy=self.art['origin']
        points=[(ox+self.vertices[i][1]*P,oy-self.vertices[i][2]*P) for i in indices]
        self.face(indices,uv=[uv(self.art,p,feature) for p in points])


def make_material(spec,directory):
    source=ROOT/'source-assets/creatures/fish/reference-atlases'/f"{spec['id']}_reference.png"
    if not source.exists():
        raise RuntimeError('Prepare reference atlases first: python3 scripts/blender/prepare_fish_reference.py')
    path=Path(directory)/f"{spec['id']}_albedo.png"
    shutil.copy2(source,path)
    image=bpy.data.images.load(str(path),check_existing=False)
    image.name=spec['id']+'_ReferencePixelAtlas'
    image.colorspace_settings.name='sRGB'
    material=bpy.data.materials.new(spec['id']+'_ReferencePixelMaterial')
    material.use_nodes=True
    bsdf=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bsdf.inputs['Roughness'].default_value=.95
    bsdf.inputs['Metallic'].default_value=0
    bsdf.inputs['Specular IOR Level'].default_value=.12
    tex=material.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image=image
    tex.interpolation='Closest'
    material.node_tree.nodes.active=tex
    material.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    # Preserve the palette of the pixel illustration under ordinary lit views.
    material.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Emission Color'])
    bsdf.inputs['Emission Strength'].default_value=.25
    return material,path


def lerp(a,b,t):
    return a*(1-t)+b*t


def section(art,px):
    data=art['body']
    for a,b in zip(data,data[1:]):
        if px<=b[0]:
            t=max(0,min(1,(px-a[0])/(b[0]-a[0])))
            return tuple(lerp(a[j],b[j],t) for j in (1,2,3))
    return tuple(data[-1][j] for j in (1,2,3))


def depth_at(art,px,py):
    top,bottom,width=section(art,px)
    r=max(1,(bottom-top)*.5)
    t=(py-(top+bottom)*.5)/r
    return width*math.sqrt(max(.05,1-t*t))/P


def ring_body(surface,spec,sections,weights=None,feature=False,segments=32,color=None):
    art=spec['_reference']
    rows=[]
    def skin_at(px,py):
        if callable(weights):
            return weights(px,py)
        return weights if weights is not None else body_weights(spec,coord(art,(px,py))[0])
    for px,top,bottom,width in sections:
        mid=(top+bottom)*.5
        radius=(bottom-top)*.5
        row=[]
        for j in range(segments):
            a=math.tau*j/segments
            py=mid-radius*math.sin(a)
            lateral=width*math.cos(a)/P
            if weights is None and not art.get('puffer'):
                # The eyelid/eyeball are sculpted into the head surface. This
                # retains one coherent face texture under every viewing angle.
                for cx,cy,rx,rz,depth in art['eyes']:
                    r=((px-cx)/rx)**2+((py-cy)/rz)**2
                    if r<1:
                        lateral+=math.copysign(depth/P*math.sqrt(1-r),lateral)
            row.append(surface.vertex(coord(art,(px,py),lateral),skin_at(px,py)))
        rows.append(row)
    for a,b in zip(rows,rows[1:]):
        for j in range(segments):
            k=(j+1)%segments
            indices=(a[j],b[j],b[k],a[k])
            if color is None:
                surface.projected(indices,feature)
            else:
                surface.face(indices,color=color)
    for row,data,reverse in [(rows[0],sections[0],False),(rows[-1],sections[-1],True)]:
        px,top,bottom,_=data
        point=coord(art,(px,(top+bottom)*.5))
        skin=skin_at(px,(top+bottom)*.5)
        pole=surface.vertex(point,skin)
        for j in range(segments):
            ids=(pole,row[j],row[(j+1)%segments])
            ids=tuple(reversed(ids)) if reverse else ids
            if color is None:
                surface.projected(ids,feature)
            else:
                surface.face(ids,color=color)


def outline_sections(points,width):
    low,high=min(p[0] for p in points),max(p[0] for p in points)
    xs=sorted(set([low+.15,high-.15]+[p[0] for p in points if low<p[0]<high]+
                  [low+(high-low)*i/max(2,math.ceil((high-low)/4))
                   for i in range(1,max(2,math.ceil((high-low)/4)))]))
    rows=[]
    for x in xs:
        ys=[]
        for a,b in zip(points,points[1:]+points[:1]):
            if (a[0]<=x<b[0]) or (b[0]<=x<a[0]):
                ys.append(lerp(a[1],b[1],(x-a[0])/(b[0]-a[0])))
        if len(ys)<2:
            continue
        t=(x-low)/(high-low)
        rows.append((x,min(ys),max(ys),max(1,width*(.35+.65*math.sin(math.pi*t)**.35))))
    return rows


def solid_polygon(surface,spec,points,bone,thickness=2,center_y=0,color=None):
    """A closed, subtly folded thin solid with the traced silhouette."""
    art=spec['_reference']
    front=[]
    back=[]
    for point in points:
        skin=bone(coord(art,point)) if callable(bone) else bone
        y=center_y(point) if callable(center_y) else center_y
        front.append(surface.vertex(coord(art,point,y+thickness/P*.5),skin))
        back.append(surface.vertex(coord(art,point,y-thickness/P*.5),skin))
    ps=[Vector((p[0],p[1],0)) for p in points]
    lookup={tuple(p):i for i,p in enumerate(ps)}
    for tri in tessellate_polygon([ps]):
        ids=[p if isinstance(p,int) else lookup[tuple(p)] for p in tri]
        if color is None:
            surface.projected([front[i] for i in ids])
            surface.projected([back[i] for i in reversed(ids)])
        else:
            surface.face([front[i] for i in ids],color=color)
            surface.face([back[i] for i in reversed(ids)],color=color)
    for i in range(len(points)):
        j=(i+1)%len(points)
        if color is None:
            surface.projected((front[i],back[i],back[j],front[j]))
        else:
            surface.face((front[i],back[i],back[j],front[j]),color=color)


def add_puffer_face(surface):
    # Both eyes sit on the front hemisphere, so this fish has two eyes in 3D,
    # not a duplicated two-eye picture on each side of its spherical body.
    for sign in (-1,1):
        center=(.47,sign*.30,.22)
        surface.ellipsoid(center,(.18,.145,.23),'Head',2,20,12)
        surface.ellipsoid((.505,sign*.31,.23),(.175,.148,.218),'Head',7,20,12)
        # Orient the dark pupils toward the nose and slightly outward.
        surface.ellipsoid((.625,sign*.35,.25),(.067,.075,.113),'Head',8,16,10)
        surface.ellipsoid((.667,sign*.354,.305),(.023,.027,.033),'Head',9,10,6)
    center=(.664,0,-.025)
    surface.ellipsoid(center,(.034,.115,.150),'Head',11,20,12)
    surface.ellipsoid((.694,0,-.022),(.026,.076,.111),'Head',8,20,12)
    for lower in (False,True):
        points=[]
        for i in range(13):
            a=math.pi*i/12+(math.pi if lower else 0)
            points.append((.70,.098*math.cos(a),-.02+.131*math.sin(a)))
        surface.tube(points,.027,'Jaw' if lower else 'Head',10,8)
    # Staggered short spines along the whole sphere, not four comb-like rows.
    for n in range(42):
        z=1-2*(n+.5)/42
        a=n*math.pi*(3-math.sqrt(5))
        direction=Vector((math.sqrt(1-z*z)*math.cos(a),math.sqrt(1-z*z)*math.sin(a),z))
        if direction.x>.46 and direction.z>-.35:
            continue
        center=Vector((-.005,0,-.01))
        base=center+Vector((direction.x*.61,direction.y*.49,direction.z*.54))
        tip=base+direction*.077
        surface.cone(base,tip,.035,'Body',4,6)


def curve_samples(art,count=84):
    """Centripetal-looking Catmull interpolation of the reference's resting S."""
    data=art['curve']
    result=[]
    for j in range(count):
        t=j/(count-1)*(len(data)-1)
        i=min(len(data)-2,int(t))
        t-=i
        a,b,c,d=data[max(0,i-1)],data[i],data[i+1],data[min(len(data)-1,i+2)]
        result.append(tuple(.5*((2*b[k])+(-a[k]+c[k])*t+
            (2*a[k]-5*b[k]+4*c[k]-d[k])*t*t+
            (-a[k]+3*b[k]-3*c[k]+d[k])*t*t*t) for k in range(4)))
    return result


def ribbon_skin(spec,t):
    names=['Head','Body']+[f'Spine_{i:02d}' for i in range(1,8)]+['Tail']
    p=min(len(names)-1.000001,max(0,t*(len(names)-1)))
    index=int(p)
    blend=p-index
    return {names[index]:1-blend,names[index+1]:blend}


def add_ribbon(surface,spec):
    art=spec['_reference']
    samples=curve_samples(art)
    segments=24
    rows=[]
    for i,(x,y,r,width) in enumerate(samples):
        a=samples[max(0,i-1)]
        b=samples[min(len(samples)-1,i+1)]
        tangent=Vector((b[0]-a[0],b[1]-a[1])).normalized()
        normal=Vector((tangent.y,-tangent.x))
        rows.append([surface.vertex(coord(art,(x+normal.x*r*math.sin(math.tau*j/segments),
                                    y+normal.y*r*math.sin(math.tau*j/segments)),
                                    width/P*math.cos(math.tau*j/segments)),
                          ribbon_skin(spec,i/(len(samples)-1))) for j in range(segments)])
    for a,b in zip(rows,rows[1:]):
        for j in range(segments):
            k=(j+1)%segments
            surface.projected((a[j],b[j],b[k],a[k]),False)
    for row,sample,t in [(rows[0],samples[0],0),(rows[-1],samples[-1],1)]:
        center=surface.vertex(coord(art,sample[:2]),ribbon_skin(spec,t))
        for j in range(segments):
            surface.projected((center,row[(j+1)%segments],row[j]),False)
    # Dorsal membrane follows the S curve and shares the adjacent body weights.
    for i in range(3,len(samples)-1):
        points=[]
        for j,offset in [(i,0),(i+1,0),(i+1,7),(i,7)]:
            x,y,r,_=samples[j]
            a=samples[max(0,j-1)]
            b=samples[min(len(samples)-1,j+1)]
            d=Vector((b[0]-a[0],b[1]-a[1])).normalized()
            points.append((x+d.y*(r+offset),y-d.x*(r+offset)))
        weights=ribbon_skin(spec,(i+.5)/(len(samples)-1))
        solid_polygon(surface,spec,points,lambda p:weights,1.3)


def create_mesh(spec,collection,material,rig):
    art=spec['_reference']
    surface=ReferenceSurface(art)
    if art.get('ribbon'):
        add_ribbon(surface,spec)
        # Separate broad head and cheeks, seated into the swept ribbon volume.
        refined=[]
        for a,b in zip(art['body'],art['body'][1:]):
            steps=max(1,math.ceil((b[0]-a[0])/5))
            refined.extend(tuple(lerp(a[k],b[k],i/steps) for k in range(4)) for i in range(steps))
        refined.append(art['body'][-1])
        ring_body(surface,spec,refined,'Head',segments=28)
    else:
        refined=[]
        for a,b in zip(art['body'],art['body'][1:]):
            steps=max(1,math.ceil((b[0]-a[0])/5))
            refined.extend(tuple(lerp(a[k],b[k],i/steps) for k in range(4)) for i in range(steps))
        refined.append(art['body'][-1])
        ring_body(surface,spec,refined,segments=32)
    for entry in art['parts']:
        if entry['paired']:
            for sign,side in [(1,'L'),(-1,'R')]:
                root=entry['points'][0]
                base=depth_at(art,*root)
                def fin_depth(point,base=base,root=root,sign=sign):
                    return sign*(base+.012+max(0,point[0]-root[0])*.14/P+
                                 max(0,point[1]-root[1])*.07/P)
                solid_polygon(surface,spec,entry['points'],'Pectoral_'+side,
                              entry['width'],fin_depth)
        else:
            solid_polygon(surface,spec,entry['points'],entry['bone'],entry['width'])
    # Eyes are integrated in the anatomical head sections; pufferfish has a
    # dedicated frontal eye construction below.
    for i,points in enumerate(art['lips']):
        # Cross sections form a broad mouth spanning the front of the face.
        width=max(15,min(40,section(art,sum(p[0] for p in points)/len(points))[2]*.92))
        ring_body(surface,spec,outline_sections(points,width),'Jaw' if i else 'Head',True,20)
    if art.get('teeth'):
        # The dark lining is recessed behind the teeth. Its upper edge follows
        # the skull and its lower edge follows the mandible; a rigid, wide
        # solid attached only to Head becomes a projecting black bill in Bite.
        inner=[(max(37,px),py) for px,py in art['mouth_outline']]
        def oral_skin(px,py):
            t=max(0,min(1,(py-155)/52))
            return {'Head':1-t,'Jaw':t}
        ring_body(surface,spec,outline_sections(inner,22),
                  oral_skin,segments=24,color=6)
        ring_body(surface,spec,outline_sections(art['jaw_outline'],51),'Jaw',True,28)
        for points in art['tooth_shapes']:
            for sign in (-1,1):
                solid_polygon(surface,spec,points,'Jaw',7,sign*.205,color=7)
        # Teeth turn around the front of the upper jaw, visible in front view.
        for i in range(3):
            y=(i-1)*.080
            surface.cone((.74,y,-.20),(.76,y,-.115),.028,'Jaw',7,5)
    if art.get('puffer'):
        add_puffer_face(surface)
    obj=surface.object(spec['id']+'_Mesh',collection,material,rig)
    obj['reference_method']='Traced volumetric anatomy; native concept UV; mirrored complete 3D mesh'
    return obj
